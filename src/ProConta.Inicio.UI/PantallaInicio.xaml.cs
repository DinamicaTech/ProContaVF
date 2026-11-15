using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.Inicio.UI;

/// <summary>
/// Vista de la pantalla de inicio. Los indicadores van en 6 columnas si caben y en 3 si la pantalla es estrecha. La salud de la
/// contabilidad va en una columna propia a la derecha, con su desplazamiento; en ancho estrecho pasa debajo de los bloques.
/// </summary>
public partial class PantallaInicio : UserControl
{
    public PantallaInicio()
    {
        InitializeComponent();
        SizeChanged += (_, _) => AjustarColumnas();
        Loaded += (_, _) => AjustarColumnas();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is InicioVM vm)
                vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(InicioVM.VerSalud)) AjustarColumnas(); };
        };
    }

    /// <summary>Ancho a partir del cual la salud va en su columna a la derecha.</summary>
    public const double AnchoSaludALaDerecha = 1250;

    private void AjustarColumnas()
    {
        var ancho = ActualWidth;
        var verSalud = DataContext is InicioVM { VerSalud: true };
        var derecha = ancho >= AnchoSaludALaDerecha;
        // Los indicadores comparten el ancho con la salud cuando está a la derecha.
        var anchoBloques = derecha && verSalud ? ancho - ColumnaSalud.Width.Value : ancho;
        if (Buscar<UniformGrid>(Indicadores) is { } rejilla) rejilla.Columns = anchoBloques >= 1250 ? 6 : 3;
        ColumnaSalud.Width = new GridLength(derecha && verSalud ? 400 : 0);
        if (derecha && SaludDerecha.Content is null)
        {
            SaludAbajo.Content = null;
            SaludDerecha.Content = PanelSalud;
        }
        else if (!derecha && SaludAbajo.Content is null)
        {
            SaludDerecha.Content = null;
            SaludAbajo.Content = PanelSalud;
        }
        // Debajo, la lista crece con la página; a la derecha, la columna tiene su propio desplazamiento.
        DesplazamientoSalud.VerticalScrollBarVisibility = derecha ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private void ListaSalud_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is InicioVM { Salud: { } salud }) salud.Seleccionar(ListaSalud.SelectedItems.OfType<ElementoSaludVM>());
    }

    private void ListaSalud_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not InicioVM { Salud: { } salud }) return;
        if ((e.OriginalSource as DependencyObject) is { } o && ItemsControl.ContainerFromElement(ListaSalud, o) is ListBoxItem { DataContext: ElementoSaludVM item }
            && salud.AbrirCommand.CanExecute(item))
            salud.AbrirCommand.Execute(item);
    }

    private static T? Buscar<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) return t;
            if (Buscar<T>(hijo) is { } r) return r;
        }
        return null;
    }

    private void TablaClientes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is InicioVM vm && TablaClientes.SelectedItem is ClienteVM c && vm.AbrirClienteCommand.CanExecute(c)) vm.AbrirClienteCommand.Execute(c);
    }
}
