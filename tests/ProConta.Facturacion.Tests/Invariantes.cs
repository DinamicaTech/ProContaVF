using ProConta.Contabilidad.Asientos;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Tests;

/// <summary>
/// Invariantes contables que comprueba toda prueba de facturación a la que apliquen (docs/global-rules.md, Project rules):
/// el asiento cuadra; bases más impuestos, menos retención, más suplidos, es el total; y las facturas numeradas de cada
/// serie no dejan huecos.
/// </summary>
internal static class Invariantes
{
    public static void Totales(TotalesFactura t)
    {
        Assert.Equal(t.ImporteTotal.Valor, t.BaseImponible.Valor + t.Iva.Valor - t.Retencion.Valor + t.Suplidos.Valor);
        Assert.Equal(t.ImporteBruto.Valor, t.BaseImponible.Valor + t.Iva.Valor + t.Suplidos.Valor);
    }

    public static void Factura(FacturaVenta f)
    {
        Totales(f.Totales);
        foreach (var l in f.Lineas)
            Assert.Equal(l.ImporteTotal.Valor, l.BaseImponible.Valor + l.CuotaIva.Valor + l.CuotaRecargo.Valor);
    }

    public static void Asiento(Asiento a)
    {
        Assert.Equal(a.TotalDebe, a.TotalHaber);
        Assert.Equal(a.TotalDebe.Valor, a.Apuntes.Sum(x => x.Debe.Valor));
    }

    /// <summary>Todas las comprobaciones sobre lo que hay en la BD: facturas numeradas, sus asientos y sus series.</summary>
    public static async Task EnBaseDeDatosAsync(IBaseDatos bd, ServicioFacturas servicio)
    {
        var ids = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdFactura FROM Facturas WHERE NumeroFactura IS NOT NULL", r => r.Entero("IdFactura")));
        foreach (var id in ids) Factura((await servicio.ObtenerAsync(id))!);

        var asientos = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT a.IdAsiento, a.TotalDebe, a.TotalHaber, SUM(p.ImporteDebe) AS Debe, SUM(p.ImporteHaber) AS Haber FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
            "WHERE a.MotorPropietario = ? GROUP BY a.IdAsiento, a.TotalDebe, a.TotalHaber", r => (r.Decimal("TotalDebe"), r.Decimal("TotalHaber"), r.Decimal("Debe"), r.Decimal("Haber")),
            AsientoVenta.MotorPropietario));
        foreach (var (totalDebe, totalHaber, debe, haber) in asientos)
        {
            Assert.Equal(debe, haber);
            Assert.Equal(totalDebe, debe);
            Assert.Equal(totalHaber, haber);
        }

        var series = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdSerie, RangoDesde, UltimoNumero FROM SeriesFacturacion", r => (Id: r.Entero("IdSerie"), Desde: r.Entero("RangoDesde"), Ultimo: r.EsNulo("UltimoNumero") ? (int?)null : r.Entero("UltimoNumero"))));
        foreach (var serie in series)
        {
            var numeros = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT NumeroFactura FROM Facturas WHERE IdSerie = ? AND NumeroFactura IS NOT NULL", r => r.Texto("NumeroFactura"), serie.Id));
            Assert.Equal(numeros.Count, numeros.Distinct().Count());
            Assert.Equal(serie.Ultimo is int u ? u - serie.Desde + 1 : 0, numeros.Count);
        }
    }
}
