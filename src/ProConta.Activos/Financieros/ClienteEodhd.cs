using System.Globalization;
using System.Net;
using System.Text.Json;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Financieros;

/// <summary>Un resultado de la búsqueda de EODHD.</summary>
/// <param name="Simbolo">Código y bolsa de EODHD (IWDA.AS, EUNL.XETRA, LU0996182563.EUFUND).</param>
public sealed record ResultadoBusquedaEodhd(string Simbolo, string Codigo, string Bolsa, string Nombre, string Tipo, string Moneda, string? Isin);

/// <summary>
/// Cliente de EOD Historical Data con la clave de Parámetros › Integraciones › «Cotizaciones · EODHD» (activos/activos-financieros 2026-11-23 20:11): plan
/// gratuito de 20 consultas al día, así que solo se usa cuando Yahoo no basta.
/// </summary>
public sealed class ClienteEodhd(HttpClient http)
{
    public const string Direccion = "https://eodhd.com/api/";

    /// <summary>Instrumentos con ese ISIN (vacío si no lo encuentra).</summary>
    public async Task<Resultado<IReadOnlyList<ResultadoBusquedaEodhd>>> BuscarAsync(string token, string isin, CancellationToken cancelacion = default)
    {
        var r = await GetAsync($"{Direccion}search/{Uri.EscapeDataString(isin)}?api_token={Uri.EscapeDataString(token)}&fmt=json", cancelacion);
        return r.EsCorrecto ? Resultado<IReadOnlyList<ResultadoBusquedaEodhd>>.Correcto(LeerBusqueda(r.Valor, isin)) : Resultado<IReadOnlyList<ResultadoBusquedaEodhd>>.Fallo(r.Errores);
    }

    /// <summary>Precio del momento (con unos minutos de retraso) y su día; null si EODHD no lo da.</summary>
    public async Task<Resultado<(decimal Precio, DateTime Fecha)?>> TiempoRealAsync(string token, string simbolo, CancellationToken cancelacion = default)
    {
        var r = await GetAsync($"{Direccion}real-time/{Uri.EscapeDataString(simbolo)}?api_token={Uri.EscapeDataString(token)}&fmt=json", cancelacion);
        return r.EsCorrecto ? Resultado<(decimal, DateTime)?>.Correcto(LeerTiempoReal(r.Valor)) : Resultado<(decimal, DateTime)?>.Fallo(r.Errores);
    }

    /// <summary>Cierres diarios entre dos fechas (ambas incluidas).</summary>
    public async Task<Resultado<IReadOnlyList<(DateTime Fecha, decimal Cierre)>>> CierresAsync(string token, string simbolo, DateTime desde, DateTime hasta,
        CancellationToken cancelacion = default)
    {
        var r = await GetAsync($"{Direccion}eod/{Uri.EscapeDataString(simbolo)}?from={desde:yyyy-MM-dd}&to={hasta:yyyy-MM-dd}&period=d&api_token={Uri.EscapeDataString(token)}&fmt=json",
            cancelacion);
        return r.EsCorrecto ? Resultado<IReadOnlyList<(DateTime, decimal)>>.Correcto(LeerCierres(r.Valor)) : Resultado<IReadOnlyList<(DateTime, decimal)>>.Fallo(r.Errores);
    }

    private async Task<Resultado<string>> GetAsync(string url, CancellationToken cancelacion)
    {
        try
        {
            using var resp = await http.GetAsync(url, cancelacion);
            var cuerpo = await resp.Content.ReadAsStringAsync(cancelacion);
            if (resp.IsSuccessStatusCode) return Resultado<string>.Correcto(cuerpo);
            return Resultado<string>.Fallo(resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new Error("Eodhd.ClaveNoValida", "EODHD no acepta la clave.", null, "La clave guardada no es válida o ha caducado.",
                    "Cópiala de nuevo de eodhd.com › Settings › API token y pégala en Parámetros y tablas › Integraciones › «Cotizaciones · EODHD»."),
                HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests => new Error("Eodhd.Limite", "EODHD dice que hoy ya se han hecho todas las consultas del plan.",
                    null, "El plan gratuito permite 20 consultas al día.", "Vuelve a intentarlo mañana o escribe el precio a mano."),
                HttpStatusCode.NotFound => new Error("Eodhd.NoEncontrado", "EODHD no tiene ese símbolo.", null, null, "Escribe el precio a mano."),
                _ => new Error("Eodhd.Http", $"EODHD no responde bien (HTTP {(int)resp.StatusCode}).", null, null, "Vuelve a intentarlo en unos minutos."),
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Resultado<string>.Fallo(new Error("Eodhd.SinConexion", "No he podido conectar con EODHD.", null, ex.Message, "Comprueba la conexión a internet y vuelve a intentarlo."));
        }
    }

    /// <summary>Lee la búsqueda; con ISIN, solo las filas de ese ISIN (la búsqueda también devuelve parecidos).</summary>
    public static IReadOnlyList<ResultadoBusquedaEodhd> LeerBusqueda(string json, string? isin = null)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        var r = new List<ResultadoBusquedaEodhd>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var codigo = Cadena(e, "Code");
            var bolsa = Cadena(e, "Exchange");
            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(bolsa)) continue;
            var isinFila = Cadena(e, "ISIN");
            if (isin is not null && isinFila is not null && !string.Equals(isinFila, isin, StringComparison.OrdinalIgnoreCase)) continue;
            r.Add(new($"{codigo}.{bolsa}", codigo, bolsa, Cadena(e, "Name") ?? codigo, TipoInstrumento(Cadena(e, "Type") ?? ""), Cadena(e, "Currency") ?? "", isinFila));
        }
        return r;
    }

    public static (decimal Precio, DateTime Fecha)? LeerTiempoReal(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("close", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetDecimal(out var precio) || precio <= 0m)
            return null;
        var fecha = e.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).UtcDateTime.Date : DateTime.Today;
        return (precio, fecha);
    }

    public static IReadOnlyList<(DateTime Fecha, decimal Cierre)> LeerCierres(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        var r = new List<(DateTime, decimal)>();
        foreach (var e in doc.RootElement.EnumerateArray())
            if (Cadena(e, "date") is { } d && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                && e.TryGetProperty("close", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var v) && v > 0m)
                r.Add((f, v));
        return r.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>ETF, FONDO, ACCION o el tipo de EODHD en mayúsculas.</summary>
    public static string TipoInstrumento(string tipo) => tipo.Trim().ToUpperInvariant() switch
    {
        "ETF" => "ETF",
        "FUND" or "MUTUAL FUND" => "FONDO",
        "COMMON STOCK" or "PREFERRED STOCK" => "ACCION",
        var x => x.Length == 0 ? "OTRO" : x,
    };

    private static string? Cadena(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
}
