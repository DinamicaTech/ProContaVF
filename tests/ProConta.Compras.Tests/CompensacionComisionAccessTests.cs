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
/// Compensación de la comisión de las plataformas (facturacion/reservas/compensacion-comision) contra una copia con datos sintéticos: al exportar la
/// factura de comisión de un proveedor que es también el cliente de una regla de venta, su 410 se compensa contra su 430 con un asiento propio, que
/// re-exportar no duplica, deshacer quita y un trimestre cerrado no toca (contra-asiento en el lote siguiente).
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CompensacionComisionAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly RelojFijo _reloj = new(new DateTime(2026, 8, 20, 10, 0, 0));

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

    private CompensacionComision Compensacion() => new(Bd, new MotorAsientosAccess(Bd, reloj: _reloj), _reloj);

    private Task TrimestreAsync(int trimestre) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = ?", trimestre, 9001));

    private async Task EmpresaAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", NifSociedad);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.TiposIvaAsync(Bd, 21m);
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE SociedadesEjercicios SET TrimestreActivo = ?, CuentaRedondeoGastos = ?, CuentaRedondeoIngresos = ?, CuentaIVAInversionSujetoPasivoSoportado = ?, CuentaIVAInversionSujetoPasivoRepercutido = ? " +
            "WHERE IdSociedadEjercicio = ?", 3, "6690000", "7690000", "4720021", "4770021", 9001));
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre, imputable) in new[]
        {
            ("400", "Proveedores", false), ("410", "Acreedores", false), ("430", "Clientes", false), ("6230000", "Comisiones", true),
            ("4720021", "IVA soportado 21 %", true), ("4770021", "IVA repercutido 21 %", true), ("6690000", "Redondeo negativo", true), ("7690000", "Redondeo positivo", true),
            ("4300001", "Plataforma Ficticia Ltd.", true),
        })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: imputable))).EsCorrecto);
    }

    /// <summary>
    /// Acreedor sintético (410) y, si se pide, el rol de cliente del mismo tercero con la 4300001 y una regla de venta que lo usa: una plataforma.
    /// </summary>
    private async Task<ProveedorCreado> PlataformaAsync(bool esPlataforma = true, bool reglaActiva = true)
    {
        var cuentas = new CatalogoCuentasAccess(Bd);
        var roles = new ServicioRoles(Bd, cuentas);
        var alta = new ServicioAltaProveedorCompra(Bd, new RepositorioTercerosAccess(Bd), roles, cuentas, null, _actor);
        var prov = await alta.AltaAsync(_ctx, new DatosAltaProveedor(NifProveedor, "Plataforma Ficticia Ltd."));
        Assert.True(prov.EsCorrecto, prov.Error?.ToString());
        if (!esPlataforma) return prov.Valor;
        var cliente = await roles.AsegurarClienteAsync(prov.Valor.IdTercero);
        Assert.True(cliente.EsCorrecto, cliente.Error?.ToString());
        var identidad = (await cuentas.ResolverAsync(_ctx, "4300001")).Valor.IdIdentidadContable;
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = ?", cliente.Valor, "T01");
            await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, Clase, IdIdentidadContable) VALUES (?, ?, ?, ?)", cliente.Valor, "T01", "CLIENTE", identidad);
            await s.EjecutarAsync("INSERT INTO OrigenesVenta (IdSociedad, Codigo, Nombre, TextosImportacion, EsDirecta, Activo) VALUES (?, ?, ?, ?, ?, ?)",
                "T01", "FICTICIA", "Ficticia", "Ficticia", false, true);
            var origen = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdOrigenVenta) FROM OrigenesVenta"));
            await s.EjecutarAsync("INSERT INTO ReglasVenta (IdSociedad, IdOrigenVenta, IdCliente, Activa, Agrupar, AlFacturar) VALUES (?, ?, ?, ?, ?, ?)",
                "T01", origen, cliente.Valor, reglaActiva, "MENSUAL", "BORRADOR");
            return true;
        });
        return prov.Valor;
    }

    /// <summary>Factura de comisión con inversión del sujeto pasivo, como las de las plataformas: a la 410 va el total.</summary>
    private async Task<FacturaCompra> FacturaAsync(ProveedorCreado prov, decimal total = 66.19m, string letra = "a", DateTime? fecha = null)
    {
        var f = new FacturaCompra
        {
            Ejercicio = _ctx, RutaPdf = @$"X:\Soc\2026\Proveedores\Ficticia\{letra}.pdf", Md5 = new string(letra[0], 32), Carpeta = "Ficticia",
            IdProveedor = prov.IdProveedor, CuentaContableProveedor = prov.Cuenta, NifProveedor = NifProveedor, NumeroFactura = "COM-" + letra,
            Fecha = fecha ?? new DateTime(2026, 7, 25), Concepto = "Comisión", BaseImponible = total, CuotaIva = 0m, ImporteTotal = total,
            CuentaGasto = "6230000", Producto = ProductoCompra.Servicios, InversionSujetoPasivo = true,
        };
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 21m, total, 0m));
        var id = await Bd.EnTransaccionAsync(s => RepositorioFacturasCompra.InsertarAsync(s, f, _reloj.Ahora));
        Assert.True(id.EsCorrecto, id.Error?.ToString());
        return (await Bd.EjecutarAsync(s => RepositorioFacturasCompra.CargarAsync(s, id.Valor)))!;
    }

    private Task<FacturaCompra> RecargarAsync(int id) => Bd.EjecutarAsync(async s => (await RepositorioFacturasCompra.CargarAsync(s, id))!);

    private async Task<decimal> SaldoAsync(string cuenta) => Convert.ToDecimal(await Bd.EjecutarAsync(s => s.EscalarAsync(
        "SELECT IIf(IsNull(SUM(p.ImporteDebe)), 0, SUM(p.ImporteDebe)) - IIf(IsNull(SUM(p.ImporteHaber)), 0, SUM(p.ImporteHaber)) FROM Apuntes AS p INNER JOIN Asientos AS a " +
        "ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND p.CuentaContable = ?", 9001, cuenta)));

    private async Task<IReadOnlyList<int>> CompensacionesAsync(int idRegistro) => await Bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT IdAsiento FROM Asientos WHERE MotorPropietario = ? AND CodigoProceso = ? AND (ClaveEjecucion = ? OR ClaveEjecucion LIKE ?) ORDER BY IdAsiento",
        f => f.Entero("IdAsiento"), AsientoCompra.MotorPropietario, CompensacionComision.CodigoProceso, idRegistro.ToString(), idRegistro + ".%"));

    private async Task<FacturaCompra> ExportarAsync(FacturaCompra f, string? contiene = null)
    {
        var r = await Servicio().ExportarAsync(f, _actor, new OpcionesExportacion(EsProveedor: false));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        if (contiene is not null) Assert.Contains(contiene, r.Valor.Aviso, StringComparison.Ordinal);
        return await RecargarAsync(f.IdRegistro);
    }

    [Fact]
    public async Task Exportar_la_comision_de_una_plataforma_compensa_su_410_contra_su_430_con_la_plantilla_de_sistema()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync();
        var f = await ExportarAsync(await FacturaAsync(prov), "He compensado su 410 contra la 4300001");

        var ids = await CompensacionesAsync(f.IdRegistro);
        var asiento = (await new MotorAsientosAccess(Bd).ObtenerAsync(Assert.Single(ids)))!;
        Assert.Equal(new DateTime(2026, 7, 25), asiento.FechaContable);
        Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        Assert.Equal(66.19m, asiento.Apuntes.Single(a => a.CodigoCuenta == prov.Cuenta).Debe.Valor);
        Assert.Equal(66.19m, asiento.Apuntes.Single(a => a.CodigoCuenta == "4300001").Haber.Valor);
        Assert.Equal("Compensación comisión Plataforma Ficticia Ltd. fra. COM-a", asiento.Descripcion);
        Assert.All(asiento.Apuntes, a => Assert.Equal("COM-a", a.NumeroDocumento));
        // Con la plantilla de sistema que siembra la migración 20261116_1700_COMPENSACION_COMISION.
        Assert.NotNull(asiento.IdPlantilla);
        Assert.Equal(0m, await SaldoAsync(prov.Cuenta));
        Assert.Equal(-66.19m, await SaldoAsync("4300001"));
        Assert.Empty(await Compensacion().PendientesAsync("T01", 9001));
    }

    [Fact]
    public async Task Re_exportar_no_duplica_la_compensacion_y_la_ajusta_al_nuevo_importe()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync();
        var f = await ExportarAsync(await FacturaAsync(prov));
        var primera = Assert.Single(await CompensacionesAsync(f.IdRegistro));

        f = await ExportarAsync(f);
        Assert.Equal(primera, Assert.Single(await CompensacionesAsync(f.IdRegistro)));

        f.BaseImponible = f.ImporteTotal = 70m;
        f.Desgloses.Clear();
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 21m, 70m, 0m));
        f = await ExportarAsync(f, "70,00 €");
        Assert.Equal(primera, Assert.Single(await CompensacionesAsync(f.IdRegistro)));
        Assert.Equal(0m, await SaldoAsync(prov.Cuenta));
        Assert.Equal(-70m, await SaldoAsync("4300001"));
    }

    [Fact]
    public async Task Deshacer_la_exportacion_quita_la_compensacion()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync();
        var f = await ExportarAsync(await FacturaAsync(prov));
        var r = await Servicio().DeshacerAsync(f, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Contains("He quitado su compensación", r.Valor.Aviso, StringComparison.Ordinal);
        Assert.Empty(await CompensacionesAsync(f.IdRegistro));
        Assert.Equal(0m, await SaldoAsync(prov.Cuenta));
        Assert.Equal(0m, await SaldoAsync("4300001"));
    }

    [Fact]
    public async Task Un_proveedor_que_no_es_plataforma_no_se_compensa()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync(esPlataforma: false);
        var f = await ExportarAsync(await FacturaAsync(prov));
        Assert.Empty(await CompensacionesAsync(f.IdRegistro));
        Assert.Equal(-66.19m, await SaldoAsync(prov.Cuenta));
    }

    [Fact]
    public async Task Si_la_compensacion_es_de_un_trimestre_cerrado_no_se_toca_y_el_lote_siguiente_la_anula()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync();
        var f = await ExportarAsync(await FacturaAsync(prov));
        var primera = Assert.Single(await CompensacionesAsync(f.IdRegistro));

        // Pasa al 4T: el asiento de la factura y su compensación (25/07) quedan en un trimestre cerrado.
        await TrimestreAsync(4);
        f.BaseImponible = f.ImporteTotal = 70m;
        f.Desgloses.Clear();
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 21m, 70m, 0m));
        f = await ExportarAsync(f);
        var ids = await CompensacionesAsync(f.IdRegistro);
        Assert.Equal(3, ids.Count);
        Assert.Equal(primera, ids[0]);
        var motor = new MotorAsientosAccess(Bd);
        Assert.Equal(new DateTime(2026, 7, 25), (await motor.ObtenerAsync(primera))!.FechaContable);
        var contra = (await motor.ObtenerAsync(ids[1]))!;
        Assert.StartsWith(CompensacionComision.PrefijoAnulacion, contra.Descripcion, StringComparison.Ordinal);
        Assert.Equal(new DateTime(2026, 10, 1), contra.FechaContable);
        Assert.Equal(0m, await SaldoAsync(prov.Cuenta));
        Assert.Equal(-70m, await SaldoAsync("4300001"));

        // Repetir no cambia nada.
        f = await ExportarAsync(f);
        Assert.Equal(ids, await CompensacionesAsync(f.IdRegistro));
    }

    [Fact]
    public async Task Las_pendientes_se_compensan_desde_el_aviso_de_inicio()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync(reglaActiva: false);
        var f1 = await ExportarAsync(await FacturaAsync(prov, 66.19m, "a"));
        var f2 = await ExportarAsync(await FacturaAsync(prov, 33.81m, "b", new DateTime(2026, 7, 28)));
        Assert.Empty(await CompensacionesAsync(f1.IdRegistro));
        Assert.Empty(await Compensacion().PendientesAsync("T01", 9001));

        // La regla de venta se activa después (como las facturas que contabilizó el ERP): quedan dos pendientes.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ReglasVenta SET Activa = True"));
        var pendientes = await Compensacion().PendientesAsync("T01", 9001);
        Assert.Equal(2, pendientes.Count);
        Assert.Equal(100m, pendientes.Sum(p => p.Importe));
        Assert.All(pendientes, p => Assert.Equal("Plataforma Ficticia Ltd.", p.Plataforma));

        var r = await Compensacion().CompensarPendientesAsync("T01", 9001, _actor);
        Assert.Equal(2, r.Hechas);
        Assert.Equal(100m, r.Importe);
        Assert.Empty(r.Problemas);
        Assert.Single(await CompensacionesAsync(f1.IdRegistro));
        Assert.Single(await CompensacionesAsync(f2.IdRegistro));
        Assert.Equal(0m, await SaldoAsync(prov.Cuenta));
        Assert.Empty(await Compensacion().PendientesAsync("T01", 9001));
        Assert.Equal(0, (await Compensacion().CompensarPendientesAsync("T01", 9001, _actor)).Hechas);
    }

    [Fact]
    public async Task Sin_cuenta_430_en_el_ejercicio_exporta_igual_y_el_aviso_dice_que_corregir()
    {
        await EmpresaAsync();
        var prov = await PlataformaAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM CuentasClientesSociedad"));
        var f = await ExportarAsync(await FacturaAsync(prov), "no tiene cuenta 430");
        Assert.True(f.Contabilizada);
        Assert.Empty(await CompensacionesAsync(f.IdRegistro));
        Assert.Single(await Compensacion().PendientesAsync("T01", 9001));
    }
}
