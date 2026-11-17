using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Dividendos;

/// <summary>Aplicación del resultado dentro del contenedor (contabilidad/procesos/aplicacion-resultado): una sola pestaña por sesión.</summary>
public sealed class ModuloAplicacionResultado(ContextoAplicacionResultado contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private AplicacionResultadoVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new AplicacionResultadoVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
