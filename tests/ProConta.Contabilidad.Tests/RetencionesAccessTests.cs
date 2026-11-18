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
/// Modelos 111 y 115 contra Access (contabilidad/modelos-tributarios/modelo-111 y modelo-115): lectura de las nóminas vigentes (sin las
/// importaciones reemplazadas), reparto de la especie, facturas de compra con retención (también las migradas sin importe, con el
/// porcentaje), bloqueos, cuadre con las 4751 y presentación con complementaria con un cliente de la AEAT simulado. Datos sintéticos;
/// certificado autofirmado de prueba creado en memoria.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class RetencionesAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private readonly string _carpeta = BdPruebas.CarpetaTemporal();

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private sealed class ClienteSimulado : IClienteAeat
    {
        public List<PeticionPresentacionAeat> Presentaciones { get; } = [];
        public int Numero { get; set; } = 1;

        public Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken c = default) =>
            Task.FromResult(new ValidacionAeat(Encoding.ASCII.GetBytes("%PDF borrador"), [], []));

        public Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat p, CancellationToken c = default)
        {
            Presentaciones.Add(p);
            var n = Numero++;
            return Task.FromResult(new RespuestaPresentacionAeat($"CSVPRUEBA{n:0000000}", "2026-04-10", "10:0" + n, $"2026{p.Modelo}000000{n:000}", $"{p.Modelo}000000000{n}", "U", 0m,
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

    private (ServicioRetenciones M111, ServicioRetenciones M115, ServicioPresentacionAeat Presentacion) Servicios(ClienteSimulado cliente, DateTime hoy)
    {
        var presentacion = new ServicioPresentacionAeat(Bd, cliente, Certificado, new RelojFijo(hoy));
        return (ServicioRetenciones.Modelo111(Bd, presentacion, new RelojFijo(hoy)), ServicioRetenciones.Modelo115(Bd, presentacion, new RelojFijo(hoy)), presentacion);
    }

    private async Task<int> AsientoAsync(DateTime fecha, string descripcion, params Apunte[] apuntes)
    {
        var a = new Asiento(_ctx, fecha, descripcion);
        a.Apuntes.AddRange(apuntes);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([a], _actor, "Datos sintéticos")).EsCorrecto);
        return await Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdAsiento) FROM Asientos WHERE Descripcion = ?", descripcion)));
    }

    private Task<int> ImportacionAsync(string huella, string estado, DateTime fecha) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO ImportacionesNomina (IdSociedadEjercicio, NombreFichero, Huella, Origen, TipoPaga, FechaNomina, Estado, FechaHoraImportacion) " +
            "VALUES (9001, ?, ?, 'FICHERO', 'MENSUAL', ?, ?, ?)", huella + ".xlsx", huella, fecha, estado, fecha);
        return await s.UltimoIdAsync();
    });

    private Task NominaAsync(int importacion, int ordinal, string nif, DateTime fecha, decimal bruto, decimal dtoEsp, decimal autonomo, decimal irpf, int? asiento) => Bd.EjecutarAsync(s =>
        s.EjecutarAsync("INSERT INTO Nominas (IdImportacionNomina, Ordinal, NombreOrigen, NifOrigen, TipoPaga, FechaNomina, Bruto, DtoReEsp, Autonomo, IRPF111, IdAsiento) " +
            "VALUES (?, ?, ?, ?, 'MENSUAL', ?, ?, ?, ?, ?, ?)", importacion, ordinal, $"Trabajador sintético {nif}", nif, fecha, bruto, dtoEsp, autonomo, irpf,
            (object?)asiento ?? DBNull.Value));

    private Task FacturaAsync(string numero, string proveedor, DateTime fecha, decimal baseImponible, decimal? pct, int? tipo, decimal? retencion, bool exportada, int? asiento) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, CuentaContableProveedor, NIF, NumeroFactura, Fecha, ImporteBI, ImporteIVA, ImporteTotal, ConceptoGasto, " +
            "Pagada, Exportada, Duplicada, InversionSujetoPasivo, PorcentajeRetencion, TipoRetencion, ImporteRetencion, IdAsientoContable) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "T01", "2026", 9001, proveedor, proveedor == "4100001" ? "00000000T" : "B12345674", numero, fecha, baseImponible, 0m, baseImponible - (retencion ?? 0m), "6230000",
            false, exportada, false, false, (object?)pct ?? DBNull.Value, tipo is int tt ? (object)(byte)tt : DBNull.Value, (object?)retencion ?? DBNull.Value,
            (object?)asiento ?? DBNull.Value));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ?, IbanDomiciliacion = 'ES9121000418450200051332' WHERE IdSociedad = 'T01'", _carpeta);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0) await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, EntornoPruebas) VALUES (1, True)");
            else await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = True");
            return true;
        });
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("475", "HP acreedora"), ("640", "Sueldos"), ("465", "Remuneraciones"), ("623", "Servicios profesionales"), ("621", "Arrendamientos"),
            ("410", "Acreedores") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("4751000", "Retenciones del trabajo y profesionales"), ("4751001", "Retenciones de alquileres"), ("6400000", "Sueldos"),
            ("4650000", "Remuneraciones pendientes"), ("6230000", "Profesionales"), ("6210000", "Alquileres"), ("4100001", "Profesional sintético"), ("4100002", "Arrendador sintético") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);

        // Nómina vigente de enero (con especie) y una de febrero de una importación reemplazada, que no cuenta.
        var nomina = await AsientoAsync(new DateTime(2026, 1, 31), "Nómina sintética enero",
            Apunte.AlDebe("6400000", 3000m), Apunte.AlHaber("4751000", 600m), Apunte.AlHaber("4650000", 2400m));
        var vigente = await ImportacionAsync("NOMINA-ENERO", "CONTABILIZADA", new DateTime(2026, 1, 31));
        await NominaAsync(vigente, 1, "00000001R", new DateTime(2026, 1, 31), 3000m, -100m, 1000m, 600m, nomina);
        var reemplazada = await ImportacionAsync("NOMINA-FEBRERO-V1", "REEMPLAZADA", new DateTime(2026, 2, 28));
        await NominaAsync(reemplazada, 1, "00000001R", new DateTime(2026, 2, 28), 999m, 0m, 0m, 99m, null);

        // Profesional migrado (porcentaje sin importe de retención) y alquiler.
        var prof = await AsientoAsync(new DateTime(2026, 2, 10), "Factura profesional sintética",
            Apunte.AlDebe("6230000", 1000m), Apunte.AlHaber("4751000", 150m), Apunte.AlHaber("4100001", 850m));
        await FacturaAsync("P-1", "4100001", new DateTime(2026, 2, 10), 1000m, 15m, 1, null, true, prof);
        var alq = await AsientoAsync(new DateTime(2026, 3, 1), "Factura alquiler sintética",
            Apunte.AlDebe("6210000", 1000m), Apunte.AlHaber("4751001", 190m), Apunte.AlHaber("4100002", 810m));
        await FacturaAsync("A-1", "4100002", new DateTime(2026, 3, 1), 1000m, 19m, 2, 190m, true, alq);

        // 2T: una con retención sin tipo y una de profesional sin contabilizar.
        await FacturaAsync("X-1", "4100001", new DateTime(2026, 5, 2), 200m, 15m, null, 30m, false, null);
        await FacturaAsync("P-2", "4100001", new DateTime(2026, 5, 3), 300m, 15m, 1, 45m, false, null);
    }

    [Fact]
    public async Task CalculaLasCasillasDesdeNominasYFacturasYBloqueaLoPendiente()
    {
        await PrepararAsync();
        var (m111, m115, _) = Servicios(new ClienteSimulado(), new DateTime(2026, 7, 10));
        var d = (await m111.DeclaracionAsync(_ctx, 1, false)).Valor.Declaracion;
        Assert.Equal((1m, 1900m, 393.10m), (d.Casilla(1), d.Casilla(2), d.Casilla(3)));
        Assert.Equal((1m, 1000m, 206.90m), (d.Casilla(4), d.Casilla(5), d.Casilla(6))); // 600 × 1.000 / 2.900
        Assert.Equal((1m, 1000m, 150m), (d.Casilla(7), d.Casilla(8), d.Casilla(9)));
        Assert.Equal((750m, 750m), (d.Casilla(28), d.Casilla(30)));
        var a = (await m115.DeclaracionAsync(_ctx, 1, false)).Valor.Declaracion;
        Assert.Equal((1m, 1000m, 190m, 190m), (a.Casilla(1), a.Casilla(2), a.Casilla(3), a.Casilla(5)));

        var t111 = await m111.TrimestresAsync(_ctx);
        var t115 = await m115.TrimestresAsync(_ctx);
        Assert.Equal(Estado303.SinPresentar, t111[0].Estado);
        Assert.Empty(t111[0].Bloqueos);
        Assert.DoesNotContain(t111[0].Avisos, x => x.Contains("4751")); // las 4751 cuadran con el modelo (solo queda el aviso del plazo)
        Assert.DoesNotContain(t115[0].Avisos, x => x.Contains("4751"));
        Assert.Equal('I', t111[0].FormaPago); // el 10/07 ya no se puede domiciliar el 1T
        // 2T: la factura sin tipo bloquea los dos; la de profesional sin contabilizar, solo el 111.
        Assert.Equal(2, t111[1].Bloqueos.Count);
        Assert.Contains("X-1", Assert.Single(t115[1].Bloqueos));
        Assert.Equal(Estado303.EnCurso, t111[2].Estado);

        // Un abono manual a la 4751000 en el 1T: el 111 avisa de la diferencia con lo que esperará el aviso S4.
        await AsientoAsync(new DateTime(2026, 3, 31), "Ajuste manual sintético", Apunte.AlDebe("6400000", 12.34m), Apunte.AlHaber("4751000", 12.34m));
        Assert.Contains((await m111.TrimestresAsync(_ctx))[0].Avisos, x => x.Contains("diferencia 12,34 €"));
    }

    [Fact]
    public async Task PresentaYLaComplementariaLlevaLoDeclaradoAntes()
    {
        await PrepararAsync();
        var cliente = new ClienteSimulado();
        var (m111, _, presentacion) = Servicios(cliente, new DateTime(2026, 4, 10));
        var r = await m111.DeclaracionAsync(_ctx, 1, false);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal('U', r.Valor.Declaracion.TipoDeclaracion);
        Assert.Equal((750m, 750m), (r.Valor.Aeat.ImporteAIngresar, r.Valor.Aeat.Resultado));
        var b = await presentacion.PrepararAsync(_ctx, r.Valor.Aeat);
        Assert.True(b.SePuedePresentar, string.Join(" ", b.Bloqueos));
        Assert.Equal("B12345674", b.NifLeido);
        var p = await presentacion.PresentarAsync(_ctx, b, true, _actor);
        Assert.True(p.EsCorrecto, p.Error?.Mensaje);
        Assert.Equal(("111", "1T", 750m), (p.Valor.Guardada.Modelo, p.Valor.Guardada.Periodo, p.Valor.Guardada.Resultado));
        Assert.Equal(Path.Combine(_carpeta, "2026", "Modelos AEAT", "111", "PRUEBAS 111 2026 1T 1110000000001.pdf"), p.Valor.Guardada.RutaPdf);
        Assert.Equal(Estado303.Presentado, (await m111.TrimestresAsync(_ctx))[0].Estado);

        // Llega otra factura de profesional del 1T: el trimestre queda desactualizado y la complementaria resta lo declarado.
        var otra = await AsientoAsync(new DateTime(2026, 3, 20), "Otra factura profesional sintética",
            Apunte.AlDebe("6230000", 400m), Apunte.AlHaber("4751000", 60m), Apunte.AlHaber("4100001", 340m));
        await FacturaAsync("P-3", "4100001", new DateTime(2026, 3, 20), 400m, 15m, 1, 60m, true, otra);
        var t = (await m111.TrimestresAsync(_ctx))[0];
        Assert.Equal(Estado303.Desactualizado, t.Estado);
        Assert.Contains(t.Avisos, x => x.Contains("complementaria"));
        var comp = await m111.DeclaracionAsync(_ctx, 1, complementaria: true);
        Assert.True(comp.EsCorrecto, comp.Error?.Mensaje);
        Assert.Equal((810m, 750m, 60m), (comp.Valor.Declaracion.Casilla(28), comp.Valor.Declaracion.Casilla(29), comp.Valor.Declaracion.Casilla(30)));
        Assert.Equal((1m, 1400m), (comp.Valor.Declaracion.Casilla(7), comp.Valor.Declaracion.Casilla(8)));
        Assert.Equal("1110000000001", comp.Valor.Aeat.ComplementariaDe);
        Assert.Contains("X1110000000001", comp.Valor.Aeat.Fichero);
        var bc = await presentacion.PrepararAsync(_ctx, comp.Valor.Aeat);
        Assert.True(bc.SePuedePresentar, string.Join(" ", bc.Bloqueos));
        Assert.True((await presentacion.PresentarAsync(_ctx, bc, true, _actor)).EsCorrecto);
        Assert.Equal(Estado303.Complementaria, (await m111.TrimestresAsync(_ctx))[0].Estado);
        Assert.Equal(2, cliente.Presentaciones.Count);
        Assert.All(cliente.Presentaciones, x => Assert.Equal("111", x.Modelo));
    }
}
