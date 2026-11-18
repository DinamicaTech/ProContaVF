using ProConta.Contabilidad.Modelos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Modelos 123 y 193 sin BD (contabilidad/modelos-tributarios/modelo-123 y modelo-193): casillas del 123, complementaria y forma de pago,
/// posiciones de su fichero y registros del 193 con el diseño de 2025. Datos sintéticos.
/// </summary>
public sealed class Modelo123Tests
{
    private static readonly Declarante303 Sociedad = new("B12345674", "Sociedad sintética, SL", "ES9121000418450200051332");

    private static readonly Perceptor193[] Socios =
    [
        new(new PerceptorInformativa("00000000T", "PRUEBA SINTETICA UNO", "08"), 61728.40m, 19m, 11728.40m, "reparto de dividendos del programa", new DateTime(2025, 6, 30)),
        new(new PerceptorInformativa("00000001R", "PRUEBA SINTETICA DOS", "28"), 61728.40m, 19m, 11728.40m, "reparto de dividendos del programa", new DateTime(2025, 6, 30)),
    ];

    [Fact]
    public void LasCasillasSalenDeLosDividendosDelTrimestre()
    {
        var d = Modelo123.Calcular(Socios, 2025, 2, Sociedad);
        Assert.True(d.Valida, string.Join(" ", d.Errores));
        Assert.Equal((2m, 2m, 123456.80m, 123456.80m, 23456.80m, 23456.80m), (d.Casilla(1), d.Casilla(3), d.Casilla(4), d.Casilla(6), d.Casilla(7), d.Casilla(9)));
        Assert.Equal((23456.80m, 0m, 23456.80m), (d.Casilla(12), d.Casilla(13), d.Casilla(14)));
        Assert.Equal((23456.80m, 23456.80m, 'U'), (d.TotalRetenciones, d.Resultado, d.TipoDeclaracion));
        Assert.Equal('I', Modelo123.Calcular(Socios, 2025, 2, Sociedad, conNrc: true).TipoDeclaracion);
        Assert.Empty(d.Avisos);
        Assert.Contains(Modelo123.Calcular([Socios[0] with { Origen = "subcuenta de retención 4751011" }], 2025, 2, Sociedad).Avisos, a => a.Contains("acta del reparto"));
    }

    [Fact]
    public void LaComplementariaSoloSubeElResultado()
    {
        var c = Modelo123.Calcular(Socios, 2025, 2, Sociedad, new RectificativaRetenciones("1232500000001", 20000m));
        Assert.Equal((20000m, 3456.80m), (c.Casilla(13), c.Casilla(14)));
        Assert.Contains(Modelo123.Calcular(Socios, 2025, 2, Sociedad, new RectificativaRetenciones("1232500000001", 30000m)).Errores, e => e.Contains("Una complementaria solo puede subir"));
    }

    [Fact]
    public void ElFicheroDel123SigueSuDiseno()
    {
        var d = Modelo123.Calcular(Socios, 2025, 2, Sociedad, new RectificativaRetenciones("1232500000001", 20000m));
        var f = FicheroRetenciones.Generar(d, ProgramaAeat.Actual);
        Assert.Equal(FicheroRetenciones.LongitudCabecera + FicheroRetenciones.LongitudPagina123 + 18, f.Length);
        Assert.StartsWith("<T123020252T0000><AUX>", f);
        Assert.EndsWith("</T123020252T0000>", f);
        Assert.Equal("B12345674", FicheroRetenciones.LeerNif(f));
        var p = f.Substring(FicheroRetenciones.LongitudCabecera, FicheroRetenciones.LongitudPagina123);
        string En(int posicion, int largo) => p.Substring(posicion - 1, largo);
        Assert.Equal("<T12301000>", En(1, 11));
        Assert.Equal("U", En(13, 1));
        Assert.Equal("SOCIEDAD SINTÉTICA, SL".PadRight(80), En(23, 80));
        Assert.Equal("20252T", En(103, 6));
        Assert.Equal("000000000000002", En(109, 15));                              // [01] número de rentas
        Assert.Equal("00000000012345680", En(154, 17));                            // [04] base
        Assert.Equal("00000000002345680", En(205, 17));                            // [07] retenciones
        Assert.Equal("00000000000345680", En(324, 17));                            // [14] resultado
        Assert.Equal("X1232500000001", En(341, 14));                               // complementaria y justificante anterior
        Assert.Equal("ES9121000418450200051332".PadRight(34), En(355, 34));
        Assert.Equal("</T12301000>", En(589, 12));
    }

    [Fact]
    public void LosRegistrosDel193LlevanLaClaveADeDividendos()
    {
        var d = new Declaracion193(2025, Sociedad, Socios, [], []);
        var (t1, t2) = Modelo193.Registros(d, new ContactoInformativa("930000000", "CONTACTO PRUEBA"), "1932500900001");
        Assert.All(t2.Prepend(t1), r => Assert.Equal(RegistroInformativo.Longitud, r.Length));
        string En(string r, int desde, int hasta) => r.Substring(desde - 1, hasta - desde + 1);
        Assert.Equal("11932025B12345674", En(t1, 1, 17));
        Assert.Equal("000000002", En(t1, 136, 144));                              // perceptores
        Assert.Equal("000000012345680", En(t1, 145, 159));                         // base
        Assert.Equal("000000002345680", En(t1, 160, 174));                         // retenciones
        Assert.Equal(new string('0', 15), En(t1, 175, 189));                       // ingresados a cero con naturaleza «S»
        Assert.Equal("S", En(t1, 235, 235));
        Assert.Equal(2, t2.Count);
        var r = t2[1];
        Assert.Equal("21932025B1234567400000001R", En(r, 1, 26));
        Assert.Equal("PRUEBA SINTETICA DOS", En(r, 36, 75).Trim());
        Assert.Equal("28", En(r, 77, 78));
        Assert.Equal("A02", En(r, 92, 94));                                        // clave A, naturaleza 02 (dividendos)
        Assert.Equal("1", En(r, 122, 122));                                        // dineraria
        Assert.Equal("0000006172840", En(r, 123, 135));
        Assert.Equal("0000006172840", En(r, 152, 164));
        Assert.Equal("1900", En(r, 165, 168));                                     // 19,00 %
        Assert.Equal("0000001172840", En(r, 169, 181));
        Assert.Equal("S", En(r, 208, 208));
        Assert.Equal("0000002", En(r, 315, 321));                                  // número de orden
        Assert.Equal("30062025D", En(r, 331, 339));                                // fecha de devengo y clave de mercado
    }
}
