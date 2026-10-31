using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProConta.UI.Base.Controles;

/// <summary>Estado de una vista de datos.</summary>
public enum EstadoVista
{
    Contenido,
    Vacio,
    Cargando,
    Error,
    SinPermiso
}

/// <summary>
/// Muestra el contenido o, en su lugar, un estado vacío, de carga, de error o sin permiso, con título, explicación y una acción
/// que lleva a la solución (regla «sin callejones sin salida en los errores»).
/// </summary>
public class VistaEstado : ContentControl
{
    static VistaEstado()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VistaEstado), new FrameworkPropertyMetadata(typeof(VistaEstado)));
    }

    public static readonly DependencyProperty EstadoProperty = DependencyProperty.Register(nameof(Estado), typeof(EstadoVista), typeof(VistaEstado), new PropertyMetadata(EstadoVista.Contenido));
    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(nameof(Titulo), typeof(string), typeof(VistaEstado), new PropertyMetadata(""));
    public static readonly DependencyProperty MensajeProperty = DependencyProperty.Register(nameof(Mensaje), typeof(string), typeof(VistaEstado), new PropertyMetadata(""));
    public static readonly DependencyProperty TextoAccionProperty = DependencyProperty.Register(nameof(TextoAccion), typeof(string), typeof(VistaEstado), new PropertyMetadata(null));
    public static readonly DependencyProperty AyudaAccionProperty = DependencyProperty.Register(nameof(AyudaAccion), typeof(string), typeof(VistaEstado), new PropertyMetadata(null));
    public static readonly DependencyProperty ComandoAccionProperty = DependencyProperty.Register(nameof(ComandoAccion), typeof(ICommand), typeof(VistaEstado), new PropertyMetadata(null));

    public EstadoVista Estado { get => (EstadoVista)GetValue(EstadoProperty); set => SetValue(EstadoProperty, value); }
    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Mensaje { get => (string)GetValue(MensajeProperty); set => SetValue(MensajeProperty, value); }
    public string? TextoAccion { get => (string?)GetValue(TextoAccionProperty); set => SetValue(TextoAccionProperty, value); }
    public string? AyudaAccion { get => (string?)GetValue(AyudaAccionProperty); set => SetValue(AyudaAccionProperty, value); }
    public ICommand? ComandoAccion { get => (ICommand?)GetValue(ComandoAccionProperty); set => SetValue(ComandoAccionProperty, value); }
}
