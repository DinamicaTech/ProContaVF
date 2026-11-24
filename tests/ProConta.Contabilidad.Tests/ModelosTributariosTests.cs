using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Pantalla Contabilidad › Modelos tributarios sin BD (contabilidad/modelos-tributarios): plazos con el paso a día hábil, estados de los
/// periodos, opciones del menú contextual según el estado, deduplicación de la consulta a la AEAT, modelos por defecto y la ventana de
/// conformidad. Datos sintéticos.
/// </summary>
public sealed class ModelosTributariosTests
{
    private static PresentacionGuardada Presentada(string modelo, string periodo, decimal resultado, string? origen = null, string justificante = "3030000000001", string csv = "CSVPRUEBA0000001") =>
        new(1, "T01", modelo, 2026, periodo, "PRUEBAS", new DateTime(2026, 4, 15, 10, 0, 0), csv, null, justificante, null, "U", resultado, resultado, null, null, "operador", null, null, null, origen);

    private static PeriodoModelo Periodo(string modelo, string periodo, Estado303 estado = Estado303.SinPresentar, decimal? calculado = 100m, PresentacionGuardada? ultima = null,
        string? soloConsulta = null)
    {
        var (desde, hasta) = PlazosModelos.Plazo(modelo, 2026, periodo);
        return new PeriodoModelo(modelo, 2026, periodo, desde, hasta, estado, calculado, null, ultima, ultima is null ? [] : [ultima], [], [], soloConsulta);
    }

    [Theory]
    [InlineData("303", "1T", "2026-04-01", "2026-04-20")]
    [InlineData("303", "4T", "2027-01-01", "2027-02-01")]   // 30/1/2027 es sábado: pasa al lunes 1/2
    [InlineData("111", "4T", "2027-01-01", "2027-01-20")]
    [InlineData("115", "3T", "2026-10-01", "2026-10-20")]
    [InlineData("349", "4T", "2027-01-01", "2027-02-01")]
    [InlineData("202", "1P", "2026-04-01", "2026-04-20")]
    [InlineData("202", "3P", "2026-12-01", "2026-12-21")]   // 20/12/2026 es domingo
    [InlineData("390", "0A", "2027-01-01", "2027-02-01")]
    [InlineData("190", "0A", "2027-01-01", "2027-02-01")]   // 31/1/2027 es domingo
    [InlineData("347", "0A", "2027-02-01", "2027-03-01")]   // 28/2/2027 es domingo
    [InlineData("200", "0A", "2027-07-01", "2027-07-26")]   // 25/7/2027 es domingo
    public void LosPlazosPasanAlSiguienteDiaHabil(string modelo, string periodo, string desde, string hasta)
    {
        var (d, h) = PlazosModelos.Plazo(modelo, 2026, periodo);
        Assert.Equal((DateTime.Parse(desde), DateTime.Parse(hasta)), (d, h));
    }

    [Fact]
    public void LosFestivosNacionalesNoSonHabiles()
    {
        Assert.Equal(new DateTime(2026, 4, 5), PlazosModelos.DomingoDePascua(2026));
        Assert.False(PlazosModelos.EsHabil(new DateTime(2026, 4, 3)));     // Viernes Santo
        Assert.False(PlazosModelos.EsHabil(new DateTime(2026, 10, 12)));   // lunes, Fiesta Nacional
        Assert.True(PlazosModelos.EsHabil(new DateTime(2026, 10, 13)));
        Assert.Equal(new DateTime(2026, 12, 9), PlazosModelos.DiaHabil(new DateTime(2026, 12, 8)));   // Inmaculada, martes
        Assert.Equal(["1T", "2T", "3T", "4T"], PlazosModelos.Periodos("123"));
        Assert.Equal(["0A"], PlazosModelos.Periodos("193"));
    }

    [Fact]
    public void ElEstadoDelPeriodoDependeDeHoyYDeLoPresentado()
    {
        var p = Periodo("303", "3T");
        Assert.Equal(EstadoPeriodoModelo.AunNoAbre, p.Estado(new DateTime(2026, 9, 30)));
        Assert.Equal("Abre el 01/10", p.TextoEstado(new DateTime(2026, 9, 30)));
        Assert.Equal(EstadoPeriodoModelo.EnPlazo, p.Estado(new DateTime(2026, 10, 18)));
        Assert.Equal("En plazo · quedan 2 días", p.TextoEstado(new DateTime(2026, 10, 18)));
        Assert.Equal("En plazo · queda 1 día", p.TextoEstado(new DateTime(2026, 10, 19)));
        Assert.Equal("En plazo · termina hoy", p.TextoEstado(new DateTime(2026, 10, 20)));
        Assert.Equal(EstadoPeriodoModelo.Vencido, p.Estado(new DateTime(2026, 10, 21)));
        Assert.Contains("Consultar a la AEAT", p.AyudaEstado(new DateTime(2026, 10, 21)));

        // Sin nada que declarar: las informativas, las retenciones y el 202 no se presentan; el 303 sí.
        Assert.Equal(EstadoPeriodoModelo.SinObligacion, Periodo("349", "3T", calculado: 0m).Estado(new DateTime(2026, 10, 25)));
        Assert.Equal(EstadoPeriodoModelo.SinObligacion, Periodo("115", "3T", calculado: 0m).Estado(new DateTime(2026, 10, 25)));
        Assert.Equal(EstadoPeriodoModelo.Vencido, Periodo("303", "3T", calculado: 0m).Estado(new DateTime(2026, 10, 25)));
        Assert.Equal(EstadoPeriodoModelo.AunNoAbre, Periodo("349", "4T", calculado: 0m).Estado(new DateTime(2026, 10, 25)));

        var presentado = Periodo("303", "3T", Estado303.Presentado, 100m, Presentada("303", "3T", 100m));
        Assert.Equal(EstadoPeriodoModelo.Presentado, presentado.Estado(new DateTime(2026, 11, 18)));
        Assert.Contains("desde el programa", presentado.AyudaEstado(new DateTime(2026, 11, 18)));
        var desactualizado = Periodo("303", "3T", Estado303.Desactualizado, 120m, Presentada("303", "3T", 100m));
        Assert.Equal(EstadoPeriodoModelo.Desactualizado, desactualizado.Estado(new DateTime(2026, 11, 18)));
        Assert.Contains("120,00", desactualizado.AyudaEstado(new DateTime(2026, 11, 18)));
    }

    [Fact]
    public void LoPresentadoPorLaGestoriaNoSeDaPorDesactualizado()
    {
        var externa = Presentada("111", "3T", 0m, PresentacionGuardada.OrigenAeat);
        Assert.True(externa.EsExterna);
        Assert.False(externa.DistintoDe(2984.10m));
        Assert.True(Presentada("111", "3T", 100m).DistintoDe(2984.10m));
        Assert.False(Presentada("111", "3T", 100m, PresentacionGuardada.OrigenPrograma).DistintoDe(100m));
    }

    [Fact]
    public void Las_informativas_se_pueden_registrar_como_presentadas_fuera_del_programa()
    {
        // Propietario 2026-11-24 21:22: la AEAT no deja consultar las informativas de TGVI; se registran a mano.
        var hoy = new DateTime(2026, 11, 24);
        AccionPeriodo? R(PeriodoModelo p) => AccionesPeriodos.De([p], hoy, true).SingleOrDefault(x => x.Accion == AccionModelo.RegistrarExterna);
        Assert.True(R(Periodo("349", "2T"))!.Disponible);
        Assert.Equal("Ya consta presentado", R(Periodo("349", "2T", Estado303.Presentado, 100m, Presentada("349", "2T", 0m, PresentacionGuardada.OrigenAeat)))!.NoSePuede);
        Assert.Equal("El plazo empieza el 01/01/2027", R(Periodo("349", "4T"))!.NoSePuede);
        Assert.Null(R(Periodo("303", "3T")));   // el 303 se trae con «Consultar a la AEAT»

        Assert.Null(ServicioModelosTributarios.ErrorPresentacionExterna(new DateTime(2026, 7, 15), "3492602000001", "abcd1234efgh5678", hoy));
        Assert.Contains("fecha", ServicioModelosTributarios.ErrorPresentacionExterna(null, "3492602000001", "ABCD1234EFGH5678", hoy));
        Assert.Contains("posterior", ServicioModelosTributarios.ErrorPresentacionExterna(hoy.AddDays(1), "3492602000001", "ABCD1234EFGH5678", hoy));
        Assert.Contains("13 cifras", ServicioModelosTributarios.ErrorPresentacionExterna(hoy, "34926", "ABCD1234EFGH5678", hoy));
        Assert.Contains("16 letras", ServicioModelosTributarios.ErrorPresentacionExterna(hoy, "3492602000001", "ABC", hoy));
    }

    [Fact]
    public void ElMenuSeAdaptaAlEstadoDelPeriodo()
    {
        var hoy = new DateTime(2026, 11, 18);
        AccionPeriodo A(IReadOnlyList<AccionPeriodo> l, AccionModelo a) => l.Single(x => x.Accion == a);

        var sinPresentar = AccionesPeriodos.De([Periodo("303", "3T")], hoy, puedePresentar: true);
        Assert.True(A(sinPresentar, AccionModelo.Presentar).Disponible);
        Assert.False(A(sinPresentar, AccionModelo.PresentarComplementaria).Disponible);
        Assert.Equal("No consta ninguna presentación", A(sinPresentar, AccionModelo.AbrirJustificante).NoSePuede);
        Assert.True(A(sinPresentar, AccionModelo.VerBorrador).Disponible);
        Assert.Contains(sinPresentar, a => a.Accion == AccionModelo.AbrirProceso && a.Texto == "Abrir «Liquidación de IVA»");

        var presentado = AccionesPeriodos.De([Periodo("303", "3T", Estado303.Presentado, 100m, Presentada("303", "3T", 100m))], hoy, true);
        Assert.Equal("Ya está presentado; solo admite una complementaria", A(presentado, AccionModelo.Presentar).NoSePuede);
        Assert.True(A(presentado, AccionModelo.PresentarComplementaria).Disponible);
        Assert.True(A(presentado, AccionModelo.CopiarCsv).Disponible);
        Assert.True(A(presentado, AccionModelo.DatosPresentacion).Disponible);

        var gestoria = AccionesPeriodos.De([Periodo("111", "3T", Estado303.Presentado, 100m, Presentada("111", "3T", 0m, PresentacionGuardada.OrigenAeat))], hoy, true);
        Assert.Contains("gestoría", A(gestoria, AccionModelo.PresentarComplementaria).NoSePuede);
        Assert.Contains("Sede", A(gestoria, AccionModelo.AbrirJustificante).NoSePuede);

        var futuro = AccionesPeriodos.De([Periodo("303", "4T")], hoy, true);
        Assert.Equal("El plazo empieza el 01/01/2027", A(futuro, AccionModelo.Presentar).NoSePuede);
        Assert.True(A(futuro, AccionModelo.GenerarFichero).Disponible);       // el fichero provisional sí se puede guardar

        var sinPermiso = AccionesPeriodos.De([Periodo("303", "3T")], hoy, puedePresentar: false);
        Assert.Contains("permiso", A(sinPermiso, AccionModelo.Presentar).NoSePuede);

        var soloConsulta = AccionesPeriodos.De([Periodo("200", "0A", soloConsulta: "Sin diseño")], new DateTime(2027, 7, 10), true);
        Assert.Equal("Sin diseño", A(soloConsulta, AccionModelo.VerBorrador).NoSePuede);
        Assert.Equal("Sin diseño", A(soloConsulta, AccionModelo.Presentar).NoSePuede);

        var informativa = AccionesPeriodos.De([Periodo("347", "0A")], new DateTime(2027, 2, 10), true);
        Assert.Equal("Ver el borrador (vista previa de la AEAT)", A(informativa, AccionModelo.VerBorrador).Texto);
        Assert.Equal("Abrir los registros del 347 de Año", A(informativa, AccionModelo.AbrirCasillas).Texto);

        var varios = AccionesPeriodos.De([Periodo("303", "1T", Estado303.Presentado, 1m, Presentada("303", "1T", 1m)), Periodo("303", "4T")], hoy, true);
        Assert.Equal(["Ver los borradores (1)", "Generar los ficheros… (2)", "Abrir los justificantes (1)", "Presentar…"], varios.Select(a => a.Texto));
        Assert.Contains("de uno en uno", A(varios, AccionModelo.Presentar).NoSePuede);
    }

    [Fact]
    public void LaConsultaNoDuplicaLoQueYaEsta()
    {
        DeclaracionPresentadaAeat D(string periodo, string just, string csv, string modelo = "111", string ejercicio = "2026") =>
            new(ejercicio, modelo, periodo, "B12345674", csv, "EXP", just, null, new DateTime(2026, 10, 13));
        var guardadas = new[] { Presentada("111", "1T", 10m, justificante: "1110000000001", csv: "CSVUNO0000000001") };
        var nuevas = ServicioModelosTributarios.Nuevas(
        [
            D("1T", "1110000000001", "CSVOTRO000000001"),   // mismo justificante
            D("2T", "1110000000099", "CSVUNO0000000001"),   // mismo CSV
            D("3T", "1110000000003", "CSVTRES000000001"),
            D("3T", "1110000000003", "CSVTRES000000001"),   // repetida en la propia respuesta
            D("4T", "1110000000004", "CSVCUAT000000001", ejercicio: "2025"),
            D("4T", "1150000000004", "CSVQUIN000000001", modelo: "115"),
        ], guardadas, 2026, "111");
        Assert.Equal(["3T"], nuevas.Select(n => n.Periodo));
    }

    [Fact]
    public void PorDefectoSalenLosModelosConDatosOPresentaciones()
    {
        var periodos = new Dictionary<string, IReadOnlyList<PeriodoModelo>>
        {
            ["303"] = [Periodo("303", "1T", calculado: 50m)],
            ["111"] = [Periodo("111", "1T", calculado: 0m)],
            ["115"] = [Periodo("115", "1T", Estado303.Presentado, 0m, Presentada("115", "1T", 0m, PresentacionGuardada.OrigenAeat))],
            ["349"] = [Periodo("349", "1T", calculado: null)],
        };
        Assert.Equal(new HashSet<string> { "303", "115" }, ServicioModelosTributarios.PorDefecto(periodos));
    }

    [Fact]
    public async Task LaMemoriaDeCalculosSoloDuraElAmbito()
    {
        var n = 0;
        Task<int> Calcular() => Task.FromResult(++n);
        Assert.Equal(1, await CacheCalculo.ObtenerAsync("x", Calcular));
        Assert.Equal(2, await CacheCalculo.ObtenerAsync("x", Calcular));      // sin ámbito, siempre calcula
        using (CacheCalculo.Abrir())
        {
            Assert.Equal(3, await CacheCalculo.ObtenerAsync("x", Calcular));
            Assert.Equal(3, await CacheCalculo.ObtenerAsync("x", Calcular));  // dentro, una sola vez
            Assert.Equal(4, await CacheCalculo.ObtenerAsync("y", Calcular));
            var leidas = 0;
            ProConta.Datos.Persistencia.EsquemaFisico Leer() { leidas++; return new([]); }
            ProConta.Datos.Persistencia.MemoriaEsquema.Obtener("bd", Leer);
            ProConta.Datos.Persistencia.MemoriaEsquema.Obtener("bd", Leer);
            Assert.Equal(1, leidas);
        }
        Assert.False(CacheCalculo.Activa);
        Assert.Equal(5, await CacheCalculo.ObtenerAsync("x", Calcular));
    }

    [Fact]
    public void LosDoceModelosTienenSuDefinicion()
    {
        Assert.Equal(["303", "111", "115", "123", "349", "202", "390", "190", "180", "193", "347", "200"], DefinicionModelo.Todos.Select(d => d.Id));
        Assert.All(DefinicionModelo.Todos, d => Assert.NotEmpty(PlazosModelos.Periodos(d.Id)));
        Assert.True(DefinicionModelo.De("347").Informativa);
        Assert.False(DefinicionModelo.De("390").ConPago);
        Assert.NotNull(Modelo200Tributario.SoloConsulta(2026));
        Assert.Null(Modelo200Tributario.SoloConsulta(2025));
        Assert.NotNull(Modelo390Tributario.SoloConsulta(2024));
    }

    [Fact]
    public void LaConformidadEnsenaLoQueHayQueRevisar()
    {
        var d = new DeclaracionAeat("115", 2026, "3T", "fichero", "B12345674", "SOCIEDAD SINTETICA SL", 'N', 0m, -10m, null, null, [("Retenciones (03)", "0,00 €")], _ => "B12345674");
        var b = new BorradorAeat(d, EntornoAeat.Pruebas, new ValidacionAeat([1, 2], ["aviso de prueba"], []), "B00000000", null, [], ["sin certificado"], ["un aviso"]);
        var c = ConformidadPresentacion.De(b);
        Assert.True(c.Pruebas);
        Assert.False(c.SePuedePresentar);
        Assert.Contains(c.Filas, f => f.Etiqueta.StartsWith("Declarante") && f.Revisar);       // el NIF leído no es el de la sociedad
        Assert.Contains(c.Filas, f => f.Etiqueta.StartsWith("Presentador") && f.Revisar);
        Assert.Contains(c.Filas, f => f.Valor.Contains("PRUEBAS"));
        Assert.Equal(["sin certificado"], c.Bloqueos);
    }
}
