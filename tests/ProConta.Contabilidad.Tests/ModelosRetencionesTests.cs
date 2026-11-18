using ProConta.Contabilidad.Modelos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Casillas y ficheros de los modelos 111 y 115 (contabilidad/modelos-tributarios/modelo-111 y modelo-115), sin BD: la suma de las
/// casillas da el total de las retenciones de los documentos y cada documento llega a un solo modelo; perceptores distintos; forma de
/// pago; complementaria; posiciones de los diseños de registro; plazo de domiciliación; bloqueos y cuadre con las 4751 (aviso S4).
/// Datos sintéticos.
/// </summary>
public sealed class ModelosRetencionesTests
{
    private static readonly Declarante303 Sociedad = new("B00000000", "Sociedad sintética, SL", "ES9121000418450200051332");
    private const ClaseRetencionModelo T = ClaseRetencionModelo.Trabajo, P = ClaseRetencionModelo.Profesionales, A = ClaseRetencionModelo.Arrendamientos;

    private static PercepcionRetencion Pr(ClaseRetencionModelo clase, string perceptor, int mes, decimal dinerario, decimal retencion, decimal especie = 0m, decimal ingreso = 0m) =>
        new(clase, perceptor, new DateTime(2026, mes, 25), dinerario, retencion, especie, ingreso, $"Doc {perceptor} {mes}");

    private static IReadOnlyList<PercepcionRetencion> Trimestre1() =>
    [
        Pr(T, "E1", 1, 2630.39m, 876.89m), Pr(T, "E2", 1, 2540.85m, 0m), Pr(T, "E3", 1, 1480.95m, 0m),
        Pr(T, "E1", 2, 2630.39m, 876.89m), Pr(T, "E2", 2, 2540.85m, 0m), Pr(T, "E1", 3, 2000m, 600m, especie: 134.31m, ingreso: 25.52m),
        Pr(P, "B11111111", 2, 1000m, 150m), Pr(P, "B11111111", 3, 500m, 75m), Pr(P, "B22222222", 3, -200m, -30m), Pr(P, "B22222222", 3, 400m, 60m),
        Pr(A, "12345678Z", 1, 1200m, 228m), Pr(A, "12345678Z", 2, 1200m, 228m), Pr(A, "B33333333", 3, 800m, 152m),
    ];

    [Fact]
    public void CasillasDel111ConPerceptoresDistintos()
    {
        var d = Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad);
        Assert.Equal((3m, 2630.39m * 2 + 2540.85m * 2 + 1480.95m + 2000m, 876.89m * 2 + 600m), (d.Casilla(1), d.Casilla(2), d.Casilla(3)));
        Assert.Equal((1m, 134.31m, 25.52m), (d.Casilla(4), d.Casilla(5), d.Casilla(6)));
        Assert.Equal((2m, 1700m, 255m), (d.Casilla(7), d.Casilla(8), d.Casilla(9)));
        Assert.Equal(d.Casilla(3) + d.Casilla(6) + d.Casilla(9), d.Casilla(28));
        Assert.Equal(d.Casilla(28), d.Casilla(30));
        Assert.Equal(0m, d.Casilla(29));
        Assert.All(Enumerable.Range(10, 18), n => Assert.Equal(0m, d.Casilla(n)));
        Assert.Equal('U', d.TipoDeclaracion);
        Assert.True(d.Valida, string.Join(" ", d.Errores));
    }

    [Fact]
    public void CasillasDel115()
    {
        var d = Modelo115.Calcular(Trimestre1(), 2026, 1, Sociedad);
        Assert.Equal((2m, 3200m, 608m, 0m, 608m), (d.Casilla(1), d.Casilla(2), d.Casilla(3), d.Casilla(4), d.Casilla(5)));
        Assert.Equal(608m, d.TotalRetenciones);
        Assert.Equal('U', d.TipoDeclaracion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void LaSumaDeLasCasillasDaElTotalDeLosDocumentosYCadaUnoVaAUnSoloModelo(int semilla)
    {
        var azar = new Random(semilla);
        var clases = new[] { T, P, A };
        var docs = Enumerable.Range(0, 60).Select(i =>
        {
            var clase = clases[azar.Next(3)];
            var importe = Math.Round((decimal)azar.NextDouble() * 3000m, 2);
            var especie = clase == T && azar.Next(4) == 0 ? Math.Round((decimal)azar.NextDouble() * 200m, 2) : 0m;
            return Pr(clase, $"X{azar.Next(8)}", 1 + azar.Next(3), importe, Math.Round(importe * 0.15m, 2), especie, Math.Round(especie * 0.19m, 2));
        }).ToList();
        var m111 = Modelo111.Calcular(docs, 2026, 1, Sociedad);
        var m115 = Modelo115.Calcular(docs, 2026, 1, Sociedad);
        // Las casillas suman lo de los documentos y entre los dos modelos está todo, una sola vez.
        Assert.Equal(docs.Where(d => d.Clase != A).Sum(d => d.Retencion + d.IngresoCuenta), m111.Casilla(28));
        Assert.Equal(docs.Where(d => d.Clase == A).Sum(d => d.Retencion), m115.Casilla(3));
        Assert.Equal(docs.Sum(d => d.Retencion + d.IngresoCuenta), m111.TotalRetenciones + m115.TotalRetenciones);
        Assert.Equal(docs.Where(d => d.Clase != A).Sum(d => d.Dinerario + d.Especie), m111.Casilla(2) + m111.Casilla(5) + m111.Casilla(8));
        Assert.Equal(new[] { 3, 6, 9, 12, 15, 18, 21, 24, 27 }.Sum(m111.Casilla), m111.Casilla(28));
        Assert.Equal(m111.Casilla(28) - m111.Casilla(29), m111.Casilla(30));
        Assert.True(m111.Casilla(1) <= 8 && m111.Casilla(7) <= 8 && m115.Casilla(1) <= 8);
        // El fichero se genera y lleva el NIF de la sociedad.
        Assert.Equal("B00000000", FicheroRetenciones.LeerNif(FicheroRetenciones.Generar(m111, ProgramaAeat.Actual)));
        Assert.Equal("B00000000", FicheroRetenciones.LeerNif(FicheroRetenciones.Generar(m115, ProgramaAeat.Actual)));
    }

    [Fact]
    public void FormaDePagoYCuentaDeDomiciliacion()
    {
        Assert.Equal('U', Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad).TipoDeclaracion);
        Assert.Equal('I', Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad, conNrc: true).TipoDeclaracion);
        var cero = Modelo115.Calcular([], 2026, 1, Sociedad);
        Assert.Equal(('N', 0m, 0m), (cero.TipoDeclaracion, cero.Casilla(1), cero.Resultado));
        Assert.True(cero.Valida);
        var sinIban = Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad with { Iban = null });
        Assert.Contains(sinIban.Errores, e => e.Contains("cuenta de domiciliación"));
        Assert.Empty(Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad with { Iban = null }, conNrc: true).Errores); // con NRC no hace falta
        Assert.Contains(Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad with { Iban = "ES0000000000000000000000" }).Errores, e => e.Contains("no es válido"));
        Assert.Contains(Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad with { Nif = "" }).Errores, e => e.Contains("NIF"));
    }

    [Fact]
    public void LaComplementariaRestaLoDeclaradoYNoPuedeBajarlo()
    {
        var d = Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad, new RectificativaRetenciones("1110000000001", 2000m));
        Assert.Equal((2000m, d.Casilla(28) - 2000m), (d.Casilla(29), d.Casilla(30)));
        Assert.Equal('U', d.TipoDeclaracion);
        var baja = Modelo115.Calcular(Trimestre1(), 2026, 1, Sociedad, new RectificativaRetenciones("1150000000001", 700m));
        Assert.Equal(-92m, baja.Resultado);
        Assert.Contains(baja.Errores, e => e.Contains("rectificación"));
        var igual = Modelo115.Calcular(Trimestre1(), 2026, 1, Sociedad, new RectificativaRetenciones("1150000000001", 608m));
        Assert.Equal(('N', 0m), (igual.TipoDeclaracion, igual.Resultado));
        Assert.Contains(igual.Errores, a => a.Contains("no admite una complementaria sin más importe"));
    }

    [Fact]
    public void LosNegativosNoSeAdmiten()
    {
        var d = Modelo111.Calcular([Pr(P, "B1", 1, -500m, -75m), Pr(T, "E1", 1, 100m, 10m)], 2026, 1, Sociedad);
        Assert.Contains(d.Errores, e => e.Contains("casilla 08"));
        Assert.Contains(d.Errores, e => e.Contains("negativo"));
        Assert.False(d.Valida);
    }

    [Fact]
    public void ElFicheroDel111TieneLasPosicionesDelDiseno()
    {
        var d = Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad);
        var f = FicheroRetenciones.Generar(d, ProgramaAeat.Actual);
        Assert.Equal(FicheroRetenciones.LongitudCabecera + FicheroRetenciones.LongitudPagina111 + 18, f.Length);
        Assert.StartsWith("<T111020261T0000><AUX>", f);
        Assert.Equal("0100", f.Substring(92, 4));
        Assert.Equal("B62344502", f.Substring(100, 9));
        Assert.EndsWith("</T111020261T0000>", f);
        var p = f.Substring(FicheroRetenciones.LongitudCabecera, FicheroRetenciones.LongitudPagina111);
        string En(int posicion, int longitud) => p.Substring(posicion - 1, longitud);
        Assert.Equal("<T11101000>", En(1, 11));
        Assert.Equal("U", En(13, 1));
        Assert.Equal("B00000000", En(14, 9));
        Assert.Equal("SOCIEDAD SINTÉTICA, SL".PadRight(60), En(23, 60));
        Assert.Equal(new string(' ', 20), En(83, 20));
        Assert.Equal("20261T", En(103, 6));
        Assert.Equal("00000003", En(109, 8));
        Assert.Equal(Cent(d.Casilla(2)), En(117, 17));
        Assert.Equal(Cent(d.Casilla(3)), En(134, 17));
        Assert.Equal("00000001", En(151, 8));
        Assert.Equal(Cent(25.52m), En(176, 17));
        Assert.Equal("00000002", En(193, 8));
        Assert.Equal(Cent(255m), En(218, 17));
        Assert.Equal(Cent(d.Casilla(28)), En(487, 17));
        Assert.Equal(Cent(0m), En(504, 17));
        Assert.Equal(Cent(d.Casilla(30)), En(521, 17));
        Assert.Equal(" " + new string(' ', 13), En(538, 14));
        Assert.Equal("ES9121000418450200051332".PadRight(34), En(553, 34));
        Assert.Equal("</T11101000>", En(989, 12));

        // Con NRC no va la cuenta; la complementaria lleva la marca y el justificante.
        var c = Modelo111.Calcular(Trimestre1(), 2026, 1, Sociedad, new RectificativaRetenciones("1110000000001", 100m), conNrc: true);
        var pc = FicheroRetenciones.Generar(c, ProgramaAeat.Actual).Substring(FicheroRetenciones.LongitudCabecera, FicheroRetenciones.LongitudPagina111);
        Assert.Equal("I", pc.Substring(12, 1));
        Assert.Equal("X1110000000001", pc.Substring(537, 14));
        Assert.Equal(Cent(100m), pc.Substring(503, 17));
        Assert.Equal(new string(' ', 34), pc.Substring(552, 34));
    }

    [Fact]
    public void ElFicheroDel115TieneLasPosicionesDelDiseno()
    {
        var d = Modelo115.Calcular(Trimestre1(), 2026, 3, Sociedad, new RectificativaRetenciones("1150000000009", 8m));
        var f = FicheroRetenciones.Generar(d, ProgramaAeat.Actual);
        Assert.Equal(FicheroRetenciones.LongitudCabecera + FicheroRetenciones.LongitudPagina115 + 18, f.Length);
        Assert.StartsWith("<T115020263T0000><AUX>", f);
        var p = f.Substring(FicheroRetenciones.LongitudCabecera, FicheroRetenciones.LongitudPagina115);
        string En(int posicion, int longitud) => p.Substring(posicion - 1, longitud);
        Assert.Equal("<T11501000>", En(1, 11));
        Assert.Equal("U", En(13, 1));
        Assert.Equal("20263T", En(103, 6));
        Assert.Equal("000000000000002", En(109, 15));
        Assert.Equal(Cent(3200m), En(124, 17));
        Assert.Equal(Cent(608m), En(141, 17));
        Assert.Equal(Cent(8m), En(158, 17));
        Assert.Equal(Cent(600m), En(175, 17));
        Assert.Equal("X1150000000009", En(192, 14));
        Assert.Equal("ES9121000418450200051332".PadRight(34), En(206, 34));
        Assert.Equal("</T11501000>", En(489, 12));
        Assert.Equal("B00000000", FicheroRetenciones.LeerNif(f));
        // Negativa: tipo N, todo a cero y sin cuenta.
        var n = FicheroRetenciones.Generar(Modelo115.Calcular([], 2026, 2, Sociedad), ProgramaAeat.Actual).Substring(FicheroRetenciones.LongitudCabecera, FicheroRetenciones.LongitudPagina115);
        Assert.Equal("N", n.Substring(12, 1));
        Assert.Equal(new string('0', 15) + Cent(0m), n.Substring(108, 32));
        Assert.Equal(new string(' ', 34), n.Substring(205, 34));
    }

    private static string Cent(decimal v) => decimal.Round(v * 100m, 0).ToString("0").PadLeft(17, '0');

    [Theory]
    [InlineData(3, "2026-10-01", false)]
    [InlineData(3, "2026-10-15", false)]
    [InlineData(3, "2026-10-16", true)]
    [InlineData(3, "2026-11-18", true)]
    [InlineData(4, "2027-01-15", false)]
    [InlineData(4, "2027-01-16", true)]
    [InlineData(1, "2026-03-31", true)]
    public void FueraDelPlazoDeDomiciliacionSePagaConNrc(int trimestre, string hoy, bool fuera)
    {
        var d = Modelo111.Calcular(Trimestre1(), 2026, trimestre, Sociedad);
        Assert.Equal(fuera, ServicioRetenciones.FueraDePlazoDomiciliacion(d, DateTime.Parse(hoy)) is not null);
        Assert.Null(ServicioRetenciones.FueraDePlazoDomiciliacion(Modelo115.Calcular([], 2026, trimestre, Sociedad), DateTime.Parse(hoy)));
    }

    private static ServicioRetenciones Servicio(string modelo) => new(null!, null!, modelo);

    private static LecturaRetenciones Lectura(IReadOnlyList<PercepcionRetencion> p, IReadOnlyList<ApunteRetencion> apuntes, Dictionary<int, ClaseRetencionModelo> asientos,
        IReadOnlyList<DocumentoRetencionPendiente>? pendientes = null) => new(p, pendientes ?? [], [], apuntes, asientos);

    private static ApunteRetencion Ap(int asiento, string cuenta, int mes, int dia, decimal debe, decimal haber, bool apertura = false) =>
        new(asiento, cuenta, new DateTime(2026, mes, dia), apertura, debe, haber);

    [Fact]
    public void CuadraConLoQueEsperaElAvisoS4()
    {
        // Nóminas del 1T y 2T (asientos 1-6) en la 4751000; el 1T se paga en abril por su importe; alquileres en la 4751001.
        var p = new List<PercepcionRetencion> { Pr(T, "E1", 1, 2000m, 300m), Pr(T, "E1", 2, 2000m, 300m), Pr(T, "E1", 4, 2000m, 300m), Pr(A, "B1", 5, 1000m, 190m) };
        var apuntes = new List<ApunteRetencion>
        {
            Ap(100, "4751000", 1, 1, 0m, 450m, apertura: true), Ap(1, "4751000", 1, 25, 0m, 300m), Ap(2, "4751000", 2, 25, 0m, 300m), Ap(50, "4751000", 1, 20, 450m, 0m),
            Ap(3, "4751000", 4, 25, 0m, 300m), Ap(51, "4751000", 4, 20, 600m, 0m), Ap(7, "4751001", 5, 10, 0m, 190m),
        };
        var asientos = new Dictionary<int, ClaseRetencionModelo> { [1] = T, [2] = T, [3] = T, [7] = A };
        var l = Lectura(p, apuntes, asientos);
        Assert.Empty(Servicio("111").CuadreS4(l, 1));
        Assert.Empty(Servicio("111").CuadreS4(l, 2));
        Assert.Empty(Servicio("115").CuadreS4(l, 2));

        // Un abono manual a la 4751000 en el 2T: el 111 no lo tiene y lo avisa; el 115 no.
        apuntes.Add(Ap(60, "4751000", 6, 30, 0m, 45.50m));
        var aviso = Assert.Single(Servicio("111").CuadreS4(l, 2));
        Assert.Contains("diferencia 45,50 €", aviso);
        Assert.Empty(Servicio("115").CuadreS4(l, 2));

        // Retenciones en una 4751 sin nóminas ni facturas (dividendos): solo se avisa en el 111.
        apuntes.Add(Ap(61, "4751011", 5, 31, 0m, 1000m));
        Assert.Contains(Servicio("111").CuadreS4(l, 2), a => a.Contains("4751011") && a.Contains("123"));
        Assert.DoesNotContain(Servicio("115").CuadreS4(l, 2), a => a.Contains("4751011"));
    }

    [Fact]
    public void ConSubcuentaCompartidaSeComparaLaSumaDeLosDosModelos()
    {
        var p = new List<PercepcionRetencion> { Pr(T, "E1", 1, 2000m, 300m), Pr(A, "B1", 2, 1000m, 190m) };
        var apuntes = new List<ApunteRetencion> { Ap(1, "4751000", 1, 25, 0m, 300m), Ap(2, "4751000", 2, 10, 0m, 190m) };
        var l = Lectura(p, apuntes, new() { [1] = T, [2] = A });
        Assert.Empty(Servicio("111").CuadreS4(l, 1));
        Assert.Empty(Servicio("115").CuadreS4(l, 1));
    }

    [Fact]
    public void BloqueanLasFacturasConRetencionSinClaseOSinContabilizar()
    {
        var pendientes = new List<DocumentoRetencionPendiente>
        {
            new(null, 1, "F-1 del 10/02/2026", false, 30m), new(P, 1, "F-2 del 11/02/2026", true, 15m), new(A, 2, "F-3 del 01/04/2026", true, 19m),
        };
        var l = Lectura([], [], [], pendientes);
        var b111 = Servicio("111").Bloqueos(l, 1);
        Assert.Equal(2, b111.Count);
        Assert.Contains(b111, b => b.Contains("F-1") && b.Contains("tipo de retención"));
        Assert.Contains(b111, b => b.Contains("F-2") && b.Contains("sin contabilizar"));
        var b115 = Servicio("115").Bloqueos(l, 1);
        Assert.Contains("F-1", Assert.Single(b115));
        Assert.Contains("F-3", Assert.Single(Servicio("115").Bloqueos(l, 2)));
        Assert.Empty(Servicio("111").Bloqueos(l, 2));
    }

    [Fact]
    public void LaNominaReparteLaRetencionEntreDinerarioYEspecie()
    {
        // Bruto 3.000 con 1.000 de cuota de autónomo pagada por la empresa (especie) y 100 de descuento por especie.
        Assert.Equal((1900m, 393.10m, 1000m, 206.90m), ServicioRetenciones.DesgloseNomina(3000m, -100m, 1000m, 0m, 600m));
        // Con ingreso a cuenta propio, ese es el de la especie.
        Assert.Equal((1900m, 500m, 1000m, 100m), ServicioRetenciones.DesgloseNomina(3000m, -100m, 1000m, 100m, 600m));
        // Sin especie todo es dinerario; la suma de 03 y 06 es siempre la retención de la nómina.
        Assert.Equal((1200m, 0m, 0m, 0m), ServicioRetenciones.DesgloseNomina(1200m, 0m, 0m, 0m, 0m));
        var (_, ret, _, ing) = ServicioRetenciones.DesgloseNomina(2537.19m, -44.77m, 1411.11m, 0m, 124.73m);
        Assert.Equal(124.73m, ret + ing);
    }
}
