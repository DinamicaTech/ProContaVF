using ProConta.UI.Base.Graficos;

namespace ProConta.UI.Base.Tests;

/// <summary>Geometría del gráfico temporal (informes/graficos, para activos/activos-financieros): eje de fechas, eje de valores y sombreado.</summary>
public class GraficoTemporalTests
{
    private static readonly Caja Area = new(0, 0, 100, 50);

    [Fact]
    public void Eje_de_fechas_proporcional_al_tiempo()
    {
        var d = new DateTime(2026, 1, 1);
        Assert.Equal(0, GeometriaTemporal.X(Area, d, d.AddDays(10), d));
        Assert.Equal(50, GeometriaTemporal.X(Area, d, d.AddDays(10), d.AddDays(5)));
        Assert.Equal(100, GeometriaTemporal.X(Area, d, d.AddDays(10), d.AddDays(10)));
    }

    [Fact]
    public void Indice_mas_cercano_al_raton()
    {
        var d = new DateTime(2026, 1, 1);
        DateTime[] f = [d, d.AddDays(1), d.AddDays(4), d.AddDays(10)];
        Assert.Equal(0, GeometriaTemporal.IndiceMasCercano(Area, f, 2));
        Assert.Equal(2, GeometriaTemporal.IndiceMasCercano(Area, f, 45));
        Assert.Equal(3, GeometriaTemporal.IndiceMasCercano(Area, f, 90));
        Assert.Equal(-1, GeometriaTemporal.IndiceMasCercano(Area, f, 140));
    }

    [Fact]
    public void Rango_de_precios_ajustado_y_de_importes_desde_cero()
    {
        var r = GeometriaTemporal.RangoValores([101.3, 118.7], incluirCero: false, divisiones: 4);
        Assert.True(r.Min <= 101.3 && r.Min >= 90);
        Assert.True(r.Max >= 118.7 && r.Max <= 125);
        Assert.Equal(0, GeometriaTemporal.RangoValores([100, 200], incluirCero: true).Min);
        var p = GeometriaTemporal.RangoValores([-0.031, 0.12], incluirCero: true);
        Assert.Contains(0.0, p.Divisiones);
    }

    [Fact]
    public void Rotulos_de_fechas_por_anio_mes_o_dia()
    {
        var anios = GeometriaTemporal.MarcasFechas(new(2020, 3, 1), new(2026, 11, 23), 8);
        Assert.Equal("2021", anios[0].Texto);
        Assert.True(anios.Count <= 8);
        var meses = GeometriaTemporal.MarcasFechas(new(2026, 5, 23), new(2026, 11, 23), 8);
        Assert.Equal(new DateTime(2026, 6, 1), meses[0].Fecha);
        Assert.Equal(6, meses.Count);
        var dias = GeometriaTemporal.MarcasFechas(new(2026, 10, 23), new(2026, 11, 23), 6);
        Assert.True(dias.Count is > 2 and <= 6);
    }

    [Fact]
    public void Sombreado_se_corta_donde_se_cruzan_las_series()
    {
        double[] xs = [0, 10, 20];
        var (pos, neg) = GeometriaTemporal.Relleno(xs, [2, 0, 1], [1, 1, 1], v => v);
        Assert.Single(pos);
        Assert.Single(neg);
        Assert.Contains(new Punto(5, 1), pos[0]);       // cruce a mitad del primer tramo
        Assert.Contains(new Punto(5, 1), neg[0]);
    }

    [Fact]
    public void Sombreado_con_hueco_se_interrumpe()
    {
        var (pos, _) = GeometriaTemporal.Relleno([0, 1, 2, 3], [2, 2, null, 2], [1, 1, 1, 1], v => v);
        Assert.Single(pos);   // el tramo final de un solo punto no tiene área
    }

    [Fact]
    public void Tramos_sutiles_comparten_el_punto_del_cambio()
    {
        double?[] v = [1, 2, 3, 4, null, 6];
        bool[] sutil = [true, true, false, false, false, false];
        var t = GeometriaTemporal.TramosConEstilo(v, sutil);
        Assert.Equal(3, t.Count);
        Assert.Equal([0, 1], t[0].Indices);
        Assert.True(t[0].Sutil);
        Assert.Equal([1, 2, 3], t[1].Indices);   // empieza en el último punto sutil: la línea no se corta
        Assert.False(t[1].Sutil);
        Assert.Equal([5], t[2].Indices);          // el hueco corta
    }

    [Fact]
    public void Sin_marca_de_sutil_hay_un_tramo_por_hueco()
    {
        var t = GeometriaTemporal.TramosConEstilo([1, 2, null, 4, 5], null);
        Assert.Equal(2, t.Count);
        Assert.All(t, x => Assert.False(x.Sutil));
    }

    [Fact]
    public void Indice_mas_cercano_en_dias()
    {
        double[] xs = [10, 11, 14, 20];
        Assert.Equal(0, GeometriaTemporal.IndiceMasCercano(xs, 5));
        Assert.Equal(1, GeometriaTemporal.IndiceMasCercano(xs, 12.4));
        Assert.Equal(2, GeometriaTemporal.IndiceMasCercano(xs, 12.6));
        Assert.Equal(3, GeometriaTemporal.IndiceMasCercano(xs, 99));
        Assert.Equal(-1, GeometriaTemporal.IndiceMasCercano([], 1));
    }

    [Fact]
    public void El_zoom_no_sale_del_periodo_ni_baja_del_minimo()
    {
        Assert.Equal((0, 100), ZoomHorizontal.Recortar(-50, 150, 0, 100, 7));      // alejar de más: el periodo completo
        Assert.Equal((90, 100), ZoomHorizontal.Recortar(95, 105, 0, 100, 7));      // se desplaza para no salir
        Assert.Equal((46.5, 53.5), ZoomHorizontal.Recortar(49, 51, 0, 100, 7));   // nunca menos de 7 días
        Assert.Equal((20, 40), ZoomHorizontal.Recortar(40, 20, 0, 100, 7));       // selección de derecha a izquierda
    }

    [Fact]
    public void Rotulos_de_fechas_segun_el_zoom()
    {
        var t = new TicksFechas();
        var desde = new DateTime(2026, 1, 1).ToOADate();
        t.Regenerate(new ScottPlot.CoordinateRange(desde, desde + 365 * 10), ScottPlot.Edge.Bottom, new ScottPlot.PixelLength(700), ScottPlot.Paint.NewDisposablePaint(), new ScottPlot.LabelStyle());
        Assert.Contains(t.Ticks, k => k.Label == "2027");
        t.Regenerate(new ScottPlot.CoordinateRange(desde, desde + 20), ScottPlot.Edge.Bottom, new ScottPlot.PixelLength(700), ScottPlot.Paint.NewDisposablePaint(), new ScottPlot.LabelStyle());
        Assert.All(t.Ticks, k => Assert.Matches(@"^\d{1,2} ene$", k.Label));
    }

    private static DatosTemporales Datos(int dias, bool marcas)
    {
        var d0 = new DateTime(2026, 1, 1);
        var fechas = Enumerable.Range(0, dias).Select(i => d0.AddDays(i)).ToList();
        var valor = fechas.Select((_, i) => (double?)(100 + i)).ToList();
        var coste = fechas.Select(_ => (double?)110).ToList();
        return new DatosTemporales(fechas, [new SerieTemporal("Valor", "#2a78d6", valor), new SerieTemporal("Coste", "#7A8090", coste, Escalon: true)],
            AreaEntre: (0, 1), Marcas: marcas ? [new MarcaTemporal(3, true)] : null);
    }

    [Fact]
    public void El_eje_vertical_se_ajusta_a_lo_que_se_ve()
    {
        var plano = new PlanoTemporal(new ScottPlot.Plot());
        plano.Dibujar(Datos(100, false), -1, null, 300);
        var completo = plano.Plot.Axes.GetLimits();
        Assert.True(completo.Left < new DateTime(2026, 1, 1).ToOADate(), "holgura a la izquierda para la primera compra");
        Assert.True(completo.Top >= 199);
        // Zoom a los 20 primeros días: valores de 100 a 120 (y el coste de 110).
        plano.Dibujar(Datos(100, false), -1, null, 300, plano.Xs[0], plano.Xs[19]);
        var zoom = plano.Plot.Axes.GetLimits();
        Assert.True(zoom.Top < 150, $"tope {zoom.Top}");
        Assert.True(zoom.Bottom >= 90, $"base {zoom.Bottom}");
    }

    [Fact]
    public void Con_compras_deja_sitio_al_pie()
    {
        var plano = new PlanoTemporal(new ScottPlot.Plot());
        plano.Dibujar(Datos(50, true), -1, null, 300);
        var lim = plano.Plot.Axes.GetLimits();
        Assert.True(lim.Bottom < 100, "la compra va por debajo del valor más bajo");
        Assert.Equal(3, plano.IndiceEn(plano.Xs[3] + 0.2));
        plano.Resaltar(3, -1);
        plano.Plot.GetImage(400, 300);   // se dibuja sin errores
    }
}
