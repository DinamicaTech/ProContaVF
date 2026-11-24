using ScottPlot;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Dibujo de los gráficos pequeños sobre ScottPlot (informes/graficos): minigráfico, barra apilada, barra con referencia y franja de rango. No
/// tienen ejes: el área de datos ocupa todo el control y sus coordenadas son píxeles lógicos (x de 0 al ancho, y de 0 abajo al alto arriba).
/// Solo usa el núcleo de ScottPlot, así que se puede dibujar a PNG en las pruebas.
/// </summary>
public static class PlanoSimples
{
    private static void Lienzo(Plot plot, double ancho, double alto)
    {
        EstiloPlano.Preparar(plot, new PixelPadding(0), ejes: false);
        plot.Axes.SetLimits(0, Math.Max(1, ancho), 0, Math.Max(1, alto));
    }

    private static void Caja(Plot plot, double x, double y, double ancho, double alto, string color, string? borde = null)
    {
        if (ancho <= 0 || alto <= 0) return;
        var r = plot.Add.Rectangle(x, x + ancho, y, y + alto);
        r.FillStyle.Color = EstiloPlano.De(color);
        r.LineStyle.Width = borde is null ? 0 : 1;
        if (borde is not null) r.LineStyle.Color = EstiloPlano.De(borde);
    }

    /// <summary>Minigráfico: la evolución en una línea fina sin ejes, cortada en los huecos, con un punto en el último valor.</summary>
    public static void Minigrafico(Plot plot, IReadOnlyList<decimal?>? valores, string color, double ancho, double alto)
    {
        Lienzo(plot, ancho, alto);
        if (valores is not { Count: > 0 } || ancho < 10) return;
        var puntos = MinigraficoGeometria.Puntos(valores, ancho, alto);
        // Las y de la geometría crecen hacia abajo (como en pantalla); aquí crecen hacia arriba.
        double Y(Punto p) => alto - p.Y;
        foreach (var tramo in GeometriaGrafico.Tramos(valores))
        {
            if (tramo.Count == 1)
            {
                var p = puntos[tramo[0]]!.Value;
                plot.Add.Marker(p.X, Y(p), MarkerShape.FilledCircle, 3, EstiloPlano.De(color));
                continue;
            }
            EstiloPlano.Linea(plot, tramo.Select(i => puntos[i]!.Value.X).ToArray(), tramo.Select(i => Y(puntos[i]!.Value)).ToArray(), color, 2);
        }
        if (puntos.LastOrDefault(p => p is not null) is { } u) EstiloPlano.Punto(plot, u.X, Y(u), color, 6);
    }

    /// <summary>Barra apilada horizontal: un segmento por tramo, proporcional a su importe; el <paramref name="resaltado"/> lleva borde oscuro.</summary>
    public static void Apilada(Plot plot, IReadOnlyList<TramoApilado>? tramos, double ancho, double alto, int resaltado = -1)
    {
        Lienzo(plot, ancho, alto);
        if (tramos is not { Count: > 0 }) return;
        var seg = GeometriaGrafico.Apilada(tramos.Select(t => t.Valor).ToList(), ancho);
        for (var i = 0; i < tramos.Count; i++)
            Caja(plot, seg[i].X, 0, seg[i].Ancho, alto, tramos[i].Color, i == resaltado ? EstiloPlano.Tinta : null);
    }

    /// <summary>Barra horizontal con referencia: pista gris, barra del valor y una raya oscura en la referencia (sobre el mismo máximo).</summary>
    public static void Referencia(Plot plot, decimal valor, decimal? referencia, decimal maximo, string color, double ancho, double alto)
    {
        Lienzo(plot, ancho, alto);
        Caja(plot, 0, 0, ancho, alto, EstiloPlano.Pista);
        Caja(plot, 0, 0, ancho * GeometriaGrafico.Proporcion(valor, maximo), alto, color);
        if (referencia is decimal r && r > 0 && maximo > 0)
        {
            var x = Math.Clamp(ancho * GeometriaGrafico.Proporcion(r, maximo), 1, ancho - 1);
            Caja(plot, x - 1, 0, 2, alto, "#8C16213A");
        }
    }

    /// <summary>Franja de rango de un ratio: pista, banda verde del rango de referencia, raya gris del comparado y marca oscura del valor.</summary>
    public static void Franja(Plot plot, decimal? valor, decimal? referencia, decimal? minimo, decimal? maximo, bool menosEsMejor, double ancho, double alto)
    {
        Lienzo(plot, ancho, alto);
        if (ancho < 10) return;
        var pista = Math.Min(8, alto);
        var y = (alto - pista) / 2;
        Caja(plot, 0, y, ancho, pista, EstiloPlano.Pista);
        var (desde, hasta, v, r) = GeometriaGrafico.Franja(minimo, maximo, menosEsMejor, valor, referencia);
        if (hasta > desde) Caja(plot, desde * ancho, y, (hasta - desde) * ancho, pista, "#BFE0CC");
        if (r is double rr) Caja(plot, Math.Clamp(rr * ancho, 1, ancho - 1) - 1, 1, 2, alto - 2, "#9AA0AB");
        if (v is double vv) Caja(plot, Math.Clamp(vv * ancho, 2, ancho - 2) - 1.5, 0, 3, alto, EstiloPlano.Tinta);
    }

    /// <summary>Tramo de la barra apilada bajo la coordenada <paramref name="x"/> (píxeles); −1 si ninguno.</summary>
    public static int TramoEn(IReadOnlyList<TramoApilado>? tramos, double ancho, double x)
    {
        if (tramos is not { Count: > 0 }) return -1;
        var seg = GeometriaGrafico.Apilada(tramos.Select(t => t.Valor).ToList(), ancho);
        for (var i = 0; i < seg.Count; i++)
            if (seg[i].Ancho > 0 && x >= seg[i].X && x < seg[i].X + seg[i].Ancho) return i;
        return -1;
    }
}
