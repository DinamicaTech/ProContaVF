namespace ProConta.Activos.Financieros;

/// <summary>
/// Código ISIN (ISO 6166): dos letras de país, nueve caracteres alfanuméricos y un dígito de control (activos/activos-financieros 2026-11-23 20:32, D:
/// hace falta un ISIN validado para consultar las cotizaciones). El dígito de control se comprueba con el algoritmo de Luhn sobre los dígitos que
/// resultan de cambiar cada letra por su número (A = 10 … Z = 35).
/// </summary>
public static class Isin
{
    public const int Longitud = 12;

    /// <summary>El ISIN en mayúsculas y sin espacios ni guiones.</summary>
    public static string Normalizar(string? texto) =>
        string.Concat((texto ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();

    /// <summary>Qué le pasa al ISIN, con cómo arreglarlo; null si es válido.</summary>
    public static string? Error(string? texto)
    {
        var isin = Normalizar(texto);
        if (isin.Length == 0) return "Escribe el ISIN del fondo o del ETF: son 12 caracteres, por ejemplo IE00B4L5Y983. Está en el extracto del bróker o en la ficha del fondo.";
        if (isin.Length != Longitud) return $"El ISIN tiene {isin.Length} caracteres y deben ser {Longitud} (dos letras del país, nueve letras o cifras y un dígito de control): revísalo.";
        if (!char.IsAsciiLetterUpper(isin[0]) || !char.IsAsciiLetterUpper(isin[1])) return "El ISIN empieza por las dos letras del país (por ejemplo IE, LU o ES): revisa los dos primeros caracteres.";
        if (!isin[2..11].All(char.IsAsciiLetterOrDigit)) return "Entre el país y el último dígito solo puede haber letras y cifras: revisa el ISIN.";
        if (!char.IsAsciiDigit(isin[11])) return "El último carácter del ISIN es un dígito de control, siempre una cifra: revisa el ISIN.";
        return DigitoControl(isin[..11]) == isin[11] - '0' ? null
            : "El dígito de control del ISIN no cuadra: probablemente hay un carácter mal copiado. Cópialo de nuevo del extracto del bróker.";
    }

    public static bool EsValido(string? texto) => Error(texto) is null;

    /// <summary>Dígito de control de los once primeros caracteres.</summary>
    public static int DigitoControl(string once)
    {
        var digitos = string.Concat(once.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var suma = 0;
        var doblar = true; // desde la derecha, el primero se dobla (el de control iría a su derecha)
        for (var i = digitos.Length - 1; i >= 0; i--)
        {
            var d = digitos[i] - '0';
            if (doblar) { d *= 2; if (d > 9) d -= 9; }
            suma += d;
            doblar = !doblar;
        }
        return (10 - suma % 10) % 10;
    }
}
