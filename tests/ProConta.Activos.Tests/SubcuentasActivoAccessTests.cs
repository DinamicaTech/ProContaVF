using System.Globalization;
using ProConta.Activos.Mantenimiento;
using ProConta.Activos.Subcuentas;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Activos.Tests;

/// <summary>Mantenimientos de plantillas y «Generar subcuentas» contra una copia de pruebas con datos sintéticos.</summary>
[Trait("Categoria", "Access")]
public sealed class SubcuentasActivoAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly ContextoSociedadEjercicio _ctx2027 = new("T01", 9002, 2027);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private GeneradorSubcuentasActivo Generador => new(Bd, Cuentas);
    private ContextoMantenimiento Contexto(ContextoSociedadEjercicio c) => new(c, "Sociedad sintética", new Actor("Pruebas"));
    private ProveedorPlantillasSubcuentasActivo Plantillas => new(Bd, Generador);

    private int _vivienda;
    private int _centro;

    private async Task EmpresaAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2027);
        Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta("2210", "Construcciones", EsImputable: false))).EsCorrecto);
        Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta("2820", "Amortización acumulada de construcciones", EsImputable: false))).EsCorrecto);
        (_vivienda, _centro) = await Bd.EjecutarAsync(async s =>
        {
            var tipo = Convert.ToInt32(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'VIVIENDA'"), CultureInfo.InvariantCulture);
            await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", "300", "Piso A", true, true, (short)1, (byte)1, "300", "300");
            return (tipo, await s.UltimoIdAsync());
        });
        await ActivoAsync("Piso A", _centro, ajeno: false);
        await ActivoAsync("Piso particular", null, ajeno: true);
    }

    private Task<int> ActivoAsync(string nombre, int? centro, bool ajeno) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync(
            "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, DeterioroAcumulado, " +
            "ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "T01", _vivienda, nombre.ToUpperInvariant().Replace(' ', '-'), nombre, new DateTime(2026, 1, 1), (byte)1, !ajeno, 0m, 0m, 0m, 0m, (byte)0, centro, ajeno, 0);
        return await s.UltimoIdAsync();
    });

    /// <summary>Línea nueva escribiendo el nombre del tipo de subcuenta (se crea por dentro si no existe).</summary>
    private async Task<RegistroMantenimiento> LineaAsync(string tipoSubcuenta, string prefijo, bool activa = true)
    {
        var p = Plantillas;
        var r = await p.NuevoAsync(Contexto(_ctx));
        r["IdTipoActivo"] = _vivienda;
        r["TipoSubcuenta"] = tipoSubcuenta;
        r["Prefijo"] = prefijo;
        r["EstaActiva"] = activa;
        Assert.Empty(p.Validar(r));
        var g = await p.GuardarAsync(r, Contexto(_ctx));
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        return g.Valor;
    }

    private Task<int> ContarAsync(string sql, params object?[] p) =>
        Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    [Fact]
    public async Task Genera_las_subcuentas_que_faltan_y_la_segunda_vez_no_hace_nada()
    {
        await EmpresaAsync();
        await LineaAsync("Construcción [TOKEN]", "2210");
        await LineaAsync("Amort. acumulada [TOKEN]", "2820");
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM TiposSubcuentaActivo WHERE Rol = 'LIBRE' AND Nombre IN ('Construcción [TOKEN]', 'Amort. acumulada [TOKEN]')"));

        var propuesta = await Generador.ProponerAsync(_ctx, [_vivienda]);
        Assert.Equal(["Construcción Piso A", "Amort. acumulada Piso A"], propuesta.Select(o => o.NombreNuevo));
        Assert.All(propuesta, o => Assert.Equal(AccionSubcuenta.Crear, o.Accion));

        var r = await Generador.GenerarAsync(_ctx, [_vivienda]);
        Assert.Empty(r.Errores);
        Assert.Equal(2, r.Creadas);
        var construccion = (await Cuentas.ResolverAsync(_ctx, "2210001")).Valor;
        Assert.Equal(("Construcción Piso A", _centro), (construccion.Nombre, construccion.IdCentroCostePredeterminado!.Value));
        Assert.Equal((await Cuentas.ResolverAsync(_ctx, "2210")).Valor.IdCuentaContable, construccion.IdCuentaPadre);
        Assert.True((await Cuentas.ResolverAsync(_ctx, "2820001")).EsCorrecto);
        // El activo ajeno no recibe nada (activos 2026-11-12 11:45, E).
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM SubcuentasActivo"));

        Assert.Empty(await Generador.ProponerAsync(_ctx, [_vivienda]));
        var otra = await Generador.GenerarAsync(_ctx, [_vivienda]);
        Assert.Equal((0, 0, 0), (otra.Creadas, otra.DadasDeAlta, otra.Renombradas));
        var tipos = new ProveedorTiposActivo(Bd, Generador);
        var registro = (await tipos.ListarAsync(Contexto(_ctx))).First(x => Convert.ToInt32(x.Clave, CultureInfo.InvariantCulture) == _vivienda);
        Assert.True((await tipos.ProponerAsync("generar-subcuentas", registro, Contexto(_ctx))).Valor.NadaQueHacer);
    }

    [Fact]
    public async Task Renombra_al_cambiar_la_plantilla_y_da_de_alta_en_otro_ejercicio_con_el_mismo_codigo()
    {
        await EmpresaAsync();
        await LineaAsync("Construcción [TOKEN]", "2210");
        await Generador.GenerarAsync(_ctx, [_vivienda]);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE TiposSubcuentaActivo SET Nombre = 'Construcción vivienda [TOKEN]' WHERE Nombre = 'Construcción [TOKEN]'"));

        var renombre = Assert.Single(await Generador.ProponerAsync(_ctx, [_vivienda]));
        Assert.Equal((AccionSubcuenta.Renombrar, "2210001", false), (renombre.Accion, renombre.Actual!.Codigo, renombre.MuyDistinto));
        Assert.Equal(1, (await Generador.GenerarAsync(_ctx, [_vivienda])).Renombradas);
        Assert.Equal("Construcción vivienda Piso A", (await Cuentas.ResolverAsync(_ctx, "2210001")).Valor.Nombre);

        var alta = Assert.Single(await Generador.ProponerAsync(_ctx2027, [_vivienda]));
        Assert.Equal(AccionSubcuenta.AltaEnEjercicio, alta.Accion);
        Assert.Equal(1, (await Generador.GenerarAsync(_ctx2027, [_vivienda])).DadasDeAlta);
        Assert.Equal("Construcción vivienda Piso A", (await Cuentas.ResolverAsync(_ctx2027, "2210001")).Valor.Nombre);
        Assert.Empty(await Generador.ProponerAsync(_ctx2027, [_vivienda]));
    }

    [Fact]
    public async Task Un_rol_fijo_solo_puede_estar_una_vez_en_la_plantilla_de_un_tipo()
    {
        await EmpresaAsync();
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES ('Valor [TOKEN]', NULL, 'ACTIVO', True)");
            await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES ('Otro valor [TOKEN]', NULL, 'ACTIVO', True)");
            return true;
        });
        await LineaAsync("Valor [TOKEN]", "2210");
        var p = Plantillas;
        var r = await p.NuevoAsync(Contexto(_ctx));
        r["IdTipoActivo"] = _vivienda;
        r["TipoSubcuenta"] = "otro valor [token]";
        r["Prefijo"] = "2211";
        var g = await p.GuardarAsync(r, Contexto(_ctx));
        Assert.Equal("Plantillas.RolRepetido", g.Error!.Codigo);
    }

    [Fact]
    public async Task Una_linea_con_subcuentas_no_se_borra_y_desactivada_borra_las_que_no_tienen_apuntes()
    {
        await EmpresaAsync();
        var linea = await LineaAsync("Seguro [TOKEN]", "2210");
        await Generador.GenerarAsync(_ctx, [_vivienda]);
        var p = Plantillas;
        linea = (await p.ListarAsync(Contexto(_ctx))).Single();
        Assert.Equal("Plantillas.ConSubcuentas", (await p.EliminarAsync(linea, Contexto(_ctx))).Error!.Codigo);
        Assert.Equal("Plantillas.LineaActiva", (await p.ProponerAsync("borrar-sin-apuntes", linea, Contexto(_ctx))).Error!.Codigo);

        linea["EstaActiva"] = false;
        var guardada = await p.GuardarAsync(linea, Contexto(_ctx));
        Assert.True(guardada.EsCorrecto, guardada.Error?.ToString());
        Assert.Contains("Borrar subcuentas sin apuntes", guardada.Valor.Texto("_Aviso"));
        Assert.Empty(await Generador.ProponerAsync(_ctx, [_vivienda]));

        var propuesta = await p.ProponerAsync("borrar-sin-apuntes", guardada.Valor, Contexto(_ctx));
        Assert.Contains("2210001", Assert.Single(propuesta.Valor.Detalle!));
        var hecho = await p.EjecutarAsync("borrar-sin-apuntes", guardada.Valor, Contexto(_ctx));
        Assert.True(hecho.EsCorrecto, hecho.Error?.ToString());
        Assert.Null(hecho.Valor.Detalle);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM SubcuentasActivo"));
        Assert.False((await Cuentas.ResolverAsync(_ctx, "2210001")).EsCorrecto);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM CodigosContablesReservados WHERE Codigo = '2210001'"));

        // Ya sin subcuentas, la línea sí se puede borrar.
        Assert.True((await p.EliminarAsync(guardada.Valor, Contexto(_ctx))).EsCorrecto);
    }

    [Fact]
    public async Task Una_subcuenta_con_apuntes_no_se_borra()
    {
        await EmpresaAsync();
        await LineaAsync("Seguro [TOKEN]", "2210");
        await Generador.GenerarAsync(_ctx, [_vivienda]);
        var identidad = (await Cuentas.ResolverAsync(_ctx, "2210001")).Valor.IdIdentidadContable;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CuentasContables SET SaldoInicial = 100 WHERE Codigo = '2210001'"));
        Assert.Empty(await Generador.SinApuntesAsync(_ctx, (await Plantillas.ListarAsync(Contexto(_ctx))).Select(x => Convert.ToInt32(x.Clave, CultureInfo.InvariantCulture)).Single()));
        Assert.Equal("Cuentas.ConApuntes", (await Cuentas.EliminarSinApuntesAsync(_ctx, identidad)).Error!.Codigo);
    }
}
