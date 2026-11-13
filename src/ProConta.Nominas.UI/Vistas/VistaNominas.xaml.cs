using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Nominas.UI.VistaModelos;

namespace ProConta.Nominas.UI.Vistas;

/// <summary>
/// Vista de la pantalla Nóminas. La selección de periodos vive en el modelo por Id: cuando el modelo rehace la lista (al filtrar o
/// refrescar) la vista vuelve a seleccionar esos Id. Soltar un Excel en la pantalla lo importa.
/// </summary>
public partial class VistaNominas : UserControl
{
    private bool _restaurando;

    public VistaNominas()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is NominasVM viejo) viejo.PropertyChanged -= AlCambiarModelo;
            if (e.NewValue is NominasVM nuevo) nuevo.PropertyChanged += AlCambiarModelo;
        };
    }

    private NominasVM? Vm => DataContext as NominasVM;

    private void AlCambiarModelo(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NominasVM.IdsSeleccionados))
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RestaurarSeleccion);
    }

    /// <summary>Vuelve a seleccionar en la rejilla los periodos cuyo Id está en la selección del modelo.</summary>
    private void RestaurarSeleccion()
    {
        if (Vm is not { } vm) return;
        var ids = vm.IdsSeleccionados;
        var actuales = TablaPeriodos.SelectedItems.OfType<FilaImportacionVM>().ToList();
        if (actuales.Count == ids.Count && actuales.All(f => ids.Contains(f.Id) && vm.Importaciones.Contains(f))) return;
        _restaurando = true;
        try
        {
            TablaPeriodos.SelectedItems.Clear();
            foreach (var f in vm.Importaciones.Where(f => ids.Contains(f.Id))) TablaPeriodos.SelectedItems.Add(f);
            if (vm.Actual is { } a && vm.Importaciones.Contains(a)) TablaPeriodos.ScrollIntoView(a);
        }
        finally { _restaurando = false; }
    }

    private void Periodos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restaurando) return;
        Vm?.CambiarSeleccion(TablaPeriodos.SelectedItems.OfType<FilaImportacionVM>().ToList());
    }

    /// <summary>El clic derecho sobre un periodo no seleccionado lo selecciona solo a él; sobre uno seleccionado conserva la selección.</summary>
    private void Periodos_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = VisualTreeHelper.GetParent(x))
        {
            if (x is not DataGridRow fila) continue;
            if (!fila.IsSelected)
            {
                TablaPeriodos.SelectedItems.Clear();
                fila.IsSelected = true;
            }
            return;
        }
    }

    /// <summary>Doble clic en una nómina: su asiento.</summary>
    private void Nominas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = VisualTreeHelper.GetParent(x))
        {
            if (x is System.Windows.Controls.Primitives.DataGridColumnHeader) return;
            if (x is DataGridRow)
            {
                if (Vm?.VerAsientoCommand is { } c && c.CanExecute(null)) c.Execute(null);
                e.Handled = true;
                return;
            }
        }
    }

    private void ExportarPeriodos_Click(object sender, RoutedEventArgs e)
    {
        if (TablaPeriodos.ExportarExcelCommand.CanExecute(null)) TablaPeriodos.ExportarExcelCommand.Execute(null);
    }

    private void ExportarNominas_Click(object sender, RoutedEventArgs e)
    {
        if (TablaNominas.ExportarExcelCommand.CanExecute(null)) TablaNominas.ExportarExcelCommand.Execute(null);
    }

    private static string? ExcelSoltado(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } f && Path.GetExtension(f[0]).ToLowerInvariant() is ".xls" or ".xlsx" ? f[0] : null;

    private void AlArrastrar(object sender, DragEventArgs e)
    {
        e.Effects = ExcelSoltado(e) is not null && Vm?.ImportarCommand.CanExecute(null) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AlSoltar(object sender, DragEventArgs e)
    {
        if (ExcelSoltado(e) is { } ruta && Vm is { } vm && vm.ImportarCommand.CanExecute(null)) _ = vm.ImportarAsync(ruta);
        e.Handled = true;
    }
}
