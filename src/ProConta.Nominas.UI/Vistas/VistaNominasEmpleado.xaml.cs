using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Nominas.UI.VistaModelos;

namespace ProConta.Nominas.UI.Vistas;

/// <summary>Pestaña Nóminas de la ficha del tercero: doble clic en una nómina abre su asiento.</summary>
public partial class VistaNominasEmpleado : UserControl
{
    public VistaNominasEmpleado() => InitializeComponent();

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = VisualTreeHelper.GetParent(x))
        {
            if (x is System.Windows.Controls.Primitives.DataGridColumnHeader) return;
            if (x is DataGridRow)
            {
                if ((DataContext as NominasEmpleadoVM)?.VerAsientoCommand is { } c && c.CanExecute(null)) c.Execute(null);
                e.Handled = true;
                return;
            }
        }
    }

    private void Exportar_Click(object sender, RoutedEventArgs e)
    {
        if (Tabla.ExportarExcelCommand.CanExecute(null)) Tabla.ExportarExcelCommand.Execute(null);
    }
}
