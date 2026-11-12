using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProConta.Activos.Subcuentas;

/// <summary>
/// Reglas de las subcuentas generadas por plantilla (activos 2026-11-12 11:45): el nombre sale del tipo de subcuenta con su
/// [TOKEN] sustituido por el nombre del activo, y un renombre se marca como «muy distinto» si el parecido baja del 65 %.
/// </summary>
public static partial class ReglasSubcuentaActivo
{
    /// <summary>Longitud máxima del nombre de una cuenta (ReglasCuenta.LongitudMaximaNombre).</summary>
    public const int LongitudMaximaNombre = 150;

    /// <summary>Parecido por debajo del cual un renombre se marca en la propuesta (el ERP antiguo se paraba).</summary>
    public const double ParecidoMinimo = 0.65;

    [GeneratedRegex(@"\[[^\[\]]*\]")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>Si el nombre del tipo de subcuenta lleva un [TOKEN] donde va el nombre del activo.</summary>
    public static bool TieneToken(string nombreTipo) => Token().IsMatch(nombreTipo ?? "");

    /// <summary>Nombre de la subcuenta: el del tipo con cada [TOKEN] sustituido por el nombre del activo, sin espacios repetidos.</summary>
    public static string Nombre(string nombreTipo, string nombreActivo)
    {
        var activo = (nombreActivo ?? "").Trim();
        var nombre = Espacios().Replace(Token().Replace(nombreTipo ?? "", _ => activo), " ").Trim();
        return nombre.Length <= LongitudMaximaNombre ? nombre : nombre[..LongitudMaximaNombre].TrimEnd();
    }

    /// <summary>Parecido entre dos nombres (1 iguales, 0 nada en común), sin distinguir mayúsculas ni acentos.</summary>
    public static double Parecido(string? a, string? b)
    {
        var x = Plano(a);
        var y = Plano(b);
        if (x.Length == 0 && y.Length == 0) return 1;
        var maximo = Math.Max(x.Length, y.Length);
        return 1.0 - (double)Distancia(x, y) / maximo;
    }

    public static bool MuyDistinto(string? actual, string? nuevo) => Parecido(actual, nuevo) < ParecidoMinimo;

    private static string Plano(string? texto)
    {
        var d = Espacios().Replace((texto ?? "").Trim(), " ").ToLower(CultureInfo.InvariantCulture).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>Distancia de edición (Levenshtein).</summary>
    private static int Distancia(string a, string b)
    {
        var previa = new int[b.Length + 1];
        var actual = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previa[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            actual[0] = i;
            for (var j = 1; j <= b.Length; j++)
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, previa[j] + 1), previa[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previa, actual) = (actual, previa);
        }
        return previa[b.Length];
    }
}
