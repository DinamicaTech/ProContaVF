using ScottPlot;

namespace ProConta.UI.Base.Graficos;

/// <summary>Rótulos fijos de un eje: los que da el gráfico (divisiones «redondas» del eje de valores o nombres de las categorías).</summary>
public sealed class TicksFijos(IEnumerable<Tick> ticks) : ITickGenerator
{
    public Tick[] Ticks { get; set; } = ticks.ToArray();

    public int MaxTickCount { get; set; } = 1000;

    public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle) { }

    /// <summary>Rótulos del eje de valores con el formato del gráfico.</summary>
    public static TicksFijos Valores(RangoEje rango, Func<double, string> formato) =>
        new(rango.Divisiones.Select(v => Tick.Major(v, formato(v))));
}

/// <summary>
/// Rótulos del eje de fechas (en días OLE, <see cref="DateTime.ToOADate"/>) que se recalculan con el zoom: años, meses o días según el periodo que se
/// ve, como <see cref="GeometriaTemporal.MarcasFechas"/>, uno cada 70 píxeles como mucho.
/// </summary>
public sealed class TicksFechas : ITickGenerator
{
    public Tick[] Ticks { get; private set; } = [];

    public int MaxTickCount { get; set; } = 1000;

    public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
    {
        if (!(range.Span > 0) || double.IsNaN(range.Min) || range.Min < -600000 || range.Max > 2900000)
        {
            Ticks = [];
            return;
        }
        var desde = DateTime.FromOADate(range.Min);
        var hasta = DateTime.FromOADate(range.Max);
        Ticks = GeometriaTemporal.MarcasFechas(desde, hasta, Math.Max(2, (int)(size.Length / 70)))
            .Select(m => Tick.Major(m.Fecha.ToOADate(), m.Texto)).ToArray();
    }
}
