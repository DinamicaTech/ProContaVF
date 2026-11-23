using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.UI.Base.Graficos;

namespace ProConta.Activos.UI.Vistas;

/// <summary>
/// Análisis de los activos financieros: un clic en una fila la muestra u oculta en los gráficos (sin Ctrl ni Mayús), doble clic abre su análisis y la
/// leyenda de la comparativa resalta la línea del activo al pasar el ratón.
/// </summary>
public partial class VistaCarteraFinanciera : UserControl
{
    public VistaCarteraFinanciera() => InitializeComponent();

    private CarteraFinancieraVM? Vm => DataContext as CarteraFinancieraVM;

    private FilaCarteraVM? FilaDe(object origen) =>
        origen is DependencyObject o && ItemsControl.ContainerFromElement(Tabla, o) is DataGridRow fila ? fila.Item as FilaCarteraVM : null;

    private static bool DentroDe<T>(object origen) where T : DependencyObject
    {
        for (var o = origen as DependencyObject; o is not null; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is T) return true;
        return false;
    }

    private void Tabla_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || DentroDe<CheckBox>(e.OriginalSource) || DentroDe<ButtonBase>(e.OriginalSource) || DentroDe<DataGridColumnHeader>(e.OriginalSource)
            || DentroDe<Thumb>(e.OriginalSource)) return;
        if (FilaDe(e.OriginalSource) is { PuedeMarcarse: true } fila) fila.Marcado = !fila.Marcado;
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DentroDe<CheckBox>(e.OriginalSource) || DentroDe<ButtonBase>(e.OriginalSource) || FilaDe(e.OriginalSource) is not { } fila || Vm is null) return;
        Vm.VerAnalisisCommand.Execute(fila);
        e.Handled = true;
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabla.SelectedItem is not FilaCarteraVM fila || Vm is null) return;
        if (e.Key == Key.Space && fila.PuedeMarcarse) { fila.Marcado = !fila.Marcado; e.Handled = true; }
        else if (e.Key == Key.Enter) { Vm.VerAnalisisCommand.Execute(fila); e.Handled = true; }
        else if (e.Key is Key.Right or Key.Left && fila.Desplegable) { fila.Abierto = e.Key == Key.Right; e.Handled = true; }
    }

    private void Desplegar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilaCarteraVM f) f.Abierto = !f.Abierto;
    }

    private void Leyenda_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LeyendaComparativa l && Vm is not null) Vm.SerieResaltada = l.Serie;
    }

    private void Leyenda_MouseLeave(object sender, MouseEventArgs e)
    {
        if (Vm is not null) Vm.SerieResaltada = -1;
    }
}

/// <summary>Color en texto (#rrggbb) a pincel.</summary>
public sealed class PincelDeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string c && c.Length > 0 ? GraficoBase.Pincel(c) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
