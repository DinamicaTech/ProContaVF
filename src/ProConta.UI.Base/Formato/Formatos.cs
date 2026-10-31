using System.Globalization;

namespace ProConta.UI.Base.Formato;

/// <summary>
/// Formatos de presentación comunes (docs/plataforma/ui-base.md): todo importe se muestra en euros con separador
/// de miles y dos decimales (1.234,56 €), también en los campos de entrada; las fechas como dd/MM/aaaa.
/// </summary>
public static class Formatos
{
    /// <summary>Cultura de presentación de ProConta (España).</summary>
    public static readonly CultureInfo Cultura = CrearCultura();

    /// <summary>Signo menos tipográfico, igual que en las maquetas.</summary>
    public const string Menos = "−";

    private static CultureInfo CrearCultura()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("es-ES").Clone();
        c.NumberFormat.NumberGroupSeparator = ".";
        c.NumberFormat.NumberDecimalSeparator = ",";
        c.NumberFormat.CurrencyGroupSeparator = ".";
        c.NumberFormat.CurrencyDecimalSeparator = ",";
        // es-ES no agrupa los números de cuatro cifras; ProConta sí (1.234,56 €).
        c.NumberFormat.NumberGroupSizes = new[] { 3 };
        c.NumberFormat.CurrencyGroupSizes = new[] { 3 };
        return CultureInfo.ReadOnly(c);
    }

    /// <summary>Número con dos decimales y separador de miles, sin símbolo: 1.234,56 (negativos con «−»).</summary>
    public static string Numero(decimal valor)
    {
        valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        var abs = Math.Abs(valor).ToString("#,##0.00", Cultura);
        return valor < 0 ? Menos + abs : abs;
    }

    /// <summary>Importe en euros: 1.234,56 € (negativos con «−»).</summary>
    public static string Importe(decimal valor) => Numero(valor) + " €";

    /// <summary>
    /// Lee un importe escrito por el operador: con o sin «€», con o sin separador de miles, con «-» o «−».
    /// La coma es el decimal; un único punto seguido de una o dos cifras al final también (punto del teclado numérico).
    /// </summary>
    public static bool LeerImporte(string? texto, out decimal valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Replace("€", "").Replace(" ", "").Replace("\u00A0", "").Replace(Menos, "-").Trim();
        if (t.Length == 0) return false;
        if (!t.Contains(',') && t.Count(c => c == '.') == 1)
        {
            var i = t.IndexOf('.');
            var dec = t.Length - i - 1;
            if (dec is 1 or 2) t = t[..i] + "," + t[(i + 1)..];
        }
        if (!decimal.TryParse(t, System.Globalization.NumberStyles.Number, Cultura, out valor)) return false;
        valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Importe o texto vacío cuando es cero (columnas Debe y Haber).</summary>
    public static string ImporteOVacio(decimal valor) => Math.Round(valor, 2) == 0 ? string.Empty : Importe(valor);

    /// <summary>Saldo contable con signo: deudor en positivo, acreedor en negativo, sin sufijo D/H.</summary>
    public static string Saldo(decimal saldo) => Importe(saldo);

    /// <summary>Fecha corta: 31/10/2026.</summary>
    public static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", Cultura);

    /// <summary>Fecha corta o vacío.</summary>
    public static string Fecha(DateOnly? fecha) => fecha is { } f ? Fecha(f) : string.Empty;

    /// <summary>Fecha corta de un instante (se ignora la hora).</summary>
    public static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", Cultura);

    /// <summary>Recuento con separador de miles: 5.242.</summary>
    public static string Recuento(int n) => n.ToString("#,##0", Cultura);

    /// <summary>Recuento con su palabra en singular o plural: «1 apunte», «5.242 apuntes».</summary>
    public static string Plural(int n, string singular, string plural) => Recuento(n) + " " + (n == 1 ? singular : plural);
}
