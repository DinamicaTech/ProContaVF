using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.ReinicioCuadre;

/// <summary>Reiniciar el cuadre del Mayor dentro del contenedor (contabilidad/procesos/reiniciar-cuadre): una sola pestaña por sesión.</summary>
public sealed class ModuloReinicioCuadre(ContextoReinicioCuadre contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private ReinicioCuadreVM? _vm;

    /// <summary>Abre (o activa) la pantalla y recalcula la propuesta.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new ReinicioCuadreVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
