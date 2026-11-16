using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Facturacion.UI.VistaModelos;

namespace ProConta.Facturacion.UI.Vistas;

/// <summary>Vista del editor de factura (prototipo sources/maquetas/FacturaBorrador).</summary>
public partial class VistaEditorFactura : UserControl
{
    public VistaEditorFactura()
    {
        InitializeComponent();
        // Al abrir o volver a la pestaña, el foco pasa al editor para que F2 y F3 funcionen sin hacer clic antes.
        Focusable = true;
        FocusVisualStyle = null;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && !IsKeyboardFocusWithin)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible && !IsKeyboardFocusWithin) Focus(); }));
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is EditorFacturaVM viejo) viejo.LineaAnadida -= EnfocarLineaNueva;
            if (e.NewValue is EditorFacturaVM nuevo) nuevo.LineaAnadida += EnfocarLineaNueva;
        };
    }

    /// <summary>
    /// Tras añadir una línea, el foco va a su cantidad (o a su nombre, si es una línea sin concepto) para seguir
    /// escribiendo sin el ratón (facturacion/pantallas 2026-11-15). Espera a que la fila nueva esté dibujada.
    /// </summary>
    private void EnfocarLineaNueva(bool sinConcepto) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ListaLineas.Items.Count == 0) return;
            if (ListaLineas.ItemContainerGenerator.ContainerFromIndex(ListaLineas.Items.Count - 1) is not DependencyObject fila) return;
            if (Buscar(fila, sinConcepto ? "Nombre" : "Cantidad") is not { } caja) return;
            caja.BringIntoView();
            caja.Focus();
            caja.SelectAll();
        }));

    private static TextBox? Buscar(DependencyObject raiz, string etiqueta)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is TextBox { Tag: string t } caja && t == etiqueta) return caja;
            if (Buscar(hijo, etiqueta) is { } encontrada) return encontrada;
        }
        return null;
    }
}
