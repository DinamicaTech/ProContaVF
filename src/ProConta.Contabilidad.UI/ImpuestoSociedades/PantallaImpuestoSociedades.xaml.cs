using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.Contabilidad.UI.ImpuestoSociedades;

/// <summary>
/// Vista de la pantalla Impuesto sobre Sociedades; la lógica está en <see cref="ImpuestoSociedadesVM"/>. Aquí solo se lleva el doble clic
/// de una fila (subcuenta, apunte, piso) a la orden de esa fila.
/// </summary>
public partial class PantallaImpuestoSociedades : UserControl
{
    public PantallaImpuestoSociedades() => InitializeComponent();

    private void AlDobleClic(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject origen) return;
        // El doble clic en un botón, un campo o un desplegable es de ese control, no de la fila.
        for (var d = origen; d is not null && !ReferenceEquals(d, this); d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or TextBoxBase or ComboBox or ScrollBar or DataGridColumnHeader) return;
        var contexto = (origen as FrameworkElement)?.DataContext ?? (origen as FrameworkContentElement)?.DataContext;
        if (contexto is IFilaConDobleClic { DobleClicCommand: { } orden } && orden.CanExecute(null))
        {
            orden.Execute(null);
            e.Handled = true;
        }
    }
}
