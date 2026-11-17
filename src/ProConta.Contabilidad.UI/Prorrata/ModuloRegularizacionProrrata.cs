using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Prorrata;

/// <summary>Prorrata de IVA dentro del contenedor (contabilidad/procesos/prorrata): una sola pestaña por sesión.</summary>
public sealed class ModuloRegularizacionProrrata(ContextoRegularizacionProrrata contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private RegularizacionProrrataVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new RegularizacionProrrataVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
