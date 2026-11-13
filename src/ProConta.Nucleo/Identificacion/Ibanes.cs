namespace ProConta.Nucleo.Identificacion;

/// <summary>Formato y comprobación de los IBAN (ISO 13616): se guardan sin espacios y se muestran de cuatro en cuatro.</summary>
public static class Ibanes
{
    /// <summary>El IBAN sin espacios ni guiones y en mayúsculas; null si está vacío.</summary>
    public static string? Normalizar(string? iban)
    {
        var t = new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Si es un IBAN bien formado: país, dígitos de control correctos (módulo 97) y entre 15 y 34 caracteres.</summary>
    public static bool EsValido(string? iban)
    {
        var t = Normalizar(iban);
        if (t is null || t.Length is < 15 or > 34 || !char.IsAsciiLetter(t[0]) || !char.IsAsciiLetter(t[1]) || !char.IsAsciiDigit(t[2]) || !char.IsAsciiDigit(t[3])) return false;
        if (t.Any(c => !char.IsAsciiLetterOrDigit(c))) return false;
        if (t.StartsWith("ES", StringComparison.Ordinal) && t.Length != 24) return false;
        var resto = 0;
        foreach (var c in t[4..] + t[..4])
        {
            var v = char.IsAsciiDigit(c) ? (c - '0').ToString() : (c - 'A' + 10).ToString();
            foreach (var d in v) resto = (resto * 10 + (d - '0')) % 97;
        }
        return resto == 1;
    }

    /// <summary>El IBAN de cuatro en cuatro («ES12 2100 …»).</summary>
    public static string? Agrupar(string? iban) =>
        Normalizar(iban) is { } t ? DeCuatroEnCuatro(t) : null;

    /// <summary>El IBAN enmascarado, con el país y los cuatro últimos dígitos a la vista («ES** **** **** **** **** 8356»).</summary>
    public static string? Enmascarar(string? iban)
    {
        if (Normalizar(iban) is not { Length: > 8 } t) return null;
        // Sin pasar por Agrupar: Normalizar quitaría los asteriscos.
        return DeCuatroEnCuatro(t[..2] + new string('*', t.Length - 6) + t[^4..]);
    }

    private static string DeCuatroEnCuatro(string t) =>
        string.Join(" ", Enumerable.Range(0, (t.Length + 3) / 4).Select(i => t.Substring(i * 4, Math.Min(4, t.Length - i * 4))));

    /// <summary>IBAN español a partir del CCC (entidad, oficina, dígito de control y cuenta): calcula los dígitos de control del país.</summary>
    public static string? DesdeCcc(string? entidad, string? oficina, string? control, string? cuenta)
    {
        var ccc = $"{entidad?.Trim()}{oficina?.Trim()}{control?.Trim()}{cuenta?.Trim()}";
        if (ccc.Length != 20 || !ccc.All(char.IsAsciiDigit)) return null;
        var resto = 0;
        foreach (var d in ccc + "142800") resto = (resto * 10 + (d - '0')) % 97;
        return $"ES{98 - resto:00}{ccc}";
    }
}
