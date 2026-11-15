using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.Inicio.UI;

/// <summary>Vista de la pantalla de inicio. Los indicadores van en 6 columnas si caben y en 3 si la pantalla es estrecha.</summary>
public partial class PantallaInicio : UserControl
{
    public PantallaInicio()
    {
        InitializeComponent();
        SizeChanged += (_, _) => AjustarColumnas();
        Loaded += (_, _) => AjustarColumnas();
    }

    private void AjustarColumnas()
    {
        if (Buscar<UniformGrid>(Indicadores) is { } rejilla) rejilla.Columns = ActualWidth >= 1250 ? 6 : 3;
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
