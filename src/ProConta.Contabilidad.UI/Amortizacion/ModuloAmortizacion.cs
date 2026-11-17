using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Amortizacion;

/// <summary>Amortización de activos dentro del contenedor (contabilidad/procesos/amortizacion-de-activos): una sola pestaña por sesión.</summary>
public sealed class ModuloAmortizacion(ContextoAmortizacion contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private AmortizacionVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new AmortizacionVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
