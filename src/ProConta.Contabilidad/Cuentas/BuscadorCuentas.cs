using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Cuentas;

/// <summary>
/// Reglas del buscador de cuentas (contabilidad/cuenta-contable 2026-11-06): búsqueda por código, con el punto de relleno
/// («628.11» = 6280011), o por cualquier parte del nombre; filtro por grupos que se puede quitar; y siguiente código libre
/// de una cuenta para dar de alta una subcuenta.
/// </summary>
public static class BuscadorCuentas
{
    /// <summary>Longitud de las subcuentas cuando la cuenta aún no tiene ninguna.</summary>
    public const int LongitudSubcuenta = 7;

    /// <summary>
    /// «628.11» con longitud 7 da «6280011»: lo de la izquierda del punto, ceros y lo de la derecha. Sin punto, o si no
    /// cabe, null.
    /// </summary>
    public static string? ExpandirPunto(string? texto, int longitud = LongitudSubcuenta)
    {
        var t = (texto ?? "").Trim();
        var punto = t.IndexOf('.');
        if (punto < 0 || punto != t.LastIndexOf('.')) return null;
        var izquierda = t[..punto];
        var derecha = t[(punto + 1)..];
        if (!(izquierda + derecha).All(char.IsAsciiDigit) || izquierda.Length == 0 || izquierda.Length + derecha.Length > longitud) return null;
        return izquierda + new string('0', longitud - izquierda.Length - derecha.Length) + derecha;
    }

    /// <summary>
    /// Las cuentas que admiten apuntes (activas, no bloqueadas e imputables), de los <paramref name="grupos"/> si se dan
    /// (prefijos de código), que coinciden con <paramref name="texto"/>: por el principio del código (con el punto
    /// expandido) si son cifras, o por cualquier parte del nombre sin distinguir mayúsculas ni acentos.
    /// </summary>
    public static IReadOnlyList<CuentaContable> Filtrar(IEnumerable<CuentaContable> cuentas, string? texto, IReadOnlyList<string>? grupos)
    {
        var t = (texto ?? "").Trim();
        var conGrupos = grupos is { Count: > 0 };
        var q = cuentas.Where(c => c.EstaActiva && !c.EstaBloqueada && c.EsImputable && (!conGrupos || grupos!.Any(g => c.Codigo.StartsWith(g, StringComparison.Ordinal))));
        if (t.Length > 0)
        {
            if (t.All(char.IsAsciiDigit))
                q = q.Where(c => c.Codigo.StartsWith(t, StringComparison.Ordinal));
            else if (t.Contains('.') && t.Replace(".", "").All(char.IsAsciiDigit))
                q = q.Where(c => ExpandirPunto(t, c.Codigo.Length) is { } codigo && c.Codigo == codigo);
            else
            {
                var buscado = Plano(t);
                q = q.Where(c => Plano(c.Nombre).Contains(buscado, StringComparison.Ordinal) || c.Codigo.StartsWith(t, StringComparison.Ordinal));
            }
        }
        return q.OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Siguiente código libre de la cuenta <paramref name="cuenta"/> (tres cifras): uno más que la subcuenta más alta
    /// que empieza por ella, saltando los ocupados (los del ejercicio y los reservados en la sociedad). La longitud es la
    /// más frecuente entre sus subcuentas, o 7. Null si no queda ninguno.
    /// </summary>
    public static string? SiguienteCodigo(string cuenta, IEnumerable<string> ocupados)
    {
        var usados = new HashSet<string>(ocupados.Where(c => c.StartsWith(cuenta, StringComparison.Ordinal) && c.All(char.IsAsciiDigit)), StringComparer.Ordinal);
        var hermanos = usados.Where(c => c.Length > cuenta.Length).ToList();
        var longitud = hermanos.Count == 0 ? LongitudSubcuenta
            : hermanos.GroupBy(c => c.Length).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var cifras = longitud - cuenta.Length;
        if (cifras <= 0) return null;
        var maximo = (long)Math.Pow(10, cifras) - 1;
        var desde = hermanos.Where(c => c.Length == longitud).Select(c => long.Parse(c[cuenta.Length..], CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
        for (var n = desde; n <= maximo; n++)
        {
            var codigo = cuenta + n.ToString(CultureInfo.InvariantCulture).PadLeft(cifras, '0');
            if (!usados.Contains(codigo)) return codigo;
        }
        // Al final de la numeración, el primer hueco libre.
        for (var n = 1L; n < desde && n <= maximo; n++)
        {
            var codigo = cuenta + n.ToString(CultureInfo.InvariantCulture).PadLeft(cifras, '0');
            if (!usados.Contains(codigo)) return codigo;
        }
        return null;
    }

    /// <summary>Minúsculas y sin acentos, para comparar.</summary>
    public static string Plano(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
