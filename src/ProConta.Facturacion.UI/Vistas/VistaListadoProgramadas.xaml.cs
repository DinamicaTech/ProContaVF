using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Facturacion.UI.VistaModelos;
using ProConta.UI.Base.Comandos;

namespace ProConta.Facturacion.UI.Vistas;

/// <summary>Vista de Facturas programadas: listado y detalle de la programada activa.</summary>
public partial class VistaListadoProgramadas : UserControl
{
    public VistaListadoProgramadas()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private ListadoProgramadasVM? Vm => DataContext as ListadoProgramadasVM;

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject d && EsDeFila(d)) Vm?.AbrirCommand.Execute(null);
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm?.Activa is not null)
        {
            Vm.AbrirCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool EsDeFila(System.Windows.DependencyObject d)
    {
        for (var x = d; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x))
        {
            if (x is DataGridRow) return true;
            if (x is System.Windows.Controls.Primitives.DataGridColumnHeader) return false;
        }
        return false;
    }
}
