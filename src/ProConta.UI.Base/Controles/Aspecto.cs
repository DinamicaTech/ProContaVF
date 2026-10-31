using System.Windows;
using System.Windows.Media;

namespace ProConta.UI.Base.Controles;

/// <summary>
/// Colores de los estados (ratón encima, pulsado) que cada variante de botón puede cambiar sin rehacer la plantilla.
/// Los disparadores de una plantilla tienen prioridad sobre los del estilo, así que la plantilla lee estos valores.
/// </summary>
public static class Aspecto
{
    public static readonly DependencyProperty FondoEncimaProperty = DependencyProperty.RegisterAttached("FondoEncima", typeof(Brush), typeof(Aspecto), new PropertyMetadata(null));
    public static Brush? GetFondoEncima(DependencyObject o) => (Brush?)o.GetValue(FondoEncimaProperty);
    public static void SetFondoEncima(DependencyObject o, Brush? v) => o.SetValue(FondoEncimaProperty, v);

    public static readonly DependencyProperty BordeEncimaProperty = DependencyProperty.RegisterAttached("BordeEncima", typeof(Brush), typeof(Aspecto), new PropertyMetadata(null));
    public static Brush? GetBordeEncima(DependencyObject o) => (Brush?)o.GetValue(BordeEncimaProperty);
    public static void SetBordeEncima(DependencyObject o, Brush? v) => o.SetValue(BordeEncimaProperty, v);

    public static readonly DependencyProperty FondoPulsadoProperty = DependencyProperty.RegisterAttached("FondoPulsado", typeof(Brush), typeof(Aspecto), new PropertyMetadata(null));
    public static Brush? GetFondoPulsado(DependencyObject o) => (Brush?)o.GetValue(FondoPulsadoProperty);
    public static void SetFondoPulsado(DependencyObject o, Brush? v) => o.SetValue(FondoPulsadoProperty, v);
}
