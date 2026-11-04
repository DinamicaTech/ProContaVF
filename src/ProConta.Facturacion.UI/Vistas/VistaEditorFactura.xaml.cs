using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
    }
}
