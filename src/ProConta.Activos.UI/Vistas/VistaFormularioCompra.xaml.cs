using System.Windows.Controls;

namespace ProConta.Activos.UI.Vistas;

/// <summary>La compra de participaciones de un activo financiero, en el alta por ISIN y en «Nueva compra…».</summary>
public partial class VistaFormularioCompra : UserControl
{
    public VistaFormularioCompra()
    {
        InitializeComponent();
        // El cursor empieza en Participaciones: la fecha (hoy) y el precio ya vienen puestos.
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Dispatcher.BeginInvoke(() => CampoParticipaciones.Focus(), System.Windows.Threading.DispatcherPriority.Input); };
    }
}
