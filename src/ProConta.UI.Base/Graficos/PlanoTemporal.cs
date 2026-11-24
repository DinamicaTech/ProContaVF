using ScottPlot;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Dibujo del gráfico con eje de fechas sobre ScottPlot (informes/graficos; derivado para activos/activos-financieros): series diarias cortadas en
/// los huecos, tramos sutiles (punteados y tenues), líneas en escalón, sombreado verde o rojo entre dos series, compras ▲ y ventas ▼ al pie y el
/// día bajo el ratón. El eje horizontal va en días OLE (<see cref="DateTime.ToOADate"/>); con el zoom, el vertical se ajusta a lo que se ve.
/// Solo usa el núcleo de ScottPlot, así que se puede dibujar a PNG en las pruebas.
/// </summary>
public sealed class PlanoTemporal(Plot plot)
{
    private const string Verde = "#1E8E5A", Rojo = "#C2412D", Ambar = "#B7791F";

    /// <summary>Márgenes del área de datos en píxeles lógicos: izquierda (rótulos del eje), derecha, abajo (fechas) y arriba.</summary>
    public static PixelPadding Margen { get; } = new(56, 12, 22, 8);

    /// <summary>Alto, en píxeles, de la franja al pie para las compras y ventas.</summary>
    public const double FranjaMarcas = 14;

    private readonly List<IPlottable> _resaltado = [];
    private readonly List<ScottPlot.Plottables.Marker> _marcas = [];
    private DatosTemporales? _datos;
    private Func<double, string> _formato = FormatoGrafico.Compacto;
    private double _altoArea = 200;

    public Plot Plot { get; } = plot;

    /// <summary>Fechas en días OLE, una por fecha de los datos.</summary>
    public double[] Xs { get; private set; } = [];

    /// <summary>Periodo completo de los datos (null sin datos).</summary>
    public CoordinateRange? Completo => Xs.Length >= 2 ? new CoordinateRange(Xs[0], Xs[^1]) : null;

    /// <summary>Hay datos suficientes para dibujar líneas.</summary>
    public bool ConDatos => _datos is not null && Xs.Length >= 2;

    /// <summary>
    /// Dibuja los datos: <paramref name="serieResaltada"/> (−1 ninguna) va encima y más gruesa y las demás atenuadas. <paramref name="alto"/> es el
    /// alto del control en píxeles lógicos (para el número de divisiones). Con <paramref name="desde"/>/<paramref name="hasta"/> conserva un zoom.
    /// </summary>
    public void Dibujar(DatosTemporales? datos, int serieResaltada, Func<double, string>? formato, double alto, double? desde = null, double? hasta = null)
    {
        _datos = datos;
        _formato = formato ?? FormatoGrafico.Compacto;
        _altoArea = Math.Max(40, alto - Margen.Top - Margen.Bottom);
        _resaltado.Clear();
        _marcas.Clear();
        EstiloPlano.Preparar(Plot, Margen);
        Xs = datos?.Fechas.Select(f => f.ToOADate()).ToArray() ?? [];
        if (datos is null || Xs.Length < 2)
        {
            Plot.Axes.Left.IsVisible = Plot.Axes.Bottom.IsVisible = Plot.Grid.IsVisible = false;
            Plot.Axes.SetLimits(0, 1, 0, 1);
            if (datos is not null) EstiloPlano.Rotulo(Plot, "Sin datos para el periodo", 0.5, 0.5, EstiloPlano.TextoApagado, alineacion: Alignment.MiddleCenter);
            return;
        }
        Plot.Axes.Bottom.TickGenerator = new TicksFechas();

        if (datos.LineaCero)
        {
            var cero = Plot.Add.HorizontalLine(0, 1, EstiloPlano.De(PaletaGraficos.Apagado));
            cero.LineStyle.AntiAlias = false;
        }

        if (datos.AreaEntre is { } ae && ae.A < datos.Series.Count && ae.B < datos.Series.Count)
        {
            var (pos, neg) = GeometriaTemporal.Relleno(Xs, datos.Series[ae.A].Valores, datos.Series[ae.B].Valores, v => v);
            Poligonos(pos, Verde);
            Poligonos(neg, Rojo);
        }

        var orden = Enumerable.Range(0, datos.Series.Count).OrderBy(i => i == serieResaltada ? 1 : 0);
        foreach (var i in orden) DibujarSerie(datos.Series[i], serieResaltada >= 0 && i != serieResaltada, serieResaltada == i);

        if (datos.Marcas is { Count: > 0 } marcas)
            foreach (var m in marcas.Where(m => m.Indice >= 0 && m.Indice < Xs.Length))
            {
                var mk = Plot.Add.Marker(Xs[m.Indice], 0, m.Compra ? MarkerShape.FilledTriangleUp : MarkerShape.FilledTriangleDown, 9,
                    EstiloPlano.De(m.Compra ? Verde : Ambar));
                _marcas.Add(mk);
            }

        Plot.Axes.SetLimitsX(desde ?? Xs[0], hasta ?? Xs[^1]);
        if (desde is not null || hasta is not null)
        {
            var (a, b) = ZoomHorizontal.Recortar(desde ?? Xs[0], hasta ?? Xs[^1], Xs[0], Xs[^1], 7);
            Plot.Axes.SetLimitsX(a, b);
        }
        AjustarY();
    }

    private void Poligonos(IReadOnlyList<IReadOnlyList<Punto>> poligonos, string color)
    {
        foreach (var p in poligonos.Where(p => p.Count >= 3))
        {
            var pol = Plot.Add.Polygon(p.Select(q => new Coordinates(q.X, q.Y)).ToArray());
            pol.FillStyle.Color = EstiloPlano.De(color, 0.2);
            pol.LineStyle.Width = 0;
        }
    }

    private void DibujarSerie(SerieTemporal s, bool atenuada, bool resaltada)
    {
        var grosor = (float)(resaltada ? s.Grosor + 1 : s.Grosor);
        foreach (var (indices, sutil) in GeometriaTemporal.TramosConEstilo(s.Valores, s.Sutil))
        {
            var opacidad = sutil ? 0.35 : atenuada ? 0.28 : 1;
            if (indices.Count == 1)
            {
                var k = indices[0];
                Plot.Add.Marker(Xs[k], s.Valores[k]!.Value, MarkerShape.FilledCircle, 3, EstiloPlano.De(s.Color, opacidad));
                continue;
            }
            var (xs, ys) = Puntos(indices, s.Valores, s.Escalon);
            var patron = sutil ? EstiloPlano.Puntos : s.Discontinua ? EstiloPlano.Trazos : (LinePattern?)null;
            EstiloPlano.Linea(Plot, xs, ys, s.Color, sutil ? Math.Max(1, grosor - 0.5f) : grosor, patron, opacidad);
        }
    }

    private (double[] Xs, double[] Ys) Puntos(IReadOnlyList<int> indices, IReadOnlyList<double?> valores, bool escalon)
    {
        if (!escalon) return (indices.Select(k => Xs[k]).ToArray(), indices.Select(k => valores[k]!.Value).ToArray());
        var xs = new List<double>();
        var ys = new List<double>();
        for (var j = 0; j < indices.Count; j++)
        {
            var v = valores[indices[j]]!.Value;
            if (j > 0)
            {
                xs.Add(Xs[indices[j]]);
                ys.Add(valores[indices[j - 1]]!.Value);
            }
            xs.Add(Xs[indices[j]]);
            ys.Add(v);
        }
        return (xs.ToArray(), ys.ToArray());
    }

    /// <summary>
    /// Ajusta el eje vertical a los valores del periodo visible, con divisiones «redondas» y sitio al pie para las compras y ventas. Se llama
    /// tras cada zoom.
    /// </summary>
    public void AjustarY()
    {
        if (_datos is not { } d || Xs.Length < 2) return;
        var lim = Plot.Axes.GetLimits();
        var desde = Array.FindIndex(Xs, x => x >= lim.Left);
        var hasta = Array.FindLastIndex(Xs, x => x <= lim.Right);
        if (desde < 0) desde = 0;
        if (hasta < desde) hasta = Math.Min(Xs.Length - 1, desde + 1);
        // Un punto a cada lado, para que la línea que entra y sale del borde también quepa.
        desde = Math.Max(0, desde - 1);
        hasta = Math.Min(Xs.Length - 1, hasta + 1);
        var visibles = d.Series.SelectMany(s => Enumerable.Range(desde, hasta - desde + 1).Where(k => k < s.Valores.Count).Select(k => s.Valores[k]));
        var rango = GeometriaTemporal.RangoValores(visibles, d.IncluirCero, Math.Max(3, (int)(_altoArea / 40)));
        Plot.Axes.Left.TickGenerator = TicksFijos.Valores(rango, _formato);
        var abajo = rango.Min;
        if (_marcas.Count > 0)
        {
            var porPixel = (rango.Max - rango.Min) / Math.Max(20, _altoArea - FranjaMarcas);
            abajo = rango.Min - FranjaMarcas * porPixel;
            foreach (var m in _marcas) m.Y = rango.Min - FranjaMarcas / 2 * porPixel;
        }
        Plot.Axes.SetLimitsY(abajo, rango.Max);
    }

    /// <summary>Índice de la fecha más cercana a la coordenada <paramref name="x"/> (días OLE), dentro del periodo visible; −1 si no hay.</summary>
    public int IndiceEn(double x)
    {
        if (!ConDatos) return -1;
        var lim = Plot.Axes.GetLimits();
        if (x < lim.Left - lim.HorizontalSpan * 0.02 || x > lim.Right + lim.HorizontalSpan * 0.02) return -1;
        return GeometriaTemporal.IndiceMasCercano(Xs, x);
    }

    /// <summary>Marca el día <paramref name="indice"/> (−1 ninguno): línea vertical a trazos y un punto en cada serie (solo la resaltada si la hay).</summary>
    public void Resaltar(int indice, int serieResaltada)
    {
        foreach (var p in _resaltado) Plot.Remove(p);
        _resaltado.Clear();
        if (_datos is not { } d || indice < 0 || indice >= Xs.Length) return;
        var linea = Plot.Add.VerticalLine(Xs[indice], 1, EstiloPlano.De(PaletaGraficos.Apagado), EstiloPlano.Trazos);
        linea.LineStyle.AntiAlias = false;
        _resaltado.Add(linea);
        for (var i = 0; i < d.Series.Count; i++)
            if (indice < d.Series[i].Valores.Count && d.Series[i].Valores[indice] is double v && (serieResaltada < 0 || serieResaltada == i))
                _resaltado.Add(EstiloPlano.Punto(Plot, Xs[indice], v, d.Series[i].Color, 8));
    }
}
