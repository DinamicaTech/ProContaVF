using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace ProConta.Documentos;

/// <summary>
/// Texto de un fichero para proponer sus datos: el de las primeras páginas de un PDF con texto (PdfPig), el de un HTML o un TXT. Las imágenes y los
/// PDF escaneados no tienen texto (no se hace OCR aquí): la propuesta lo dice. Nunca lanza: si no puede leerlo, devuelve vacío.
/// </summary>
public static class TextoFichero
{
    public const int PaginasLeidas = 3;

    public static string Leer(string ruta)
    {
        try
        {
            var ext = Path.GetExtension(ruta).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => LeerPdf(ruta),
                ".htm" or ".html" => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(File.ReadAllText(ruta), @"<(script|style)[^>]*>.*?</\1>", " ",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase), "<[^>]+>", " ")),
                ".txt" or ".csv" or ".xml" => File.ReadAllText(ruta),
                _ => "",
            };
        }
        catch (Exception) { return ""; }
    }

    private static string LeerPdf(string ruta)
    {
        using var pdf = PdfDocument.Open(ruta);
        var sb = new StringBuilder();
        foreach (var pagina in pdf.GetPages().Take(PaginasLeidas))
        {
            // Las palabras con un espacio entre ellas: el texto «tal cual» de PdfPig a veces junta palabras de líneas distintas.
            sb.AppendJoin(' ', pagina.GetWords().Select(w => w.Text));
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
