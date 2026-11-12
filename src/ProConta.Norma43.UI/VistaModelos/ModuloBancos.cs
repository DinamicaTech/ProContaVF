using ProConta.Norma43.Importacion;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>Bancos dentro del contenedor (norma43/bancos): una sola pestaña por sesión.</summary>
public sealed class ModuloBancos(ServicioImportacionN43 servicio, ConsultaBancos consulta, ContextoBancos contexto, IAnfitrionPestanas anfitrion, IDialogosBancos dialogos)
{
    private BancosVM? _bancos;

    public void Abrir()
    {
        if (_bancos is not null)
        {
            anfitrion.Activar(_bancos);
            _ = _bancos.CargarAsync();
            return;
        }
        _bancos = new BancosVM(servicio, consulta, dialogos, contexto, () => _bancos = null);
        anfitrion.Abrir(_bancos);
        _ = _bancos.CargarAsync();
    }
}
