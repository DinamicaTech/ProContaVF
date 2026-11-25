using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>Un texto del PDF con su posición (puntos PDF, origen abajo a la izquierda) y su letra.</summary>
public sealed record PalabraPdf(int Pagina, string Texto, double Izquierda, double Derecha, double Base, string Fuente, double Tamano);

/// <summary>
/// Lo leído del PDF justificante de una presentación a la AEAT (el que guarda la gestoría o se descarga de la Sede): los datos de la
/// primera hoja («Información de la presentación de la declaración») y las casillas con importe del modelo.
/// </summary>
/// <param name="Casillas">Casilla → importe, con el código tal como sale impreso («27», «01586»); la primera vez que sale si se repite.</param>
/// <param name="ImporteAIngresar">El «IMPORTE» de la primera hoja cuando la declaración sale a ingresar; 0 si no.</param>
public sealed record JustificanteAeat(string Modelo, int? Ejercicio, string? Periodo, string? Nif, string Justificante, string Csv, string? Expediente,
    DateTime? FechaHora, string? NifPresentador, string? Presentador, string? TipoResultado, decimal ImporteAIngresar, IReadOnlyDictionary<string, decimal> Casillas)
{
    /// <summary>El importe de una casilla, aceptando «1» o «01» para la misma; null si no sale en el justificante (vacía).</summary>
    public decimal? Casilla(string codigo)
    {
        if (Casillas.TryGetValue(codigo, out var v)) return v;
        var n = codigo.TrimStart('0');
        foreach (var kv in Casillas)
            if (kv.Key.TrimStart('0') == n && n.Length > 0) return kv.Value;
        return null;
    }
}

/// <summary>
/// Lee el PDF justificante de una presentación a la AEAT (contabilidad/modelos-tributarios/presentacion 2026-11-25): de la primera hoja,
/// modelo, fecha y hora, expediente, CSV, justificante y presentador; de las hojas del modelo, ejercicio, periodo, NIF del declarante y
/// las casillas con importe.
/// <para>Cómo: en los PDF de la AEAT los datos rellenados van en Nimbus Sans y los números de casilla en News Gothic, en la misma línea y
/// a la izquierda del importe. Cada importe se asigna al número de casilla más cercano a su izquierda en su línea. Por eso sirve para
/// todos los modelos de presentación directa (303, 111, 115, 390, 200…) y para la hoja resumen de las informativas (349…).</para>
/// </summary>
public static partial class LectorJustificanteAeat
{
    /// <summary>Lee el PDF; null si no es un justificante de la AEAT (no tiene CSV ni número de justificante en la primera hoja).</summary>
    public static JustificanteAeat? Leer(byte[] pdf)
    {
        List<PalabraPdf> palabras = [];
        try
        {
            using var doc = PdfDocument.Open(pdf);
            foreach (var p in doc.GetPages())
                palabras.AddRange(p.GetWords().Where(w => w.Letters.Count > 0).Select(w => De(p, w)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        return Leer(palabras);
    }

    private static PalabraPdf De(Page p, Word w) =>
        new(p.Number, w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, w.Letters[0].FontName ?? "", w.Letters[0].PointSize);

    /// <summary>El texto de una hoja línea a línea (de arriba abajo y de izquierda a derecha), con las palabras separadas por un espacio.</summary>
    public static string TextoPorLineas(IEnumerable<PalabraPdf> hoja) =>
        string.Join(" ", Lineas(hoja).SelectMany(l => l).Select(p => p.Texto));

    private static List<List<PalabraPdf>> Lineas(IEnumerable<PalabraPdf> hoja)
    {
        var lineas = new List<List<PalabraPdf>>();
        foreach (var p in hoja.OrderByDescending(p => p.Base))
        {
            if (lineas.Count > 0 && Math.Abs(lineas[^1][0].Base - p.Base) <= 3.0) lineas[^1].Add(p);
            else lineas.Add([p]);
        }
        foreach (var l in lineas) l.Sort((a, b) => a.Izquierda.CompareTo(b.Izquierda));
        return lineas;
    }

    /// <summary>Lee a partir de las palabras del PDF con su posición (separado del PDF para las pruebas).</summary>
    public static JustificanteAeat? Leer(IReadOnlyList<PalabraPdf> palabras)
    {
        if (palabras.Count == 0) return null;
        var primeraPagina = palabras.Min(p => p.Pagina);
        var t = Regex.Replace(TextoPorLineas(palabras.Where(p => p.Pagina == primeraPagina)), @"\s+", " ");
        var csv = ReCsv().Match(t) is { Success: true } mc ? mc.Groups[1].Value : null;
        var just = ReJustificante().Match(t) is { Success: true } mj ? mj.Groups[1].Value : null;
        var modelo = ReModelo().Match(t) is { Success: true } mm ? mm.Groups[1].Value : null;
        if (csv is null || just is null || modelo is null) return null;
        DateTime? fecha = ReFecha().Match(t) is { Success: true } mf && DateTime.TryParseExact($"{mf.Groups[1].Value} {mf.Groups[2].Value}", "dd-MM-yyyy HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
        var expediente = ReExpediente().Match(t) is { Success: true } me ? me.Groups[1].Value : null;
        var nifPres = ReNifPresentador().Match(t) is { Success: true } mn ? mn.Groups[1].Value : null;
        var pres = RePresentador().Match(t) is { Success: true } mp ? mp.Groups[1].Value.Trim() : null;
        var importe = ReImporte().Match(t) is { Success: true } mi && Importe(mi.Groups[1].Value.Replace(" ", ".")) is decimal imp ? imp : 0m;
        var tipo = t.Contains("DOMICILIACI", StringComparison.Ordinal) ? "Domiciliación"
            : t.Contains("INGRESAR", StringComparison.Ordinal) ? "A ingresar"
            : t.Contains("DEVOLUCI", StringComparison.Ordinal) || t.Contains("DEVOLVER", StringComparison.Ordinal) ? "A devolver"
            : t.Contains("COMPENSAR", StringComparison.Ordinal) ? "A compensar"
            : t.Contains("NEGATIVA", StringComparison.Ordinal) || t.Contains("RESULTADO CERO", StringComparison.Ordinal) ? "Negativa o sin actividad"
            : null;

        // Ejercicio, periodo y NIF del declarante: los primeros que salen, de arriba abajo, en las hojas del modelo.
        var datos = palabras.Where(p => p.Pagina > primeraPagina && EsDato(p)).OrderBy(p => p.Pagina).ThenByDescending(p => Math.Round(p.Base)).ThenBy(p => p.Izquierda).ToList();
        int? ejercicio = datos.Select(p => p.Texto).FirstOrDefault(x => ReEjercicio().IsMatch(x)) is { } e ? int.Parse(e, CultureInfo.InvariantCulture) : null;
        var periodo = datos.Select(p => p.Texto).FirstOrDefault(x => RePeriodo().IsMatch(x));
        var nif = datos.Select(p => p.Texto).FirstOrDefault(x => ReNif().IsMatch(x));
        return new JustificanteAeat(modelo, ejercicio, periodo ?? (EsAnual(modelo) ? "0A" : null), nif, just, csv, expediente, fecha, nifPres, pres, tipo, importe,
            Casillas(palabras));
    }

    /// <summary>Modelos anuales (periodo «0A»).</summary>
    public static bool EsAnual(string modelo) => modelo is "390" or "200" or "347" or "190" or "180" or "193";

    /// <summary>Casilla → importe: cada dato numérico con el número de casilla más cercano a su izquierda en la misma línea.</summary>
    public static IReadOnlyDictionary<string, decimal> Casillas(IReadOnlyList<PalabraPdf> palabras)
    {
        var r = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var pagina in palabras.GroupBy(p => p.Pagina).OrderBy(g => g.Key))
        {
            var etiquetas = pagina.Where(EsEtiqueta).ToList();
            var datos = Unir(pagina.Where(EsDato));
            foreach (var v in datos)
            {
                if (Importe(v.Texto) is not decimal importe) continue;
                var etiqueta = etiquetas
                    .Where(e => Math.Abs(e.Base - v.Base) <= 4.0 && e.Derecha <= v.Izquierda + 0.5)
                    .OrderBy(e => v.Izquierda - e.Derecha)
                    .FirstOrDefault();
                if (etiqueta is null) continue;
                // Un dato entre la etiqueta y este (p. ej. la base antes del tipo) ya tiene esa etiqueta: no se repite.
                if (datos.Any(o => o != v && Math.Abs(o.Base - v.Base) <= 4.0 && o.Izquierda >= etiqueta.Derecha && o.Derecha <= v.Izquierda)) continue;
                r.TryAdd(etiqueta.Texto, importe);
            }
        }
        return r;
    }

    /// <summary>
    /// Los datos de una hoja por líneas, con los importes partidos por un espacio de miles («6 642,80», que el PDF da como dos palabras)
    /// unidos en uno.
    /// </summary>
    private static List<PalabraPdf> Unir(IEnumerable<PalabraPdf> datos)
    {
        var r = new List<PalabraPdf>();
        foreach (var linea in Lineas(datos))
            foreach (var p in linea)
            {
                if (r.Count > 0 && r[^1] is var a && Math.Abs(a.Base - p.Base) <= 3.0 && p.Izquierda - a.Derecha < 4.0
                    && ReMilesInicio().IsMatch(a.Texto) && ReMilesResto().IsMatch(p.Texto))
                    r[^1] = a with { Texto = a.Texto + "." + p.Texto, Derecha = p.Derecha };
                else r.Add(p);
            }
        return r;
    }

    /// <summary>Texto rellenado (Nimbus Sans, sin negrita), no el impreso del modelo.</summary>
    private static bool EsDato(PalabraPdf p) => p.Fuente.Contains("NimbusSan", StringComparison.OrdinalIgnoreCase) && !p.Fuente.Contains("Bold", StringComparison.OrdinalIgnoreCase);

    /// <summary>Número de casilla impreso en el modelo (News Gothic, de 2 a 5 cifras).</summary>
    private static bool EsEtiqueta(PalabraPdf p) => !EsDato(p) && !p.Fuente.Contains("NimbusSan", StringComparison.OrdinalIgnoreCase)
        && p.Texto.Length is >= 2 and <= 5 && p.Texto.All(char.IsAsciiDigit);

    /// <summary>«1.234,56», «-27,02» o «3» → decimal; null si no es un importe.</summary>
    public static decimal? Importe(string s)
    {
        s = s.Trim();
        if (!ReImporteTexto().IsMatch(s)) return null;
        return decimal.TryParse(s.Replace(".", "").Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    [GeneratedRegex(@"C[oó]digo Seguro de Verificaci[oó]n: ?([A-Z0-9]{16})")] private static partial Regex ReCsv();
    [GeneratedRegex(@"N[uú]mero de justificante: ?(\d{13})")] private static partial Regex ReJustificante();
    [GeneratedRegex(@"Modelo:? (\d{3})", RegexOptions.IgnoreCase)] private static partial Regex ReModelo();
    [GeneratedRegex(@"realizada el:? ?(\d{2}-\d{2}-\d{4}) a las (\d{2}:\d{2}:\d{2})")] private static partial Regex ReFecha();
    [GeneratedRegex(@"asignado\): ?(\S+)")] private static partial Regex ReExpediente();
    [GeneratedRegex(@"NIF Presentador: ?(\S+)")] private static partial Regex ReNifPresentador();
    [GeneratedRegex(@"Raz[oó]n social: ?(.+?) En calidad de")] private static partial Regex RePresentador();
    [GeneratedRegex(@"IMPORTE: ?(-?\d[\d\. ]*,\d{2})")] private static partial Regex ReImporte();
    [GeneratedRegex(@"^-?\d{1,3}$")] private static partial Regex ReMilesInicio();
    [GeneratedRegex(@"^\d{3}(\.\d{3})*(,\d{1,2})?$")] private static partial Regex ReMilesResto();
    [GeneratedRegex(@"^(19|20)\d{2}$")] private static partial Regex ReEjercicio();
    [GeneratedRegex(@"^([1-4]T|0A|[1-3]P)$")] private static partial Regex RePeriodo();
    [GeneratedRegex(@"^([A-HJ-NP-SUVW]\d{7}[0-9A-J]|\d{8}[A-Z]|[XYZ]\d{7}[A-Z])$")] private static partial Regex ReNif();
    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})*(,\d{1,2})?$|^-?\d+(,\d{1,2})?$")] private static partial Regex ReImporteTexto();
}
