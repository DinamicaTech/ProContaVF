using ScottPlot;
using ScottPlot.Interactivity;
using ScottPlot.Interactivity.UserActions;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Zoom del eje horizontal de un gráfico con fechas (informes/graficos): la rueda acerca o aleja alrededor del puntero, arrastrar con el botón
/// izquierdo amplía la zona marcada y el doble clic vuelve a la vista completa. Nunca se sale del periodo completo de los datos. Tras cada
/// cambio avisa con <see cref="AlCambiar"/> para que el gráfico ajuste el eje vertical a lo que se ve.
/// </summary>
public sealed class ZoomHorizontal : IUserActionResponse
{
    /// <summary>Fracción del periodo visible que acerca o aleja cada paso de la rueda.</summary>
    public const double PasoRueda = 0.2;

    /// <summary>Píxeles que hay que arrastrar para que cuente como selección de zona (menos es un clic).</summary>
    public const float ArrastreMinimo = 6;

    private readonly Func<CoordinateRange?> _completo;
    private readonly double _anchoMinimo;
    private Pixel _inicio = Pixel.NaN;
    private bool _arrastrando;
    private DateTime _ultimoClic = DateTime.MinValue;

    /// <param name="completo">Periodo completo de los datos (null si no hay datos).</param>
    /// <param name="anchoMinimo">El periodo visible más corto (por ejemplo 7 días).</param>
    public ZoomHorizontal(Func<CoordinateRange?> completo, double anchoMinimo)
    {
        _completo = completo;
        _anchoMinimo = anchoMinimo;
    }

    /// <summary>Se llama tras cambiar el periodo visible (y tras volver a la vista completa).</summary>
    public Action<Plot>? AlCambiar { get; set; }

    public void ResetState(IPlotControl plotControl)
    {
        _inicio = Pixel.NaN;
        _arrastrando = false;
        plotControl.Plot.ZoomRectangle.IsVisible = false;
    }

    public ResponseInfo Execute(IPlotControl plotControl, IUserAction accion, KeyboardState teclas)
    {
        var plot = plotControl.Plot;
        switch (accion)
        {
            case MouseWheelUp arriba:
                return Rueda(plot, arriba.Pixel, 1 / (1 + PasoRueda));
            case MouseWheelDown abajo:
                return Rueda(plot, abajo.Pixel, 1 + PasoRueda);
            case LeftMouseDown pulsado:
                _inicio = pulsado.Pixel;
                _arrastrando = false;
                return ResponseInfo.NoActionRequired;
            case MouseMove mov when !float.IsNaN(_inicio.X):
                if (!_arrastrando && Math.Abs(mov.Pixel.X - _inicio.X) < ArrastreMinimo) return ResponseInfo.NoActionRequired;
                _arrastrando = true;
                plot.ZoomRectangle.IsVisible = true;
                plot.ZoomRectangle.HorizontalSpan = false;
                plot.ZoomRectangle.VerticalSpan = true;
                plot.ZoomRectangle.MouseDown = _inicio.Divide((float)plot.ScaleFactor);
                plot.ZoomRectangle.MouseUp = mov.Pixel.Divide((float)plot.ScaleFactor);
                return new ResponseInfo { RefreshNeeded = true, IsPrimary = true };
            case LeftMouseUp soltado:
                return Soltar(plot, soltado.Pixel);
            default:
                return ResponseInfo.NoActionRequired;
        }
    }

    private ResponseInfo Soltar(Plot plot, Pixel fin)
    {
        var inicio = _inicio;
        var arrastrando = _arrastrando;
        _inicio = Pixel.NaN;
        _arrastrando = false;
        if (arrastrando)
        {
            plot.ZoomRectangle.IsVisible = false;
            var a = plot.GetCoordinates(inicio).X;
            var b = plot.GetCoordinates(fin).X;
            Fijar(plot, Math.Min(a, b), Math.Max(a, b));
            _ultimoClic = DateTime.MinValue;
            return ResponseInfo.Refresh;
        }
        var ahora = DateTime.UtcNow;
        if (ahora - _ultimoClic < TimeSpan.FromMilliseconds(500))
        {
            _ultimoClic = DateTime.MinValue;
            VistaCompleta(plot);
            return ResponseInfo.Refresh;
        }
        _ultimoClic = ahora;
        return ResponseInfo.NoActionRequired;
    }

    private ResponseInfo Rueda(Plot plot, Pixel pixel, double factor)
    {
        var limites = plot.Axes.GetLimits();
        var x = plot.GetCoordinates(pixel).X;
        if (double.IsNaN(x) || limites.HorizontalSpan <= 0) return ResponseInfo.NoActionRequired;
        var t = Math.Clamp((x - limites.Left) / limites.HorizontalSpan, 0, 1);
        var ancho = limites.HorizontalSpan * factor;
        Fijar(plot, x - ancho * t, x + ancho * (1 - t));
        return ResponseInfo.Refresh;
    }

    /// <summary>Vuelve al periodo completo de los datos.</summary>
    public void VistaCompleta(Plot plot)
    {
        if (_completo() is not { } c) return;
        plot.Axes.SetLimitsX(c.Min, c.Max);
        AlCambiar?.Invoke(plot);
    }

    /// <summary>Muestra de <paramref name="desde"/> a <paramref name="hasta"/>, sin salir del periodo completo ni bajar del ancho mínimo.</summary>
    public void Fijar(Plot plot, double desde, double hasta)
    {
        if (_completo() is not { } c) return;
        var (a, b) = Recortar(desde, hasta, c.Min, c.Max, _anchoMinimo);
        plot.Axes.SetLimitsX(a, b);
        AlCambiar?.Invoke(plot);
    }

    /// <summary>
    /// Ajusta un periodo [desde, hasta] para que quepa en [min, max] (desplazándolo si se sale) y no sea más corto que <paramref name="anchoMinimo"/>.
    /// Pura, para las pruebas.
    /// </summary>
    public static (double Desde, double Hasta) Recortar(double desde, double hasta, double min, double max, double anchoMinimo)
    {
        if (hasta < desde) (desde, hasta) = (hasta, desde);
        var total = max - min;
        if (total <= 0) return (min, max);
        var ancho = Math.Clamp(hasta - desde, Math.Min(anchoMinimo, total), total);
        var centro = (desde + hasta) / 2;
        var a = centro - ancho / 2;
        if (a < min) a = min;
        if (a + ancho > max) a = max - ancho;
        return (a, a + ancho);
    }
}
