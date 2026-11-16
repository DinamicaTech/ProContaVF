using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ProConta.UI.Base.Graficos;

namespace ProConta.Inicio.UI;

/// <summary>Color de serie en texto («#2a78d6») a pincel; con ConverterParameter=«rayado», el pincel rayado de la proyección.</summary>
public sealed class ColorPincelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string c && c.Length > 0
            ? parameter as string == "rayado" ? GraficoBase.Rayado(c) : GraficoBase.Pincel(c)
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
