using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Ratios;
using ProConta.Inicio;
using ProConta.UI.Base.Graficos;

namespace ProConta.Inicio.Tests;

/// <summary>Modelo de balance de situación mínimo con los códigos de apartado y título del PGC Pymes de la maestra.</summary>
internal static class ModeloSintetico
{
    private static ReglaBalance R(string prefijo, int mult, string apartado, string titulo, int prioridad = 10) =>
        new(prefijo, mult, prioridad, apartado.StartsWith("ACT") ? "ACTIVO" : "PATRIMONIO NETO Y PASIVO", 0, apartado, 0, titulo, 0, null, apartado, titulo);

    public static ModeloBalance Situacion { get; } = new("SINTETICO", "Situación sintética", TipoBalance.Situacion,
    [
        R("21", 1, "ACT.A", "ACT.A.2.002"),
        R("28", 1, "ACT.A", "ACT.A.2.002"),
        R("30", 1, "ACT.B", "ACT.B.1.009"),
        R("43", 1, "ACT.B", "ACT.B.2.010"),
        R("472", 1, "ACT.B", "ACT.B.2.010"),
        R("57", 1, "ACT.B", "ACT.B.6.017"),
        R("10", -1, "PNP.A", "PNP.A.1.019"),
        R("12", -1, "PNP.A", "PNP.A.5.027"),
        R("6", -1, "PNP.A", "PNP.A.7.031", 1),
        R("7", -1, "PNP.A", "PNP.A.7.031", 1),
        R("17", -1, "PNP.B", "PNP.B.2.039"),
        R("40", -1, "PNP.C", "PNP.C.4.052"),
        R("475", -1, "PNP.C", "PNP.C.4.052"),
        R("477", -1, "PNP.C", "PNP.C.4.052"),
    ]);
}

/// <summary>Los 12 ratios con valores conocidos, sus estados y los casos límite (informes/ratios), sin BD.</summary>
public sealed class RatiosTests
{
    private static readonly DateTime Balance = new(2026, 10, 31);

    private static MovimientoRatio M(int anio, int mes, int dia, string cuenta, decimal debe, decimal haber, byte tipo = 0) => new(new DateTime(anio, mes, dia), cuenta, tipo, debe, haber);

    /// <summary>
    /// Apertura de 2026: inmovilizado 10.000, existencias 2.000, banco 6.000, bróker 4.000 / capital 15.000, deuda a largo
    /// 5.000, proveedor 2.000. En el año: una venta de 10.000 + 2.100 de IVA, un gasto de 3.000 + 630, una amortización de 1.000 y
    /// 200 de intereses pagados; y una venta del 2 de noviembre, posterior al balance, que no cuenta.
    /// </summary>
    private static List<MovimientoRatio> Actual() =>
    [
        M(2026, 1, 1, "2100000", 10000, 0, 1), M(2026, 1, 1, "3000000", 2000, 0, 1), M(2026, 1, 1, "5720001", 6000, 0, 1), M(2026, 1, 1, "5720019", 4000, 0, 1),
        M(2026, 1, 1, "1000000", 0, 15000, 1), M(2026, 1, 1, "1700000", 0, 5000, 1), M(2026, 1, 1, "4000001", 0, 2000, 1),
        M(2026, 3, 1, "4300001", 12100, 0), M(2026, 3, 1, "7000001", 0, 10000), M(2026, 3, 1, "4770001", 0, 2100),
        M(2026, 4, 1, "6290001", 3000, 0), M(2026, 4, 1, "4720001", 630, 0), M(2026, 4, 1, "4000001", 0, 3630),
        M(2026, 6, 30, "6810001", 1000, 0), M(2026, 6, 30, "2810001", 0, 1000),
        M(2026, 6, 30, "6620001", 200, 0), M(2026, 6, 30, "5720001", 0, 200),
        M(2026, 11, 2, "4300001", 1210, 0), M(2026, 11, 2, "7000001", 0, 1000), M(2026, 11, 2, "4770001", 0, 210),
    ];

    /// <summary>2025: una venta de diciembre (dentro de los 12 meses), otra de junio (fuera) y la regularización (no cuenta).</summary>
    private static List<MovimientoRatio> Anterior() =>
    [
        M(2025, 6, 1, "7000001", 0, 9999), M(2025, 6, 1, "5720001", 9999, 0),
        M(2025, 12, 1, "7000001", 0, 5000), M(2025, 12, 1, "5720001", 5000, 0),
        M(2025, 12, 31, "7000001", 14999, 0, 2), M(2025, 12, 31, "1290000", 0, 14999, 2),
    ];

    private static ResultadoRatios Calcular(IReadOnlySet<string>? inversiones = null) =>
        CalculoRatios.Calcular(ModeloSintetico.Situacion, Actual(), [.. Actual(), .. Anterior()], Balance, inversiones ?? new HashSet<string> { "5720019" });

    private static decimal V(ResultadoRatios r, string clave) => r.Ratio(clave)!.Valor!.Value;

    [Fact]
    public void LosDoceRatiosConValoresConocidos()
    {
        var r = Calcular();
        Assert.True(r.HayBalance);
        Assert.Equal(new DateTime(2025, 11, 1), r.DesdeResultado);
        Assert.Empty(r.SinClasificar);
        // Balance a 31/10: AC 24.530 (existencias 2.000, realizable 16.730 con el bróker, disponible 5.800), AT 33.530, PN 20.800, PNC 5.000, PC 7.730.
        Assert.Equal(24530m / 7730m, V(r, "liquidez"));
        Assert.Equal(22530m / 7730m, V(r, "acida"));
        Assert.Equal(5800m / 7730m, V(r, "tesoreria"));
        Assert.Equal(33530m / 12730m, V(r, "solvencia"));
        Assert.Equal(12730m / 20800m, V(r, "endeudamiento"));
        Assert.Equal(7730m / 12730m, V(r, "calidad"));
        // 12 meses: ventas 15.000 (10.000 de 2026 y 5.000 de diciembre de 2025); gastos 3.000, amortización 1.000 e intereses 200.
        Assert.Equal(10800m / 20800m * 100m, V(r, "roe"));
        Assert.Equal(11000m / 33530m * 100m, V(r, "roa"));
        Assert.Equal(12000m / 15000m * 100m, V(r, "ebitda"));
        Assert.Equal(10800m / 15000m * 100m, V(r, "neto"));
        Assert.Equal(12100m / 17100m * 365m, V(r, "pmc"));
        Assert.Equal(5630m / 3630m * 365m, V(r, "pmp"));
        // Cada ratio enlaza a las cifras que lo forman.
        var acida = r.Ratio("acida")!;
        Assert.Equal(["Realizable", "Disponible", "Pasivo corriente"], acida.Partidas.Select(p => p.Nombre));
        Assert.Equal(16730m, acida.Partidas[0].Importe);
        Assert.Contains(acida.Partidas[0].Detalle, c => c.Cuenta == "5720019");
        Assert.Equal(["5720001"], acida.Partidas[1].CuentasMayor);
        // El balance cuadra: activo = patrimonio neto + pasivo exigible.
        var solv = r.Ratio("solvencia")!;
        Assert.Equal(solv.Partidas[0].Importe, solv.Partidas[1].Importe + r.Ratio("endeudamiento")!.Partidas[1].Importe);
    }

    [Fact]
    public void ElPeriodoMedioDeCobroSoloCuentaLosSaldosDeudoresDeClientes()
    {
        // Un anticipo de otro cliente (saldo acreedor en su 430) no reduce lo pendiente de cobro, como en el indicador.
        var mov = Actual();
        mov.AddRange([M(2026, 5, 1, "5720001", 500, 0), M(2026, 5, 1, "4300002", 0, 500)]);
        var r = CalculoRatios.Calcular(ModeloSintetico.Situacion, mov, [.. mov, .. Anterior()], Balance, new HashSet<string> { "5720019" });
        var pmc = r.Ratio("pmc")!;
        Assert.Equal(12100m / 17100m * 365m, pmc.Valor);
        Assert.Equal(["4300001"], pmc.Partidas[0].Detalle.Select(c => c.Cuenta));
        Assert.Contains("Saldo deudor de las 430 a 436", pmc.Partidas[0].Cuentas);
        Assert.Contains("saldos deudores", pmc.Definicion.Formula);
    }

    [Fact]
    public void LaCuentaDeInversionNoEsDisponibleSinoRealizable()
    {
        var sinMarca = Calcular(new HashSet<string>());
        var conMarca = Calcular();
        Assert.Equal(9800m / 7730m, V(sinMarca, "tesoreria"));
        Assert.Equal(5800m / 7730m, V(conMarca, "tesoreria"));
        // La prueba ácida y la liquidez no cambian: el bróker sigue en el activo corriente.
        Assert.Equal(V(sinMarca, "acida"), V(conMarca, "acida"));
        Assert.Equal(V(sinMarca, "liquidez"), V(conMarca, "liquidez"));
        Assert.Contains("5720019", conMarca.Ratio("tesoreria")!.Partidas[0].Cuentas);
    }

    [Fact]
    public void EstadosSegunElRango()
    {
        var r = Calcular();
        Assert.Equal(EstadoRatio.Vigilar, r.Ratio("liquidez")!.Estado);   // 3,17 > 2,5
        Assert.Equal(EstadoRatio.Bien, r.Ratio("solvencia")!.Estado);
        Assert.Equal(EstadoRatio.Vigilar, r.Ratio("calidad")!.Estado);    // 0,61: hasta 1,3 × 0,5
        Assert.Equal(EstadoRatio.Mal, r.Ratio("pmc")!.Estado);            // 258 días > 39
        Assert.Equal(r.Ratios.Count(x => x.Estado == EstadoRatio.Bien), r.Bien);

        var liquidez = CalculoRatios.Definiciones.Single(d => d.Clave == "liquidez");
        Assert.Equal(EstadoRatio.Bien, liquidez.Estado(1.5m));
        Assert.Equal(EstadoRatio.Bien, liquidez.Estado(2.5m));
        Assert.Equal(EstadoRatio.Vigilar, liquidez.Estado(1.2m));         // 80 % de 1,5 = 1,2: aún vigilar
        Assert.Equal(EstadoRatio.Mal, liquidez.Estado(1.19m));
        Assert.Equal(EstadoRatio.Vigilar, liquidez.Estado(2.51m));
        var roe = CalculoRatios.Definiciones.Single(d => d.Clave == "roe");
        Assert.Equal(EstadoRatio.Bien, roe.Estado(50m));                   // sin máximo
        Assert.Equal(EstadoRatio.Mal, roe.Estado(-3m));
        // Endeudamiento: tener poca deuda no es malo; por debajo del mínimo (0,4) es bien y por encima del máximo, vigilar.
        var endeudamiento = CalculoRatios.Definiciones.Single(d => d.Clave == "endeudamiento");
        Assert.Equal(EstadoRatio.Bien, endeudamiento.Estado(0.39m));
        Assert.Equal(EstadoRatio.Bien, endeudamiento.Estado(0.05m));
        Assert.Equal(EstadoRatio.Bien, endeudamiento.Estado(0m));
        Assert.Equal(EstadoRatio.Bien, endeudamiento.Estado(1m));
        Assert.Equal(EstadoRatio.Vigilar, endeudamiento.Estado(1.01m));
        Assert.Contains("por debajo de 0,4", endeudamiento.Referencia);
        var pmp = CalculoRatios.Definiciones.Single(d => d.Clave == "pmp");
        Assert.Equal(EstadoRatio.Bien, pmp.Estado(60m));
        Assert.Equal(EstadoRatio.Vigilar, pmp.Estado(78m));
        Assert.Equal(EstadoRatio.Mal, pmp.Estado(78.01m));
        Assert.Equal(12, CalculoRatios.Definiciones.Count);
    }

    [Fact]
    public void CasosLimiteSinDatosConSuMotivo()
    {
        // Sin pasivo corriente: los ratios con ese divisor salen sin datos, nunca con un número inventado.
        var sinPasivo = CalculoRatios.Calcular(ModeloSintetico.Situacion,
            [M(2026, 1, 1, "5720001", 1000, 0, 1), M(2026, 1, 1, "1000000", 0, 1000, 1)], [], Balance);
        var liq = sinPasivo.Ratio("liquidez")!;
        Assert.Null(liq.Valor);
        Assert.Equal(EstadoRatio.SinDatos, liq.Estado);
        Assert.Contains("Pasivo corriente es cero", liq.Motivo);
        Assert.Contains("Ventas (12 meses) es cero", sinPasivo.Ratio("neto")!.Motivo);

        // Patrimonio neto negativo: el endeudamiento y el ROE no tienen sentido.
        var negativo = CalculoRatios.Calcular(ModeloSintetico.Situacion,
            [M(2026, 1, 1, "5720001", 1000, 0, 1), M(2026, 1, 1, "4000001", 0, 3000, 1), M(2026, 1, 1, "1200000", 2000, 0, 1)], [], Balance);
        Assert.Contains("Patrimonio neto es negativo", negativo.Ratio("endeudamiento")!.Motivo);

        // Sin el resultado de los 12 meses: los de resultados salen sin datos con el motivo dado; los de balance, no.
        var sinResultado = CalculoRatios.Calcular(ModeloSintetico.Situacion, Actual(), Actual(), Balance, null, "Faltan los datos de 2025.");
        Assert.Equal("Faltan los datos de 2025.", sinResultado.Ratio("roa")!.Motivo);
        Assert.NotNull(sinResultado.Ratio("liquidez")!.Valor);

        // Sin balance: todo sin datos.
        var vacio = CalculoRatios.Calcular(ModeloSintetico.Situacion, [], [], Balance);
        Assert.False(vacio.HayBalance);
        Assert.All(vacio.Ratios, x => Assert.Equal(EstadoRatio.SinDatos, x.Estado));

        // El cierre no cuenta y una cuenta que el modelo no clasifica se lista aparte.
        var raro = CalculoRatios.Calcular(ModeloSintetico.Situacion,
            [M(2026, 1, 1, "5720001", 1000, 0, 1), M(2026, 1, 1, "1000000", 0, 1000, 1), M(2026, 5, 1, "5550000", 50, 0), M(2026, 5, 1, "5720001", 0, 50),
             M(2026, 10, 31, "5720001", 0, 950, 3)], [], Balance);
        Assert.Equal(["5550000"], raro.SinClasificar);
        Assert.Equal(950m, raro.Ratio("tesoreria")!.Partidas[0].Importe);
    }

    [Fact]
    public void LasCuentasDeBancosDeNorma43SonDisponibleYUnaMasaNegativaNoDaValor()
    {
        // Una 555 que Norma 43 usa como banco y el modelo pondría en el pasivo corriente con saldo deudor: es disponible.
        var modelo = new ModeloBalance("SINTETICO", "Situación sintética", TipoBalance.Situacion, [.. ModeloSintetico.Situacion.Reglas, new ReglaBalance("555", -1, 10, "PATRIMONIO NETO Y PASIVO", 0, "PNP.C", 0, "PNP.C.2.047", 0, null, "PNP.C", "PNP.C.2.047")]);
        List<MovimientoRatio> movs =
        [
            M(2026, 1, 1, "5720001", 6000, 0, 1), M(2026, 1, 1, "5550000", 3000, 0, 1), M(2026, 1, 1, "1000000", 0, 3500, 1), M(2026, 1, 1, "4000001", 0, 500, 1),
            M(2026, 1, 1, "1700000", 0, 5000, 1),
        ];
        var sinBanco = CalculoRatios.Calcular(modelo, movs, [], Balance);
        // Sin tratarla como banco, el pasivo corriente es 500 − 3.000 = −2.500: los ratios con él, de divisor o de numerador, no dan valor.
        Assert.Contains("Pasivo corriente es negativo", sinBanco.Ratio("liquidez")!.Motivo);
        Assert.Contains("Pasivo corriente es negativo", sinBanco.Ratio("calidad")!.Motivo);
        Assert.Null(sinBanco.Ratio("calidad")!.Valor);
        var conBanco = CalculoRatios.Calcular(modelo, movs, [], Balance, null, null, new HashSet<string> { "5550000" });
        Assert.Equal(9000m / 500m, conBanco.Ratio("tesoreria")!.Valor);
        Assert.Contains("5550000", conBanco.Ratio("tesoreria")!.Partidas[0].Cuentas);
        Assert.Equal(500m / 5500m, conBanco.Ratio("calidad")!.Valor);
        // Marcada como inversión, la cuenta bancaria es realizable.
        var inversion = CalculoRatios.Calcular(modelo, movs, [], Balance, new HashSet<string> { "5550000" }, null, new HashSet<string> { "5550000" });
        Assert.Equal(6000m / 500m, inversion.Ratio("tesoreria")!.Valor);
        Assert.Equal(9000m / 500m, inversion.Ratio("acida")!.Valor);
    }

    [Fact]
    public void FechaDelBalanceYDefinicionDeEbitda()
    {
        Assert.Equal(new DateTime(2026, 10, 31), CalculoRatios.FechaBalance(new DateTime(2026, 11, 15), false));
        Assert.Equal(new DateTime(2025, 12, 31), CalculoRatios.FechaBalance(new DateTime(2026, 1, 20), false));
        Assert.Equal(new DateTime(2025, 12, 31), CalculoRatios.FechaBalance(new DateTime(2025, 12, 31), true));
        Assert.Equal(new DateTime(2025, 3, 1), CalculoRatios.DesdeResultado(new DateTime(2026, 2, 28)));
        Assert.True(CalculoRatios.EsEbitda("7000001"));
        Assert.True(CalculoRatios.EsEbitda("6400001"));
        foreach (var c in new[] { "6300001", "6810001", "6940001", "7940001", "6620001", "7690001", "5720001" })
            Assert.False(CalculoRatios.EsEbitda(c), c);
        // En Inicio: el EBITDA quita también la regularización y el cierre.
        var movs = new List<MovimientoDia>
        {
            new(new DateTime(2026, 3, 1), "7000001", 0, 0, 1000m), new(new DateTime(2026, 3, 1), "6400001", 0, 300m, 0),
            new(new DateTime(2026, 3, 1), "6810001", 0, 100m, 0), new(new DateTime(2026, 3, 1), "6300001", 0, 50m, 0),
            new(new DateTime(2026, 3, 1), "6960001", 0, 20m, 0), new(new DateTime(2026, 3, 1), "7950001", 0, 0, 10m),
            new(new DateTime(2026, 3, 1), "6650001", 0, 5m, 0), new(new DateTime(2026, 3, 1), "7610001", 0, 0, 7m),
            new(new DateTime(2026, 12, 31), "7000001", 2, 1000m, 0), new(new DateTime(2026, 4, 1), "7000001", 0, 0, 999m),
        };
        Assert.Equal(700m, CalculoInicio.Ebitda(movs, new DateTime(2026, 3, 31)));
        Assert.Equal(700m, CalculoInicio.Ebitda(movs.Take(9), new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void GeometriaDeLaFranjaDeRango()
    {
        // Rango 1,5 a 2,5: escala hasta 2,5 × 1,3 = 3,25.
        var (desde, hasta, valor, referencia) = GeometriaGrafico.Franja(1.5m, 2.5m, false, 2m, 1.3m);
        Assert.Equal(1.5 / 3.25, desde, 6);
        Assert.Equal(2.5 / 3.25, hasta, 6);
        Assert.Equal(2 / 3.25, valor!.Value, 6);
        Assert.Equal(1.3 / 3.25, referencia!.Value, 6);
        // Abierto por arriba: la banda llega al final; un valor grande amplía la escala.
        var abierta = GeometriaGrafico.Franja(6m, null, false, 24m, null);
        Assert.Equal(1, abierta.BandaHasta);
        Assert.Equal(6 / 30.0, abierta.BandaDesde, 6);
        Assert.Equal(24 / 30.0, abierta.Valor!.Value, 6);
        Assert.Null(abierta.Referencia);
        // Menos es mejor: la banda empieza en 0; un valor negativo se queda en el borde; sin valor, sin marca.
        var menos = GeometriaGrafico.Franja(null, 30m, true, -5m, null);
        Assert.Equal(0, menos.BandaDesde);
        Assert.Equal(30 / 48.0, menos.BandaHasta, 6);
        Assert.Equal(0, menos.Valor);
        Assert.Null(GeometriaGrafico.Franja(1m, 2m, false, null, null).Valor);
    }
}
