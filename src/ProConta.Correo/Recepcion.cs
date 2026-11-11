using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Pop3;
using MimeKit;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Correo;

// ---------- transporte POP3 ----------

/// <summary>Un mensaje del buzón: su identidad estable (UIDL) y su tamaño.</summary>
public sealed record MensajeRemoto(string Uidl, long TamanoBytes);

/// <summary>Buzón POP3 de una cuenta: listar por UIDL, descargar el MIME original y borrar lo ya procesado.</summary>
public interface IClientePop3
{
    Task<IReadOnlyList<MensajeRemoto>> ListarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default);

    /// <summary>El MIME completo del mensaje; null si ya no está en el buzón.</summary>
    Task<byte[]?> DescargarAsync(CuentaCorreo cuenta, string uidl, CancellationToken cancelacion = default);

    /// <summary>Borra los mensajes indicados (los que sigan en el buzón) y devuelve los UIDL borrados.</summary>
    Task<IReadOnlyList<string>> EliminarAsync(CuentaCorreo cuenta, IReadOnlyList<string> uidls, CancellationToken cancelacion = default);
}

/// <summary>POP3 con MailKit, siempre cifrado (STARTTLS obligatorio o TLS implícito). Cada operación abre y cierra su sesión.</summary>
public sealed class ClientePop3MailKit : IClientePop3
{
    public async Task<IReadOnlyList<MensajeRemoto>> ListarAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default)
    {
        using var cliente = await ConectarAsync(cuenta, cancelacion);
        var uidls = await cliente.GetMessageUidsAsync(cancelacion);
        var tamanos = await cliente.GetMessageSizesAsync(cancelacion);
        await cliente.DisconnectAsync(true, cancelacion);
        return uidls.Select((u, i) => new MensajeRemoto(u, i < tamanos.Count ? tamanos[i] : 0)).ToList();
    }

    public async Task<byte[]?> DescargarAsync(CuentaCorreo cuenta, string uidl, CancellationToken cancelacion = default)
    {
        using var cliente = await ConectarAsync(cuenta, cancelacion);
        var indice = (await cliente.GetMessageUidsAsync(cancelacion)).ToList().IndexOf(uidl);
        if (indice < 0) { await cliente.DisconnectAsync(true, cancelacion); return null; }
        using var flujo = new MemoryStream();
        using (var mime = await cliente.GetMessageAsync(indice, cancelacion)) await mime.WriteToAsync(flujo, cancelacion);
        await cliente.DisconnectAsync(true, cancelacion);
        return flujo.ToArray();
    }

    public async Task<IReadOnlyList<string>> EliminarAsync(CuentaCorreo cuenta, IReadOnlyList<string> uidls, CancellationToken cancelacion = default)
    {
        if (uidls.Count == 0) return [];
        using var cliente = await ConectarAsync(cuenta, cancelacion);
        var actuales = (await cliente.GetMessageUidsAsync(cancelacion)).ToList();
        var borrados = new List<string>();
        foreach (var u in uidls)
        {
            var i = actuales.IndexOf(u);
            if (i < 0) continue;
            await cliente.DeleteMessageAsync(i, cancelacion);
            borrados.Add(u);
        }
        // POP3 solo confirma el borrado al cerrar la sesión con QUIT.
        await cliente.DisconnectAsync(true, cancelacion);
        return borrados;
    }

    private static async Task<Pop3Client> ConectarAsync(CuentaCorreo cuenta, CancellationToken cancelacion)
    {
        if (string.IsNullOrWhiteSpace(cuenta.ServidorPop3)) throw new InvalidOperationException("La cuenta de correo no tiene servidor POP3.");
        if (string.IsNullOrEmpty(cuenta.Contrasena)) throw new InvalidOperationException("La cuenta de correo no tiene contraseña guardada (o no se puede leer).");
        var cliente = new Pop3Client { Timeout = Math.Max(10, cuenta.TiempoEsperaSegundos) * 1000 };
        try
        {
            await cliente.ConnectAsync(cuenta.ServidorPop3.Trim(), cuenta.PuertoPop3, TransporteSmtpMailKit.Opcion(cuenta.SeguridadPop3), cancelacion);
            await cliente.AuthenticateAsync(cuenta.UsuarioEfectivo, cuenta.Contrasena, cancelacion);
            return cliente;
        }
        catch
        {
            cliente.Dispose();
            throw;
        }
    }
}

// ---------- modelo ----------

/// <summary>Qué es un adjunto recibido, según su nombre y su contenido (spec § 23 RF-DIS-06).</summary>
public enum TipoAdjuntoRecibido { FacturaCompra, Norma43, Nomina, NoReconocido }

/// <summary>Estado de un adjunto recibido.</summary>
public enum EstadoAdjuntoRecibido
{
    /// <summary>Aún no se ha procesado.</summary>
    Pendiente,
    /// <summary>Procesado entero (una factura registrada y contabilizada).</summary>
    Procesado,
    /// <summary>Registrado, pero alguien tiene que revisarlo (confianza baja o falta un dato).</summary>
    Revisar,
    /// <summary>Su motor aún no existe en ProContaVF: se conserva hasta que llegue.</summary>
    MotorPendiente,
    /// <summary>No es nada que ProContaVF procese; se conserva con el mensaje.</summary>
    NoReconocido,
    /// <summary>Falló al procesarlo: se puede volver a procesar.</summary>
    Error,
}

/// <summary>Estado de un mensaje recibido.</summary>
public enum EstadoRecibido
{
    /// <summary>Descargado y guardado, pendiente de procesar.</summary>
    Descargado,
    /// <summary>Procesado entero: se puede borrar del servidor.</summary>
    Procesado,
    /// <summary>Algo espera a un motor que aún no existe (Norma 43, nóminas, informes): se conserva en el servidor.</summary>
    EnEspera,
    /// <summary>Algún adjunto falló: se conserva en el servidor y se puede volver a procesar.</summary>
    ConIncidencia,
    /// <summary>El remitente no está autorizado: no se procesa ni se responde.</summary>
    NoAutorizado,
    /// <summary>Respuesta automática, aviso de no entrega o correo propio: no se procesa.</summary>
    Ignorado,
}

/// <summary>Remitente autorizado de un ejercicio, con lo que puede pedir.</summary>
public sealed record RemitenteAutorizado(string Direccion, bool PuedeImportar, bool PuedePedirInformes);

/// <summary>Un campo reconocido de una factura, con su confianza (0-1) si la hay.</summary>
public sealed record CampoReconocido(string Campo, string Valor, decimal? Confianza);

/// <summary>Lo que ha pasado con una factura de compra recibida por correo.</summary>
public sealed record ResultadoFacturaRecibida(
    EstadoAdjuntoRecibido Estado,
    int? IdFacturaCompra,
    int? IdAsiento,
    string Explicacion,
    IReadOnlyList<CampoReconocido> Campos);

/// <summary>
/// Procesa una factura de compra recibida por correo (lo implementa compras, correo/recepcion 2026-11-10 19:40): la deja en
/// la carpeta raíz de proveedores para el escaneo, la reconoce, la registra y, si todos los campos alcanzan el 95 % de
/// confianza, la contabiliza. Repetirlo con el mismo PDF no crea otra factura.
/// </summary>
public interface IProcesadorFacturaCompraRecibida
{
    Task<ResultadoFacturaRecibida> ProcesarAsync(ContextoSociedadEjercicio contexto, string rutaPdf, string nombre, Actor actor, CancellationToken cancelacion = default);
}

/// <summary>Contenido funcional de un MIME recibido.</summary>
public sealed record MensajeLeido(string? MessageId, string? Remitente, string? NombreRemitente, string? Asunto, DateTime? Fecha, bool EsAutomatico,
    IReadOnlyList<(string Nombre, byte[] Contenido)> Adjuntos);

/// <summary>Lectura del MIME y clasificación de adjuntos, sin BD ni red.</summary>
public static class LectorCorreo
{
    public static MensajeLeido Leer(byte[] mime)
    {
        using var flujo = new MemoryStream(mime);
        using var m = MimeMessage.Load(flujo);
        var de = m.From.Mailboxes.FirstOrDefault();
        var cab = m.Headers;
        var automatico =
            (cab[HeaderId.AutoSubmitted] is { } auto && !auto.Trim().Equals("no", StringComparison.OrdinalIgnoreCase)) ||
            (cab["Precedence"] is { } p && p.Trim().ToLowerInvariant() is "bulk" or "junk" or "auto_reply" or "list") ||
            cab["X-Autoreply"] is not null || cab["X-Autorespond"] is not null ||
            (de?.Address is { } d && (d.StartsWith("mailer-daemon@", StringComparison.OrdinalIgnoreCase) || d.StartsWith("postmaster@", StringComparison.OrdinalIgnoreCase))) ||
            m.Body is MultipartReport;
        var adjuntos = new List<(string, byte[])>();
        var n = 0;
        foreach (var parte in m.Attachments.OfType<MimePart>().Concat(m.BodyParts.OfType<MimePart>().Where(b => !b.IsAttachment && b.FileName is not null)).Distinct())
        {
            n++;
            using var contenido = new MemoryStream();
            parte.Content?.DecodeTo(contenido);
            adjuntos.Add((string.IsNullOrWhiteSpace(parte.FileName) ? $"adjunto-{n}" : parte.FileName, contenido.ToArray()));
        }
        return new MensajeLeido(m.MessageId, de?.Address?.Trim(), string.IsNullOrWhiteSpace(de?.Name) ? null : de.Name, m.Subject,
            m.Date == DateTimeOffset.MinValue ? null : m.Date.LocalDateTime, automatico, adjuntos);
    }

    /// <summary>Tipo de un adjunto: PDF → factura de compra; fichero Norma 43 (registros de 80 caracteres que empiezan por 11); Excel → nóminas.</summary>
    public static TipoAdjuntoRecibido Clasificar(string nombre, byte[] contenido)
    {
        var ext = Path.GetExtension(nombre).ToLowerInvariant();
        if (ext == ".pdf" || (contenido.Length > 4 && contenido[0] == '%' && contenido[1] == 'P' && contenido[2] == 'D' && contenido[3] == 'F')) return TipoAdjuntoRecibido.FacturaCompra;
        if (ext is ".xls" or ".xlsx" or ".xlsm") return TipoAdjuntoRecibido.Nomina;
        if (ext is ".n43" or ".q43" or ".aeb" or ".csb" or ".csb43" or ".txt" or ".dat" or "")
        {
            var texto = Encoding.Latin1.GetString(contenido, 0, Math.Min(contenido.Length, 4000));
            var primera = texto.Split('\n')[0].TrimEnd('\r');
            if (primera.StartsWith("11", StringComparison.Ordinal) && primera.Length == 80) return TipoAdjuntoRecibido.Norma43;
        }
        return TipoAdjuntoRecibido.NoReconocido;
    }
}

// ---------- servicio ----------

/// <summary>
/// Recepción de correo (correo/recepcion, spec § 23): sondea el buzón POP3 de cada cuenta activa mientras la aplicación está
/// abierta. Cada mensaje se identifica por cuenta + UIDL y se guarda (.eml y adjuntos, con su huella) antes de procesarlo, con
/// hitos persistidos para reanudar sin duplicar: adjuntos procesados, respuesta al remitente y borrado del servidor. Solo se
/// atiende a los remitentes autorizados del ejercicio de la cuenta. El borrado del servidor es el último paso y solo se hace
/// con el mensaje procesado entero y la respuesta aceptada.
/// </summary>
public sealed class ServicioRecepcionCorreo
{
    private const string ClaveTurno = "COLA_ENTRADA";
    private readonly IBaseDatos _bd;
    private readonly IClientePop3 _pop3;
    private readonly ServicioCorreo _correo;
    private readonly IProcesadorFacturaCompraRecibida? _facturas;
    private readonly IReloj _reloj;
    private readonly Actor _actor = Actor.Proceso("correo-entrante");
    private readonly Dictionary<int, DateTime> _ultimoSondeo = [];

    public ServicioRecepcionCorreo(IBaseDatos bd, IClientePop3 pop3, ServicioCorreo correo, IProcesadorFacturaCompraRecibida? facturas, IReloj? reloj = null)
    {
        _bd = bd;
        _pop3 = pop3;
        _correo = correo;
        _facturas = facturas;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    /// <summary>
    /// Una pasada: si esta sesión tiene el turno de entrada, sondea las cuentas activas con POP3 cuyo intervalo ha pasado.
    /// Devuelve cuántos mensajes han cambiado. Un fallo de una cuenta no detiene a las demás; se devuelve en <paramref name="avisos"/>.
    /// </summary>
    public async Task<int> SondearAsync(List<string>? avisos = null, bool forzar = false, CancellationToken cancelacion = default)
    {
        if (!await _correo.TomarTurnoAsync(ClaveTurno)) return 0;
        var cuentas = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            $"SELECT {RepositorioCuentasCorreo.Columnas} FROM CuentasCorreo WHERE Activa = True AND ServidorPop3 Is Not Null", RepositorioCuentasCorreo.Mapear));
        var cambios = 0;
        foreach (var cuenta in cuentas.Where(c => !string.IsNullOrWhiteSpace(c.ServidorPop3)))
        {
            var ahora = _reloj.Ahora;
            if (!forzar && _ultimoSondeo.TryGetValue(cuenta.IdCuentaCorreo, out var ultimo) && ahora - ultimo < TimeSpan.FromMinutes(Math.Max(1, cuenta.IntervaloRecepcionMinutos))) continue;
            _ultimoSondeo[cuenta.IdCuentaCorreo] = ahora;
            try
            {
                cambios += await SondearCuentaAsync(cuenta, cancelacion);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                avisos?.Add($"No he podido leer el buzón de {cuenta.Direccion}: {ex.Message}");
            }
        }
        return cambios;
    }

    /// <summary>
    /// El buzón de una cuenta: descarga y guarda los mensajes nuevos, procesa los pendientes, envía las respuestas y borra del
    /// servidor los ya terminados (si la cuenta lo tiene activado).
    /// </summary>
    public async Task<int> SondearCuentaAsync(CuentaCorreo cuenta, CancellationToken cancelacion = default)
    {
        var remotos = await _pop3.ListarAsync(cuenta, cancelacion);
        var conocidos = (await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT Uidl FROM MensajesRecibidosCorreo WHERE IdCuentaCorreo = ?", f => f.Texto("Uidl") ?? "", cuenta.IdCuentaCorreo))).ToHashSet(StringComparer.Ordinal);
        var cambios = 0;
        foreach (var r in remotos.Where(r => !conocidos.Contains(r.Uidl)))
        {
            cancelacion.ThrowIfCancellationRequested();
            var mime = await _pop3.DescargarAsync(cuenta, r.Uidl, cancelacion);
            if (mime is null) continue;
            await GuardarAsync(cuenta, r.Uidl, mime);
            cambios++;
        }
        // Procesar lo pendiente (también lo descargado en una pasada anterior que se cortó).
        var pendientes = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdMensajeRecibido FROM MensajesRecibidosCorreo WHERE IdCuentaCorreo = ? AND Estado = ? ORDER BY IdMensajeRecibido",
            f => f.Entero("IdMensajeRecibido"), cuenta.IdCuentaCorreo, nameof(EstadoRecibido.Descargado)));
        foreach (var id in pendientes)
        {
            cancelacion.ThrowIfCancellationRequested();
            await ProcesarAsync(id, cancelacion);
            cambios++;
        }
        // Respuestas que quedaron sin encolar (corte entre procesar y responder).
        var sinRespuesta = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdMensajeRecibido FROM MensajesRecibidosCorreo WHERE IdCuentaCorreo = ? AND IdMensajeRespuesta Is Null AND Estado IN ('Procesado', 'EnEspera', 'ConIncidencia')",
            f => f.Entero("IdMensajeRecibido"), cuenta.IdCuentaCorreo));
        foreach (var id in sinRespuesta) await ResponderAsync(id);
        if (cuenta.EliminarTrasProcesar) cambios += await EliminarTerminadosAsync(cuenta, remotos.Select(r => r.Uidl).ToHashSet(StringComparer.Ordinal), cancelacion);
        return cambios;
    }

    /// <summary>Guarda el .eml y sus adjuntos en la carpeta de documentos de la empresa y registra el mensaje (Descargado).</summary>
    private async Task<int> GuardarAsync(CuentaCorreo cuenta, string uidl, byte[] mime)
    {
        var leido = LectorCorreo.Leer(mime);
        var ahora = _reloj.Ahora;
        var (raiz, ejercicio) = await CarpetaAsync(cuenta.IdSociedadEjercicio);
        var guid = Guid.NewGuid().ToString("N");
        var relativa = Path.Combine(ejercicio, "Correo", "Recibido", ahora.ToString("yyyyMM", CultureInfo.InvariantCulture), guid);
        var carpeta = Path.Combine(raiz, relativa);
        Directory.CreateDirectory(carpeta);
        var eml = Path.Combine(carpeta, "mensaje.eml");
        await File.WriteAllBytesAsync(eml, mime);
        File.SetAttributes(eml, FileAttributes.ReadOnly);
        var adjuntos = new List<(short Ordinal, string Nombre, int Tamano, string Huella, string Ruta, TipoAdjuntoRecibido Tipo)>();
        short ordinal = 0;
        foreach (var (nombre, contenido) in leido.Adjuntos)
        {
            ordinal++;
            var fichero = $"{ordinal:00}-{NombreSeguro(nombre)}";
            var ruta = Path.Combine(carpeta, fichero);
            await File.WriteAllBytesAsync(ruta, contenido);
            File.SetAttributes(ruta, FileAttributes.ReadOnly);
            adjuntos.Add((ordinal, ServicioCorreo.Recortar(nombre, 255)!, contenido.Length, Convert.ToHexString(SHA256.HashData(contenido)), Path.Combine(relativa, fichero), LectorCorreo.Clasificar(nombre, contenido)));
        }
        var propio = leido.Remitente is { } rem && (string.Equals(rem, cuenta.Direccion, StringComparison.OrdinalIgnoreCase) || string.Equals(rem, cuenta.ResponderA, StringComparison.OrdinalIgnoreCase));
        var estado = leido.EsAutomatico || propio || leido.Remitente is null ? EstadoRecibido.Ignorado : EstadoRecibido.Descargado;
        return await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO MensajesRecibidosCorreo (IdCuentaCorreo, IdSociedadEjercicio, Uidl, MessageId, Remitente, NombreRemitente, Asunto, FechaMensaje, FechaRecepcion, " +
                "TamanoBytes, Huella, RutaEml, Estado, Resumen, EliminadoServidor, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, False, 0)",
                cuenta.IdCuentaCorreo, cuenta.IdSociedadEjercicio, ServicioCorreo.Recortar(uidl, 255), ServicioCorreo.Recortar(leido.MessageId, 255),
                ServicioCorreo.Recortar(leido.Remitente, 254), ServicioCorreo.Recortar(leido.NombreRemitente, 150), ServicioCorreo.Recortar(leido.Asunto, 255),
                leido.Fecha, ahora, mime.Length, Convert.ToHexString(SHA256.HashData(mime)), Path.Combine(relativa, "mensaje.eml"), estado.ToString(),
                estado == EstadoRecibido.Ignorado ? (propio ? "Correo enviado desde la propia cuenta: no se procesa." : "Respuesta automática o aviso del servidor de correo: no se procesa.") : null);
            var id = await s.UltimoIdAsync();
            foreach (var a in adjuntos)
                await s.EjecutarAsync("INSERT INTO AdjuntosRecibidosCorreo (IdMensajeRecibido, Ordinal, Nombre, TamanoBytes, Huella, RutaRelativa, Tipo, Estado) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    id, a.Ordinal, a.Nombre, a.Tamano, a.Huella, a.Ruta, a.Tipo.ToString(),
                    (a.Tipo == TipoAdjuntoRecibido.NoReconocido ? EstadoAdjuntoRecibido.NoReconocido : EstadoAdjuntoRecibido.Pendiente).ToString());
            return id;
        });
    }

    /// <summary>
    /// Procesa un mensaje descargado: comprueba el remitente, procesa cada adjunto pendiente con su motor (guardando el hito de
    /// cada uno) y fija el estado del mensaje; después envía la respuesta.
    /// </summary>
    public async Task ProcesarAsync(int idMensaje, CancellationToken cancelacion = default)
    {
        var m = await ObtenerAsync(idMensaje);
        if (m is null || m.Estado != EstadoRecibido.Descargado) return;
        var ctx = await ContextoAsync(m.IdSociedadEjercicio);
        var remitente = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT Direccion, PuedeImportar, PuedePedirInformes FROM RemitentesAutorizadosCorreo WHERE IdSociedadEjercicio = ? AND Direccion = ? AND Activo = True",
            f => new RemitenteAutorizado(f.Texto("Direccion") ?? "", f.Logico("PuedeImportar"), f.Logico("PuedePedirInformes")), m.IdSociedadEjercicio, m.Remitente ?? ""));
        if (remitente is null)
        {
            await FijarEstadoAsync(idMensaje, EstadoRecibido.NoAutorizado,
                $"{m.Remitente} no está entre los remitentes autorizados de este ejercicio: no se procesa ni se responde. Si debe poder enviar documentos, añádelo en Parámetros y tablas › Remitentes autorizados y vuelve a procesarlo.");
            return;
        }
        foreach (var a in m.Adjuntos.Where(a => a.Estado == EstadoAdjuntoRecibido.Pendiente))
        {
            cancelacion.ThrowIfCancellationRequested();
            var (estado, idFactura, idAsiento, detalle) = await ProcesarAdjuntoAsync(ctx, m, a, remitente, cancelacion);
            await _bd.EjecutarAsync(s => s.EjecutarAsync(
                "UPDATE AdjuntosRecibidosCorreo SET Estado = ?, IdFacturaCompra = ?, IdAsiento = ?, Detalle = ? WHERE IdAdjuntoRecibido = ?",
                estado.ToString(), idFactura, idAsiento, detalle, a.IdAdjunto));
        }
        var actual = (await ObtenerAsync(idMensaje))!;
        var estadosAdjuntos = actual.Adjuntos.Select(a => a.Estado).ToList();
        EstadoRecibido final;
        string resumen;
        if (actual.Adjuntos.Count == 0 || estadosAdjuntos.All(e => e == EstadoAdjuntoRecibido.NoReconocido))
        {
            // Sin documentos: el asunto es un comando de informe (spec § 23 RF-DIS-07), que llegará con informes/reporting.
            var comando = (actual.Asunto ?? "").Trim();
            if (comando.Length == 0) { final = EstadoRecibido.Procesado; resumen = "Sin adjuntos que procesar ni comando en el asunto."; }
            else if (!remitente.PuedePedirInformes) { final = EstadoRecibido.Procesado; resumen = $"El asunto «{comando}» parece un comando de informe, pero este remitente no tiene permiso para pedir informes."; }
            else { final = EstadoRecibido.EnEspera; resumen = $"Comando «{comando}»: los informes por correo aún no están disponibles en ProContaVF; el mensaje se conserva hasta que lo estén."; }
        }
        else if (estadosAdjuntos.Contains(EstadoAdjuntoRecibido.Error)) { final = EstadoRecibido.ConIncidencia; resumen = "Algún adjunto no se ha podido procesar: revisa el detalle y vuelve a procesarlo."; }
        else if (estadosAdjuntos.Contains(EstadoAdjuntoRecibido.MotorPendiente)) { final = EstadoRecibido.EnEspera; resumen = "Algún adjunto espera a un módulo que aún no está en ProContaVF (Norma 43 o nóminas): se conserva hasta entonces."; }
        else { final = EstadoRecibido.Procesado; resumen = "Procesado."; }
        await FijarEstadoAsync(idMensaje, final, resumen);
        await ResponderAsync(idMensaje);
    }

    private async Task<(EstadoAdjuntoRecibido Estado, int? IdFactura, int? IdAsiento, string Detalle)> ProcesarAdjuntoAsync(
        ContextoSociedadEjercicio ctx, DetalleRecibido m, AdjuntoRecibido a, RemitenteAutorizado remitente, CancellationToken cancelacion)
    {
        if (!remitente.PuedeImportar && a.Tipo != TipoAdjuntoRecibido.NoReconocido)
            return (EstadoAdjuntoRecibido.Error, null, null, "Este remitente no tiene permiso para enviar documentos a importar (Parámetros y tablas › Remitentes autorizados).");
        switch (a.Tipo)
        {
            case TipoAdjuntoRecibido.FacturaCompra:
                if (_facturas is null) return (EstadoAdjuntoRecibido.MotorPendiente, null, null, "La importación de facturas de compra no está disponible en esta sesión.");
                if (a.RutaCompleta is null || !File.Exists(a.RutaCompleta)) return (EstadoAdjuntoRecibido.Error, null, null, $"No encuentro la copia del adjunto en {a.RutaCompleta ?? a.RutaRelativa}.");
                try
                {
                    var r = await _facturas.ProcesarAsync(ctx, a.RutaCompleta, a.Nombre, _actor, cancelacion);
                    var campos = r.Campos.Count == 0 ? "" : " Campos: " + string.Join("; ", r.Campos.Select(c => $"{c.Campo}: {c.Valor}{(c.Confianza is decimal k ? $" ({k:P0})" : "")}")) + ".";
                    return (r.Estado, r.IdFacturaCompra, r.IdAsiento, r.Explicacion + campos);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (EstadoAdjuntoRecibido.Error, null, null, $"No he podido procesar la factura: {ex.Message}");
                }
            case TipoAdjuntoRecibido.Norma43:
                return (EstadoAdjuntoRecibido.MotorPendiente, null, null, "Extracto Norma 43: la importación de extractos aún no está en ProContaVF; el fichero se conserva hasta entonces.");
            case TipoAdjuntoRecibido.Nomina:
                return (EstadoAdjuntoRecibido.MotorPendiente, null, null, "Fichero de nóminas: la importación de nóminas aún no está en ProContaVF; el fichero se conserva hasta entonces.");
            default:
                return (EstadoAdjuntoRecibido.NoReconocido, null, null, "No es un documento que ProContaVF procese.");
        }
    }

    /// <summary>Responde al remitente con lo que se ha hecho (una sola vez por mensaje: el hito es IdMensajeRespuesta).</summary>
    private async Task ResponderAsync(int idMensaje)
    {
        var m = await ObtenerAsync(idMensaje);
        if (m is null || m.IdMensajeRespuesta is not null || m.Remitente is null || m.Estado is EstadoRecibido.Descargado or EstadoRecibido.NoAutorizado or EstadoRecibido.Ignorado) return;
        var html = new StringBuilder();
        html.Append($"<p>Hola:</p><p>He recibido tu correo «{WebUtility.HtmlEncode(m.Asunto ?? "(sin asunto)")}» del {m.FechaRecepcion:dd/MM/yyyy HH:mm}. Esto es lo que he hecho:</p><ul>");
        if (m.Adjuntos.Count == 0 || m.Adjuntos.All(a => a.Estado == EstadoAdjuntoRecibido.NoReconocido)) html.Append($"<li>{WebUtility.HtmlEncode(m.Resumen ?? "")}</li>");
        foreach (var a in m.Adjuntos)
            html.Append($"<li><b>{WebUtility.HtmlEncode(a.Nombre)}</b>: {WebUtility.HtmlEncode(TextoEstado(a.Estado))}. {WebUtility.HtmlEncode(a.Detalle ?? "")}</li>");
        html.Append("</ul><p>Un saludo,<br>ProContaVF</p>");
        var mensaje = new MensajeNuevo
        {
            IdSociedadEjercicio = m.IdSociedadEjercicio, TipoCorreo = TiposCorreo.Otro, TipoOrigen = "CorreoRecibido", IdOrigen = m.IdMensaje,
            ReferenciaOrigen = $"Respuesta a {m.Remitente}",
            Destinatarios = [new Destinatario(TipoDestinatario.Para, m.Remitente, m.NombreRemitente)],
            Asunto = ServicioCorreo.Recortar("Re: " + (m.Asunto ?? "documentos recibidos"), 255)!,
            CuerpoHtml = html.ToString(),
        };
        var r = await _correo.EncolarAsync(mensaje, _actor);
        if (!r.EsCorrecto) return;
        await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE MensajesRecibidosCorreo SET IdMensajeRespuesta = ?, Version = Version + 1 WHERE IdMensajeRecibido = ? AND IdMensajeRespuesta Is Null", r.Valor, idMensaje));
        await _correo.EnviarAhoraAsync(r.Valor);
    }

    /// <summary>
    /// Borra del servidor los mensajes terminados: procesados enteros (o ignorados) y con la respuesta aceptada. Lo que espera a un
    /// motor, tiene incidencia o no está autorizado se conserva. Si un UIDL ya no está en el servidor, se da por borrado.
    /// </summary>
    private async Task<int> EliminarTerminadosAsync(CuentaCorreo cuenta, IReadOnlySet<string> enServidor, CancellationToken cancelacion)
    {
        var candidatos = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT r.IdMensajeRecibido, r.Uidl, r.Estado, m.Estado AS EstadoRespuesta FROM MensajesRecibidosCorreo AS r LEFT JOIN MensajesCorreo AS m ON r.IdMensajeRespuesta = m.IdMensajeCorreo " +
            "WHERE r.IdCuentaCorreo = ? AND r.EliminadoServidor = False AND r.Estado IN ('Procesado', 'Ignorado')",
            f => (Id: f.Entero("IdMensajeRecibido"), Uidl: f.Texto("Uidl") ?? "", Estado: f.Texto("Estado"), Respuesta: f.Texto("EstadoRespuesta")), cuenta.IdCuentaCorreo));
        var listos = candidatos.Where(c => c.Estado == nameof(EstadoRecibido.Ignorado) || c.Respuesta is nameof(EstadoMensaje.Aceptado) or nameof(EstadoMensaje.AceptadoEnParte)).ToList();
        if (listos.Count == 0) return 0;
        var ausentes = listos.Where(c => !enServidor.Contains(c.Uidl)).ToList();
        var borrados = await _pop3.EliminarAsync(cuenta, listos.Where(c => enServidor.Contains(c.Uidl)).Select(c => c.Uidl).ToList(), cancelacion);
        var ahora = _reloj.Ahora;
        var hechos = 0;
        foreach (var c in listos.Where(c => borrados.Contains(c.Uidl) || ausentes.Contains(c)))
        {
            await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE MensajesRecibidosCorreo SET EliminadoServidor = True, FechaEliminacion = ?, Version = Version + 1 WHERE IdMensajeRecibido = ?", ahora, c.Id));
            hechos++;
        }
        return hechos;
    }

    /// <summary>
    /// Vuelve a procesar un mensaje con incidencia, no autorizado o en espera: los adjuntos con error o en espera vuelven a pendiente
    /// y se enviará una respuesta nueva. Lo ya procesado (una factura registrada) no se repite.
    /// </summary>
    public async Task<bool> ReprocesarAsync(int idMensaje, CancellationToken cancelacion = default)
    {
        var n = await _bd.EnTransaccionAsync(async s =>
        {
            var c = await s.EjecutarAsync("UPDATE MensajesRecibidosCorreo SET Estado = 'Descargado', IdMensajeRespuesta = Null, Version = Version + 1 " +
                "WHERE IdMensajeRecibido = ? AND Estado IN ('ConIncidencia', 'NoAutorizado', 'EnEspera')", idMensaje);
            if (c > 0)
                await s.EjecutarAsync("UPDATE AdjuntosRecibidosCorreo SET Estado = 'Pendiente', Detalle = Null WHERE IdMensajeRecibido = ? AND Estado IN ('Error', 'MotorPendiente')", idMensaje);
            return c;
        });
        if (n == 0) return false;
        await ProcesarAsync(idMensaje, cancelacion);
        return true;
    }

    // ---------- consultas ----------

    public async Task<IReadOnlyList<FilaRecibido>> ListarAsync(int idSociedadEjercicio, int maximo = 2000) =>
        await _bd.EjecutarAsync(s => s.ConsultarAsync(
            $"SELECT TOP {Math.Clamp(maximo, 1, 5000)} r.IdMensajeRecibido, r.FechaRecepcion, r.Remitente, r.Asunto, r.Estado, r.Resumen, r.EliminadoServidor, " +
            "(SELECT COUNT(*) FROM AdjuntosRecibidosCorreo AS a WHERE a.IdMensajeRecibido = r.IdMensajeRecibido) AS Adjuntos " +
            "FROM MensajesRecibidosCorreo AS r WHERE r.IdSociedadEjercicio = ? ORDER BY r.FechaRecepcion DESC, r.IdMensajeRecibido DESC",
            f => new FilaRecibido(f.Entero("IdMensajeRecibido"), f.Fecha("FechaRecepcion"), f.Texto("Remitente"), f.Texto("Asunto"),
                Enum.TryParse<EstadoRecibido>(f.Texto("Estado"), out var e) ? e : EstadoRecibido.Descargado, f.Texto("Resumen"), f.Logico("EliminadoServidor"), f.Entero("Adjuntos")),
            idSociedadEjercicio));

    public async Task<DetalleRecibido?> ObtenerAsync(int idMensaje)
    {
        var d = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT r.*, so.RutaDocumentos FROM (MensajesRecibidosCorreo AS r INNER JOIN SociedadesEjercicios AS se ON r.IdSociedadEjercicio = se.IdSociedadEjercicio) " +
            "LEFT JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad WHERE r.IdMensajeRecibido = ?",
            f => new DetalleRecibido
            {
                IdMensaje = f.Entero("IdMensajeRecibido"), IdCuentaCorreo = f.Entero("IdCuentaCorreo"), IdSociedadEjercicio = f.Entero("IdSociedadEjercicio"),
                Uidl = f.Texto("Uidl") ?? "", MessageId = f.Texto("MessageId"), Remitente = f.Texto("Remitente"), NombreRemitente = f.Texto("NombreRemitente"),
                Asunto = f.Texto("Asunto"), FechaMensaje = f.FechaNula("FechaMensaje"), FechaRecepcion = f.Fecha("FechaRecepcion"),
                Estado = Enum.TryParse<EstadoRecibido>(f.Texto("Estado"), out var e) ? e : EstadoRecibido.Descargado, Resumen = f.Texto("Resumen"),
                IdMensajeRespuesta = f.EnteroNulo("IdMensajeRespuesta"), EliminadoServidor = f.Logico("EliminadoServidor"), FechaEliminacion = f.FechaNula("FechaEliminacion"),
                RutaEml = f.Texto("RutaEml") ?? "", Raiz = f.Texto("RutaDocumentos") is { } r ? ServicioCorreo.RaizDocumentos(r, _bd.Ruta) : null,
            }, idMensaje));
        if (d is null) return null;
        d.Adjuntos = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdAdjuntoRecibido, Ordinal, Nombre, TamanoBytes, Huella, RutaRelativa, Tipo, Estado, IdFacturaCompra, IdAsiento, Detalle FROM AdjuntosRecibidosCorreo WHERE IdMensajeRecibido = ? ORDER BY Ordinal",
            f => new AdjuntoRecibido(f.Entero("IdAdjuntoRecibido"), f.Entero("Ordinal"), f.Texto("Nombre") ?? "", f.Entero("TamanoBytes"), f.Texto("Huella") ?? "",
                f.Texto("RutaRelativa") ?? "", d.Raiz is null ? null : Path.Combine(d.Raiz, f.Texto("RutaRelativa") ?? ""),
                Enum.TryParse<TipoAdjuntoRecibido>(f.Texto("Tipo"), out var t) ? t : TipoAdjuntoRecibido.NoReconocido,
                Enum.TryParse<EstadoAdjuntoRecibido>(f.Texto("Estado"), out var e) ? e : EstadoAdjuntoRecibido.Pendiente,
                f.EnteroNulo("IdFacturaCompra"), f.EnteroNulo("IdAsiento"), f.Texto("Detalle")), idMensaje));
        return d;
    }

    // ---------- apoyo ----------

    private Task FijarEstadoAsync(int id, EstadoRecibido estado, string resumen) =>
        _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE MensajesRecibidosCorreo SET Estado = ?, Resumen = ?, FechaProceso = ?, Version = Version + 1 WHERE IdMensajeRecibido = ?",
            estado.ToString(), resumen, _reloj.Ahora, id));

    private async Task<ContextoSociedadEjercicio> ContextoAsync(int idSociedadEjercicio)
    {
        var f = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT IdSociedad, Ejercicio FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            r => (Sociedad: r.Texto("IdSociedad") ?? "", Ejercicio: r.Texto("Ejercicio") ?? "0"), idSociedadEjercicio));
        return new ContextoSociedadEjercicio(f.Sociedad, idSociedadEjercicio, int.TryParse(f.Ejercicio, out var e) ? e : 0);
    }

    private async Task<(string Raiz, string Ejercicio)> CarpetaAsync(int idSociedadEjercicio)
    {
        var f = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT se.Ejercicio, so.Nombre, so.RutaDocumentos FROM SociedadesEjercicios AS se LEFT JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad WHERE se.IdSociedadEjercicio = ?",
            r => (Ejercicio: r.Texto("Ejercicio") ?? "", Nombre: r.Texto("Nombre") ?? "", Raiz: r.Texto("RutaDocumentos")), idSociedadEjercicio));
        if (string.IsNullOrWhiteSpace(f.Raiz))
            throw new InvalidOperationException($"La empresa {f.Nombre} no tiene carpeta de documentos, y es donde se guarda el correo recibido. Escríbela en Parámetros y tablas › Empresas; mientras tanto el correo se queda en el servidor.");
        return (ServicioCorreo.RaizDocumentos(f.Raiz, _bd.Ruta), f.Ejercicio.Trim());
    }

    private static string NombreSeguro(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(nombre.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (limpio.Length == 0) limpio = "adjunto";
        return limpio.Length > 120 ? limpio[..120] : limpio;
    }

    public static string TextoEstado(EstadoAdjuntoRecibido e) => e switch
    {
        EstadoAdjuntoRecibido.Pendiente => "pendiente",
        EstadoAdjuntoRecibido.Procesado => "procesado",
        EstadoAdjuntoRecibido.Revisar => "registrado, pendiente de revisar",
        EstadoAdjuntoRecibido.MotorPendiente => "en espera",
        EstadoAdjuntoRecibido.NoReconocido => "no procesado",
        _ => "no se ha podido procesar",
    };

    public static string Texto(EstadoRecibido e) => e switch
    {
        EstadoRecibido.Descargado => "Pendiente de procesar",
        EstadoRecibido.Procesado => "Procesado",
        EstadoRecibido.EnEspera => "En espera de un módulo",
        EstadoRecibido.ConIncidencia => "Con incidencia",
        EstadoRecibido.NoAutorizado => "Remitente no autorizado",
        _ => "Ignorado",
    };
}

public sealed record FilaRecibido(int IdMensaje, DateTime FechaRecepcion, string? Remitente, string? Asunto, EstadoRecibido Estado, string? Resumen, bool EliminadoServidor, int Adjuntos);

public sealed record AdjuntoRecibido(int IdAdjunto, int Ordinal, string Nombre, int TamanoBytes, string Huella, string RutaRelativa, string? RutaCompleta,
    TipoAdjuntoRecibido Tipo, EstadoAdjuntoRecibido Estado, int? IdFacturaCompra, int? IdAsiento, string? Detalle);

/// <summary>Detalle de un mensaje recibido, con sus adjuntos y lo que generaron.</summary>
public sealed class DetalleRecibido
{
    public int IdMensaje { get; init; }
    public int IdCuentaCorreo { get; init; }
    public int IdSociedadEjercicio { get; init; }
    public string Uidl { get; init; } = "";
    public string? MessageId { get; init; }
    public string? Remitente { get; init; }
    public string? NombreRemitente { get; init; }
    public string? Asunto { get; init; }
    public DateTime? FechaMensaje { get; init; }
    public DateTime FechaRecepcion { get; init; }
    public EstadoRecibido Estado { get; init; }
    public string? Resumen { get; init; }
    public int? IdMensajeRespuesta { get; init; }
    public bool EliminadoServidor { get; init; }
    public DateTime? FechaEliminacion { get; init; }
    public string RutaEml { get; init; } = "";
    public string? Raiz { get; init; }
    public string? RutaEmlCompleta => Raiz is null ? null : Path.Combine(Raiz, RutaEml);
    public IReadOnlyList<AdjuntoRecibido> Adjuntos { get; set; } = [];
}
