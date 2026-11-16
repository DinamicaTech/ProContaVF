using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo de la liquidación del IVA de un trimestre (contabilidad/procesos/liquidacion-iva), sin BD. Comprueba el ejemplo de
/// aceptación del nodo, la compensación de la 4700 (V4), el importe fijado a mano, el ajuste y el cuadre de cada asiento.
/// </summary>
public sealed class LiquidacionIvaTests
{
    private const int Centro100 = 10, CentroProrrata = 20, CentroProrrataB = 21, CentroNoDed = 30;

    private static LineaIva L(SentidoIva sentido, DeduccionIva ded, decimal cuota, string cuenta, int? centro, int trim = 1,
        OperacionIva op = OperacionIva.Nacional, bool isp = false) =>
        new(new DateTime(2026, trim * 3, 1), trim, 1, sentido, op, isp, 21m, "Raíz", "Centro", "Actividad", ded, "Tercero sintético", null, null,
            cuota / 0.21m, cuota, sentido == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? cuota : 0m, cuenta, null)
        { IdCentroCoste = centro };

    private static PropuestaLiquidacionIva Calcular(IReadOnlyList<LineaIva> lineas, IReadOnlyList<SaldoSubcuenta> saldos, decimal prorrata,
        IReadOnlyList<SaldoSubcuenta>? credito = null, decimal? fijado = null, int trimestre = 1) =>
        CalculoLiquidacionIva.Calcular(new DatosLiquidacionIva(2026, trimestre, saldos, credito ?? [], Calculo303.Calcular(lineas, prorrata), lineas,
            CuentasLiquidacionIva.Defecto, fijado));

    private static void Cuadra(PropuestaLiquidacionIva p)
    {
        Assert.Equal(p.TotalDebe, p.TotalHaber);
        Assert.All(p.Lineas, l => Assert.True((l.Debe == 0m) != (l.Haber == 0m), $"{l.Cuenta}: una línea va al Debe o al Haber"));
    }

    /// <summary>Ejemplo de aceptación del nodo: repercutido 10.000, soportado 100 % 2.000, por prorrata 1.000, prorrata del 80 %.</summary>
    private static readonly IReadOnlyList<LineaIva> Ejemplo =
    [
        L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 10000m, "4770021", Centro100),
        L(SentidoIva.Soportado, DeduccionIva.Deducible100, 2000m, "4725021", Centro100),
        L(SentidoIva.Soportado, DeduccionIva.Prorrata, 1000m, "4727021", CentroProrrata),
    ];

    private static readonly IReadOnlyList<SaldoSubcuenta> SaldosEjemplo =
        [new("4770021", -10000m), new("4725021", 2000m), new("4727021", 1000m)];

    [Fact]
    public void ElEjemploDelNodoDaElAsientoDeAceptacion()
    {
        var p = Calcular(Ejemplo, SaldosEjemplo, 0.80m);
        Assert.Equal(
            [("4770021", 10000m, 0m), ("4725021", 0m, 2000m), ("4727021", 0m, 1000m), ("6341000", 200m, 0m), ("4750000", 0m, 7200m)],
            p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Assert.Equal(7200m, p.ResultadoCalculado);
        Assert.Equal(7200m, p.AIngresar);
        Assert.Equal(0m, p.Ajuste);
        Assert.Empty(p.Avisos);
        Assert.Equal(new DateTime(2026, 3, 31), p.Fecha);
        // La 6341 queda en el centro de la actividad en prorrata.
        Assert.Equal(CentroProrrata, p.Lineas.Single(l => l.Cuenta == "6341000").IdCentroCoste);
        Cuadra(p);
    }

    [Fact]
    public void LaCuatroSieteDosNueveNoSeSaldaYLosSaldosCeroNoSalen()
    {
        var p = Calcular(Ejemplo, [.. SaldosEjemplo, new("4729001", 55m), new("4720021", 0m)], 0.80m);
        Assert.DoesNotContain(p.Lineas, l => l.Cuenta is "4729001" or "4720021");
        Cuadra(p);
    }

    [Fact]
    public void ACompensarCargaLaCuatroSieteCeroCero()
    {
        IReadOnlyList<LineaIva> lineas = [L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 100m, "4770021", Centro100), L(SentidoIva.Soportado, DeduccionIva.Deducible100, 400m, "4725021", Centro100)];
        var p = Calcular(lineas, [new("4770021", -100m), new("4725021", 400m)], 0.15m);
        Assert.Equal(-300m, p.ResultadoCalculado);
        Assert.Equal(300m, p.ACompensar);
        Assert.Contains(p.Lineas, l => l.Cuenta == "4700000" && l.Debe == 300m);
        Assert.DoesNotContain(p.Lineas, l => l.Cuenta == "4750000");
        Cuadra(p);
    }

    [Fact]
    public void ElCreditoAnteriorSeCompensaAntesDeIngresar()
    {
        // V4: 2.500 € a compensar de trimestres anteriores; sale a ingresar 7.200: abona la 4700 por 2.500 y la 4750 por 4.700.
        var p = Calcular(Ejemplo, SaldosEjemplo, 0.80m, credito: [new("4700000", 2500m)]);
        Assert.Equal(2500m, p.Compensado);
        Assert.Equal(4700m, p.AIngresar);
        Assert.Contains(p.Lineas, l => l.Cuenta == "4700000" && l.Haber == 2500m);
        Assert.Contains(p.Lineas, l => l.Cuenta == "4750000" && l.Haber == 4700m);
        Cuadra(p);
    }

    [Fact]
    public void ElCreditoMayorQueElResultadoSoloSeCompensaHastaElResultado()
    {
        var p = Calcular(Ejemplo, SaldosEjemplo, 0.80m, credito: [new("4700000", 5000m), new("4700001", 4000m), new("4700002", -10m)]);
        Assert.Equal(7200m, p.Compensado);
        Assert.Equal(0m, p.AIngresar);
        Assert.Equal([("4700000", 5000m), ("4700001", 2200m)], p.Lineas.Where(l => l.Cuenta.StartsWith("4700")).Select(l => (l.Cuenta, l.Haber)).ToList());
        Assert.DoesNotContain(p.Lineas, l => l.Cuenta == "4750000");
        Cuadra(p);
    }

    [Fact]
    public void ElImporteFijadoAMenosVaALaSeisTresNueveUnoYAMasALaSeisTresCuatroUno()
    {
        // Presentado 7.150 (50 menos que lo calculado): ajuste positivo, 6391 al Haber.
        var menos = Calcular(Ejemplo, SaldosEjemplo, 0.80m, fijado: 7150m);
        Assert.Equal(50m, menos.Ajuste);
        Assert.Equal(50m, menos.DiferenciaFijada);
        Assert.Equal(0m, menos.DiferenciaLibros);
        Assert.Contains(menos.Lineas, l => l.Cuenta == "6391000" && l.Haber == 50m);
        Assert.Contains(menos.Lineas, l => l.Cuenta == "4750000" && l.Haber == 7150m);
        Assert.Contains(menos.Avisos, a => a.Contains("difiere del calculado"));
        Cuadra(menos);
        // Presentado 7.210 (10 más): ajuste negativo, 6341 al Debe.
        var mas = Calcular(Ejemplo, SaldosEjemplo, 0.80m, fijado: 7210m);
        Assert.Equal(-10m, mas.Ajuste);
        Assert.Equal(2, mas.Lineas.Count(l => l.Cuenta == "6341000"));
        Assert.Contains(mas.Lineas, l => l.Cuenta == "6341000" && l.Debe == 10m && l.Concepto.StartsWith("Ajuste"));
        Cuadra(mas);
    }

    [Fact]
    public void LaDiferenciaEntreCuentasYLibrosVaAlAjusteConAviso()
    {
        // La 477 tiene 30,37 € más que el libro (un apunte de IVA sin factura): con las cuentas saldría a pagar 30,37 € más.
        var p = Calcular(Ejemplo, [new("4770021", -10030.37m), new("4725021", 2000m), new("4727021", 1000m)], 0.80m);
        Assert.Equal(7200m, p.AIngresar);
        Assert.Equal(30.37m, p.Ajuste);
        Assert.Equal(30.37m, p.DiferenciaLibros);
        Assert.Contains(p.Lineas, l => l.Cuenta == "6391000" && l.Haber == 30.37m);
        Assert.Contains(p.Avisos, a => a.Contains("no coinciden con los libros"));
        Cuadra(p);
    }

    [Fact]
    public void ElIvaNoDeducibleQueEstaEnUnaCuatroSieteDosVaALaSeisTresCuatroUno()
    {
        IReadOnlyList<LineaIva> lineas = [.. Ejemplo, L(SentidoIva.Soportado, DeduccionIva.NoDeducible, 120m, "4720021", CentroNoDed)];
        var p = Calcular(lineas, [.. SaldosEjemplo, new("4720021", 120m)], 0.80m);
        Assert.Contains(p.Lineas, l => l.Cuenta == "6341000" && l.Debe == 120m && l.IdCentroCoste == CentroNoDed);
        Assert.Contains(p.Lineas, l => l.Cuenta == "4720021" && l.Haber == 120m);
        Assert.Equal(7200m, p.AIngresar);
        Assert.Equal(0m, p.Ajuste);
        Cuadra(p);
    }

    [Fact]
    public void ElIvaSinClasificarNoSeDeduceVaAlAjusteYSeAvisa()
    {
        IReadOnlyList<LineaIva> lineas = [.. Ejemplo, L(SentidoIva.Soportado, DeduccionIva.SinCentro, 25.14m, "4720021", null)];
        var p = Calcular(lineas, [.. SaldosEjemplo, new("4720021", 25.14m)], 0.80m);
        Assert.Equal(25.14m, p.SinClasificar472);
        Assert.Equal(-25.14m, p.Ajuste);
        Assert.Equal(0m, p.DiferenciaLibros);
        Assert.Contains(p.Avisos, a => a.Contains("sin clasificar"));
        Assert.DoesNotContain(p.Avisos, a => a.Contains("no coinciden"));
        Cuadra(p);
    }

    [Fact]
    public void LaParteNoDeducibleSeRepartePorCentroYSumaLaDelTrescientosTres()
    {
        // 15 % de prorrata sobre 100,01 + 200,02 = 300,03: deducible Redondear(45,0045) = 45,00; no deducible 255,03, repartido 85,01 / 170,02.
        IReadOnlyList<LineaIva> lineas =
        [
            L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 1000m, "4770021", Centro100),
            L(SentidoIva.Soportado, DeduccionIva.Prorrata, 100.01m, "4720021", CentroProrrata),
            L(SentidoIva.Soportado, DeduccionIva.Prorrata, 200.02m, "4720021", CentroProrrataB),
        ];
        var p = Calcular(lineas, [new("4770021", -1000m), new("4720021", 300.03m)], 0.15m);
        Assert.Equal(255.03m, p.NoDeducibleProrrata);
        var partes = p.Lineas.Where(l => l.Cuenta == "6341000").Select(l => (l.IdCentroCoste, l.Debe)).ToList();
        Assert.Equal(255.03m, partes.Sum(x => x.Debe));
        Assert.Equal([((int?)CentroProrrata, 85.01m), ((int?)CentroProrrataB, 170.02m)], partes);
        Assert.Equal(1000m - 45m, p.AIngresar);
        Cuadra(p);
    }

    [Fact]
    public void ElRepartoPorRestoMayorSumaExactamenteElTotal()
    {
        var partes = CalculoLiquidacionIva.Repartir(0.10m, [(1, 1m), (2, 1m), (3, 1m)]);
        Assert.Equal(0.10m, partes.Sum(p => p.Importe));
        Assert.Equal([0.04m, 0.03m, 0.03m], partes.Select(p => p.Importe).ToList());
        Assert.Equal([(null, 5m)], CalculoLiquidacionIva.Repartir(5m, []));
        Assert.Empty(CalculoLiquidacionIva.Repartir(0m, [(1, 1m)]));
    }

    [Fact]
    public void SoloLiquidaLasLineasDeSuTrimestre()
    {
        IReadOnlyList<LineaIva> lineas = [.. Ejemplo, L(SentidoIva.Repercutido, DeduccionIva.Deducible100, 999m, "4770021", Centro100, trim: 2)];
        var p = Calcular(lineas, SaldosEjemplo, 0.80m);
        Assert.Equal(7200m, p.ResultadoCalculado);
        Cuadra(p);
    }

    [Fact]
    public void SinNadaQueLiquidarNoHayLineasYSeAvisa()
    {
        var p = Calcular([], [], 0.15m);
        Assert.Empty(p.Lineas);
        Assert.Contains(p.Avisos, a => a.Contains("no tiene IVA que liquidar"));
    }

    [Fact]
    public void LaParidadDeDosMilVeintiseisDaElResultadoDelLibroAnterior()
    {
        // Mismas cifras que Iva303Tests (2T: 3.026,65 €, lo que se pagó), con las 472/477 iguales a los libros: sin ajuste.
        var lineas = Iva303Tests.Paridad2026.Select(l => l with { CuentaIva = l.Sentido == SentidoIva.Repercutido ? "4770021" : "4720021" }).ToList();
        var r = Calculo303.Calcular(lineas, 0.15m);
        decimal Saldo(SentidoIva s, int t) => lineas.Where(l => l.Trimestre == t && l.Sentido == s).Sum(l => l.Cuota)
            + (s == SentidoIva.Repercutido ? lineas.Where(l => l.Trimestre == t && l.Autorrepercutida).Sum(l => l.CuotaAutorrepercutida) : 0m);
        var p = CalculoLiquidacionIva.Calcular(new DatosLiquidacionIva(2026, 2,
            [new("4770021", -Saldo(SentidoIva.Repercutido, 2)), new("4720021", Saldo(SentidoIva.Soportado, 2))], [], r, lineas, CuentasLiquidacionIva.Defecto));
        Assert.Equal(3026.65m, p.ResultadoCalculado);
        Assert.Equal(3026.65m, p.AIngresar);
        Cuadra(p);
    }
}
