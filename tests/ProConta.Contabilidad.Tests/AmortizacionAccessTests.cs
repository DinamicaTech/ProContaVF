using System.Globalization;
using ProConta.Contabilidad.Amortizacion;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Amortización de activos contra Access (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59): entran los activos amortizables
/// y propios con plan y subcuentas; el activo ajeno no sale y el que no tiene plan sale como problema; confirmar graba un asiento que
/// cuadra como lote del periodo; repetir no duplica; la amortización mensual suma la anual; lo excluido se guarda; deshacer borra el
/// lote. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class AmortizacionAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private static readonly DateTime Enero = new(2027, 1, 10);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);
    private ServicioAmortizacion Servicio(DateTime? hoy = null) => new(Bd, new MotorAsientosAccess(Bd), null, new RelojFijo(hoy ?? Enero));

    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)), CultureInfo.InvariantCulture);
    private Task<int> AsientosAsync() => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioAmortizacion.Motor);

    private int _piso, _ajeno, _sinPlan, _centro;

    /// <summary>
    /// Piso A: base 36.500, 2 % desde 2015, con 6820001 y 2820001 y 7.300 ya amortizados (en la apertura) → 730 al año.
    /// Piso particular: ajeno (no sale). Local B: amortizable sin plan (sale como problema).
    /// </summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("100", "Capital"), ("221", "Construcciones"), ("282", "Amortización acumulada de inversiones inmobiliarias"),
                     ("682", "Amortización de inversiones inmobiliarias"), ("572", "Bancos") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital social"), ("5720001", "Banco sintético"), ("2210001", "Construcción Piso A") })
            Assert.True((await Cuentas().AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);
        var gasto = await Cuentas().AltaSubcuentaAsync(_ctx, "6820001", "Amortización Piso A");
        var acumulada = await Cuentas().AltaSubcuentaAsync(_ctx, "2820001", "Amort. acumulada Piso A");
        Assert.True(gasto.EsCorrecto && acumulada.EsCorrecto);

        await Bd.EjecutarAsync(async s =>
        {
            var tipo = Convert.ToInt32(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'VIVIENDA'"), CultureInfo.InvariantCulture);
            await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", "300", "Piso A", true, true, (short)1, (byte)1, "300", "300");
            _centro = await s.UltimoIdAsync();
            async Task<int> Activo(string codigo, string nombre, bool amortizable, bool ajeno, int? centro)
            {
                await s.EjecutarAsync(
                    "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, DeterioroAcumulado, " +
                    "ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", tipo, codigo, nombre, new DateTime(2015, 1, 1), (byte)1, amortizable, 0m, 0m, 0m, 0m, (byte)0, centro, ajeno, 0);
                return await s.UltimoIdAsync();
            }
            _piso = await Activo("PISO-A", "Piso A", true, false, _centro);
            _ajeno = await Activo("PISO-PART", "Piso particular", false, true, null);
            _sinPlan = await Activo("LOCAL-B", "Local B", true, false, null);
            foreach (var id in new[] { _piso, _ajeno })
                await s.EjecutarAsync("INSERT INTO PlanesAmortizacionActivo (IdActivo, FechaInicio, BaseAmortizable, ValorResidual, VidaUtilMeses, Metodo, EstaActivo, PorcentajeAnual) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    id, new DateTime(2015, 1, 1), 36_500m, 0m, 600, "LINEAL", true, 0.02m);

            async Task<int> Linea(string nombre, string rol, string prefijo, short orden)
            {
                await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, ?)", nombre, nombre, rol, true);
                var tipoSub = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo, Prefijo, Orden, EstaActiva) VALUES (?, ?, ?, ?, ?)", tipo, tipoSub, prefijo, orden, true);
                return await s.UltimoIdAsync();
            }
            var lineaGasto = await Linea("Prueba amortización [TOKEN]", ServicioAmortizacion.RolGasto, "6820", 901);
            var lineaAcumulada = await Linea("Prueba amort. acumulada [TOKEN]", ServicioAmortizacion.RolAcumulada, "2820", 902);
            await s.EjecutarAsync("INSERT INTO SubcuentasActivo (IdActivo, IdPlantillaSubcuentaActivo, IdIdentidadContable, FechaHoraCreacion) VALUES (?, ?, ?, ?)",
                _piso, lineaGasto, gasto.Valor.IdIdentidadContable, new DateTime(2026, 1, 1));
            await s.EjecutarAsync("INSERT INTO SubcuentasActivo (IdActivo, IdPlantillaSubcuentaActivo, IdIdentidadContable, FechaHoraCreacion) VALUES (?, ?, ?, ?)",
                _piso, lineaAcumulada, acumulada.Valor.IdIdentidadContable, new DateTime(2026, 1, 1));
            return 0;
        });

        var apertura = new Asiento(_ctx, new DateTime(2026, 1, 1), "Saldos iniciales sintéticos");
        apertura.Apuntes.AddRange([Apunte.AlDebe("2210001", 36_500m), Apunte.AlHaber("2820001", 7_300m), Apunte.AlHaber("1000000", 29_200m)]);
        var r = await new MotorAsientosAccess(Bd).GuardarManualesAsync([apertura], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
    }

    private Task PeriodicidadAsync(string valor) => Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET PeriodicidadAmortizacion = ? WHERE IdSociedadEjercicio = 9001", valor));

    [Fact]
    public async Task Confirmar_graba_el_asiento_anual_y_repetir_no_duplica()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var situacion = await servicio.SituacionAsync(_ctx);
        var anual = Assert.Single(situacion.Periodos);
        Assert.Equal(EstadoAmortizacion.Pendiente, anual.Estado);
        var p = anual.Propuesta;
        Assert.Equal(["PISO-A", "LOCAL-B"], p.Filas.Select(f => f.Activo.Codigo));
        Assert.DoesNotContain(p.Filas, f => f.Activo.IdActivo == _ajeno);
        Assert.True(p.Filas[1].ConProblema);
        var piso = p.Filas[0].Cuota!;
        Assert.Equal((730m, 7_300m, 29_200m), (piso.Importe, piso.Acumulada, piso.Pendiente));

        var r = await servicio.ConfirmarAsync(_ctx, anual.Periodo, new HashSet<int>(), null, _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal(1, await AsientosAsync());
        var (debe, haber, centro, fecha) = await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT SUM(p.ImporteDebe) AS D, SUM(p.ImporteHaber) AS H, MAX(p.IdCentroCoste) AS C, MAX(a.FechaContable) AS F FROM Asientos AS a INNER JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento WHERE a.MotorPropietario = ?",
            f => (f.DecimalNulo("D") ?? 0m, f.DecimalNulo("H") ?? 0m, f.EnteroNulo("C"), f.Fecha("F")), ServicioAmortizacion.Motor));
        Assert.Equal((730m, 730m, _centro, new DateTime(2026, 12, 31)), (debe, haber, centro, fecha));
        Assert.Equal(EstadoAmortizacion.Hecho, (await servicio.SituacionAsync(_ctx)).Periodos[0].Estado);

        // Repetir pide motivo y no duplica: la propuesta sigue siendo 730 (su propio lote no cuenta como ya contabilizado).
        var sinMotivo = await servicio.ConfirmarAsync(_ctx, anual.Periodo, new HashSet<int>(), null, _actor);
        Assert.False(sinMotivo.EsCorrecto);
        var otra = await servicio.ConfirmarAsync(_ctx, anual.Periodo, new HashSet<int>(), "Prueba de repetición", _actor);
        Assert.True(otra.EsCorrecto, otra.Error?.Mensaje);
        Assert.Equal(1, await AsientosAsync());
        Assert.Equal(730m, (await servicio.SituacionAsync(_ctx)).Periodos[0].Grabado);

        // Deshacer borra el lote.
        var d = await servicio.DeshacerAsync(_ctx, anual.Periodo, "Prueba", _actor);
        Assert.True(d.EsCorrecto, d.Error?.Mensaje);
        Assert.Equal(0, await AsientosAsync());
    }

    [Fact]
    public async Task La_amortizacion_mensual_suma_la_anual()
    {
        await PrepararAsync();
        await PeriodicidadAsync("MENSUAL");
        var servicio = Servicio();
        var periodos = (await servicio.SituacionAsync(_ctx)).Periodos;
        Assert.Equal(12, periodos.Count);
        foreach (var periodo in periodos.Select(x => x.Periodo))
        {
            var r = await servicio.ConfirmarAsync(_ctx, periodo, new HashSet<int>(), null, _actor);
            Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        }
        Assert.Equal(12, await AsientosAsync());
        var total = Convert.ToDecimal(await Bd.EjecutarAsync(s => s.EscalarAsync(
            "SELECT SUM(p.ImporteDebe) FROM Asientos AS a INNER JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento WHERE a.MotorPropietario = ? AND p.CuentaContable = '6820001'",
            ServicioAmortizacion.Motor)), CultureInfo.InvariantCulture);
        Assert.Equal(730m, total);
        Assert.All((await servicio.SituacionAsync(_ctx)).Periodos, x => Assert.Equal(EstadoAmortizacion.Hecho, x.Estado));

        // Pasar a anual no duplica: lo ya amortizado cada mes se descuenta.
        await PeriodicidadAsync("ANUAL");
        Assert.Equal(0m, (await servicio.SituacionAsync(_ctx)).Periodos.Single().Propuesta.Total);
    }

    [Fact]
    public async Task Lo_excluido_se_guarda_y_un_ejercicio_a_medias_pide_motivo()
    {
        await PrepararAsync();
        var servicio = Servicio(new DateTime(2026, 11, 17));
        var periodo = (await servicio.SituacionAsync(_ctx)).Periodos.Single().Periodo;
        var sinMotivo = await servicio.ConfirmarAsync(_ctx, periodo, new HashSet<int> { _piso }, null, _actor);
        Assert.False(sinMotivo.EsCorrecto);
        var r = await servicio.ConfirmarAsync(_ctx, periodo, new HashSet<int> { _piso }, "Prueba de exclusión", _actor);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Null(r.Valor);
        Assert.Equal(0, await AsientosAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM AmortizacionExclusiones WHERE IdSociedadEjercicio = 9001 AND IdActivo = ?", _piso));
        var p = await servicio.ProponerAsync(_ctx, periodo);
        Assert.False(p.Filas.Single(f => f.Activo.IdActivo == _piso).Incluido);
        Assert.Equal(0m, p.Total);
    }
}
