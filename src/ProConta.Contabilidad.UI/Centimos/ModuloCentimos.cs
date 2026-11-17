using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Centimos;

/// <summary>Céntimos residuales dentro del contenedor (contabilidad/procesos/centimos-residuales): una sola pestaña por sesión.</summary>
public sealed class ModuloCentimos(ContextoCentimos contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private CentimosVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new CentimosVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
