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
}
