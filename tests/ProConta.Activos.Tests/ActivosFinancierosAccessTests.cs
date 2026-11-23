using System.Globalization;
using System.Net;
using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Activos.Tests;

/// <summary>
/// Alta de activos financieros por ISIN contra una copia de pruebas con datos sintéticos (activos/activos-financieros 2026-11-23 20:32 y 20:38): la
/// migración deja activo el tipo «Inversión financiera»; el alta guarda instrumento, enlace, primera compra y cotización, con coste y fecha en el activo y
/// sin asientos; un ISIN repetido se rechaza; «Nueva compra» suma participaciones y coste; cambiar el coste del lote recalcula el activo; y los cambios del
/// BCE se guardan una vez y se reutilizan.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ActivosFinancierosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioActivos Servicio => new(Bd, new CatalogoCuentasAccess(Bd));
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)), CultureInfo.InvariantCulture);

    private int _inversion, _centro;

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            _inversion = Convert.ToInt32(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'INVERSION' AND EstaActivo = True AND EsAmortizablePredeterminado = False"),
                CultureInfo.InvariantCulture);
            await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, IdCentroCostePadre, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", null, "4", "INVERSIONES ETFS", true, true, (short)1, (byte)1, "4", "0004");
            _centro = await s.UltimoIdAsync();
            return 0;
        });
    }

    private static readonly DatosInstrumento Msci = new("IE00B4L5Y983", "iShares Core MSCI World UCITS ETF USD (Acc)", "IWDA.AS", "IWDA.AS", "Amsterdam", "EUR", "ETF",
        FuenteCotizacion.Yahoo);

    private static PrecioEnEuros Cotizacion(DateTime fecha, decimal precio) => new(precio, "EUR", fecha, 1m, fecha, precio, "Yahoo Finance · cierre", FuenteCotizacion.Yahoo);

    private DatosActivo Etf(DateTime fecha, decimal coste) => new()
    {
        Codigo = "IWDA", Nombre = Msci.Nombre, NombreCorto = "IWDA", IdTipoActivo = _inversion, CodigoTipo = TiposActivoPgc.Inversion, FechaCompra = fecha, Coste = coste,
        IdCentroCoste = _centro,
    };

    [Fact]
    public async Task Alta_por_isin_guarda_instrumento_compra_y_cotizacion_sin_asientos_y_nueva_compra_suma()
    {
        await PrepararAsync();
        var dia = new DateTime(2026, 11, 20);
        var compra = new CompraInversion(dia, 12.5m, 98.765432m, "EUR", 1m, CompraInversion.CostePropuesto(12.5m, 98.765432m), Cotizacion(dia, 98.765432m));
        var r = await Servicio.AltaAsync(_ctx, new AltaActivo(Etf(dia, compra.ImporteTotalEur), null, Msci, compra));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        var id = r.Valor;

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM InstrumentosFinancieros WHERE Isin = 'IE00B4L5Y983' AND Ticker = 'IWDA.AS' AND TickerEodhd = 'IWDA.AS' AND OrigenDatos = 'YAHOO' AND MonedaCotizacion = 'EUR'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ActivosInversiones WHERE IdActivo = ? AND ParticipacionesActuales = 12.5", id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM OperacionesInversion WHERE IdActivo = ? AND Tipo = 1 AND ImporteTotalEur = 1234.57", id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero WHERE FechaCotizacion = ?", dia));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Activos WHERE IdActivo = ? AND CosteAdquisicionActual = 1234.57 AND EsAmortizable = False AND FechaAdquisicion = ?", id, dia));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM PlanesAmortizacionActivo WHERE IdActivo = ?", id));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        // El mismo ISIN otra vez: se rechaza y se dice que se use «Nueva compra».
        var repetido = await Servicio.AltaAsync(_ctx, new AltaActivo(Etf(dia, 10m) with { Codigo = "IWDA2" }, null, Msci, compra));
        Assert.False(repetido.EsCorrecto);
        Assert.Contains("Nueva compra", repetido.Error!.TextoOperador());

        // Nueva compra anterior: suma participaciones y coste, y la fecha de compra pasa a ser la del primer lote.
        var antes = new DateTime(2026, 10, 1);
        var n = await Servicio.NuevaCompraAsync(_ctx, id, new CompraInversion(antes, 2m, 90m, "EUR", 1m, 180m, null));
        Assert.True(n.EsCorrecto, n.Error?.ToString());
        var inv = (await Servicio.InversionAsync(id))!;
        Assert.Equal((14.5m, 1414.57m, 2), (inv.Participaciones, inv.Coste, inv.Operaciones.Count));
        Assert.Equal(antes, inv.Operaciones[0].Fecha);
        Assert.Equal("Precio escrito a mano.", inv.Operaciones[0].Notas);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Activos WHERE IdActivo = ? AND CosteAdquisicionActual = 1414.57 AND FechaAdquisicion = ? AND Version = 1", id, antes));

        // El bróker confirma 1.240,10 € por el lote del 20/11: el coste del activo se recalcula.
        var lote = inv.Operaciones[1];
        Assert.True((await Servicio.CambiarCosteLoteAsync(_ctx, lote.IdOperacion, 1240.10m)).EsCorrecto);
        Assert.Equal(1420.10m, (await Servicio.InversionAsync(id))!.Coste);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Activos WHERE IdActivo = ? AND CosteAdquisicionActual = 1420.1 AND ValorContable = 1420.1", id));
        Assert.False((await Servicio.CambiarCosteLoteAsync(_ctx, lote.IdOperacion, 0m)).EsCorrecto);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        var listado = Assert.Single(await Servicio.ListarAsync(_ctx));
        Assert.Equal(("Financieros", 1420.10m), (listado.Clase, listado.Coste));
    }

    [Fact]
    public async Task Un_activo_que_no_es_financiero_no_lleva_isin_ni_compras()
    {
        await PrepararAsync();
        var vivienda = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'VIVIENDA'")), CultureInfo.InvariantCulture);
        var dia = new DateTime(2026, 11, 20);
        var compra = new CompraInversion(dia, 1m, 10m, "EUR", 1m, 10m, null);
        var conIsin = await Servicio.AltaAsync(_ctx, new AltaActivo(Etf(dia, 10m) with { IdTipoActivo = vivienda, CodigoTipo = TiposActivoPgc.Vivienda }, null, Msci, compra));
        Assert.False(conIsin.EsCorrecto);
        var sinIsin = await Servicio.AltaAsync(_ctx, new AltaActivo(Etf(dia, 10m)));
        Assert.False(sinIsin.EsCorrecto);
        Assert.Contains("ISIN", sinIsin.Error!.Mensaje);

        var piso = await Servicio.AltaAsync(_ctx, new AltaActivo(Etf(dia, 10m) with { Codigo = "Piso", IdTipoActivo = vivienda, CodigoTipo = TiposActivoPgc.Vivienda }));
        Assert.True(piso.EsCorrecto, piso.Error?.ToString());
        var nueva = await Servicio.NuevaCompraAsync(_ctx, piso.Valor, compra);
        Assert.False(nueva.EsCorrecto);
        Assert.Contains("no es un activo financiero", nueva.Error!.Mensaje);
    }

    [Fact]
    public async Task Los_cambios_del_bce_se_guardan_una_vez_y_valen_para_el_fin_de_semana()
    {
        await PrepararAsync();
        var csv = "KEY,FREQ,CURRENCY,CURRENCY_DENOM,EXR_TYPE,EXR_SUFFIX,TIME_PERIOD,OBS_VALUE\n" +
                  "EXR.D.USD.EUR.SP00.A,D,USD,EUR,SP00,A,2026-11-19,1.0512\nEXR.D.USD.EUR.SP00.A,D,USD,EUR,SP00,A,2026-11-20,1.0533\n";
        var http = new HttpFijo(url => url.Contains("data-api.ecb.europa.eu") ? (HttpStatusCode.OK, csv) : null);
        var cambios = new ServicioCambiosDivisa(Bd, new ClienteBce(new HttpClient(http)));

        var viernes = await cambios.CambioAsync("USD", new DateTime(2026, 11, 20));
        Assert.Equal((new DateTime(2026, 11, 20), 1.0533m), viernes.Valor);
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM CambiosDivisa WHERE Moneda = 'USD' AND Origen = 'BCE'"));
        var pedidas = http.Pedidas.Count;
        Assert.Equal((new DateTime(2026, 11, 19), 1.0512m), (await cambios.CambioAsync("usd", new DateTime(2026, 11, 19))).Valor);
        Assert.Equal(pedidas, http.Pedidas.Count); // el del 19 ya estaba guardado

        // El sábado no hay cambio publicado: se vuelve a preguntar y vale el del viernes; no se duplica nada.
        Assert.Equal((new DateTime(2026, 11, 20), 1.0533m), (await cambios.CambioAsync("USD", new DateTime(2026, 11, 21))).Valor);
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM CambiosDivisa"));
        Assert.Equal((new DateTime(2026, 11, 21), 1m), (await cambios.CambioAsync("EUR", new DateTime(2026, 11, 21))).Valor);
    }

    private static long Unix(DateTime d) => new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public async Task La_cartera_completa_el_historico_y_toma_el_precio_del_dia_de_eodhd_la_primera_vez()
    {
        await PrepararAsync();
        var hoy = new DateTime(2026, 11, 23); // lunes
        var compra = new DateTime(2026, 11, 17);
        var alta = await Servicio.AltaAsync(_ctx, new AltaActivo(Etf(compra, 1000m), null, Msci, new CompraInversion(compra, 10m, 100m, "EUR", 1m, 1000m, Cotizacion(compra, 100m))));
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        var idInstrumento = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT IdInstrumentoFinanciero FROM InstrumentosFinancieros")), CultureInfo.InvariantCulture);
        // Un precio del momento del viernes que el cierre real debe sustituir.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO CotizacionesInstrumentoFinanciero (IdInstrumentoFinanciero, FechaCotizacion, Precio, Moneda, Origen, FechaHoraImportacion) " +
            "VALUES (?, ?, ?, ?, ?, ?)", idInstrumento, new DateTime(2026, 11, 20), 103m, "EUR", "EODHD · del momento", DateTime.Now));

        DateTime[] dias = [new(2026, 11, 17), new(2026, 11, 18), new(2026, 11, 19), new(2026, 11, 20), hoy];
        decimal[] cierres = [100.5m, 101.5m, 102.5m, 103.5m, 105m];
        var grafico = "{\"chart\":{\"result\":[{\"meta\":{\"currency\":\"EUR\",\"symbol\":\"IWDA.AS\",\"exchangeName\":\"AMS\",\"regularMarketPrice\":105.2,\"regularMarketTime\":" +
            Unix(hoy.AddHours(10)) + ",\"gmtoffset\":3600},\"timestamp\":[" + string.Join(",", dias.Select(d => Unix(d.AddHours(8)))) + "],\"indicators\":{\"quote\":[{\"close\":[" +
            string.Join(",", cierres.Select(c => c.ToString(CultureInfo.InvariantCulture))) + "]}]}}],\"error\":null}}";
        var http = new HttpFijo(url =>
            url.Contains("finance/chart/IWDA.AS") ? (HttpStatusCode.OK, grafico)
            : url.Contains("eodhd.com/api/real-time/IWDA.AS") ? (HttpStatusCode.OK, "{\"timestamp\":" + Unix(hoy.AddHours(11)) + ",\"close\":105.1}")
            : null);
        var yahoo = new ClienteYahooFinance(new HttpClient(http));
        var cambios = new ServicioCambiosDivisa(Bd, new ClienteBce(new HttpClient(http)));
        var cotizaciones = new ServicioCotizaciones(yahoo, new ClienteEodhd(new HttpClient(http)), cambios, () => Task.FromResult<string?>("clave"), () => hoy);
        var cartera = new ServicioCarteraFinanciera(Bd, Servicio, cotizaciones, yahoo, cambios, () => hoy);

        // Primera apertura del día: el histórico se completa (18 y 19 nuevos, el 20 sustituido, hoy no) y el precio de hoy sale de EODHD y se guarda.
        var r = await cartera.CargarAsync("T01", conRed: true);
        Assert.Empty(r.Avisos);
        var p = Assert.Single(r.Activos).Posicion;
        Assert.Equal((105.1m, "EODHD · del momento", 1051m), (p.Precio!.Value, p.OrigenPrecio, p.Valor));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero WHERE FechaCotizacion = ? AND Precio = 103.5 AND Origen = 'Yahoo Finance · cierre'", new DateTime(2026, 11, 20)));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero WHERE FechaCotizacion = ? AND Precio = 100", compra));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero WHERE FechaCotizacion = ? AND Precio = 105.1", hoy));
        Assert.Equal(1, http.Pedidas.Count(u => u.Contains("real-time")));

        // Siguientes aperturas: el precio del momento es el de Yahoo y no se vuelve a gastar una consulta de EODHD.
        p = Assert.Single((await cartera.CargarAsync("T01", conRed: true)).Activos).Posicion;
        Assert.Equal((105.2m, "Yahoo Finance · del momento"), (p.Precio!.Value, p.OrigenPrecio));
        Assert.Equal(1, http.Pedidas.Count(u => u.Contains("real-time")));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero"));

        // Sin red: lo guardado, con el precio de EODHD de esta mañana.
        p = Assert.Single((await cartera.CargarAsync("T01", conRed: false)).Activos).Posicion;
        Assert.Equal(105.1m, p.Precio);
        Assert.Contains("guardada", p.OrigenPrecio);
        Assert.Equal(5, p.Historico.Count);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
    }
}
