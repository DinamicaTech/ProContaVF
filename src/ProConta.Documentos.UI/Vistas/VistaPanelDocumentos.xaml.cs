using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProConta.Documentos.UI.Vistas;

/// <summary>Panel Documentos de una ficha: se carga la primera vez que se enseña; doble clic abre el documento; los ficheros soltados se vinculan aquí.</summary>
public partial class VistaPanelDocumentos : UserControl
{
    public VistaPanelDocumentos() => InitializeComponent();

    private PanelDocumentosVM? Vm => DataContext as PanelDocumentosVM;

    private void AlCargar(object sender, RoutedEventArgs e)
    {
        if (Vm is { Cargado: false } vm) _ = vm.CargarAsync();
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VistaDocumentos.FilaDe(Tabla, e.OriginalSource) is { } fila && Vm is not null && Vm.AbrirCommand.CanExecute(fila)) Vm.AbrirCommand.Execute(fila);
    }

    private void AlArrastrar(object sender, DragEventArgs e)
    {
        e.Effects = VistaDocumentos.Ficheros(e) is { Length: > 0 } && Vm?.AnadirCommand.CanExecute(null) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AlSoltar(object sender, DragEventArgs e)
    {
        if (VistaDocumentos.Ficheros(e) is { Length: > 0 } f && Vm is not null) _ = Vm.SoltarAsync(f);
        e.Handled = true;
    }
}
