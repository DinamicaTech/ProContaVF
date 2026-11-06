using ProConta.Compras.Facturas;
using ProConta.Compras.Proveedores;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;
using static ProConta.Compras.Tests.Documentos;

namespace ProConta.Compras.Tests;

/// <summary>
/// Exportar, re-exportar y deshacer la exportación de facturas de compra contra una copia con datos sintéticos
/// (compras/pantallas 2026-11-06 12:10): asiento y libro de IVA en la misma operación, reglas del trimestre activo y
/// pregunta 400/410 en la primera exportación de un acreedor.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ContabilizacionComprasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private RelojFijo _reloj = new(new DateTime(2026, 8, 20, 10, 0, 0));

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private sealed class RelojFijo(DateTime ahora) : IReloj
    {
        public DateTime Ahora => ahora;
    }

    private ContabilizadorCompras Servicio()
    {
        var cuentas = new CatalogoCuentasAccess(Bd);
        return new ContabilizadorCompras(Bd, new MotorAsientosAccess(Bd, reloj: _reloj), cuentas, new ServicioRoles(Bd, cuentas), _reloj);
    }

    private async Task EmpresaAsync(int trimestreActivo = 3)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", NifSociedad);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE SociedadesEjercicios SET TipoIVA1 = ?, TipoIVA2 = ?, CuentaTipoIVA1 = ?, CuentaTipoIVA2 = ?, TrimestreActivo = ?, " +
            "CuentaRedondeoGastos = ?, CuentaRedondeoIngresos = ? WHERE IdSociedadEjercicio = ?",
            21f, 10f, "4720021", "4720010", trimestreActivo, "6690000", "7690000", 9001));
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre, imputable) in new[]
        {
            ("400", "Proveedores", false), ("410", "Acreedores", false), ("6290000", "Otros servicios", true), ("4720021", "IVA soportado 21 %", true),
            ("4720010", "IVA soportado 10 %", true), ("6690000", "Redondeo negativo", true), ("7690000", "Redondeo positivo", true)
        })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: imputable))).EsCorrecto);
    }

    /// <summary>Acreedor sintético con su 410 (como lo da de alta la ficha) y una factura suya registrada.</summary>
    private async Task<(FacturaCompra Factura, ProveedorCreado Proveedor)> FacturaAsync(decimal total = 176m, string md5Letra = "a")
    {
        var cuentas = new CatalogoCuentasAccess(Bd);
        var alta = new ServicioAltaProveedorCompra(Bd, new RepositorioTercerosAccess(Bd), new ServicioRoles(Bd, cuentas), cuentas, null, _actor);
        var prov = await alta.AltaAsync(_ctx, new DatosAltaProveedor(NifProveedor, "Emisor Ficticio S.L."));
        Assert.True(prov.EsCorrecto, prov.Error?.ToString());
        var f = new FacturaCompra
        {
            Ejercicio = _ctx, RutaPdf = @$"X:\Soc\2026\Proveedores\Ficticio\{md5Letra}.pdf", Md5 = new string(md5Letra[0], 32), Carpeta = "Ficticio",
            IdProveedor = prov.Valor.IdProveedor, CuentaContableProveedor = prov.Valor.Cuenta, NifProveedor = NifProveedor, NumeroFactura = "FV" + md5Letra,
            Fecha = new DateTime(2026, 7, 10), Concepto = "Emisor Ficticio S.L. Fact: FV" + md5Letra, BaseImponible = 150m, CuotaIva = 26m, ImporteTotal = total,
            CuentaGasto = "6290000", Producto = ProductoCompra.Servicios
        };
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 21m, 100m, 21m));
        f.Desgloses.Add(new DesgloseFacturaCompra(2, 10m, 50m, 5m));
        var id = await Bd.EnTransaccionAsync(s => RepositorioFacturasCompra.InsertarAsync(s, f, _reloj.Ahora));
        Assert.True(id.EsCorrecto, id.Error?.ToString());
        return ((await Bd.EjecutarAsync(s => RepositorioFacturasCompra.CargarAsync(s, id.Valor)))!, prov.Valor);
    }

    private Task<FacturaCompra> RecargarAsync(int id) => Bd.EjecutarAsync(async s => (await RepositorioFacturasCompra.CargarAsync(s, id))!);

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    [Fact]
    public async Task La_primera_de_un_acreedor_pregunta_400_o_410_y_exporta_asiento_y_libro_de_iva()
    {
        await EmpresaAsync();
        var (f, _) = await FacturaAsync();
        var servicio = Servicio();

        var c = await servicio.ComprobarAsync(f);
        Assert.True(c.Lista, string.Join(" ", c.Errores.Select(e => e.Mensaje)));
        Assert.Equal(PreguntaExportacion.ProveedorOAcreedor, c.Pregunta);
        Assert.Equal("Exportar.Pregunta400", (await servicio.ExportarAsync(f, _actor, new OpcionesExportacion(Masiva: true))).Error!.Codigo);

        var r = await servicio.ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: false));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        var g = await RecargarAsync(f.IdRegistro);
        Assert.True(g.Contabilizada);
        Assert.Equal(r.Valor.IdAsiento, g.IdAsientoContable);
        var asiento = (await new MotorAsientosAccess(Bd).ObtenerAsync(r.Valor.IdAsiento!.Value))!;
        Assert.Equal(new DateTime(2026, 8, 20), asiento.FechaContable);
        Assert.Equal(176m, asiento.TotalDebe.Valor);
        Assert.Equal("compras/factura-compra", asiento.Lote!.MotorPropietario);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM IVASoportado WHERE IdAsiento = ?", r.Valor.IdAsiento));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM DesglosesIVASoportado"));
        Assert.Equal(3, await Contar("SELECT p.Numero FROM IVASoportado AS i INNER JOIN PeriodosIVA AS p ON i.IdPeriodoIVA = p.IdPeriodoIVA WHERE i.IdAsiento = ?", r.Valor.IdAsiento));
        Assert.Equal(26m, Convert.ToDecimal(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT CuotaSoportadaTotal FROM IVASoportado"))));
        Assert.Equal(PreguntaExportacion.Ninguna, (await servicio.ComprobarAsync(g)).Pregunta);
    }

    [Fact]
    public async Task Pasar_a_proveedor_crea_la_400_y_exporta_con_ella()
    {
        await EmpresaAsync();
        var (f, prov) = await FacturaAsync();
        var r = await Servicio().ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: true));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal("4000001", r.Valor.CuentaProveedor);
        var cuentas = new CatalogoCuentasAccess(Bd);
        var roles = await new ServicioRoles(Bd, cuentas).CuentasAsync(prov.IdTercero, _ctx);
        Assert.Equal("4000001", roles.Proveedor!.Codigo);
        Assert.Null(roles.Acreedor);
        Assert.Equal("4000001", (await RecargarAsync(f.IdRegistro)).CuentaContableProveedor);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Apuntes WHERE CuentaContable = '4000001' AND IdAsiento = ?", r.Valor.IdAsiento));
    }

    [Fact]
    public async Task En_el_trimestre_activo_reexportar_regenera_y_deshacer_borra_asiento_y_libro()
    {
        await EmpresaAsync();
        var (f, _) = await FacturaAsync();
        var servicio = Servicio();
        Assert.True((await servicio.ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: false))).EsCorrecto);

        var g = await RecargarAsync(f.IdRegistro);
        g.Concepto = "Concepto cambiado";
        var r = await servicio.ExportarAsync(g, _actor, guardarCambios: true);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Null(r.Valor.IdContraAsiento);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'compras/factura-compra'"));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM IVASoportado"));
        var h = await RecargarAsync(f.IdRegistro);
        Assert.Equal(("Concepto cambiado", r.Valor.IdAsiento), (h.Concepto, h.IdAsientoContable));

        var d = await servicio.DeshacerAsync(h, _actor);
        Assert.True(d.EsCorrecto, d.Error?.ToString());
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'compras/factura-compra'"));
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM IVASoportado"));
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM DesglosesIVASoportado"));
        var i = await RecargarAsync(f.IdRegistro);
        Assert.False(i.Contabilizada);
        Assert.Null(i.IdAsientoContable);
    }

    [Fact]
    public async Task De_un_trimestre_cerrado_se_hace_contra_asiento_y_reexportar_otra_vez_modifica_esos_dos()
    {
        await EmpresaAsync(trimestreActivo: 3);
        var (f, _) = await FacturaAsync();
        Assert.True((await Servicio().ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: false))).EsCorrecto);
        var original = (await RecargarAsync(f.IdRegistro)).IdAsientoContable!.Value;

        // Se cierra el tercer trimestre: el asiento original ya no se toca.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 4 WHERE IdSociedadEjercicio = 9001"));
        _reloj = new RelojFijo(new DateTime(2026, 10, 15, 9, 0, 0));
        var servicio = Servicio();
        var g = await RecargarAsync(f.IdRegistro);
        Assert.True(await servicio.TrimestreCerradoAsync(g));
        g.Concepto = "Corregida en el cuarto";
        var r = await servicio.ExportarAsync(g, _actor, guardarCambios: true);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.NotNull(r.Valor.IdContraAsiento);
        Assert.NotNull(await new MotorAsientosAccess(Bd).ObtenerAsync(original));
        Assert.Equal(3, await Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'compras/factura-compra'"));
        Assert.Equal(-176m, Convert.ToDecimal(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT ImporteTotalFactura FROM IVASoportado WHERE IdAsiento = ?", r.Valor.IdContraAsiento))));

        // Re-exportar de nuevo en el cuarto trimestre regenera el contra-asiento y el asiento nuevo, sin añadir más.
        var h = await RecargarAsync(f.IdRegistro);
        h.Concepto = "Otra vez";
        var otra = await servicio.ExportarAsync(h, _actor, guardarCambios: true);
        Assert.True(otra.EsCorrecto, otra.Error?.ToString());
        Assert.Equal(3, await Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'compras/factura-compra'"));
        Assert.Equal(3, await Contar("SELECT COUNT(*) FROM IVASoportado"));

        // Deshacer en el cuarto: quita el asiento nuevo y deja el contra-asiento del original.
        var d = await servicio.DeshacerAsync(await RecargarAsync(f.IdRegistro), _actor);
        Assert.True(d.EsCorrecto, d.Error?.ToString());
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'compras/factura-compra'"));
        Assert.Equal(0m, Convert.ToDecimal(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT SUM(ImporteTotalFactura) FROM IVASoportado"))));
        Assert.False((await RecargarAsync(f.IdRegistro)).Contabilizada);
    }

    [Fact]
    public async Task Sin_cuenta_de_gasto_o_con_la_cuenta_de_iva_sin_configurar_no_exporta_nada()
    {
        await EmpresaAsync();
        var (f, _) = await FacturaAsync();
        f.CuentaGasto = null;
        f.Desgloses.Add(new DesgloseFacturaCompra(3, 4m, 10m, 0.4m));
        f.BaseImponible = 160m;
        f.CuotaIva = 26.4m;
        f.ImporteTotal = 186.4m;
        var c = await Servicio().ComprobarAsync(f);
        Assert.False(c.Lista);
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("cuenta de gasto"));
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("IVA soportado del 4"));
        Assert.False((await Servicio().ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: false))).EsCorrecto);
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM Asientos"));
    }

    [Fact]
    public async Task Hasta_tres_centimos_de_descuadre_se_ajustan_y_mas_pide_motivo()
    {
        await EmpresaAsync();
        var (f, _) = await FacturaAsync(total: 176.02m);
        var servicio = Servicio();
        var r = await servicio.ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: false));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Apuntes WHERE CuentaContable = '6690000' AND IdAsiento = ?", r.Valor.IdAsiento));

        var (g, _) = await FacturaAsync(total: 180m, md5Letra: "b");
        Assert.Equal(PreguntaExportacion.MotivoAjuste, (await servicio.ComprobarAsync(g)).Pregunta);
        var conMotivo = await servicio.ExportarAsync(g, _actor, new OpcionesExportacion(MotivoAjuste: "Portes sin desglosar"));
        Assert.True(conMotivo.EsCorrecto, conMotivo.Error?.ToString());
    }
}
