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
}
