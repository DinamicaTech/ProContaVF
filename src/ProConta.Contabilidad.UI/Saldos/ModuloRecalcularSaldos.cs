using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Saldos;

/// <summary>Recalcular saldos dentro del contenedor (contabilidad/procesos/recalcular-saldos): una sola pestaña por sesión.</summary>
public sealed class ModuloRecalcularSaldos(ContextoRecalcularSaldos contexto, IAnfitrionPestanas anfitrion)
{
    private RecalcularSaldosVM? _vm;

    /// <summary>Abre (o activa) la pantalla y vuelve a comparar.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new RecalcularSaldosVM(contexto, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
