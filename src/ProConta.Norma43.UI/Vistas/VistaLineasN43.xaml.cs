using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Tablas;

namespace ProConta.Norma43.UI.Vistas;

/// <summary>
/// Vista de las líneas N43. La selección vive en el modelo por Id: cuando el modelo sustituye las filas visibles (al filtrar o
/// refrescar), la vista vuelve a seleccionar las que tenían esos Id; ordenar no cambia las filas, así que la selección sigue.
/// </summary>
public partial class VistaLineasN43 : UserControl
{
    public VistaLineasN43()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LineasN43VM viejo) viejo.PropertyChanged -= AlCambiarModelo;
            if (e.NewValue is LineasN43VM nuevo) nuevo.PropertyChanged += AlCambiarModelo;
        };
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private LineasN43VM? Vm => DataContext as LineasN43VM;

    private void AlCambiarModelo(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LineasN43VM.Filas))
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RestaurarSeleccion);
    }

    /// <summary>Vuelve a seleccionar en la rejilla las filas cuyo Id está en la selección del modelo.</summary>
    private void RestaurarSeleccion()
    {
        if (Vm is not { } vm) return;
        vm.Restaurando = true;
        try
        {
            Tabla.SelectedItems.Clear();
            foreach (var f in vm.Filas.Where(f => vm.IdsSeleccionados.Contains(f.Id))) Tabla.SelectedItems.Add(f);
            if (vm.Actual is { } a && vm.Filas.Contains(a)) Tabla.ScrollIntoView(a);
        }
        finally { vm.Restaurando = false; }
        vm.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaLineaN43VM>().ToList());
    }

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaLineaN43VM>().ToList());

    /// <summary>Doble clic: en una celda de cuenta (origen o destino), su Mayor; en el resto de la fila, el asiento de la línea.</summary>
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm || e.OriginalSource is not DependencyObject d) return;
        DataGridCell? celda = null;
        for (var x = d; x is not null; x = VisualTreeHelper.GetParent(x))
        {
            if (x is DataGridCell c) celda ??= c;
            if (x is System.Windows.Controls.Primitives.DataGridColumnHeader) return;
            if (x is DataGridRow { Item: FilaLineaN43VM fila })
            {
                var clave = celda?.Column is { } col ? TablaProConta.GetClave(col) : null;
                if (clave is "cuenta-origen" or "desc-origen") vm.AbrirMayor(fila, origen: true);
                else if (clave is "cuenta-destino" or "desc-destino") vm.AbrirMayor(fila, origen: false);
                else _ = vm.VerAsientoAsync(fila);
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
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = VisualTreeHelper.GetParent(x))
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

    private void Exportar_Click(object sender, RoutedEventArgs e)
    {
        if (Tabla.ExportarExcelCommand.CanExecute(null)) Tabla.ExportarExcelCommand.Execute(null);
    }
}
