using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ProConta.Terceros.UI.VistaModelos;
using ProConta.UI.Base.Comandos;

namespace ProConta.Terceros.UI.Vistas;

/// <summary>Vista del buscador de terceros (pestaña Terceros o ventana de selección).</summary>
public partial class VistaBuscador : UserControl
{
    public VistaBuscador()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is BuscadorTercerosVM { ModoSeleccion: true })
            {
                BotonNuevo.Visibility = Visibility.Collapsed;
                BotonAbrir.Visibility = Visibility.Collapsed;
                TituloPantalla.Visibility = Visibility.Collapsed;
                AyudaPie.Text = "Doble clic o Intro elige el tercero";
            }
            Dispatcher.BeginInvoke(() => CajaBusqueda.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private BuscadorTercerosVM? Vm => DataContext as BuscadorTercerosVM;

    private void Nuevo_Click(object sender, RoutedEventArgs e)
    {
        if (BotonNuevo.ContextMenu is { } menu)
        {
            menu.PlacementTarget = BotonNuevo;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Solo el doble clic en una fila (no en la cabecera ni en el separador de columnas).
        if (e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(Tabla, o) is DataGridRow && Vm?.AbrirCommand.CanExecute(null) == true)
        {
            Vm.AbrirCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm?.AbrirCommand.CanExecute(null) == true)
        {
            Vm.AbrirCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CajaBusqueda_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && Tabla.Items.Count > 0)
        {
            Tabla.SelectedIndex = Math.Max(0, Tabla.SelectedIndex);
            Tabla.UpdateLayout();
            if (Tabla.ItemContainerGenerator.ContainerFromIndex(Tabla.SelectedIndex) is DataGridRow fila) fila.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Vm?.AbrirCommand.CanExecute(null) == true)
        {
            Vm.AbrirCommand.Execute(null);
            e.Handled = true;
        }
    }
}
