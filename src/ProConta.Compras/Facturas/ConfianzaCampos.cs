using System.Text.Json;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Confianza y avisos de cada campo reconocido, leídos del log del reconocimiento (FacturasCompra.LOGImportacion,
/// versión 1). Sin log (facturas del ERP o escritas a mano), no hay confianza que enseñar.
/// </summary>
public sealed record ConfianzaCampos(decimal? Nif, decimal? Numero, decimal? Fecha, decimal? Importes, decimal? Global, IReadOnlyList<string> Avisos, string? Fuente)
{
    public static ConfianzaCampos Ninguna { get; } = new(null, null, null, null, null, [], null);

    /// <summary>Campos leídos con una plantilla del proveedor (nif, numero, fecha, total).</summary>
    public IReadOnlyList<string> Plantilla { get; init; } = [];

    public bool EsPlantilla(string campo) => Plantilla.Contains(campo);

    public static ConfianzaCampos DesdeLog(string? log)
    {
        if (string.IsNullOrWhiteSpace(log)) return Ninguna;
        try
        {
            using var doc = JsonDocument.Parse(log);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return Ninguna;
            var candidato = raiz.TryGetProperty("candidato", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
            decimal? D(JsonElement e, string nombre) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
            var avisos = raiz.TryGetProperty("advertencias", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
            var fuente = raiz.TryGetProperty("fuenteElegida", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            var plantilla = candidato.ValueKind == JsonValueKind.Object && candidato.TryGetProperty("CamposPlantilla", out var cp) && cp.ValueKind == JsonValueKind.Array
                ? cp.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
            return new ConfianzaCampos(D(candidato, "ConfianzaNif"), D(candidato, "ConfianzaNumero"), D(candidato, "ConfianzaFecha"),
                D(candidato, "ConfianzaImportes"), D(raiz, "confianza"), avisos, fuente) { Plantilla = plantilla };
        }
        catch (JsonException)
        {
            return Ninguna;
        }
    }
}
