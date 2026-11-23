using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Compras.UI.VistaModelos;

namespace ProConta.Compras.UI.Vistas;

/// <summary>Facturas recurrentes pendientes: un clic en una fila la marca o la desmarca; doble clic abre el PDF de la última factura.</summary>
public partial class VistaRecurrentesPendientes : UserControl
{
    public VistaRecurrentesPendientes() => InitializeComponent();

    private RecurrentesPendientesVM? Vm => DataContext as RecurrentesPendientesVM;

    private FilaRecurrenteVM? FilaDe(object origen) =>
        origen is DependencyObject o && ItemsControl.ContainerFromElement(Tabla, o) is DataGridRow fila ? fila.Item as FilaRecurrenteVM : null;

    private static bool DentroDe<T>(object origen) where T : DependencyObject
    {
        for (var o = origen as DependencyObject; o is not null; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is T) return true;
        return false;
    }

    private void Tabla_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DentroDe<CheckBox>(e.OriginalSource) || DentroDe<DataGridColumnHeader>(e.OriginalSource) || DentroDe<Thumb>(e.OriginalSource)) return;
        if (e.ClickCount > 1) return;
        if (FilaDe(e.OriginalSource) is { } fila) fila.Marcado = !fila.Marcado;
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DentroDe<CheckBox>(e.OriginalSource) || FilaDe(e.OriginalSource) is not { } fila || Vm is null) return;
        if (Vm.AbrirPdfCommand.CanExecute(fila)) Vm.AbrirPdfCommand.Execute(fila);
        e.Handled = true;
    }

    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FilaDe(e.OriginalSource) is { } fila) Vm?.MarcarSolo(fila);
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabla.SelectedItem is not FilaRecurrenteVM fila || Vm is null) return;
        if (e.Key == Key.Space) { fila.Marcado = !fila.Marcado; e.Handled = true; }
        else if (e.Key == Key.Enter && Vm.AbrirPdfCommand.CanExecute(fila)) { Vm.AbrirPdfCommand.Execute(fila); e.Handled = true; }
    }
}
