using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Lineas;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>Líneas N43 dentro del contenedor (norma43/pantallas): una sola pestaña por sesión.</summary>
public sealed class ModuloLineasN43(ConsultaLineasN43 consulta, ServicioLineasN43 servicio, ServicioConciliacionN43 conciliacion, AsignadorCuentasN43 asignador,
    ContextoLineasN43 contexto, IAnfitrionPestanas anfitrion, IDialogosLineasN43 dialogos)
{
    private LineasN43VM? _lineas;

    public void Abrir()
    {
        if (_lineas is not null)
        {
            anfitrion.Activar(_lineas);
            _ = _lineas.CargarAsync();
            return;
        }
        _lineas = new LineasN43VM(consulta, servicio, conciliacion, asignador, dialogos, contexto, () => _lineas = null);
        anfitrion.Abrir(_lineas);
        _ = _lineas.IniciarAsync();
    }
}
