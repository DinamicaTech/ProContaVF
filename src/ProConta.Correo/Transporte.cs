using System.Net.Sockets;
using MailKit;
using MailKit.Net.Pop3;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ProConta.Correo;

/// <summary>Lo que se entrega al servidor SMTP en un intento: la instantánea guardada del mensaje, ya con el desvío aplicado.</summary>
public sealed record MensajeTransmision(
    string MessageId,
    string Remitente,
    string? NombreRemitente,
    string? ResponderA,
    string Asunto,
    string? CuerpoHtml,
    string? CuerpoTexto,
    IReadOnlyList<Destinatario> Destinatarios,
    IReadOnlyList<(string Nombre, string TipoContenido, byte[] Contenido)> Adjuntos);

/// <summary>Clase del resultado de un intento, según la evidencia que da el servidor (correo/envio 2026-11-10 19:40).</summary>
public enum ClaseResultado
{
    /// <summary>El servidor aceptó el mensaje para todos los destinatarios.</summary>
    Aceptado,
    /// <summary>Aceptado para unos destinatarios y rechazado para otros.</summary>
    AceptadoEnParte,
    /// <summary>Consta que no se envió y el fallo puede pasar solo (4xx, conexión): se reintenta según la política.</summary>
    FalloTransitorio,
    /// <summary>Consta que no se envió y repetir no lo arreglará sin corregir algo (5xx, credencial, configuración).</summary>
    FalloDefinitivo,
    /// <summary>La conexión se cortó después de mandar el contenido y antes de la respuesta: no se sabe si salió.</summary>
    Incierto,
}

/// <summary>Resultado de un destinatario en un intento, con la respuesta saneada del servidor.</summary>
public sealed record ResultadoDireccion(string Direccion, ResultadoDestinatario Resultado, string? Respuesta);

/// <summary>Evidencia de un intento: clase, fase alcanzada, respuesta del servidor y resultado por destinatario.</summary>
public sealed record ResultadoTransmision(
    ClaseResultado Clase,
    string Fase,
    string? RespuestaServidor,
    string Explicacion,
    IReadOnlyList<ResultadoDireccion> PorDestinatario,
    string? DetalleTecnico = null);

/// <summary>Resultado de probar una cuenta (conexión cifrada e inicio de sesión), para el operador.</summary>
public sealed record ResultadoPrueba(bool Correcta, string Mensaje, string? AccionCorrectora = null);

/// <summary>Transporte SMTP: el único sitio que habla con el servidor de salida.</summary>
public interface ITransporteSmtp
{
    Task<ResultadoTransmision> EnviarAsync(CuentaCorreo cuenta, MensajeTransmision mensaje, CancellationToken cancelacion = default);

    /// <summary>Conecta, cifra e inicia sesión sin enviar nada.</summary>
    Task<ResultadoPrueba> ProbarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default);
}

/// <summary>Prueba de la cuenta en el servidor POP3 de entrada (la recepción llega con correo/recepcion).</summary>
public interface IPruebaPop3
{
    Task<ResultadoPrueba> ProbarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default);
}

/// <summary>
/// Transporte SMTP con MailKit. Exige cifrado (STARTTLS obligatorio o TLS implícito), registra la respuesta del servidor a
/// cada destinatario sin abortar por uno rechazado, y clasifica cada fallo según la fase en que ocurre: antes de mandar el
/// contenido consta que no se envió; después de mandarlo y sin respuesta, es incierto.
/// </summary>
public sealed class TransporteSmtpMailKit : ITransporteSmtp
{
    /// <summary>Solo para las pruebas con un servidor local: acepta su certificado. En el programa es null (validación normal).</summary>
    internal System.Net.Security.RemoteCertificateValidationCallback? ValidacionCertificado { get; init; }

    public async Task<ResultadoTransmision> EnviarAsync(CuentaCorreo cuenta, MensajeTransmision mensaje, CancellationToken cancelacion = default)
    {
        using var mime = CrearMime(mensaje);
        using var cliente = new ClienteSmtpConEvidencia { Timeout = Math.Max(1, cuenta.TiempoEsperaSegundos) * 1000 };
        if (ValidacionCertificado is not null) cliente.ServerCertificateValidationCallback = ValidacionCertificado;
        var total = mensaje.Destinatarios.Select(d => d.Direccion.ToLowerInvariant()).Distinct().Count();
        var fase = "Conexión";
        try
        {
            if (string.IsNullOrWhiteSpace(cuenta.ServidorSmtp))
                return Fallo(ClaseResultado.FalloDefinitivo, fase, null, "La cuenta de correo no tiene servidor SMTP.", mensaje, cliente);
            if (string.IsNullOrEmpty(cuenta.Contrasena))
                return Fallo(ClaseResultado.FalloDefinitivo, fase, null, "La cuenta de correo no tiene contraseña guardada (o no se puede leer).", mensaje, cliente);
            await cliente.ConnectAsync(cuenta.ServidorSmtp.Trim(), cuenta.PuertoSmtp, Opcion(cuenta.SeguridadSmtp), cancelacion);
            fase = "Autenticación";
            await cliente.AuthenticateAsync(cuenta.UsuarioEfectivo, cuenta.Contrasena, cancelacion);
            fase = "Destinatarios";
            cliente.TotalDestinatarios = total;
            var respuesta = await cliente.SendAsync(mime, cancelacion);
            fase = "Fin";
            try { await cliente.DisconnectAsync(true, CancellationToken.None); } catch (Exception) { /* ya aceptado: el cierre no cambia nada */ }
            var rechazados = cliente.Resultados.Count(r => r.Resultado == ResultadoDestinatario.Rechazado);
            var clase = rechazados == 0 ? ClaseResultado.Aceptado : ClaseResultado.AceptadoEnParte;
            var explicacion = rechazados == 0
                ? "El servidor de correo lo ha aceptado."
                : $"El servidor de correo lo ha aceptado para {cliente.Resultados.Count - rechazados} destinatarios y ha rechazado {rechazados}.";
            return new ResultadoTransmision(clase, fase, Sanear(respuesta), explicacion, Completar(cliente.Resultados, mensaje));
        }
        catch (OperationCanceledException) when (cliente.ContenidoEnviado)
        {
            return Fallo(ClaseResultado.Incierto, "Contenido", null, "Se agotó el tiempo después de mandar el contenido, sin respuesta del servidor: no sé si se ha enviado.", mensaje, cliente);
        }
        catch (OperationCanceledException)
        {
            return Fallo(ClaseResultado.FalloTransitorio, fase, null, "Se agotó el tiempo antes de mandar el contenido: no se ha enviado.", mensaje, cliente);
        }
        catch (AuthenticationException ex)
        {
            return Fallo(ClaseResultado.FalloDefinitivo, fase, null, "El servidor ha rechazado el usuario o la contraseña de la cuenta de correo.", mensaje, cliente, ex);
        }
        catch (SslHandshakeException ex)
        {
            return Fallo(ClaseResultado.FalloDefinitivo, fase, null, "No he podido cifrar la conexión con el servidor (el puerto o la seguridad de la cuenta no coinciden con los del servidor).", mensaje, cliente, ex);
        }
        catch (NotSupportedException ex) when (fase == "Conexión")
        {
            return Fallo(ClaseResultado.FalloDefinitivo, fase, null, "El servidor no admite conexión cifrada (STARTTLS) en ese puerto, y sin cifrar no envío la contraseña.", mensaje, cliente, ex);
        }
        catch (SmtpCommandException ex)
        {
            var codigo = (int)ex.StatusCode;
            var respuesta = Sanear($"{codigo} {ex.Message}");
            var transitorio = codigo is >= 400 and < 500;
            // Rechazo explícito del servidor: consta que no se ha enviado, aunque fuera en la fase del contenido.
            return Fallo(transitorio ? ClaseResultado.FalloTransitorio : ClaseResultado.FalloDefinitivo, cliente.ContenidoEnviado ? "Contenido" : fase, respuesta,
                ex.ErrorCode switch
                {
                    SmtpErrorCode.SenderNotAccepted => $"El servidor no acepta el remitente {mensaje.Remitente}.",
                    SmtpErrorCode.RecipientNotAccepted => "El servidor ha rechazado a todos los destinatarios.",
                    _ => transitorio ? "El servidor de correo lo ha rechazado de momento (problema temporal)." : "El servidor de correo lo ha rechazado.",
                }, mensaje, cliente, ex);
        }
        catch (Exception ex) when (ex is IOException or SocketException or SmtpProtocolException or ServiceNotConnectedException or TimeoutException)
        {
            if (cliente.ContenidoEnviado)
                return Fallo(ClaseResultado.Incierto, "Contenido", null, "Se cortó la conexión después de mandar el contenido y antes de que el servidor respondiera: no sé si se ha enviado.", mensaje, cliente, ex);
            return Fallo(ClaseResultado.FalloTransitorio, fase, null,
                fase == "Conexión" ? $"No he podido conectar con el servidor {cuenta.ServidorSmtp}:{cuenta.PuertoSmtp}." : "Se cortó la conexión antes de mandar el contenido: no se ha enviado.",
                mensaje, cliente, ex);
        }
    }

    public async Task<ResultadoPrueba> ProbarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(cuenta.ServidorSmtp)) return new(false, "Falta el servidor SMTP.", "Escríbelo en la cuenta de correo y guarda.");
        if (string.IsNullOrEmpty(cuenta.Contrasena)) return new(false, "No hay contraseña guardada (o no se puede leer).", "Escribe la contraseña en la cuenta de correo y guarda.");
        using var cliente = new SmtpClient { Timeout = Math.Max(1, cuenta.TiempoEsperaSegundos) * 1000 };
        if (ValidacionCertificado is not null) cliente.ServerCertificateValidationCallback = ValidacionCertificado;
        try
        {
            await cliente.ConnectAsync(cuenta.ServidorSmtp.Trim(), cuenta.PuertoSmtp, Opcion(cuenta.SeguridadSmtp), cancelacion);
            await cliente.AuthenticateAsync(cuenta.UsuarioEfectivo, cuenta.Contrasena, cancelacion);
            await cliente.DisconnectAsync(true, cancelacion);
            return new(true, $"Envío preparado: la conexión cifrada con {cuenta.ServidorSmtp}:{cuenta.PuertoSmtp} y el inicio de sesión de {cuenta.UsuarioEfectivo} son correctos. No he enviado nada.");
        }
        catch (Exception ex)
        {
            return Diagnostico.Explicar("SMTP", cuenta.ServidorSmtp, cuenta.PuertoSmtp, ex);
        }
    }

    internal static SecureSocketOptions Opcion(SeguridadConexion s) =>
        s == SeguridadConexion.TlsImplicito ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    internal static MimeMessage CrearMime(MensajeTransmision m)
    {
        var mime = new MimeMessage { MessageId = m.MessageId, Subject = m.Asunto };
        mime.From.Add(new MailboxAddress(m.NombreRemitente ?? "", m.Remitente));
        if (!string.IsNullOrWhiteSpace(m.ResponderA)) mime.ReplyTo.Add(MailboxAddress.Parse(m.ResponderA));
        foreach (var d in m.Destinatarios)
        {
            var buzon = new MailboxAddress(d.Nombre ?? "", d.Direccion);
            (d.Tipo switch { TipoDestinatario.Para => mime.To, TipoDestinatario.Cc => mime.Cc, _ => mime.Bcc }).Add(buzon);
        }
        var cuerpo = new BodyBuilder { HtmlBody = m.CuerpoHtml, TextBody = m.CuerpoTexto ?? (m.CuerpoHtml is null ? "" : TextoPlano.DeHtml(m.CuerpoHtml)) };
        foreach (var (nombre, tipo, contenido) in m.Adjuntos) cuerpo.Attachments.Add(nombre, contenido, ContentType.Parse(tipo));
        mime.Body = cuerpo.ToMessageBody();
        return mime;
    }

    private static ResultadoTransmision Fallo(ClaseResultado clase, string fase, string? respuesta, string explicacion, MensajeTransmision m, ClienteSmtpConEvidencia cliente, Exception? ex = null) =>
        new(clase, fase, respuesta, explicacion, Completar(cliente.Resultados, m, clase == ClaseResultado.FalloDefinitivo || clase == ClaseResultado.FalloTransitorio),
            ex is null ? null : $"{ex.GetType().Name}: {Sanear(ex.Message)}");

    /// <summary>
    /// Un resultado por dirección del mensaje. Las que el servidor no llegó a contestar quedan sin resultado, salvo que conste
    /// que el mensaje no salió (fallo confirmado): entonces ninguna lo recibió, aunque el servidor hubiera aceptado su RCPT.
    /// </summary>
    private static List<ResultadoDireccion> Completar(IReadOnlyList<ResultadoDireccion> vistos, MensajeTransmision m, bool noSalio = false)
    {
        var lista = new List<ResultadoDireccion>();
        foreach (var d in m.Destinatarios.Select(d => d.Direccion).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var visto = vistos.FirstOrDefault(v => string.Equals(v.Direccion, d, StringComparison.OrdinalIgnoreCase));
            if (noSalio) lista.Add(new(d, visto?.Resultado == ResultadoDestinatario.Rechazado ? ResultadoDestinatario.Rechazado : ResultadoDestinatario.SinResultado, visto?.Respuesta));
            else lista.Add(visto ?? new ResultadoDireccion(d, ResultadoDestinatario.SinResultado, null));
        }
        return lista;
    }

    /// <summary>Texto de servidor sin saltos de línea y de longitud acotada (no contiene credenciales: solo respuestas SMTP).</summary>
    internal static string? Sanear(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpio = string.Join(" ", texto.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return limpio.Length > 250 ? limpio[..250] : limpio;
    }

    /// <summary>SmtpClient que anota la respuesta a cada destinatario y no aborta por uno rechazado.</summary>
    private sealed class ClienteSmtpConEvidencia : SmtpClient
    {
        private readonly List<ResultadoDireccion> _resultados = [];
        public IReadOnlyList<ResultadoDireccion> Resultados => _resultados;
        public int TotalDestinatarios { get; set; }

        /// <summary>Todos los destinatarios tienen respuesta y alguno se aceptó: a partir de aquí se manda el contenido.</summary>
        public bool ContenidoEnviado => TotalDestinatarios > 0 && _resultados.Count >= TotalDestinatarios && _resultados.Any(r => r.Resultado == ResultadoDestinatario.Aceptado);

        protected override void OnRecipientAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) =>
            _resultados.Add(new(mailbox.Address, ResultadoDestinatario.Aceptado, Sanear($"{(int)response.StatusCode} {response.Response}")));

        protected override void OnRecipientNotAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) =>
            _resultados.Add(new(mailbox.Address, ResultadoDestinatario.Rechazado, Sanear($"{(int)response.StatusCode} {response.Response}")));
    }
}

/// <summary>Prueba POP3 con MailKit: conexión cifrada e inicio de sesión, sin descargar ni borrar nada.</summary>
public sealed class PruebaPop3MailKit : IPruebaPop3
{
    public async Task<ResultadoPrueba> ProbarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(cuenta.ServidorPop3)) return new(false, "Falta el servidor POP3.", "Escríbelo en la cuenta de correo y guarda.");
        if (string.IsNullOrEmpty(cuenta.Contrasena)) return new(false, "No hay contraseña guardada (o no se puede leer).", "Escribe la contraseña en la cuenta de correo y guarda.");
        using var cliente = new Pop3Client { Timeout = Math.Max(10, cuenta.TiempoEsperaSegundos) * 1000 };
        try
        {
            await cliente.ConnectAsync(cuenta.ServidorPop3.Trim(), cuenta.PuertoPop3, TransporteSmtpMailKit.Opcion(cuenta.SeguridadPop3), cancelacion);
            await cliente.AuthenticateAsync(cuenta.UsuarioEfectivo, cuenta.Contrasena, cancelacion);
            var mensajes = cliente.Count;
            await cliente.DisconnectAsync(true, cancelacion);
            return new(true, $"Recepción preparada: la conexión cifrada con {cuenta.ServidorPop3}:{cuenta.PuertoPop3} y el inicio de sesión son correctos. El buzón tiene {mensajes} {(mensajes == 1 ? "mensaje" : "mensajes")}; no he descargado ni borrado nada.");
        }
        catch (Exception ex)
        {
            return Diagnostico.Explicar("POP3", cuenta.ServidorPop3, cuenta.PuertoPop3, ex);
        }
    }
}

/// <summary>Explicación para el operador de un fallo al conectar con el servidor, sin datos técnicos sensibles.</summary>
internal static class Diagnostico
{
    public static ResultadoPrueba Explicar(string protocolo, string servidor, int puerto, Exception ex) => ex switch
    {
        AuthenticationException => new(false, $"{protocolo}: el servidor ha rechazado el usuario o la contraseña.", "Revisa el usuario (vacío, se usa la dirección) y vuelve a escribir la contraseña."),
        SslHandshakeException => new(false, $"{protocolo}: no he podido cifrar la conexión con {servidor}:{puerto}.", "Comprueba la seguridad de la cuenta: STARTTLS para los puertos 587 y 110, TLS implícito para 465 y 995."),
        NotSupportedException => new(false, $"{protocolo}: el servidor no admite conexión cifrada en el puerto {puerto}, y sin cifrar no envío la contraseña.", "Usa el puerto cifrado del servidor (465 o 995 con TLS implícito)."),
        SocketException or IOException => new(false, $"{protocolo}: no he podido conectar con {servidor}:{puerto}.", "Comprueba el nombre del servidor, el puerto y la conexión a Internet, y vuelve a probar."),
        OperationCanceledException or TimeoutException => new(false, $"{protocolo}: el servidor {servidor} no ha respondido a tiempo.", "Vuelve a probar; si sigue igual, revisa el servidor y el puerto."),
        _ => new(false, $"{protocolo}: el servidor no ha completado la conexión ({ex.GetType().Name}).", "Revisa servidor, puerto, seguridad y contraseña, y vuelve a probar."),
    };
}

/// <summary>Versión de texto de un cuerpo HTML, para la parte alternativa del mensaje.</summary>
public static class TextoPlano
{
    public static string DeHtml(string html)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h\d)\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<\s*li[^>]*>", "• ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "");
        t = System.Net.WebUtility.HtmlDecode(t);
        return System.Text.RegularExpressions.Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
    }
}
