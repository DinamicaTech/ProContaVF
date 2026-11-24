using System.Windows;
using Coordinates = ScottPlot.Coordinates;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Gráfico con eje de valores y eje de categorías (meses): rejilla horizontal, rótulos del eje y tooltip por categoría. Base de
/// <see cref="GraficoBarras"/> y <see cref="GraficoLineas"/>; el dibujo está en <see cref="PlanoCategorias"/>.
/// </summary>
public abstract class GraficoCategorias : GraficoBase
{
    public static readonly DependencyProperty DatosProperty = DependencyProperty.Register(nameof(Datos), typeof(DatosGrafico), typeof(GraficoCategorias),
        new PropertyMetadata(null, (d, _) => ((GraficoCategorias)d).AlCambiarDatos()));

    private readonly PlanoCategorias _plano;

    protected GraficoCategorias() => _plano = new PlanoCategorias(Plot);

    /// <summary>Series y categorías que se dibujan.</summary>
    public DatosGrafico? Datos { get => (DatosGrafico?)GetValue(DatosProperty); set => SetValue(DatosProperty, value); }

    /// <summary>Formato de los valores en el tooltip; por defecto en miles de euros con un decimal.</summary>
    public Func<decimal, string> FormatoValor { get; set; } = FormatoGrafico.Miles;

    /// <summary>Formato de los rótulos del eje de valores; por defecto compacto («120k»).</summary>
    public Func<double, string> FormatoEje { get; set; } = FormatoGrafico.Compacto;

    protected abstract bool EnBarras { get; }

    private void AlCambiarDatos()
    {
        if (Datos?.Descripcion is { } d) System.Windows.Automation.AutomationProperties.SetName(this, d);
        Redibujar();
    }

    protected override void Construir(double ancho, double alto) => _plano.Dibujar(Datos, EnBarras, FormatoEje, ancho, alto);

    protected override int IndiceEn(Coordinates c, Point posicion)
    {
        var lim = Plot.Axes.GetLimits();
        return c.Y < lim.Bottom || c.Y > lim.Top ? -1 : _plano.IndiceEn(c.X);
    }

    protected override void Resaltar(int indice) => _plano.Resaltar(indice);

    protected override ContenidoTooltip? TooltipDe(int indice) => Datos?.TooltipDe(indice, FormatoValor);

    protected override Size MeasureOverride(Size disponible)
    {
        base.MeasureOverride(disponible);
        return new(double.IsInfinity(disponible.Width) ? 400 : disponible.Width, double.IsInfinity(disponible.Height) ? 220 : disponible.Height);
    }
}

/// <summary>
/// Barras agrupadas por categoría (informes/graficos): una barra por serie en cada mes; la proyección de una serie se apila encima de su valor
/// real con un rayado del mismo color y borde. Un valor nulo no dibuja barra (hueco). Rotula el valor más alto de la primera serie.
/// </summary>
public sealed class GraficoBarras : GraficoCategorias
{
    protected override bool EnBarras => true;
}

/// <summary>
/// Líneas por categoría (informes/graficos): cada serie es una línea con un punto al final; un nulo corta la línea (no se interpola el hueco).
/// Una serie discontinua se dibuja a trazos con el último punto hueco (proyección o previsión) y una serie con área rellena suavemente hasta la
/// base. Opcionalmente, una marca vertical («hoy»).
/// </summary>
public sealed class GraficoLineas : GraficoCategorias
{
    protected override bool EnBarras => false;
}
