using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Facturacion.UI.VistaModelos;
using ProConta.UI.Base.Comandos;

namespace ProConta.Facturacion.UI.Vistas;

/// <summary>Vista de Ventas por facturar: listado y ficha de la venta activa.</summary>
public partial class VistaListadoVentas : UserControl
{
    public VistaListadoVentas()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private ListadoVentasVM? Vm => DataContext as ListadoVentasVM;

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaVentaVM>().ToList());

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
}
