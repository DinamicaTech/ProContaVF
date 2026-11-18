using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.Documentos.UI.Vistas;

/// <summary>
/// Pantalla Documentos: un clic en una fila (fuera de sus campos) la marca o la desmarca, doble clic abre el fichero, clic derecho marca solo esa si
/// no lo estaba, y los ficheros arrastrados desde el Explorador o el correo se añaden a la bandeja.
/// </summary>
public partial class VistaDocumentos : UserControl
{
    public VistaDocumentos() => InitializeComponent();

    private PantallaDocumentosVM? Vm => DataContext as PantallaDocumentosVM;

    internal static FilaDocumentoVM? FilaDe(DataGrid tabla, object origen) =>
        origen is DependencyObject o && ItemsControl.ContainerFromElement(tabla, o) is DataGridRow fila ? fila.Item as FilaDocumentoVM : null;

    internal static bool DentroDe<T>(object origen) where T : DependencyObject
    {
        for (var o = origen as DependencyObject; o is not null; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is T) return true;
        return false;
    }

    /// <summary>Clic en un control de la fila (casilla, desplegable, fecha, título o botón): no marca la fila.</summary>
    internal static bool EnControl(object origen) =>
        DentroDe<CheckBox>(origen) || DentroDe<ComboBox>(origen) || DentroDe<DatePicker>(origen) || DentroDe<TextBox>(origen) || DentroDe<ButtonBase>(origen)
        || DentroDe<DataGridColumnHeader>(origen) || DentroDe<Thumb>(origen);

    private void Tabla_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (EnControl(e.OriginalSource)) return;
        if (FilaDe((DataGrid)sender, e.OriginalSource) is { } fila) fila.Marcado = !fila.Marcado;
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EnControl(e.OriginalSource) || FilaDe((DataGrid)sender, e.OriginalSource) is not { } fila || Vm is null) return;
        if (Vm.AbrirCommand.CanExecute(fila)) Vm.AbrirCommand.Execute(fila);
        e.Handled = true;
    }

    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FilaDe((DataGrid)sender, e.OriginalSource) is { } fila) Vm?.MarcarSolo(fila);
    }

    private void Tabla_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (EnControl(e.OriginalSource) || ((DataGrid)sender).SelectedItem is not FilaDocumentoVM fila || Vm is null) return;
        if (e.Key == Key.Space) { fila.Marcado = !fila.Marcado; e.Handled = true; }
        else if (e.Key == Key.Enter && Vm.AbrirCommand.CanExecute(fila)) { Vm.AbrirCommand.Execute(fila); e.Handled = true; }
    }

    internal static string[]? Ficheros(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private void AlArrastrar(object sender, DragEventArgs e)
    {
        e.Effects = Ficheros(e) is { Length: > 0 } && Vm?.AnadirCommand.CanExecute(null) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AlSoltar(object sender, DragEventArgs e)
    {
        if (Ficheros(e) is { Length: > 0 } f && Vm is not null) _ = Vm.SoltarAsync(f);
        e.Handled = true;
    }
}

/// <summary>Niega un booleano (para habilitar campos cuando algo no es cierto).</summary>
public sealed class NegarConverter : IValueConverter
{
    public static readonly NegarConverter Instancia = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
