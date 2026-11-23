using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Legalizacion;

/// <summary>Legalización de libros dentro del contenedor (contabilidad/procesos/legalizacion-libros): una sola pestaña por sesión.</summary>
public sealed class ModuloLegalizacion(ContextoLegalizacion contexto, IAnfitrionPestanas anfitrion)
{
    private LegalizacionVM? _vm;

    /// <summary>Abre (o activa) la pantalla y vuelve a reunir los datos del ejercicio.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync();
            return;
        }
        _vm = new LegalizacionVM(contexto, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
