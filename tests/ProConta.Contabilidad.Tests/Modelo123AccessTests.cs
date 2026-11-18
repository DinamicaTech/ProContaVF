using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Dividendos;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Modelos 123 y 193 contra Access (contabilidad/modelos-tributarios/modelo-123 y modelo-193): los dividendos del trimestre y del año salen
/// del reparto grabado o, sin él, de lo abonado en la subcuenta de retención de cada socio; un trimestre sin dividendos no se presenta y el
/// fichero del 123 lo valida la pieza común con un cliente de la AEAT simulado. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class Modelo123AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private readonly Dictionary<string, int> _identidades = [];
    private static readonly DateTime Enero = new(2027, 1, 10);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private sealed class ClienteSimulado : IClienteAeat
    {
        public List<string> Validados { get; } = [];

        public Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken c = default)
        {
            Validados.Add($"{modelo} {ejercicio} {periodo} {fichero.Length}");
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

    /// <summary>Dos socios al 50 % con su 526 y su 4751, reservas voluntarias y 50.000 € pagados a cuenta a cada uno el 30/06.</summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET CIF = 'B12345674' WHERE IdSociedad = 'T01'"));
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("113", "Reservas voluntarias"), ("526", "Dividendo activo a pagar"), ("475", "HP acreedora"), ("572", "Bancos") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1130000", "Reservas voluntarias"), ("5260001", "Socia Uno"), ("5260002", "Socio Dos"),
                     ("4751011", "Socia Uno"), ("4751012", "Socio Dos"), ("5720001", "Banco sintético") })
        {
            var c = await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        var reservas = new Asiento(_ctx, new DateTime(2026, 1, 1), "Reservas sintéticas");
        reservas.Apuntes.AddRange([Apunte.AlDebe("5720001", 200000m), Apunte.AlHaber("1130000", 200000m)]);
        var pago1 = new Asiento(_ctx, new DateTime(2026, 6, 30), "Pago a cuenta Socia Uno");
        pago1.Apuntes.AddRange([Apunte.AlDebe("5260001", 50000m), Apunte.AlHaber("5720001", 50000m)]);
        var pago2 = new Asiento(_ctx, new DateTime(2026, 6, 30), "Pago a cuenta Socio Dos");
        pago2.Apuntes.AddRange([Apunte.AlDebe("5260002", 50000m), Apunte.AlHaber("5720001", 50000m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([reservas, pago1, pago2], _actor, "Datos sintéticos")).EsCorrecto);
        await Bd.EjecutarAsync(async s =>
        {
            foreach (var (nombre, nif, c526, c4751) in new[] { ("Socia Uno", "00000000T", "5260001", "4751011"), ("Socio Dos", "00000001R", "5260002", "4751012") })
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, NIF, CodigoPostal, Activo, Version) VALUES (?, ?, ?, ?, ?)", nombre, nif, "08001", true, 0);
                var t = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO Socios (IdTercero, Activo, FechaAlta) VALUES (?, ?, ?)", t, true, new DateTime(2020, 1, 1));
                var socio = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO SociosSociedad (IdSocio, IdSociedad, Participacion, IdIdentidadDividendos, IdIdentidadRetencion, Activo) VALUES (?, ?, ?, ?, ?, ?)",
                    socio, "T01", 50m, _identidades[c526], _identidades[c4751], true);
            }
            return 0;
        });
    }

    private Servicio123 Servicio123(ClienteSimulado cliente) => new(Bd, new ServicioPresentacionAeat(Bd, cliente, Certificado, new RelojFijo(Enero)), new RelojFijo(Enero));

    [Fact]
    public async Task ConElRepartoGrabadoSalenElCientoVeintitresYElCientoNoventaYTres()
    {
        await PrepararAsync();
        var dividendos = new ServicioDividendos(Bd, new MotorAsientosAccess(Bd), new CatalogoCuentasAccess(Bd), null, new RelojFijo(new DateTime(2026, 12, 15, 10, 0, 0)));
        var datos = await dividendos.DatosAsync(_ctx, new DateTime(2026, 12, 15));
        var netos = datos.Socios.Select(s => new NetoSocio(s.IdSocio, s.NetoPropuesto)).ToList();
        var reparto = await dividendos.ConfirmarAsync(_ctx, new DateTime(2026, 12, 15), netos, null, null, _actor);
        Assert.True(reparto.EsCorrecto, reparto.Error?.TextoOperador());

        var cliente = new ClienteSimulado();
        var s = Servicio123(cliente);
        var trimestres = await s.TrimestresAsync(_ctx);
        Assert.Contains(trimestres[0].Bloqueos, b => b.Contains("No hay dividendos"));
        Assert.Equal((Estado303.SinPresentar, 23456.80m), (trimestres[3].Estado, trimestres[3].TotalCalculado));
        Assert.False((await s.DeclaracionAsync(_ctx, 1, false)).EsCorrecto);

        var r = await s.DeclaracionAsync(_ctx, 4, false);
        Assert.True(r.EsCorrecto, string.Join(" ", r.Errores.Select(e => e.Mensaje + " " + e.Causa)));
        var d = r.Valor.Declaracion;
        Assert.Equal((2m, 123456.80m, 23456.80m, 23456.80m, 23456.80m), (d.Casilla(1), d.Casilla(4), d.Casilla(7), d.Casilla(12), d.Casilla(14)));
        Assert.Equal(("123", "4T", 23456.80m), (r.Valor.Aeat.Modelo, r.Valor.Aeat.Periodo, r.Valor.Aeat.Resultado));
        Assert.Equal("B12345674", FicheroRetenciones.LeerNif(r.Valor.Aeat.Fichero));
        Assert.True((await s.Presentacion.ValidarAsync(r.Valor.Aeat)).Correcta);
        Assert.Equal(["123 2026 4T " + (FicheroRetenciones.LongitudCabecera + FicheroRetenciones.LongitudPagina123 + 18)], cliente.Validados);

        // El 193 del año: los mismos dividendos, de un reparto grabado (sin aviso de base reconstruida).
        var d193 = await new Servicio193(Bd).CalcularAsync(_ctx);
        Assert.Equal((2, 123456.80m, 23456.80m), (d193.Perceptores.Count, d193.TotalBase, d193.TotalRetenciones));
        Assert.All(d193.Perceptores, p => Assert.Equal(new DateTime(2026, 12, 15), p.Fecha));
        Assert.Empty(d193.Avisos);
        Assert.True(d193.Valida, string.Join(" ", d193.Errores));
    }

    [Fact]
    public async Task SinRepartoGrabadoLaBaseSaleDeLaRetencionDelTrimestre()
    {
        await PrepararAsync();
        var a = new Asiento(_ctx, new DateTime(2026, 6, 30), "Dividendo de la gestoría");
        a.Apuntes.AddRange([Apunte.AlDebe("1130000", 23456.80m), Apunte.AlHaber("4751011", 11728.40m), Apunte.AlHaber("4751012", 11728.40m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([a], _actor, "Datos sintéticos")).EsCorrecto);

        var s = Servicio123(new ClienteSimulado());
        var segundo = await s.DividendosAsync(_ctx, 2);
        Assert.Equal(2, segundo.Count);
        Assert.All(segundo, p => Assert.Equal((61728.42m, 19m, 11728.40m), (p.Base, p.Porcentaje, p.Retencion)));
        Assert.Empty(await s.DividendosAsync(_ctx, 3));
        var r = await s.DeclaracionAsync(_ctx, 2, false);
        Assert.True(r.EsCorrecto, string.Join(" ", r.Errores.Select(e => e.Mensaje)));
        Assert.Equal((2m, 123456.84m, 23456.80m), (r.Valor.Declaracion.Casilla(1), r.Valor.Declaracion.Casilla(4), r.Valor.Declaracion.Casilla(7)));
        Assert.Contains(r.Valor.Declaracion.Avisos, x => x.Contains("acta del reparto"));
        Assert.Equal('I', r.Valor.Declaracion.TipoDeclaracion);                        // en enero el 2T está fuera del plazo de domiciliación
    }
}
