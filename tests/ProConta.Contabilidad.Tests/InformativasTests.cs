using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Declaraciones informativas (contabilidad/modelos-tributarios/modelo-349, modelo-347, modelo-190, modelo-180 y modelo-193) y la pieza
/// de TGVI Online, sin BD: formato de los registros de 500 posiciones, el 349 cuadra con la casilla 10 del 303, el 347 aplica el umbral y
/// las exclusiones, el 190 y el 180 cuadran con la suma de los cuatro 111 y 115, número identificativo y bloques. Datos sintéticos.
/// </summary>
public sealed class InformativasTests
{
    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;
    private static readonly Declarante303 Sociedad = new("B00000000", "Sociedad sintética, SL", "ES9121000418450200051332");
    private static readonly ContactoInformativa Contacto = new("93 000 00 00", "Apellido Apellido Nombre");

    private static LineaIva L(int trim, SentidoIva sentido, OperacionIva op, decimal b, decimal cuota, string nif, string tercero, bool isp = false, string? clave = null,
        string? cuenta = null, int asiento = 0) =>
        new(new DateTime(2026, trim * 3, 1), trim, asiento == 0 ? 5000 + trim : asiento, sentido, op, isp, 21m, "Raíz", "Centro", "Actividad", DeduccionIva.Deducible100, tercero, nif,
            $"F{trim}", b, cuota, sentido == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? cuota : 0m, cuenta, null)
        { ClaveOperacion = clave };

    [Fact]
    public void ElRegistroInformativoLimpiaYColocaLosCampos()
    {
        Assert.Equal("CAFE ÑANDU GMBH KALTETECHNIK", RegistroInformativo.Limpiar("  Café Ñandú GmbH  Kältetechnik "));
        var r = new RegistroInformativo().Texto(1, "2").An(5, 10, "Ángel").Num(11, 15, 42).Importe(16, 25, 1234.5m).ImporteConSigno(26, 36, -7.25m).Nif(40, "b-1234567-4").ToString();
        Assert.Equal(500, r.Length);
        Assert.Equal("ANGEL ", r.Substring(4, 6));
        Assert.Equal("00042", r.Substring(10, 5));
        Assert.Equal("0000123450", r.Substring(15, 10));
        Assert.Equal("N0000000725", r.Substring(25, 11));
        Assert.Equal("B12345674", r.Substring(39, 9));
        Assert.Equal(930000000L, Contacto.TelefonoNumerico);
    }

    [Fact]
    public void El349AgrupaPorOperadorYClaveYCuadraConEl303()
    {
        var lineas = new List<LineaIva>
        {
            L(2, S, OperacionIva.Intracomunitaria, 100m, 21m, "IE1234567T", "Plataforma Uno"), L(2, S, OperacionIva.Intracomunitaria, 50.55m, 10.62m, "1234567T", "Plataforma Uno", asiento: 7001),
            L(2, S, OperacionIva.Intracomunitaria, 30m, 6.30m, "DE123456789", "Proveedor de bienes", cuenta: "4720121"),
            L(2, R, OperacionIva.Nacional, 400m, 0m, "FR12345678901", "Cliente UE", clave: "E5"),
            L(2, S, OperacionIva.Nacional, 1000m, 210m, "B12345674", "Nacional"),
            L(1, S, OperacionIva.Intracomunitaria, 999m, 209.79m, "NL123456789B01", "Otro trimestre"),
        };
        var r303 = Calculo303.Calcular(lineas, 1m);
        var d = Modelo349.Calcular(lineas, r303, 2026, 2, Sociedad, l => l.CuentaIva == "4720121", l => l.IdAsiento == 7001 ? "IE" : null);
        Assert.True(d.Valida, string.Join(" ", d.Errores));
        Assert.Equal(3, d.Operadores.Count);
        Assert.Equal(("IE", "1234567T", 'I', 150.55m), (d.Operadores.Single(o => o.Clave == 'I').Pais, d.Operadores.Single(o => o.Clave == 'I').Numero, 'I', d.Operadores.Single(o => o.Clave == 'I').Base));
        Assert.Equal(30m, d.Operadores.Single(o => o.Clave == 'A').Base);
        Assert.Equal(400m, d.Operadores.Single(o => o.Clave == 'E').Base);
        Assert.Empty(d.Avisos); // A + I = casilla 10 del 303 y E = casilla 59
        Assert.Equal(r303.Linea(Calculo303.ClaveIntracomunitarias).En(2).Base, d.Operadores.Where(o => o.Clave is 'A' or 'I').Sum(o => o.Base));

        var (t1, t2) = Modelo349.Registros(d, Contacto, "3492602000001");
        Assert.Equal(500, t1.Length);
        Assert.Equal("1349", t1[..4]);
        Assert.Equal("2026", t1.Substring(4, 4));
        Assert.Equal("B00000000", t1.Substring(8, 9));
        Assert.Equal("T", t1.Substring(57, 1));
        Assert.Equal("930000000", t1.Substring(58, 9));
        Assert.Equal("3492602000001", t1.Substring(107, 13));
        Assert.Equal("2T", t1.Substring(135, 2));
        Assert.Equal("000000003", t1.Substring(137, 9));
        Assert.Equal("000000000058055", t1.Substring(146, 15));
        var i = t2.Single(x => x[132] == 'I');
        Assert.Equal(("IE", "1234567T".PadRight(15), "PLATAFORMA UNO".PadRight(40), "0000000015055"),
            (i.Substring(75, 2), i.Substring(77, 15), i.Substring(92, 40), i.Substring(133, 13)));
        Assert.All(t2, x => Assert.Equal(500, x.Length));
    }

    [Fact]
    public void El349SinPaisDeLaUeNoSeGenera()
    {
        var lineas = new List<LineaIva> { L(2, S, OperacionIva.Intracomunitaria, 100m, 21m, "9827384L", "Sin país") };
        var d = Modelo349.Calcular(lineas, Calculo303.Calcular(lineas, 1m), 2026, 2, Sociedad, _ => false, _ => "ES");
        Assert.Contains(d.Errores, e => e.Contains("NIF comunitario"));
    }

    [Fact]
    public void El347SumaElAnoConIvaYAplicaUmbralYExclusiones()
    {
        var lineas = new List<LineaIva>
        {
            L(1, R, OperacionIva.Nacional, 1000m, 210m, "B12345674", "Cliente A"), L(3, R, OperacionIva.Nacional, 2000m, 420m, "B12345674", "Cliente A"),
            L(1, R, OperacionIva.Nacional, 2000m, 420m, "A58818501", "Cliente pequeño"),
            L(2, S, OperacionIva.Nacional, 3000m, 630m, "00000001R", "Proveedor persona"),
            L(2, S, OperacionIva.Nacional, 5000m, 1050m, "B87654321", "Con retención", asiento: 9999),
            L(2, S, OperacionIva.Nacional, 4000m, 840m, "B76543210", "ISP nacional", isp: true),
            L(2, S, OperacionIva.Intracomunitaria, 9000m, 1890m, "IE1234567T", "Intracomunitario"),
            L(2, R, OperacionIva.Nacional, 9000m, 0m, "PASAPORTE1", "Extranjero"),
        };
        var d = Modelo347.Calcular(lineas, 2026, Sociedad, l => l.IdAsiento == 9999, nif => nif == "00000001R" ? null : "08001");
        Assert.Equal(new[] { "00000001R", "B12345674", "B76543210" }, d.Declarados.Select(x => x.Nif).Order(StringComparer.Ordinal));
        var a = d.Declarados.Single(x => x.Nif == "B12345674");
        Assert.Equal(('B', 3630m, 1210m, 0m, 2420m, 0m), (a.Clave, a.Total, a.Trimestres[0], a.Trimestres[1], a.Trimestres[2], a.Trimestres[3]));
        Assert.Equal((4000m, true), (d.Declarados.Single(x => x.Nif == "B76543210").Total, d.Declarados.Single(x => x.Nif == "B76543210").InversionSujetoPasivo));
        Assert.Contains(d.Errores, e => e.Contains("código postal"));        // el proveedor persona no tiene código postal
        Assert.Contains(d.Avisos, a => a.Contains("sin NIF español"));
        var ok = d with { Errores = [], Declarados = [.. d.Declarados.Select(x => x with { Provincia = "08" })] };
        var (t1, t2) = Modelo347.Registros(ok, Contacto, "3472600000001");
        Assert.Equal("000000003", t1.Substring(135, 9));
        Assert.Equal(" " + (ok.Total * 100m).ToString("0").PadLeft(15, '0'), t1.Substring(144, 16));
        var r = t2.Single(x => x.Substring(17, 9) == "B12345674");
        Assert.Equal(("D", "08", "B", " 000000000363000"), (r.Substring(75, 1), r.Substring(76, 2), r.Substring(81, 1), r.Substring(82, 16)));
        Assert.Equal((" 000000000121000", " 000000000000000", " 000000000242000"), (r.Substring(135, 16), r.Substring(167, 16), r.Substring(199, 16)));
        Assert.Equal("X", t2.Single(x => x.Substring(17, 9) == "B76543210").Substring(281, 1));
    }

    private static PercepcionRetencion Pr(ClaseRetencionModelo clase, string perceptor, int mes, decimal din, decimal ret, decimal esp = 0m, decimal ing = 0m) =>
        new(clase, perceptor, new DateTime(2026, mes, 25), din, ret, esp, ing);

    [Fact]
    public void El190YEl180CuadranConLosCuatro111Y115()
    {
        var p = new List<PercepcionRetencion>
        {
            Pr(ClaseRetencionModelo.Trabajo, "E1", 1, 2000m, 300m, 500m, 90m), Pr(ClaseRetencionModelo.Trabajo, "E1", 5, 2000m, 300m, 500m, 90m),
            Pr(ClaseRetencionModelo.Trabajo, "E2", 8, 1500m, 0m), Pr(ClaseRetencionModelo.Profesionales, "P7", 11, 1000m, 150m),
            Pr(ClaseRetencionModelo.Arrendamientos, "P9", 2, 1200m, 228m), Pr(ClaseRetencionModelo.Arrendamientos, "P9", 12, 1200m, 228m),
        };
        PerceptorInformativa Quien(string k) => new(k switch { "E1" => "00000001R", "E2" => "00000002W", "P7" => "B12345674", _ => "A58818501" }, $"PERCEPTOR {k}", "08");
        var d190 = Modelo190.Calcular(p, 2026, Sociedad, Quien, new Dictionary<string, string> { ["E1"] = "04" });
        var suma111 = Enumerable.Range(1, 4).Sum(t => Modelo111.Calcular(p.Where(x => (x.Fecha.Month + 2) / 3 == t), 2026, t, Sociedad).Casilla(28));
        Assert.Equal(suma111, d190.TotalRetenciones);
        Assert.Equal(3, d190.Percepciones.Count);
        Assert.Equal(('E', "04", 4000m, 600m, 1000m, 180m), (d190.Percepciones[1].Clave, d190.Percepciones[1].Subclave, d190.Percepciones[1].Dinerario,
            d190.Percepciones[1].Retenciones, d190.Percepciones[1].Especie, d190.Percepciones[1].IngresosCuenta));
        Assert.Contains(d190.Errores, e => e.Contains("datos personales"));        // A y E piden datos que el programa no guarda
        var (t1, t2) = Modelo190.Registros(d190, Contacto, "1902600000001");
        Assert.Equal("000000003", t1.Substring(135, 9));
        Assert.Equal((suma111 * 100m).ToString("0").PadLeft(15, '0'), t1.Substring(160, 15));
        var g = t2.Single(x => x[77] == 'G');
        Assert.Equal(("01", " 0000000100000", "0000000015000"), (g.Substring(78, 2), g.Substring(80, 14), g.Substring(94, 13)));

        var d180 = Modelo180.Calcular(p, 2026, Sociedad, Quien);
        var suma115 = Enumerable.Range(1, 4).Sum(t => Modelo115.Calcular(p.Where(x => (x.Fecha.Month + 2) / 3 == t), 2026, t, Sociedad).Casilla(3));
        Assert.Equal(suma115, d180.TotalRetenciones);
        Assert.Equal((2400m, 456m, 19m), (d180.Arrendadores.Single().Base, d180.Arrendadores.Single().Retenciones, d180.Arrendadores.Single().Porcentaje));
        Assert.Contains(d180.Errores, e => e.Contains("inmueble"));
    }

    [Theory]
    [InlineData("349", 2026, "2T", 900001, "3492602900001")]
    [InlineData("347", 2025, "0A", 1, "3472500000001")]
    [InlineData("190", 2026, "0A", 12, "1902600000012")]
    public void NumeroIdentificativoDeLaDeclaracion(string modelo, int ejercicio, string periodo, int secuencial, string esperado) =>
        Assert.Equal(esperado, ServicioTgvi.NumeroJustificante(modelo, ejercicio, periodo, secuencial));

    [Fact]
    public void LosRegistrosVanEnBloquesDe40000()
    {
        var t2 = Enumerable.Range(0, 40_001).Select(_ => new string('2', 500)).ToList();
        var d = new DeclaracionInformativa("347", 2026, "0A", "B00000000", "S", "1347" + new string(' ', 4) + "B00000000", t2, "3472600000001", 0m, []);
        Assert.Equal(2, d.Bloques.Count);
        Assert.Equal(40_000 * 500, d.Bloques[0].Length);
        Assert.Equal(500, d.Bloques[1].Length);
        Assert.Equal("B00000000", d.NifLeido);
        Assert.Equal("https://prewww1.aeat.es/wlpl/OVPT-NTGV/EnviarDatos", ServiciosTgvi.Operacion(EntornoAeat.Pruebas, "EnviarDatos"));
        Assert.True(ServiciosAeat.EsDelEntorno(ServiciosTgvi.Operacion(EntornoAeat.Produccion, "PresentarEnvio"), EntornoAeat.Produccion));
        Assert.False(ServiciosAeat.EsDelEntorno(ServiciosTgvi.Operacion(EntornoAeat.Pruebas, "PresentarEnvio"), EntornoAeat.Produccion));
    }
}
