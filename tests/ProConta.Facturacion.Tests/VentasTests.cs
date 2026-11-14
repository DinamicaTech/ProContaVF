using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Ventas;

namespace ProConta.Facturacion.Tests;

/// <summary>Reglas sin BD de las ventas por facturar (facturacion/reservas): agrupación, líneas exactas y lectura de Avaibook.</summary>
public sealed class VentasTests
{
    private static readonly OrigenVenta Airbnb = new(1, "T01", "AIRBNB", "Airbnb", 57, "Airbnb", false, true);
    private static readonly OrigenVenta Booking = new(2, "T01", "BOOKING", "Booking", 58, "Booking.com;Booking", false, true);
    private static readonly OrigenVenta Directa = new(3, "T01", "DIRECTA", "Directa", null, null, true, true);
    private static readonly Dictionary<int, OrigenVenta> Origenes = new() { [1] = Airbnb, [2] = Booking, [3] = Directa };
    private static readonly Dictionary<int, string> Clientes = new() { [57] = "CLIENTES AIRBNB", [58] = "CLIENTES BOOKING", [99] = "Huésped que pide factura" };
    private static readonly Dictionary<int, ActivoVenta> Activos = new() { [18] = new(18, "Castelldefels 133", 34), [19] = new(19, "Cádiz 3", 35) };
    private static readonly Concepto Alquiler = new(10, "T01", null, "Alquiler turístico", null, 1, "7520002", "Alquiler turístico", true, null, 3, 0m, 0m, true, null);
    private static readonly Dictionary<int, Concepto> Conceptos = new() { [10] = Alquiler };

    private static int _id;

    private static VentaPorFacturar Venta(int origen, DateTime entrada, decimal importe, int noches = 3, int? piso = 18, int? cliente = null) => new VentaPorFacturar
    {
        IdSociedad = "T01",
        IdOrigenVenta = origen,
        IdCliente = cliente,
        IdActivo = piso,
        IdConcepto = 10,
        FechaReferencia = entrada,
        FechaFin = entrada.AddDays(noches),
        Cantidad = noches,
        Importe = importe,
        CausaExencion = "E1",
    }.ConId(++_id);

    [Fact]
    public void AgrupaPorClienteMesYPisoConUnaLineaPorVenta()
    {
        var ventas = new[]
        {
            Venta(1, new DateTime(2026, 1, 2), 1391m, 11),
            Venta(1, new DateTime(2026, 1, 13), 822m, 7),
            Venta(1, new DateTime(2026, 1, 21), 338m),
            Venta(2, new DateTime(2026, 1, 18), 272.8m, 2, piso: 19),
            Venta(1, new DateTime(2026, 2, 1), 500m),                 // otro mes
            Venta(1, new DateTime(2026, 1, 25), 300m, piso: 19),      // otro piso
            Venta(1, new DateTime(2026, 1, 26), 450m, cliente: 99),   // el huésped pide factura a su nombre
        };
        var grupos = AgrupadorVentas.Agrupar(ventas, Origenes, Clientes, Activos, Conceptos);

        Assert.All(grupos, g => Assert.True(g.Facturable, g.Problema));
        Assert.Equal(5, grupos.Count);
        var enero = grupos.Single(g => g.Clave == new ClaveFacturaVentas(57, 2026, 1, 18));
        Assert.Equal(3, enero.Ventas.Count);
        Assert.Equal(2551m, enero.Total);
        Assert.Single(grupos, g => g.Clave!.IdCliente == 99);
        Assert.Single(grupos, g => g.Clave!.IdCliente == 58);
        Assert.Equal("Airbnb · enero 2026 · Castelldefels 133", AgrupadorVentas.DescripcionFactura(enero, Origenes));
    }

    [Fact]
    public void SinClienteNiConceptoNoSeFacturaYDiceQueCorregir()
    {
        var sinCliente = Venta(3, new DateTime(2026, 1, 5), 2000m);
        var sinConcepto = Venta(1, new DateTime(2026, 1, 6), 100m);
        sinConcepto.IdConcepto = null;
        var grupos = AgrupadorVentas.Agrupar([sinCliente, sinConcepto], Origenes, Clientes, Activos, Conceptos);
        Assert.Equal(2, grupos.Count);
        Assert.All(grupos, g => Assert.False(g.Facturable));
        Assert.Contains(grupos, g => g.Problema!.Contains("No tiene cliente"));
        Assert.Contains(grupos, g => g.Problema!.Contains("No tiene concepto"));
    }

    [Fact]
    public void LasYaFacturadasOAnuladasNoEntran()
    {
        var anulada = Venta(1, new DateTime(2026, 1, 2), 100m);
        anulada.Anulada = true;
        var erp = Venta(1, new DateTime(2026, 1, 3), 100m);
        erp.FacturadaFuera = true;
        Assert.Empty(AgrupadorVentas.Agrupar([anulada, erp], Origenes, Clientes, Activos, Conceptos));
    }

    [Theory]
    [InlineData(1391, 11)]
    [InlineData(822, 7)]
    [InlineData(1000, 3)]
    [InlineData(1784, 9)]
    [InlineData(2000, 1500)]
    public void LaLineaConservaElImporteExactoDeLaVenta(decimal importe, int noches)
    {
        var v = Venta(1, new DateTime(2026, 1, 2), importe, noches);
        var l = AgrupadorVentas.Linea(v, Alquiler, Activos[18], Airbnb);
        Assert.Equal(importe, l.ImporteTotal.Valor);
        Assert.Equal(importe, l.BaseImponible.Valor);
        Assert.Equal(0m, l.TipoIva);
        Assert.Equal("E1", l.CausaExencion);
        Assert.Equal(34, l.IdCentroCoste);
        Assert.Equal(noches, l.Cantidad);
        Assert.Equal("7520002", l.CuentaContable);
    }

    [Fact]
    public void ConIvaElTotalEsLoQuePagaElHuesped()
    {
        var v = Venta(1, new DateTime(2026, 1, 2), 1100m, 4);
        v.CausaExencion = null;
        v.TipoIva = 10m;
        var l = AgrupadorVentas.Linea(v, Alquiler, null, Airbnb);
        Assert.Equal(1100m, l.ImporteTotal.Valor);
        Assert.Equal(1000m, l.BaseImponible.Valor);
        Assert.Equal(100m, l.CuotaIva.Valor);
    }

    [Fact]
    public void LaFacturaCuadra()
    {
        var ventas = new[] { Venta(1, new DateTime(2026, 1, 2), 1391m, 11), Venta(1, new DateTime(2026, 1, 13), 822m, 7), Venta(1, new DateTime(2026, 1, 30), 238m, 2) };
        var f = new FacturaVenta();
        foreach (var v in ventas) f.AnadirLinea(AgrupadorVentas.Linea(v, Alquiler, Activos[18], Airbnb));
        Assert.Equal(2451m, f.Totales.ImporteTotal.Valor);
        Assert.Equal(f.Totales.BaseImponible.Valor + f.Totales.Iva.Valor - f.Totales.Retencion.Valor + f.Totales.Suplidos.Valor, f.Totales.ImporteTotal.Valor);
    }

    [Fact]
    public void LaDescripcionDeLaLineaNoLlevaElHuesped()
    {
        var v = Venta(1, new DateTime(2026, 1, 2), 1391m, 11);
        v.Referencia = "HM4JR5TNCD";
        v.Titular = "Nombre Del Huésped";
        var d = AgrupadorVentas.DescripcionLinea(v, Airbnb);
        Assert.Equal("Airbnb · 02/01/2026 – 13/01/2026 · 11 noches · reserva HM4JR5TNCD", d);
        Assert.DoesNotContain("Huésped", d);
    }

    [Fact]
    public void OrigenReconoceLosTextosDeImportacion()
    {
        Assert.True(Booking.Reconoce("Booking.com"));
        Assert.True(Booking.Reconoce("booking"));
        Assert.True(Airbnb.Reconoce("Airbnb"));
        Assert.False(Airbnb.Reconoce("Booking.com"));
        Assert.False(Directa.Reconoce(null));
    }

    [Fact]
    public void LeeLasReservasDeLaApiDeAvaibook()
    {
        const string json = """
            [{"id":"1","externalId":"17118951_HM4JR5TNCD","status":"CONFIRMED","origin":"PARTNER","partnerName":"Airbnb","checkInDate":"2026-01-02","checkOutDate":"2026-01-13",
              "accommodationId":377813,"accommodationName":"Apartamento Playa","totalAmount":1391,"partnerFee":215.61,"avaibookFee":0,"travellerName":"X"},
             {"id":"2","externalId":"5794801287_5562094711","status":"CANCELLED","origin":"PARTNER","partnerName":"Booking.com","checkInDate":"2026-01-23","checkOutDate":"2026-01-27",
              "accommodationId":377814,"totalAmount":597,"partnerFee":89.55},
             {"id":"3","status":"PENDING_OWNER","origin":"OWNER_WEB","checkInDate":"2026-03-01","checkOutDate":"2026-03-02","accommodationId":377814,"totalAmount":100}]
            """;
        var r = ClienteAvaibookHttp.Leer(json);
        Assert.Equal(3, r.Count);
        Assert.Equal("17118951_HM4JR5TNCD", r[0].Clave);
        Assert.Equal(11, r[0].Noches);
        Assert.Equal(1391m, r[0].TotalAmount);
        Assert.Equal(215.61m, r[0].PartnerFee);
        Assert.Equal("377813", r[0].AccommodationId);
        Assert.True(r[1].Cancelada && r[1].Firme);
        Assert.False(r[2].Firme);
        Assert.Equal("AB-3", r[2].Clave);
        Assert.NotEqual(r[0].Huella(), (r[0] with { TotalAmount = 1400m }).Huella());
        Assert.Equal(r[0].Huella(), (r[0] with { TravellerName = "Otro" }).Huella());
    }
}

internal static class VentasPrueba
{
    public static VentaPorFacturar ConId(this VentaPorFacturar v, int id)
    {
        typeof(VentaPorFacturar).GetProperty(nameof(VentaPorFacturar.IdVentaPorFacturar))!.SetValue(v, id);
        return v;
    }
}
