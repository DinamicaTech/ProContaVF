using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Modelo 202 contra Access (contabilidad/modelos-tributarios/modelo-202): la base sale del asiento del Impuesto sobre Sociedades del
/// ejercicio de referencia (cuota de la 6300 menos las retenciones de las 473, sin la de pagos fraccionados, que se reconoce por sus
/// cargos del banco en abril, octubre o diciembre), el periodo cuyo plazo no ha empezado no se genera, se presenta con un cliente de
/// la AEAT simulado y se avisa si lo ingresado no está cargado en la 473 de pagos a cuenta. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class Modelo202AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx2024 = new("T01", 9002, 2024);
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);
    private readonly Actor _actor = new("operador.prueba");
    private readonly string _carpeta = BdPruebas.CarpetaTemporal();

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private sealed class ClienteSimulado : IClienteAeat
    {
        public List<PeticionPresentacionAeat> Presentaciones { get; } = [];

        public Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken c = default) =>
            Task.FromResult(new ValidacionAeat(Encoding.ASCII.GetBytes("%PDF borrador"), [], []));

        public Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat p, CancellationToken c = default)
        {
            Presentaciones.Add(p);
            return Task.FromResult(new RespuestaPresentacionAeat("CSVPRUEBA2020001", "2025-10-10", "10:00", "2025202000000001", "2020000000001", "U", 1800m,
                "https://prewww1.aeat.es/pdf", p.Fichero.Substring(341, 9), p.FirNif, p.FirNombre, [], [], [], "{\"respuesta\":{}}"));
        }

        public Task<ConsultaAeat> ConsultarAsync(EntornoAeat entorno, X509Certificate2 certificado, string nif, string nombre, string modelo, string ejercicio, string? periodo,
            CancellationToken c = default) => Task.FromResult(new ConsultaAeat([], null));

        public Task<byte[]> DescargarAsync(string url, X509Certificate2? certificado, CancellationToken c = default) => Task.FromResult(Encoding.ASCII.GetBytes("%PDF justificante"));
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

    private async Task AsientoAsync(ContextoSociedadEjercicio ctx, DateTime fecha, string descripcion, params Apunte[] apuntes)
    {
        var a = new Asiento(ctx, fecha, descripcion);
        a.Apuntes.AddRange(apuntes);
        var r = await new MotorAsientosAccess(Bd).GuardarManualesAsync([a], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, string.Join(" ", r.Errores.Select(e => e.Mensaje + " " + e.Causa)));
    }

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2024);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ?, IbanDomiciliacion = 'ES9121000418450200051332' WHERE IdSociedad = 'T01'", _carpeta);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0) await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, EntornoPruebas) VALUES (1, True)");
            else await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = True");
            return true;
        });
        var cuentas = new CatalogoCuentasAccess(Bd);
        var plan = new[] { ("473", "HP retenciones y pagos a cuenta", false), ("630", "Impuesto", false), ("475", "HP acreedora", false), ("572", "Bancos", false), ("430", "Clientes", false),
            ("4730000", "HP pagos y retenciones a cuenta", true), ("4730001", "Retenciones alquiler sintético", true), ("6300000", "Impuesto corriente", true),
            ("4752000", "HP acreedora por IS", true), ("5720001", "Banco sintético", true), ("4300001", "Cliente sintético", true) };
        foreach (var (codigo, nombre, imputable) in plan)
        {
            var alta = await cuentas.AltaAsync(_ctx2024, new AltaCuenta(codigo, nombre, EsImputable: imputable));
            Assert.True(alta.EsCorrecto, alta.Error?.Mensaje);
            Assert.True((await cuentas.AltaEnEjercicioAsync(_ctx, alta.Valor.IdIdentidadContable)).EsCorrecto);
        }
        // 2024: retenciones de un inquilino (sin banco), un pago fraccionado por el banco el 20 de abril y el asiento del impuesto.
        await AsientoAsync(_ctx2024, new DateTime(2024, 2, 5), "Alquiler sintético", Apunte.AlDebe("4730001", 190m), Apunte.AlDebe("5720001", 810m), Apunte.AlHaber("4300001", 1000m));
        await AsientoAsync(_ctx2024, new DateTime(2024, 4, 20), "Pago 202 sintético", Apunte.AlDebe("4730000", 500m), Apunte.AlHaber("5720001", 500m));
        await AsientoAsync(_ctx2024, new DateTime(2024, 12, 31), "Impuesto sintético 2024",
            Apunte.AlDebe("6300000", 10190m), Apunte.AlHaber("4730001", 190m), Apunte.AlHaber("4730000", 500m), Apunte.AlHaber("4752000", 9500m));
    }

    [Fact]
    public async Task LaBaseSaleDelAsientoDelImpuestoSinLosPagosFraccionados()
    {
        await PrepararAsync();
        var cliente = new ClienteSimulado();
        var presentacion = new ServicioPresentacionAeat(Bd, cliente, Certificado, new RelojFijo(new DateTime(2025, 10, 10)));
        var s = new Servicio202(Bd, presentacion, new RelojFijo(new DateTime(2025, 10, 10)));
        var b = await s.BaseAsync("T01", 2024);
        Assert.NotNull(b);
        Assert.Equal(("asiento del impuesto", 10190m, 190m, 10000m), (b!.Origen, b.CuotaLiquida, b.Retenciones, b.Base));
        Assert.Equal(["4730000"], b.CuentasPagosFraccionados);
        Assert.Null(await s.BaseAsync("T01", 2023));

        var datos = new DatosActividad202("6820");
        var periodos = await s.PeriodosAsync(_ctx, datos);
        Assert.Contains("2023", Assert.Single(periodos[0].Bloqueos));                   // 1P: no hay impuesto de 2023
        Assert.Equal((Estado303.SinPresentar, 1800m, 'U'), (periodos[1].Estado, periodos[1].PagoCalculado, periodos[1].FormaPago));
        Assert.Equal(Estado303.EnCurso, periodos[2].Estado);
        Assert.False((await s.DeclaracionAsync(_ctx, 3, false, datos)).EsCorrecto);   // el plazo del 3P no ha empezado

        var r = await s.DeclaracionAsync(_ctx, 2, false, datos);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        var borrador = await presentacion.PrepararAsync(_ctx, r.Valor.Aeat);
        Assert.True(borrador.SePuedePresentar, string.Join(" ", borrador.Bloqueos));
        var p = await presentacion.PresentarAsync(_ctx, borrador, true, _actor);
        Assert.True(p.EsCorrecto, p.Error?.Mensaje);
        Assert.Equal(("202", "2P", 1800m), (p.Valor.Guardada.Modelo, p.Valor.Guardada.Periodo, p.Valor.Guardada.Resultado));

        // Presentado, pero el pago no está en la 473 de pagos a cuenta: se avisa; al contabilizarlo, el aviso desaparece.
        var t = (await s.PeriodosAsync(_ctx, datos))[1];
        Assert.Equal(Estado303.Presentado, t.Estado);
        Assert.Contains(t.Avisos, a => a.Contains("473"));
        await AsientoAsync(_ctx, new DateTime(2025, 10, 15), "Pago 202 sintético 2P", Apunte.AlDebe("4730000", 1800m), Apunte.AlHaber("5720001", 1800m));
        Assert.DoesNotContain((await s.PeriodosAsync(_ctx, datos))[1].Avisos, a => a.Contains("473"));
    }
}
