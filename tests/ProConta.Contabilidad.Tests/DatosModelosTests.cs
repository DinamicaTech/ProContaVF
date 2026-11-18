using ProConta.Contabilidad.Impuestos;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Datos que los modelos leen de la BD desde el esquema 72 (contabilidad/modelos-tributarios), sin BD: los datos personales y familiares del
/// 190, el inmueble del 180, el registro de inmuebles del 347 y la línea del 200 de cada ajuste del Impuesto sobre Sociedades. Datos sintéticos.
/// </summary>
public sealed class DatosModelosTests
{
    private static readonly Declarante303 Sociedad = new("B00000000", "Sociedad sintética, SL", "ES9121000418450200051332");
    private static readonly ContactoInformativa Contacto = new("930000000", "Apellido Apellido Nombre");

    private static PercepcionRetencion Pr(ClaseRetencionModelo clase, string perceptor, int mes, decimal din, decimal ret) => new(clase, perceptor, new DateTime(2025, mes, 25), din, ret);

    private static PerceptorInformativa Quien(string k) => new(k switch { "E1" => "00000001R", "E2" => "00000002W", _ => "A58818501" }, $"PERCEPTOR {k}", "08");

    private static readonly DireccionInmueble Direccion = new("CALLE", "SINTETICA", "NUM", "7", null, null, null, null, "BJ", null, null, null, "MUNICIPIO SINTETICO", "08001", "08", "08001");

    private static readonly Inmueble180 Local = new("L1 · Local sintético", 1, "1111102DF1711S0001XM", Direccion);

    [Fact]
    public void LaFamiliaDelCientoNoventaCuentaPorEdadesAlFinalDelAno()
    {
        var f = Familia190.De(
        [
            new('D', 2023), new('D', 2015, PorEntero: false), new('D', 2010, AnioAdopcion: 2024), new('D', 2012, Discapacidad: 2),
            new('A', 1955), new('A', 1945, PorEntero: false), new('A', 1940, Discapacidad: 1),
        ], 2025);
        Assert.Equal((2, 2, 2, 1), (f.Menores3, f.Menores3Entero, f.Resto, f.RestoEntero));       // 2023 y la adopción de 2024 son menores de 3
        Assert.Equal(new[] { 0, 1, 0 }, f.DiscDesc);
        Assert.Equal((1, 1, 2, 1), (f.Asc75, f.Asc75Entero, f.AscMas75, f.AscMas75Entero));
        Assert.Equal(new[] { 1, 0, 0 }, f.DiscAsc);
        Assert.Equal(new[] { 1, 1, 2 }, f.ComputoHijos);                                            // los tres mayores, por orden de nacimiento: 2010, 2012 y 2015 (mitad)
    }

    [Fact]
    public void ElCientoNoventaEscribeLosDatosPersonalesDeLaClaveA()
    {
        var p = new[] { Pr(ClaseRetencionModelo.Trabajo, "E1", 1, 1000m, 100m), Pr(ClaseRetencionModelo.Trabajo, "E2", 2, 1000m, 0m) };
        var datos = new DatosPersonales190(1980, 2, "00000003A", 1, 2, true, [new('D', 2024), new('A', 1940)]);
        var d = Modelo190.Calcular(p, 2025, Sociedad, Quien, new Dictionary<string, string> { ["E2"] = "01" }, k => k == "E1" ? datos : null);
        Assert.Empty(d.Errores);
        var (_, t2) = Modelo190.Registros(d, Contacto, "1902500000001");
        var a = t2.Single(x => x[77] == 'A');
        Assert.Equal(("1980", "2", "00000003A", "1", "2", "0", "1"), (a.Substring(152, 4), a.Substring(156, 1), a.Substring(157, 9), a.Substring(166, 1), a.Substring(167, 1),
            a.Substring(168, 1), a.Substring(169, 1)));
        Assert.Equal(("1", "1", "00", "00"), (a.Substring(222, 1), a.Substring(223, 1), a.Substring(224, 2), a.Substring(226, 2)));   // un descendiente menor de 3, por entero
        Assert.Equal(("0", "0", "1", "1"), (a.Substring(240, 1), a.Substring(241, 1), a.Substring(242, 1), a.Substring(243, 1)));     // un ascendiente de 75 o más
        Assert.Equal("100", a.Substring(250, 3));
        var e = t2.Single(x => x[77] == 'E');
        Assert.Equal(new string('0', 4) + "0", e.Substring(152, 5));                                 // la E no lleva datos personales
    }

    [Fact]
    public void ElCientoNoventaPideElAnoDeNacimientoYElNifDelConyuge()
    {
        var p = new[] { Pr(ClaseRetencionModelo.Trabajo, "E1", 1, 1000m, 100m) };
        var sin = Modelo190.Calcular(p, 2025, Sociedad, Quien, new Dictionary<string, string>(), _ => null);
        Assert.Contains(sin.Errores, e => e.Contains("año de nacimiento") && e.Contains("Datos de los empleados (190)"));
        var conyuge = Modelo190.Calcular(p, 2025, Sociedad, Quien, new Dictionary<string, string>(), _ => new DatosPersonales190(1980, 2));
        Assert.Contains(conyuge.Errores, e => e.Contains("NIF del cónyuge"));
        var sinCp = Modelo190.Calcular(p, 2025, Sociedad, k => Quien(k) with { Provincia = null }, new Dictionary<string, string>(), _ => new DatosPersonales190(1980));
        Assert.Empty(sinCp.Errores);                                                                  // sin código postal avisa y no bloquea
        Assert.Contains(sinCp.Avisos, a => a.Contains("código postal"));
        var sinSubclave = Modelo190.Calcular(p, 2025, Sociedad, Quien, new Dictionary<string, string> { ["E1"] = "" }, _ => null);
        Assert.Contains(sinSubclave.Errores, e => e.Contains("subclave de la clave E"));             // administrador sin subclave: no se adivina
        Assert.DoesNotContain(sinSubclave.Errores, e => e.Contains("año de nacimiento"));
    }

    [Fact]
    public void ElCientoOchentaLlevaElInmuebleDeLasFacturasDeAlquiler()
    {
        var p = new[] { Pr(ClaseRetencionModelo.Arrendamientos, "P9", 2, 1000m, 190m) };
        var d = Modelo180.Calcular(p, 2025, Sociedad, Quien, _ => ([Local], []));
        Assert.True(d.Valida, string.Join(" ", d.Errores));
        var (_, t2) = Modelo180.Registros(d, Contacto, "1802500000001");
        var r = t2.Single();
        Assert.Equal(("1", "1111102DF1711S0001XM", "CALLE", "SINTETICA"), (r.Substring(113, 1), r.Substring(114, 20), r.Substring(134, 5), r.Substring(139, 9)));
        Assert.Equal(("NUM", "00007", "BJ "), (r.Substring(189, 3), r.Substring(192, 5), r.Substring(209, 3)));     // número a cinco cifras (la AEAT no admite blancos)
        Assert.Equal(("08001", "08", "08001"), (r.Substring(315, 5), r.Substring(320, 2), r.Substring(322, 5)));

        var sinInmueble = Modelo180.Calcular(p, 2025, Sociedad, Quien, _ => ([], ["F-7"]));
        Assert.Contains(sinInmueble.Errores, e => e.Contains("F-7") && e.Contains("centro de coste"));
        var incompleto = Modelo180.Calcular(p, 2025, Sociedad, Quien, _ => ([Local with { Situacion = null }], []));
        Assert.Contains(incompleto.Errores, e => e.Contains("la situación") && e.Contains("Inmuebles para los modelos (180 y 347)"));
    }

    private static LineaIva Venta(int trim, decimal b, string nif) =>
        new(new DateTime(2025, trim * 3, 1), trim, 5000 + trim, SentidoIva.Repercutido, OperacionIva.Nacional, false, 21m, "Raíz", "Centro", "Actividad", DeduccionIva.Deducible100,
            "Cliente", nif, $"F{trim}", b, b * 0.21m, 0m, null, null);

    [Fact]
    public void ElTrescientosCuarentaYSieteSeparaElAlquilerDelLocalYLlevaElInmueble()
    {
        var lineas = new[] { Venta(1, 2000m, "B12345674"), Venta(2, 2000m, "B12345674"), Venta(3, 1000m, "B12345674") };
        var arrendamiento = new Arrendamiento347("B12345674", "Cliente", Local, [2420m, 2420m, 0m, 0m]);
        var d = Modelo347.Calcular(lineas, 2025, Sociedad, _ => false, _ => "08001", [arrendamiento]);
        Assert.True(d.Errores.Count == 0, string.Join(" ", d.Errores));
        Assert.Equal(2, d.Declarados.Count);
        Assert.Equal((4840m, true), (d.Declarados.Single(x => x.ArrendamientoLocal).Total, true));
        Assert.Equal(1210m, d.Declarados.Single(x => !x.ArrendamientoLocal).Total);
        var (t1, t2) = Modelo347.Registros(d, Contacto, "3472500000001");
        Assert.Equal(("000000001", " 000000000484000"), (t1.Substring(160, 9), t1.Substring(169, 16)));
        Assert.Equal("X", t2.Single(x => x[75] == 'D' && x[99] == 'X').Substring(99, 1));
        var i = t2.Single(x => x[75] == 'I');
        Assert.Equal((" 000000000484000", "1", "1111102DF1711S0001XM"), (i.Substring(98, 16), i.Substring(114, 1), i.Substring(115, 20)));
        Assert.Equal(("CALLE", "00007"), (i.Substring(140, 5), i.Substring(198, 5)));

        var incompleto = Modelo347.Calcular(lineas, 2025, Sociedad, _ => false, _ => "08001", [arrendamiento with { Inmueble = Local with { Direccion = Direccion with { NombreVia = null } } }]);
        Assert.Contains(incompleto.Errores, e => e.Contains("la vía") && e.Contains("Inmuebles para los modelos (180 y 347)"));
        Assert.Equal((2, 3), (new Arrendamiento347("", "", Local with { Situacion = 3 }, [0m, 0m, 0m, 0m]).Situacion347, new Arrendamiento347("", "", Local with { Situacion = 4 }, [0m, 0m, 0m, 0m]).Situacion347));
    }

    [Theory]
    [InlineData("Exención art. 21 LIS por la venta de participaciones", AjusteImpuestoSociedades.LineaExencionArt21)]
    [InlineData("Dividendos exentos (artículo 21)", AjusteImpuestoSociedades.LineaExencionArt21)]
    [InlineData("Multas y sanciones", AjusteImpuestoSociedades.LineaOtras)]
    [InlineData(null, AjusteImpuestoSociedades.LineaOtras)]
    public void LaLineaDelDoscientosSaleDeLaDescripcionSiNoSeHaElegido(string? descripcion, string linea)
    {
        Assert.Equal(linea, AjusteImpuestoSociedades.LineaPorDescripcion(descripcion));
        Assert.Equal(linea, new AjusteImpuestoSociedades(false, false, descripcion ?? "", 1m).LineaDel200);
        Assert.Equal(AjusteImpuestoSociedades.LineaOtras, new AjusteImpuestoSociedades(false, false, descripcion ?? "", 1m, Linea200: AjusteImpuestoSociedades.LineaOtras).LineaDel200);
    }
}
