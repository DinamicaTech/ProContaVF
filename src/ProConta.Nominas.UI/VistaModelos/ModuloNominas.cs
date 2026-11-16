using ProConta.UI.Base.Contenedor;

namespace ProConta.Nominas.UI.VistaModelos;

/// <summary>Nóminas dentro del contenedor (nominas/pantallas): una sola pestaña por sesión.</summary>
public sealed class ModuloNominas(ServiciosNominas servicios, ContextoNominas contexto, IAnfitrionPestanas anfitrion, IDialogosNominas dialogos)
{
    private NominasVM? _nominas;

    /// <summary>Abre (o activa) la pantalla; con <paramref name="idImportacion"/>, con ese periodo elegido.</summary>
    public void Abrir(int? idImportacion = null)
    {
        if (_nominas is not null)
        {
            anfitrion.Activar(_nominas);
            _ = _nominas.CargarAsync(idImportacion);
            return;
        }
        _nominas = new NominasVM(servicios, dialogos, contexto, () => _nominas = null);
        anfitrion.Abrir(_nominas);
        _ = _nominas.CargarAsync(idImportacion);
    }
}
