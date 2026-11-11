using System.IO;
using System.Security.Cryptography;
using ProConta.Correo.Mantenimiento;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Correo.Tests;

/// <summary>Transporte programado para las pruebas de la cola: devuelve la clase pedida y anota lo que recibe.</summary>
internal sealed class TransporteProgramado : ITransporteSmtp
{
    public Queue<ClaseResultado> Respuestas { get; } = new();
    public List<MensajeTransmision> Enviados { get; } = [];

    public Task<ResultadoTransmision> EnviarAsync(CuentaCorreo cuenta, MensajeTransmision mensaje, CancellationToken cancelacion = default)
    {
        Enviados.Add(mensaje);
        var clase = Respuestas.Count > 0 ? Respuestas.Dequeue() : ClaseResultado.Aceptado;
        var porDestinatario = mensaje.Destinatarios.Select(d => new ResultadoDireccion(d.Direccion,
            clase is ClaseResultado.Aceptado or ClaseResultado.AceptadoEnParte ? ResultadoDestinatario.Aceptado : ResultadoDestinatario.SinResultado, "250 ok")).ToList();
        if (clase == ClaseResultado.AceptadoEnParte) porDestinatario[^1] = porDestinatario[^1] with { Resultado = ResultadoDestinatario.Rechazado, Respuesta = "550 no existe" };
        return Task.FromResult(new ResultadoTransmision(clase, "Fin", "250 ok", $"Resultado programado: {clase}.", porDestinatario));
    }

    public Task<ResultadoPrueba> ProbarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default) => Task.FromResult(new ResultadoPrueba(true, "ok"));
}

/// <summary>Reloj que avanza cuando se le pide.</summary>
internal sealed class RelojManual(DateTime ahora) : IReloj
{
    public DateTime Ahora { get; set; } = ahora;
}

/// <summary>
/// Cola de correo saliente contra una copia de la BD (correo/envio): instantánea y adjuntos con huella antes de enviar,
/// reintentos solo ante fallo transitorio, incierto sin reintento automático, turno entre sesiones, desvío de pruebas,
/// reintentar, reenviar, cancelar y resolver un incierto, y la cuenta de correo en Parámetros y tablas.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ColaAccessTests : IDisposable
{
    private const int Ejercicio = 9101;
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly IBaseDatos _bd;
    private readonly string _documentos = BdPruebas.CarpetaTemporal();
    private readonly TransporteProgramado _transporte = new();
    private readonly RelojManual _reloj = new(new DateTime(2026, 11, 10, 10, 0, 0));
    private readonly ServicioCorreo _servicio;
    private readonly Actor _actor = new("Prueba");

    public ColaAccessTests()
    {
        _bd = _copia.Bd();
        DatosSinteticos.SociedadAsync(_bd, "C01", "Correo Demo SL").GetAwaiter().GetResult();
        DatosSinteticos.EjercicioAsync(_bd, Ejercicio, "C01", 2026).GetAwaiter().GetResult();
        _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ? WHERE IdSociedad = 'C01'", _documentos)).GetAwaiter().GetResult();
        _servicio = new ServicioCorreo(_bd, _transporte, _reloj);
    }

    private ContextoMantenimiento Contexto => new(new ContextoSociedadEjercicio("C01", Ejercicio, 2026), "Correo Demo SL", _actor);

    private async Task CrearCuentaAsync(string? contrasena = "secreta")
    {
        var p = new ProveedorCuentaCorreo(_bd, _transporte, new PruebaPop3MailKit());
        var r = (await p.ListarAsync(Contexto)).Single();
        r["Direccion"] = "demo2026@example.invalid";
        r["ServidorSmtp"] = "smtp.example.invalid";
        r["ServidorPop3"] = "pop.example.invalid";
        r["Contrasena"] = contrasena;
        Assert.Empty(p.Validar(r));
        Assert.True((await p.GuardarAsync(r, Contexto)).EsCorrecto);
    }

    private static MensajeNuevo Mensaje(params string[] para) => new()
    {
        IdSociedadEjercicio = Ejercicio, TipoCorreo = TiposCorreo.Factura, TipoOrigen = "Factura", IdOrigen = 7, ReferenciaOrigen = "F-7",
        Asunto = "Factura F-7", CuerpoHtml = "<p>Adjunta la factura.</p>",
        Destinatarios = para.Select(p => new Destinatario(TipoDestinatario.Para, p)).ToList(),
        Adjuntos = [new AdjuntoNuevo("F-7.pdf", "%PDF-1.4 factura de prueba"u8.ToArray())],
    };

    private async Task<int> EncolarAsync(params string[] para)
    {
        var r = await _servicio.EncolarAsync(Mensaje(para.Length == 0 ? ["cliente@example.invalid"] : para), _actor);
        Assert.True(r.EsCorrecto, r.Error?.TextoOperador());
        return r.Valor;
    }

    [Fact]
    public async Task Sin_cuenta_no_encola_y_dice_donde_configurarla()
    {
        var r = await _servicio.EncolarAsync(Mensaje("cliente@example.invalid"), _actor);
        Assert.False(r.EsCorrecto);
        Assert.Equal("Correo.SinCuenta", r.Error!.Codigo);
        Assert.Contains("Cuenta de correo", r.Error.AccionCorrectora);
    }

    [Fact]
    public async Task La_contrasena_se_guarda_cifrada_y_no_vuelve_a_la_pantalla()
    {
        await CrearCuentaAsync();
        var guardado = await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT SecretoProtegido FROM CuentasCorreo")) as string;
        Assert.DoesNotContain("secreta", guardado);
        var p = new ProveedorCuentaCorreo(_bd, _transporte, new PruebaPop3MailKit());
        var r = (await p.ListarAsync(Contexto)).Single();
        Assert.Null(r["Contrasena"]);
        Assert.Equal("Sí", r["EstadoContrasena"]);
        Assert.Equal("secreta", (await _servicio.Cuentas.ObtenerPorEjercicioAsync(Ejercicio))!.Contrasena);
        // Guardar sin escribirla conserva la guardada.
        r["NombreVisible"] = "Otro nombre";
        Assert.True((await p.GuardarAsync(r, Contexto)).EsCorrecto);
        Assert.Equal("secreta", (await _servicio.Cuentas.ObtenerPorEjercicioAsync(Ejercicio))!.Contrasena);
    }

    [Fact]
    public async Task Encolar_guarda_la_instantanea_y_una_copia_inmutable_del_adjunto()
    {
        await CrearCuentaAsync();
        var id = await EncolarAsync();
        var d = (await _servicio.ObtenerAsync(id))!;
        Assert.Equal(EstadoMensaje.Pendiente, d.Estado);
        Assert.EndsWith("@example.invalid", d.MessageId);
        Assert.Equal("demo2026@example.invalid", d.Remitente);
        var adjunto = Assert.Single(d.Adjuntos);
        Assert.True(File.Exists(adjunto.RutaCompleta));
        Assert.True(File.GetAttributes(adjunto.RutaCompleta!).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(adjunto.RutaCompleta!))), adjunto.Huella);
        Assert.StartsWith(Path.Combine("2026", "Correo"), adjunto.RutaRelativa);
    }

    [Fact]
    public async Task Enviar_ahora_aceptado()
    {
        await CrearCuentaAsync();
        var id = await EncolarAsync();
        var r = await _servicio.EnviarAhoraAsync(id);
        Assert.Equal(EstadoMensaje.Aceptado, r.Estado);
        var d = (await _servicio.ObtenerAsync(id))!;
        Assert.NotNull(d.FechaAceptacion);
        Assert.Equal(ResultadoDestinatario.Aceptado, Assert.Single(d.Destinatarios).Resultado);
        Assert.Equal("Aceptado", Assert.Single(d.HistorialIntentos).Resultado);
        // Enviarlo otra vez no lo duplica: ya no está pendiente.
        await _servicio.EnviarAhoraAsync(id);
        Assert.Single(_transporte.Enviados);
    }

    [Fact]
    public async Task Aceptado_en_parte_no_es_fallo()
    {
        await CrearCuentaAsync();
        _transporte.Respuestas.Enqueue(ClaseResultado.AceptadoEnParte);
        var id = await EncolarAsync("a@example.invalid", "b@example.invalid");
        Assert.Equal(EstadoMensaje.AceptadoEnParte, (await _servicio.EnviarAhoraAsync(id)).Estado);
        var d = (await _servicio.ObtenerAsync(id))!;
        Assert.Contains(d.Destinatarios, x => x.Resultado == ResultadoDestinatario.Rechazado);
        Assert.Contains(d.Destinatarios, x => x.Resultado == ResultadoDestinatario.Aceptado);
    }

    [Fact]
    public async Task Fallo_transitorio_se_reintenta_cuatro_veces_y_despues_queda_fallido()
    {
        await CrearCuentaAsync();
        for (var i = 0; i < 5; i++) _transporte.Respuestas.Enqueue(ClaseResultado.FalloTransitorio);
        var id = await EncolarAsync();
        var r = await _servicio.EnviarAhoraAsync(id);
        Assert.Equal(EstadoMensaje.Pendiente, r.Estado);
        Assert.Equal(_reloj.Ahora.AddMinutes(5), r.ProximoIntento);
        // Antes de su hora no se toca.
        Assert.Equal(0, await _servicio.ProcesarPendientesAsync());
        foreach (var espera in new[] { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(16) })
        {
            _reloj.Ahora += espera;
            Assert.Equal(1, await _servicio.ProcesarPendientesAsync());
        }
        var d = (await _servicio.ObtenerAsync(id))!;
        Assert.Equal(EstadoMensaje.Fallido, d.Estado);
        Assert.Equal(5, d.HistorialIntentos.Count);
        Assert.Contains("reintentarlo", d.UltimoError);
        // Todos los intentos con el mismo Message-ID.
        Assert.Single(_transporte.Enviados.Select(e => e.MessageId).Distinct());
    }

    [Fact]
    public async Task Incierto_no_se_reintenta_solo_y_se_resuelve_con_motivo()
    {
        await CrearCuentaAsync();
        _transporte.Respuestas.Enqueue(ClaseResultado.Incierto);
        var id = await EncolarAsync();
        Assert.Equal(EstadoMensaje.Incierto, (await _servicio.EnviarAhoraAsync(id)).Estado);
        _reloj.Ahora = _reloj.Ahora.AddDays(1);
        Assert.Equal(0, await _servicio.ProcesarPendientesAsync());
        Assert.False((await _servicio.ResolverInciertoAsync(id, true, " ", _actor)).EsCorrecto);
        Assert.True((await _servicio.ResolverInciertoAsync(id, true, "El cliente confirma que lo recibió", _actor)).EsCorrecto);
        var d = (await _servicio.ObtenerAsync(id))!;
        Assert.Equal(EstadoMensaje.Aceptado, d.Estado);
        Assert.Equal("Prueba", d.ResueltoPor);
        Assert.Contains("confirma", d.MotivoResolucion);
        // Resolverlo otra vez no hace nada.
        Assert.False((await _servicio.ResolverInciertoAsync(id, false, "otra vez", _actor)).EsCorrecto);
    }

    [Fact]
    public async Task Incierto_vuelto_a_enviar_es_otro_intento_del_mismo_mensaje()
    {
        await CrearCuentaAsync();
        _transporte.Respuestas.Enqueue(ClaseResultado.Incierto);
        var id = await EncolarAsync();
        await _servicio.EnviarAhoraAsync(id);
        Assert.True((await _servicio.ResolverInciertoAsync(id, false, "No le ha llegado", _actor)).EsCorrecto);
        Assert.Equal(1, await _servicio.ProcesarPendientesAsync());
        var d = (await _servicio.ObtenerAsync(id))!;
        Assert.Equal(EstadoMensaje.Aceptado, d.Estado);
        Assert.Equal(2, d.HistorialIntentos.Count);
    }

    [Fact]
    public async Task Envio_abandonado_a_medias_pasa_a_incierto()
    {
        await CrearCuentaAsync();
        var id = await EncolarAsync();
        await _bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE MensajesCorreo SET Estado = 'EnProceso', NumeroIntentos = 1 WHERE IdMensajeCorreo = ?", id);
            return await s.EjecutarAsync("INSERT INTO IntentosMensajeCorreo (IdMensajeCorreo, Numero, Inicio, Resultado) VALUES (?, 1, ?, 'EnCurso')", id, _reloj.Ahora);
        });
        _reloj.Ahora = _reloj.Ahora.AddMinutes(20);
        await _servicio.ProcesarPendientesAsync();
        Assert.Equal(EstadoMensaje.Incierto, await _servicio.EstadoAsync(id));
        Assert.Empty(_transporte.Enviados);
    }

    [Fact]
    public async Task Solo_una_sesion_procesa_la_cola()
    {
        await CrearCuentaAsync();
        var otra = new ServicioCorreo(_bd, _transporte, _reloj);
        Assert.True(await _servicio.TomarTurnoAsync());
        Assert.False(await otra.TomarTurnoAsync());
        await EncolarAsync();
        Assert.Equal(0, await otra.ProcesarPendientesAsync());
        // Si la primera desaparece, el turno caduca y la otra lo toma.
        _reloj.Ahora = _reloj.Ahora.AddMinutes(4);
        Assert.Equal(1, await otra.ProcesarPendientesAsync());
        await otra.SoltarTurnoAsync();
        Assert.True(await _servicio.TomarTurnoAsync());
    }

    [Fact]
    public async Task Reintentar_reenviar_y_cancelar_segun_el_estado()
    {
        await CrearCuentaAsync();
        _transporte.Respuestas.Enqueue(ClaseResultado.FalloDefinitivo);
        var fallido = await EncolarAsync();
        await _servicio.EnviarAhoraAsync(fallido);
        var aceptado = await EncolarAsync();
        await _servicio.EnviarAhoraAsync(aceptado);
        var pendiente = await EncolarAsync();

        Assert.Equal([fallido], await _servicio.ReintentarAsync([fallido, aceptado, pendiente]));
        Assert.Equal(EstadoMensaje.Pendiente, await _servicio.EstadoAsync(fallido));

        var nuevos = await _servicio.ReenviarAsync([fallido, aceptado], _actor);
        var reenvio = Assert.Single(nuevos);
        var d = (await _servicio.ObtenerAsync(reenvio))!;
        Assert.Equal(aceptado, d.IdMensajeOriginal);
        Assert.NotEqual((await _servicio.ObtenerAsync(aceptado))!.MessageId, d.MessageId);
        Assert.Equal((await _servicio.ObtenerAsync(aceptado))!.Adjuntos[0].Huella, Assert.Single(d.Adjuntos).Huella);

        Assert.Equal([pendiente], await _servicio.CancelarAsync([pendiente, aceptado], _actor));
        Assert.Equal(EstadoMensaje.Cancelado, await _servicio.EstadoAsync(pendiente));
        Assert.Empty(await _servicio.CancelarAsync([pendiente], _actor));
    }

    [Fact]
    public async Task Con_desvio_solo_sale_a_la_direccion_de_pruebas()
    {
        await CrearCuentaAsync();
        await _bd.EjecutarAsync(async s =>
        {
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0)
                await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, OcultarListaOperadoresAcceso) VALUES (1, False)");
            return await s.EjecutarAsync("UPDATE Configuracion SET CorreoDesviarA = 'pruebas@example.invalid'");
        });
        var id = await EncolarAsync("cliente@example.invalid");
        var r = await _servicio.EnviarAhoraAsync(id);
        var enviado = Assert.Single(_transporte.Enviados);
        Assert.Equal("pruebas@example.invalid", Assert.Single(enviado.Destinatarios).Direccion);
        Assert.StartsWith("[PRUEBAS]", enviado.Asunto);
        Assert.Contains("cliente@example.invalid", enviado.CuerpoHtml);
        Assert.Contains("Desviado", r.Explicacion);
        Assert.Equal("pruebas@example.invalid", (await _servicio.ObtenerAsync(id))!.DesviadoA);
    }

    [Fact]
    public async Task Adjunto_alterado_no_se_envia()
    {
        await CrearCuentaAsync();
        var id = await EncolarAsync();
        var ruta = (await _servicio.ObtenerAsync(id))!.Adjuntos[0].RutaCompleta!;
        File.SetAttributes(ruta, FileAttributes.Normal);
        await File.WriteAllTextAsync(ruta, "otro contenido");
        var r = await _servicio.EnviarAhoraAsync(id);
        Assert.Equal(EstadoMensaje.Fallido, r.Estado);
        Assert.Contains("huella", r.Explicacion);
        Assert.Empty(_transporte.Enviados);
    }

    [Fact]
    public async Task El_registro_lista_por_lote_y_por_origen()
    {
        await CrearCuentaAsync();
        var lote = await _servicio.CrearLoteAsync(Ejercicio, "Facturas de noviembre", "Facturas", _actor);
        var enLote = (await _servicio.EncolarAsync(Mensaje("a@example.invalid", "b@example.invalid"), _actor, lote)).Valor;
        await EncolarAsync();
        var delLote = Assert.Single(await _servicio.ListarAsync(new FiltroRegistro(IdLote: lote)));
        Assert.Equal(enLote, delLote.IdMensaje);
        Assert.Equal("a@example.invalid; b@example.invalid", delLote.Para);
        Assert.Equal(1, delLote.Adjuntos);
        Assert.Equal(2, (await _servicio.ListarAsync(new FiltroRegistro(IdSociedadEjercicio: Ejercicio))).Count);
        Assert.Equal(2, (await _servicio.ListarAsync(new FiltroRegistro(TipoOrigen: "Factura", IdOrigen: 7))).Count);
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
