using System.IO;
using ScottPlot;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Estilo común de los gráficos sobre ScottPlot (informes/graficos): colores de la paleta, fuente IBM Plex Sans de la biblioteca, rejilla
/// horizontal suave sin marco y márgenes fijos. Solo usa el núcleo de ScottPlot (sin WPF), así se puede dibujar y probar fuera de la ventana.
/// </summary>
public static class EstiloPlano
{
    /// <summary>Nombre con el que se registra la fuente de los gráficos en ScottPlot.</summary>
    public const string Fuente = "IBM Plex Sans";

    public const string Tinta = "#16213A";
    public const string TextoApagado = "#646A78";
    public const string Pista = "#F3F0E9";

    private static readonly object Cerrojo = new();
    private static bool _fuentesCargadas;
    private static bool _fuentePropia;

    /// <summary>Color de ScottPlot desde «#RRGGBB» o «#AARRGGBB».</summary>
    public static Color De(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) return Color.FromHex(h[2..]).WithAlpha(Convert.ToByte(h[..2], 16));
        return Color.FromHex(h);
    }

    /// <summary>El mismo color con una opacidad de 0 a 1.</summary>
    public static Color De(string hex, double opacidad) => De(hex).WithAlpha(opacidad);

    /// <summary>
    /// Registra IBM Plex Sans (normal y seminegrita) desde la carpeta Fonts junto al programa, una sola vez. Si no están los ficheros, ScottPlot
    /// usa la fuente del sistema.
    /// </summary>
    public static void CargarFuentes()
    {
        lock (Cerrojo)
        {
            if (_fuentesCargadas) return;
            _fuentesCargadas = true;
            var carpeta = Path.Combine(AppContext.BaseDirectory, "Fonts");
            var normal = Path.Combine(carpeta, "IBMPlexSans-Regular.ttf");
            var negrita = Path.Combine(carpeta, "IBMPlexSans-SemiBold.ttf");
            try
            {
                if (!File.Exists(normal)) return;
                Fonts.AddFontFile(Fuente, normal);
                if (File.Exists(negrita)) Fonts.AddFontFile(Fuente, negrita, bold: true);
                _fuentePropia = true;
            }
            catch (Exception)
            {
                // Sin la fuente propia el gráfico se dibuja igual, con la del sistema.
            }
        }
    }

    private static string FuenteDisponible => _fuentePropia ? Fuente : Fonts.Default;

    /// <summary>
    /// Deja el gráfico vacío y con el estilo común: fondo transparente, sin marco ni leyenda, rejilla horizontal suave, rótulos de los ejes con la
    /// fuente de la biblioteca y el área de datos con los márgenes fijos <paramref name="margen"/> (en píxeles lógicos).
    /// </summary>
    public static void Preparar(Plot plot, PixelPadding margen, bool ejes = true)
    {
        CargarFuentes();
        plot.Clear();
        plot.FigureBackground.Color = Colors.Transparent;
        plot.DataBackground.Color = Colors.Transparent;
        plot.HideLegend();
        plot.Axes.Frame(false);
        plot.Axes.Margins(0, 0);
        plot.Axes.Title.IsVisible = false;
        plot.Layout.Fixed(margen);
        var fuente = FuenteDisponible;
        foreach (var eje in new IAxis[] { plot.Axes.Left, plot.Axes.Bottom })
        {
            eje.IsVisible = ejes;
            eje.Label.Text = "";
            eje.TickLabelStyle.FontName = fuente;
            eje.TickLabelStyle.FontSize = 11;
            eje.TickLabelStyle.ForeColor = De(TextoApagado);
            eje.MajorTickStyle.Length = 0;
            eje.MinorTickStyle.Length = 0;
            eje.FrameLineStyle.Width = 0;
        }
        plot.Axes.Right.IsVisible = false;
        plot.Axes.Top.IsVisible = false;
        plot.Axes.Bottom.MajorTickStyle.Length = 4;
        plot.Axes.Bottom.MajorTickStyle.Color = De(PaletaGraficos.Rejilla);
        plot.Grid.IsVisible = ejes;
        plot.Grid.XAxisStyle.IsVisible = false;
        plot.Grid.YAxisStyle.MajorLineStyle.Color = De(PaletaGraficos.Rejilla);
        plot.Grid.YAxisStyle.MajorLineStyle.Width = 1;
        plot.Grid.YAxisStyle.MajorLineStyle.AntiAlias = false;
        plot.Grid.YAxisStyle.MinorLineStyle.Width = 0;
        if (plot.ZoomRectangle is ScottPlot.Plottables.ZoomRectangle zr)
        {
            zr.FillStyle.Color = De(PaletaGraficos.Serie1, 0.12);
            zr.LineStyle.Color = De(PaletaGraficos.Serie1, 0.6);
        }
    }

    /// <summary>Texto fijo en el gráfico (rótulo de un valor, «Sin datos…»), con la fuente de la biblioteca.</summary>
    public static ScottPlot.Plottables.Text Rotulo(Plot plot, string texto, double x, double y, string color = Tinta, bool negrita = false,
        Alignment alineacion = Alignment.LowerCenter, float tamano = 11, float desplazamientoY = 0)
    {
        var t = plot.Add.Text(texto, x, y);
        t.LabelFontName = FuenteDisponible;
        t.LabelFontSize = tamano;
        t.LabelFontColor = De(color);
        t.LabelBold = negrita;
        t.LabelAlignment = alineacion;
        t.LabelBackgroundColor = Colors.Transparent;
        t.LabelBorderWidth = 0;
        t.OffsetY = desplazamientoY;
        return t;
    }

    /// <summary>Una línea sin marcadores entre los puntos dados.</summary>
    public static ScottPlot.Plottables.Scatter Linea(Plot plot, double[] xs, double[] ys, string color, float grosor = 2, LinePattern? patron = null, double opacidad = 1)
    {
        var s = plot.Add.Scatter(xs, ys, De(color, opacidad));
        s.LineWidth = grosor;
        s.MarkerSize = 0;
        s.LineStyle.AntiAlias = true;
        if (patron is { } p) s.LinePattern = p;
        return s;
    }

    /// <summary>Punto redondo con borde blanco (último valor, día bajo el ratón).</summary>
    public static ScottPlot.Plottables.Marker Punto(Plot plot, double x, double y, string color, float diametro = 8, bool hueco = false)
    {
        var m = plot.Add.Marker(x, y, MarkerShape.FilledCircle, diametro, hueco ? Colors.White : De(color));
        m.MarkerStyle.OutlineColor = hueco ? De(color) : Colors.White;
        m.MarkerStyle.OutlineWidth = hueco ? 2 : 1.5f;
        return m;
    }

    /// <summary>Trazo a rayas cortas, como las proyecciones de la maqueta.</summary>
    public static LinePattern Trazos { get; } = new([5f, 4f], 0f, "Trazos");

    /// <summary>Trazo de puntos sutil (periodos sin el activo en cartera).</summary>
    public static LinePattern Puntos { get; } = new([2f, 4f], 0f, "Puntos");
}
