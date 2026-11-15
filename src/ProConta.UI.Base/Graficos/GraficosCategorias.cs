using System.Windows;
using System.Windows.Media;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Gráfico con eje de valores y eje de categorías (meses): rejilla horizontal, rótulos del eje y tooltip por categoría.
/// Base de <see cref="GraficoBarras"/> y <see cref="GraficoLineas"/>.
/// </summary>
public abstract class GraficoCategorias : GraficoBase
{
    public static readonly DependencyProperty DatosProperty = DependencyProperty.Register(nameof(Datos), typeof(DatosGrafico), typeof(GraficoCategorias),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((GraficoCategorias)d).AlCambiarDatos()));

    /// <summary>Series y categorías que se dibujan.</summary>
    public DatosGrafico? Datos { get => (DatosGrafico?)GetValue(DatosProperty); set => SetValue(DatosProperty, value); }

    /// <summary>Formato de los valores en el tooltip; por defecto en miles de euros con un decimal.</summary>
    public Func<decimal, string> FormatoValor { get; set; } = FormatoGrafico.Miles;

    /// <summary>Formato de los rótulos del eje de valores; por defecto compacto («120k»).</summary>
    public Func<double, string> FormatoEje { get; set; } = FormatoGrafico.Compacto;

    protected Margenes Margen => new(48, Datos?.Marca is null ? 12 : 22, 10, 24);

    protected Caja AreaTrazado => GeometriaGrafico.Area(ActualWidth, ActualHeight, Margen);

    protected int NumeroCategorias => Datos?.Categorias.Count ?? 0;

    private void AlCambiarDatos()
    {
        if (Datos?.Descripcion is { } d) System.Windows.Automation.AutomationProperties.SetName(this, d);
    }

    protected abstract bool ApilaProyeccion { get; }

    protected EscalaLineal Escala(out RangoEje rango)
    {
        var a = AreaTrazado;
        rango = EscalasGrafico.Rango(Datos?.ValoresEje(ApilaProyeccion) ?? [], minFijo: Datos?.Minimo, maxFijo: Datos?.Maximo);
        return new EscalaLineal(rango.Min, rango.Max, a.Abajo, a.Y);
    }

    /// <summary>Rejilla, rótulos del eje de valores y nombres de las categorías (si no caben todos, uno de cada dos).</summary>
    protected void DibujarEjes(DrawingContext dc, EscalaLineal escala, RangoEje rango)
    {
        var a = AreaTrazado;
        foreach (var v in rango.Divisiones)
        {
            var y = Math.Round(escala.Mapear(v)) + 0.5;
            dc.DrawLine(LapizRejilla, new Point(a.X, y), new Point(a.Derecha, y));
            Escribir(dc, FormatoEje(v), a.X - 6, y + 4, alineacion: 1);
        }
        var n = NumeroCategorias;
        if (n == 0) return;
        var paso = a.Ancho / n < 26 ? 2 : 1;
        for (var i = 0; i < n; i += paso)
            Escribir(dc, Datos!.Categorias[i], GeometriaGrafico.CentroCategoria(a, n, i), ActualHeight - 8);
    }

    /// <summary>Marca vertical a trazos sobre una categoría (por ejemplo «hoy»), con su rótulo encima.</summary>
    protected void DibujarMarca(DrawingContext dc)
    {
        if (Datos?.Marca is not int m || m < 0 || m >= NumeroCategorias) return;
        var a = AreaTrazado;
        var x = Math.Round(GeometriaGrafico.CentroCategoria(a, NumeroCategorias, m)) + 0.5;
        var lapiz = new Pen(Pincel(PaletaGraficos.Apagado), 1) { DashStyle = new DashStyle([2, 3], 0) };
        dc.DrawLine(lapiz, new Point(x, a.Y - 4), new Point(x, a.Abajo));
        if (Datos.TextoMarca is { } t) Escribir(dc, t, x, a.Y - 7);
    }

    protected override int IndiceEn(Point posicion) =>
        posicion.Y < AreaTrazado.Y - 6 || posicion.Y > AreaTrazado.Abajo ? -1 : GeometriaGrafico.CategoriaEn(AreaTrazado, NumeroCategorias, posicion.X);

    protected override ContenidoTooltip? TooltipDe(int indice) => Datos?.TooltipDe(indice, FormatoValor);

    protected override Size MeasureOverride(Size disponible) =>
        new(double.IsInfinity(disponible.Width) ? 400 : disponible.Width, double.IsInfinity(disponible.Height) ? 220 : disponible.Height);
}

/// <summary>
/// Barras agrupadas por categoría (informes/graficos): una barra por serie en cada mes; la proyección de una serie se apila
/// encima de su valor real con un rayado del mismo color y borde. Un valor nulo no dibuja barra (hueco). Rotula el valor más
/// alto de la primera serie.
/// </summary>
public sealed class GraficoBarras : GraficoCategorias
{
    protected override bool ApilaProyeccion => true;

    protected override void OnRender(DrawingContext dc)
    {
        FondoSensible(dc);
        if (Datos is not { } datos || NumeroCategorias == 0 || ActualWidth < 60) return;
        var escala = Escala(out var rango);
        var a = AreaTrazado;
        if (Resaltado >= 0)
        {
            var b = GeometriaGrafico.Banda(a, NumeroCategorias, Resaltado);
            dc.DrawRectangle(Pincel("#F3F0E9"), null, new Rect(b.X, b.Y, b.Ancho, b.Alto));
        }
        DibujarEjes(dc, escala, rango);
        var series = datos.Series;
        (int Cat, Caja Caja, decimal Valor)? destacado = null;
        for (var i = 0; i < NumeroCategorias; i++)
        {
            var barras = GeometriaGrafico.BarrasDelGrupo(GeometriaGrafico.Banda(a, NumeroCategorias, i), series.Count);
            for (var s = 0; s < series.Count; s++)
            {
                var serie = series[s];
                var v = i < serie.Valores.Count ? serie.Valores[i] : null;
                var p = serie.Proyeccion is { } pr && i < pr.Count ? pr[i] : null;
                var (x, ancho) = barras[s];
                if (v is decimal real && real != 0)
                {
                    var c = GeometriaGrafico.Barra(escala, x, ancho, 0, (double)real);
                    dc.DrawGeometry(Pincel(serie.Color), null, BarraRedondeada(c, p is null ? 4 : 0, real >= 0));
                    if (s == 0 && real > 0 && (destacado is null || real > destacado.Value.Valor)) destacado = (i, c, real);
                }
                if (p is decimal proy && proy > 0)
                {
                    var baseReal = (double)Math.Max(0, v ?? 0);
                    var c = GeometriaGrafico.Barra(escala, x, ancho, baseReal, baseReal + (double)proy);
                    var hueco = baseReal > 0 ? 2 : 0;
                    c = c with { Alto = Math.Max(0, c.Alto - hueco) };
                    dc.DrawGeometry(Rayado(serie.Color), new Pen(Pincel(serie.Color), 1), BarraRedondeada(c, 4));
                }
            }
        }
        if (destacado is { } d)
            Escribir(dc, FormatoEje((double)d.Valor), d.Caja.X + d.Caja.Ancho / 2, d.Caja.Y - 5, tamano: 11, color: Tinta, negrita: true);
        // Línea del cero cuando hay negativos.
        if (rango.Min < 0)
        {
            var y0 = Math.Round(escala.Mapear(0)) + 0.5;
            dc.DrawLine(new Pen(Pincel("#C9C4B8"), 1), new Point(a.X, y0), new Point(a.Derecha, y0));
        }
    }
}

/// <summary>
/// Líneas por categoría (informes/graficos): cada serie es una línea con un punto al final; un nulo corta la línea (no se
/// interpola el hueco). Una serie discontinua se dibuja a trazos con el último punto hueco (proyección o previsión) y una serie
/// con área rellena suavemente hasta la base. Opcionalmente, una marca vertical («hoy»).
/// </summary>
public sealed class GraficoLineas : GraficoCategorias
{
    protected override bool ApilaProyeccion => false;

    protected override void OnRender(DrawingContext dc)
    {
        FondoSensible(dc);
        if (Datos is not { } datos || NumeroCategorias == 0 || ActualWidth < 60) return;
        var escala = Escala(out var rango);
        var a = AreaTrazado;
        DibujarEjes(dc, escala, rango);
        DibujarMarca(dc);
        var n = NumeroCategorias;
        double X(int i) => GeometriaGrafico.CentroCategoria(a, n, i);
        var baseY = escala.Mapear(Math.Max(rango.Min, 0));
        foreach (var serie in datos.Series)
        {
            var color = Pincel(serie.Color);
            var lapiz = new Pen(color, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (serie.Discontinua) lapiz.DashStyle = new DashStyle([2.5, 2], 0);
            foreach (var tramo in GeometriaGrafico.Tramos(serie.Valores))
            {
                var puntos = tramo.Select(i => new Point(X(i), escala.Mapear((double)serie.Valores[i]!.Value))).ToList();
                if (serie.Area && puntos.Count > 1)
                {
                    var area = new StreamGeometry();
                    using (var ctx = area.Open())
                    {
                        ctx.BeginFigure(new Point(puntos[0].X, baseY), true, true);
                        foreach (var p in puntos) ctx.LineTo(p, false, false);
                        ctx.LineTo(new Point(puntos[^1].X, baseY), false, false);
                    }
                    var relleno = new SolidColorBrush(color.Color) { Opacity = 0.10 };
                    dc.DrawGeometry(relleno, null, area);
                }
                if (puntos.Count > 1)
                {
                    var linea = new StreamGeometry();
                    using (var ctx = linea.Open())
                    {
                        ctx.BeginFigure(puntos[0], false, false);
                        foreach (var p in puntos.Skip(1)) ctx.LineTo(p, true, true);
                    }
                    dc.DrawGeometry(null, lapiz, linea);
                }
                else dc.DrawEllipse(color, null, puntos[0], 2.5, 2.5);
            }
            // Punto final de la serie: lleno, o hueco si es discontinua.
            var ultimo = Enumerable.Range(0, serie.Valores.Count).LastOrDefault(i => serie.Valores[i] is not null, -1);
            if (ultimo >= 0)
            {
                var p = new Point(X(ultimo), escala.Mapear((double)serie.Valores[ultimo]!.Value));
                if (serie.Discontinua) dc.DrawEllipse(Brushes.White, new Pen(color, 2), p, 3.5, 3.5);
                else dc.DrawEllipse(color, new Pen(Brushes.White, 2), p, 4, 4);
                if (serie.Rotulo is { } rotulo) Escribir(dc, rotulo, p.X - 6, p.Y - 9, alineacion: 1, color: Tinta, negrita: true);
            }
        }
        if (Resaltado >= 0)
        {
            var x = Math.Round(X(Resaltado)) + 0.5;
            dc.DrawLine(new Pen(Pincel(PaletaGraficos.Apagado), 1) { DashStyle = DashStyles.Solid }, new Point(x, a.Y), new Point(x, a.Abajo));
            foreach (var serie in datos.Series)
                if (Resaltado < serie.Valores.Count && serie.Valores[Resaltado] is decimal v)
                    dc.DrawEllipse(Pincel(serie.Color), new Pen(Brushes.White, 2), new Point(X(Resaltado), escala.Mapear((double)v)), 4.5, 4.5);
        }
    }
}
