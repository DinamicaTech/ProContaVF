using System.Windows;
using System.Windows.Media;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Minigráfico (informes/graficos): la evolución de un indicador en una línea fina, sin ejes, con un punto en el último valor.
/// Los nulos cortan la línea. Su tooltip es el del control que lo contiene.
/// </summary>
public sealed class Minigrafico : FrameworkElement
{
    public static readonly DependencyProperty ValoresProperty = DependencyProperty.Register(nameof(Valores), typeof(IReadOnlyList<decimal?>), typeof(Minigrafico),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(string), typeof(Minigrafico),
        new FrameworkPropertyMetadata("#1D5C8C", FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<decimal?>? Valores { get => (IReadOnlyList<decimal?>?)GetValue(ValoresProperty); set => SetValue(ValoresProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    protected override Size MeasureOverride(Size disponible) => new(double.IsInfinity(disponible.Width) ? 120 : disponible.Width, 30);

    protected override void OnRender(DrawingContext dc)
    {
        if (Valores is not { Count: > 0 } valores || ActualWidth < 10) return;
        var puntos = MinigraficoGeometria.Puntos(valores, ActualWidth, ActualHeight);
        var color = GraficoBase.Pincel(Color);
        var lapiz = new Pen(color, 2) { LineJoin = PenLineJoin.Round };
        foreach (var tramo in GeometriaGrafico.Tramos(valores))
        {
            if (tramo.Count == 1)
            {
                var p = puntos[tramo[0]]!.Value;
                dc.DrawEllipse(color, null, new Point(p.X, p.Y), 1.5, 1.5);
                continue;
            }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var p0 = puntos[tramo[0]]!.Value;
                ctx.BeginFigure(new Point(p0.X, p0.Y), false, false);
                foreach (var i in tramo.Skip(1)) ctx.LineTo(new Point(puntos[i]!.Value.X, puntos[i]!.Value.Y), true, true);
            }
            dc.DrawGeometry(null, lapiz, g);
        }
        var ultimo = puntos.LastOrDefault(p => p is not null);
        if (ultimo is { } u) dc.DrawEllipse(color, new Pen(Brushes.White, 1.5), new Point(u.X, u.Y), 3, 3);
    }
}

/// <summary>Geometría pura del minigráfico (para probarla sin WPF).</summary>
public static class MinigraficoGeometria
{
    /// <summary>Un punto por valor (null en los huecos), repartidos a lo ancho; el mínimo abajo y el máximo arriba, con 3 px de margen.</summary>
    public static IReadOnlyList<Punto?> Puntos(IReadOnlyList<decimal?> valores, double ancho, double alto)
    {
        var con = valores.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (con.Count == 0) return valores.Select(_ => (Punto?)null).ToList();
        var min = con.Min();
        var max = con.Max();
        var n = valores.Count;
        return valores.Select((v, i) => v is not decimal d ? (Punto?)null : new Punto(
            n == 1 ? ancho / 2 : 3 + i * (ancho - 6) / (n - 1),
            alto - 3 - (max == min ? 0.5 : (double)((d - min) / (max - min))) * (alto - 8))).ToList();
    }
}

/// <summary>
/// Barra apilada horizontal (informes/graficos), por ejemplo la antigüedad de lo pendiente de cobro: un segmento por tramo,
/// proporcional a su importe, con su color; al pasar el ratón, el tooltip del tramo con su importe y su peso.
/// </summary>
public sealed class BarraApilada : GraficoBase
{
    public static readonly DependencyProperty TramosProperty = DependencyProperty.Register(nameof(Tramos), typeof(IReadOnlyList<TramoApilado>), typeof(BarraApilada),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<TramoApilado>? Tramos { get => (IReadOnlyList<TramoApilado>?)GetValue(TramosProperty); set => SetValue(TramosProperty, value); }

    public Func<decimal, string> FormatoValor { get; set; } = v => Formato.Formatos.Importe(Math.Round(v, 2));

    private IReadOnlyList<(double X, double Ancho)> Segmentos => GeometriaGrafico.Apilada(Tramos?.Select(t => t.Valor).ToList() ?? [], ActualWidth);

    protected override Size MeasureOverride(Size disponible) => new(double.IsInfinity(disponible.Width) ? 300 : disponible.Width, 22);

    protected override void OnRender(DrawingContext dc)
    {
        FondoSensible(dc);
        if (Tramos is not { Count: > 0 } tramos) return;
        var seg = Segmentos;
        for (var i = 0; i < tramos.Count; i++)
        {
            if (seg[i].Ancho <= 0) continue;
            var r = new Rect(seg[i].X, 0, seg[i].Ancho, ActualHeight);
            dc.DrawRoundedRectangle(Pincel(tramos[i].Color), i == Resaltado ? new Pen(Tinta, 1) : null, r, 3, 3);
        }
    }

    protected override int IndiceEn(Point posicion)
    {
        var seg = Segmentos;
        for (var i = 0; i < seg.Count; i++)
            if (seg[i].Ancho > 0 && posicion.X >= seg[i].X && posicion.X < seg[i].X + seg[i].Ancho) return i;
        return -1;
    }

    protected override ContenidoTooltip? TooltipDe(int indice)
    {
        if (Tramos is not { } tramos || indice >= tramos.Count) return null;
        var t = tramos[indice];
        var total = tramos.Where(x => x.Valor > 0).Sum(x => x.Valor);
        var filas = new List<FilaTooltip> { new("Importe", FormatoValor(t.Valor), t.Color) };
        if (total > 0) filas.Add(new FilaTooltip("Peso", (t.Valor / total * 100m).ToString("0.0", Formato.Formatos.Cultura) + " %"));
        if (t.Ayuda is { } a) filas.Add(new FilaTooltip(a, ""));
        return new ContenidoTooltip(t.Etiqueta, filas);
    }
}

/// <summary>
/// Barra horizontal con marca de referencia (informes/graficos): la pista gris, la barra del valor en su color y, si hay
/// referencia (por ejemplo el ejercicio comparado), una raya oscura en su posición. Valor y referencia se miden contra
/// <see cref="Maximo"/>, el mismo para todas las filas de una lista.
/// </summary>
public sealed class BarraReferencia : FrameworkElement
{
    public static readonly DependencyProperty ValorProperty = Registrar(nameof(Valor), typeof(decimal), 0m);
    public static readonly DependencyProperty ReferenciaProperty = Registrar(nameof(Referencia), typeof(decimal?), null);
    public static readonly DependencyProperty MaximoProperty = Registrar(nameof(Maximo), typeof(decimal), 0m);
    public static readonly DependencyProperty ColorProperty = Registrar(nameof(Color), typeof(string), PaletaGraficos.Serie1);

    private static DependencyProperty Registrar(string nombre, Type tipo, object? defecto) =>
        DependencyProperty.Register(nombre, tipo, typeof(BarraReferencia), new FrameworkPropertyMetadata(defecto, FrameworkPropertyMetadataOptions.AffectsRender));

    public decimal Valor { get => (decimal)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public decimal? Referencia { get => (decimal?)GetValue(ReferenciaProperty); set => SetValue(ReferenciaProperty, value); }
    public decimal Maximo { get => (decimal)GetValue(MaximoProperty); set => SetValue(MaximoProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    protected override Size MeasureOverride(Size disponible) => new(double.IsInfinity(disponible.Width) ? 200 : disponible.Width, 8);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle(GraficoBase.Pincel("#F3F0E9"), null, new Rect(0, 0, w, h), h / 2, h / 2);
        var ancho = w * GeometriaGrafico.Proporcion(Valor, Maximo);
        if (ancho > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), h / 2, h / 2));
            dc.DrawRoundedRectangle(GraficoBase.Pincel(Color), null, new Rect(0, 0, ancho, h), Math.Min(4, h / 2), Math.Min(4, h / 2));
            dc.Pop();
        }
        if (Referencia is decimal r && r > 0 && Maximo > 0)
        {
            var x = Math.Clamp(w * GeometriaGrafico.Proporcion(r, Maximo), 1, w - 1);
            dc.DrawRectangle(Marca, null, new Rect(x - 1, -1, 2, h + 2));
        }
    }

    private static readonly Brush Marca = CrearMarca();

    private static Brush CrearMarca()
    {
        var p = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x21, 0x3A)) { Opacity = 0.55 };
        p.Freeze();
        return p;
    }
}

/// <summary>
/// Franja de rango de un ratio (informes/graficos): la pista gris, la banda verde del rango de referencia, una marca oscura en
/// el valor actual y una raya gris en el del ejercicio comparado. Las posiciones las calcula <see cref="GeometriaGrafico.Franja"/>.
/// Su tooltip es el del control que lo contiene.
/// </summary>
public sealed class FranjaRango : FrameworkElement
{
    public static readonly DependencyProperty ValorProperty = Registrar(nameof(Valor), typeof(decimal?), null);
    public static readonly DependencyProperty ReferenciaProperty = Registrar(nameof(Referencia), typeof(decimal?), null);
    public static readonly DependencyProperty MinimoProperty = Registrar(nameof(Minimo), typeof(decimal?), null);
    public static readonly DependencyProperty MaximoProperty = Registrar(nameof(Maximo), typeof(decimal?), null);
    public static readonly DependencyProperty MenosEsMejorProperty = Registrar(nameof(MenosEsMejor), typeof(bool), false);

    private static DependencyProperty Registrar(string nombre, Type tipo, object? defecto) =>
        DependencyProperty.Register(nombre, tipo, typeof(FranjaRango), new FrameworkPropertyMetadata(defecto, FrameworkPropertyMetadataOptions.AffectsRender));

    public decimal? Valor { get => (decimal?)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public decimal? Referencia { get => (decimal?)GetValue(ReferenciaProperty); set => SetValue(ReferenciaProperty, value); }
    public decimal? Minimo { get => (decimal?)GetValue(MinimoProperty); set => SetValue(MinimoProperty, value); }
    public decimal? Maximo { get => (decimal?)GetValue(MaximoProperty); set => SetValue(MaximoProperty, value); }
    public bool MenosEsMejor { get => (bool)GetValue(MenosEsMejorProperty); set => SetValue(MenosEsMejorProperty, value); }

    protected override Size MeasureOverride(Size disponible) => new(double.IsInfinity(disponible.Width) ? 120 : disponible.Width, 14);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10) return;
        var pista = Math.Min(8, h);
        var y = (h - pista) / 2;
        dc.DrawRoundedRectangle(GraficoBase.Pincel("#F3F0E9"), null, new Rect(0, y, w, pista), pista / 2, pista / 2);
        var (desde, hasta, valor, referencia) = GeometriaGrafico.Franja(Minimo, Maximo, MenosEsMejor, Valor, Referencia);
        if (hasta > desde) dc.DrawRoundedRectangle(GraficoBase.Pincel("#BFE0CC"), null, new Rect(desde * w, y, (hasta - desde) * w, pista), 3, 3);
        if (referencia is double r) dc.DrawRectangle(GraficoBase.Pincel("#9AA0AB"), null, new Rect(Math.Clamp(r * w, 1, w - 1) - 1, 1, 2, h - 2));
        if (valor is double v) dc.DrawRoundedRectangle(GraficoBase.Pincel("#16213A"), null, new Rect(Math.Clamp(v * w, 2, w - 2) - 1.5, 0, 3, h), 1, 1);
    }
}
