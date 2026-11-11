using System.Text.RegularExpressions;
using ProConta.Datos.Persistencia;

namespace ProConta.Correo;

/// <summary>Asunto y cuerpo de una plantilla, con su formato.</summary>
public sealed record PlantillaCorreo(string Asunto, string Cuerpo, bool EsHtml);

/// <summary>
/// Plantillas de correo por empresa y tipo (PlantillasCorreo). Las variables van entre llaves ({NumeroFactura}); las que el
/// origen no puede rellenar se quedan tal cual, para que se vean en el compositor. Sin plantilla activa, se usa la del programa.
/// </summary>
public sealed class ServicioPlantillasCorreo(IBaseDatos bd)
{
    /// <summary>Plantillas del programa para cuando la empresa no tiene la suya.</summary>
    public static PlantillaCorreo Predeterminada(string tipoCorreo) => tipoCorreo switch
    {
        TiposCorreo.Factura => new PlantillaCorreo(
            "Factura {NumeroFactura} de {Empresa}",
            "<p>Hola:</p><p>Te enviamos adjunta la factura {NumeroFactura} del {FechaFactura}, por un importe de {Importe}.</p><p>Un saludo,<br>{Empresa}</p>",
            true),
        _ => new PlantillaCorreo("", "", true),
    };

    /// <summary>Variables que entiende cada tipo, para la ayuda del mantenimiento.</summary>
    public const string VariablesFactura = "{NumeroFactura}, {FechaFactura}, {Importe}, {Cliente}, {Empresa}, {Ejercicio}";

    /// <summary>La plantilla activa de la empresa para el tipo, o la del programa.</summary>
    public async Task<PlantillaCorreo> ObtenerAsync(string idSociedad, string tipoCorreo)
    {
        var p = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT Asunto, Cuerpo, FormatoCuerpo FROM PlantillasCorreo WHERE IdSociedad = ? AND TipoCorreo = ? AND Activo = True",
            f => new PlantillaCorreo(f.Texto("Asunto") ?? "", f.Texto("Cuerpo") ?? "", !string.Equals(f.Texto("FormatoCuerpo"), "TEXTO", StringComparison.OrdinalIgnoreCase)),
            idSociedad, tipoCorreo));
        return p ?? Predeterminada(tipoCorreo);
    }

    /// <summary>Sustituye las variables conocidas; en HTML, los valores se escapan.</summary>
    public static string Rellenar(string plantilla, IReadOnlyDictionary<string, string?> valores, bool html) =>
        Regex.Replace(plantilla, @"\{(\w+)\}", m =>
            valores.TryGetValue(m.Groups[1].Value, out var v) && v is not null ? (html ? System.Net.WebUtility.HtmlEncode(v) : v) : m.Value);

    /// <summary>Variables que siguen sin rellenar (entre llaves), para avisar antes de enviar.</summary>
    public static IReadOnlyList<string> SinRellenar(string? texto) =>
        texto is null ? [] : Regex.Matches(texto, @"\{(\w+)\}").Select(m => m.Value).Distinct().ToList();

    /// <summary>Un texto plano convertido en HTML sencillo (párrafos y saltos de línea).</summary>
    public static string HtmlDeTexto(string texto) =>
        string.Join("", texto.Replace("\r\n", "\n").Split("\n\n").Select(p => "<p>" + System.Net.WebUtility.HtmlEncode(p).Replace("\n", "<br>") + "</p>"));
}
