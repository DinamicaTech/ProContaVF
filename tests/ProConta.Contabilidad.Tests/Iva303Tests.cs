using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo del 303 y de la prorrata (contabilidad/modelos-tributarios y contabilidad/procesos/prorrata), sin BD. El juego de
/// paridad son los totales agregados del libro anterior de la gestoría (AsientosIVA2026-3Tv1, hoja DatosIVA): no hay datos de
/// terceros, solo sumas por trimestre, sentido, deducción, operación y tipo.
/// </summary>
public sealed class Iva303Tests
{
    private static LineaIva L(int trim, SentidoIva sentido, DeduccionIva ded, OperacionIva op, bool isp, decimal tipo, decimal b, decimal cuota) =>
        new(new DateTime(2026, trim * 3, 1), trim, 1000 + trim, sentido, op, isp, tipo, "Raíz", "Centro", "Actividad", ded, "Tercero sintético", null, null, b, cuota,
            sentido == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? cuota : 0m, null, null);

    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;
    private const DeduccionIva D100 = DeduccionIva.Deducible100, NoD = DeduccionIva.NoDeducible, Pro = DeduccionIva.Prorrata, Sin = DeduccionIva.SinCentro;
    private const OperacionIva Nac = OperacionIva.Nacional, Intra = OperacionIva.Intracomunitaria, Imp = OperacionIva.Importacion;

    /// <summary>Totales del libro anterior de 2026 (base y cuota en positivo; «varios» tipos se dan como 21 %, no cambia el cálculo).</summary>
    public static IReadOnlyList<LineaIva> Paridad2026 { get; } =
    [
        L(1, R, D100, Nac, false, 21, 15833.70m, 3325.08m), L(1, R, NoD, Nac, false, 0, 52763.23m, 0m),
        L(1, S, D100, Nac, false, 21, 9161.17m, 1923.84m), L(1, S, NoD, Nac, false, 21, 3786.01m, 749.90m),
        L(1, S, Pro, Nac, false, 0, 58.05m, 0m), L(1, S, Pro, Nac, false, 10, 102.41m, 10.24m), L(1, S, Pro, Nac, false, 21, 2969.84m, 623.68m),
        L(1, S, Pro, Nac, true, 21, 144.62m, 30.37m), L(1, S, Pro, Imp, false, 21, 51.98m, 10.92m),
        L(2, R, D100, Nac, false, 21, 18173.70m, 3816.48m), L(2, R, NoD, Nac, false, 0, 102564.98m, 0m),
        L(2, S, D100, Nac, false, 21, 9572.13m, 2010.15m), L(2, S, NoD, Intra, false, 21, 4396.62m, 923.27m), L(2, S, NoD, Intra, true, 21, 1810.49m, 380.20m),
        L(2, S, NoD, Nac, false, 21, 5841.20m, 1054.38m), L(2, S, Pro, Nac, false, 0, 19.49m, 0m), L(2, S, Pro, Nac, false, 10, 280.83m, 28.09m),
        L(2, S, Pro, Nac, false, 21, 3020.40m, 634.28m), L(2, S, Pro, Nac, true, 21, 90.79m, 19.07m),
        L(3, R, D100, Nac, false, 21, 18281.90m, 3839.20m), L(3, R, NoD, Nac, false, 0, 55394.49m, 0m),
        L(3, S, D100, Nac, false, 21, 33.06m, 6.94m), L(3, S, Sin, Nac, false, 21, 119.72m, 25.14m), L(3, S, NoD, Intra, false, 21, 243.18m, 51.07m),
        L(3, S, NoD, Intra, true, 21, 273.78m, 57.49m), L(3, S, NoD, Nac, false, 21, 4581.19m, 882.97m), L(3, S, Pro, Nac, false, 0, 221.29m, 0m),
        L(3, S, Pro, Nac, false, 10, 488.62m, 48.84m), L(3, S, Pro, Nac, false, 21, 2720.43m, 571.32m),
        L(4, S, Pro, Nac, false, 21, 57.18m, 12.01m),
    ];

    [Fact]
    public void ElResultadoDelLibroAnteriorSeReproduceConProrrataDel15()
    {
        var r = Calculo303.Calcular(Paridad2026, 0.15m);
        // La importación con DUA del 1T (10,92 €) ya no se devenga (modelo-303 2026-11-17 14:38): el libro anterior daba 3.366,37 y 1.341,25.
        Assert.Equal([3355.45m, 5139.02m, 3947.76m], [r.TotalDevengado(1), r.TotalDevengado(2), r.TotalDevengado(3)]);
        Assert.Equal([2025.12m, 2112.37m, 99.96m], [r.TotalDeducible(1), r.TotalDeducible(2), r.TotalDeducible(3)]);
        Assert.Equal([1330.33m, 3026.65m, 3847.80m], [r.Resultado(1), r.Resultado(2), r.Resultado(3)]);
        // El año es la suma de los trimestres ya redondeados.
        Assert.Equal(r.Resultado(1) + r.Resultado(2) + r.Resultado(3) + r.Resultado(4), r.Resultado(0));
        // El redondeo de la prorrata es por casilla y trimestre: interiores corrientes con la ISP nacional (10,24 + 623,68 + 30,37) × 15 % = 99,6435 → 99,64.
        Assert.Equal(1923.84m + 99.64m, r.Linea(Calculo303.ClaveInterioresCorrientes).En(1).Cuota);
        Assert.Equal(99.64m, r.Linea(Calculo303.ClaveInterioresCorrientes).Prorrata(1));
        Assert.Equal(1.64m, r.Linea(Calculo303.ClaveImportacionesCorrientes).En(1).Cuota);
        Assert.Equal(99.64m + 1.64m, r.DeducidoProrrata(1));
        // La base: la de lo 100 % deducible más la de prorrata × 15 %, sin las de tipo 0: 9.161,17 + Redondear(3.216,87 × 15 %).
        Assert.Equal(9161.17m + 482.53m, r.Linea(Calculo303.ClaveInterioresCorrientes).En(1).Base);
        Assert.Equal(25.14m, r.Linea(Calculo303.ClaveSinClasificar).En(3).Cuota);
        Assert.Equal(52763.23m, r.Linea(Calculo303.ClaveExentas).En(1).Base);
    }

    [Fact]
    public void LaProrrataRealDelLibroAnteriorEsEl20YLaRegularizacion99_44()
    {
        var r303 = Calculo303.Calcular(Paridad2026, 0.15m);
        var p = CalculoProrrata.Calcular(Paridad2026, 0.15m, r303);
        Assert.Equal(52289.30m, p.Numerador);
        Assert.Equal(263012.00m, p.Denominador);
        Assert.Equal(0.20m, p.Real);
        Assert.Equal(99.44m, p.Regularizacion(0));
        Assert.Equal(12.01m, p.CuotaSujeta[3]);
        Assert.True(p.ProvisionalInformada);
        // Volumen por actividad: las bases con derecho primero.
        Assert.Equal(DeduccionIva.Deducible100, p.Volumen[0].Deduccion);
        Assert.Equal(p.Denominador, p.Volumen.Sum(v => v.Total));
    }

    [Theory]
    [InlineData(19.88, 20)]
    [InlineData(19.00, 19)]
    [InlineData(19.001, 20)]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    public void LaProrrataRealSeRedondeaAlEnteroSuperiorSinRuidoDecimal(double numerador, int esperado)
    {
        var lineas = new List<LineaIva> { L(1, R, D100, Nac, false, 21, (decimal)numerador, 0m) };
        if (numerador < 100) lineas.Add(L(1, R, NoD, Nac, false, 0, 100m - (decimal)numerador, 0m));
        var p = CalculoProrrata.Calcular(lineas, null, Calculo303.Calcular(lineas, 1m));
        Assert.Equal(esperado / 100m, p.Real);
        Assert.False(p.ProvisionalInformada);
        Assert.Equal(1m, p.Provisional);
        // Ruido decimal en el cociente: no sube un punto.
        Assert.Equal(0.19m, CalculoProrrata.RedondearArriba(0.19000000000000000001m));
    }

    [Fact]
    public void CasosLimiteDelDevengoYLaDeduccion()
    {
        List<LineaIva> lineas =
        [
            L(1, R, NoD, Nac, false, 21, 100m, 21m),     // venta con cuota en una actividad no deducible: devenga
            L(1, S, D100, Intra, false, 21, 200m, 42m),  // intracomunitaria en actividad 100 %: devenga y deduce
            L(1, S, NoD, Nac, true, 21, 300m, 63m),      // ISP nacional en actividad no deducible: devenga, no deduce
            L(1, S, Sin, Nac, false, 21, 400m, 84m),     // sin centro: fuera del cálculo
            L(1, S, Pro, Intra, true, 21, 1000m, 210m),  // intracomunitaria con ISP en prorrata: solo cuenta como intracomunitaria
        ];
        var r = Calculo303.Calcular(lineas, 0.15m);
        Assert.Equal(21m, r.Linea(Calculo303.ClaveRepercutido(21m)).En(1).Cuota);
        Assert.Equal(42m + 210m, r.Linea(Calculo303.ClaveIntracomunitarias).En(1).Cuota);
        Assert.Equal(63m, r.Linea(Calculo303.ClaveIsp).En(1).Cuota);
        Assert.Equal(21m + 42m + 210m + 63m, r.TotalDevengado(1));
        Assert.Equal(42m + 31.5m, r.Linea(Calculo303.ClaveIntracomunitariasCorrientes).En(1).Cuota);
        Assert.Equal(31.5m, r.DeducidoProrrata(1));
        Assert.Equal(0m, r.Linea(Calculo303.ClaveInterioresCorrientes).En(1).Cuota);
        Assert.Equal(42m + 31.5m, r.TotalDeducible(1));
        Assert.Equal(84m, r.Linea(Calculo303.ClaveSinClasificar).En(1).Cuota);
        Assert.Equal(63m, r.Linea(Calculo303.ClaveNoDeducible).En(1).Cuota);
        // Los tipos fijos salen aunque no tengan cuota.
        Assert.Equal(0m, r.Linea(Calculo303.ClaveRepercutido(4m)).En(1).Cuota);
    }

    [Fact]
    public void UnaLineaConFechaFueraDelEjercicioNoEntraEnNingunTrimestre()
    {
        var fuera = L(1, S, D100, Nac, false, 21, 100m, 21m) with { Trimestre = 0 };
        var r = Calculo303.Calcular([fuera], 0.15m);
        Assert.Equal(0m, r.TotalDeducible(0));
    }

    // ---------- clasificación de los registros de los libros ----------

    private static readonly IReadOnlyDictionary<int, ActividadIva> Actividades = new Dictionary<int, ActividadIva>
    {
        [1] = new("GEN", "General", 1, null), [2] = new("LOC", "Locales", 2, "4727"), [3] = new("VIV", "Viviendas", 0, "4726"),
    };

    private static readonly IReadOnlyDictionary<int, CentroIva> Centros = new Dictionary<int, CentroIva>
    {
        [1] = new("EMPRESA", "Suministros"), [2] = new("INMUEBLES", "Local sintético"), [3] = new("INMUEBLES", "Piso sintético"), [9] = new("EMPRESA", "Sin actividad"),
    };

    private static RegistroIvaLeido Reg(SentidoIva s, string? codigo, DateTime fecha, decimal cuota, params ApunteIvaLeido[] apuntes) =>
        new(s, 77, fecha, codigo, false, "Proveedor sintético", "B00000000", "F-1", [new DesgloseIvaLeido(100m, 21m, cuota, 0m)], apuntes);

    [Fact]
    public void ElCentroEsElDelApunteDeIvaYSiNoElDelGasto()
    {
        var conIva = Reg(S, "SOPORTADO_0", new DateTime(2026, 2, 3), 21m,
            new("6290000", 100m, 0m, 2), new("4720021", 21m, 0m, 1), new("4100001", 0m, 121m, null));
        var sinIva = Reg(R, null, new DateTime(2026, 5, 3), 0m, new ApunteIvaLeido("4300001", 100m, 0m, null), new ApunteIvaLeido("7520000", 0m, 100m, 3));
        var l = ClasificadorLineasIva.Clasificar([conIva, sinIva], 2026, Actividades, Centros);
        Assert.Equal(("Suministros", "EMPRESA", DeduccionIva.Prorrata, 1), (l[0].Departamento, l[0].Centro, l[0].Deduccion, l[0].Trimestre));
        Assert.Equal("4720021", l[0].CuentaIva);
        Assert.Equal(("Piso sintético", DeduccionIva.NoDeducible, 2), (l[1].Departamento, l[1].Deduccion, l[1].Trimestre));
    }

    [Fact]
    public void LaOperacionSaleDeLa477YNoDelCodigoDelTipo()
    {
        var fecha = new DateTime(2026, 4, 2);
        // Exterior con 477: intracomunitaria, se marque como SOPORTADO_1 o SOPORTADO_2.
        var intra = Reg(S, "SOPORTADO_2", fecha, 13.9m, new ApunteIvaLeido("6230000", 66.19m, 0m, 2), new ApunteIvaLeido("4720221", 13.9m, 0m, 2), new ApunteIvaLeido("4770221", 0m, 13.9m, 2));
        // Exterior sin 477 (IVA a la 4724): importación.
        var imp = Reg(S, "SOPORTADO_1", fecha, 10.92m, new ApunteIvaLeido("6290000", 42.96m, 0m, 1), new ApunteIvaLeido("4724001", 9.02m, 0m, 1));
        // Interior con 477: inversión del sujeto pasivo.
        var isp = Reg(S, "SOPORTADO_0", fecha, 30.37m, new ApunteIvaLeido("6290000", 144.62m, 0m, 1), new ApunteIvaLeido("4721021", 30.37m, 0m, 1), new ApunteIvaLeido("4771021", 0m, 30.37m, 1));
        var l = ClasificadorLineasIva.Clasificar([intra, imp, isp], 2026, Actividades, Centros);
        Assert.Equal((OperacionIva.Intracomunitaria, false, 13.9m), (l[0].Operacion, l[0].Isp, l[0].CuotaAutorrepercutida));
        Assert.Equal((OperacionIva.Importacion, false), (l[1].Operacion, l[1].Isp));
        Assert.Equal((OperacionIva.Nacional, true), (l[2].Operacion, l[2].Isp));
        Assert.All(l, x => Assert.True(x.Autorrepercutida));
    }

    [Fact]
    public void SinCentroSinActividadOFueraDelEjercicioSalenConAviso()
    {
        var sinCentro = Reg(S, "SOPORTADO_0", new DateTime(2026, 7, 1), 25.14m, new ApunteIvaLeido("6220000", 119.72m, 0m, null), new ApunteIvaLeido("4720021", 25.14m, 0m, null));
        var sinActividad = Reg(S, "SOPORTADO_0", new DateTime(2026, 7, 1), 21m, new ApunteIvaLeido("4720021", 21m, 0m, 9));
        var fuera = Reg(S, "SOPORTADO_0", new DateTime(1900, 1, 1), 21m, new ApunteIvaLeido("4720021", 21m, 0m, 1));
        var l = ClasificadorLineasIva.Clasificar([sinCentro, sinActividad, fuera], 2026, Actividades, Centros);
        var porAviso = l.ToDictionary(x => x.Aviso!);
        Assert.Equal(DeduccionIva.SinCentro, porAviso[TextosIva.AvisoSinCentro].Deduccion);
        Assert.Equal(DeduccionIva.SinCentro, porAviso[TextosIva.AvisoSinActividad].Deduccion);
        Assert.Equal(0, porAviso[TextosIva.AvisoFueraEjercicio].Trimestre);
        Assert.Equal(DeduccionIva.Prorrata, porAviso[TextosIva.AvisoFueraEjercicio].Deduccion);
    }
}
