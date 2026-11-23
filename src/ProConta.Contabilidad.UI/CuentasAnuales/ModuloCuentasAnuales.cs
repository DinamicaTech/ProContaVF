using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.CuentasAnuales;

/// <summary>Cuentas anuales dentro del contenedor (contabilidad/procesos/cuentas-anuales): una sola pestaña por sesión.</summary>
public sealed class ModuloCuentasAnuales(ContextoCuentasAnuales contexto, IAnfitrionPestanas anfitrion)
{
    private CuentasAnualesVM? _vm;

    /// <summary>Abre (o activa) la pantalla y vuelve a calcular las cuentas.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new CuentasAnualesVM(contexto, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
