using System.Globalization;
using ProConta.Activos.Gestion;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Activos.Tests;

/// <summary>
/// Pantalla de activos contra una copia de pruebas con datos sintéticos (activos 2026-11-17 18:50): alta de un activo propio (ficha, inmueble, plan,
/// centro nuevo y subcuentas) y de uno ajeno (sin centro, plan ni subcuentas), guardar la ficha con control de versión, cambio de tipo con la regla de
/// bloqueo, apertura por centro de coste (uno, varios, ninguno) y cuenta de resultados con su amortización prevista. Ninguna operación graba asientos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ActivosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private ServicioActivos Servicio => new(Bd, Cuentas);
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)), CultureInfo.InvariantCulture);

    private int _vivienda, _local, _inmuebles, _centroA, _centroComun;

    /// <summary>
    /// Sociedad T01 con 2026; plantilla de vivienda con valor (2210), amortización acumulada (2820), gasto por amortización (6820) y seguro (6250); la de
    /// local, solo con su valor (2211). Centros: INMUEBLES (padre), 300 Piso A y 390 Gastos comunes.
    /// </summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("100", "Capital"), ("572", "Bancos"), ("2210", "Construcciones"), ("2211", "Construcciones de locales"),
                     ("2820", "Amortización acumulada"), ("6820", "Amortización"), ("6250", "Seguros"), ("705", "Prestaciones de servicios") })
            Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco sintético"), ("7050001", "Alquiler sintético") })
            Assert.True((await Cuentas.AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);
        await Bd.EjecutarAsync(async s =>
        {
            int Id(object? v) => Convert.ToInt32(v, CultureInfo.InvariantCulture);
            _vivienda = Id(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'VIVIENDA'"));
            _local = Id(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'LOCAL'"));
            async Task<int> Centro(string codigo, string nombre, int? padre, bool imputable, int nivel)
            {
                await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, IdCentroCostePadre, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", padre, codigo, nombre, imputable, true, (short)1, (byte)nivel, padre is null ? codigo : "3/" + codigo, padre is null ? "0001" : "0001/" + codigo);
                return await s.UltimoIdAsync();
            }
            _inmuebles = await Centro("3", "INMUEBLES", null, false, 1);
            _centroA = await Centro("300", "Piso A", _inmuebles, true, 2);
            _centroComun = await Centro("390", "Gastos comunes", _inmuebles, true, 2);
            async Task Linea(int tipo, string nombre, string rol, string prefijo, short orden)
            {
                await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, ?)", nombre, nombre, rol, true);
                var tipoSub = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo, Prefijo, Orden, EstaActiva) VALUES (?, ?, ?, ?, ?)", tipo, tipoSub, prefijo, orden, true);
            }
            await Linea(_vivienda, "Valor [TOKEN]", RolesSubcuentaActivo.Activo, "2210", 1);
            await Linea(_vivienda, "Amort. acumulada [TOKEN]", RolesSubcuentaActivo.AmortizacionAcumulada, "2820", 2);
            await Linea(_vivienda, "Amortización [TOKEN]", RolesSubcuentaActivo.GastoAmortizacion, "6820", 3);
            await Linea(_vivienda, "Seguro [TOKEN]", RolesSubcuentaActivo.Libre, "6250", 4);
            await Linea(_local, "Valor local [TOKEN]", RolesSubcuentaActivo.Activo, "2211", 1);
            return 0;
        });
    }

    private DatosActivo Piso(string codigo = "PisoRoble", string nombre = "Piso Calle del Roble 14") => new()
    {
        Codigo = codigo, Nombre = nombre, NombreCorto = "Roble 14", IdTipoActivo = _vivienda, CodigoTipo = TiposActivoPgc.Vivienda,
        FechaCompra = new DateTime(2026, 7, 1), Coste = 200_000m,
        Inmueble = new DatosInmueble("1234567DF1234A0001AA", 60_000m, 40_000m, 0.4m, 900m, new DateTime(2026, 8, 1), 50m),
        SeAmortiza = true, InicioAmortizacion = new DateTime(2026, 7, 1), BaseAmortizable = 80_000m, PorcentajeAnual = 0.03m,
    };

    private async Task<int> AltaAsync(DatosActivo d, int? padre = null)
    {
        var r = await Servicio.AltaAsync(_ctx, new AltaActivo(d, padre));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    [Fact]
    public async Task Alta_de_un_activo_propio_crea_la_ficha_el_inmueble_el_plan_el_centro_nuevo_y_sus_subcuentas()
    {
        await PrepararAsync();
        var id = await AltaAsync(Piso(), padre: _inmuebles);

        var (codigoCentro, nombreCentro, padre) = await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT c.Codigo, c.Nombre, c.IdCentroCostePadre FROM Activos AS a INNER JOIN CentrosCoste AS c ON a.IdCentroCoste = c.IdCentroCoste WHERE a.IdActivo = ?",
            f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), f.EnteroNulo("IdCentroCostePadre")), id));
        Assert.Equal(("PisoRoble", "Roble 14", (int?)_inmuebles), (codigoCentro, nombreCentro, padre));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ActivosInmuebles WHERE IdActivo = ? AND PorcentajeConstruccion = 0.4 AND ValorCatastralTotal = 100000", id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM PlanesAmortizacionActivo WHERE IdActivo = ? AND BaseAmortizable = 80000 AND PorcentajeAnual = 0.03 AND EstaActivo = True AND VidaUtilMeses = 400", id));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        // Las subcuentas se proponen y se generan después, con el centro nuevo como predeterminado.
        var propuesta = await Servicio.ProponerSubcuentasAsync(_ctx, [id]);
        Assert.Equal(["Valor Piso Calle del Roble 14", "Amort. acumulada Piso Calle del Roble 14", "Amortización Piso Calle del Roble 14", "Seguro Piso Calle del Roble 14"],
            propuesta.Select(o => o.NombreNuevo));
        var g = await Servicio.GenerarSubcuentasAsync(_ctx, [id]);
        Assert.True(g.EsCorrecto);
        Assert.Equal(4, g.Valor.Creadas);
        Assert.Empty(g.Valor.Errores);
        var valor = (await Cuentas.ResolverAsync(_ctx, "2210001")).Valor;
        Assert.Equal(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT IdCentroCoste FROM Activos WHERE IdActivo = ?", id)), valor.IdCentroCostePredeterminado);

        var ficha = (await Servicio.FichaAsync(_ctx, id))!;
        Assert.Equal(("PisoRoble", 0.4m, 80_000m, 0.03m), (ficha.Datos.Codigo, ficha.Datos.Inmueble!.PorcentajeConstruccion, ficha.Datos.BaseAmortizable, ficha.Datos.PorcentajeAnual));
        Assert.Null(ficha.MotivoBloqueoTipo);
        Assert.Equal(4, ficha.Subcuentas.Count);
        var subcuentas = await Servicio.SubcuentasAsync(_ctx, id);
        Assert.Empty(subcuentas.QueFaltan);
        Assert.Contains(subcuentas.Subcuentas, s => s.Codigo == "2820001" && s.Papel == "Amortización acumulada");
        var listado = Assert.Single(await Servicio.ListarAsync(_ctx));
        Assert.Equal(("En uso", 80_000m, 0), (listado.Estado, listado.Amortizable, listado.SubcuentasQueFaltan));

        // Un código repetido no se da de alta.
        var repetido = await Servicio.AltaAsync(_ctx, new AltaActivo(Piso(), _inmuebles));
        Assert.False(repetido.EsCorrecto);
        Assert.Contains("Ya hay otro activo", repetido.Error!.Mensaje);
    }

    [Fact]
    public async Task Alta_de_un_activo_ajeno_no_crea_centro_plan_ni_subcuentas()
    {
        await PrepararAsync();
        var centros = await ContarAsync("SELECT COUNT(*) FROM CentrosCoste");
        var id = await AltaAsync(Piso("PisoParticular", "Piso particular") with { EsAjeno = true, FechaCompra = null }, padre: _inmuebles);
        Assert.Equal(centros, await ContarAsync("SELECT COUNT(*) FROM CentrosCoste"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Activos WHERE IdActivo = ? AND EsAjeno = True AND IdCentroCoste IS NULL AND EsAmortizable = False", id));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM PlanesAmortizacionActivo WHERE IdActivo = ?", id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ActivosInmuebles WHERE IdActivo = ? AND CosteCompra IS NULL", id));
        Assert.Empty(await Servicio.ProponerSubcuentasAsync(_ctx, [id]));
        Assert.Equal(0, (await Servicio.GenerarSubcuentasAsync(_ctx, [id])).Valor.Creadas);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM SubcuentasActivo"));
        var fila = Assert.Single(await Servicio.ListarAsync(_ctx));
        Assert.Equal(("Ajeno", 0m, 0m), (fila.Estado, fila.Ingresos, fila.Gastos));
    }

    [Fact]
    public async Task Guardar_la_ficha_actualiza_activo_inmueble_y_plan_con_control_de_version()
    {
        await PrepararAsync();
        var id = await AltaAsync(Piso() with { IdCentroCoste = _centroA });
        var ficha = (await Servicio.FichaAsync(_ctx, id))!;
        var cambiada = ficha.Datos with
        {
            Nombre = "Piso Roble 14, 2.º 1.ª", Coste = 210_000m, BaseAmortizable = 84_000m, PorcentajeAnual = 0.02m, ValorResidual = 1_000m,
            Inmueble = ficha.Datos.Inmueble! with { PorcentajeConstruccion = 0.45m, AlquilerMensual = 950m },
        };
        var r = await Servicio.GuardarAsync(_ctx, cambiada, ficha.Version);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Empty(r.Valor);
        var leida = (await Servicio.FichaAsync(_ctx, id))!;
        Assert.Equal(ficha.Version + 1, leida.Version);
        Assert.Equal(("Piso Roble 14, 2.º 1.ª", 210_000m, 84_000m, 0.02m, 1_000m), (leida.Datos.Nombre, leida.Datos.Coste, leida.Datos.BaseAmortizable, leida.Datos.PorcentajeAnual, leida.Datos.ValorResidual));
        Assert.Equal((0.45m, 950m), (leida.Datos.Inmueble!.PorcentajeConstruccion, leida.Datos.Inmueble.AlquilerMensual));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM PlanesAmortizacionActivo WHERE IdActivo = ?", id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ActivosInmuebles WHERE IdActivo = ? AND CosteCompra = 210000", id));

        // Con la versión vieja no se guarda: alguien lo cambió mientras tanto.
        var vieja = await Servicio.GuardarAsync(_ctx, cambiada with { Nombre = "Otro nombre" }, ficha.Version);
        Assert.Equal("Activos.Cambiado", vieja.Error!.Codigo);
        // Sin amortización, el plan se desactiva; con ella, se reactiva el mismo.
        Assert.True((await Servicio.GuardarAsync(_ctx, leida.Datos with { SeAmortiza = false }, leida.Version)).EsCorrecto);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM PlanesAmortizacionActivo WHERE IdActivo = ? AND EstaActivo = False", id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Activos WHERE IdActivo = ? AND EsAmortizable = False", id));
        // Un dato mal escrito no se guarda y dice por qué.
        var mal = await Servicio.GuardarAsync(_ctx, leida.Datos with { Codigo = "" }, leida.Version + 1);
        Assert.Contains("Escribe un código", mal.Error!.Mensaje);
    }

    [Fact]
    public async Task Cambiar_el_tipo_sin_apuntes_cambia_sus_subcuentas_y_con_apuntes_recientes_se_bloquea()
    {
        await PrepararAsync();
        var id = await AltaAsync(Piso() with { IdCentroCoste = _centroA });
        Assert.Equal(4, (await Servicio.GenerarSubcuentasAsync(_ctx, [id])).Valor.Creadas);

        // Sin apuntes: se borran las cuatro de vivienda y se genera la de local.
        var ficha = (await Servicio.FichaAsync(_ctx, id))!;
        var aLocal = ficha.Datos with { IdTipoActivo = _local, CodigoTipo = TiposActivoPgc.Local };
        var cambio = (await Servicio.ProponerCambioTipoAsync(_ctx, aLocal)).Valor!;
        Assert.Equal((4, 0, true), (cambio.Borrar.Count, cambio.Desvincular.Count, cambio.GenerarNuevas));
        var r = await Servicio.GuardarAsync(_ctx, aLocal, ficha.Version);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Empty(r.Valor);
        Assert.False((await Cuentas.ResolverAsync(_ctx, "2210001")).EsCorrecto);
        Assert.True((await Cuentas.ResolverAsync(_ctx, "2211001")).EsCorrecto);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM SubcuentasActivo WHERE IdActivo = ?", id));

        // Con un apunte en el ejercicio de trabajo, el tipo y la marca de ajeno quedan bloqueados.
        var asiento = new Asiento(_ctx, new DateTime(2026, 9, 1), "Compra sintética");
        asiento.Apuntes.AddRange([Apunte.AlDebe("2211001", 100m), Apunte.AlHaber("5720001", 100m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([asiento], _actor, "Datos sintéticos")).EsCorrecto);
        ficha = (await Servicio.FichaAsync(_ctx, id))!;
        Assert.NotNull(ficha.MotivoBloqueoTipo);
        Assert.Contains("2211001 (1 apunte)", ficha.MotivoBloqueoTipo);
        var bloqueado = await Servicio.GuardarAsync(_ctx, ficha.Datos with { EsAjeno = true }, ficha.Version);
        Assert.Equal("Activos.TipoBloqueado", bloqueado.Error!.Codigo);
        // El resto de datos sí se guarda.
        Assert.True((await Servicio.GuardarAsync(_ctx, ficha.Datos with { Notas = "Revisado" }, ficha.Version)).EsCorrecto);
    }

    [Fact]
    public async Task La_apertura_por_centro_encuentra_uno_varios_o_ninguno()
    {
        await PrepararAsync();
        var uno = await AltaAsync(Piso("PisoA", "Piso A") with { IdCentroCoste = _centroA });
        await AltaAsync(Piso("PisoB", "Piso B") with { IdCentroCoste = _centroComun });
        await AltaAsync(Piso("PisoC", "Piso C") with { IdCentroCoste = _centroComun });

        var a = await Servicio.ActivosDeCentroAsync(_ctx, "300 Piso A");
        Assert.Equal(uno, Assert.Single(a.Activos).IdActivo);
        var varios = await Servicio.ActivosDeCentroAsync(_ctx, "390 Gastos comunes");
        Assert.Equal((_centroComun, 2), (varios.IdCentroCoste!.Value, varios.Activos.Count));
        var ninguno = await Servicio.ActivosDeCentroAsync(_ctx, "3 INMUEBLES");
        Assert.Equal(_inmuebles, ninguno.IdCentroCoste);
        Assert.Empty(ninguno.Activos);
        var desconocido = await Servicio.ActivosDeCentroAsync(_ctx, "999 No existe");
        Assert.Null(desconocido.IdCentroCoste);
        Assert.Empty(desconocido.Activos);
    }

    [Fact]
    public async Task La_cuenta_de_resultados_suma_sus_subcuentas_y_su_centro_con_la_amortizacion_prevista()
    {
        await PrepararAsync();
        var id = await AltaAsync(Piso() with { IdCentroCoste = _centroA });
        await Servicio.GenerarSubcuentasAsync(_ctx, [id]);
        var alquiler = new Asiento(_ctx, new DateTime(2026, 8, 1), "Alquiler de agosto");
        var ingreso = Apunte.AlHaber("7050001", 900m);
        ingreso.IdCentroCoste = _centroA; // ingreso en una cuenta general, con el centro del piso
        alquiler.Apuntes.AddRange([Apunte.AlDebe("5720001", 900m), ingreso]);
        var seguro = new Asiento(_ctx, new DateTime(2026, 9, 1), "Seguro");
        seguro.Apuntes.AddRange([Apunte.AlDebe("6250001", 120m), Apunte.AlHaber("5720001", 120m)]); // subcuenta del piso, sin centro
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([alquiler, seguro], _actor, "Datos sintéticos")).EsCorrecto);

        var r = (await Servicio.CuentaResultadosAsync("T01", id, 2026, conAnterior: true, conGenerales: false))!;
        var c = r.Actual;
        Assert.Null(r.Anterior); // no hay 2025
        Assert.Equal(900m, c.Ingresos);
        // 80.000 al 3 % desde el 1/7: 184 días de 365 = 1.209,86, pendiente de contabilizar.
        var prevista = Assert.Single(c.Filas, f => f.Tipo == TipoFilaResultado.AmortizacionPrevista);
        Assert.Equal(1_209.86m, prevista.Total);
        // El resultado no lleva la amortización, ni la contabilizada ni la prevista (activos 2026-11-27 13:40): va aparte, informativa.
        Assert.Equal(120m, c.Gastos);
        Assert.Equal(900m - 120m, c.Resultado);
        var tabla = await Servicio.AmortizacionAsync(_ctx, id);
        Assert.Equal(1_209.86m, tabla.Filas.Single(f => f.Ejercicio == 2026).Cuota);
        Assert.Equal(EstadoAmortizacionEjercicio.PendienteDeContabilizar, tabla.Filas[0].Estado);
    }
}
