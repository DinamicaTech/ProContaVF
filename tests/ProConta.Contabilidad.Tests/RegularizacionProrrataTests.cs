using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Prorrata definitiva y su regularización (contabilidad/procesos/prorrata), sin BD: el 303 del 4T lleva la regularización solo con la
/// definitiva fijada, el asiento del 31/12 a la 4729 contra la 6391 o la 6341 (repartidas por centro) y la liquidación del 4T salda la
/// 4729 sin ajuste. Ejemplo sintético: ventas con derecho 2.000 de 10.000 (legal 20 %), cuota sujeta 1.000 en el 1T (centro 20) y 500
/// en el 3T (centro 21), provisional 15 %: deducido 150 + 75 = 225.
/// </summary>
public sealed class RegularizacionProrrataTests
{
    private const int Centro100 = 10, CentroA = 20, CentroB = 21, CentroExento = 30;

    private static LineaIva L(SentidoIva sentido, DeduccionIva ded, decimal baseImponible, decimal tipo, string cuenta, int? centro, int trim) =>
        new(new DateTime(2026, trim * 3, 1), trim, 1, sentido, OperacionIva.Nacional, false, tipo, "Raíz", "Centro", "Actividad", ded, "Tercero sintético", null, null,
            baseImponible, Math.Round(baseImponible * tipo / 100m, 2), 0m, cuenta, null)
        { IdCentroCoste = centro };

    private static readonly IReadOnlyList<LineaIva> Ejemplo =
    [
        L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 2000m, 21m, "4770021", Centro100, 1),
        L(SentidoIva.Repercutido, DeduccionIva.NoDeducible, 8000m, 0m, "4776000", CentroExento, 2),
        L(SentidoIva.Soportado, DeduccionIva.Prorrata, 1000m / 0.21m, 21m, "4720021", CentroA, 1),
        L(SentidoIva.Soportado, DeduccionIva.Prorrata, 500m / 0.21m, 21m, "4720021", CentroB, 3),
    ];

    private static PropuestaRegularizacionProrrata Regularizar(decimal definitiva, IReadOnlyList<LineaIva>? lineas = null) =>
        CalculoRegularizacionProrrata.Calcular(new DatosRegularizacionProrrata(2026, CalculoIvaEjercicio.Calcular(lineas ?? Ejemplo, 0.15m, definitiva), lineas ?? Ejemplo,
            CuentasRegularizacionProrrata.Defecto));

    private static void Cuadra(PropuestaRegularizacionProrrata p)
    {
        Assert.Equal(p.TotalDebe, p.TotalHaber);
        Assert.All(p.Lineas, l => Assert.True((l.Debe == 0m) != (l.Haber == 0m), $"{l.Cuenta}: una línea va al Debe o al Haber"));
    }

    [Fact]
    public void SinDefinitivaEl303NoLlevaRegularizacionYLaProrrataSeEstimaConLaReal()
    {
        var iva = CalculoIvaEjercicio.Calcular(Ejemplo, 0.15m, null);
        Assert.Equal(0.20m, iva.Prorrata.Real);
        Assert.False(iva.Prorrata.DefinitivaFijada);
        Assert.Equal(0.20m, iva.Prorrata.Aplicada);
        Assert.Equal(75m, iva.Prorrata.Regularizacion(0));
        Assert.All(Enumerable.Range(1, 4), t => Assert.Equal(0m, iva.Modelo303.Linea(Calculo303.ClaveRegularizacionProrrata).En(t).Cuota));
    }

    [Fact]
    public void ConDefinitivaEl303DelCuartoTrimestreDeduceLaRegularizacion()
    {
        var sin = CalculoIvaEjercicio.Calcular(Ejemplo, 0.15m, null).Modelo303;
        var con = CalculoIvaEjercicio.Calcular(Ejemplo, 0.15m, 0.20m).Modelo303;
        Assert.Equal(75m, con.Linea(Calculo303.ClaveRegularizacionProrrata).En(4).Cuota);
        Assert.Equal(75m, con.Linea(Calculo303.ClaveRegularizacionProrrata).En(0).Cuota);
        Assert.Equal(sin.TotalDeducible(4) + 75m, con.TotalDeducible(4));
        Assert.Equal(sin.Resultado(4) - 75m, con.Resultado(4));
        // Los tres primeros trimestres no cambian.
        Assert.All(Enumerable.Range(1, 3), t => Assert.Equal(sin.Resultado(t), con.Resultado(t)));
    }

    [Fact]
    public void AFavorCargaLaCuatroSieteDosNueveYAbonaLaSeisTresNueveUnoPorCentro()
    {
        var p = Regularizar(0.20m);
        Assert.Equal((0.15m, 0.20m, 0.20m, true), (p.Provisional, p.Legal, p.Definitiva, p.DefinitivaEsLaLegal));
        Assert.Equal((1500m, 225m, 300m, 75m), (p.CuotaSujeta, p.Deducido, p.Deducible, p.Regularizacion));
        Assert.Equal(new DateTime(2026, 12, 31), p.Fecha);
        Assert.Equal(
            [("4729001", 75m, 0m, (int?)null), ("6391000", 0m, 50m, CentroA), ("6391000", 0m, 25m, CentroB)],
            p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber, l.IdCentroCoste)).ToList());
        Cuadra(p);
        Assert.Empty(p.Avisos);
    }

    [Fact]
    public void EnContraCargaLaSeisTresCuatroUnoYAbonaLaCuatroSieteDosNueve()
    {
        var p = Regularizar(0.10m);
        Assert.Equal(-75m, p.Regularizacion);
        Assert.Equal(
            [("6341000", 50m, 0m, (int?)CentroA), ("6341000", 25m, 0m, CentroB), ("4729001", 0m, 75m, null)],
            p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber, l.IdCentroCoste)).ToList());
        Cuadra(p);
        // No es la legal: lo avisa, pero se puede confirmar (es la del gestor).
        Assert.False(p.DefinitivaEsLaLegal);
        Assert.Contains(p.Avisos, a => a.Contains("no es la legal"));
    }

    [Fact]
    public void SiLaDefinitivaEsLaProvisionalNoHayAsiento()
    {
        var p = Regularizar(0.15m);
        Assert.Equal(0m, p.Regularizacion);
        Assert.Empty(p.Lineas);
        Assert.Contains(p.Avisos, a => a.Contains("no hace falta asiento"));
    }

    [Fact]
    public void LaRegularizacionRedondeaPorTrimestreComoElInforme()
    {
        // 333,33 × 20 % = 66,666 → 66,67 por trimestre; dos trimestres: 133,34 (no 133,33 del total).
        IReadOnlyList<LineaIva> lineas =
        [
            L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 2000m, 21m, "4770021", Centro100, 1),
            L(SentidoIva.Repercutido, DeduccionIva.NoDeducible, 8000m, 0m, "4776000", CentroExento, 1),
            L(SentidoIva.Soportado, DeduccionIva.Prorrata, 333.33m / 0.21m, 21m, "4720021", CentroA, 1),
            L(SentidoIva.Soportado, DeduccionIva.Prorrata, 333.33m / 0.21m, 21m, "4720021", CentroA, 2),
        ];
        var p = Regularizar(0.20m, lineas);
        Assert.Equal(133.34m, p.Deducible);
        Assert.Equal(p.Regularizacion, CalculoIvaEjercicio.Calcular(lineas, 0.15m, 0.20m).Modelo303.Linea(Calculo303.ClaveRegularizacionProrrata).En(4).Cuota);
        Cuadra(p);
    }

    [Fact]
    public void LaLiquidacionDelCuartoTrimestreSaldaLaCuatroSieteDosNueveSinAjuste()
    {
        IReadOnlyList<LineaIva> lineas = [.. Ejemplo, L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 100m / 0.21m, 21m, "4770021", Centro100, 4)];
        var iva = CalculoIvaEjercicio.Calcular(lineas, 0.15m, 0.20m);
        var regularizacion = iva.Modelo303.Linea(Calculo303.ClaveRegularizacionProrrata).En(4).Cuota;
        var p = CalculoLiquidacionIva.Calcular(new DatosLiquidacionIva(2026, 4, [new("4770021", -100m), new("4729001", regularizacion)], [], iva.Modelo303, lineas,
            CuentasLiquidacionIva.Defecto));
        Assert.Contains(p.Lineas, l => l.Cuenta == "4729001" && l.Haber == regularizacion && l.Concepto.Contains("prorrata"));
        Assert.Equal(100m - regularizacion, p.ResultadoCalculado);
        Assert.Equal(0m, p.Ajuste);
        Assert.Equal(p.TotalDebe, p.TotalHaber);
    }

    [Fact]
    public void LaCuatroSieteDosNueveSoloSeSaldaEnElCuartoTrimestre()
    {
        Assert.False(CalculoLiquidacionIva.SeLiquida("4729001", 1));
        Assert.False(CalculoLiquidacionIva.SeLiquida("4729001", 3));
        Assert.True(CalculoLiquidacionIva.SeLiquida("4729001", 4));
        Assert.True(CalculoLiquidacionIva.SeLiquida("4720021", 2));
        Assert.False(CalculoLiquidacionIva.SeLiquida("4729001"));
    }
}
