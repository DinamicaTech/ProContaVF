using ProConta.Contabilidad.Modelos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Modelo 200 sin BD (contabilidad/modelos-tributarios/modelo-200): casado de subcuentas con casillas del modelo PYMES, totales del
/// balance y la PyG, liquidación desde el cálculo del IS, cuadros de correcciones, límite de gastos financieros, aplicación del
/// resultado, claves de la página 1, forma de declarar y fichero con el diseño de 2025. Datos sintéticos.
/// </summary>
public sealed class Modelo200Tests
{
    private static readonly Declarante303 Declarante = new("B12345674", "Sociedad sintética", "ES9121000418450200051332");
    private static readonly Persona200 Admin = new("00000000T", "PRUEBA SINTETICA, UNO", false, "08");

    private static DatosDeclaracion200 Datos(decimal dividendos = 0m) => new("6820", PersonalFijo: 1m, FinesSociales: true, Telefono: "930000000", Dividendos: dividendos,
        Administradores: [Admin], TitularesReales: [new TitularReal200("00000000T", "PRUEBA SINTETICA, UNO", new DateTime(1970, 1, 1)), new TitularReal200("00000001R", "PRUEBA SINTETICA, DOS")],
        Secretario: Admin, Representantes: [new Representante200("00000000T", "PRUEBA SINTETICA, UNO", new DateTime(2000, 1, 1), "NOTARIA")]);

    private static readonly Persona200[] Socios = [new("00000000T", "PRUEBA SINTETICA, UNO", false, "08", 5000m, 50m), new("00000001R", "PRUEBA SINTETICA, DOS", false, "08", 5000m, 50m)];

    /// <summary>
    /// Capital 10.000; alquileres 30.000 (705), servicios de profesionales 2.000 (623), otros servicios 3.000 (629), amortización 1.000
    /// (681), gastos financieros 100 (669), beneficio por venta de participaciones 5.000 (766) e impuesto 6.000 (630): resultado 22.900.
    /// </summary>
    private static List<SaldoCuenta200> Saldos() =>
    [
        new(Lado200.Activo, "2110001", 50000m), new(Lado200.Activo, "2811001", -1000m), new(Lado200.Activo, "2650001", 600m),
        new(Lado200.Activo, "4300001", 3000m), new(Lado200.Activo, "4730001", 0m), new(Lado200.Activo, "5400001", 2000m), new(Lado200.Activo, "5720001", 6400m),
        new(Lado200.PatrimonioNetoPasivo, "1000000", 10000m), new(Lado200.PatrimonioNetoPasivo, "1130000", 25000m), new(Lado200.PatrimonioNetoPasivo, "1290000", 0m),
        new(Lado200.PatrimonioNetoPasivo, "7050000", 30000m), new(Lado200.PatrimonioNetoPasivo, "6230000", -2000m), new(Lado200.PatrimonioNetoPasivo, "6290000", -3000m),
        new(Lado200.PatrimonioNetoPasivo, "6810000", -1000m), new(Lado200.PatrimonioNetoPasivo, "6690000", -100m), new(Lado200.PatrimonioNetoPasivo, "7660001", 5000m),
        new(Lado200.PatrimonioNetoPasivo, "6300000", -6000m), new(Lado200.PatrimonioNetoPasivo, "1800001", 600m), new(Lado200.PatrimonioNetoPasivo, "4752000", 2400m),
        new(Lado200.PatrimonioNetoPasivo, "4100001", 100m),
        new(Lado200.PerdidasGanancias, "7050000", 30000m), new(Lado200.PerdidasGanancias, "6230000", -2000m), new(Lado200.PerdidasGanancias, "6290000", -3000m),
        new(Lado200.PerdidasGanancias, "6810000", -1000m), new(Lado200.PerdidasGanancias, "6690000", -100m), new(Lado200.PerdidasGanancias, "7660001", 5000m),
        new(Lado200.PerdidasGanancias, "6300000", -6000m),
    ];

    /// <summary>Resultado antes del impuesto 28.900 − exención del art. 21 de 5.000 = base 23.900; cuota 21/22 %; retenciones y pagos fraccionados.</summary>
    private static Liquidacion200 Liquidacion(decimal pago2 = 1000m) =>
        new(28900m, 0m, 23900m, 0.22m, 0.21m, 5000m, 0m, false, 0m, 5000m, 300m, 1200m, 0m, [0m, pago2, 1000m]);

    private static readonly AjusteDeclaracion200[] Ajustes = [new(Correccion200.ExencionTransmisionValoresResidentes, false, false, 5000m, "Exención art. 21")];

    [Theory]
    [InlineData(Lado200.Activo, "2650001", new[] { 133, 126, 101 })]
    [InlineData(Lado200.Activo, "4709000", new[] { 159, 149, 136 })]
    [InlineData(Lado200.Activo, "5510001", new[] { 175, 168, 136 })]
    [InlineData(Lado200.PatrimonioNetoPasivo, "1800030", new[] { 222, 216, 210 })]
    [InlineData(Lado200.PatrimonioNetoPasivo, "1120000", new[] { 193, 191, 186, 185 })]
    [InlineData(Lado200.PatrimonioNetoPasivo, "4750000", new[] { 249, 239, 228 })]
    [InlineData(Lado200.PatrimonioNetoPasivo, "4790000", new[] { 224, 210 })]
    [InlineData(Lado200.PerdidasGanancias, "6230000", new[] { 253, 280, 279 })]
    [InlineData(Lado200.PerdidasGanancias, "7550000", new[] { 268, 266, 265 })]
    [InlineData(Lado200.PerdidasGanancias, "7663000", new[] { 321, 319, 313 })]
    [InlineData(Lado200.PerdidasGanancias, "6000000", new[] { 760, 261, 260 })]
    public void CadaSubcuentaVaASuCasillaYSusEpigrafes(Lado200 lado, string cuenta, int[] cadena) => Assert.Equal(cadena, Modelo200.CadenaDe(lado, cuenta));

    [Fact]
    public void UnaSubcuentaSinCasillaNoTieneCadena() => Assert.Null(Modelo200.CadenaDe(Lado200.Activo, "9990000"));

    [Theory]
    [InlineData("00000000", "00000000T")]
    [InlineData("b-12345674", "B12345674")]
    [InlineData("X0000000", "X0000000T")]
    [InlineData("Y 1234567", "Y1234567X")]
    public void ElNifLlevaSuLetra(string nif, string esperado) => Assert.Equal(esperado, Modelo200.Nif(nif));

    [Fact]
    public void ElTipoDeGravamenVaConSusCuatroCifras()
    {
        Assert.Equal("2122", Modelo200.TipoGravamen(0.22m, 0.21m));
        Assert.Equal("2500", Modelo200.TipoGravamen(0.25m, null));
    }

    [Fact]
    public void BalancePerdidasYGananciasYLiquidacion()
    {
        var d = Modelo200.Calcular(2025, Declarante, Datos(), Saldos(), Ajustes, Liquidacion(), cifraNegociosAnteriorMenor1M: true, socios: Socios);
        Assert.True(d.Valida, string.Join(" | ", d.Errores));
        // Balance: activo = patrimonio neto y pasivo
        Assert.Equal((49000m, 600m, 49600m), (d.Casilla("03000", 111), d.Casilla("03000", 133), d.Casilla("03000", 101)));
        Assert.Equal((2000m, 6400m, 11400m, 61000m), (d.Casilla("04000", 169), d.Casilla("04000", 177), d.Casilla("03000", 136), d.Casilla("04000", 180)));
        Assert.Equal((22900m, 57900m, 600m, 2500m, 61000m), (d.Casilla("05000", 199), d.Casilla("05000", 185), d.Casilla("05000", 222), d.Casilla("06000", 249), d.Casilla("06000", 252)));
        // Pérdidas y ganancias
        Assert.Equal((30000m, -2000m, -3000m, -5000m, -1000m, 24000m), (d.Casilla("07000", 255), d.Casilla("07000", 253), d.Casilla("07000", 254), d.Casilla("07000", 279),
            d.Casilla("07000", 284), d.Casilla("07000", 296)));
        Assert.Equal((-100m, 5000m, 4900m, 28900m, -6000m, 22900m), (d.Casilla("08000", 307), d.Casilla("08000", 321), d.Casilla("08000", 324), d.Casilla("08000", 325),
            d.Casilla("08000", 326), d.Casilla("08000", 500)));
        // Liquidación
        Assert.Equal((22900m, 6000m, 28900m), (d.Casilla("12000", 500), d.Casilla("12000", 301), d.Casilla("12000", 501)));
        Assert.Equal((5000m, 5000m), (d.Casilla("13000", 2183), d.Casilla("13000", 418)));
        Assert.Equal((5000m, 5000m, 5000m), (d.Casilla("20B00", 2302), d.Casilla("26E00", 2986), d.Casilla("26G00", 2302)));
        Assert.Equal((23900m, 23900m, 5000m, 5000m), (d.Casilla("14000", 550), d.Casilla("14000", 552), d.Casilla("14000", 562), d.Casilla("14000", 582)));
        Assert.Equal((300m, 1200m, 1500m, 3500m, 1000m, 1000m, 1500m), (d.Casilla("14B00", 1785), d.Casilla("14B00", 1787), d.Casilla("14B00", 1766),
            d.Casilla("14B00", 599), d.Casilla("14B00", 603), d.Casilla("14B00", 605), d.Casilla("14B00", 1586)));
        Assert.Equal(1500m, d.Resultado);
        Assert.Equal(('U', 1500m), (d.TipoDeclaracion, d.ImporteAIngresar));
        Assert.Equal("2122", d.TipoGravamen);
        Assert.Equal([73, 88], d.Claves);
        // Gastos financieros: deducibles por el millón; el límite del beneficio operativo que sobra queda para los siguientes.
        Assert.Equal((100m, 7500m, 1_000_000m, 100m, 7400m), (d.Casilla("20000", 1248), d.Casilla("20000", 1249), d.Casilla("20000", 2369), d.Casilla("20000", 1256),
            d.Casilla("20000", 3590)));
        // Aplicación del resultado: todo a reservas
        Assert.Equal((22900m, 22900m, 22900m), (d.Casilla("20D00", 650), d.Casilla("20D00", 1522), d.Casilla("20D00", 666)));
        Assert.DoesNotContain("15000", d.Paginas);
    }

    [Fact]
    public void ADevolverSinIbanOSinDatosNoEsValida()
    {
        var d = Modelo200.Calcular(2025, Declarante with { Iban = null }, new DatosDeclaracion200(""), Saldos(), Ajustes, Liquidacion(pago2: 3000m), false);
        Assert.Equal('D', d.TipoDeclaracion);
        Assert.Equal(-500m, d.Resultado);
        Assert.Contains(d.Errores, e => e.Contains("IBAN"));
        Assert.Contains(d.Errores, e => e.Contains("CNAE"));
        Assert.Contains(d.Errores, e => e.Contains("administradores"));
        Assert.Contains(d.Errores, e => e.Contains("titulares reales"));
        Assert.Contains(d.Errores, e => e.Contains("socios"));
        Assert.Equal('R', Modelo200.Calcular(2025, Declarante, Datos() with { RenunciaDevolucion = true }, Saldos(), Ajustes, Liquidacion(3000m), false, socios: Socios).TipoDeclaracion);
    }

    [Fact]
    public void LoQueNoCasaSeDice()
    {
        var saldos = Saldos();
        saldos.Add(new(Lado200.Activo, "9990000", 10m));
        var d = Modelo200.Calcular(2025, Declarante, Datos(), saldos, [], Liquidacion() with { BasesNegativas = 100m }, false, socios: Socios);
        Assert.Contains(d.Errores, e => e.Contains("9990000"));
        Assert.Contains(d.Errores, e => e.Contains("base imponible"));                         // falta el ajuste del art. 21 en el detalle
        Assert.Contains(d.Errores, e => e.Contains("página 15"));
    }

    [Fact]
    public void ElFicheroSigueElDisenoDe2025()
    {
        var d = Modelo200.Calcular(2025, Declarante, Datos(dividendos: 2900m), Saldos(), Ajustes, Liquidacion(), true, socios: Socios);
        Assert.Equal((2900m, 20000m), (d.Casilla("20D00", 656), d.Casilla("20D00", 1522)));
        var f = Fichero200.Generar(d, ProgramaAeat.Actual);
        Assert.StartsWith("<T200020250A0000><AUX>", f, StringComparison.Ordinal);
        Assert.EndsWith("</T200020250A0000>", f, StringComparison.Ordinal);
        Assert.Equal("B12345674", Fichero200.LeerNif(f));
        var paginas = System.Text.RegularExpressions.Regex.Matches(f, "<T200([0-9A-Z]{5})>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["01000", "01B00", "02000", "02B00", "02B00", "03000", "04000", "05000", "06000", "07000", "08000", "12000", "13000", "14000", "14B00", "20000", "20B00", "20D00",
            "26E00", "26G00", "DID00"], paginas);
        var p1 = f.Substring(f.IndexOf("<T20001000>", StringComparison.Ordinal), 627);
        Assert.Equal("U", p1.Substring(12, 1));                                                 // tipo de declaración
        Assert.Equal("20250A", p1.Substring(102, 6));
        Assert.Equal("16820", p1.Substring(124, 5));                                            // tipo de ejercicio y CNAE
        Assert.Equal(('0', '1', '1'), (p1[205], p1[226], p1[234]));                             // claves 00038 (no), 00088 y 00073 (posiciones 206, 227 y 235)
        var p14 = f.Substring(f.IndexOf("<T20014000>", StringComparison.Ordinal), 1180);
        Assert.Contains("2122", p14, StringComparison.Ordinal);
        var bis = f.Split("<T20002B00>");
        Assert.Equal(' ', bis[1][0]);                                                           // la primera, no complementaria
        Assert.Equal('C', bis[2][0]);                                                           // la segunda, con el segundo titular real
    }

    [Fact]
    public void SinDisenoDelEjercicioNoSeGenera()
    {
        var d = Modelo200.Calcular(2026, Declarante, Datos(), Saldos(), Ajustes, Liquidacion(), true, socios: Socios);
        Assert.Throws<InvalidOperationException>(() => Fichero200.Generar(d, ProgramaAeat.Actual));
        Assert.Equal([2025], Fichero200.Ejercicios);
    }

    [Theory]
    [InlineData("Exención art. 21 LIS", Correccion200.ExencionTransmisionValoresResidentes)]
    [InlineData("Ajuste artículo 21.3", Correccion200.ExencionTransmisionValoresResidentes)]
    [InlineData("Multas y sanciones", Correccion200.Otras)]
    [InlineData("Art. 210 de otra ley", Correccion200.Otras)]
    public void LosAjustesDelIsSeClasificanPorSuDescripcion(string descripcion, Correccion200 linea) => Assert.Equal(linea, Servicio200.Linea(descripcion));
}
