using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Formato de los registros de las declaraciones informativas (347, 349, 180, 190, 193): registros de 500 posiciones; alfanuméricos a
/// la izquierda con blancos, en mayúsculas, sin vocales acentuadas ni caracteres especiales (Ñ y Ç se conservan, ISO-8859-1); numéricos a
/// la derecha con ceros, sin signo ni coma; importes en euros con los céntimos en las dos últimas posiciones.
/// </summary>
public sealed class RegistroInformativo
{
    public const int Longitud = 500;
    private readonly char[] _r = Enumerable.Repeat(' ', Longitud).ToArray();

    /// <summary>Alfanumérico en las posiciones <paramref name="desde"/> a <paramref name="hasta"/> (1 a 500, como el diseño).</summary>
    public RegistroInformativo An(int desde, int hasta, string? texto)
    {
        var largo = hasta - desde + 1;
        var t = Limpiar(texto);
        Poner(desde, (t.Length > largo ? t[..largo] : t).PadRight(largo));
        return this;
    }

    /// <summary>Numérico entero a la derecha con ceros.</summary>
    public RegistroInformativo Num(int desde, int hasta, long valor)
    {
        if (valor < 0) throw new ArgumentOutOfRangeException(nameof(valor), $"El numérico de las posiciones {desde}-{hasta} no admite negativos.");
        var s = valor.ToString(CultureInfo.InvariantCulture);
        var largo = hasta - desde + 1;
        if (s.Length > largo) throw new ArgumentOutOfRangeException(nameof(valor), $"{valor} no cabe en las posiciones {desde}-{hasta}.");
        Poner(desde, s.PadLeft(largo, '0'));
        return this;
    }

    /// <summary>Importe sin signo en céntimos (entero y dos decimales) en las posiciones dadas.</summary>
    public RegistroInformativo Importe(int desde, int hasta, decimal valor) =>
        Num(desde, hasta, (long)decimal.Round(Math.Abs(valor) * 100m, 0, MidpointRounding.AwayFromZero));

    /// <summary>Importe con signo: «N» en <paramref name="posicionSigno"/> si es negativo (blanco si no) y el valor absoluto a continuación.</summary>
    public RegistroInformativo ImporteConSigno(int posicionSigno, int hasta, decimal valor)
    {
        Poner(posicionSigno, valor < 0m ? "N" : " ");
        return Importe(posicionSigno + 1, hasta, valor);
    }

    /// <summary>NIF de 9 posiciones a la derecha con ceros a la izquierda (como piden los diseños).</summary>
    public RegistroInformativo Nif(int desde, string? nif)
    {
        var n = NormalizarNif(nif);
        Poner(desde, n.Length >= 9 ? n[^9..] : n.PadLeft(9, '0'));
        return this;
    }

    public RegistroInformativo Texto(int desde, string fijo)
    {
        Poner(desde, fijo);
        return this;
    }

    private void Poner(int desde, string s)
    {
        if (desde < 1 || desde - 1 + s.Length > Longitud) throw new ArgumentOutOfRangeException(nameof(desde), $"El campo de la posición {desde} se sale del registro.");
        s.CopyTo(0, _r, desde - 1, s.Length);
    }

    public override string ToString() => new(_r);

    public static string NormalizarNif(string? nif) => new((nif ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    /// <summary>Mayúsculas sin acentos ni diéresis (salvo Ñ y Ç) y solo los caracteres que admite la AEAT; lo demás, blanco.</summary>
    public static string Limpiar(string? texto)
    {
        var s = (texto ?? "").Trim().ToUpper(CultureInfo.GetCultureInfo("es-ES"));
        var r = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is 'Ñ' or 'Ç') { r.Append(ch); continue; }
            var d = ch.ToString().Normalize(NormalizationForm.FormD);
            var b = d[0];
            r.Append(b is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '&' or ',' or '-' or '.' or '\'' or '(' or ')' or '/' ? b : ' ');
        }
        return string.Join(' ', r.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Persona de contacto del tipo 1 de las informativas: teléfono (9 cifras) y nombre («apellido apellido nombre»).</summary>
public sealed record ContactoInformativa(string Telefono, string Nombre)
{
    public long TelefonoNumerico => long.TryParse(new string(Telefono.Where(char.IsAsciiDigit).ToArray()) is { Length: > 0 } t ? t[^Math.Min(9, t.Length)..] : "0",
        NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
}

/// <summary>Cabecera común del registro tipo 1 de las informativas (posiciones 1 a 137 en el 349 y 1 a 135 en las demás).</summary>
internal static class Tipo1Informativa
{
    public static RegistroInformativo Base(string modelo, int ejercicio, Declarante303 declarante, ContactoInformativa contacto, string numero, string? anterior, bool sustitutiva)
    {
        var r = new RegistroInformativo()
            .Texto(1, "1").Texto(2, modelo).Num(5, 8, ejercicio).Nif(9, declarante.Nif).An(18, 57, declarante.RazonSocial).Texto(58, "T")
            .Num(59, 67, contacto.TelefonoNumerico).An(68, 107, contacto.Nombre)
            .Texto(108, numero.PadLeft(13, '0')[..13]);
        if (anterior is not null) r.Texto(sustitutiva ? 122 : 121, sustitutiva ? "S" : "C").Texto(123, anterior.PadLeft(13, '0')[..13]);
        else r.Num(123, 135, 0);
        return r;
    }
}
