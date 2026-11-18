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
/// Modelo 200 contra Access (contabilidad/modelos-tributarios/modelo-200): el balance y la PyG salen del módulo de balances con el
/// asiento del impuesto confirmado, la liquidación del Impuesto sobre Sociedades grabado, las retenciones por su naturaleza y el pago
/// fraccionado por el mes de su cargo; el fichero se valida con un cliente de la AEAT simulado. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class Modelo200AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);
    private readonly Actor _actor = new("operador.prueba");
    private static readonly DateTime Julio = new(2026, 7, 1);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private sealed class ClienteSimulado : IClienteAeat
    {
        public List<string> Validados { get; } = [];

        public Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken c = default)
        {
            Validados.Add($"{modelo} {ejercicio} {periodo}");
            return Task.FromResult(new ValidacionAeat(Encoding.ASCII.GetBytes("%PDF borrador"), [], []));
        }

        public Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat p, CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<ConsultaAeat> ConsultarAsync(EntornoAeat entorno, X509Certificate2 certificado, string nif, string nombre, string modelo, string ejercicio, string? periodo,
            CancellationToken c = default) => Task.FromResult(new ConsultaAeat([], null));

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

    private ServicioImpuestoSociedades Impuesto() => new(Bd, new MotorAsientosAccess(Bd), new CatalogoCuentasAccess(Bd), null, new RelojFijo(Julio));

    /// <summary>
    /// Capital 10.000; alquiler de 20.000 con 1.900 de retención; servicios 4.000 y multa 100; pago fraccionado de 500 el 20 de
    /// octubre. Impuesto (25 %, sin bonificación): base 16.000, cuota 4.000, a ingresar 1.600.
    /// </summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET CIF = 'B12345674', IbanDomiciliacion = 'ES9121000418450200051332' WHERE IdSociedad = 'T01'");
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0) await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, EntornoPruebas) VALUES (1, True)");
            else await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = True");
            return true;
        });
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("630", "Impuesto sobre beneficios"), ("475", "HP acreedora"), ("470", "HP deudora") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital social"), ("4300001", "Inquilino sintético"), ("7520000", "Ingresos por arrendamientos"),
                     ("4730001", "Retenciones alquiler"), ("4730002", "Pagos a cuenta"), ("6290001", "Otros servicios"), ("6780000", "Gastos excepcionales"), ("5720001", "Banco sintético") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        Asiento A(int mes, int dia, string texto, params Apunte[] apuntes)
        {
            var a = new Asiento(_ctx, new DateTime(2025, mes, dia), texto);
            a.Apuntes.AddRange(apuntes);
            return a;
        }
        var r = await new MotorAsientosAccess(Bd).GuardarManualesAsync(
        [
            A(1, 10, "Capital", Apunte.AlDebe("5720001", 10000m), Apunte.AlHaber("1000000", 10000m)),
            A(3, 1, "Alquiler sintético", Apunte.AlDebe("4300001", 18100m), Apunte.AlDebe("4730001", 1900m), Apunte.AlHaber("7520000", 20000m)),
            A(4, 1, "Gasto sintético", Apunte.AlDebe("6290001", 4000m), Apunte.AlDebe("6780000", 100m), Apunte.AlHaber("5720001", 4100m)),
            A(10, 20, "Pago fraccionado sintético", Apunte.AlDebe("4730002", 500m), Apunte.AlHaber("5720001", 500m)),
        ], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, string.Join(" ", r.Errores.Select(e => e.Mensaje + " " + e.Causa)));
        var entrada = new EntradaImpuestoSociedades([new(false, true, "Multas y sanciones", 100m, "6780000")], Bonificaciones: 0m);
        var confirmado = await Impuesto().ConfirmarAsync(_ctx, entrada, null, _actor);
        Assert.True(confirmado.EsCorrecto, confirmado.Error?.Mensaje);
    }

    private static readonly Persona200 Admin = new("00000000T", "PRUEBA SINTETICA, UNO", false, "08");

    private static DatosDeclaracion200 Datos() => new("6820", PersonalFijo: 1m, Telefono: "930000000", Administradores: [Admin],
        TitularesReales: [new TitularReal200("00000000T", "PRUEBA SINTETICA, UNO", new DateTime(1970, 1, 1))], Secretario: Admin,
        Representantes: [new Representante200("00000000T", "PRUEBA SINTETICA, UNO", new DateTime(2000, 1, 1), "NOTARIA")]);

    [Fact]
    public async Task ElDoscientosSaleDelBalanceYDelImpuestoGrabado()
    {
        await PrepararAsync();
        var cliente = new ClienteSimulado();
        var presentacion = new ServicioPresentacionAeat(Bd, cliente, Certificado, new RelojFijo(Julio));
        var s = new Servicio200(Bd, presentacion, Impuesto());
        var r = await s.DeclaracionAsync(_ctx, Datos());
        Assert.True(r.EsCorrecto, string.Join(" ", r.Errores.Select(e => e.Mensaje + " " + e.Causa)));
        var (d, aeat) = r.Valor;

        // Balance de situación y PyG del módulo de balances, con el asiento del impuesto
        Assert.Equal((23500m, 23500m), (d.Casilla("04000", 180), d.Casilla("06000", 252)));
        Assert.Equal((10000m, 11900m, 1600m), (d.Casilla("05000", 188), d.Casilla("05000", 199), d.Casilla("06000", 249)));
        Assert.Equal((20000m, -4000m, -100m, 15900m), (d.Casilla("07000", 267), d.Casilla("07000", 254), d.Casilla("07000", 295), d.Casilla("07000", 296)));
        Assert.Equal((-4000m, 11900m), (d.Casilla("08000", 326), d.Casilla("08000", 500)));

        // Liquidación del IS grabado: la multa en «otras correcciones», retenciones de alquiler y pago fraccionado de octubre (2P)
        Assert.Equal((4000m, 15900m), (d.Casilla("12000", 301), d.Casilla("12000", 501)));
        Assert.Equal((100m, 100m, 100m), (d.Casilla("13000", 413), d.Casilla("20B00", 2301), d.Casilla("26G00", 3391)));
        Assert.Equal((16000m, 4000m, "2500"), (d.Casilla("14000", 552), d.Casilla("14000", 562), d.TipoGravamen));
        Assert.Equal((1900m, 0m, 500m, 1600m), (d.Casilla("14B00", 1787), d.Casilla("14B00", 1785), d.Casilla("14B00", 603), d.Resultado));
        Assert.Equal('U', d.TipoDeclaracion);
        Assert.Contains(d.Errores, e => e.Contains("socios"));                                  // la sociedad sintética no tiene socios dados de alta
        Assert.DoesNotContain(d.Errores, e => e.Contains("cuadra") || e.Contains("casilla"));

        // El fichero lo valida la pieza común
        Assert.Equal(("200", "0A", 'U', 1600m), (aeat.Modelo, aeat.Periodo, aeat.FormaPago, aeat.ImporteAIngresar));
        Assert.Equal("B12345674", Fichero200.LeerNif(aeat.Fichero));
        var v = await presentacion.ValidarAsync(aeat);
        Assert.True(v.Correcta);
        Assert.Equal(["200 2025 0A"], cliente.Validados);
    }

    [Fact]
    public async Task SinDisenoDelEjercicioNoSeGenera()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
        var s = new Servicio200(Bd, new ServicioPresentacionAeat(Bd, new ClienteSimulado(), Certificado), Impuesto());
        var r = await s.DeclaracionAsync(new ContextoSociedadEjercicio("T01", 9002, 2026), Datos());
        Assert.False(r.EsCorrecto);
        Assert.Equal("Modelo200.SinDiseno", r.Errores[0].Codigo);
    }

    /// <summary>
    /// Esquema 72: el CNAE, el teléfono, la plantilla y la opción del 0,7 % (Empresas y Ejercicios) y los cargos vigentes a 31/12 (Cargos de la
    /// empresa) los leen el 200 y el 390; la línea del 200 de cada ajuste se graba con el impuesto.
    /// </summary>
    [Fact]
    public async Task ElDoscientosYElTrescientosNoventaLeenSusDatosDeLaBd()
    {
        await PrepararAsync();
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET Cnae = '6820', Telefono = '930000000' WHERE IdSociedad = 'T01'");
            await s.EjecutarAsync("UPDATE SociedadesEjercicios SET PersonalFijo = 1.5, PersonalNoFijo = 0.25, OpcionFinesSociales = True WHERE IdSociedadEjercicio = 9001");
            await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, PrimerApellido, SegundoApellido, NombrePersona, NIF, CodigoPostal, Activo) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                "FISICA", "Persona sintética", "SINTETICA", "PRUEBA", "UNO", "00000000T", "08001", true);
            var uno = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdTercero) FROM Terceros"));
            await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, PrimerApellido, NombrePersona, NIF, Activo) VALUES (?, ?, ?, ?, ?, ?)",
                "FISICA", "Persona sintética dos", "SINTETICA", "DOS", "00000001R", true);
            var dos = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdTercero) FROM Terceros"));
            foreach (var (tercero, cargo, nacimiento, poder, hasta) in new (int, string, DateTime?, DateTime?, DateTime?)[]
                     {
                         (uno, "ADMINISTRADOR", null, null, null), (uno, "SECRETARIO", null, null, null), (uno, "TITULAR_REAL", new DateTime(1970, 1, 1), null, null),
                         (uno, "REPRESENTANTE", null, new DateTime(2000, 1, 1), null), (dos, "ADMINISTRADOR", null, null, new DateTime(2025, 6, 30)),
                     })
                await s.EjecutarAsync("INSERT INTO CargosSociedad (IdSociedad, IdTercero, Cargo, FechaNacimiento, FechaPoder, Notaria, FechaHasta, Activo) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", tercero, cargo, nacimiento, poder, poder is null ? null : "NOTARIA", hasta, true);
            await s.EjecutarAsync("UPDATE Actividades SET Clave390 = 'A01', EpigrafeIae = '8612', Cnae = '6820', Orden390 = 1 WHERE IdSociedad = 'T01'");
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Actividades WHERE IdSociedad = 'T01'")) == 0)
                await s.EjecutarAsync("INSERT INTO Actividades (IdSociedad, Codigo, Nombre, Deducible, Activa, Clave390, EpigrafeIae, Cnae, Orden390) VALUES ('T01', 'LOCALES', 'Alquiler de locales', 1, True, 'A01', '8612', '6820', 1)");
            return true;
        });
        var presentacion = new ServicioPresentacionAeat(Bd, new ClienteSimulado(), Certificado, new RelojFijo(Julio));
        var s200 = new Servicio200(Bd, presentacion, Impuesto());

        var d = await s200.DatosAsync(_ctx);
        Assert.Equal(("6820", "930000000", 1.5m, 0.25m, true), (d.Cnae, d.Telefono, d.PersonalFijo, d.PersonalNoFijo, d.FinesSociales));
        Assert.Equal(["SINTETICA PRUEBA, UNO"], d.Administradores!.Select(x => x.Nombre));      // el que cesó en junio ya no está a 31/12
        Assert.Equal(("00000000T", "08"), (d.Administradores![0].Nif, d.Administradores[0].Provincia));
        Assert.Equal(new DateTime(1970, 1, 1), d.TitularesReales!.Single().FechaNacimiento);
        Assert.Equal("00000000T", d.Secretario!.Nif);
        Assert.Equal((new DateTime(2000, 1, 1), "NOTARIA"), (d.Representantes!.Single().FechaPoder, d.Representantes![0].Notaria));

        var r = await s200.DeclaracionAsync(_ctx);
        Assert.True(r.EsCorrecto, string.Join(" ", r.Errores.Select(e => e.Mensaje + " " + e.Causa)));
        Assert.DoesNotContain(r.Valor.Declaracion.Errores, e => e.Contains("CNAE") || e.Contains("administrador") || e.Contains("titular"));
        Assert.Equal(100m, r.Valor.Declaracion.Casilla("13000", 413));                            // la multa, en «otras correcciones» por su línea grabada

        var guardado = await Impuesto().GuardadoAsync(_ctx);
        Assert.Equal(AjusteImpuestoSociedades.LineaOtras, guardado!.Entrada.Ajustes.Single().Linea200);

        var d390 = await new Servicio390(Bd, presentacion).DatosAsync(_ctx);
        Assert.Equal(("6820", "A01", "8612"), (d390.Cnae, d390.Actividades.Single().Clave, d390.Actividades.Single().Epigrafe));
        Assert.Equal("00000000T", d390.Representantes!.Single().Nif);
    }
}
