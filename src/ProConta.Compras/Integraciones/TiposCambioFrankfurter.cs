using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ProConta.Compras.Reconocimiento;

namespace ProConta.Compras.Integraciones;

/// <summary>
/// Cambio oficial del BCE con Frankfurter (api.frankfurter.app, sin clave; aprobado en compras/reconocimiento 2026-11-05 11:24).
/// Para una fecha sin cambio publicado (fin de semana o festivo), Frankfurter da el del último día hábil anterior; si ese
/// día queda a más de 7 días, no se usa. Sin conexión o con una respuesta que no se entiende, devuelve null y la factura
/// queda pidiendo el cambio.
/// </summary>
public sealed class TiposCambioFrankfurter(HttpClient http, Uri? direccion = null) : IProveedorTiposCambioHistoricos
{
    private readonly Uri _base = direccion ?? new Uri("https://api.frankfurter.app/");

    public async Task<TipoCambioHistorico?> ObtenerAsync(string divisaOrigen, string divisaDestino, DateTime fecha, CancellationToken cancelacion)
    {
        var origen = divisaOrigen.Trim().ToUpperInvariant();
        var destino = divisaDestino.Trim().ToUpperInvariant();
        if (origen.Length != 3 || destino.Length != 3 || !origen.All(char.IsAsciiLetterUpper) || !destino.All(char.IsAsciiLetterUpper)) return null;
        if (origen == destino) return new TipoCambioHistorico(fecha.Date, origen, destino, 1m);
        var uri = new Uri(_base, $"{fecha:yyyy-MM-dd}?from={origen}&to={destino}");
        try
        {
            using var respuesta = await http.GetAsync(uri, cancelacion).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode) return null;
            await using var flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(flujo, cancellationToken: cancelacion).ConfigureAwait(false);
            var raiz = doc.RootElement;
            if (!raiz.TryGetProperty("date", out var d) || !DateTime.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var efectiva)) return null;
            if (efectiva.Date > fecha.Date || (fecha.Date - efectiva.Date).TotalDays > 7) return null;
            if (!raiz.TryGetProperty("rates", out var tasas) || !tasas.TryGetProperty(destino, out var t) || !t.TryGetDecimal(out var tipo) || tipo <= 0) return null;
            var importe = raiz.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var am) && am > 0 ? am : 1m;
            return new TipoCambioHistorico(efectiva.Date, origen, destino, Math.Round(tipo / importe, 6, MidpointRounding.AwayFromZero));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancelacion.IsCancellationRequested)
        {
            return null;
        }
    }
}
