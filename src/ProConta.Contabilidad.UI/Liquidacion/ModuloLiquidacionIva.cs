using System.Windows;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Liquidacion;

/// <summary>Diálogos de la pantalla con los mensajes comunes; el motivo y el error, los mismos del Mayor.</summary>
public sealed class DialogosLiquidacionIva(Func<Window?> propietario) : IDialogosLiquidacionIva
{
    private readonly Mayor.DialogosMayor _comunes = new(propietario);

    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, opciones);

    public string? PedirMotivo(string titulo, string explicacion) => _comunes.PedirMotivo(titulo, explicacion);

    public void Error(string titulo, string mensaje) => _comunes.Error(titulo, mensaje);
}

/// <summary>Liquidación de IVA dentro del contenedor (contabilidad/procesos/liquidacion-iva): una sola pestaña por sesión.</summary>
public sealed class ModuloLiquidacionIva(ContextoLiquidacionIva contexto, IAnfitrionPestanas anfitrion, IDialogosLiquidacionIva dialogos)
{
    private LiquidacionIvaVM? _vm;

    /// <summary>Abre (o activa) la pantalla; con <paramref name="trimestre"/>, con ese trimestre elegido.</summary>
    public void Abrir(int? trimestre = null)
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync(trimestre);
            return;
        }
        _vm = new LiquidacionIvaVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync(trimestre);
    }
}
