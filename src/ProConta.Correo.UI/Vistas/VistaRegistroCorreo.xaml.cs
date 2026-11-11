using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Correo.UI.VistaModelos;
using ProConta.UI.Base.Comandos;

namespace ProConta.Correo.UI.Vistas;

/// <summary>Registro de correo enviado (maqueta sources/maquetas/CorreoLotes, con las acciones en el menú contextual).</summary>
public partial class VistaRegistroCorreo : UserControl
{
    public VistaRegistroCorreo()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private RegistroCorreoVM? Vm => DataContext as RegistroCorreoVM;

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaRegistroVM>().ToList());

    /// <summary>El clic derecho sobre una fila no seleccionada la selecciona a ella sola; sobre una seleccionada conserva la selección.</summary>
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
