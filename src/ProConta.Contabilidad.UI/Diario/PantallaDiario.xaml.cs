using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Contabilidad.Diario;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Tablas;

namespace ProConta.Contabilidad.UI.Diario;

/// <summary>
/// Vista del Diario. La selección vive en el modelo por clave de fila: cuando el modelo rehace las filas (al filtrar, plegar o
/// refrescar), la vista vuelve a seleccionar las que siguen; ordenar la lista no cambia las filas, así que la selección sigue.
/// </summary>
public partial class PantallaDiario : UserControl
{
    public PantallaDiario()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DiarioVM viejo)
            {
                viejo.PropertyChanged -= AlCambiarModelo;
                viejo.SeleccionPedida -= Seleccionar;
            }
            if (e.NewValue is DiarioVM nuevo)
            {
                nuevo.PropertyChanged += AlCambiarModelo;
                nuevo.SeleccionPedida += Seleccionar;
                nuevo.ColumnasVisibles = () => Tabla.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).Select(TablaProConta.ClaveDe).ToList();
                nuevo.OrdenLista = () => Tabla.Items.OfType<FilaDiarioVM>().Where(f => f.Apunte is not null).Select(f => f.Apunte!).ToList();
            }
        };
        Tabla.Aviso += (_, texto) => Vm?.AvisarDesdeLaVista(texto);
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private DiarioVM? Vm => DataContext as DiarioVM;

    private void AlCambiarModelo(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiarioVM.Filas))
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RestaurarSeleccion);
        // La vista por asiento no se ordena: lleva los asientos por fecha y número, con sus apuntes debajo.
        if (e.PropertyName == nameof(DiarioVM.PorAsiento) && Vm is { PorAsiento: true })
        {
            Tabla.Items.SortDescriptions.Clear();
            foreach (var c in Tabla.Columns) c.SortDirection = null;
        }
    }

    /// <summary>Vuelve a seleccionar en la rejilla las filas cuya clave está en la selección del modelo.</summary>
    private void RestaurarSeleccion()
    {
        if (Vm is not { } vm) return;
        vm.Restaurando = true;
        try
        {
            Tabla.SelectedItems.Clear();
            if (vm.ClavesSeleccion.Count > 0)
                foreach (var f in vm.Filas.Where(f => vm.ClavesSeleccion.Contains(f.Clave))) Tabla.SelectedItems.Add(f);
        }
        finally { vm.Restaurando = false; }
    }

    private void Seleccionar(FilaDiarioVM fila)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Tabla.SelectedItems.Clear();
            Tabla.SelectedItem = fila;
            Tabla.ScrollIntoView(fila);
        });
    }

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaDiarioVM>().ToList());

    /// <summary>Doble clic en una fila (no en la cabecera de la tabla ni en el botón de plegar): abre su asiento.</summary>
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm) return;
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
        {
            if (x is System.Windows.Controls.Primitives.DataGridColumnHeader or Button) return;
            if (x is DataGridRow { Item: FilaDiarioVM fila })
            {
                vm.AbrirFila(fila);
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// El clic derecho sobre una fila no seleccionada la selecciona a ella sola; sobre una ya seleccionada conserva la selección, para que
    /// el menú contextual actúe sobre todas.
    /// </summary>
    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
        {
            if (x is not DataGridRow fila) continue;
            if (!fila.IsSelected)
            {
                Tabla.SelectedItems.Clear();
                fila.IsSelected = true;
            }
            return;
        }
    }

    /// <summary>Ctrl+C copia la selección con las columnas visibles (y no el texto de las celdas de la rejilla, que incluiría las cabeceras de asiento).</summary>
    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && Vm is { } vm && vm.AccionCopiar.Comando.CanExecute(null))
        {
            vm.AccionCopiar.Comando.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>La lista de apuntes de la ficha tiene su barra; cuando llega a su tope, la rueda sigue moviendo la ficha entera.</summary>
    private void ApuntesFicha_Rueda(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer lista) return;
        var arriba = e.Delta > 0 && lista.VerticalOffset <= 0;
        var abajo = e.Delta < 0 && lista.VerticalOffset >= lista.ScrollableHeight;
        if (!arriba && !abajo) return;
        e.Handled = true;
        (lista.Parent as UIElement)?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }

    private void Columnas_Click(object sender, RoutedEventArgs e) => DialogosDiario.Columnas(Window.GetWindow(this), Tabla, ColumnasDiario.Todas);
}
