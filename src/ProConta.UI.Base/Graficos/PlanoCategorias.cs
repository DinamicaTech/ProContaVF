using ScottPlot;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Dibujo de los gráficos por categorías (meses) sobre ScottPlot (informes/graficos): barras agrupadas con la proyección apilada y rayada, o líneas
/// cortadas en los huecos con área suave, tramo discontinuo y marca vertical («hoy»). La categoría <c>i</c> ocupa de <c>i</c> a <c>i + 1</c> en el eje
/// horizontal. Solo usa el núcleo de ScottPlot, así que se puede dibujar a PNG en las pruebas.
/// </summary>
public sealed class PlanoCategorias(Plot plot)
{
    private const string Banda = "#F3F0E9";
    private readonly List<IPlottable> _resaltado = [];
    private DatosGrafico? _datos;
    private bool _barras;
    private double _anchoArea;

    public Plot Plot { get; } = plot;

    /// <summary>Márgenes del área de datos (izquierda, derecha, abajo, arriba); más alto arriba si hay marca con rótulo.</summary>
    public static PixelPadding MargenDe(DatosGrafico? datos) => new(48, 10, 24, datos?.Marca is null ? 14 : 24);

    private int N => _datos?.Categorias.Count ?? 0;

    /// <summary>
    /// Dibuja <paramref name="datos"/> como barras o líneas en un control de <paramref name="ancho"/> por <paramref name="alto"/> píxeles lógicos.
    /// </summary>
    public void Dibujar(DatosGrafico? datos, bool barras, Func<double, string> formatoEje, double ancho, double alto)
    {
        _datos = datos;
        _barras = barras;
        _resaltado.Clear();
        var margen = MargenDe(datos);
        EstiloPlano.Preparar(Plot, margen);
        _anchoArea = Math.Max(1, ancho - margen.Left - margen.Right);
        var altoArea = Math.Max(20, alto - margen.Top - margen.Bottom);
        if (datos is null || N == 0)
        {
            Plot.Axes.Left.IsVisible = Plot.Axes.Bottom.IsVisible = Plot.Grid.IsVisible = false;
            Plot.Axes.SetLimits(0, 1, 0, 1);
            return;
        }
        var rango = EscalasGrafico.Rango(datos.ValoresEje(barras), minFijo: datos.Minimo, maxFijo: datos.Maximo);
        Plot.Axes.Left.TickGenerator = TicksFijos.Valores(rango, formatoEje);
        var paso = _anchoArea / N < 26 ? 2 : 1;
        Plot.Axes.Bottom.TickGenerator = new TicksFijos(Enumerable.Range(0, N).Where(i => i % paso == 0).Select(i => Tick.Major(i + 0.5, datos.Categorias[i])));
        Plot.Axes.Bottom.MajorTickStyle.Length = 0;
        // Sitio arriba para el rótulo de la barra más alta o de la marca, sin mover las divisiones.
        var porPixel = (rango.Max - rango.Min) / altoArea;
        Plot.Axes.SetLimits(0, N, rango.Min, rango.Max + (barras ? 16 * porPixel : 0));
        if (rango.Min < 0) Plot.Add.HorizontalLine(0, 1, EstiloPlano.De("#C9C4B8")).LineStyle.AntiAlias = false;
        if (barras) DibujarBarras(datos, formatoEje);
        else DibujarLineas(datos, rango, porPixel);
    }

    private void DibujarBarras(DatosGrafico datos, Func<double, string> formatoEje)
    {
        var series = datos.Series;
        var banda = _anchoArea / N;
        var grupo = GeometriaGrafico.BarrasDelGrupo(new Caja(0, 0, banda, 1), series.Count);
        var lista = new List<Bar>();
        (int Cat, int Serie, double Valor)? destacado = null;
        for (var i = 0; i < N; i++)
            for (var s = 0; s < series.Count; s++)
            {
                var serie = series[s];
                var v = i < serie.Valores.Count ? serie.Valores[i] : null;
                var p = serie.Proyeccion is { } pr && i < pr.Count ? pr[i] : null;
                var posicion = i + (grupo[s].X + grupo[s].Ancho / 2) / banda;
                var tamano = grupo[s].Ancho / banda;
                if (v is decimal real && real != 0)
                {
                    lista.Add(new Bar { Position = posicion, Value = (double)real, ValueBase = 0, Size = tamano, FillColor = EstiloPlano.De(serie.Color), LineWidth = 0 });
                    if (s == 0 && real > 0 && (destacado is null || (double)real > destacado.Value.Valor)) destacado = (i, s, (double)real);
                }
                if (p is decimal proy && proy > 0)
                {
                    var baseReal = (double)Math.Max(0, v ?? 0);
                    var bar = new Bar
                    {
                        Position = posicion, ValueBase = baseReal, Value = baseReal + (double)proy, Size = tamano,
                        FillColor = Colors.White, LineColor = EstiloPlano.De(serie.Color), LineWidth = 1,
                    };
                    bar.FillStyle.Hatch = new ScottPlot.Hatches.Striped(ScottPlot.Hatches.StripeDirection.DiagonalUp);
                    bar.FillStyle.HatchColor = EstiloPlano.De(serie.Color);
                    lista.Add(bar);
                }
            }
        if (lista.Count > 0) Plot.Add.Bars(lista);
        if (destacado is { } d)
        {
            var b = lista.First(x => Math.Abs(x.Value - d.Valor) < 1e-9 && (int)x.Position == d.Cat);
            EstiloPlano.Rotulo(Plot, formatoEje(d.Valor), b.Position, d.Valor, negrita: true, desplazamientoY: -4);
        }
    }

    private void DibujarLineas(DatosGrafico datos, RangoEje rango, double porPixel)
    {
        if (datos.Marca is int m && m >= 0 && m < N)
        {
            var linea = Plot.Add.VerticalLine(m + 0.5, 1, EstiloPlano.De(PaletaGraficos.Apagado), new LinePattern([2f, 3f], 0f, "Marca"));
            linea.LineStyle.AntiAlias = false;
            // La línea sube hasta el rótulo, por encima de la última división.
            Plot.Axes.SetLimitsY(rango.Min, rango.Max + 14 * porPixel);
            if (datos.TextoMarca is { } t) EstiloPlano.Rotulo(Plot, t, m + 0.5, rango.Max + 14 * porPixel, EstiloPlano.TextoApagado, alineacion: Alignment.UpperCenter);
        }
        var baseY = Math.Max(rango.Min, 0);
        foreach (var serie in datos.Series)
        {
            foreach (var tramo in GeometriaGrafico.Tramos(serie.Valores))
            {
                var xs = tramo.Select(i => i + 0.5).ToArray();
                var ys = tramo.Select(i => (double)serie.Valores[i]!.Value).ToArray();
                if (xs.Length == 1)
                {
                    Plot.Add.Marker(xs[0], ys[0], MarkerShape.FilledCircle, 5, EstiloPlano.De(serie.Color));
                    continue;
                }
                var l = EstiloPlano.Linea(Plot, xs, ys, serie.Color, 2, serie.Discontinua ? EstiloPlano.Trazos : null);
                if (serie.Area)
                {
                    l.FillY = true;
                    l.FillYValue = baseY;
                    l.FillYAboveColor = l.FillYBelowColor = EstiloPlano.De(serie.Color, 0.10);
                }
            }
            var ultimo = Enumerable.Range(0, serie.Valores.Count).LastOrDefault(i => serie.Valores[i] is not null, -1);
            if (ultimo >= 0)
            {
                var y = (double)serie.Valores[ultimo]!.Value;
                EstiloPlano.Punto(Plot, ultimo + 0.5, y, serie.Color, serie.Discontinua ? 7 : 9, hueco: serie.Discontinua);
                if (serie.Rotulo is { } rotulo)
                    EstiloPlano.Rotulo(Plot, rotulo, ultimo + 0.5, y, negrita: true, alineacion: Alignment.LowerRight, desplazamientoY: -6).OffsetX = -6;
            }
        }
    }

    /// <summary>Categoría bajo la coordenada <paramref name="x"/>; −1 fuera.</summary>
    public int IndiceEn(double x) => N == 0 || x < 0 || x >= N ? -1 : (int)x;

    /// <summary>Resalta la categoría <paramref name="indice"/> (−1 ninguna): banda de fondo en barras; línea vertical y puntos en líneas.</summary>
    public void Resaltar(int indice)
    {
        foreach (var p in _resaltado) Plot.Remove(p);
        _resaltado.Clear();
        if (_datos is not { } d || indice < 0 || indice >= N) return;
        if (_barras)
        {
            var lim = Plot.Axes.GetLimits();
            var r = Plot.Add.Rectangle(indice, indice + 1, lim.Bottom, lim.Top);
            r.FillStyle.Color = EstiloPlano.De(Banda);
            r.LineStyle.Width = 0;
            // Detrás de las barras.
            Plot.PlottableList.Remove(r);
            Plot.PlottableList.Insert(0, r);
            _resaltado.Add(r);
            return;
        }
        var linea = Plot.Add.VerticalLine(indice + 0.5, 1, EstiloPlano.De(PaletaGraficos.Apagado));
        linea.LineStyle.AntiAlias = false;
        _resaltado.Add(linea);
        foreach (var serie in d.Series)
            if (indice < serie.Valores.Count && serie.Valores[indice] is decimal v)
                _resaltado.Add(EstiloPlano.Punto(Plot, indice + 0.5, (double)v, serie.Color, 9));
    }
}
