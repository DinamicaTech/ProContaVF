using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProConta.Activos.UI.Vistas;

/// <summary>Ficha del activo: el doble clic en una subcuenta (de la cuenta de resultados o del apartado Subcuentas) abre su Mayor.</summary>
public partial class VistaFichaActivo : UserControl
{
    public VistaFichaActivo() => InitializeComponent();

    private static object? Fila(object sender, MouseButtonEventArgs e) =>
        sender is DataGrid tabla && e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(tabla, o) is DataGridRow fila ? fila.Item : null;

    private void Cuenta_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Fila(sender, e) is FilaCuentaVM { MayorCommand: { } c } && c.CanExecute(null)) { c.Execute(null); e.Handled = true; }
    }

    private void Subcuentas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Fila(sender, e) is FilaSubcuentaVM { MayorCommand: { } c } && c.CanExecute(null)) { c.Execute(null); e.Handled = true; }
    }
}
