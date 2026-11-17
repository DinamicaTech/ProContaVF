using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Cierre;

/// <summary>Cierre del ejercicio dentro del contenedor (contabilidad/procesos/cierre-ejercicio): una sola pestaña por sesión.</summary>
public sealed class ModuloCierreEjercicio(ContextoCierreEjercicio contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private CierreEjercicioVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new CierreEjercicioVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
