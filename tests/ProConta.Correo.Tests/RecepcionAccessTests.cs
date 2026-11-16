using System.IO;
using MimeKit;
using ProConta.Correo.Mantenimiento;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Correo.Tests;

/// <summary>Buzón POP3 en memoria.</summary>
internal sealed class BuzonFalso : IClientePop3
{
    public Dictionary<string, byte[]> Mensajes { get; } = [];
    public List<string> Borrados { get; } = [];
    public int Descargas { get; private set; }

    public Task<IReadOnlyList<MensajeRemoto>> ListarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<MensajeRemoto>>(Mensajes.Select(m => new MensajeRemoto(m.Key, m.Value.Length)).ToList());

    public Task<byte[]?> DescargarAsync(CuentaCorreo cuenta, string uidl, CancellationToken cancelacion = default)
    {
        Descargas++;
        return Task.FromResult(Mensajes.TryGetValue(uidl, out var m) ? m : null);
    }

    public Task<IReadOnlyList<string>> EliminarAsync(CuentaCorreo cuenta, IReadOnlyList<string> uidls, CancellationToken cancelacion = default)
    {
        var hechos = uidls.Where(Mensajes.Remove).ToList();
        Borrados.AddRange(hechos);
        return Task.FromResult<IReadOnlyList<string>>(hechos);
    }

    public static byte[] Mime(string de, string asunto, params (string Nombre, byte[] Contenido)[] adjuntos) => Mime(de, asunto, null, adjuntos);

    public static byte[] Mime(string de, string asunto, Action<MimeMessage>? ajustar, params (string Nombre, byte[] Contenido)[] adjuntos)
    {
        var m = new MimeMessage();
        m.From.Add(MailboxAddress.Parse(de));
        m.To.Add(MailboxAddress.Parse("demo2026@example.invalid"));
        m.Subject = asunto;
        var cuerpo = new BodyBuilder { TextBody = "Adjunto los documentos." };
        foreach (var (n, c) in adjuntos) cuerpo.Attachments.Add(n, c);
        m.Body = cuerpo.ToMessageBody();
        ajustar?.Invoke(m);
        using var f = new MemoryStream();
        m.WriteTo(f);
        return f.ToArray();
    }
}

/// <summary>Procesador de facturas programado: devuelve el estado pedido o falla.</summary>
internal sealed class FacturasFalsas : IProcesadorFacturaCompraRecibida
{
    public EstadoAdjuntoRecibido Estado { get; set; } = EstadoAdjuntoRecibido.Procesado;
    public bool Fallar { get; set; }
    public List<string> Procesadas { get; } = [];

    public Task<ResultadoFacturaRecibida> ProcesarAsync(ContextoSociedadEjercicio contexto, string rutaPdf, string nombre, Actor actor, CancellationToken cancelacion = default)
    {
        if (Fallar) throw new IOException("carpeta no disponible");
        Procesadas.Add(nombre);
        return Task.FromResult(new ResultadoFacturaRecibida(Estado, 501, Estado == EstadoAdjuntoRecibido.Procesado ? 9001 : null,
            Estado == EstadoAdjuntoRecibido.Procesado ? "Factura F1: registrada y contabilizada (asiento 9001)." : "Factura F1: registrada, pendiente de revisar.",
            [new CampoReconocido("Número", "F1", 0.99m)]));
    }
}

/// <summary>
/// Recepción contra una copia de la BD (correo/recepcion, aceptación de spec § 23): sondeos repetidos continúan la misma
/// operación sin duplicar, remitente no autorizado no se procesa, lo que espera a un motor o falla no se borra del servidor y
/// el borrado es el último paso, tras la respuesta aceptada.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class RecepcionAccessTests : IDisposable
{
    private const int Ejercicio = 9201;
    private const string Autorizado = "gestor@example.invalid";
    private static readonly byte[] Pdf = "%PDF-1.4 factura recibida"u8.ToArray();
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly IBaseDatos _bd;
    private readonly string _documentos = BdPruebas.CarpetaTemporal();
    private readonly TransporteProgramado _smtp = new();
    private readonly BuzonFalso _buzon = new();
    private readonly FacturasFalsas _facturas = new();
    private readonly ServicioCorreo _correo;
    private readonly ServicioRecepcionCorreo _recepcion;
    private CuentaCorreo _cuenta = null!;

    public RecepcionAccessTests()
    {
        _bd = _copia.Bd();
        DatosSinteticos.SociedadAsync(_bd, "R01", "Recepcion Demo SL").GetAwaiter().GetResult();
        DatosSinteticos.EjercicioAsync(_bd, Ejercicio, "R01", 2026).GetAwaiter().GetResult();
        _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ? WHERE IdSociedad = 'R01'", _documentos)).GetAwaiter().GetResult();
        _correo = new ServicioCorreo(_bd, _smtp);
        _recepcion = new ServicioRecepcionCorreo(_bd, _buzon, _correo, _facturas);
        PrepararAsync().GetAwaiter().GetResult();
    }

    private async Task PrepararAsync()
    {
        var ctx = new ContextoMantenimiento(new ContextoSociedadEjercicio("R01", Ejercicio, 2026), "Recepcion Demo SL", new Actor("Prueba"));
        var p = new ProveedorCuentaCorreo(_bd, _smtp, new PruebaPop3MailKit());
        var r = (await p.ListarAsync(ctx)).Single();
        r["Direccion"] = "demo2026@example.invalid";
        r["ServidorSmtp"] = "smtp.example.invalid";
        r["ServidorPop3"] = "pop.example.invalid";
        r["Contrasena"] = "secreta";
        Assert.True((await p.GuardarAsync(r, ctx)).EsCorrecto);
        _cuenta = (await _correo.Cuentas.ObtenerPorEjercicioAsync(Ejercicio))!;
        var remitentes = ProveedoresCorreo.CrearRemitentes(_bd);
        var nuevo = await remitentes.NuevoAsync(ctx);
        nuevo["Direccion"] = Autorizado;
        nuevo["PuedePedirInformes"] = true;
        Assert.True((await remitentes.GuardarAsync(nuevo, ctx)).EsCorrecto);
    }

    private async Task<DetalleRecibido> UnicoAsync() => (await _recepcion.ObtenerAsync((await _recepcion.ListarAsync(Ejercicio)).Single().IdMensaje))!;

    [Fact]
    public async Task Factura_de_remitente_autorizado_se_procesa_responde_y_se_borra_del_servidor()
    {
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Factura", ("F1.pdf", Pdf));
        await _recepcion.SondearCuentaAsync(_cuenta);
        var m = await UnicoAsync();
        Assert.Equal(EstadoRecibido.Procesado, m.Estado);
        Assert.True(File.Exists(m.RutaEmlCompleta));
        var a = Assert.Single(m.Adjuntos);
        Assert.Equal(TipoAdjuntoRecibido.FacturaCompra, a.Tipo);
        Assert.Equal(EstadoAdjuntoRecibido.Procesado, a.Estado);
        Assert.Equal(9001, a.IdAsiento);
        Assert.Contains("asiento 9001", a.Detalle);
        // Respuesta al remitente con lo hecho.
        var respuesta = Assert.Single(_smtp.Enviados);
        Assert.Equal(Autorizado, respuesta.Destinatarios.Single().Direccion);
        Assert.Contains("F1.pdf", respuesta.CuerpoHtml);
        Assert.Equal(["u1"], _buzon.Borrados);
        Assert.True((await UnicoAsync()).EliminadoServidor);
    }

    [Fact]
    public async Task Sondeos_repetidos_no_duplican_nada()
    {
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Factura", ("F1.pdf", Pdf));
        var cuenta = _cuenta with { EliminarTrasProcesar = false };
        await _recepcion.SondearCuentaAsync(cuenta);
        await _recepcion.SondearCuentaAsync(cuenta);
        await _recepcion.SondearCuentaAsync(cuenta);
        Assert.Single(await _recepcion.ListarAsync(Ejercicio));
        Assert.Single(_facturas.Procesadas);
        Assert.Single(_smtp.Enviados);
        Assert.Equal(1, _buzon.Descargas);
        Assert.Empty(_buzon.Borrados);
    }

    [Fact]
    public async Task Remitente_no_autorizado_no_se_procesa_ni_responde_ni_se_borra()
    {
        _buzon.Mensajes["u1"] = BuzonFalso.Mime("desconocido@example.invalid", "Factura", ("F1.pdf", Pdf));
        await _recepcion.SondearCuentaAsync(_cuenta);
        var m = await UnicoAsync();
        Assert.Equal(EstadoRecibido.NoAutorizado, m.Estado);
        Assert.Contains("Remitentes autorizados", m.Resumen);
        Assert.Empty(_facturas.Procesadas);
        Assert.Empty(_smtp.Enviados);
        Assert.Empty(_buzon.Borrados);
        // Autorizado después, se vuelve a procesar.
        await _bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO RemitentesAutorizadosCorreo (IdSociedadEjercicio, Direccion, PuedeImportar, PuedePedirInformes, Activo) VALUES (?, ?, True, False, True)",
            Ejercicio, "desconocido@example.invalid"));
        Assert.True(await _recepcion.ReprocesarAsync(m.IdMensaje));
        Assert.Equal(EstadoRecibido.Procesado, (await UnicoAsync()).Estado);
        Assert.Single(_facturas.Procesadas);
    }

    [Fact]
    public async Task Norma43_y_nominas_esperan_a_su_modulo_y_no_se_borran()
    {
        var n43 = System.Text.Encoding.Latin1.GetBytes(("11" + new string('0', 78)) + "\r\n" + ("22" + new string('0', 78)) + "\r\n");
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Extracto y nóminas", ("extracto.n43", n43), ("nominas.xlsx", [1, 2, 3]));
        await _recepcion.SondearCuentaAsync(_cuenta);
        var m = await UnicoAsync();
        Assert.Equal(EstadoRecibido.EnEspera, m.Estado);
        Assert.Equal([TipoAdjuntoRecibido.Norma43, TipoAdjuntoRecibido.Nomina], m.Adjuntos.Select(a => a.Tipo));
        Assert.All(m.Adjuntos, a => Assert.Equal(EstadoAdjuntoRecibido.MotorPendiente, a.Estado));
        Assert.Single(_smtp.Enviados);
        Assert.Empty(_buzon.Borrados);
    }

    /// <summary>Procesador de nóminas programado: anota lo que recibe y devuelve el estado pedido.</summary>
    private sealed class NominasFalsas(EstadoAdjuntoRecibido estado) : IProcesadorNominasRecibidas
    {
        public List<(string Nombre, int IdAdjunto, bool Existe)> Recibidas { get; } = [];

        public Task<ResultadoNominasRecibidas> ProcesarAsync(ContextoSociedadEjercicio contexto, string ruta, string nombre, int idAdjunto, Actor actor,
            CancellationToken cancelacion = default)
        {
            Recibidas.Add((nombre, idAdjunto, File.Exists(ruta)));
            return Task.FromResult(new ResultadoNominasRecibidas(estado, estado == EstadoAdjuntoRecibido.Procesado ? 9002 : null,
                estado == EstadoAdjuntoRecibido.Procesado ? "Nóminas de octubre contabilizadas." : "Octubre: 1 trabajador sin resolver."));
        }
    }

    [Theory]
    [InlineData(EstadoAdjuntoRecibido.Procesado)]
    [InlineData(EstadoAdjuntoRecibido.Revisar)]
    public async Task El_Excel_de_nominas_va_a_su_modulo_con_la_copia_del_adjunto(EstadoAdjuntoRecibido resultado)
    {
        var nominas = new NominasFalsas(resultado);
        var recepcion = new ServicioRecepcionCorreo(_bd, _buzon, _correo, _facturas, nominas: nominas);
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Nóminas de octubre", ("nominas-octubre.xlsx", [1, 2, 3]));
        await recepcion.SondearCuentaAsync(_cuenta);
        var m = (await recepcion.ObtenerAsync((await recepcion.ListarAsync(Ejercicio)).Single().IdMensaje))!;
        var a = Assert.Single(m.Adjuntos);
        Assert.Equal((TipoAdjuntoRecibido.Nomina, resultado), (a.Tipo, a.Estado));
        Assert.Equal(("nominas-octubre.xlsx", a.IdAdjunto, true), Assert.Single(nominas.Recibidas));
        // Lo que queda «para revisar» ya está en la pantalla Nóminas: el mensaje está procesado.
        Assert.Equal(EstadoRecibido.Procesado, m.Estado);
    }

    [Fact]
    public async Task Un_fallo_deja_incidencia_y_se_reprocesa_sin_borrar()
    {
        _facturas.Fallar = true;
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Factura", ("F1.pdf", Pdf));
        await _recepcion.SondearCuentaAsync(_cuenta);
        var m = await UnicoAsync();
        Assert.Equal(EstadoRecibido.ConIncidencia, m.Estado);
        Assert.Equal(EstadoAdjuntoRecibido.Error, m.Adjuntos[0].Estado);
        Assert.Empty(_buzon.Borrados);
        _facturas.Fallar = false;
        Assert.True(await _recepcion.ReprocesarAsync(m.IdMensaje));
        Assert.Equal(EstadoRecibido.Procesado, (await UnicoAsync()).Estado);
        await _recepcion.SondearCuentaAsync(_cuenta);
        Assert.Equal(["u1"], _buzon.Borrados);
        Assert.Equal(2, _smtp.Enviados.Count);
    }

    [Fact]
    public async Task Respuesta_incierta_impide_borrar()
    {
        _smtp.Respuestas.Enqueue(ClaseResultado.Incierto);
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Factura", ("F1.pdf", Pdf));
        await _recepcion.SondearCuentaAsync(_cuenta);
        await _recepcion.SondearCuentaAsync(_cuenta);
        Assert.Equal(EstadoRecibido.Procesado, (await UnicoAsync()).Estado);
        Assert.Empty(_buzon.Borrados);
    }

    [Fact]
    public async Task Respuestas_automaticas_y_correo_propio_se_ignoran()
    {
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Fuera de la oficina", m => m.Headers.Add("Auto-Submitted", "auto-replied"));
        _buzon.Mensajes["u2"] = BuzonFalso.Mime("demo2026@example.invalid", "Re: Factura");
        await _recepcion.SondearCuentaAsync(_cuenta);
        var lista = await _recepcion.ListarAsync(Ejercicio);
        Assert.Equal(2, lista.Count);
        Assert.All(lista, f => Assert.Equal(EstadoRecibido.Ignorado, f.Estado));
        Assert.Empty(_smtp.Enviados);
        Assert.Equal(2, _buzon.Borrados.Count);
    }

    [Fact]
    public async Task Un_corte_antes_de_responder_se_completa_en_el_siguiente_sondeo()
    {
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "Factura", ("F1.pdf", Pdf));
        await _recepcion.SondearCuentaAsync(_cuenta with { EliminarTrasProcesar = false });
        // Simula el corte: la respuesta no llegó a encolarse.
        await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE MensajesRecibidosCorreo SET IdMensajeRespuesta = Null"));
        await _recepcion.SondearCuentaAsync(_cuenta);
        Assert.Single(_facturas.Procesadas);
        Assert.Equal(2, _smtp.Enviados.Count);
        Assert.Equal(["u1"], _buzon.Borrados);
    }

    [Fact]
    public async Task Comando_en_el_asunto_espera_a_los_informes()
    {
        _buzon.Mensajes["u1"] = BuzonFalso.Mime(Autorizado, "MAYOR 4300001 PDF");
        await _recepcion.SondearCuentaAsync(_cuenta);
        var m = await UnicoAsync();
        Assert.Equal(EstadoRecibido.EnEspera, m.Estado);
        Assert.Contains("informes por correo aún no están disponibles", m.Resumen);
        Assert.Contains("MAYOR 4300001 PDF", Assert.Single(_smtp.Enviados).CuerpoHtml);
    }

    public void Dispose()
    {
        _copia.Dispose();
        try
        {
            foreach (var f in Directory.EnumerateFiles(_documentos, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_documentos, true);
        }
        catch (IOException) { }
    }
}
