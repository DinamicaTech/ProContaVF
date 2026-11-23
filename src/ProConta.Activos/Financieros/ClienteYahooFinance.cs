using System.Net;
using System.Text.Json;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Financieros;

/// <summary>Un resultado de la búsqueda de Yahoo: símbolo, nombre, bolsa y tipo (la moneda sale del gráfico).</summary>
public sealed record ResultadoBusquedaYahoo(string Simbolo, string Nombre, string Bolsa, string Tipo);

/// <summary>Datos del gráfico de Yahoo de un símbolo: moneda, bolsa, último precio y cierres diarios.</summary>
/// <param name="Cierres">Cierre de cada día de cotización, por fecha local de la bolsa.</param>
public sealed record GraficoYahoo(string Simbolo, string Moneda, string Bolsa, string? Nombre, string? Tipo, decimal? UltimoPrecio, DateTime? FechaUltimoPrecio,
    IReadOnlyList<(DateTime Fecha, decimal Cierre)> Cierres);

/// <summary>
/// Cliente de Yahoo Finance sin API oficial (activos/activos-financieros 2026-11-23 20:11: puede dejar de funcionar sin aviso, por eso EODHD es el respaldo):
/// búsqueda por ISIN (v1/finance/search) y gráfico diario (v8/finance/chart), que da la moneda, el precio del momento y el histórico.
/// </summary>
public sealed class ClienteYahooFinance(HttpClient http)
{
    public const string Direccion = "https://query2.finance.yahoo.com/";

    /// <summary>Símbolos de Yahoo para un ISIN (vacío si no lo encuentra).</summary>
    public async Task<Resultado<IReadOnlyList<ResultadoBusquedaYahoo>>> BuscarAsync(string isin, CancellationToken cancelacion = default)
    {
        var r = await GetAsync($"{Direccion}v1/finance/search?q={Uri.EscapeDataString(isin)}&quotesCount=10&newsCount=0&listsCount=0", cancelacion);
        return r.EsCorrecto ? Resultado<IReadOnlyList<ResultadoBusquedaYahoo>>.Correcto(LeerBusqueda(r.Valor)) : Resultado<IReadOnlyList<ResultadoBusquedaYahoo>>.Fallo(r.Errores);
    }

    /// <summary>Gráfico diario de un símbolo entre dos fechas (ambas incluidas).</summary>
    public async Task<Resultado<GraficoYahoo>> GraficoAsync(string simbolo, DateTime desde, DateTime hasta, CancellationToken cancelacion = default)
    {
        var p1 = new DateTimeOffset(desde.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var p2 = new DateTimeOffset(hasta.Date.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
        var r = await GetAsync($"{Direccion}v8/finance/chart/{Uri.EscapeDataString(simbolo)}?period1={p1}&period2={p2}&interval=1d&events=history", cancelacion);
        if (!r.EsCorrecto) return Resultado<GraficoYahoo>.Fallo(r.Errores);
        return LeerGrafico(simbolo, r.Valor) is { } g ? Resultado<GraficoYahoo>.Correcto(g)
            : Resultado<GraficoYahoo>.Fallo(new Error("Yahoo.SinDatos", $"Yahoo Finance no tiene datos del símbolo {simbolo}.", null, "El símbolo no existe en Yahoo o ya no cotiza.",
                "Comprueba el símbolo en finance.yahoo.com o escribe el precio a mano."));
    }

    private async Task<Resultado<string>> GetAsync(string url, CancellationToken cancelacion)
    {
        try
        {
            using var pet = new HttpRequestMessage(HttpMethod.Get, url);
            // Sin un agente de navegador, Yahoo responde 429 a menudo.
            pet.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ProContaVF");
            pet.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var resp = await http.SendAsync(pet, cancelacion);
            var cuerpo = await resp.Content.ReadAsStringAsync(cancelacion);
            if (resp.StatusCode == HttpStatusCode.NotFound) return Resultado<string>.Correcto(cuerpo); // el gráfico responde 404 con un error en JSON
            if (!resp.IsSuccessStatusCode)
                return Resultado<string>.Fallo(new Error("Yahoo.Http", $"Yahoo Finance no responde bien (HTTP {(int)resp.StatusCode}).", null,
                    resp.StatusCode == HttpStatusCode.TooManyRequests ? "Demasiadas consultas seguidas." : "Yahoo no tiene API oficial y a veces rechaza consultas.",
                    "Espera un minuto y vuelve a intentarlo; si sigue fallando, escribe el precio a mano."));
            return Resultado<string>.Correcto(cuerpo);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Resultado<string>.Fallo(new Error("Yahoo.SinConexion", "No he podido conectar con Yahoo Finance.", null, ex.Message,
                "Comprueba la conexión a internet y vuelve a intentarlo; si no, escribe el precio a mano."));
        }
    }

    /// <summary>Lee la respuesta de la búsqueda: solo instrumentos con símbolo (ETF, fondos, acciones…), sin noticias.</summary>
    public static IReadOnlyList<ResultadoBusquedaYahoo> LeerBusqueda(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array) return [];
        var r = new List<ResultadoBusquedaYahoo>();
        foreach (var q in quotes.EnumerateArray())
        {
            var simbolo = Cadena(q, "symbol");
            if (string.IsNullOrWhiteSpace(simbolo)) continue;
            var tipo = Cadena(q, "quoteType") ?? "";
            if (tipo is "OPTION" or "FUTURE" or "CURRENCY" or "CRYPTOCURRENCY" or "INDEX") continue;
            r.Add(new(simbolo, Cadena(q, "longname") ?? Cadena(q, "shortname") ?? simbolo, Cadena(q, "exchDisp") ?? Cadena(q, "exchange") ?? "", TipoInstrumento(tipo)));
        }
        return r;
    }

    /// <summary>Lee la respuesta del gráfico; null si Yahoo devuelve un error o no hay resultado.</summary>
    public static GraficoYahoo? LeerGrafico(string simbolo, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chart", out var chart) || !chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
            return null;
        var r0 = result[0];
        if (!r0.TryGetProperty("meta", out var meta)) return null;
        var moneda = Cadena(meta, "currency");
        if (string.IsNullOrWhiteSpace(moneda)) return null;
        var desfase = meta.TryGetProperty("gmtoffset", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetInt64() : 0L;
        DateTime Dia(long ts) => DateTimeOffset.FromUnixTimeSeconds(ts + desfase).UtcDateTime.Date;
        var cierres = new List<(DateTime, decimal)>();
        if (r0.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Array
            && r0.TryGetProperty("indicators", out var ind) && ind.TryGetProperty("quote", out var quote) && quote.ValueKind == JsonValueKind.Array && quote.GetArrayLength() > 0
            && quote[0].TryGetProperty("close", out var close) && close.ValueKind == JsonValueKind.Array)
        {
            var n = Math.Min(ts.GetArrayLength(), close.GetArrayLength());
            for (var i = 0; i < n; i++)
                if (ts[i].ValueKind == JsonValueKind.Number && close[i].ValueKind == JsonValueKind.Number && close[i].TryGetDecimal(out var c) && c > 0m)
                    cierres.Add((Dia(ts[i].GetInt64()), c));
        }
        // Un mismo día puede venir dos veces (la sesión en curso): se queda el último valor.
        var porDia = cierres.GroupBy(x => x.Item1).Select(x => x.Last()).OrderBy(x => x.Item1).ToList();
        decimal? ultimo = meta.TryGetProperty("regularMarketPrice", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var pv) && pv > 0m ? pv : null;
        DateTime? fechaUltimo = meta.TryGetProperty("regularMarketTime", out var t) && t.ValueKind == JsonValueKind.Number ? Dia(t.GetInt64()) : null;
        return new GraficoYahoo(Cadena(meta, "symbol") ?? simbolo, moneda, Cadena(meta, "fullExchangeName") ?? Cadena(meta, "exchangeName") ?? "",
            Cadena(meta, "longName") ?? Cadena(meta, "shortName"), Cadena(meta, "instrumentType") is { } it ? TipoInstrumento(it) : null, ultimo, fechaUltimo, porDia);
    }

    /// <summary>ETF, FONDO, ACCION o el tipo de Yahoo tal cual.</summary>
    public static string TipoInstrumento(string tipoYahoo) => tipoYahoo.ToUpperInvariant() switch
    {
        "ETF" => "ETF",
        "MUTUALFUND" => "FONDO",
        "EQUITY" => "ACCION",
        var x => x.Length == 0 ? "OTRO" : x,
    };

    private static string? Cadena(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
}
