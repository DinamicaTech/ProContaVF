using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Presentación del 303 contra Access con un cliente de la AEAT simulado (contabilidad/modelos-tributarios/presentacion y modelo-303):
/// el entorno sale de la BD, sin conformidad no se presenta, lo ya presentado solo admite complementaria, la respuesta aceptada se
/// guarda con el PDF archivado, la rechazada no guarda nada y, al presentar, la liquidación del IVA propone lo presentado y marca
/// desactualizado lo liquidado por otro importe. Datos sintéticos; certificado autofirmado de prueba creado en memoria.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class PresentacionAeatAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private readonly string _carpeta = BdPruebas.CarpetaTemporal();

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    /// <summary>Cliente simulado: valida siempre, guarda las peticiones y responde lo que se le diga.</summary>
    private sealed class ClienteSimulado : IClienteAeat
    {
        public List<(EntornoAeat Entorno, PeticionPresentacionAeat Peticion)> Presentaciones { get; } = [];
        public List<DeclaracionPresentadaAeat> YaPresentadas { get; } = [];
        public string[] Errores { get; set; } = [];
        public int Numero { get; set; } = 1;

        public Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken c = default) =>
            Task.FromResult(new ValidacionAeat(Encoding.ASCII.GetBytes("%PDF borrador"), [], []));

        public Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat p, CancellationToken c = default)
        {
            Presentaciones.Add((entorno, p));
            if (Errores.Length > 0)
                return Task.FromResult(new RespuestaPresentacionAeat(null, null, null, null, null, null, null, null, null, null, null, [], [], Errores, "{}"));
            var n = Numero++;
            return Task.FromResult(new RespuestaPresentacionAeat($"CSVPRUEBA{n:0000000}", "2026-11-17", "10:0" + n, $"2026303000000{n:000}", $"303000000000{n}", "N", 0m,
                "https://prewww1.aeat.es/pdf", p.Fichero.Substring(341, 9), p.FirNif, p.FirNombre, ["31190 - Revise la información adicional."], [], [], "{\"respuesta\":{}}"));
        }

        public Task<ConsultaAeat> ConsultarAsync(EntornoAeat entorno, X509Certificate2 certificado, string nif, string nombre, string modelo, string ejercicio, string? periodo,
            CancellationToken c = default) => Task.FromResult(new ConsultaAeat(YaPresentadas, null));

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

    private (Servicio303 Servicio, ServicioPresentacionAeat Presentacion) Servicios(ClienteSimulado cliente, DateTime hoy)
    {
        var presentacion = new ServicioPresentacionAeat(Bd, cliente, Certificado, new RelojFijo(hoy));
        return (new Servicio303(Bd, presentacion, new RelojFijo(hoy)), presentacion);
    }

    private async Task PrepararAsync(bool pruebas = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ?, IbanDomiciliacion = 'ES9121000418450200051332' WHERE IdSociedad = 'T01'", _carpeta);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0) await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, EntornoPruebas) VALUES (1, ?)", pruebas);
            else await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = ?", pruebas);
            await s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 1, [PorcentajeProrrataAñoAnterior] = 100 WHERE IdSociedadEjercicio = 9001");
            return true;
        });
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("470", "HP deudora"), ("475", "HP acreedora"), ("634", "Ajustes negativos"), ("639", "Ajustes positivos") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("4700000", "HP deudora por IVA"), ("5720001", "Banco sintético") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        // Un crédito de la 4700 de 1.000 € el 31/03: el 1T (sin libros de IVA) sale a cero y se presenta con la forma N.
        var a = new Asiento(_ctx, new DateTime(2026, 3, 31), "Crédito sintético");
        a.Apuntes.AddRange([Apunte.AlDebe("4700000", 1000m), Apunte.AlHaber("5720001", 1000m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([a], _actor, "Datos sintéticos")).EsCorrecto);
    }

    private Task<int> ContarAsync() => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PresentacionesAeat")));

    [Fact]
    public async Task PresentaEnElEntornoDeLaBdGuardaLaRespuestaYArchivaElJustificante()
    {
        await PrepararAsync(pruebas: true);
        var cliente = new ClienteSimulado();
        var (servicio, presentacion) = Servicios(cliente, new DateTime(2026, 4, 10));
        var r = await servicio.DeclaracionAsync(_ctx, 1, complementaria: false);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal('N', r.Valor.Declaracion.TipoDeclaracion);
        Assert.Equal((1000m, 0m, 1000m), (r.Valor.Declaracion.Casilla(110), r.Valor.Declaracion.Casilla(78), r.Valor.Declaracion.Casilla(87))); // crédito de la 4700 sin aplicar: el resultado es 0
        var b = await presentacion.PrepararAsync(_ctx, r.Valor.Aeat);
        Assert.True(b.SePuedePresentar, string.Join(" ", b.Bloqueos));
        Assert.Equal(EntornoAeat.Pruebas, b.Entorno);
        Assert.Equal("B12345674", b.NifLeido);

        // Sin conformidad no se presenta nada.
        Assert.False((await presentacion.PresentarAsync(_ctx, b, conforme: false, _actor)).EsCorrecto);
        Assert.Empty(cliente.Presentaciones);

        var p = await presentacion.PresentarAsync(_ctx, b, conforme: true, _actor);
        Assert.True(p.EsCorrecto, p.Error?.Mensaje);
        var (entorno, peticion) = Assert.Single(cliente.Presentaciones);
        Assert.Equal(EntornoAeat.Pruebas, entorno);
        Assert.Equal(("B12345674", "SOCIEDAD SINTETICA"), (peticion.FirNif, peticion.FirNombre)); // primero la entidad representada
        var g = p.Valor.Guardada;
        Assert.Equal(("PRUEBAS", "1T", "CSVPRUEBA0000001", "3030000000001"), (g.Entorno, g.Periodo, g.Csv, g.Justificante));
        Assert.True(File.Exists(g.RutaPdf));
        Assert.Equal(Path.Combine(_carpeta, "2026", "Modelos AEAT", "303", "PRUEBAS 303 2026 1T 3030000000001.pdf"), g.RutaPdf);
        Assert.Equal(1, await ContarAsync());

        // Ya presentado: solo complementaria, con el justificante de la anterior.
        var t = await servicio.TrimestresAsync(_ctx);
        Assert.Equal(Estado303.Presentado, t[0].Estado);
        var otra = await presentacion.PrepararAsync(_ctx, (await servicio.DeclaracionAsync(_ctx, 1, false)).Valor.Aeat);
        Assert.Contains(otra.Bloqueos, x => x.Contains("complementaria"));
        var comp = await servicio.DeclaracionAsync(_ctx, 1, complementaria: true);
        Assert.Equal("3030000000001", comp.Valor.Aeat.ComplementariaDe);
        Assert.Contains("X3030000000001", comp.Valor.Aeat.Fichero);
        var bc = await presentacion.PrepararAsync(_ctx, comp.Valor.Aeat);
        Assert.True(bc.SePuedePresentar, string.Join(" ", bc.Bloqueos));
        Assert.True((await presentacion.PresentarAsync(_ctx, bc, true, _actor)).EsCorrecto);
        Assert.Equal(Estado303.Complementaria, (await servicio.TrimestresAsync(_ctx))[0].Estado);
    }

    [Fact]
    public async Task UnaBdDeProduccionPresentaEnProduccionYUnRechazoNoGuardaNada()
    {
        await PrepararAsync(pruebas: false);
        var cliente = new ClienteSimulado { Errores = ["E01 - Error sintético."] };
        var (servicio, presentacion) = Servicios(cliente, new DateTime(2026, 4, 10));
        var b = await presentacion.PrepararAsync(_ctx, (await servicio.DeclaracionAsync(_ctx, 1, false)).Valor.Aeat);
        Assert.Equal(EntornoAeat.Produccion, b.Entorno);
        var p = await presentacion.PresentarAsync(_ctx, b, true, _actor);
        Assert.False(p.EsCorrecto);
        Assert.Contains("E01 - Error sintético.", p.Error!.Causa);
        Assert.All(cliente.Presentaciones, x => Assert.Equal(EntornoAeat.Produccion, x.Entorno));
        Assert.Equal(0, await ContarAsync());
        // Lo presentado por la gestoría (según la AEAT) también bloquea: solo complementaria.
        cliente.YaPresentadas.Add(new DeclaracionPresentadaAeat("2026", "303", "1T", "B12345674", "CSVGESTORIA00001", "", "3039999999999", null, new DateTime(2026, 4, 5)));
        var b2 = await presentacion.PrepararAsync(_ctx, (await servicio.DeclaracionAsync(_ctx, 1, false)).Valor.Aeat);
        Assert.Contains(b2.Bloqueos, x => x.Contains("3039999999999"));
    }

    [Fact]
    public async Task AlPresentarLaLiquidacionProponeLoPresentadoYMarcaDesactualizadoLoLiquidadoPorOtroImporte()
    {
        await PrepararAsync(pruebas: true);
        var liquidacion = new ServicioLiquidacionIva(Bd, new MotorAsientosAccess(Bd), new CatalogoCuentasAccess(Bd), null, new RelojFijo(new DateTime(2026, 4, 10)));
        // Liquidado por 50 € (fijado a mano) antes de presentar.
        Assert.True((await liquidacion.ConfirmarAsync(_ctx, 1, 50m, "Ajuste sintético", _actor)).EsCorrecto);
        Assert.Equal(EstadoLiquidacionIva.Liquidado, (await liquidacion.TrimestresAsync(_ctx))[0].Estado);
        // Se presenta el 303 con resultado 0: lo liquidado ya no es lo presentado.
        var (servicio, presentacion) = Servicios(new ClienteSimulado(), new DateTime(2026, 4, 10));
        var b = await presentacion.PrepararAsync(_ctx, (await servicio.DeclaracionAsync(_ctx, 1, false)).Valor.Aeat);
        Assert.True((await presentacion.PresentarAsync(_ctx, b, true, _actor)).EsCorrecto);
        var t = (await liquidacion.TrimestresAsync(_ctx))[0];
        Assert.Equal(EstadoLiquidacionIva.Desactualizado, t.Estado);
        Assert.Contains("Se presentó el 303", t.Motivo);
        Assert.Equal(0m, t.Presentado!.Resultado);
        // La propuesta usa el importe presentado, sin cambiar el asiento hasta que el operador la confirme.
        var propuesta = (await liquidacion.ProponerAsync(_ctx, 1)).Valor;
        Assert.Equal(0m, propuesta.ImporteLiquidado);
        Assert.Contains(propuesta.Avisos, x => x.Contains("se presentó"));
        Assert.Equal(50m, (await liquidacion.GuardadasAsync(_ctx))[0].ImporteLiquidado);
    }
}
