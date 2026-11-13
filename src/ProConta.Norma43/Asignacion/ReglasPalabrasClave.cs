using System.Globalization;
using System.Text;

namespace ProConta.Norma43.Asignacion;

/// <summary>Palabra clave bancaria (PalabrasClaveBancarias) lista para comparar.</summary>
public sealed record PalabraClaveN43(int Id, string? Entidad, string? Sentido, string PalabraClave, string? PalabraClave2, int? IdIdentidadContable, int? Especial)
{
    /// <summary>Especial = 5: remesa de recibos que se explota después (norma43/remesas); la línea toma su cuenta como cualquier otra.</summary>
    public bool EsRemesa => Especial == ReglasPalabrasClave.EspecialRemesa;
    /// <summary>Trozos normalizados de cada texto: el asterisco es un comodín (los trozos deben aparecer en ese orden).</summary>
    internal IReadOnlyList<string> Trozos1 { get; } = ReglasPalabrasClave.Trozos(PalabraClave);
    internal IReadOnlyList<string> Trozos2 { get; } = ReglasPalabrasClave.Trozos(PalabraClave2);
    internal int Letras => Trozos1.Sum(t => t.Length) + Trozos2.Sum(t => t.Length);
    /// <summary>Sentido de las líneas a las que se aplica (D cargo, H abono), o null si vale para los dos.</summary>
    internal string? SentidoLinea { get; } = ReglasPalabrasClave.SentidoDeLinea(Sentido);
}

/// <summary>Movimiento bancario que se quiere imputar: lo que hace falta para buscarle palabra clave.</summary>
public sealed record LineaN43ParaAsignar(int IdLinea, string Entidad, string Sentido, decimal Importe, string? ConceptoCompleto, string? Documento, string? Referencia1, string? Referencia2)
{
    /// <summary>Texto en que se buscan las palabras clave: el concepto completo, las referencias y el documento.</summary>
    internal string Texto { get; } = ReglasPalabrasClave.Normalizar(string.Join(" ", ConceptoCompleto, Referencia1, Referencia2, Documento));
}

/// <summary>Qué palabra clave le toca a una línea.</summary>
public enum ResultadoBusqueda
{
    /// <summary>Ninguna palabra clave encaja.</summary>
    SinPalabra,
    /// <summary>Una palabra clave encaja y es la más concreta: se toma.</summary>
    Encontrada,
    /// <summary>Encajan varias igual de concretas con cuentas distintas: no se elige ninguna.</summary>
    Ambigua,
}

/// <summary>Palabra clave elegida para una línea (o por qué no hay).</summary>
public sealed record EleccionPalabra(ResultadoBusqueda Resultado, PalabraClaveN43? Palabra, IReadOnlyList<PalabraClaveN43> Empatadas)
{
    public static readonly EleccionPalabra Ninguna = new(ResultadoBusqueda.SinPalabra, null, []);
}

/// <summary>
/// Reglas de las palabras clave bancarias (norma43/asignacion-cuentas). Una palabra clave encaja con una línea si es de la
/// misma entidad (o no tiene entidad), del mismo sentido (o no tiene sentido) y el texto de la línea contiene la palabra clave
/// y, si la tiene, también la segunda (Y lógico); un asterisco dentro de una palabra clave es un comodín, como en el ERP. La comparación no distingue mayúsculas, acentos, espacios ni signos: solo
/// cuentan letras y cifras, porque las palabras clave se copiaron de los conceptos del ERP, que guardaba los espacios y los
/// acentos a su manera. Si encajan varias, gana la más concreta: la que tiene segunda palabra, después la que es de la
/// entidad, después la de más letras en total; si empatan con cuentas distintas, la línea se queda sin asignar (ambigua).
/// </summary>
public static class ReglasPalabrasClave
{
    /// <summary>Valor de Especial de las palabras clave de remesas de recibos (se explotan en norma43/remesas).</summary>
    public const int EspecialRemesa = 5;

    /// <summary>
    /// Sentido de las líneas al que se aplica el sentido guardado en la palabra clave. La palabra clave lo guarda desde el punto
    /// de vista de la contabilidad del banco (ANALISIS-FUNCIONAL §10.2): D (debe de la 572) son los abonos, que en la línea son H,
    /// y H (haber de la 572) son los cargos, D en la línea. Los códigos del cuaderno (1 cargo, 2 abono) se aceptan tal cual.
    /// </summary>
    public static string? SentidoDeLinea(string? sentidoPalabra) => (sentidoPalabra ?? "").Trim().ToUpperInvariant() switch
    {
        "D" => "H",
        "H" => "D",
        "1" => "D",
        "2" => "H",
        _ => null,
    };

    /// <summary>Texto comparable: mayúsculas, sin acentos y solo letras y cifras. Repara antes el UTF-8 leído como Windows-1252.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var t = RepararMojibake(texto).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Trozos comparables de una palabra clave separados por el comodín «*» (como en el ERP), sin los vacíos.</summary>
    public static IReadOnlyList<string> Trozos(string? palabra) =>
        string.IsNullOrWhiteSpace(palabra) ? [] : palabra.Split('*').Select(Normalizar).Where(t => t.Length > 0).ToList();

    /// <summary>True si el texto contiene todos los trozos en orden.</summary>
    internal static bool Contiene(string texto, IReadOnlyList<string> trozos)
    {
        var desde = 0;
        foreach (var t in trozos)
        {
            var i = texto.IndexOf(t, desde, StringComparison.Ordinal);
            if (i < 0) return false;
            desde = i + t.Length;
        }
        return true;
    }

    /// <summary>«CAMIÃ³N» → «CAMIóN»: el ERP leyó el UTF-8 del banco como Windows-1252 y algunas palabras clave se copiaron así.</summary>
    internal static string RepararMojibake(string texto)
    {
        if (texto.IndexOf('Ã') < 0 && texto.IndexOf('Â') < 0) return texto;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var w1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return new UTF8Encoding(false, true).GetString(w1252.GetBytes(texto));
        }
        catch (Exception e) when (e is DecoderFallbackException or EncoderFallbackException or ArgumentException)
        {
            return texto;
        }
    }

    /// <summary>True si la palabra clave encaja con la línea (entidad, sentido y textos).</summary>
    public static bool Encaja(PalabraClaveN43 p, LineaN43ParaAsignar l)
    {
        if (p.Trozos1.Count == 0) return false;
        if (!string.IsNullOrWhiteSpace(p.Entidad) && !string.Equals(p.Entidad.Trim(), l.Entidad.Trim(), StringComparison.Ordinal)) return false;
        if (p.SentidoLinea is { } s && !string.Equals(s, l.Sentido.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return Contiene(l.Texto, p.Trozos1) && Contiene(l.Texto, p.Trozos2);
    }

    /// <summary>Elige la palabra clave de una línea entre las que encajan (ver la regla de la clase).</summary>
    public static EleccionPalabra Elegir(LineaN43ParaAsignar linea, IEnumerable<PalabraClaveN43> palabras)
    {
        var candidatas = palabras.Where(p => Encaja(p, linea)).ToList();
        if (candidatas.Count == 0) return EleccionPalabra.Ninguna;
        var mejor = candidatas.Max(Concrecion);
        var empatadas = candidatas.Where(p => Concrecion(p) == mejor).OrderBy(p => p.Id).ToList();
        var destinos = empatadas.Select(Destino).Distinct().Count();
        return destinos == 1
            ? new EleccionPalabra(ResultadoBusqueda.Encontrada, empatadas[0], empatadas)
            : new EleccionPalabra(ResultadoBusqueda.Ambigua, null, empatadas);
    }

    private static (int, int, int) Concrecion(PalabraClaveN43 p) =>
        (p.Trozos2.Count > 0 ? 1 : 0, string.IsNullOrWhiteSpace(p.Entidad) ? 0 : 1, p.Letras);

    // Dos palabras clave empatadas que llevan a la misma cuenta no son ambiguas.
    private static string Destino(PalabraClaveN43 p) => p.IdIdentidadContable?.ToString(CultureInfo.InvariantCulture) ?? "SIN CUENTA";
}
