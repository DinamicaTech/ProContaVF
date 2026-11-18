using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Pieza común de TGVI Online contra Access (contabilidad/modelos-tributarios/presentacion) con un cliente de la AEAT simulado: el
/// envío se inicializa con el tipo 1 y los tipo 2 van en orden, los erróneos se recuperan y no impiden presentar los correctos, sin
/// conformidad no se presenta, la presentación se guarda en PresentacionesAeat con su CSV, expediente y número identificativo y el PDF
/// archivado, el número siguiente cambia, y la baja queda anotada. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class TgviAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private readonly string _carpeta = BdPruebas.CarpetaTemporal();

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private sealed class ClienteSimulado : IClienteTgvi
    {
        public List<string> Operaciones { get; } = [];
        public int Erroneos { get; set; }

        private static RespuestaTgvi R(int codigo, Dictionary<string, string> c, string cuerpo = "") =>
            new(codigo, codigo == 0 ? "Operación realizada correctamente" : "Error", c, Encoding.Latin1.GetBytes(cuerpo));

        public Task<RespuestaTgvi> InicializarAsync(EntornoAeat entorno, X509Certificate2 certificado, string modelo, string ejercicio, string periodo, string ndc, int numBloques,
            string registroTipo1, CancellationToken c = default)
        {
            Operaciones.Add($"Inicializar {modelo} {ejercicio} {periodo} {ndc} {numBloques} {registroTipo1.Length}");
            return Task.FromResult(R(0, new() { ["idenvio"] = $"{modelo}{ejercicio}{periodo}202611180000000001", ["estado"] = "CREADO", ["sigbloque"] = "1" }));
        }

        public Task<RespuestaTgvi> EnviarDatosAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, int numBloque, string bloque, CancellationToken c = default)
        {
            Operaciones.Add($"EnviarDatos {numBloque} {bloque.Length / 500}");
            var total = bloque.Length / 500;
            return Task.FromResult(R(0, new() { ["estado"] = "FINALIZADO", ["totalt2ok"] = (total - Erroneos).ToString(), ["totalt2ko"] = Erroneos.ToString(), ["avisos"] = "N" }));
        }

        public Task<RespuestaTgvi> VistaPreviaAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, CancellationToken c = default)
        {
            Operaciones.Add("VistaPrevia");
            return Task.FromResult(R(0, [], "%PDF vista previa"));
        }

        public Task<RespuestaTgvi> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, string firNif, string firNombre, CancellationToken c = default)
        {
            Operaciones.Add($"Presentar {firNif}");
            return Task.FromResult(R(0, new() { ["estado"] = "PRESENTADO", ["csv"] = "CSVTGVIPRUEBA001", ["expediente"] = "2026349000000000000001", ["numeroregistros"] = "2" }));
        }

        public Task<RespuestaTgvi> RecuperarAsync(EntornoAeat entorno, X509Certificate2 certificado, string operacion, string idEnvio, CancellationToken c = default)
        {
            Operaciones.Add(operacion);
            return Task.FromResult(R(0, [], "2349...;(2) 20501 - Contenido incorrecto en NIF del Declarado.\r\n"));
        }

        public Task<RespuestaTgvi> BajaAsync(EntornoAeat entorno, X509Certificate2 certificado, string modelo, string ejercicio, string periodo, string ndc, string expediente, string firNif,
            string firNombre, CancellationToken c = default)
        {
            Operaciones.Add($"Baja {expediente}");
            return Task.FromResult(R(0, new() { ["expediente"] = "2026349000000000000099" }));
        }

        public Task<byte[]> DescargarAsync(string url, X509Certificate2? certificado, CancellationToken c = default)
        {
            Operaciones.Add("Descargar " + url);
            return Task.FromResult(Encoding.ASCII.GetBytes("%PDF justificante"));
        }
    }

    public void Dispose()
    {
        _copia.Dispose();
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private IBaseDatos Bd => _copia.Bd();

    private static Task<X509Certificate2?> Certificado(string _)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=PRUEBA SINTETICA, SERIALNUMBER=IDCES-00000000T, O=SOCIEDAD SINTETICA, OID.2.5.4.97=VATES-B12345674", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Task.FromResult<X509Certificate2?>(req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1)));
    }

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ? WHERE IdSociedad = 'T01'", _carpeta);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0) await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, EntornoPruebas) VALUES (1, True)");
            else await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = True");
            return true;
        });
    }

    private static DeclaracionInformativa Declaracion(string numero)
    {
        var d = new Declaracion349(2026, 2, new Declarante303("B12345674", "Sociedad sintética", null),
            [new Operador349("IE", "1234567T", "OPERADOR UNO", 'I', 100m), new Operador349("DE", "123456789", "OPERADOR DOS", 'A', 50m)], [], []);
        var (t1, t2) = Modelo349.Registros(d, new ContactoInformativa("930000000", "CONTACTO"), numero);
        return new DeclaracionInformativa("349", 2026, "2T", "B12345674", "Sociedad sintética", t1, t2, numero, d.Total, []);
    }

    [Fact]
    public async Task ValidaPresentaLosCorrectosGuardaYDaDeBaja()
    {
        await PrepararAsync();
        var cliente = new ClienteSimulado { Erroneos = 1 };
        var tgvi = new ServicioTgvi(Bd, cliente, Certificado, new RelojFijo(new DateTime(2026, 7, 10)));
        var numero = await tgvi.NumeroJustificanteAsync("T01", "349", 2026, "2T");
        Assert.Equal("3492602900001", numero);                                 // entorno de pruebas: desde 900001

        var b = await tgvi.PrepararAsync(_ctx, Declaracion(numero));
        Assert.True(b.SePuedePresentar, string.Join(" ", b.Bloqueos));
        Assert.Equal((EntornoAeat.Pruebas, 1, 1), (b.Entorno, b.Correctos, b.Erroneos));
        Assert.Single(b.Errores);
        Assert.Contains(b.Avisos, a => a.Contains("complementaria"));
        Assert.NotNull(b.VistaPrevia);
        Assert.Equal(["Inicializar 349 2026 2T B12345674 1 500", "EnviarDatos 1 2", "RecuperarErrores", "VistaPrevia"], cliente.Operaciones);

        Assert.False((await tgvi.PresentarAsync(_ctx, b, conforme: false, _actor)).EsCorrecto);
        var p = await tgvi.PresentarAsync(_ctx, b, conforme: true, _actor);
        Assert.True(p.EsCorrecto, p.Error?.Mensaje);
        var g = p.Valor.Guardada;
        Assert.Equal(("349", "2T", "CSVTGVIPRUEBA001", "2026349000000000000001", "3492602900001", 150m), (g.Modelo, g.Periodo, g.Csv, g.Expediente, g.Justificante, g.Resultado));
        Assert.Equal(Path.Combine(_carpeta, "2026", "Modelos AEAT", "349", "PRUEBAS 349 2026 2T 3492602900001.pdf"), g.RutaPdf);
        Assert.True(File.Exists(g.RutaPdf));
        Assert.Contains(p.Valor.Avisos, a => a.Contains("1 erróneos"));
        Assert.Contains(cliente.Operaciones, o => o.StartsWith("Descargar https://prewww2.aeat.es/", StringComparison.Ordinal) && o.EndsWith("CSV=CSVTGVIPRUEBA001", StringComparison.Ordinal));
        Assert.Equal("3492602900002", await tgvi.NumeroJustificanteAsync("T01", "349", 2026, "2T"));

        var baja = await tgvi.BajaAsync(_ctx, g, _actor);
        Assert.True(baja.EsCorrecto, baja.Error?.Mensaje);
        Assert.Equal("2026349000000000000099", baja.Valor);
        var avisos = await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT Avisos FROM PresentacionesAeat WHERE IdPresentacionAeat = ?", g.Id));
        Assert.Contains("Dada de baja", (string)avisos!);
    }

    [Fact]
    public async Task UnDeclaranteDistintoONingunCorrectoNoSePresenta()
    {
        await PrepararAsync();
        var cliente = new ClienteSimulado { Erroneos = 2 };
        var tgvi = new ServicioTgvi(Bd, cliente, Certificado, new RelojFijo(new DateTime(2026, 7, 10)));
        var b = await tgvi.PrepararAsync(_ctx, Declaracion("3492602900001"));
        Assert.Contains(b.Bloqueos, x => x.Contains("Todos los registros"));
        Assert.False((await tgvi.PresentarAsync(_ctx, b, true, _actor)).EsCorrecto);
        var otro = await tgvi.PrepararAsync(_ctx, Declaracion("3492602900001") with { NifSociedad = "A58818501" });
        Assert.Contains(otro.Bloqueos, x => x.Contains("no es el de la sociedad"));
    }
}
