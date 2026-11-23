using ProConta.Compras.Proveedores;
using ProConta.Compras.Recurrentes;
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
/// Facturas recurrentes pendientes contra una copia con datos sintéticos (compras/facturas-recurrentes-pendientes): lee las facturas de la sociedad
/// de los últimos 12 meses de cualquier ejercicio (también las del ERP sin IdSociedadEjercicio), «No llegará» y «Volver a esperarla» persisten, y la
/// periodicidad de la ficha del proveedor se guarda con su historial y manda.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class FacturasRecurrentesAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private static readonly DateTime Hoy = new(2026, 11, 23);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private ServicioFacturasRecurrentes Servicio() => new(Bd, () => Hoy);

    private async Task<ProveedorCreado> PreparaAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", NifSociedad);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2025);
        var cuentas = new CatalogoCuentasAccess(Bd);
        Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta("400", "Proveedores", EsImputable: false))).EsCorrecto);
        Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta("410", "Acreedores", EsImputable: false))).EsCorrecto);
        var alta = new ServicioAltaProveedorCompra(Bd, new RepositorioTercerosAccess(Bd), new ServicioRoles(Bd, cuentas), cuentas, null, _actor);
        var prov = await alta.AltaAsync(_ctx, new DatosAltaProveedor(NifProveedor, "Suministros Ficticios SL"));
        Assert.True(prov.EsCorrecto, prov.Error?.ToString());
        return prov.Valor;
    }

    /// <summary>Factura mínima: con ejercicio, o sin él (como las del ERP) con solo la sociedad.</summary>
    private Task FacturaAsync(int idProveedor, DateTime fecha, decimal total, int? idEjercicio, bool duplicada = false) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, IdProveedor, NumeroFactura, Fecha, ImporteTotal, Pagada, Exportada, Duplicada, InversionSujetoPasivo, RequiereRevisionImportacion, Version) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "T01", fecha.Year.ToString(), idEjercicio, idProveedor, $"F-{fecha:yyyyMMdd}", fecha, total, false, true, duplicada, false, false, 0));

    [Fact]
    public async Task Lee_las_facturas_de_varios_ejercicios_y_persiste_los_descartes_y_la_periodicidad_de_la_ficha()
    {
        var prov = await PreparaAsync();
        // Mensual: diciembre de 2025 (del ERP, sin ejercicio), enero y febrero de 2026; la de marzo es duplicada y no cuenta.
        await FacturaAsync(prov.IdProveedor, new DateTime(2025, 12, 5), 60.50m, null);
        await FacturaAsync(prov.IdProveedor, new DateTime(2026, 1, 5), 60.50m, 9001);
        await FacturaAsync(prov.IdProveedor, new DateTime(2026, 2, 5), 61.25m, 9001);
        await FacturaAsync(prov.IdProveedor, new DateTime(2026, 3, 5), 61.25m, 9001, duplicada: true);

        var pendientes = await Servicio().PendientesAsync("T01");
        Assert.Equal(9, pendientes.Count); // marzo a noviembre de 2026
        Assert.All(pendientes, p =>
        {
            Assert.Equal(PeriodicidadFacturas.Mensual, p.Periodicidad);
            Assert.Equal(61.25m, p.ImporteEstimado);
            Assert.Equal("Suministros Ficticios SL", p.Proveedor);
            Assert.Equal(prov.IdTercero, p.IdTercero);
        });
        Assert.Equal(new DateTime(2026, 3, 1), pendientes[0].Periodo);

        // «No llegará» en marzo y abril: no salen, ni al repetirlo se duplican; se ven con «Mostrar descartadas».
        var marzoAbril = new List<(int, DateTime)> { (prov.IdProveedor, new DateTime(2026, 3, 1)), (prov.IdProveedor, new DateTime(2026, 4, 15)) };
        Assert.Equal(2, await Servicio().DescartarAsync("T01", marzoAbril, _actor));
        Assert.Equal(0, await Servicio().DescartarAsync("T01", marzoAbril, _actor));
        Assert.Equal(7, (await Servicio().PendientesAsync("T01")).Count);
        var todas = await Servicio().PendientesAsync("T01", mostrarDescartadas: true);
        Assert.Equal(2, todas.Count(p => p.Descartada));

        // «Volver a esperarla» en abril.
        Assert.Equal(1, await Servicio().DeshacerDescarteAsync("T01", [(prov.IdProveedor, new DateTime(2026, 4, 1))]));
        Assert.Equal(8, (await Servicio().PendientesAsync("T01")).Count);

        // Ficha: trimestral, guardado con historial; luego «No controlar».
        var repo = new RepositorioTercerosAccess(Bd);
        var t = (await repo.ObtenerAsync(prov.IdTercero))!;
        Assert.Null(t.Roles.Proveedor!.PeriodicidadFacturas);
        t.Roles.Proveedor.PeriodicidadFacturas = PeriodicidadFacturas.Trimestral;
        var guardado = await repo.GuardarAsync(t, _actor);
        Assert.True(guardado.EsCorrecto, guardado.Error?.ToString());
        Assert.Equal(PeriodicidadFacturas.Trimestral, (await repo.ObtenerAsync(prov.IdTercero))!.Roles.Proveedor!.PeriodicidadFacturas);
        Assert.Contains(await repo.HistorialAsync(prov.IdTercero), m => m.Campo == "PeriodicidadFacturas" && m.ValorNuevo == "Trimestral");
        var trimestral = await Servicio().PendientesAsync("T01");
        Assert.Equal([new DateTime(2026, 5, 1), new DateTime(2026, 8, 1), new DateTime(2026, 11, 1)], trimestral.Select(p => p.Periodo));
        Assert.All(trimestral, p => Assert.True(p.PeriodicidadFijada));

        t = (await repo.ObtenerAsync(prov.IdTercero))!;
        t.Roles.Proveedor!.PeriodicidadFacturas = PeriodicidadFacturas.NoControlar;
        Assert.True((await repo.GuardarAsync(t, _actor)).EsCorrecto);
        Assert.Empty(await Servicio().PendientesAsync("T01"));
    }
}
