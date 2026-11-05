using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Facturacion.UI.VistaModelos;
using ProConta.UI.Base.Comandos;

namespace ProConta.Facturacion.UI.Vistas;

/// <summary>Vista de Facturas (maqueta sources/maquetas/Facturas).</summary>
public partial class VistaListadoFacturas : UserControl
{
    public VistaListadoFacturas()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
        Loaded += (_, _) => AplicarColumnaVeriFactu();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ListadoFacturasVM viejo) viejo.PropertyChanged -= AlCambiarModelo;
            if (e.NewValue is ListadoFacturasVM nuevo) nuevo.PropertyChanged += AlCambiarModelo;
        };
    }

    private void AlCambiarModelo(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListadoFacturasVM.VeriFactuActivo)) AplicarColumnaVeriFactu();
    }

    /// <summary>
    /// Con VeriFactu desactivado la columna no se ve. Se aplica después de que la tabla restaure las preferencias, que
    /// podrían volver a mostrarla.
    /// </summary>
    private void AplicarColumnaVeriFactu() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            var columna = Tabla.Columns.FirstOrDefault(k => ProConta.UI.Base.Tablas.TablaProConta.GetClave(k) == "verifactu");
            if (columna is not null) columna.Visibility = Vm?.VeriFactuActivo == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        });

    public ICommand EnfocarBusquedaCommand { get; }

    private ListadoFacturasVM? Vm => DataContext as ListadoFacturasVM;

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject d && EsDeFila(d) && Vm?.AbrirCommand.CanExecute(null) == true) Vm.AbrirCommand.Execute(null);
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm?.AbrirCommand.CanExecute(null) == true)
        {
            Vm.AbrirCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaFacturaVM>().ToList());

    /// <summary>
    /// El clic derecho sobre una fila no seleccionada la selecciona a ella sola; sobre una ya seleccionada conserva la
    /// selección, para que el menú contextual actúe sobre todas.
    /// </summary>
    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as System.Windows.DependencyObject; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x))
        {
            if (x is not DataGridRow fila) continue;
            if (!fila.IsSelected) Tabla.SelectedItem = fila.Item;
            return;
        }
    }

    private static bool EsDeFila(System.Windows.DependencyObject d)
    {
        for (var x = d; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x))
        {
            if (x is DataGridRow) return true;
            if (x is System.Windows.Controls.Primitives.DataGridColumnHeader or CheckBox or System.Windows.Controls.Primitives.ToggleButton) return false;
        }
        return false;
    }
}
