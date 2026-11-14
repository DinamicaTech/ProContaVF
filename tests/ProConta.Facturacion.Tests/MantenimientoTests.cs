using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Mantenimiento;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Mantenimiento;
using ProConta.Pruebas.Access;

namespace ProConta.Facturacion.Tests;

public class MantenimientoGenericoTests
{
    private static readonly ContextoMantenimiento Ctx = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("prueba"));

    private static ServicioMantenimiento Servicio(bool puedeEditar = true) =>
        new([.. ProveedoresOrganizacion.Todos(null!), .. ProveedoresFacturacion.Todos(null!, null!), new ProveedorConfiguracionGeneral(null!)], () => Task.FromResult(puedeEditar));

    [Fact]
    public void ValidacionGenericaAcumulaErroresPorCampo()
    {
        CampoMantenimiento[] campos =
        [
            new("Nombre", "Nombre") { Obligatorio = true, Longitud = 5 },
            new("Pct", "Porcentaje", TipoCampo.Porcentaje),
            new("Correo", "Correo", TipoCampo.Email),
            new("Cuenta", "Cuenta", TipoCampo.Cuenta),
        ];
        var r = new RegistroMantenimiento(null, new Dictionary<string, object?> { ["Nombre"] = "Demasiado largo", ["Pct"] = 120m, ["Correo"] = "sin-arroba", ["Cuenta"] = "47A" });
        var errores = ValidacionGenerica.Validar(campos, r);
        Assert.Equal(["Nombre", "Pct", "Correo", "Cuenta"], errores.Select(e => e.Campo));
        r = new RegistroMantenimiento(null, new Dictionary<string, object?> { ["Nombre"] = " " });
        Assert.Equal("Falta nombre.", Assert.Single(ValidacionGenerica.Validar(campos, r)).Mensaje);
    }

    [Fact]
    public void BusquedaSinTildesEncuentraMantenimientosYCampos()
    {
        var s = Servicio();
        var r = s.Buscar("retencion");
        Assert.Contains(r, x => x.Campo?.Clave == "CuentaRetencionServicios" && x.Proveedor.Clave == "ejercicios");
        Assert.Equal("series", s.Buscar("numeración").First().Proveedor.Clave);
        Assert.Empty(s.Buscar("   "));
    }

    [Fact]
    public async Task SinPermisoNiSeValidaNiSeGuarda()
    {
        var s = Servicio(puedeEditar: false);
        var p = s.Proveedor("iva")!;
        var r = await s.GuardarAsync(p, new RegistroMantenimiento(null), Ctx);
        Assert.Equal("Seguridad.SinPermiso", r.Error!.Codigo);
    }

    [Fact]
    public async Task TablasFiscalesSonDeSoloConsulta()
    {
        var s = Servicio();
        foreach (var clave in new[] { "tiposfactura", "estadosfactura" })
        {
            var p = s.Proveedor(clave)!;
            Assert.False(p.PermiteAlta);
            Assert.False(p.PermiteBaja);
            Assert.Equal("Mantenimiento.SoloConsulta", (await s.GuardarAsync(p, new RegistroMantenimiento("F1"), Ctx)).Error!.Codigo);
        }
    }

    [Fact]
    public void CatalogoDelDesglose()
    {
        var claves = Servicio().Proveedores.Select(p => p.Clave).ToHashSet();
        Assert.Superset(new HashSet<string> { "general", "sociedades", "ejercicios", "series", "formaspago", "iva", "tiposconcepto", "conceptos", "tiposfactura", "estadosfactura" }, claves);
        Assert.All(Servicio().Proveedores, p => Assert.Contains(p.Categoria, ServicioMantenimiento.Categorias));
    }
}

[Trait("Categoria", "Access")]
public sealed class MantenimientoAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private ContextoMantenimiento Ctx => new(_ctx, "Sociedad sintética", new Actor("prueba"));

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private ServicioMantenimiento Servicio() =>
        new([.. ProveedoresOrganizacion.Todos(Bd), .. ProveedoresFacturacion.Todos(Bd, new CatalogoCuentasAccess(Bd)), new ProveedorConfiguracionGeneral(Bd)], () => Task.FromResult(true));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2025);
    }

    /// <summary>
    /// Cada mantenimiento lee sus campos de la BD actualizada: un campo cuya columna no existe en su tabla hace fallar el listado
    /// entero («No se han especificado valores para algunos de los parámetros requeridos»), como pasó en Empresas con las cuentas
    /// de IVA repercutido, que solo tiene el ejercicio.
    /// </summary>
    [Fact]
    public async Task TodosLosMantenimientosListanConLaBdActualizada()
    {
        await PrepararAsync();
        await Bd.EjecutarAsync(x => x.EjecutarAsync("UPDATE Sociedades SET Cuenta113ParaCuadres = '1130000', Cuenta570Tesoreria = '5700000' WHERE IdSociedad = 'T01'"));
        foreach (var p in Servicio().Proveedores)
        {
            var filas = await p.ListarAsync(Ctx);
            Assert.NotNull(filas);
        }
        var empresa = (await Servicio().Proveedor("sociedades")!.ListarAsync(Ctx)).Single(r => r.Texto("IdSociedad") == "T01");
        Assert.Equal(("1130000", "5700000"), (empresa.Texto("Cuenta113ParaCuadres"), empresa.Texto("Cuenta570Tesoreria")));
        // Solo los campos que quedan en cada tabla (organizacion/sociedades-ejercicios 2026-11-13 20:55).
        Assert.Equal(["IdSociedad", "Nombre", "CIF", "DireccionFiscal", "CodigoPostalFiscal", "PoblacionFiscal", "ProvinciaFiscal", "Telefono",
            "Cuenta113ParaCuadres", "Cuenta570Tesoreria", "RutaDocumentos", "Logo"], Servicio().Proveedor("sociedades")!.Campos.Select(c => c.Clave).ToArray());
        Assert.Equal(new[] { "Ejercicio", "Nombre", "TrimestreActivo", "Activa", "EjercicioCerrado", "FormaPagoDefecto", "TipoIVAGeneral", "PorcentajeProrrataAñoAnterior",
            "CuentaSuplido", "CuentaRetencionServicios", "CuentaRetencionAlquileres", "CuentaIVAIntracomunitarioRepercutidoServicios", "CuentaIVAIntracomunitarioRepercutidoBienes",
            "CuentaIVAIntracomunitarioSoportadoServicios", "CuentaIVAIntracomunitarioSoportadoBienes", "CuentaIVAInversionSujetoPasivoRepercutido",
            "CuentaIVAInversionSujetoPasivoSoportado", "CuentaIVAImportacion", "CuentaRedondeoGastos", "CuentaRedondeoIngresos", "CarpetaFacturasCompra" }.Order(),
            Servicio().Proveedor("ejercicios")!.Campos.Select(c => c.Clave).Order());
        await Bd.EjecutarAsync(x => x.EjecutarAsync("UPDATE SociedadesEjercicios SET CuentaSuplido = '5550002', PorcentajeProrrataAñoAnterior = 0.14 WHERE IdSociedadEjercicio = 9001"));
        var ejercicio = (await Servicio().Proveedor("ejercicios")!.ListarAsync(Ctx)).Single(r => r.Texto("Ejercicio") == "2026");
        Assert.Equal("5550002", ejercicio.Texto("CuentaSuplido"));
        var eliminadas = await Bd.EjecutarAsync(x => Task.FromResult(x.LeerEsquema()));
        Assert.DoesNotContain(eliminadas.Tabla("SociedadesEjercicios")!.Columnas, c => c.Nombre is "CIF" or "TipoIVA1" or "CuentaTipoIVA0" or "CuentaIVARepercutido0" or "Usuario");
        Assert.DoesNotContain(eliminadas.Tabla("Sociedades")!.Columnas, c => c.Nombre is "FormaPagoDefecto" or "CuentaSuplido" or "TipoIVA1" or "BDExportacion");
    }

    /// <summary>
    /// La misma comprobación contra una BD de validación del propietario, solo si PROCONTA_BD_VALIDACION apunta a ella (se lee una
    /// copia temporal; el original no se abre). Sin la variable no hace nada.
    /// </summary>
    [Fact]
    public async Task TodosLosMantenimientosListanConLaBdDeValidacion()
    {
        if (Environment.GetEnvironmentVariable("PROCONTA_BD_VALIDACION") is not { Length: > 0 } origen || !File.Exists(origen)) return;
        var copia = Path.Combine(BdPruebas.CarpetaTemporal(), Path.GetFileName(origen));
        File.Copy(origen, copia);
        try
        {
            var bd = new ProConta.Datos.Persistencia.Access.BaseDatosAccess(copia);
            var ctx = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
                "SELECT TOP 1 IdSociedad, IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios ORDER BY Ejercicio DESC",
                f => new ContextoSociedadEjercicio(f.Texto("IdSociedad") ?? "", f.Entero("IdSociedadEjercicio"), int.Parse(f.Texto("Ejercicio") ?? "0"))));
            var servicio = new ServicioMantenimiento(
                [.. ProveedoresOrganizacion.Todos(bd), .. ProveedoresFacturacion.Todos(bd, new CatalogoCuentasAccess(bd)), new ProveedorConfiguracionGeneral(bd)],
                () => Task.FromResult(true));
            var contexto = new ContextoMantenimiento(ctx!, "Validación", new Actor("prueba"));
            foreach (var p in servicio.Proveedores) Assert.NotNull(await p.ListarAsync(contexto));
            Assert.NotEmpty(await servicio.Proveedor("sociedades")!.ListarAsync(contexto));
            Assert.NotEmpty(await servicio.Proveedor("ejercicios")!.ListarAsync(contexto));
        }
        finally
        {
            try { File.Delete(copia); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task SeriesConSusReglas()
    {
        await PrepararAsync();
        var s = Servicio();
        var p = s.Proveedor("series")!;
        async Task<RegistroMantenimiento> Nueva(string prefijo, bool rect = false, bool pred = false)
        {
            var n = await p.NuevoAsync(Ctx);
            n["Prefijo"] = prefijo;
            n["EsRectificativa"] = rect;
            n["EsPredeterminada"] = pred;
            var g = await s.GuardarAsync(p, n, Ctx);
            Assert.True(g.EsCorrecto, g.Error?.ToString());
            return g.Valor;
        }
        var f = await Nueva("F2026", pred: true);
        await Nueva("R2026", rect: true);
        var otraRect = await p.NuevoAsync(Ctx);
        otraRect["Prefijo"] = "X2026";
        otraRect["EsRectificativa"] = true;
        Assert.False((await s.GuardarAsync(p, otraRect, Ctx)).EsCorrecto);

        // Marcar otra como predeterminada quita la marca a la anterior y lo avisa.
        var g2 = await Nueva("G2026", pred: true);
        Assert.NotNull(g2["_Aviso"]);
        Assert.False((await p.ListarAsync(Ctx)).Single(x => x.Texto("Prefijo") == "F2026").Logico("EsPredeterminada"));

        // Con una factura, el prefijo queda bloqueado y la serie no se borra.
        await Bd.EjecutarAsync(x => x.EjecutarAsync("UPDATE SeriesFacturacion SET UltimoNumero = 5 WHERE IdSerie = ?", f.Clave));
        await Bd.EjecutarAsync(x => x.EjecutarAsync("INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, Descripcion, EstadoFactura, TipoFactura, FechaCreacion) VALUES (?, ?, ?, ?, ?, ?, ?)",
            9001, f.Clave, "F20260005", "Sintética", (byte)1, "F1", DateTime.Now));
        var conFactura = (await p.ListarAsync(Ctx)).Single(x => x.Texto("Prefijo") == "F2026");
        Assert.True(conFactura.Bloqueos.ContainsKey("Prefijo"));
        conFactura["RangoHasta"] = 3;
        Assert.False((await s.GuardarAsync(p, conFactura, Ctx)).EsCorrecto);
        Assert.Equal("Series.EnUso", (await s.EliminarAsync(p, conFactura, Ctx)).Error!.Codigo);
        Assert.True((await s.EliminarAsync(p, g2, Ctx)).EsCorrecto);
    }

    [Fact]
    public async Task ConceptosPorSociedadConCuentaPorIdentidad()
    {
        await PrepararAsync();
        var cuentas = new CatalogoCuentasAccess(Bd);
        var c2025 = new ContextoSociedadEjercicio("T01", 9000, 2025);
        var venta2025 = (await cuentas.AltaAsync(c2025, new AltaCuenta("7520001", "Arrendamientos"))).Valor;
        var venta2026 = (await cuentas.AltaEnEjercicioAsync(_ctx, venta2025.IdIdentidadContable)).Valor;
        var catalogo = new CatalogoConceptos(Bd, cuentas);

        var alta = await catalogo.GuardarAsync(new Concepto(0, "T01", null, "Alquiler local", null, null, null, null, false, null, null, 500m, 21m, true, null), "7520001", c2025);
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        Assert.Equal(venta2025.IdIdentidadContable, alta.Valor.IdIdentidadContable);

        // En 2026 se ve la cuenta de la misma identidad en ese ejercicio.
        var en2026 = Assert.Single(await catalogo.ListarAsync(_ctx));
        Assert.Equal(venta2026.Codigo, en2026.CuentaVenta);
        Assert.True(en2026.CuentaEnEjercicio);

        Assert.Equal("Conceptos.Repetido", (await catalogo.GuardarAsync(en2026 with { IdConcepto = 0 }, "7520001", _ctx)).Error!.Codigo);
        var copia = await catalogo.DuplicarAsync(en2026.IdConcepto, _ctx);
        Assert.True(copia.EsCorrecto, copia.Error?.ToString());
        Assert.Equal("Alquiler local (copia)", copia.Valor.Nombre);
        Assert.False(copia.Valor.Activo);

        // Usado en una línea no se borra; la copia sin uso, sí.
        await Bd.EjecutarAsync(async x =>
        {
            if (Convert.ToInt32(await x.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = ?", "F1")) == 0)
                await x.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES (?, ?, ?)", "F1", "Factura", false);
            if (Convert.ToInt32(await x.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", (byte)3)) == 0)
                await x.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", (byte)3, "Borrador", true, false);
            await x.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Serie sintética", 1, 99999, "S2026", false, true);
            var idSerie = await x.UltimoIdAsync();
            await x.EjecutarAsync("INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, Descripcion, EstadoFactura, TipoFactura, FechaCreacion) VALUES (?, ?, ?, ?, ?, ?)", 9001, idSerie, "Sintética", (byte)3, "F1", DateTime.Now);
            var idFactura = await x.UltimoIdAsync();
            return await x.EjecutarAsync("INSERT INTO LineasFactura (IdFactura, NumeroLinea, IdConcepto, Nombre, Descripcion, Cantidad, PrecioUnitario, BaseImponible, TipoIVA, CuotaIVA, ImporteTotal) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                idFactura, (short)1, en2026.IdConcepto, "Alquiler", " ", 1d, 500m, 500m, 21d, 105m, 605m);
        });
        Assert.True((await catalogo.UsoAsync(en2026.IdConcepto)).EnUso);
        Assert.Equal("Conceptos.EnUso", (await catalogo.EliminarAsync(en2026.IdConcepto, _ctx)).Error!.Codigo);
        Assert.True((await catalogo.EliminarAsync(copia.Valor.IdConcepto, _ctx)).EsCorrecto);

        // Otra sociedad no ve los conceptos de T01.
        Assert.Empty(await catalogo.ListarAsync(new ContextoSociedadEjercicio("T02", 9999, 2026)));
    }

    [Fact]
    public async Task TablaSencillaConUnicidadYEjerciciosDeLaEmpresa()
    {
        await PrepararAsync();
        var s = Servicio();
        var iva = s.Proveedor("iva")!;
        var antes = (await iva.ListarAsync(Ctx)).Count;
        var n = await iva.NuevoAsync(Ctx);
        n["Valor"] = 7.5m;
        n["Descripcion"] = "Tipo sintético";
        n["Activo"] = true;
        var g = await s.GuardarAsync(iva, n, Ctx);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal(7.5m, g.Valor.Decimal("Valor"));
        var repetido = await iva.NuevoAsync(Ctx);
        repetido["Valor"] = 7.5m;
        repetido["Descripcion"] = "Otro";
        Assert.Equal("TiposIva.Repetido", (await s.GuardarAsync(iva, repetido, Ctx)).Error!.Codigo);
        Assert.Equal(antes + 1, (await iva.ListarAsync(Ctx)).Count);

        var ejercicios = s.Proveedor("ejercicios")!;
        Assert.Equal(["2026", "2025"], (await ejercicios.ListarAsync(Ctx)).Select(r => r.Texto("Ejercicio")));
        var otro = await ejercicios.NuevoAsync(Ctx);
        otro["Ejercicio"] = "2026";
        Assert.Equal("Mantenimiento.EjercicioRepetido", (await s.GuardarAsync(ejercicios, otro, Ctx)).Error!.Codigo);

        var general = s.Proveedor("general")!;
        var cfg = Assert.Single(await general.ListarAsync(Ctx));
        cfg["EmailIncidencias"] = "avisos@ejemplo.test";
        var gc = await s.GuardarAsync(general, cfg, Ctx);
        Assert.True(gc.EsCorrecto, gc.Error?.ToString());
        Assert.Equal("prueba", gc.Valor.Texto("OperadorModificacion"));
    }
}
