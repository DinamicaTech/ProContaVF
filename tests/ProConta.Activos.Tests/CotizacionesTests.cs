using System.Net;
using System.Text;
using ProConta.Activos.Financieros;

namespace ProConta.Activos.Tests;

/// <summary>Respuestas HTTP fijas por dirección, para probar los clientes de cotizaciones sin red.</summary>
internal sealed class HttpFijo(Func<string, (HttpStatusCode Codigo, string Cuerpo)?> responder) : HttpMessageHandler
{
    public List<string> Pedidas { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Pedidas.Add(url);
        var r = responder(url) ?? (HttpStatusCode.NotFound, "{}");
        return Task.FromResult(new HttpResponseMessage(r.Codigo) { Content = new StringContent(r.Cuerpo, Encoding.UTF8, "application/json") });
    }
}

/// <summary>
/// Servicio de cotizaciones de los activos financieros sin base de datos ni red (activos/activos-financieros 2026-11-23 20:32): ISIN, lectura de las
/// respuestas de Yahoo, EODHD y el BCE, orden de los proveedores (Yahoo; si no, EODHD; si no, aviso grave), precio de un día no hábil y redondeos.
/// </summary>
public sealed class CotizacionesTests
{
    private static readonly DateTime Hoy = new(2026, 11, 23);

    private static long Unix(DateTime d) => new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeSeconds();

    private const string BusquedaYahoo = """
        {"quotes":[
          {"exchange":"AMS","shortname":"ISHARES CORE MSCI WORLD","quoteType":"ETF","symbol":"IWDA.AS","longname":"iShares Core MSCI World UCITS ETF USD (Acc)","exchDisp":"Amsterdam"},
          {"exchange":"LSE","quoteType":"ETF","symbol":"SWDA.L","longname":"iShares Core MSCI World UCITS ETF USD (Acc)","exchDisp":"London"},
          {"exchange":"NYQ","quoteType":"OPTION","symbol":"XYZ","longname":"Opción"}
        ],"news":[]}
        """;

    /// <summary>Gráfico de Yahoo con cierres del 17 al 21/11/2026 (lunes a viernes) a 100, 101, … en la moneda dada.</summary>
    private static string Grafico(string simbolo, string moneda, decimal? ultimo = null, string? nombre = null)
    {
        var dias = Enumerable.Range(0, 5).Select(i => new DateTime(2026, 11, 17).AddDays(i)).ToList();
        // Las sesiones empiezan a las 8:00 UTC; con gmtoffset 3600 siguen siendo ese día.
        var ts = string.Join(",", dias.Select(d => Unix(d.AddHours(8))));
        var cierres = string.Join(",", dias.Select((_, i) => (100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".5"));
        var precio = ultimo is decimal u ? $"\"regularMarketPrice\":{u.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"regularMarketTime\":{Unix(Hoy.AddHours(10))}," : "";
        return "{\"chart\":{\"result\":[{\"meta\":{\"currency\":\"" + moneda + "\",\"symbol\":\"" + simbolo + "\",\"exchangeName\":\"AMS\",\"fullExchangeName\":\"Amsterdam\"," +
               (nombre is null ? "" : "\"longName\":\"" + nombre + "\",") + precio + "\"gmtoffset\":3600,\"instrumentType\":\"ETF\"},\"timestamp\":[" + ts + "],\"indicators\":{\"quote\":[{\"close\":[" + cierres + ",null]}]}}],\"error\":null}}";
    }

    private static ServicioCotizaciones Servicio(HttpFijo http, string? clave = null) =>
        new(new ClienteYahooFinance(new HttpClient(http)), new ClienteEodhd(new HttpClient(http)), new ServicioCambiosDivisa(null!, new ClienteBce(new HttpClient(http))),
            () => Task.FromResult(clave), () => Hoy);

    [Theory]
    [InlineData("IE00B4L5Y983")]
    [InlineData("ie00 b4l5 y983")]
    [InlineData("US0378331005")]
    [InlineData("ES0113900J37")]
    [InlineData("LU0996182563")]
    public void IsinValidos(string isin) => Assert.Null(Isin.Error(isin));

    [Theory]
    [InlineData("", "Escribe el ISIN")]
    [InlineData("IE00B4L5Y98", "12")]
    [InlineData("1E00B4L5Y983", "país")]
    [InlineData("IE00B4L5Y98X", "dígito de control, siempre una cifra")]
    [InlineData("IE00B4L5Y984", "no cuadra")]
    public void IsinNoValidosDicenPorQue(string isin, string texto) => Assert.Contains(texto, Isin.Error(isin));

    [Fact]
    public void LeeLaBusquedaDeYahooSinOpciones()
    {
        var r = ClienteYahooFinance.LeerBusqueda(BusquedaYahoo);
        Assert.Equal(["IWDA.AS", "SWDA.L"], r.Select(x => x.Simbolo));
        Assert.Equal("Amsterdam", r[0].Bolsa);
        Assert.Equal("ETF", r[0].Tipo);
        Assert.Equal("iShares Core MSCI World UCITS ETF USD (Acc)", r[0].Nombre);
    }

    [Fact]
    public void LeeElGraficoDeYahooPorDiaLocal()
    {
        var g = ClienteYahooFinance.LeerGrafico("IWDA.AS", Grafico("IWDA.AS", "EUR", 105.25m))!;
        Assert.Equal("EUR", g.Moneda);
        Assert.Equal(5, g.Cierres.Count); // el null del final no cuenta
        Assert.Equal((new DateTime(2026, 11, 21), 104.5m), g.Cierres[^1]);
        Assert.Equal(105.25m, g.UltimoPrecio);
        Assert.Equal(Hoy, g.FechaUltimoPrecio);
        Assert.Null(ClienteYahooFinance.LeerGrafico("X", """{"chart":{"result":null,"error":{"code":"Not Found"}}}"""));
    }

    [Fact]
    public void LeeEodhdYElCsvDelBce()
    {
        const string json = """
            [{"Code":"IWDA","Exchange":"AS","Name":"iShares Core MSCI World","Type":"ETF","Currency":"EUR","ISIN":"IE00B4L5Y983"},
             {"Code":"OTRO","Exchange":"US","Name":"Otro","Type":"Common Stock","Currency":"USD","ISIN":"US0000000000"}]
            """;
        var b = ClienteEodhd.LeerBusqueda(json, "IE00B4L5Y983");
        Assert.Equal("IWDA.AS", Assert.Single(b).Simbolo);
        Assert.Equal((98.12m, new DateTime(2026, 11, 20)), ClienteEodhd.LeerTiempoReal($$"""{"code":"IWDA.AS","timestamp":{{Unix(new DateTime(2026, 11, 20, 16, 0, 0))}},"close":98.12}"""));
        Assert.Null(ClienteEodhd.LeerTiempoReal("""{"code":"IWDA.AS","close":"NA"}"""));
        Assert.Equal([(new DateTime(2026, 11, 19), 97m), (new DateTime(2026, 11, 20), 98m)],
            ClienteEodhd.LeerCierres("""[{"date":"2026-11-20","close":98},{"date":"2026-11-19","close":97}]"""));
        var csv = "KEY,FREQ,CURRENCY,CURRENCY_DENOM,EXR_TYPE,EXR_SUFFIX,TIME_PERIOD,OBS_VALUE,OBS_STATUS\n" +
                  "EXR.D.USD.EUR.SP00.A,D,USD,EUR,SP00,A,2026-11-19,1.0512,A\nEXR.D.USD.EUR.SP00.A,D,USD,EUR,SP00,A,2026-11-20,1.0533,A\n";
        Assert.Equal([(new DateTime(2026, 11, 19), 1.0512m), (new DateTime(2026, 11, 20), 1.0533m)], ClienteBce.LeerCsv(csv));
    }

    [Fact]
    public async Task BuscaEnYahooYProponeLaQueCotizaEnEuros()
    {
        var http = new HttpFijo(url => url.Contains("/search?") ? (HttpStatusCode.OK, BusquedaYahoo)
            : url.Contains("chart/SWDA.L") ? (HttpStatusCode.OK, Grafico("SWDA.L", "GBp"))
            : url.Contains("chart/IWDA.AS") ? (HttpStatusCode.OK, Grafico("IWDA.AS", "EUR")) : null);
        var r = await Servicio(http, clave: "no-se-usa").BuscarPorIsinAsync("IE00B4L5Y983");
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(FuenteCotizacion.Yahoo, r.Valor.EncontradoEn);
        Assert.Null(r.Valor.Aviso);
        Assert.Equal(["IWDA.AS", "SWDA.L"], r.Valor.Cotizaciones.Select(c => c.TickerYahoo));
        Assert.Equal("IWDA.AS", r.Valor.Propuesta!.TickerYahoo);
        Assert.Equal("IWDA.AS", r.Valor.Propuesta.TickerEodhd);
        Assert.Equal("SWDA.LSE", r.Valor.Cotizaciones[1].TickerEodhd);
        Assert.DoesNotContain(http.Pedidas, u => u.Contains("eodhd.com")); // con Yahoo basta: no gasta consultas de EODHD
    }

    /// <summary>
    /// Respuestas reales de Yahoo del 24/11/2026 para IE00BK5BQT80: por ISIN solo salen VWRA.L (dólares) y un listado de Stuttgart sin nombre ni
    /// precios; por el nombre salen todos, también la clase de distribución (VWRL), que tiene otro nombre y no entra. Se propone VWCE.DE (Xetra).
    /// </summary>
    [Fact]
    public async Task BuscaPorElNombreLosListadosQueNoDaElIsinYProponeXetra()
    {
        const string porIsin = """
            {"quotes":[{"exchange":"LSE","quoteType":"ETF","symbol":"VWRA.L","longname":"Vanguard FTSE All-World UCITS ETF USD Accumulation","exchDisp":"London"},
              {"exchange":"STU","quoteType":"MUTUALFUND","symbol":"IE00BK5BQT80.SG","exchDisp":"Stuttgart"}],"news":[]}
            """;
        const string porNombre = """
            {"quotes":[{"exchange":"GER","quoteType":"ETF","symbol":"VWCE.DE","longname":"Vanguard FTSE All-World UCITS ETF USD Accumulation","exchDisp":"XETRA"},
              {"exchange":"LSE","quoteType":"ETF","symbol":"VWRA.L","longname":"Vanguard FTSE All-World UCITS ETF USD Accumulation","exchDisp":"London"},
              {"exchange":"AMS","quoteType":"ETF","symbol":"VWRL.AS","longname":"Vanguard FTSE All-World UCITS ETF","exchDisp":"Amsterdam"},
              {"exchange":"MIL","quoteType":"ETF","symbol":"VWCE.MI","longname":"Vanguard FTSE All-World UCITS ETF USD Accumulation","exchDisp":"Milan"},
              {"exchange":"AMS","quoteType":"ETF","symbol":"VWCE.AS","longname":"Vanguard FTSE All-World UCITS ETF USD Accumulation","exchDisp":"Amsterdam"}],"news":[]}
            """;
        const string sinCierres = """{"chart":{"result":[{"meta":{"currency":"EUR","symbol":"IE00BK5BQT80.SG","fullExchangeName":"Stuttgart","gmtoffset":3600},"timestamp":[],"indicators":{"quote":[{"close":[]}]}}],"error":null}}""";
        var http = new HttpFijo(url => url.Contains("/search?q=IE00BK5BQT80") ? (HttpStatusCode.OK, porIsin)
            : url.Contains("/search?q=Vanguard") ? (HttpStatusCode.OK, porNombre)
            : url.Contains("chart/IE00BK5BQT80.SG?") ? (HttpStatusCode.OK, sinCierres)
            : url.Contains("chart/VWRA.L?") ? (HttpStatusCode.OK, Grafico("VWRA.L", "USD"))
            : url.Contains("chart/VWCE.") ? (HttpStatusCode.OK, Grafico("VWCE", "EUR"))
            : url.Contains("chart/VWRL.AS?") ? (HttpStatusCode.OK, Grafico("VWRL.AS", "EUR")) : null);
        var r = await Servicio(http).BuscarPorIsinAsync("IE00BK5BQT80");
        Assert.Equal(["VWCE.DE", "VWCE.AS", "VWCE.MI", "VWRA.L"], r.Valor.Cotizaciones.Select(c => c.TickerYahoo));
        Assert.Equal("VWCE.DE", r.Valor.Propuesta!.TickerYahoo);
        Assert.Equal("VWCE.XETRA", r.Valor.Propuesta.TickerEodhd);
        Assert.Equal("Vanguard FTSE All-World UCITS ETF USD Accumulation", r.Valor.Propuesta.Nombre);
        Assert.DoesNotContain(http.Pedidas, u => u.Contains("chart/VWRL.AS")); // otra clase: ni se consulta
    }

    /// <summary>IE00B5BMR087: por ISIN solo sale CSSPX.MI; por el nombre, SXR8.DE (Xetra), que pasa a ser la propuesta.</summary>
    [Fact]
    public async Task AunqueElIsinDeUnListadoEnEurosPrefiereXetra()
    {
        const string nombre = "iShares Core S&P 500 UCITS ETF USD (Acc)";
        const string porIsin = """{"quotes":[{"exchange":"MIL","quoteType":"ETF","symbol":"CSSPX.MI","longname":"iShares Core S&P 500 UCITS ETF USD (Acc)","exchDisp":"Milan"}]}""";
        const string porNombre = """
            {"quotes":[{"exchange":"GER","quoteType":"ETF","symbol":"SXR8.DE","longname":"iShares Core S&P 500 UCITS ETF USD (Acc)","exchDisp":"XETRA"},
              {"exchange":"LSE","quoteType":"ETF","symbol":"CSPX.L","longname":"iShares Core S&P 500 UCITS ETF USD (Acc)","exchDisp":"London"},
              {"exchange":"GER","quoteType":"ETF","symbol":"IUSA.DE","longname":"iShares S&P 500 UCITS ETF (Dist)","exchDisp":"XETRA"}]}
            """;
        var http = new HttpFijo(url => url.Contains("/search?q=IE00B5BMR087") ? (HttpStatusCode.OK, porIsin)
            : url.Contains("/search?q=iShares") ? (HttpStatusCode.OK, porNombre)
            : url.Contains("chart/CSPX.L?") ? (HttpStatusCode.OK, Grafico("CSPX.L", "USD", nombre: nombre))
            : url.Contains("chart/") ? (HttpStatusCode.OK, Grafico("X", "EUR", nombre: nombre)) : null);
        var r = await Servicio(http).BuscarPorIsinAsync("IE00B5BMR087");
        Assert.Equal(["SXR8.DE", "CSSPX.MI", "CSPX.L"], r.Valor.Cotizaciones.Select(c => c.TickerYahoo));
    }

    [Fact]
    public async Task SiYahooNoLoTieneBuscaEnEodhdConAvisoLeve()
    {
        var http = new HttpFijo(url => url.Contains("/search?") ? (HttpStatusCode.OK, """{"quotes":[]}""")
            : url.Contains("eodhd.com/api/search/LU0996182563") ? (HttpStatusCode.OK, """[{"Code":"LU0996182563","Exchange":"EUFUND","Name":"Fondo índice","Type":"FUND","Currency":"EUR","ISIN":"LU0996182563"}]""")
            : null);
        var r = await Servicio(http, clave: "clave").BuscarPorIsinAsync("LU0996182563");
        Assert.Equal(FuenteCotizacion.Eodhd, r.Valor.EncontradoEn);
        Assert.False(r.Valor.AvisoGrave);
        Assert.Contains("EODHD sí", r.Valor.Aviso);
        var c = Assert.Single(r.Valor.Cotizaciones);
        Assert.Null(c.TickerYahoo);
        Assert.Equal("LU0996182563.EUFUND", c.TickerEodhd);
        Assert.Equal("FONDO", c.TipoInstrumento);
    }

    [Fact]
    public async Task SiNadieLoTieneAvisoGraveConEnlaceAYahoo()
    {
        var http = new HttpFijo(url => url.Contains("/search?") ? (HttpStatusCode.OK, """{"quotes":[]}""") : url.Contains("eodhd.com") ? (HttpStatusCode.OK, "[]") : null);
        var sinClave = await Servicio(http).BuscarPorIsinAsync("ES0113900J37");
        Assert.True(sinClave.Valor.AvisoGrave);
        Assert.Contains("no hay clave guardada", sinClave.Valor.Aviso);
        var conClave = await Servicio(http, "clave").BuscarPorIsinAsync("ES0113900J37");
        Assert.True(conClave.Valor.AvisoGrave);
        Assert.Contains("ni EODHD", conClave.Valor.Aviso);
        Assert.Equal("https://finance.yahoo.com/lookup?s=ES0113900J37", conClave.Valor.UrlYahoo);
        Assert.False((await Servicio(http).BuscarPorIsinAsync("ES0113900J38")).EsCorrecto); // ISIN mal escrito: ni se busca
    }

    [Fact]
    public async Task UnSabadoTomaElCierreDelViernesDeYahoo()
    {
        var http = new HttpFijo(url => url.Contains("chart/IWDA.AS") ? (HttpStatusCode.OK, Grafico("IWDA.AS", "EUR")) : null);
        var r = await Servicio(http).PrecioEnEurosAsync(new ReferenciaCotizacion("IWDA.AS", "IWDA.AS", "EUR"), new DateTime(2026, 11, 22));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(new DateTime(2026, 11, 21), r.Valor.FechaCotizacion);
        Assert.Equal(104.5m, r.Valor.Precio);
        Assert.Equal(104.5m, r.Valor.PrecioEur);
        Assert.Equal(1m, r.Valor.UnidadesPorEuro);
        Assert.Equal("Yahoo Finance · cierre", r.Valor.Origen);
    }

    [Fact]
    public async Task HoyPrimeroEodhdYSiFallaYahooDelMomento()
    {
        var conEodhd = new HttpFijo(url => url.Contains("eodhd.com/api/real-time/IWDA.AS") ? (HttpStatusCode.OK, $$"""{"timestamp":{{Unix(Hoy.AddHours(11))}},"close":106.1}""") : null);
        var r = await Servicio(conEodhd, "clave").PrecioEnEurosAsync(new ReferenciaCotizacion("IWDA.AS", "IWDA.AS", "EUR"), Hoy);
        Assert.Equal((106.1m, "EODHD · del momento"), (r.Valor.Precio, r.Valor.Origen));

        var sinEodhd = new HttpFijo(url => url.Contains("eodhd.com") ? (HttpStatusCode.PaymentRequired, "")
            : url.Contains("chart/IWDA.AS") ? (HttpStatusCode.OK, Grafico("IWDA.AS", "EUR", 105.25m)) : null);
        var y = await Servicio(sinEodhd, "clave").PrecioEnEurosAsync(new ReferenciaCotizacion("IWDA.AS", "IWDA.AS", "EUR"), Hoy);
        Assert.Equal((105.25m, Hoy, "Yahoo Finance · del momento"), (y.Valor.Precio, y.Valor.FechaCotizacion, y.Valor.Origen));
    }

    [Fact]
    public async Task SinMonedaLaDaYahooYSinPrecioLoDice()
    {
        var http = new HttpFijo(url => url.Contains("chart/IWDA.AS") ? (HttpStatusCode.OK, Grafico("IWDA.AS", "EUR")) : null);
        var r = await Servicio(http).PrecioEnEurosAsync(new ReferenciaCotizacion("IWDA.AS", null, ""), new DateTime(2026, 11, 20));
        Assert.Equal(("EUR", 103.5m), (r.Valor.Moneda, r.Valor.Precio));
        var futuro = await Servicio(http).PrecioEnEurosAsync(new ReferenciaCotizacion("IWDA.AS", null, "EUR"), Hoy.AddDays(1));
        Assert.Contains("futura", futuro.Error!.Mensaje);
        var antiguo = await Servicio(http).PrecioEnEurosAsync(new ReferenciaCotizacion("IWDA.AS", null, "EUR"), new DateTime(2026, 1, 15));
        Assert.False(antiguo.EsCorrecto); // ningún cierre en los 10 días anteriores
    }

    [Fact]
    public void SimbolosRedondeosYPeniques()
    {
        Assert.Equal("EUNL.XETRA", ServicioCotizaciones.TickerEodhdDesdeYahoo("EUNL.DE", "IE00B4L5Y983", "ETF"));
        Assert.Equal("VOO.US", ServicioCotizaciones.TickerEodhdDesdeYahoo("VOO", "US9229083632", "ETF"));
        Assert.Equal("LU0996182563.EUFUND", ServicioCotizaciones.TickerEodhdDesdeYahoo("0P00012345.F", "LU0996182563", "FONDO"));
        Assert.Null(ServicioCotizaciones.TickerEodhdDesdeYahoo("ABC.XX", "IE00B4L5Y983", "ETF"));
        Assert.Equal((12.345m, "GBP"), ServicioCotizaciones.AMonedaIso(1234.5m, "GBp"));
        Assert.Equal((5m, "USD"), ServicioCotizaciones.AMonedaIso(5m, "usd"));
        Assert.Equal(95.238095m, ServicioCotizaciones.AEuros(100m, 1.05m)); // 95,2380952… → 6 decimales
        Assert.Equal(1234.57m, CompraInversion.CostePropuesto(12.5m, 98.765432m)); // 1.234,5679 → céntimos
        Assert.Equal(0.01m, CompraInversion.CostePropuesto(1m, 0.005m)); // al alza en el medio
    }

    [Fact]
    public void LaCompraDiceQueFalta()
    {
        var bien = new CompraInversion(Hoy, 10m, 100m, "EUR", 1m, 1000m, null);
        Assert.Empty(bien.Validar(Hoy));
        Assert.Contains((bien with { Fecha = Hoy.AddDays(1) }).Validar(Hoy), e => e.Contains("posterior a hoy"));
        Assert.Contains((bien with { Participaciones = 0m }).Validar(Hoy), e => e.Contains("participaciones"));
        Assert.Contains((bien with { Participaciones = 1.1234567m }).Validar(Hoy), e => e.Contains("6 decimales"));
        Assert.Contains((bien with { PrecioUnitario = 0m }).Validar(Hoy), e => e.Contains("precio"));
        Assert.Contains((bien with { ImporteTotalEur = 0m }).Validar(Hoy), e => e.Contains("coste total"));
    }
}
