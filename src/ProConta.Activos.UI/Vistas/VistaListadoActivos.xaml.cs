using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.Activos.UI.Vistas;

/// <summary>Listado de activos: un clic en una fila la marca o la desmarca (sin Ctrl ni Mayús), doble clic abre la ficha.</summary>
public partial class VistaListadoActivos : UserControl
{
    private ListadoActivosVM? _vm;

    public VistaListadoActivos()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) { _vm.PropertyChanged -= AlCambiarVm; _vm.GruposCambiados -= RefrescarGrupos; }
            _vm = DataContext as ListadoActivosVM;
            if (_vm is null) return;
            _vm.PropertyChanged += AlCambiarVm;
            _vm.GruposCambiados += RefrescarGrupos;
            Agrupar();
        };
    }

    private ListadoActivosVM? Vm => DataContext as ListadoActivosVM;

    private void AlCambiarVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListadoActivosVM.Filas)) Agrupar();
    }

    /// <summary>
    /// La tabla se agrupa por el tipo de activo (Parámetros › Tipos de activo), en orden alfabético, con el resumen del grupo en su cabecera
    /// (propietario 2026-11-27). Ordenar por una columna ordena dentro de cada grupo.
    /// </summary>
    private void Agrupar()
    {
        if (_vm is null || CollectionViewSource.GetDefaultView(_vm.Filas) is not { } vista || vista.GroupDescriptions is null) return;
        if (vista.GroupDescriptions.Count > 0) return;
        var grupo = new PropertyGroupDescription(nameof(FilaActivoVM.Grupo));
        grupo.SortDescriptions.Add(new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending));
        vista.GroupDescriptions.Add(grupo);
    }

    /// <summary>Vuelve a calcular los resúmenes de los grupos (al leer las posiciones de los financieros).</summary>
    private void RefrescarGrupos()
    {
        if (_vm is not null) CollectionViewSource.GetDefaultView(_vm.Filas)?.Refresh();
    }

    private static FilaActivoVM? FilaDe(object tabla, object origen) =>
        tabla is DataGrid t && origen is DependencyObject o && ItemsControl.ContainerFromElement(t, o) is DataGridRow fila ? fila.Item as FilaActivoVM : null;

    private static bool DentroDe<T>(object origen) where T : DependencyObject
    {
        for (var o = origen as DependencyObject; o is not null; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is T) return true;
        return false;
    }

    private void Tabla_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // La casilla se marca sola; en el resto de la fila, el clic hace lo mismo que la casilla.
        if (DentroDe<CheckBox>(e.OriginalSource) || DentroDe<DataGridColumnHeader>(e.OriginalSource) || DentroDe<Thumb>(e.OriginalSource)) return;
        if (FilaDe(sender, e.OriginalSource) is { } fila) fila.Marcado = !fila.Marcado;
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DentroDe<CheckBox>(e.OriginalSource) || FilaDe(sender, e.OriginalSource) is not { } fila || Vm is null) return;
        if (Vm.AbrirCommand.CanExecute(fila)) Vm.AbrirCommand.Execute(fila);
        e.Handled = true;
    }

    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FilaDe(sender, e.OriginalSource) is { } fila) Vm?.MarcarSolo(fila);
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not FilaActivoVM fila || Vm is null) return;
        if (e.Key == Key.Space) { fila.Marcado = !fila.Marcado; e.Handled = true; }
        else if (e.Key == Key.Enter && Vm.AbrirCommand.CanExecute(fila)) { Vm.AbrirCommand.Execute(fila); e.Handled = true; }
    }
}

/// <summary>Resumen de un grupo de la tabla (sus filas): cuántos activos, coste y resultado del ejercicio.</summary>
public sealed class ResumenGrupoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is System.Collections.IEnumerable filas ? ListadoActivosVM.ResumenGrupo(filas.OfType<FilaActivoVM>()) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Para los segmentos: marcado si el valor es igual al parámetro; al marcarlo, el valor pasa a ser el parámetro.</summary>
public sealed class IgualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? parameter?.ToString() ?? "" : Binding.DoNothing;
}
