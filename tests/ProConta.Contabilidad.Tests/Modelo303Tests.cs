using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Modelos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Casillas y fichero del modelo 303 (contabilidad/modelos-tributarios/modelo-303), sin BD. Incluye la validación V6 ampliada: la suma
/// de las casillas da el resultado del cálculo del 303, y cada línea de IVA llega a una sola casilla; y la paridad del 1T al 3T de 2026
/// con los totales del libro anterior (sin la importación con DUA, que ya no se devenga). Datos sintéticos.
/// </summary>
public sealed class Modelo303Tests
{
    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;
    private const OperacionIva Nac = OperacionIva.Nacional, Intra = OperacionIva.Intracomunitaria, Imp = OperacionIva.Importacion;
    private static readonly Declarante303 Sociedad = new("B00000000", "Sociedad sintética, SL", "ES9121000418450200051332");

    private static LineaIva L(int trim, SentidoIva sentido, DeduccionIva ded, OperacionIva op, bool isp, decimal tipo, decimal b, decimal cuota, bool inversion = false,
        bool rect = false, string? clave = null) =>
        new(new DateTime(2026, trim * 3, 1), trim, 3000 + trim, sentido, op, isp, tipo, "Raíz", "Centro", "Actividad", ded, "Tercero sintético", null, null, b, cuota,
            sentido == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? cuota : 0m, null, null)
        { BienInversion = inversion, Rectificativa = rect, ClaveOperacion = clave };

    /// <summary>Todas las combinaciones de operación, ISP, bien de inversión y deducción, con cuotas distintas para que un doble se note.</summary>
    private static IReadOnlyList<LineaIva> Combinaciones()
    {
        var l = new List<LineaIva>
        {
            L(1, R, DeduccionIva.Deducible100, Nac, false, 21, 1000m, 210m), L(1, R, DeduccionIva.NoDeducible, Nac, false, 10, 500m, 50m),
            L(1, R, DeduccionIva.NoDeducible, Nac, false, 4, 100m, 4m), L(1, R, DeduccionIva.NoDeducible, Nac, false, 0, 800m, 0m, clave: "E1"),
            L(1, R, DeduccionIva.Deducible100, Nac, false, 21, -100m, -21m, rect: true), L(1, R, DeduccionIva.Deducible100, Nac, false, 0, 300m, 0m, clave: "E5"),
        };
        var cuota = 1m;
        foreach (var op in new[] { Nac, Intra, Imp })
        foreach (var isp in new[] { false, true })
        foreach (var inv in new[] { false, true })
        foreach (var ded in Enum.GetValues<DeduccionIva>())
        {
            cuota += 1.37m;
            l.Add(L(1, S, ded, op, isp, 21, Math.Round(cuota / 0.21m, 2), cuota, inv));
        }
        return l;
    }

    private static decimal Suma(Declaracion303 d, params int[] casillas) => casillas.Sum(d.Casilla);

    [Theory]
    [InlineData(1)]
    [InlineData(0.15)]
    [InlineData(0.37)]
    public void V6LaSumaDeLasCasillasDaElResultadoDelCalculo(double prorrata)
    {
        var lineas = Combinaciones();
        var r = Calculo303.Calcular(lineas, (decimal)prorrata);
        var d = Modelo303.Calcular(r, 2026, 1, Sociedad, 0m);
        // Devengado: cada cuota en una sola casilla; deducible: cada línea del cálculo en su casilla.
        Assert.Equal(r.TotalDevengado(1), Suma(d, 3, 6, 9, 11, 13, 15));
        Assert.Equal(d.Casilla(27), Suma(d, 3, 6, 9, 11, 13, 15));
        Assert.Equal(r.TotalDeducible(1), Suma(d, 29, 31, 33, 35, 37, 39, 41, 42, 43, 44));
        Assert.Equal(d.Casilla(45), Suma(d, 29, 31, 33, 35, 37, 39, 41, 42, 43, 44));
        Assert.Equal(r.Resultado(1), d.Casilla(46));
        Assert.Equal(d.Casilla(27) - d.Casilla(45), d.Casilla(46));
        Assert.Equal(d.Casilla(46), d.ResultadoLiquidacion);
        Assert.Equal(d.Casilla(46), d.Casilla(71));
        // Las rectificativas emitidas van a la 14/15 y no a su tipo.
        Assert.Equal(-21m, d.Casilla(15));
        Assert.Equal(210m, d.Casilla(9));
        Assert.Equal(300m, d.Casilla(59));
        // Con prorrata del 100 % todo lo deducible de las actividades 100 % y en prorrata se deduce: el deducible es todo el soportado menos lo no deducible y lo sin clasificar.
        if (prorrata == 1)
        {
            var soportado = lineas.Where(Calculo303.EsSoportado).Sum(x => x.Cuota);
            var fuera = r.Linea(Calculo303.ClaveNoDeducible).En(1).Cuota + r.Linea(Calculo303.ClaveSinClasificar).En(1).Cuota;
            Assert.Equal(soportado - fuera, d.Casilla(45));
        }
    }

    [Fact]
    public void CadaSoportadoVaASuCasilla()
    {
        List<LineaIva> lineas =
        [
            L(2, S, DeduccionIva.Deducible100, Nac, false, 21, 100m, 21m),
            L(2, S, DeduccionIva.Deducible100, Nac, true, 21, 200m, 42m),          // ISP nacional: interiores corrientes
            L(2, S, DeduccionIva.Deducible100, Nac, false, 21, 1000m, 210m, inversion: true),
            L(2, S, DeduccionIva.Deducible100, Imp, false, 21, 50m, 10.5m),        // DUA: solo se deduce
            L(2, S, DeduccionIva.Deducible100, Imp, false, 21, 400m, 84m, inversion: true),
            L(2, S, DeduccionIva.Deducible100, Intra, false, 21, 300m, 63m),
            L(2, S, DeduccionIva.Deducible100, Intra, false, 21, 600m, 126m, inversion: true),
        ];
        var d = Modelo303.Calcular(Calculo303.Calcular(lineas, 0.15m), 2026, 2, Sociedad, 0m);
        Assert.Equal((300m, 63m), (d.Casilla(28), d.Casilla(29)));
        Assert.Equal((1000m, 210m), (d.Casilla(30), d.Casilla(31)));
        Assert.Equal((50m, 10.5m), (d.Casilla(32), d.Casilla(33)));
        Assert.Equal((400m, 84m), (d.Casilla(34), d.Casilla(35)));
        Assert.Equal((300m, 63m), (d.Casilla(36), d.Casilla(37)));
        Assert.Equal((600m, 126m), (d.Casilla(38), d.Casilla(39)));
        // Devengado: intracomunitarias e ISP, no la importación.
        Assert.Equal(63m + 126m, d.Casilla(11));
        Assert.Equal(42m, d.Casilla(13));
        Assert.Equal(63m + 126m + 42m, d.Casilla(27));
    }

    [Fact]
    public void LaProrrataSeRedondeaUnaVezPorCasilla()
    {
        // 0,07 × 15 % = 0,0105 por línea; tres líneas en la misma casilla: 0,21 × 15 % = 0,0315 → 0,03 (por línea serían 3 × 0,01).
        var lineas = Enumerable.Range(0, 3).Select(_ => L(1, S, DeduccionIva.Prorrata, Nac, false, 21, 0.33m, 0.07m)).ToList();
        var r = Calculo303.Calcular(lineas, 0.15m);
        Assert.Equal(0.03m, r.Linea(Calculo303.ClaveInterioresCorrientes).En(1).Cuota);
        Assert.Equal(0.03m, r.DeducidoProrrata(1));
        Assert.Equal(0.15m, r.Linea(Calculo303.ClaveInterioresCorrientes).En(1).Base); // 0,99 × 15 % = 0,1485 → 0,15
    }

    [Fact]
    public void ParidadDel1TAl3TDe2026()
    {
        var r = Calculo303.Calcular(Iva303Tests.Paridad2026, 0.15m);
        var resultados = Enumerable.Range(1, 3).Select(t => Modelo303.Calcular(r, 2026, t, Sociedad, 0m)).ToList();
        Assert.Equal([1330.33m, 3026.65m, 3847.80m], resultados.Select(d => d.Casilla(71)));
        var t1 = resultados[0];
        Assert.Equal((15833.70m, 3325.08m), (t1.Casilla(7), t1.Casilla(9)));
        Assert.Equal((144.62m, 30.37m), (t1.Casilla(12), t1.Casilla(13)));
        Assert.Equal(3355.45m, t1.Casilla(27));
        Assert.Equal(1923.84m + 99.64m, t1.Casilla(29));
        Assert.Equal((7.80m, 1.64m), (t1.Casilla(32), t1.Casilla(33))); // DUA de 10,92 € × 15 %
        Assert.Equal(2025.12m, t1.Casilla(45));
    }

    [Fact]
    public void FormaDePagoYCompensacion()
    {
        static Resultado303 Con(decimal repercutido, decimal soportado) => Calculo303.Calcular(
            [L(4, R, DeduccionIva.Deducible100, Nac, false, 21, repercutido / 0.21m, repercutido), L(4, S, DeduccionIva.Deducible100, Nac, false, 21, soportado / 0.21m, soportado)], 1m);
        Assert.Equal('U', Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad, 0m).TipoDeclaracion);
        Assert.Equal('I', Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad, 0m, conNrc: true).TipoDeclaracion);
        // A compensar también en el 4T: nunca devolución.
        Assert.Equal('C', Modelo303.Calcular(Con(100m, 300m), 2026, 4, Sociedad, 0m).TipoDeclaracion);
        Assert.Equal('N', Modelo303.Calcular(Con(100m, 100m), 2026, 4, Sociedad, 0m).TipoDeclaracion);
        // Cuotas a compensar de periodos anteriores: se aplican hasta dejar el resultado a cero.
        var d = Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad, 500m);
        Assert.Equal((500m, 200m, 300m, 0m, 0m), (d.Casilla(110), d.Casilla(78), d.Casilla(87), d.Casilla(69), d.Casilla(71)));
        Assert.Equal('N', d.TipoDeclaracion);
        Assert.Equal(200m, d.ResultadoLiquidacion);
        var parcial = Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad, 50m);
        Assert.Equal((50m, 0m, 150m), (parcial.Casilla(78), parcial.Casilla(87), parcial.Casilla(71)));
        // Sin IBAN no se puede domiciliar; con NRC no hace falta.
        Assert.Contains(Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad with { Iban = null }, 0m).Errores, e => e.Contains("cuenta de domiciliación"));
        Assert.Empty(Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad with { Iban = null }, 0m, conNrc: true).Errores);
        Assert.Contains(Modelo303.Calcular(Con(300m, 100m), 2026, 4, Sociedad with { Iban = "ES0021000418450200051332" }, 0m).Errores, e => e.Contains("no es válido"));
        // El diseño de registro es el de 2026.
        Assert.Contains(Modelo303.Calcular(Con(300m, 100m), 2025, 4, Sociedad, 0m).Errores, e => e.Contains("diseño de registro"));
    }

    [Fact]
    public void LaRectificativaRestaLoIngresadoYDevuelveSiSaleMenos()
    {
        var r = Calculo303.Calcular([L(1, R, DeduccionIva.Deducible100, Nac, false, 21, 1000m, 210m)], 1m);
        var mas = Modelo303.Calcular(r, 2026, 1, Sociedad, 0m, new Rectificativa303("3030000000001", 150m));
        Assert.Equal((150m, 60m, 'U', 0m), (mas.Casilla(70), mas.Casilla(71), mas.TipoDeclaracion, mas.Casilla(111)));
        var menos = Modelo303.Calcular(r, 2026, 1, Sociedad, 0m, new Rectificativa303("3030000000001", 300m));
        Assert.Equal((300m, -90m, 90m, 'N'), (menos.Casilla(70), menos.Casilla(71), menos.Casilla(111), menos.TipoDeclaracion));
    }

    // ---------- fichero ----------

    private static Declaracion303 Ejemplo(char forma = 'U', Rectificativa303? rect = null)
    {
        var r = Calculo303.Calcular(
        [
            L(3, R, DeduccionIva.Deducible100, Nac, false, 21, 18281.90m, 3839.20m), L(3, S, DeduccionIva.Deducible100, Nac, false, 21, 33.06m, 6.94m),
            L(3, R, DeduccionIva.Deducible100, Nac, false, 21, -100m, -21m, rect: true),
        ], 1m);
        return Modelo303.Calcular(r, 2026, 3, Sociedad, 0m, rect, conNrc: forma == 'I');
    }

    [Fact]
    public void ElFicheroTieneLasPaginasYPosicionesDelDiseno2026()
    {
        var d = Ejemplo();
        var f = Fichero303.Generar(d, ProgramaAeat.Actual);
        Assert.Equal(Fichero303.LongitudCabecera + Fichero303.LongitudPagina01 + Fichero303.LongitudPagina03 + Fichero303.LongitudPaginaDid + 18, f.Length);
        Assert.StartsWith("<T303020263T0000><AUX>", f);
        Assert.EndsWith("</T303020263T0000>", f);
        Assert.Equal("0100", f.Substring(92, 4));                    // versión del programa (posición 93)
        Assert.Equal("B62344502", f.Substring(100, 9));              // NIF de la empresa desarrolladora (posición 101)
        var p1 = f.Substring(Fichero303.LongitudCabecera, Fichero303.LongitudPagina01);
        Assert.StartsWith("<T30301000> U", p1);
        Assert.Equal("B00000000", p1.Substring(13, 9));
        Assert.Equal("SOCIEDAD SINTÉTICA, SL", p1.Substring(22, 80).TrimEnd());
        Assert.Equal("20263T", p1.Substring(102, 6));
        Assert.Equal("00000000001828190", p1.Substring(325, 17));      // [07] en la posición 326
        Assert.Equal("02100", p1.Substring(342, 5));                   // [08]
        Assert.Equal("00000000000383920", p1.Substring(347, 17));      // [09]
        Assert.Equal("N0000000000010000", p1.Substring(432, 17));      // [14] negativo
        Assert.Equal("N0000000000002100", p1.Substring(449, 17));      // [15] negativo
        Assert.EndsWith("</T30301000>", p1);
        var p3 = f.Substring(Fichero303.LongitudCabecera + Fichero303.LongitudPagina01, Fichero303.LongitudPagina03);
        Assert.StartsWith("<T30303000>", p3);
        Assert.Equal("10000", p3.Substring(215, 5));                   // [65] 100 %
        Assert.Equal(Fichero303.N(d.Casilla(71)), p3.Substring(407, 17)); // [71]
        var did = f.Substring(Fichero303.LongitudCabecera + Fichero303.LongitudPagina01 + Fichero303.LongitudPagina03, Fichero303.LongitudPaginaDid);
        Assert.StartsWith("<T303DID00>", did);
        Assert.Equal("ES9121000418450200051332", did.Substring(22, 34).Trim());
        Assert.Equal("B00000000", Fichero303.LeerNif(f));
        Assert.DoesNotContain('\n', f);
    }

    [Fact]
    public void ConNrcOAComepensarLaPaginaDidVaSinCuentaYLaRectificativaLlevaElJustificante()
    {
        var f = Fichero303.Generar(Ejemplo('I', new Rectificativa303("3037339500045", 0m)), ProgramaAeat.Actual);
        var p1 = f.Substring(Fichero303.LongitudCabecera, Fichero303.LongitudPagina01);
        Assert.Equal('I', p1[12]);
        var p3 = f.Substring(Fichero303.LongitudCabecera + Fichero303.LongitudPagina01, Fichero303.LongitudPagina03);
        Assert.Equal('X', p3[425]);                                    // rectificativa
        Assert.Equal("3037339500045", p3.Substring(426, 13));
        Assert.Equal('X', p3[457]);                                    // motivo: rectificaciones
        var did = f.Substring(Fichero303.LongitudCabecera + Fichero303.LongitudPagina01 + Fichero303.LongitudPagina03, Fichero303.LongitudPaginaDid);
        Assert.Equal(new string(' ', 34), did.Substring(22, 34));
    }

    [Fact]
    public void FormatoDeLosCampos()
    {
        Assert.Equal("00000000000123456", Fichero303.Num(1234.56m));
        Assert.Equal("N0000000000123456", Fichero303.N(-1234.56m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fichero303.Num(-1m));
        Assert.Equal("10000", Fichero303.Porcentaje(100m));
        Assert.Equal("AÑO & CÍA  ", Fichero303.An("año & cía", 11));
        Assert.Equal("A B", Fichero303.An("a\tb", 3));                  // carácter no admitido → blanco
    }

    // ---------- plazos y avisos ----------

    [Theory]
    [InlineData(1, "2026-04-15", false)]
    [InlineData(1, "2026-04-16", true)]
    [InlineData(4, "2027-01-25", false)]
    [InlineData(4, "2027-01-26", true)]
    public void FueraDelPlazoDeDomiciliacionSePagaConNrc(int trimestre, string hoy, bool fuera)
    {
        var r = Calculo303.Calcular([L(trimestre, R, DeduccionIva.Deducible100, Nac, false, 21, 1000m, 210m)], 1m);
        var d = Modelo303.Calcular(r, 2026, trimestre, Sociedad, 0m);
        Assert.Equal(fuera, Servicio303.FueraDePlazoDomiciliacion(d, DateTime.Parse(hoy, System.Globalization.CultureInfo.InvariantCulture)) is not null);
    }

    [Fact]
    public void AvisaDeLosRegistrosConExpedicionDeUnTrimestreAnterior()
    {
        var l = L(2, S, DeduccionIva.Deducible100, Nac, false, 21, 100m, 21m) with { FechaExpedicion = new DateTime(2026, 3, 20) };
        Assert.Single(Servicio303.ExpedicionAnterior([l, L(2, S, DeduccionIva.Deducible100, Nac, false, 21, 100m, 21m)], 2026, 2));
        Assert.Empty(Servicio303.ExpedicionAnterior([l], 2026, 1));
    }
}
