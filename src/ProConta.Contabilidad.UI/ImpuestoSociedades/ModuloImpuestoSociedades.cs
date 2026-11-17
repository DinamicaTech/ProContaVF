using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.ImpuestoSociedades;

/// <summary>Impuesto sobre Sociedades dentro del contenedor (contabilidad/procesos/impuesto-sociedades): una sola pestaña por sesión.</summary>
public sealed class ModuloImpuestoSociedades(ContextoImpuestoSociedades contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private ImpuestoSociedadesVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new ImpuestoSociedadesVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
