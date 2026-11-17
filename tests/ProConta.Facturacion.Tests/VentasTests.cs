using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Ventas;

namespace ProConta.Facturacion.Tests;

/// <summary>Reglas sin BD de las ventas por facturar (facturacion/reservas): reglas de venta, agrupación, líneas exactas y lectura de Avaibook.</summary>
public sealed class VentasTests
{
    private static readonly OrigenVenta Airbnb = new(1, "T01", "AIRBNB", "Airbnb", "Airbnb", false, true);
    private static readonly OrigenVenta Booking = new(2, "T01", "BOOKING", "Booking", "Booking.com;Booking", false, true);
    private static readonly OrigenVenta Directa = new(3, "T01", "DIRECTA", "Directa", null, true, true);
    private static readonly Dictionary<int, OrigenVenta> Origenes = new() { [1] = Airbnb, [2] = Booking, [3] = Directa };
    private static readonly Dictionary<int, string> Clientes = new() { [57] = "CLIENTES AIRBNB", [58] = "CLIENTES BOOKING", [99] = "Huésped que pide factura" };
    private static readonly Dictionary<int, ActivoVenta> Activos = new() { [18] = new(18, "Castelldefels 133", 34), [19] = new(19, "Cádiz 3", 35) };
    private static readonly Concepto Alquiler = new(10, "T01", null, "Alquiler turístico", null, 1, "7520002", "Alquiler turístico", true, null, 3, 0m, 0m, true, null);
    private static readonly Concepto Otro = new(11, "T01", null, "Alquiler de temporada", null, 2, "7520003", "Alquiler de temporada", true, null, null, 0m, 21m, true, null);
    private static readonly Dictionary<int, Concepto> Conceptos = new() { [10] = Alquiler, [11] = Otro };

    /// <summary>Airbnb y Booking exentos (E1) con su plataforma como cliente; Directa no tiene regla.</summary>
    private static readonly ReglaVenta[] Reglas =
    [
        new(1, "T01", 1, null, 10, "E1", null, 57, true),
        new(2, "T01", 2, null, 10, "E1", null, 58, true),
    ];

    private static DatosVentas Datos(IReadOnlyList<ReglaVenta>? reglas = null) => new(Origenes, reglas ?? Reglas, Clientes, Activos, Conceptos);

    private static int _id;

    /// <summary>Venta como la deja la importación: solo origen, activo, fechas e importe; lo demás lo pone la regla.</summary>
    private static VentaPorFacturar Venta(int origen, DateTime entrada, decimal importe, int noches = 3, int? activo = 18, int? cliente = null) => new VentaPorFacturar
    {
        IdSociedad = "T01",
        IdOrigenVenta = origen,
        IdCliente = cliente,
        IdActivo = activo,
        FechaReferencia = entrada,
        FechaFin = entrada.AddDays(noches),
        Cantidad = noches,
        Importe = importe,
    }.ConId(++_id);

    private static LineaFacturaVenta Linea(VentaPorFacturar v, IReadOnlyList<ReglaVenta>? reglas = null)
    {
        var res = AgrupadorVentas.Resolver(v, reglas ?? Reglas);
        return AgrupadorVentas.Linea(v, res, Conceptos[res.IdConcepto!.Value], v.IdActivo is int a ? Activos[a] : null, Origenes[v.IdOrigenVenta]);
    }

    [Fact]
    public void LaReglaPoneConceptoIvaYClienteSinTocarLaVenta()
    {
        var v = Venta(1, new DateTime(2026, 1, 2), 1391m, 11);
        var res = AgrupadorVentas.Resolver(v, Reglas);
        Assert.Equal(57, res.IdCliente);
        Assert.Equal(10, res.IdConcepto);
        Assert.Equal("E1", res.CausaExencion);
        Assert.Equal("Exenta E1", res.TextoIva);
        Assert.Null(v.IdConcepto);
        Assert.Null(v.IdCliente);
    }

    [Fact]
    public void LaReglaDelActivoGanaALaGeneralDelOrigen()
    {
        ReglaVenta[] reglas = [.. Reglas, new(3, "T01", 1, 19, 11, null, 10m, 99, true), new(4, "T01", 1, 18, 11, null, 21m, 99, false)];
        var cadiz = AgrupadorVentas.Resolver(Venta(1, new DateTime(2026, 1, 2), 100m, activo: 19), reglas);
        Assert.Equal((99, 11, null, 10m), (cadiz.IdCliente, cadiz.IdConcepto, cadiz.CausaExencion, cadiz.TipoIva));
        // La regla del activo 18 está inactiva: vale la general.
        var castelldefels = AgrupadorVentas.Resolver(Venta(1, new DateTime(2026, 1, 2), 100m, activo: 18), reglas);
        Assert.Equal((57, 10, "E1"), (castelldefels.IdCliente, castelldefels.IdConcepto, castelldefels.CausaExencion));
        Assert.Null(ReglaVenta.Para(reglas, 3, 18));
    }

    [Fact]
    public void LoPropioDeLaVentaGanaALaRegla()
    {
        var v = Venta(1, new DateTime(2026, 1, 2), 1100m, 4, cliente: 99);
        v.IdConcepto = 11;
        v.TipoIva = 10m;
        var res = AgrupadorVentas.Resolver(v, Reglas);
        Assert.Equal((99, 11, null, 10m), (res.IdCliente, res.IdConcepto, res.CausaExencion, res.TipoIva));
    }

    [Fact]
    public void CambiarElIvaDeLaReglaValeParaLoPendiente()
    {
        // El 01/12/2026 el alquiler turístico pasa de exento al 10 %: basta cambiar la regla; las ventas ya importadas no se tocan.
        var v = Venta(1, new DateTime(2026, 12, 5), 1100m, 4);
        Assert.Equal(1100m, Linea(v).BaseImponible.Valor);
        ReglaVenta[] alDiez = [Reglas[0] with { CausaExencion = null, TipoIva = 10m }, Reglas[1]];
        var l = Linea(v, alDiez);
        Assert.Null(l.CausaExencion);
        Assert.Equal(10m, l.TipoIva);
        Assert.Equal(1100m, l.ImporteTotal.Valor);
        Assert.Equal(1000m, l.BaseImponible.Valor);
        Assert.Equal(100m, l.CuotaIva.Valor);
    }

    [Fact]
    public void AgrupaPorClienteMesYActivoConUnaLineaPorVenta()
    {
        var ventas = new[]
        {
            Venta(1, new DateTime(2026, 1, 2), 1391m, 11),
            Venta(1, new DateTime(2026, 1, 13), 822m, 7),
            Venta(1, new DateTime(2026, 1, 21), 338m),
            Venta(2, new DateTime(2026, 1, 18), 272.8m, 2, activo: 19),
            Venta(1, new DateTime(2026, 2, 1), 500m),                 // otro mes
            Venta(1, new DateTime(2026, 1, 25), 300m, activo: 19),    // otro activo
            Venta(1, new DateTime(2026, 1, 26), 450m, cliente: 99),   // el huésped pide factura a su nombre
        };
        var grupos = AgrupadorVentas.Agrupar(ventas, Datos());

        Assert.All(grupos, g => Assert.True(g.Facturable, g.Problema));
        Assert.Equal(5, grupos.Count);
        var enero = grupos.Single(g => g.Clave == new ClaveFacturaVentas(57, 1, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), 18));
        Assert.Equal(3, enero.Ventas.Count);
        Assert.Equal(2551m, enero.Total);
        Assert.False(enero.Validar);
        Assert.Single(grupos, g => g.Clave!.IdCliente == 99);
        Assert.Single(grupos, g => g.Clave!.IdCliente == 58);
        Assert.Equal("Airbnb · enero 2026 · Castelldefels 133", enero.Descripcion);
    }

    [Theory]
    [InlineData(AgrupacionVentas.Semanal, "2026-01-14", "2026-01-12", "2026-01-18", "semana del 12/01/2026 al 18/01/2026")]
    [InlineData(AgrupacionVentas.Semanal, "2026-01-18", "2026-01-12", "2026-01-18", "semana del 12/01/2026 al 18/01/2026")]
    [InlineData(AgrupacionVentas.Quincenal, "2026-02-15", "2026-02-01", "2026-02-15", "1.ª quincena de febrero 2026")]
    [InlineData(AgrupacionVentas.Quincenal, "2026-02-16", "2026-02-16", "2026-02-28", "2.ª quincena de febrero 2026")]
    [InlineData(AgrupacionVentas.Mensual, "2026-12-31", "2026-12-01", "2026-12-31", "diciembre 2026")]
    [InlineData(AgrupacionVentas.Trimestral, "2026-05-20", "2026-04-01", "2026-06-30", "2.º trimestre de 2026")]
    [InlineData(AgrupacionVentas.Anual, "2026-05-20", "2026-01-01", "2026-12-31", "año 2026")]
    [InlineData("cualquier cosa", "2026-05-20", "2026-05-01", "2026-05-31", "mayo 2026")]
    public void CadaAgrupacionDaSuPeriodo(string agrupar, string fecha, string desde, string hasta, string texto)
    {
        var p = AgrupacionVentas.Periodo(agrupar, DateTime.Parse(fecha, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal((DateTime.Parse(desde, System.Globalization.CultureInfo.InvariantCulture), DateTime.Parse(hasta, System.Globalization.CultureInfo.InvariantCulture), texto), (p.Desde, p.Hasta, p.Texto));
    }

    [Fact]
    public void UnTrimestreMensualPorOrigenYActivoDaDoceFacturas()
    {
        // Dos orígenes × dos activos × tres meses, varias reservas en cada uno: 12 facturas.
        var ventas = new List<VentaPorFacturar>();
        foreach (var origen in new[] { 1, 2 })
            foreach (var activo in new[] { 18, 19 })
                for (var mes = 7; mes <= 9; mes++)
                    foreach (var dia in new[] { 3, 12, 25 })
                        ventas.Add(Venta(origen, new DateTime(2026, mes, dia), 300m, activo: activo));
        var grupos = AgrupadorVentas.Agrupar(ventas, Datos());
        Assert.Equal(12, grupos.Count);
        Assert.All(grupos, g => Assert.Equal(3, g.Ventas.Count));
    }

    [Fact]
    public void LaReglaDecideLaAgrupacionSiSeSeparaPorActivoYSiSeValida()
    {
        var ventas = new[]
        {
            Venta(1, new DateTime(2026, 7, 3), 300m, activo: 18), Venta(1, new DateTime(2026, 8, 12), 300m, activo: 19), Venta(1, new DateTime(2026, 9, 25), 300m, activo: 18),
        };
        // Trimestral, sin separar por activo y «facturar y exportar»: una sola factura validada con los dos activos.
        ReglaVenta[] trimestral = [Reglas[0] with { Agrupar = AgrupacionVentas.Trimestral, SepararPorActivo = false, AlFacturar = ReglaVenta.Validar }];
        var uno = Assert.Single(AgrupadorVentas.Agrupar(ventas, Datos(trimestral)));
        Assert.Equal(3, uno.Ventas.Count);
        Assert.True(uno.Validar);
        Assert.Null(uno.Clave!.IdActivo);
        Assert.Equal("Airbnb · 3.º trimestre de 2026", uno.Descripcion);
        Assert.Equal((new DateTime(2026, 7, 1), new DateTime(2026, 9, 30)), (uno.Clave.Desde, uno.Clave.Hasta));

        // Sin agrupar: una factura por venta, con el periodo de la propia estancia.
        ReglaVenta[] sinAgrupar = [Reglas[0] with { Agrupar = AgrupacionVentas.No, SepararPorActivo = false }];
        var sueltas = AgrupadorVentas.Agrupar(ventas, Datos(sinAgrupar));
        Assert.Equal(3, sueltas.Count);
        Assert.Equal("Airbnb · 03/07/2026 – 06/07/2026 · Castelldefels 133", sueltas[0].Descripcion);

        // Anual separando por activo: dos facturas.
        ReglaVenta[] anual = [Reglas[0] with { Agrupar = AgrupacionVentas.Anual }];
        Assert.Equal(2, AgrupadorVentas.Agrupar(ventas, Datos(anual)).Count);
    }

    [Fact]
    public void UnPortalNuevoSeDaDeAltaConNombreLimpioYCodigoLibre()
    {
        var vrbo = ImportadorAvaibook.OrigenNuevo("T01", "Vrbo [XML]", [Airbnb, Booking, Directa]);
        Assert.Equal(("VRBO", "Vrbo", "Vrbo [XML]", false, true), (vrbo.Codigo, vrbo.Nombre, vrbo.TextosImportacion, vrbo.EsDirecta, vrbo.Activo));
        Assert.True(vrbo.Reconoce("Vrbo [XML]"));
        Assert.Equal("AIRBNB2", ImportadorAvaibook.OrigenNuevo("T01", "Airbnb!", [Airbnb]).Codigo);
    }

    [Fact]
    public void SinReglaNoSeFacturaYDiceDondeConfigurarla()
    {
        var directa = Venta(3, new DateTime(2026, 1, 5), 2000m);
        var sinConcepto = Venta(1, new DateTime(2026, 1, 6), 100m);
        ReglaVenta[] reglas = [Reglas[0] with { IdConcepto = null }];
        var grupos = AgrupadorVentas.Agrupar([directa, sinConcepto], Datos(reglas));
        Assert.Equal(2, grupos.Count);
        Assert.All(grupos, g => Assert.False(g.Facturable));
        Assert.Contains(grupos, g => g.Problema!.Contains("No tiene cliente") && g.Problema.Contains("Reglas de venta"));
        Assert.Contains(grupos, g => g.Problema!.Contains("No tiene concepto") && g.Problema.Contains("Airbnb y Castelldefels 133"));
    }

    [Fact]
    public void LasYaFacturadasOAnuladasNoEntran()
    {
        var anulada = Venta(1, new DateTime(2026, 1, 2), 100m);
        anulada.Anulada = true;
        var erp = Venta(1, new DateTime(2026, 1, 3), 100m);
        erp.FacturadaFuera = true;
        Assert.Empty(AgrupadorVentas.Agrupar([anulada, erp], Datos()));
    }

    [Theory]
    [InlineData(1391, 11)]
    [InlineData(822, 7)]
    [InlineData(1000, 3)]
    [InlineData(1784, 9)]
    [InlineData(2000, 1500)]
    public void LaLineaConservaElImporteExactoDeLaVenta(decimal importe, int noches)
    {
        var l = Linea(Venta(1, new DateTime(2026, 1, 2), importe, noches));
        Assert.Equal(importe, l.ImporteTotal.Valor);
        Assert.Equal(importe, l.BaseImponible.Valor);
        Assert.Equal(0m, l.TipoIva);
        Assert.Equal("E1", l.CausaExencion);
        Assert.Equal(34, l.IdCentroCoste);
        Assert.Equal(18, l.IdActivo);
        Assert.Equal(noches, l.Cantidad);
        Assert.Equal("7520002", l.CuentaContable);
    }

    [Fact]
    public void SinIvaEnLaReglaValeElDelConcepto()
    {
        ReglaVenta[] reglas = [new(1, "T01", 1, null, 11, null, null, 57, true)];
        var l = Linea(Venta(1, new DateTime(2026, 1, 2), 1210m), reglas);
        Assert.Equal(21m, l.TipoIva);
        Assert.Equal(1000m, l.BaseImponible.Valor);
        Assert.Equal("El del concepto", AgrupadorVentas.Resolver(Venta(1, new DateTime(2026, 1, 2), 1m), reglas).TextoIva);
    }

    [Fact]
    public void LaFacturaCuadra()
    {
        var ventas = new[] { Venta(1, new DateTime(2026, 1, 2), 1391m, 11), Venta(1, new DateTime(2026, 1, 13), 822m, 7), Venta(1, new DateTime(2026, 1, 30), 238m, 2) };
        ReglaVenta[] alDiez = [Reglas[0] with { CausaExencion = null, TipoIva = 10m }];
        foreach (var reglas in new[] { Reglas, alDiez })
        {
            var f = new FacturaVenta();
            foreach (var v in ventas) f.AnadirLinea(Linea(v, reglas));
            Assert.Equal(2451m, f.Totales.ImporteTotal.Valor);
            Assert.Equal(f.Totales.BaseImponible.Valor + f.Totales.Iva.Valor - f.Totales.Retencion.Valor + f.Totales.Suplidos.Valor, f.Totales.ImporteTotal.Valor);
        }
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
