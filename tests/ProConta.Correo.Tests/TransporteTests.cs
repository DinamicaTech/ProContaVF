using System.IO;
namespace ProConta.Correo.Tests;

/// <summary>
/// Transporte SMTP contra un servidor local (sin red): cada fallo se clasifica según la fase en que ocurre, y un destinatario
/// rechazado no convierte en fallo total a los aceptados (aceptación de spec § 22).
/// </summary>
public sealed class TransporteTests : IDisposable
{
    private readonly ServidorSmtpFalso _servidor = new();
    private readonly TransporteSmtpMailKit _transporte = new() { ValidacionCertificado = (_, _, _, _) => true };

    private CuentaCorreo Cuenta(string? contrasena = "clave-de-prueba") => new()
    {
        Direccion = "empresa2026@example.invalid", ServidorSmtp = "127.0.0.1", PuertoSmtp = _servidor.Puerto, SeguridadSmtp = SeguridadConexion.StartTls,
        Contrasena = contrasena, TiempoEsperaSegundos = 5,
    };

    private static MensajeTransmision Mensaje(params string[] para) => new(
        $"{Guid.NewGuid():N}@example.invalid", "empresa2026@example.invalid", "Empresa Demo", null, "Factura F-1", "<p>Hola</p>", null,
        para.Select(p => new Destinatario(TipoDestinatario.Para, p)).ToList(), [("F-1.pdf", "application/pdf", "%PDF-1.4 prueba"u8.ToArray())]);

    [Fact]
    public async Task Aceptado_por_el_servidor()
    {
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.Aceptado, r.Clase);
        Assert.All(r.PorDestinatario, d => Assert.Equal(ResultadoDestinatario.Aceptado, d.Resultado));
        Assert.Contains("F-1.pdf", Assert.Single(_servidor.Recibidos));
    }

    [Fact]
    public async Task Un_destinatario_rechazado_no_es_fallo_total()
    {
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("cliente@example.invalid", "rechazo@example.invalid"));
        Assert.Equal(ClaseResultado.AceptadoEnParte, r.Clase);
        Assert.Equal(ResultadoDestinatario.Aceptado, r.PorDestinatario.Single(d => d.Direccion.StartsWith("cliente")).Resultado);
        Assert.Equal(ResultadoDestinatario.Rechazado, r.PorDestinatario.Single(d => d.Direccion.StartsWith("rechazo")).Resultado);
        Assert.Contains("550", r.PorDestinatario.Single(d => d.Direccion.StartsWith("rechazo")).Respuesta);
    }

    [Fact]
    public async Task Todos_rechazados_es_fallo_definitivo()
    {
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("rechazo@example.invalid"));
        Assert.Equal(ClaseResultado.FalloDefinitivo, r.Clase);
        Assert.Empty(_servidor.Recibidos);
    }

    [Fact]
    public async Task Rechazo_temporal_del_contenido_es_fallo_transitorio()
    {
        _servidor.Comportamiento = AlRecibirContenido.RechazarTemporal;
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.FalloTransitorio, r.Clase);
        Assert.Contains("451", r.RespuestaServidor);
    }

    [Fact]
    public async Task Rechazo_definitivo_del_contenido()
    {
        _servidor.Comportamiento = AlRecibirContenido.RechazarDefinitivo;
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.FalloDefinitivo, r.Clase);
    }

    [Fact]
    public async Task Corte_tras_mandar_el_contenido_es_incierto_y_no_afirma_no_enviado()
    {
        _servidor.Comportamiento = AlRecibirContenido.CortarConexion;
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.Incierto, r.Clase);
        Assert.Contains("no sé si se ha enviado", r.Explicacion);
    }

    [Fact]
    public async Task Contrasena_rechazada_es_fallo_definitivo_sin_enviar()
    {
        var r = await _transporte.EnviarAsync(Cuenta("otra"), Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.FalloDefinitivo, r.Clase);
        Assert.Equal("Autenticación", r.Fase);
        Assert.Empty(_servidor.RcptRecibidos);
    }

    [Fact]
    public async Task Sin_cifrado_no_se_manda_la_contrasena()
    {
        _servidor.OfrecerStartTls = false;
        var r = await _transporte.EnviarAsync(Cuenta(), Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.FalloDefinitivo, r.Clase);
        Assert.Empty(_servidor.RcptRecibidos);
    }

    [Fact]
    public async Task Servidor_que_no_responde_es_fallo_transitorio()
    {
        var cuenta = Cuenta() with { PuertoSmtp = 1 };
        var r = await _transporte.EnviarAsync(cuenta, Mensaje("cliente@example.invalid"));
        Assert.Equal(ClaseResultado.FalloTransitorio, r.Clase);
        Assert.Equal("Conexión", r.Fase);
    }

    [Fact]
    public async Task Probar_la_cuenta_no_envia_nada()
    {
        var bien = await _transporte.ProbarAsync(Cuenta());
        Assert.True(bien.Correcta, bien.Mensaje);
        var mal = await _transporte.ProbarAsync(Cuenta("otra"));
        Assert.False(mal.Correcta);
        Assert.Contains("contraseña", mal.Mensaje);
        Assert.Empty(_servidor.RcptRecibidos);
    }

    [Fact]
    public async Task El_cco_recibe_pero_no_aparece_en_las_cabeceras()
    {
        var m = Mensaje("cliente@example.invalid") with
        {
            Destinatarios = [new(TipoDestinatario.Para, "cliente@example.invalid"), new(TipoDestinatario.Cco, "oculto@example.invalid")],
        };
        var r = await _transporte.EnviarAsync(Cuenta(), m);
        Assert.Equal(ClaseResultado.Aceptado, r.Clase);
        Assert.Contains(_servidor.RcptRecibidos, l => l.Contains("oculto@example.invalid"));
        Assert.DoesNotContain("oculto@example.invalid", Assert.Single(_servidor.Recibidos));
        Assert.Contains(m.MessageId, _servidor.Recibidos[0]);
    }

    public void Dispose() => _servidor.Dispose();
}
