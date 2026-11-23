using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ProConta.UI.Base.Graficos;

/// <summary>Una serie del gráfico temporal: un valor por fecha (null = hueco, no se interpola).</summary>
/// <param name="Grosor">Grosor de la línea en píxeles.</param>
public sealed record SerieTemporal(string Nombre, string Color, IReadOnlyList<double?> Valores, bool Discontinua = false, double Grosor = 2);

/// <summary>Una marca en el eje de fechas (una compra ▲ o una venta ▼) en el índice de la fecha.</summary>
public sealed record MarcaTemporal(int Indice, bool Compra);

/// <summary>
/// Datos del gráfico temporal: fechas (ordenadas) y series con un valor por fecha. Con <paramref name="AreaEntre"/> (serie A, serie B) se sombrea el
/// espacio entre las dos: verde donde A está por encima de B y rojo donde está por debajo (por ejemplo, valor frente a coste: la plusvalía latente).
/// </summary>
/// <param name="IncluirCero">El eje vertical llega hasta el cero (importes); si no, se ajusta a los valores (precios).</param>
/// <param name="LineaCero">Dibuja más marcada la línea del cero (variaciones en %).</param>
public sealed record DatosTemporales(IReadOnlyList<DateTime> Fechas, IReadOnlyList<SerieTemporal> Series, Func<int, ContenidoTooltip>? Tooltip = null,
    (int A, int B)? AreaEntre = null, IReadOnlyList<MarcaTemporal>? Marcas = null, bool IncluirCero = false, bool LineaCero = false, string? Descripcion = null);

/// <summary>
/// Gráfico de líneas con eje de fechas proporcional al tiempo (informes/graficos; derivado para activos/activos-financieros): cientos de puntos diarios,
/// sombreado entre dos series, marcas de compras y ventas, línea vertical y tooltip en el día bajo el ratón, y una serie resaltada (las demás se
/// atenúan) al pasar el ratón por la leyenda. La geometría está en <see cref="GeometriaTemporal"/>.
/// </summary>
public sealed class GraficoTemporal : GraficoBase
{
    public static readonly DependencyProperty DatosProperty = DependencyProperty.Register(nameof(Datos), typeof(DatosTemporales), typeof(GraficoTemporal),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((GraficoTemporal)d).AlCambiarDatos()));

    public static readonly DependencyProperty SerieResaltadaProperty = DependencyProperty.Register(nameof(SerieResaltada), typeof(int), typeof(GraficoTemporal),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FormatoEjeProperty = DependencyProperty.Register(nameof(FormatoEje), typeof(Func<double, string>), typeof(GraficoTemporal),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public DatosTemporales? Datos { get => (DatosTemporales?)GetValue(DatosProperty); set => SetValue(DatosProperty, value); }

    /// <summary>Índice de la serie resaltada (−1 ninguna): las demás se dibujan atenuadas.</summary>
    public int SerieResaltada { get => (int)GetValue(SerieResaltadaProperty); set => SetValue(SerieResaltadaProperty, value); }

    /// <summary>Rótulos del eje vertical; por defecto compactos («120k»).</summary>
    public Func<double, string>? FormatoEje { get => (Func<double, string>?)GetValue(FormatoEjeProperty); set => SetValue(FormatoEjeProperty, value); }

    private static readonly Margenes Margen = new(56, 10, 12, 24);
    private const string Verde = "#1E8E5A", Rojo = "#C2412D";

    private Caja Area => GeometriaTemporal.AreaDe(ActualWidth, ActualHeight, Margen, Datos?.Marcas is { Count: > 0 });

    private void AlCambiarDatos()
    {
        if (Datos?.Descripcion is { } d) System.Windows.Automation.AutomationProperties.SetName(this, d);
    }

    protected override Size MeasureOverride(Size disponible) =>
        new(double.IsInfinity(disponible.Width) ? 600 : disponible.Width, double.IsInfinity(disponible.Height) ? 240 : disponible.Height);

    protected override void OnRender(DrawingContext dc)
    {
        FondoSensible(dc);
        var d = Datos;
        var a = Area;
        if (d is null || d.Fechas.Count < 2 || a.Ancho < 20 || a.Alto < 20)
        {
            if (d is not null) Escribir(dc, "Sin datos para el periodo", ActualWidth / 2, ActualHeight / 2, color: ApagadoPincel);
            return;
        }
        var rango = GeometriaTemporal.RangoValores(d.Series.SelectMany(s => s.Valores), d.IncluirCero, Math.Max(3, (int)(a.Alto / 40)));
        var esc = new EscalaLineal(rango.Min, rango.Max, a.Abajo, a.Y);
        var fmt = FormatoEje ?? FormatoGrafico.Compacto;

        // Rejilla y eje vertical.
        foreach (var v in rango.Divisiones)
        {
            var y = Math.Round(esc.Mapear(v)) + 0.5;
            var lapiz = d.LineaCero && Math.Abs(v) < 1e-12 ? Congelar(new Pen(Pincel(PaletaGraficos.Apagado), 1)) : LapizRejilla;
            dc.DrawLine(lapiz, new Point(a.X, y), new Point(a.Derecha, y));
            Escribir(dc, fmt(v), a.X - 6, y + 4, alineacion: 1);
        }
        // Eje de fechas.
        foreach (var (f, texto) in GeometriaTemporal.MarcasFechas(d.Fechas[0], d.Fechas[^1], Math.Max(2, (int)(a.Ancho / 70))))
        {
            var x = Math.Round(GeometriaTemporal.X(a, d.Fechas[0], d.Fechas[^1], f)) + 0.5;
            dc.DrawLine(LapizRejilla, new Point(x, a.Abajo), new Point(x, a.Abajo + 4));
            Escribir(dc, texto, x, ActualHeight - 6);
        }

        var xs = d.Fechas.Select(f => GeometriaTemporal.X(a, d.Fechas[0], d.Fechas[^1], f)).ToArray();

        // Sombreado entre dos series.
        if (d.AreaEntre is { } ae && ae.A < d.Series.Count && ae.B < d.Series.Count)
        {
            var (pos, neg) = GeometriaTemporal.Relleno(xs, d.Series[ae.A].Valores, d.Series[ae.B].Valores, v => esc.Mapear(v));
            DibujarPoligonos(dc, pos, Pincel("#331E8E5A"));
            DibujarPoligonos(dc, neg, Pincel("#33C2412D"));
        }

        // Series (la resaltada, encima).
        var orden = Enumerable.Range(0, d.Series.Count).OrderBy(i => i == SerieResaltada ? 1 : 0).ToList();
        foreach (var i in orden)
        {
            var s = d.Series[i];
            var atenuada = SerieResaltada >= 0 && i != SerieResaltada;
            var color = Pincel(s.Color);
            var lapiz = new Pen(atenuada ? new SolidColorBrush(Color.FromArgb(70, color.Color.R, color.Color.G, color.Color.B)) : color,
                SerieResaltada == i ? s.Grosor + 1 : s.Grosor) { LineJoin = PenLineJoin.Round };
            if (s.Discontinua) lapiz.DashStyle = new DashStyle([4, 3], 0);
            lapiz.Freeze();
            foreach (var tramo in GeometriaGrafico.Tramos(s.Valores))
            {
                if (tramo.Count == 1) { dc.DrawEllipse(lapiz.Brush, null, new Point(xs[tramo[0]], esc.Mapear(s.Valores[tramo[0]]!.Value)), 1.5, 1.5); continue; }
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(xs[tramo[0]], esc.Mapear(s.Valores[tramo[0]]!.Value)), false, false);
                    ctx.PolyLineTo(tramo.Skip(1).Select(k => new Point(xs[k], esc.Mapear(s.Valores[k]!.Value))).ToList(), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, lapiz, g);
            }
        }

        // Compras ▲ y ventas ▼ bajo el eje.
        if (d.Marcas is { Count: > 0 } marcas)
            foreach (var m in marcas.Where(m => m.Indice >= 0 && m.Indice < xs.Length))
                dc.DrawGeometry(Pincel(m.Compra ? Verde : "#B7791F"), null, Triangulo(xs[m.Indice], a.Abajo + 2, m.Compra));

        // Día bajo el ratón: línea vertical y un punto en cada serie.
        if (Resaltado >= 0 && Resaltado < xs.Length)
        {
            var x = Math.Round(xs[Resaltado]) + 0.5;
            dc.DrawLine(Congelar(new Pen(Pincel(PaletaGraficos.Apagado), 1) { DashStyle = new DashStyle([2, 3], 0) }), new Point(x, a.Y), new Point(x, a.Abajo));
            for (var i = 0; i < d.Series.Count; i++)
                if (Resaltado < d.Series[i].Valores.Count && d.Series[i].Valores[Resaltado] is double v && (SerieResaltada < 0 || SerieResaltada == i))
                    dc.DrawEllipse(Pincel(d.Series[i].Color), new Pen(Brushes.White, 1.5), new Point(xs[Resaltado], esc.Mapear(v)), 3.5, 3.5);
        }
    }

    private static void DibujarPoligonos(DrawingContext dc, IReadOnlyList<IReadOnlyList<Punto>> poligonos, Brush relleno)
    {
        if (poligonos.Count == 0) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
            foreach (var p in poligonos.Where(p => p.Count >= 3))
            {
                ctx.BeginFigure(new Point(p[0].X, p[0].Y), true, true);
                ctx.PolyLineTo(p.Skip(1).Select(q => new Point(q.X, q.Y)).ToList(), false, false);
            }
        g.Freeze();
        dc.DrawGeometry(relleno, null, g);
    }

    private static Geometry Triangulo(double x, double y, bool arriba)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            if (arriba)
            {
                ctx.BeginFigure(new Point(x, y), true, true);
                ctx.LineTo(new Point(x + 4.5, y + 7), false, false);
                ctx.LineTo(new Point(x - 4.5, y + 7), false, false);
            }
            else
            {
                ctx.BeginFigure(new Point(x - 4.5, y), true, true);
                ctx.LineTo(new Point(x + 4.5, y), false, false);
                ctx.LineTo(new Point(x, y + 7), false, false);
            }
        }
        g.Freeze();
        return g;
    }

    protected override int IndiceEn(Point posicion)
    {
        var d = Datos;
        var a = Area;
        if (d is null || d.Fechas.Count < 2 || posicion.Y < a.Y - 4 || posicion.Y > a.Abajo + 12) return -1;
        return GeometriaTemporal.IndiceMasCercano(a, d.Fechas, posicion.X);
    }

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
