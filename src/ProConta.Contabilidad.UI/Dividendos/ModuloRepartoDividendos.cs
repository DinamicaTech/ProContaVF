using System.Windows;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Dividendos;

/// <summary>Diálogos de la pantalla con los mensajes comunes; el motivo y el error, los mismos del Mayor.</summary>
public sealed class DialogosRepartoDividendos(Func<Window?> propietario) : IDialogosRepartoDividendos
{
    private readonly Mayor.DialogosMayor _comunes = new(propietario);

    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, opciones);

    public string? PedirMotivo(string titulo, string explicacion) => _comunes.PedirMotivo(titulo, explicacion);

    public void Error(string titulo, string mensaje) => _comunes.Error(titulo, mensaje);
}

/// <summary>Reparto de dividendos dentro del contenedor (contabilidad/procesos/aplicacion-resultado): una sola pestaña por sesión.</summary>
public sealed class ModuloRepartoDividendos(ContextoRepartoDividendos contexto, IAnfitrionPestanas anfitrion, IDialogosRepartoDividendos dialogos)
{
    private RepartoDividendosVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync(_vm.Seleccion?.IdReparto);
            return;
        }
        _vm = new RepartoDividendosVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
