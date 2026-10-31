using System.Windows;
using System.Windows.Media;

namespace ProConta.UI.Base.Controles;

/// <summary>Icono de un botón o elemento de menú: pc:Iconos.Icono="{StaticResource Icono.plus}" lo pinta delante del texto.</summary>
public static class Iconos
{
    public static readonly DependencyProperty IconoProperty =
        DependencyProperty.RegisterAttached("Icono", typeof(Geometry), typeof(Iconos), new PropertyMetadata(null));

    public static Geometry? GetIcono(DependencyObject o) => (Geometry?)o.GetValue(IconoProperty);
    public static void SetIcono(DependencyObject o, Geometry? v) => o.SetValue(IconoProperty, v);
}
