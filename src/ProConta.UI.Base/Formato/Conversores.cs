using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ProConta.UI.Base.Formato;

/// <summary>Muestra un decimal como importe en euros (1.234,56 €). Con ConverterParameter=«vacio», el cero se ve vacío.</summary>
[ValueConversion(typeof(decimal), typeof(string))]
public sealed class ImporteConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        var v = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        return parameter as string == "vacio" ? Formatos.ImporteOVacio(v) : Formatos.Importe(v);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Muestra una fecha (DateOnly o DateTime) como dd/MM/aaaa.</summary>
public sealed class FechaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateOnly d => Formatos.Fecha(d),
        DateTime t => Formatos.Fecha(t),
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Pincel del saldo: valores [saldo, cuenta o grupo]. Verde a favor, rojo en contra y tinta en neutro (ReglaSaldo).
/// </summary>
public sealed class SaldoPincelConverter : IMultiValueConverter
{
    public Brush AFavor { get; set; } = new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x45));
    public Brush EnContra { get; set; } = new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E));
    public Brush Neutro { get; set; } = new SolidColorBrush(Color.FromRgb(0x16, 0x21, 0x3A));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is null || values[0] == DependencyProperty.UnsetValue) return Neutro;
        var saldo = System.Convert.ToDecimal(values[0], CultureInfo.InvariantCulture);
        var g = values[1] switch
        {
            char c => c,
            string s when s.Length > 0 => s[0],
            _ => '0'
        };
        return ReglaSaldo.Sentido(g, saldo) switch
        {
            SentidoSaldo.AFavor => AFavor,
            SentidoSaldo.EnContra => EnContra,
            _ => Neutro
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible si el valor es true (o no vacío); con ConverterParameter=«invertir», al revés.</summary>
public sealed class VisibleSiConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var on = value switch { bool b => b, string s => s.Length > 0, null => false, _ => true };
        if (parameter as string == "invertir") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
