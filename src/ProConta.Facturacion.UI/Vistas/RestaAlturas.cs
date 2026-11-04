using System.Globalization;
using System.Windows.Data;

namespace ProConta.Facturacion.UI.Vistas;

/// <summary>
/// Altura que queda: el primer valor menos los demás y menos el margen del parámetro, con un mínimo de 120. Sirve para
/// que el historial termine a la misma altura que «Datos de la factura».
/// </summary>
public sealed class RestaAlturas : IMultiValueConverter
{
    public const double Minimo = 120;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not double total) return Minimo;
        var resto = values.Skip(1).OfType<double>().Sum();
        var margen = parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 0;
        return Math.Max(Minimo, total - resto - margen);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
