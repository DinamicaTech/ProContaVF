using System.Globalization;

namespace ProConta.UI.Base.Formato;

/// <summary>
/// Formatos de presentación comunes (docs/plataforma/ui-base.md): todo importe se muestra en euros con separador
/// de miles y dos decimales (1.234,56 €); las fechas como dd/MM/aaaa. Los campos de entrada mantienen el número sin símbolo.
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
