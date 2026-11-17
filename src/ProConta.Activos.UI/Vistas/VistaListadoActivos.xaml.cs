using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.Activos.UI.Vistas;

/// <summary>Listado de activos: un clic en una fila la marca o la desmarca (sin Ctrl ni Mayús), doble clic abre la ficha.</summary>
public partial class VistaListadoActivos : UserControl
{
    public VistaListadoActivos() => InitializeComponent();

    private ListadoActivosVM? Vm => DataContext as ListadoActivosVM;

    private FilaActivoVM? FilaDe(object origen) =>
        origen is DependencyObject o && ItemsControl.ContainerFromElement(Tabla, o) is DataGridRow fila ? fila.Item as FilaActivoVM : null;

    private static bool DentroDe<T>(object origen) where T : DependencyObject
    {
        for (var o = origen as DependencyObject; o is not null; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is T) return true;
        return false;
    }

    private void Tabla_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // La casilla se marca sola; en el resto de la fila, el clic hace lo mismo que la casilla.
        if (DentroDe<CheckBox>(e.OriginalSource) || DentroDe<DataGridColumnHeader>(e.OriginalSource) || DentroDe<Thumb>(e.OriginalSource)) return;
        if (FilaDe(e.OriginalSource) is { } fila) fila.Marcado = !fila.Marcado;
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DentroDe<CheckBox>(e.OriginalSource) || FilaDe(e.OriginalSource) is not { } fila || Vm is null) return;
        if (Vm.AbrirCommand.CanExecute(fila)) Vm.AbrirCommand.Execute(fila);
        e.Handled = true;
    }

    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FilaDe(e.OriginalSource) is { } fila) Vm?.MarcarSolo(fila);
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabla.SelectedItem is not FilaActivoVM fila || Vm is null) return;
        if (e.Key == Key.Space) { fila.Marcado = !fila.Marcado; e.Handled = true; }
        else if (e.Key == Key.Enter && Vm.AbrirCommand.CanExecute(fila)) { Vm.AbrirCommand.Execute(fila); e.Handled = true; }
    }
}

/// <summary>Para los segmentos: marcado si el valor es igual al parámetro; al marcarlo, el valor pasa a ser el parámetro.</summary>
public sealed class IgualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? parameter?.ToString() ?? "" : Binding.DoNothing;
}
