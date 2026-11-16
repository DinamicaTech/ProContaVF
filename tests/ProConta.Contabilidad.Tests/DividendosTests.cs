using ProConta.Contabilidad.Dividendos;

namespace ProConta.Contabilidad.Tests;

/// <summary>Cálculo del reparto de dividendos sin BD (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55). Datos inventados.</summary>
public sealed class DividendosTests
{
    private static readonly DateTime Fecha = new(2026, 12, 15);

    private static SocioCalculoDividendos Socio(int id, string nombre, decimal neto, string c526, string c4751) => new(id, nombre, 50m, c526, c4751, neto);

    [Fact]
    public void Con_19_por_ciento_y_50000_netos_por_socio_cuadra_como_el_reparto_de_2025()
    {
        var p = CalculoDividendos.Calcular(Fecha, ConfiguracionDividendos.Defecto,
            [Socio(1, "Socia Uno", 50000m, "5260001", "4751011"), Socio(2, "Socio Dos", 50000m, "5260002", "4751012")]);

        Assert.All(p.Socios, s => { Assert.Equal(61728.40m, s.Bruto); Assert.Equal(11728.40m, s.Retencion); Assert.Equal(50000m, s.Neto); });
        Assert.Equal(new LineaDividendos("1130000", 123456.80m, 0m, "Reparto de dividendos 15/12/2026"), p.Lineas[0]);
        foreach (var (c526, c4751, nombre) in new[] { ("5260001", "4751011", "Socia Uno"), ("5260002", "4751012", "Socio Dos") })
        {
            Assert.Contains(new LineaDividendos(c526, 0m, 61728.40m, $"Reparto de dividendos 15/12/2026 {nombre}"), p.Lineas);
            Assert.Contains(new LineaDividendos(c526, 11728.40m, 0m, $"Retención dividendos 15/12/2026 {nombre}"), p.Lineas);
            Assert.Contains(new LineaDividendos(c4751, 0m, 11728.40m, $"Retención dividendos 15/12/2026 {nombre}"), p.Lineas);
        }
        Assert.Equal(7, p.Lineas.Count);
        // Invariante contable: Debe = Haber.
        Assert.Equal(p.TotalDebe, p.TotalHaber);
        Assert.Equal(123456.80m + 2 * 11728.40m, p.TotalDebe);
        Assert.Equal(100000m, p.Neto);
        Assert.Equal(123456.80m, p.Bruto);
        Assert.Equal(23456.80m, p.Retencion);
        Assert.Empty(p.Avisos);
    }

    [Theory]
    [InlineData(50000, 19, 61728.40)]
    [InlineData(810, 19, 1000)]
    [InlineData(0.01, 19, 0.01)]
    [InlineData(100, 0, 100)]
    [InlineData(1234.56, 21, 1562.73)]
    public void El_bruto_redondea_al_centimo_al_alza_en_el_medio(decimal neto, decimal retencion, decimal bruto) =>
        Assert.Equal(bruto, CalculoDividendos.Bruto(neto, retencion));

    [Fact]
    public void Medio_centimo_va_al_alza()
    {
        // 0,405 / (1 − 0,1) = 0,45 exacto; 0,0045 / 0,9 = 0,005 → 0,01 (AwayFromZero), no 0,00 (redondeo del banquero).
        Assert.Equal(0.01m, CalculoDividendos.Bruto(0.0045m, 10m));
    }

    [Fact]
    public void El_neto_total_se_reparte_por_participacion_con_el_resto_mayor()
    {
        Assert.Equal([50000m, 50000m], CalculoDividendos.Repartir(100000m, [50m, 50m]));
        // 100 € entre tres a partes iguales: 33,34 + 33,33 + 33,33 = 100,00.
        var tres = CalculoDividendos.Repartir(100m, [33.3333m, 33.3333m, 33.3334m]);
        Assert.Equal(100m, tres.Sum());
        Assert.Equal([33.33m, 33.33m, 33.34m], tres);
        Assert.Equal([0.01m, 0m], CalculoDividendos.Repartir(0.01m, [60m, 40m]));
        // Sin participaciones, a partes iguales.
        Assert.Equal([50m, 50m], CalculoDividendos.Repartir(100m, [0m, 0m]));
    }

    [Fact]
    public void Avisa_si_las_reservas_no_llegan_o_los_porcentajes_no_suman_100()
    {
        var p = CalculoDividendos.Calcular(Fecha, new ConfiguracionDividendos(19m, "1130000"), [Socio(1, "Socia Uno", 810m, "5260001", "4751011")],
            saldoReservas: 900m, sumaParticipaciones: 50m);
        Assert.Equal(1000m, p.Bruto);
        Assert.Equal(2, p.Avisos.Count);
        Assert.Contains(p.Avisos, a => a.Contains("1130000") && a.Contains("900,00"));
        Assert.Contains(p.Avisos, a => a.Contains("suman 50 %"));
        Assert.Equal(p.TotalDebe, p.TotalHaber);
    }

    [Fact]
    public void Sin_retencion_no_hay_lineas_de_retencion_y_los_socios_sin_neto_no_salen()
    {
        var p = CalculoDividendos.Calcular(Fecha, new ConfiguracionDividendos(0m, "1130001"),
            [Socio(1, "Socia Uno", 300m, "5260001", "4751011"), Socio(2, "Socio Dos", 0m, "5260002", "4751012")]);
        Assert.Single(p.Socios);
        Assert.Equal([("1130001", 300m, 0m), ("5260001", 0m, 300m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)));
    }

    [Fact]
    public void La_configuracion_vacia_es_19_por_ciento_y_la_1130000()
    {
        Assert.Equal(new ConfiguracionDividendos(19m, "1130000"), ConfiguracionDividendos.Con(null, " "));
        Assert.Equal(new ConfiguracionDividendos(15m, "1130002"), ConfiguracionDividendos.Con(15m, "1130002 "));
    }
}
