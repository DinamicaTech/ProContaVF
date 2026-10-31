using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProConta.UI.Base.Controles;

/// <summary>Convierte un ancho en píxeles en el ancho de una columna de Grid.</summary>
public sealed class AnchoColumna : IValueConverter
{
    public static readonly AnchoColumna Conversor = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && d > 0 ? new GridLength(d) : new GridLength(1, GridUnitType.Auto);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GridLength g && g.IsAbsolute ? g.Value : Binding.DoNothing;
}
