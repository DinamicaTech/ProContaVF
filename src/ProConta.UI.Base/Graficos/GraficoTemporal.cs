using System.Globalization;
using System.Windows;
using Coordinates = ScottPlot.Coordinates;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Gráfico de líneas con eje de fechas proporcional al tiempo (informes/graficos; derivado para activos/activos-financieros): cientos de puntos
/// diarios, sombreado entre dos series, marcas de compras y ventas, línea vertical y tooltip en el día bajo el ratón, y una serie resaltada (las
/// demás se atenúan). Zoom: la rueda acerca o aleja, arrastrar amplía la zona marcada y el doble clic vuelve a la vista completa; el eje vertical
/// se ajusta a lo que se ve. El dibujo está en <see cref="PlanoTemporal"/> y el zoom en <see cref="ZoomHorizontal"/>.
/// </summary>
public sealed class GraficoTemporal : GraficoBase
{
    public static readonly DependencyProperty DatosProperty = DependencyProperty.Register(nameof(Datos), typeof(DatosTemporales), typeof(GraficoTemporal),
        new PropertyMetadata(null, (d, _) => ((GraficoTemporal)d).AlCambiarDatos()));

    public static readonly DependencyProperty SerieResaltadaProperty = DependencyProperty.Register(nameof(SerieResaltada), typeof(int), typeof(GraficoTemporal),
        new PropertyMetadata(-1, (d, _) => ((GraficoTemporal)d).RedibujarConZoom()));

    public static readonly DependencyProperty FormatoEjeProperty = DependencyProperty.Register(nameof(FormatoEje), typeof(Func<double, string>), typeof(GraficoTemporal),
        new PropertyMetadata(null, (d, _) => ((GraficoTemporal)d).RedibujarConZoom()));

    /// <summary>El periodo visible más corto con el zoom, en días.</summary>
    public const double DiasMinimos = 7;

    private readonly PlanoTemporal _plano;
    private readonly ZoomHorizontal _zoom;
    private (double Desde, double Hasta)? _visible;

    public GraficoTemporal()
    {
        _plano = new PlanoTemporal(Plot);
        _zoom = new ZoomHorizontal(() => _plano.Completo, DiasMinimos)
        {
            AlCambiar = _ =>
            {
                _plano.AjustarY();
                var lim = Plot.Axes.GetLimits();
                _visible = _plano.Completo is { } c && (lim.Left > c.Min + 1e-6 || lim.Right < c.Max - 1e-6) ? (lim.Left, lim.Right) : null;
            },
        };
        Lienzo.UserInputProcessor.UserActionResponses.Add(_zoom);
        ConZoom = true;
    }

    public DatosTemporales? Datos { get => (DatosTemporales?)GetValue(DatosProperty); set => SetValue(DatosProperty, value); }

    /// <summary>Índice de la serie resaltada (−1 ninguna): las demás se dibujan atenuadas.</summary>
    public int SerieResaltada { get => (int)GetValue(SerieResaltadaProperty); set => SetValue(SerieResaltadaProperty, value); }

    /// <summary>Rótulos del eje vertical; por defecto compactos («120k»).</summary>
    public Func<double, string>? FormatoEje { get => (Func<double, string>?)GetValue(FormatoEjeProperty); set => SetValue(FormatoEjeProperty, value); }

    private void AlCambiarDatos()
    {
        if (Datos?.Descripcion is { } d) System.Windows.Automation.AutomationProperties.SetName(this, d);
        // Datos nuevos (otro periodo, otra vista): se vuelve a la vista completa.
        _visible = null;
        Redibujar();
    }

    private void RedibujarConZoom() => Redibujar();

    protected override void Construir(double ancho, double alto) =>
        _plano.Dibujar(Datos, SerieResaltada, FormatoEje, alto, _visible?.Desde, _visible?.Hasta);

    protected override Size MeasureOverride(Size disponible)
    {
        base.MeasureOverride(disponible);
        return new(double.IsInfinity(disponible.Width) ? 600 : disponible.Width, double.IsInfinity(disponible.Height) ? 240 : disponible.Height);
    }

    protected override int IndiceEn(Coordinates c, Point posicion)
    {
        if (!_plano.ConDatos || posicion.Y < PlanoTemporal.Margen.Top - 4 || posicion.Y > ActualHeight - PlanoTemporal.Margen.Bottom + 12) return -1;
        return _plano.IndiceEn(c.X);
    }

    protected override void Resaltar(int indice) => _plano.Resaltar(indice, SerieResaltada);

    protected override ContenidoTooltip? TooltipDe(int indice)
    {
        var d = Datos;
        if (d is null || indice < 0 || indice >= d.Fechas.Count) return null;
        if (d.Tooltip is not null) return d.Tooltip(indice);
        var fmt = FormatoEje ?? FormatoGrafico.Compacto;
        return new ContenidoTooltip(d.Fechas[indice].ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-ES")),
            d.Series.Where(s => indice < s.Valores.Count && s.Valores[indice] is not null).Select(s => new FilaTooltip(s.Nombre, fmt(s.Valores[indice]!.Value), s.Color)).ToList());
    }
}
