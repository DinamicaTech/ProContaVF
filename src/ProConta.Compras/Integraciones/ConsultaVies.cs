using System.Net.Http;
using System.Text.Json;
using ProConta.Compras.Proveedores;

namespace ProConta.Compras.Integraciones;

/// <summary>
/// VIES de la Comisión Europea (servicio REST público, sin claves; aprobado por el propietario 2026-11-07 20:25): dice si
/// un NIF-IVA comunitario está dado de alta para operaciones intracomunitarias y, según el país, su nombre. Alemania y
/// algún otro país no dan nunca el nombre («---»).
/// </summary>
public sealed class ConsultaViesRest(HttpClient http) : IConsultaVies
{
    public const string Endpoint = "https://ec.europa.eu/taxation_customs/vies/rest-api/ms/{0}/vat/{1}";

    public async Task<ResultadoVies> ConsultarAsync(string pais, string numero, CancellationToken cancelacion = default)
    {
        using var corto = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        corto.CancelAfter(TimeSpan.FromSeconds(15));
        using var respuesta = await http.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Endpoint, pais, Uri.EscapeDataString(numero)), corto.Token);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(corto.Token);
        return Interpretar(cuerpo, (int)respuesta.StatusCode);
    }

    /// <summary>Respuesta del VIES: válido o no, con el nombre si lo da; un error del servicio o del país es «sin consultar».</summary>
    internal static ResultadoVies Interpretar(string json, int estado)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var error = r.TryGetProperty("userError", out var e) ? e.GetString() : null;
            if (error is "MS_UNAVAILABLE" or "TIMEOUT" or "SERVICE_UNAVAILABLE" or "MS_MAX_CONCURRENT_REQ" or "GLOBAL_MAX_CONCURRENT_REQ" or "INVALID_REQUESTER_INFO")
                return ResultadoVies.SinConsultar($"El VIES no responde ahora ({error}); se volverá a consultar.");
            if (!r.TryGetProperty("isValid", out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return ResultadoVies.SinConsultar($"Respuesta del VIES no reconocida (HTTP {estado}).");
            var nombre = r.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
            if (nombre is null or "" or "---") nombre = null;
            return new ResultadoVies(true, v.GetBoolean(), nombre, null);
        }
        catch (JsonException)
        {
            return ResultadoVies.SinConsultar($"Respuesta del VIES no reconocida (HTTP {estado}).");
        }
    }
}
