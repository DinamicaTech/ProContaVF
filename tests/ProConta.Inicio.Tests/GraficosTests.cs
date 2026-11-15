using ProConta.UI.Base.Graficos;

namespace ProConta.Inicio.Tests;

/// <summary>Escalas y geometría del motor de gráficos (informes/graficos), sin WPF.</summary>
public sealed class GraficosTests
{
    [Theory]
    [InlineData(0.7, 1)]
    [InlineData(1.2, 1.5)]
    [InlineData(1.5, 1.5)]
    [InlineData(1.8, 2)]
    [InlineData(2.3, 2.5)]
    [InlineData(3.1, 5)]
    [InlineData(7, 10)]
    [InlineData(149_300, 150_000)]
    [InlineData(156_800, 200_000)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void MaximoRedondo(double valor, double esperado) => Assert.Equal(esperado, EscalasGrafico.MaximoRedondo(valor), 6);

    [Fact]
    public void RangoDeSoloPositivosEmpiezaEnCeroConCuatroDivisiones()
    {
        var r = EscalasGrafico.Rango([10, null, 140, 95]);
        Assert.Equal(0, r.Min);
        Assert.Equal(150, r.Max);
        Assert.Equal([0, 37.5, 75, 112.5, 150], r.Divisiones);
    }

    [Fact]
    public void RangoConNegativosIncluyeElCeroEnUnaDivision()
    {
        var r = EscalasGrafico.Rango([-30, 80, 40]);
        Assert.True(r.Min <= -30 && r.Max >= 80);
        Assert.Contains(0, r.Divisiones);
        var soloNegativos = EscalasGrafico.Rango([-30, -10]);
        Assert.Equal(0, soloNegativos.Max);
        Assert.True(soloNegativos.Min <= -30);
        var vacio = EscalasGrafico.Rango([null, null]);
        Assert.Equal((0d, 1d), (vacio.Min, vacio.Max));
    }

    [Fact]
    public void EscalaLinealInvierteElEjeVertical()
    {
        var e = new EscalaLineal(0, 200, 250, 50);
        Assert.Equal(250, e.Mapear(0));
        Assert.Equal(50, e.Mapear(200));
        Assert.Equal(150, e.Mapear(100));
        var barra = GeometriaGrafico.Barra(e, 10, 20, 0, 100);
        Assert.Equal(new Caja(10, 150, 20, 100), barra);
        var negativa = GeometriaGrafico.Barra(new EscalaLineal(-100, 100, 200, 0), 0, 10, 0, -50);
        Assert.Equal(new Caja(0, 100, 10, 50), negativa);
    }

    [Fact]
    public void LosHuecosCortanLaLineaSinInterpolar()
    {
        decimal?[] valores = [1, 2, null, 4, null, null, 7, 8, 9];
        var tramos = GeometriaGrafico.Tramos(valores);
        Assert.Equal(3, tramos.Count);
        Assert.Equal([0, 1], tramos[0]);
        Assert.Equal([3], tramos[1]);
        Assert.Equal([6, 7, 8], tramos[2]);
        Assert.Empty(GeometriaGrafico.Tramos(new decimal?[] { null, null }));
    }

    [Fact]
    public void BandasYBarrasDelGrupo()
    {
        var area = GeometriaGrafico.Area(640, 250, new Margenes(46, 14, 10, 26));
        Assert.Equal(new Caja(46, 14, 584, 210), area);
        Assert.Equal(0, GeometriaGrafico.CategoriaEn(area, 12, 47));
        Assert.Equal(11, GeometriaGrafico.CategoriaEn(area, 12, 629));
        Assert.Equal(-1, GeometriaGrafico.CategoriaEn(area, 12, 20));
        var banda = GeometriaGrafico.Banda(area, 12, 3);
        var barras = GeometriaGrafico.BarrasDelGrupo(banda, 2);
        Assert.Equal(2, barras.Count);
        Assert.True(barras[0].X >= banda.X && barras[1].X + barras[1].Ancho <= banda.Derecha);
        Assert.Equal(2, barras[1].X - (barras[0].X + barras[0].Ancho), 6);
    }

    [Fact]
    public void BarraApiladaProporcionalConMinimoYSinCeros()
    {
        var seg = GeometriaGrafico.Apilada([600m, 300m, 0m, 1m, 99m], 400, hueco: 2, minimo: 4);
        Assert.Equal(0, seg[2].Ancho);
        Assert.True(seg[3].Ancho >= 4);
        var usados = seg.Where(s => s.Ancho > 0).ToList();
        Assert.Equal(400, usados.Sum(s => s.Ancho) + 2 * (usados.Count - 1), 6);
        Assert.True(seg[0].Ancho > seg[1].Ancho && seg[1].Ancho > seg[4].Ancho);
        Assert.All(GeometriaGrafico.Apilada([0m, -3m], 300), s => Assert.Equal(0, s.Ancho));
    }

    [Fact]
    public void MinigraficoConHuecosYValoresIguales()
    {
        var p = MinigraficoGeometria.Puntos([5m, null, 10m], 100, 30);
        Assert.Null(p[1]);
        Assert.Equal(3, p[0]!.Value.X);
        Assert.Equal(97, p[2]!.Value.X);
        Assert.True(p[2]!.Value.Y < p[0]!.Value.Y);
        var planos = MinigraficoGeometria.Puntos([4m, 4m], 100, 30);
        Assert.Equal(planos[0]!.Value.Y, planos[1]!.Value.Y);
    }

    [Fact]
    public void TextosCompactosYVariacion()
    {
        Assert.Equal("120k", FormatoGrafico.Compacto(120_000));
        Assert.Equal("1,25M", FormatoGrafico.Compacto(1_250_000));
        Assert.Equal("37,5k", FormatoGrafico.Compacto(37_500));
        Assert.Equal("−30k", FormatoGrafico.Compacto(-30_000));
        Assert.Equal("950", FormatoGrafico.Compacto(950));
        Assert.Equal("▲ 7,9 %", FormatoGrafico.TextoVariacion(7.9m));
        Assert.Equal("▼ 1,1 %", FormatoGrafico.TextoVariacion(-1.1m));
        Assert.Null(FormatoGrafico.Variacion(5, 0));
    }

    [Fact]
    public void ElTooltipPorDefectoListaCadaSerieYSuProyeccion()
    {
        var d = new DatosGrafico(["ene", "feb"], [new SerieGrafico("2026", PaletaGraficos.Serie1, [10m, null], [null, 5m]), new SerieGrafico("2025", PaletaGraficos.Serie2, [8m, 9m])]);
        var t = d.TooltipDe(1, v => v.ToString("0"));
        Assert.Equal("feb", t.Titulo);
        Assert.Equal(["2026", "Proyección", "2025"], t.Filas.Select(f => f.Etiqueta));
        Assert.Equal("—", t.Filas[0].Valor);
        Assert.Equal([10d, 5d, 8d, 9d], d.ValoresEje(apilarProyeccion: true).Select(v => v!.Value));
    }
}
