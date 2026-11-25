using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Impuestos;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Pantalla Contabilidad › Modelos tributarios contra Access (contabilidad/modelos-tributarios, esquema 73): los doce modelos dan sus
/// periodos con el contrato común, «Modelos de la sociedad…» se guarda y «Consultar a la AEAT» guarda lo presentado fuera del programa
/// una sola vez, con origen AEAT y sin dar por desactualizado lo que no tiene importe. Cliente de la AEAT simulado; datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ModelosTributariosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private static readonly DateTime Hoy = new(2026, 11, 18);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private sealed class ClienteSimulado : IClienteAeat
    {
        public List<DeclaracionPresentadaAeat> EnLaAeat { get; } = [];
        public int Consultas { get; private set; }

        public Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken c = default) =>
            Task.FromResult(new ValidacionAeat(Encoding.ASCII.GetBytes("%PDF"), [], []));

        public Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat p, CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<ConsultaAeat> ConsultarAsync(EntornoAeat entorno, X509Certificate2 certificado, string nif, string nombre, string modelo, string ejercicio, string? periodo,
            CancellationToken c = default)
        {
            Consultas++;
            return Task.FromResult(new ConsultaAeat([.. EnLaAeat.Where(p => p.Modelo == modelo && p.Ejercicio == ejercicio)], null));
        }

        public Task<byte[]> DescargarAsync(string url, X509Certificate2? certificado, CancellationToken c = default) => Task.FromResult(Encoding.ASCII.GetBytes("%PDF"));
    }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private static Task<X509Certificate2?> Certificado(string _)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=PRUEBA SINTETICA, SERIALNUMBER=IDCES-00000000T, O=SOCIEDAD SINTETICA, OID.2.5.4.97=VATES-B12345674", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Task.FromResult<X509Certificate2?>(req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1)));
    }

    private ServicioModelosTributarios Servicio(ClienteSimulado cliente)
    {
        var reloj = new RelojFijo(Hoy);
        var presentacion = new ServicioPresentacionAeat(Bd, cliente, Certificado, reloj);
        var tgvi = new ServicioTgvi(Bd, new ClienteTgviHttp(), Certificado, reloj);
        var impuesto = new ServicioImpuestoSociedades(Bd, new MotorAsientosAccess(Bd), new CatalogoCuentasAccess(Bd), null, reloj);
        return new ServicioModelosTributarios(Bd, presentacion, tgvi, impuesto, reloj);
    }

    private async Task SociedadAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET CIF = 'B12345674' WHERE IdSociedad = 'T01'");
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0) await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, EntornoPruebas) VALUES (1, True)");
            else await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = True");
            return true;
        });
    }

    [Fact]
    public async Task LosDoceModelosDanSusPeriodosConElContratoComun()
    {
        await SociedadAsync();
        var s = Servicio(new ClienteSimulado());
        foreach (var m in s.Modelos)
        {
            var periodos = await m.PeriodosAsync(_ctx);
            Assert.Equal(PlazosModelos.Periodos(m.Definicion.Id), periodos.Select(p => p.Periodo));
            Assert.All(periodos, p => Assert.Equal(PlazosModelos.Plazo(m.Definicion.Id, 2026, p.Periodo), (p.PlazoDesde, p.PlazoHasta)));
        }
        var p200 = (await s.Modelo("200").PeriodosAsync(_ctx)).Single();
        Assert.NotNull(p200.SoloConsulta);                                    // el diseño del 200 es el de 2025
        Assert.Equal(EstadoPeriodoModelo.AunNoAbre, p200.Estado(Hoy));
        var t303 = await s.Modelo("303").PeriodosAsync(_ctx);
        Assert.Equal(EstadoPeriodoModelo.Vencido, t303[0].Estado(Hoy));        // sin presentación y con el plazo pasado
        Assert.Equal(EstadoPeriodoModelo.AunNoAbre, t303[3].Estado(Hoy));
    }

    [Fact]
    public async Task LosModelosDeLaSociedadSeGuardan()
    {
        await SociedadAsync();
        var s = Servicio(new ClienteSimulado());
        Assert.Null(await s.ElegidosAsync("T01"));
        Assert.Equal(new HashSet<string> { "303", "390" }, await ServicioModelosTributarios.ParaInicioAsync(Bd, "T01"));
        await s.GuardarElegidosAsync("T01", ["303", "111", "190"]);
        Assert.Equal(new HashSet<string> { "303", "111", "190" }, await s.ElegidosAsync("T01"));
        Assert.Equal(new HashSet<string> { "303", "111", "190" }, await ServicioModelosTributarios.ParaInicioAsync(Bd, "T01"));
        await s.GuardarElegidosAsync("T01", ["115"]);
        Assert.Equal(new HashSet<string> { "115" }, await s.ElegidosAsync("T01"));
    }

    [Fact]
    public async Task LaConsultaGuardaLoDeLaGestoriaUnaSolaVez()
    {
        await SociedadAsync();
        var cliente = new ClienteSimulado();
        cliente.EnLaAeat.Add(new("2026", "111", "3T", "B12345674", "CSVGESTORIA00001", "2026111EXP", "1110000000003", null, new DateTime(2026, 10, 13, 16, 30, 0)));
        cliente.EnLaAeat.Add(new("2026", "115", "3T", "B12345674", "CSVGESTORIA00002", "2026115EXP", "1150000000003", null, null));
        var s = Servicio(cliente);
        Assert.True(await s.TocaConsultaDelDiaAsync("T01"));

        var r = await s.ConsultarAeatAsync(_ctx, ["111", "115", "347"]);
        Assert.Empty(r.Errores);
        Assert.Equal(["111", "115"], r.Nuevas.Select(n => n.Modelo).Order());
        Assert.Equal(["347"], r.SinConsulta);                                   // TGVI no tiene consulta
        Assert.All(r.Nuevas, n => Assert.True(n.EsExterna));
        Assert.Equal(Hoy, r.Nuevas.Single(n => n.Modelo == "115").FechaHora);    // sin fecha de la AEAT, la de ahora
        Assert.False(await s.TocaConsultaDelDiaAsync("T01"));

        var otra = await s.ConsultarAeatAsync(_ctx, ["111", "115"]);
        Assert.Empty(otra.Nuevas);                                               // no duplica
        var guardadas = await s.Presentacion.PresentadasAsync("T01", "111", 2026);
        Assert.Equal(("1110000000003", PresentacionGuardada.OrigenAeat, 0m), (guardadas.Single().Justificante, guardadas.Single().Origen, guardadas.Single().Resultado));

        // El 111 del 3T sale presentado (no desactualizado, aunque no tenga importe) y el menú no ofrece la complementaria desde el programa.
        var t3 = (await s.Modelo("111").PeriodosAsync(_ctx)).Single(p => p.Periodo == "3T");
        Assert.Equal(EstadoPeriodoModelo.Presentado, t3.Estado(Hoy));
        Assert.Contains("gestoría", AccionesPeriodos.De([t3], Hoy, true).Single(a => a.Accion == AccionModelo.PresentarComplementaria).NoSePuede);
    }

    [Fact]
    public async Task Una_informativa_presentada_fuera_se_registra_a_mano_y_sale_presentada()
    {
        // Propietario 2026-11-24 21:22: las informativas de TGVI no se consultan a la AEAT; se registran con su justificante y CSV.
        await SociedadAsync();
        var s = Servicio(new ClienteSimulado());
        var r = await s.RegistrarExternaAsync(_ctx, "349", "2T", new DateTime(2026, 7, 15), "3492602000001", "abcd1234efgh5678", "ronald");
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.True(r.Valor.EsExterna);
        Assert.Equal("ABCD1234EFGH5678", r.Valor.Csv);
        var t2 = (await s.Modelo("349").PeriodosAsync(_ctx)).Single(p => p.Periodo == "2T");
        Assert.Equal(EstadoPeriodoModelo.Presentado, t2.Estado(Hoy));
        Assert.Equal("Leer el justificante (PDF)…", AccionesPeriodos.De([t2], Hoy, true).Single(a => a.Accion == AccionModelo.RegistrarExterna).Texto);

        Assert.False((await s.RegistrarExternaAsync(_ctx, "349", "3T", new DateTime(2026, 10, 15), "3492602000001", "ABCD1234EFGH5678", "ronald")).EsCorrecto); // justificante repetido
        Assert.False((await s.RegistrarExternaAsync(_ctx, "303", "3T", new DateTime(2026, 10, 15), "3030000000009", "ABCD1234EFGH5679", "ronald")).EsCorrecto); // el 303 se consulta
    }

    [Fact]
    public async Task El_justificante_en_PDF_completa_lo_de_la_gestoria_y_se_compara_con_lo_calculado()
    {
        // contabilidad/modelos-tributarios/presentacion 2026-11-25: el PDF del justificante da el importe que la consulta de la AEAT no da.
        await SociedadAsync();
        var cliente = new ClienteSimulado();
        cliente.EnLaAeat.Add(new("2026", "111", "3T", "B12345674", "CSVGESTORIA00001", "2026111EXP", "1110000000003", null, new DateTime(2026, 10, 13, 16, 30, 0)));
        var s = Servicio(cliente);
        await s.ConsultarAeatAsync(_ctx, ["111"]);
        var antes = (await s.PeriodosAsync(_ctx, "111")).Single(p => p.Periodo == "3T");
        Assert.Equal(EstadoPeriodoModelo.Presentado, antes.Estado(Hoy));        // sin importe: no se compara

        // El justificante de la gestoría: 2.860,09 de retenciones; el programa no calcula ninguna (no hay nóminas ni facturas con retención).
        var pdf = JustificanteSintetico.Pdf(JustificanteSintetico.Cabecera("111", "1110000000003", "CSVGESTORIA00001", "13-10-2026 a las 16:30:00", 2860.09m),
            JustificanteSintetico.Hoja(2026, "3T", "B12345674", ("01", "3"), ("02", "11.512,24"), ("03", "2.860,09"), ("28", "2.860,09"), ("30", "2.860,09")));
        var r = await s.RegistrarJustificanteAsync(_ctx, "111", "3T", pdf, "ronald", archivar: false);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.False(r.Valor.Nueva);                                            // completa la traída por la consulta
        Assert.Equal((2860.09m, 2860.09m), (r.Valor.Presentacion.Importe, r.Valor.Presentacion.Resultado));
        Assert.Equal(["03", "28"], r.Valor.Diferencias!.Select(d => d.Casilla));
        var guardada = (await s.Presentacion.PresentadasAsync("T01", "111", 2026)).Single();
        Assert.Equal((2860.09m, 2860.09m, PresentacionGuardada.OrigenAeat), (guardada.Importe, guardada.Resultado, guardada.Origen));
        Assert.Equal(2860.09m, guardada.Casillas["28"]);

        var despues = (await s.PeriodosAsync(_ctx, "111")).Single(p => p.Periodo == "3T");
        Assert.Equal(EstadoPeriodoModelo.Desactualizado, despues.Estado(Hoy));
        Assert.Contains("casilla 28 (suma de retenciones): presentado 2.860,09 €, calculado 0,00 €", despues.AyudaEstado(Hoy));

        // Un 115 que no constaba se crea como presentado fuera del programa, con sus importes; el mismo PDF otra vez no duplica.
        var pdf115 = JustificanteSintetico.Pdf(JustificanteSintetico.Cabecera("115", "1150000000002", "CSVGESTORIA00115", "15-07-2026 a las 09:00:00"),
            JustificanteSintetico.Hoja(2026, "2T", "B12345674", ("01", "1"), ("02", "0,00"), ("03", "0,00")));
        var n = await s.RegistrarJustificanteAsync(_ctx, "115", "2T", pdf115, "ronald");
        Assert.True(n.EsCorrecto, n.Error?.ToString());
        Assert.True(n.Valor.Nueva);
        Assert.Empty(n.Valor.Diferencias!);
        Assert.True((await s.RegistrarJustificanteAsync(_ctx, "115", "2T", pdf115, "ronald")).EsCorrecto);
        Assert.Single(await s.Presentacion.PresentadasAsync("T01", "115", 2026));
        Assert.Equal(EstadoPeriodoModelo.Presentado, (await s.PeriodosAsync(_ctx, "115")).Single(p => p.Periodo == "2T").Estado(Hoy));

        // De otro periodo o que no es un justificante: no registra nada.
        var otro = await s.RegistrarJustificanteAsync(_ctx, "115", "3T", pdf115, "ronald");
        Assert.Equal("Modelos.JustificanteDeOtro", otro.Error!.Codigo);
        Assert.Equal("Modelos.NoEsJustificante", (await s.RegistrarJustificanteAsync(_ctx, "115", "3T", "%PDF-1.4 roto"u8.ToArray(), "ronald")).Error!.Codigo);
        Assert.Single(await s.Presentacion.PresentadasAsync("T01", "115", 2026));
    }
}
