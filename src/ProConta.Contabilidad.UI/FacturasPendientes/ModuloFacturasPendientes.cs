using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.FacturasPendientes;

/// <summary>Facturas pendientes de recibir dentro del contenedor (contabilidad/procesos/facturas-pendientes-recibir): una sola pestaña por sesión.</summary>
public sealed class ModuloFacturasPendientes(ContextoFacturasPendientes contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private FacturasPendientesVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new FacturasPendientesVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
