using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Modelos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Modelo 390 (contabilidad/modelos-tributarios/modelo-390) y modelo 202 (modelo-202), sin BD: los totales del 390 cuadran con la suma
/// de los cuatro 303 (también con prorrata y su redondeo por grupo y tipo), resultado anual, volumen y prorrata, páginas y posiciones del
/// fichero; y el pago fraccionado del 202 (18 % de la base, negativa, complementaria, plazos y posiciones). Datos sintéticos.
/// </summary>
public sealed class Modelos390y202Tests
{
    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;
    private const OperacionIva Nac = OperacionIva.Nacional, Intra = OperacionIva.Intracomunitaria, Imp = OperacionIva.Importacion;
    private const DeduccionIva D100 = DeduccionIva.Deducible100, Pro = DeduccionIva.Prorrata, NoD = DeduccionIva.NoDeducible;
    private static readonly Declarante303 Sociedad = new("B00000000", "Sociedad sintética, SL", "ES9121000418450200051332");
    private static readonly DatosEstadisticos390 Datos = new([new Actividad390("ALQUILER SINTETICO", "A01", "8612"), new Actividad390("OTRA ACTIVIDAD", "A03", "845")], "6820",
        Representantes: [new Representante200("00000000T", "PRUEBA SINTETICA, UNO", new DateTime(2000, 1, 2), "NOTARIA")]);
    private const string CuentaBienes = "4720121";

    private static LineaIva L(int trim, SentidoIva sentido, DeduccionIva ded, OperacionIva op, bool isp, decimal tipo, decimal b, decimal cuota, bool inversion = false,
        bool rect = false, string? clave = null, string? cuenta = null) =>
        new(new DateTime(2026, trim * 3, 1), trim, 4000 + trim, sentido, op, isp, tipo, "Raíz", "Centro", "Actividad", ded, "Tercero sintético", null, $"F{trim}", b, cuota,
            sentido == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? cuota : 0m, cuenta, null)
        { BienInversion = inversion, Rectificativa = rect, ClaveOperacion = clave };

    /// <summary>Un año con todo lo que lleva el 390: varios tipos, exentas, prorrata, intracomunitarias de bienes y servicios, ISP, importación, inversión y rectificativas.</summary>
    private static IReadOnlyList<LineaIva> Ano()
    {
        var l = new List<LineaIva>();
        for (var t = 1; t <= 4; t++)
        {
            l.AddRange(
            [
                L(t, R, D100, Nac, false, 21, 10000m + t, 2100.21m + t), L(t, R, D100, Nac, false, 10, 1000m, 100m), L(t, R, D100, Nac, false, 4, 100m, 4m),
                L(t, R, NoD, Nac, false, 0, 30000m + t, 0m), L(t, R, D100, Nac, false, 0, 500m, 0m, clave: "E5"), L(t, R, D100, Nac, false, 0, 250m, 0m, clave: "E2"),
                L(t, R, D100, Nac, false, 21, -50m, -10.50m, rect: true),
                L(t, S, D100, Nac, false, 21, 3000m, 630m), L(t, S, Pro, Nac, false, 21, 1234.57m, 259.26m), L(t, S, Pro, Nac, false, 10, 333.33m, 33.33m),
                L(t, S, NoD, Nac, false, 21, 800m, 168m), L(t, S, Pro, Nac, false, 21, 999.99m, 210m, inversion: true), L(t, S, D100, Nac, false, 0, 77m, 0m),
                L(t, S, Pro, Intra, false, 21, 555.55m, 116.67m), L(t, S, Pro, Intra, false, 21, 111.11m, 23.33m, cuenta: CuentaBienes),
                L(t, S, Pro, Nac, true, 21, 222.22m, 46.67m), L(t, S, D100, Imp, false, 21, 66.66m, 14m),
            ]);
        }
        return l;
    }

    private static (Declaracion390 D, IvaEjercicio Iva, List<Declaracion303> T) Calcular(IReadOnlyList<LineaIva> lineas, decimal prorrata, decimal? definitiva = null)
    {
        var iva = CalculoIvaEjercicio.Calcular(lineas, prorrata, definitiva);
        var t = Enumerable.Range(1, 4).Select(q => Modelo303.Calcular(iva.Modelo303, 2026, q, Sociedad, q == 1 ? 300m : 0m)).ToList();
        return (Modelo390.Calcular(lineas, iva, t, 2026, Sociedad, Datos, l => l.CuentaIva == CuentaBienes), iva, t);
    }

    [Theory]
    [InlineData(0.15, null)]
    [InlineData(0.07, 0.09)]
    [InlineData(1.0, null)]
    public void LosTotalesCuadranConLaSumaDeLosCuatro303(double provisional, double? definitiva)
    {
        var (d, _, t) = Calcular(Ano(), (decimal)provisional, (decimal?)definitiva);
        decimal Suma(int casilla) => t.Sum(x => x.Casilla(casilla));
        Assert.Equal(Suma(27), d.Casilla(34));
        Assert.Equal((Suma(28), Suma(29)), (d.Casilla(48), d.Casilla(49)));
        Assert.Equal((Suma(30), Suma(31)), (d.Casilla(50), d.Casilla(51)));
        Assert.Equal((Suma(32), Suma(33)), (d.Casilla(52), d.Casilla(53)));
        Assert.Equal((Suma(34), Suma(35)), (d.Casilla(54), d.Casilla(55)));
        Assert.Equal((Suma(36), Suma(37)), (d.Casilla(56) + d.Casilla(597), d.Casilla(57) + d.Casilla(598)));
        Assert.Equal((Suma(38), Suma(39)), (d.Casilla(58), d.Casilla(59)));
        Assert.Equal(Suma(44), d.Casilla(522));
        Assert.Equal(Suma(45), d.Casilla(64));
        Assert.Equal(Suma(46), d.Casilla(65));
        Assert.Equal(d.Casilla(65) - d.Casilla(85), d.Casilla(86));
        // Cada total es la suma de sus tipos.
        Assert.Equal(d.Casilla(191) + d.Casilla(604) + d.Casilla(606), d.Casilla(49));
        Assert.Equal(d.Casilla(2) + d.Casilla(4) + d.Casilla(6) + d.Casilla(22) + d.Casilla(24) + d.Casilla(26) + d.Casilla(546) + d.Casilla(548) + d.Casilla(552) + d.Casilla(28) + d.Casilla(30), d.Casilla(34));
        Assert.True(d.Valida, string.Join(" ", d.Errores));
    }

    [Fact]
    public void DevengadoPorTipoYVolumenDeOperaciones()
    {
        var (d, iva, t) = Calcular(Ano(), 0.15m);
        Assert.Equal((40010m, 8400.84m + 10m), (d.Casilla(5), d.Casilla(6)));
        Assert.Equal((4000m, 400m), (d.Casilla(3), d.Casilla(4)));
        Assert.Equal((400m, 16m), (d.Casilla(1), d.Casilla(2)));
        Assert.Equal((444.44m, 93.32m), (d.Casilla(25), d.Casilla(26)));       // intracomunitarias de bienes (por su cuenta de IVA)
        Assert.Equal((2222.20m, 466.68m), (d.Casilla(551), d.Casilla(552)));   // y de servicios
        Assert.Equal((888.88m, 186.68m), (d.Casilla(27), d.Casilla(28)));
        Assert.Equal((-200m, -42m), (d.Casilla(29), d.Casilla(30)));
        Assert.Equal(44210m, d.Casilla(99));                                    // bases con IVA, rectificativas incluidas
        Assert.Equal((2000m, 1000m, 120010m), (d.Casilla(103), d.Casilla(104), d.Casilla(105)));
        Assert.Equal(d.Casilla(99) + d.Casilla(103) + d.Casilla(104) + d.Casilla(105), d.Casilla(108));
        Assert.Equal(308m, d.Casilla(230));
        // Prorrata: operaciones totales, con derecho y porcentaje (sin definitiva, la real).
        Assert.Equal((iva.Prorrata.Denominador, iva.Prorrata.Numerador, iva.Prorrata.Real * 100m), (d.Casilla(115), d.Casilla(116), d.Casilla(118)));
        Assert.Equal("E", d.TipoProrrata);                                       // gastos comunes en prorrata y actividades al 100 % y al 0 %
        Assert.Contains(d.Avisos, a => a.Contains("prorrata del ejercicio no está fijada"));
        // Resultado anual: el crédito del 1T (300) se compensa en el 85; lo ingresado en el año en el 95.
        Assert.Equal(300m, d.Casilla(85));
        Assert.Equal(t.Sum(x => Math.Max(0m, x.Casilla(71))), d.Casilla(95));
        Assert.Equal(100m, d.Casilla(87));
    }

    [Fact]
    public void SinActividadesNoSePuedePresentar()
    {
        var lineas = Ano();
        var iva = CalculoIvaEjercicio.Calcular(lineas, 0.15m, null);
        var t = Enumerable.Range(1, 4).Select(q => Modelo303.Calcular(iva.Modelo303, 2026, q, Sociedad, 0m)).ToList();
        var d = Modelo390.Calcular(lineas, iva, t, 2026, Sociedad, null, _ => false);
        Assert.Contains(d.Errores, e => e.Contains("actividades"));
        Assert.Contains(d.Errores, e => e.Contains("CNAE"));
    }

    [Fact]
    public void ElFicheroDel390TieneLasPaginasDelDiseno()
    {
        var (d, _, _) = Calcular(Ano(), 0.15m);
        var f = Fichero390.Generar(d, ProgramaAeat.Actual);
        var paginas = Fichero390.PaginasPresentadas.Select(c => Diseno390.Paginas.First(p => p.Codigo == c)).ToList();
        Assert.Equal(Fichero390.LongitudCabecera + paginas.Sum(p => p.Longitud) + 18, f.Length);
        Assert.StartsWith("<T390020260A0000><AUX>", f);
        Assert.EndsWith("</T390020260A0000>", f);
        Assert.Equal("B00000000", Fichero390.LeerNif(f));
        var pos = Fichero390.LongitudCabecera;
        foreach (var p in paginas)
        {
            var texto = f.Substring(pos, p.Longitud);
            Assert.StartsWith($"<T390{p.Codigo}>", texto);
            Assert.EndsWith($"</T390{p.Codigo}>", texto);
            pos += p.Longitud;
        }
        string Pagina(string codigo) => f.Substring(f.IndexOf($"<T390{codigo}>", StringComparison.Ordinal), Diseno390.Paginas.First(p => p.Codigo == codigo).Longitud);
        string En(string codigo, int posicion, int largo) => Pagina(codigo).Substring(posicion - 1, largo);
        string Cent(decimal v) => (v < 0m ? "N" : "") + decimal.Round(Math.Abs(v) * 100m, 0).ToString("0").PadLeft(v < 0m ? 16 : 17, '0');
        Assert.Equal("2026", En("01000", 103, 4));
        Assert.Equal("ALQUILER SINTETICO".PadRight(40) + "A018612", En("01000", 149, 47));
        Assert.Equal("0", En("01000", 134, 1));                       // no es sustitutiva
        Assert.Equal(Cent(d.Casilla(5)), En("02000", 217, 17));
        Assert.Equal(Cent(d.Casilla(29)), En("02000", 1509, 17));     // negativa con «N»
        Assert.Equal(Cent(d.Casilla(34)), En("02000", 1628, 17));
        Assert.Equal(Cent(d.Casilla(49)), En("03000", 234, 17));
        Assert.Equal(Cent(d.Casilla(65)), En("04000", 676, 17));
        Assert.Equal(Cent(d.Casilla(86)), En("06000", 98, 17));
        Assert.Equal("10000", En("06000", 115, 5));                   // territorio común 100 %
        Assert.Equal(Cent(d.Casilla(108)), En("06000", 684, 17));
        Assert.Equal("6820", En("07000", 240, 4));
        Assert.Equal("E", En("07000", 278, 1));
        Assert.Equal("PRUEBA SINTETICA, UNO".PadRight(80) + "00000000T02012000NOTARIA     ", En("01000", 646, 109));
        Assert.Equal(new string('0', 17), En("07000", 328, 17));      // la segunda fila de prorrata va a cero
        // Sustitutiva: marca y justificante de la anterior.
        var s = Fichero390.Generar(d with { Sustitutiva = new Sustitutiva390("3900000000001") }, ProgramaAeat.Actual);
        Assert.Equal("1", s.Substring(s.IndexOf("<T39001000>", StringComparison.Ordinal) + 133, 1));
        Assert.Contains("3900000000001", s);
    }

    [Fact]
    public void ProrrataGeneralSiTodoVaEnProrrata()
    {
        var lineas = Ano().Where(l => l.Sentido == SentidoIva.Repercutido || l.Deduccion == Pro).ToList();
        Assert.False(Modelo390.ProrrataEspecial(lineas));
        Assert.True(Modelo390.ProrrataEspecial(Ano()));
        Assert.Equal("G", Calcular(lineas, 0.15m).D.TipoProrrata);
        Assert.Equal(" ", Calcular(Ano().Where(l => l.Deduccion != Pro).ToList(), 1m).D.TipoProrrata);
    }

    [Fact]
    public void ElDe2025VaConSuDiseno()
    {
        var (d26, _, _) = Calcular(Ano(), 0.15m);
        var d = d26 with { Ejercicio = 2025, Casillas = new Dictionary<int, decimal>(d26.Casillas) { [87] = 0m, [92] = 0m, [93] = 0m, [94] = 0m } };
        var f = Fichero390.Generar(d, ProgramaAeat.Actual);
        var paginas = Fichero390.Presentadas(2025).Select(c => Diseno390.Paginas2025.First(p => p.Codigo == c)).ToList();
        Assert.Contains("02B00", Fichero390.Presentadas(2025));
        Assert.DoesNotContain("02B00", Fichero390.Presentadas(2026));
        Assert.Equal(Fichero390.LongitudCabecera + paginas.Sum(p => p.Longitud) + 18, f.Length);
        Assert.StartsWith("<T390020250A0000><AUX>", f);
        string En(string codigo, int posicion, int largo) =>
            f.Substring(f.IndexOf($"<T390{codigo}>", StringComparison.Ordinal), Diseno390.Paginas2025.First(p => p.Codigo == codigo).Longitud).Substring(posicion - 1, largo);
        Assert.Equal("2025", En("01000", 103, 4));
        Assert.Equal("0", En("01000", 133, 1));                       // no es sustitutiva (posición 134 en el diseño de 2026)
        Assert.Equal("PRUEBA SINTETICA, UNO", En("01000", 645, 80).Trim());
        Assert.Equal("682", En("07000", 240, 3));                     // CNAE de 3 posiciones en 2025
        Assert.Equal("E", En("07000", 277, 1));
        Assert.Equal(decimal.Round(d.Casilla(118) * 100m).ToString("00000"), En("07000", 278, 5));
        Assert.Equal(new string('0', 5), En("06000", 98, 5));         // territorio común vacío en 2025
        Assert.Equal(new string('0', 17), En("06000", 123, 17));      // y sus casillas repetidas
    }

    // ---------- 202 ----------

    private static BasePagoFraccionado Is(decimal cuota, decimal retenciones) => new(2024, "asiento del impuesto", cuota, retenciones, ["4730000"]);

    [Fact]
    public void ElPagoFraccionadoEsEl18PorCientoDeLaBase()
    {
        var d = Modelo202.Calcular(2026, 1, Sociedad, Is(27792.30m, 13733.72m), new DatosActividad202("6820"), true, "25");
        Assert.Equal((14058.58m, 0m, 2530.54m), (d.Casilla(1), d.Casilla(2), d.Casilla(3)));
        Assert.Equal('U', d.TipoDeclaracion);
        Assert.True(d.Valida, string.Join(" ", d.Errores));
        Assert.Equal('I', Modelo202.Calcular(2026, 1, Sociedad, Is(27792.30m, 13733.72m), new DatosActividad202("6820"), true, "25", conNrc: true).TipoDeclaracion);
        // Con más retenciones que cuota no hay pago: negativa.
        var n = Modelo202.Calcular(2026, 2, Sociedad, Is(11586.47m, 12408.33m), new DatosActividad202("6820"), true, "25");
        Assert.Equal(('N', 0m, 0m), (n.TipoDeclaracion, n.Casilla(1), n.Casilla(3)));
        Assert.Contains(n.Avisos, a => a.Contains("-821,86"));
        // Sin Impuesto sobre Sociedades de referencia, o sin CNAE, no se puede presentar.
        Assert.Contains(Modelo202.Calcular(2026, 1, Sociedad, null, new DatosActividad202("6820"), true, "25").Errores, e => e.Contains("2024"));
        Assert.Contains(Modelo202.Calcular(2026, 1, Sociedad, Is(100m, 0m), new DatosActividad202("68"), true, "25").Errores, e => e.Contains("CNAE"));
    }

    [Fact]
    public void LaComplementariaDel202RestaLoIngresadoYNoPuedeBajarlo()
    {
        var d = Modelo202.Calcular(2026, 3, Sociedad, Is(20000m, 5000m), new DatosActividad202("6820"), true, "25", new Rectificativa202("2020000000001", 2000m));
        Assert.Equal((15000m, 2000m, 700m, 2700m), (d.Casilla(1), d.Casilla(2), d.Casilla(3), d.Pago));
        Assert.Contains(Modelo202.Calcular(2026, 3, Sociedad, Is(20000m, 5000m), new DatosActividad202("6820"), true, "25", new Rectificativa202("2020000000001", 3000m)).Errores,
            e => e.Contains("rectificación"));
        Assert.Contains(Modelo202.Calcular(2026, 3, Sociedad, Is(20000m, 5000m), new DatosActividad202("6820"), true, "25", new Rectificativa202("2020000000001", 2700m)).Errores,
            e => e.Contains("sin más importe"));
    }

    [Theory]
    [InlineData(1, 2024)]
    [InlineData(2, 2025)]
    [InlineData(3, 2025)]
    public void LaBaseSaleDelUltimoImpuestoPresentado(int periodo, int ejercicioBase) => Assert.Equal(ejercicioBase, Modelo202.EjercicioBase(2026, periodo));

    [Fact]
    public void PlazosDel202()
    {
        Assert.Equal((new DateTime(2026, 4, 1), new DateTime(2026, 4, 20)), Modelo202.Plazo(2026, 1));
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 15)), Modelo202.PlazoDomiciliacion(2026, 2));
        var d = Modelo202.Calcular(2026, 3, Sociedad, Is(20000m, 5000m), new DatosActividad202("6820"), true, "25");
        Assert.Null(Servicio202.FueraDePlazoDomiciliacion(d, new DateTime(2026, 12, 15)));
        Assert.NotNull(Servicio202.FueraDePlazoDomiciliacion(d, new DateTime(2026, 12, 16)));
    }

    [Fact]
    public void ElFicheroDel202TieneLasPosicionesDelDiseno()
    {
        var d = Modelo202.Calcular(2026, 1, Sociedad, Is(27792.30m, 13733.72m), new DatosActividad202("6820"), true, "21/22");
        var f = Fichero202.Generar(d, ProgramaAeat.Actual);
        Assert.Equal(Fichero202.LongitudCabecera + Fichero202.LongitudPagina01 + Fichero202.LongitudPagina02 + 18, f.Length);
        Assert.StartsWith("<T202020261P0000><AUX>", f);
        Assert.EndsWith("</T202020261P0000>", f);
        var p1 = f.Substring(Fichero202.LongitudCabecera, Fichero202.LongitudPagina01);
        string En(string p, int posicion, int largo) => p.Substring(posicion - 1, largo);
        Assert.Equal("<T20201000>", En(p1, 1, 11));
        Assert.Equal("U", En(p1, 13, 1));
        Assert.Equal("B00000000", En(p1, 14, 9));
        Assert.Equal("20261P", En(p1, 104, 6));
        Assert.Equal("01012026", En(p1, 110, 8));
        Assert.Equal("6820", En(p1, 118, 4));
        Assert.Equal("X", En(p1, 130, 1));
        Assert.Equal("21/22".PadRight(15), En(p1, 132, 15));
        Assert.Equal("0A", En(p1, 147, 2));
        Assert.Equal("00000000001405858", En(p1, 149, 17));
        Assert.Equal("00000000000253054", En(p1, 183, 17));
        Assert.Equal("</T20201000>", En(p1, 689, 12));
        var p2 = f.Substring(Fichero202.LongitudCabecera + Fichero202.LongitudPagina01, Fichero202.LongitudPagina02);
        Assert.Equal("<T20202000>", En(p2, 1, 11));
        Assert.Equal(" " + new string(' ', 13), En(p2, 712, 14));
        Assert.Equal("ES9121000418450200051332".PadRight(34), En(p2, 726, 34));
        Assert.Equal("</T20202000>", En(p2, 889, 12));
        Assert.Equal("B00000000", Fichero202.LeerNif(f));
        // Complementaria con NRC: marca, justificante y sin cuenta.
        var c = Modelo202.Calcular(2026, 1, Sociedad, Is(27792.30m, 13733.72m), new DatosActividad202("6820"), true, "25", new Rectificativa202("2020000000001", 1000m), conNrc: true);
        var pc = Fichero202.Generar(c, ProgramaAeat.Actual).Substring(Fichero202.LongitudCabecera + Fichero202.LongitudPagina01, Fichero202.LongitudPagina02);
        Assert.Equal("X2020000000001", En(pc, 712, 14));
        Assert.Equal(new string(' ', 34), En(pc, 726, 34));
    }
}
