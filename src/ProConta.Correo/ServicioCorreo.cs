using System.Globalization;
using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Correo;

/// <summary>Resultado de procesar un mensaje: su estado y la explicación para el operador.</summary>
public sealed record ResultadoEnvio(int IdMensaje, EstadoMensaje Estado, string Explicacion, DateTime? ProximoIntento = null);

/// <summary>
/// Cola durable de correo saliente (correo/envio 2026-11-10 19:40). Encolar guarda la instantánea completa (Message-ID,
/// destinatarios, contenido y adjuntos con su huella) antes de conectar con el servidor; procesar toma cada mensaje pendiente
/// de forma atómica, lo transmite con <see cref="ITransporteSmtp"/> y guarda el intento con su evidencia. Reintentar,
/// reenviar, resolver un incierto y cancelar cambian el estado con una condición sobre el estado actual, así que pedirlas dos
/// veces no duplica nada.
/// </summary>
public sealed class ServicioCorreo
{
    private const string ClaveTurno = "COLA_SALIDA";
    private static readonly TimeSpan DuracionTurno = TimeSpan.FromMinutes(3);
    /// <summary>Un mensaje «en proceso» desde hace más que esto se quedó a medias (la sesión se cerró o se colgó).</summary>
    private static readonly TimeSpan ProcesoAbandonado = TimeSpan.FromMinutes(15);

    private readonly IBaseDatos _bd;
    private readonly ITransporteSmtp _transporte;
    private readonly RepositorioCuentasCorreo _cuentas;
    private readonly IReloj _reloj;

    public ServicioCorreo(IBaseDatos bd, ITransporteSmtp transporte, IReloj? reloj = null)
    {
        _bd = bd;
        _transporte = transporte;
        _cuentas = new RepositorioCuentasCorreo(bd);
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    /// <summary>Identifica a esta sesión del programa en el turno de la cola.</summary>
    public string Propietario { get; } = ServicioCorreo.Recortar($"{Environment.MachineName}/{Environment.UserName}/{Environment.ProcessId}/{Guid.NewGuid():N}", 100)!;

    public RepositorioCuentasCorreo Cuentas => _cuentas;

    /// <summary>Dirección a la que se desvía todo el correo (Configuracion.CorreoDesviarA); null si no hay desvío.</summary>
    public async Task<string?> DesvioAsync()
    {
        var v = await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT TOP 1 CorreoDesviarA FROM Configuracion"));
        return v is string t && !string.IsNullOrWhiteSpace(t) ? t.Trim() : null;
    }

    // ---------- encolar ----------

    /// <summary>Crea un lote para un proceso de más de un correo.</summary>
    public async Task<int> CrearLoteAsync(int idSociedadEjercicio, string descripcion, string origen, Actor actor) =>
        await _bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO LotesCorreo (IdSociedadEjercicio, Descripcion, Origen, FechaCreacion, Operador) VALUES (?, ?, ?, ?, ?)",
                idSociedadEjercicio, Recortar(descripcion, 255), Recortar(origen, 30), _reloj.Ahora, Recortar(actor.Nombre, 30));
            return await s.UltimoIdAsync();
        });

    /// <summary>
    /// Valida el mensaje y lo guarda en la cola, pendiente de enviar ya. Los adjuntos se copian primero a la carpeta de
    /// documentos de la empresa (copia inmutable con su huella) y después se registra todo en una transacción: un fallo a
    /// medias no deja un mensaje en la cola sin sus adjuntos.
    /// </summary>
    public async Task<Resultado<int>> EncolarAsync(MensajeNuevo m, Actor actor, int? idLote = null)
    {
        var errores = ReglasMensaje.Validar(m);
        if (errores.Count > 0) return Resultado<int>.Fallo(errores);
        var cuenta = await _cuentas.ObtenerPorEjercicioAsync(m.IdSociedadEjercicio);
        var registro = m.ReferenciaOrigen ?? m.Asunto;
        if (cuenta is null)
            return Resultado<int>.Fallo(new Error("Correo.SinCuenta", "Este ejercicio no tiene cuenta de correo, y es la que envía.", registro, null,
                "Configúrala en Configuración › Parámetros y tablas › Cuenta de correo (del ejercicio de trabajo) y vuelve a enviar."));
        if (!cuenta.Activa)
            return Resultado<int>.Fallo(new Error("Correo.CuentaInactiva", $"La cuenta de correo {cuenta.Direccion} está desactivada.", registro, null,
                "Actívala en Configuración › Parámetros y tablas › Cuenta de correo y vuelve a enviar."));

        var destino = await CarpetaDocumentosAsync(m.IdSociedadEjercicio);
        if (destino.Error is { } sinCarpeta && m.Adjuntos.Count > 0) return Resultado<int>.Fallo(sinCarpeta);

        var guid = Guid.NewGuid().ToString("N");
        var dominio = cuenta.Direccion.Contains('@') ? cuenta.Direccion[(cuenta.Direccion.IndexOf('@') + 1)..] : "procontavf.local";
        var messageId = $"{guid}@{dominio}";
        var ahora = _reloj.Ahora;

        var adjuntos = new List<(short Ordinal, string Nombre, string Tipo, long Tamano, string Huella, string Ruta)>();
        try
        {
            if (m.Adjuntos.Count > 0)
            {
                var relativaCarpeta = Path.Combine(destino.Ejercicio, "Correo", ahora.ToString("yyyyMM", CultureInfo.InvariantCulture), guid);
                var carpeta = Path.Combine(destino.Raiz!, relativaCarpeta);
                Directory.CreateDirectory(carpeta);
                short ordinal = 0;
                foreach (var a in m.Adjuntos)
                {
                    ordinal++;
                    var nombre = NombreSeguro(a.Nombre);
                    var fichero = $"{ordinal:00}-{nombre}";
                    var ruta = Path.Combine(carpeta, fichero);
                    await File.WriteAllBytesAsync(ruta, a.Contenido);
                    File.SetAttributes(ruta, FileAttributes.ReadOnly);
                    adjuntos.Add((ordinal, Recortar(a.Nombre, 255)!, a.TipoContenido ?? TiposContenido.De(a.Nombre), a.Contenido.LongLength,
                        Convert.ToHexString(SHA256.HashData(a.Contenido)), Path.Combine(relativaCarpeta, fichero)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado<int>.Fallo(new Error("Correo.AdjuntoNoGuardado", $"No he podido guardar la copia de los adjuntos en {destino.Raiz}: {ex.Message}", registro,
                "La carpeta de documentos de la empresa no existe o no se puede escribir en ella.", "Comprueba la carpeta en Parámetros y tablas › Empresas y vuelve a enviar. No se ha enviado nada."));
        }

        var id = await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO MensajesCorreo (IdSociedadEjercicio, IdCuentaCorreo, IdLoteCorreo, TipoCorreo, TipoOrigen, IdOrigen, ReferenciaOrigen, MessageId, Remitente, NombreRemitente, " +
                "ResponderA, Asunto, CuerpoHtml, CuerpoTexto, Estado, NumeroIntentos, ProximoIntento, FechaCreacion, Operador, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?, ?, 0)",
                m.IdSociedadEjercicio, cuenta.IdCuentaCorreo, idLote, m.TipoCorreo, m.TipoOrigen, m.IdOrigen, Recortar(m.ReferenciaOrigen, 150), messageId, cuenta.Direccion,
                cuenta.NombreVisible, cuenta.ResponderA, m.Asunto.Trim(), m.CuerpoHtml, m.CuerpoTexto, nameof(EstadoMensaje.Pendiente), ahora, ahora, Recortar(actor.Nombre, 30));
            var nuevo = await s.UltimoIdAsync();
            foreach (var d in m.Destinatarios)
                await s.EjecutarAsync("INSERT INTO DestinatariosMensajeCorreo (IdMensajeCorreo, Tipo, Direccion, Nombre, Resultado) VALUES (?, ?, ?, ?, ?)",
                    nuevo, CodigoTipo(d.Tipo), ReglasMensaje.Normalizar(d.Direccion).Direccion, Recortar(d.Nombre, 150), nameof(ResultadoDestinatario.SinResultado));
            foreach (var a in adjuntos)
                await s.EjecutarAsync("INSERT INTO AdjuntosMensajeCorreo (IdMensajeCorreo, Ordinal, Nombre, TipoContenido, TamanoBytes, Huella, RutaRelativa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    nuevo, a.Ordinal, a.Nombre, Recortar(a.Tipo, 100), (int)Math.Min(int.MaxValue, a.Tamano), a.Huella, a.Ruta);
            return nuevo;
        });
        return Resultado<int>.Correcto(id);
    }

    // ---------- procesar ----------

    /// <summary>
    /// Procesa ahora un mensaje pendiente (el compositor y los lotes lo usan para no esperar al turno). Si otra sesión ya lo
    /// ha tomado, no hace nada y devuelve su estado actual.
    /// </summary>
    public async Task<ResultadoEnvio> EnviarAhoraAsync(int idMensaje, CancellationToken cancelacion = default)
    {
        var r = await ProcesarUnoAsync(idMensaje, cancelacion);
        if (r is not null) return r;
        var estado = await EstadoAsync(idMensaje);
        return new ResultadoEnvio(idMensaje, estado ?? EstadoMensaje.Cancelado, estado is null ? "El mensaje ya no existe." : $"El mensaje está {Texto(estado.Value).ToLowerInvariant()}.");
    }

    /// <summary>
    /// Una pasada de la cola: si esta sesión tiene el turno, marca como inciertos los mensajes abandonados en proceso y envía
    /// los pendientes cuyo momento ha llegado. Devuelve cuántos ha procesado; 0 si el turno es de otra sesión.
    /// </summary>
    public async Task<int> ProcesarPendientesAsync(CancellationToken cancelacion = default)
    {
        if (!await TomarTurnoAsync()) return 0;
        var ahora = _reloj.Ahora;
        await _bd.EjecutarAsync(async s =>
        {
            // Un corte a mitad de transmisión: no consta si salió, así que no se reintenta solo.
            var abandonados = await s.ConsultarAsync(
                "SELECT DISTINCT i.IdMensajeCorreo FROM IntentosMensajeCorreo AS i INNER JOIN MensajesCorreo AS m ON i.IdMensajeCorreo = m.IdMensajeCorreo " +
                "WHERE i.Resultado = 'EnCurso' AND i.Inicio < ? AND m.Estado = 'EnProceso'", f => f.Entero("IdMensajeCorreo"), ahora - ProcesoAbandonado);
            foreach (var id in abandonados)
            {
                await s.EjecutarAsync("UPDATE IntentosMensajeCorreo SET Resultado = ?, Fin = ?, Detalle = ? WHERE IdMensajeCorreo = ? AND Resultado = 'EnCurso'",
                    nameof(ClaseResultado.Incierto), ahora, "La sesión que lo enviaba se cerró o dejó de responder a mitad del envío.", id);
                await s.EjecutarAsync("UPDATE MensajesCorreo SET Estado = ?, UltimoError = ?, Version = Version + 1 WHERE IdMensajeCorreo = ? AND Estado = ?",
                    nameof(EstadoMensaje.Incierto), "La sesión que lo enviaba se cerró a mitad del envío: no consta si salió.", id, nameof(EstadoMensaje.EnProceso));
            }
            return true;
        });
        var pendientes = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT TOP 50 IdMensajeCorreo FROM MensajesCorreo WHERE Estado = ? AND (ProximoIntento IS NULL OR ProximoIntento <= ?) ORDER BY ProximoIntento, IdMensajeCorreo",
            f => f.Entero("IdMensajeCorreo"), nameof(EstadoMensaje.Pendiente), ahora));
        var hechos = 0;
        foreach (var id in pendientes)
        {
            cancelacion.ThrowIfCancellationRequested();
            if (!await TomarTurnoAsync()) break;
            if (await ProcesarUnoAsync(id, cancelacion) is not null) hechos++;
        }
        return hechos;
    }

    /// <summary>
    /// Turno de la cola en TurnosCorreo: lo toma si está libre o caducado, o lo renueva si ya es suyo. Con varias sesiones
    /// abiertas, solo una procesa a la vez.
    /// </summary>
    public Task<bool> TomarTurnoAsync() =>
        _bd.EjecutarAsync(async s =>
        {
            var ahora = _reloj.Ahora;
            var n = await s.EjecutarAsync("UPDATE TurnosCorreo SET Propietario = ?, CaducaEn = ? WHERE Clave = ? AND (Propietario = ? OR CaducaEn < ?)",
                Propietario, ahora + DuracionTurno, ClaveTurno, Propietario, ahora);
            if (n > 0) return true;
            if (await s.EscalarAsync("SELECT COUNT(*) FROM TurnosCorreo WHERE Clave = ?", ClaveTurno) is { } c && Convert.ToInt32(c, CultureInfo.InvariantCulture) > 0) return false;
            try
            {
                await s.EjecutarAsync("INSERT INTO TurnosCorreo (Clave, Propietario, CaducaEn) VALUES (?, ?, ?)", ClaveTurno, Propietario, ahora + DuracionTurno);
                return true;
            }
            catch (ExcepcionDatos)
            {
                return false; // otra sesión lo ha creado a la vez
            }
        });

    /// <summary>Suelta el turno al cerrar la sesión, para que otra lo tome sin esperar a que caduque.</summary>
    public Task SoltarTurnoAsync() =>
        _bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM TurnosCorreo WHERE Clave = ? AND Propietario = ?", ClaveTurno, Propietario));

    /// <summary>Toma el mensaje (pendiente → en proceso) y lo transmite; null si no estaba pendiente.</summary>
    private async Task<ResultadoEnvio?> ProcesarUnoAsync(int id, CancellationToken cancelacion)
    {
        var inicio = _reloj.Ahora;
        var numero = await _bd.EnTransaccionAsync(async s =>
        {
            var n = await s.EjecutarAsync("UPDATE MensajesCorreo SET Estado = ?, NumeroIntentos = NumeroIntentos + 1, Version = Version + 1 WHERE IdMensajeCorreo = ? AND Estado = ?",
                nameof(EstadoMensaje.EnProceso), id, nameof(EstadoMensaje.Pendiente));
            if (n == 0) return 0;
            var intento = Convert.ToInt32(await s.EscalarAsync("SELECT NumeroIntentos FROM MensajesCorreo WHERE IdMensajeCorreo = ?", id), CultureInfo.InvariantCulture);
            await s.EjecutarAsync("INSERT INTO IntentosMensajeCorreo (IdMensajeCorreo, Numero, Inicio, Resultado, Fase) VALUES (?, ?, ?, 'EnCurso', 'Preparación')", id, intento, inicio);
            return intento;
        });
        if (numero == 0) return null;

        ResultadoTransmision resultado;
        string? desviadoA = null;
        try
        {
            var (transmision, cuenta, desvio, problema) = await PrepararAsync(id);
            desviadoA = desvio;
            resultado = problema is not null || cuenta is null || transmision is null
                ? new ResultadoTransmision(ClaseResultado.FalloDefinitivo, "Preparación", null, problema ?? "No he podido preparar el mensaje.", [])
                : await _transporte.EnviarAsync(cuenta, transmision, cancelacion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resultado = new ResultadoTransmision(ClaseResultado.FalloTransitorio, "Preparación", null, $"No he podido preparar el mensaje: {ex.Message}", [], ex.GetType().Name);
        }
        return await RegistrarAsync(id, numero, resultado, desviadoA);
    }

    /// <summary>Lee la instantánea del mensaje, su cuenta y sus adjuntos (comprobando la huella) y aplica el desvío de pruebas.</summary>
    private async Task<(MensajeTransmision? Mensaje, CuentaCorreo? Cuenta, string? Desvio, string? Problema)> PrepararAsync(int id)
    {
        var detalle = await ObtenerAsync(id);
        if (detalle is null) return (null, null, null, "El mensaje ya no existe.");
        var cuenta = detalle.IdCuentaCorreo is int idCuenta ? await _cuentas.ObtenerAsync(idCuenta) : await _cuentas.ObtenerPorEjercicioAsync(detalle.IdSociedadEjercicio);
        if (cuenta is null) return (null, null, null, "La cuenta de correo con que se preparó ya no existe.");
        if (!cuenta.Activa) return (null, cuenta, null, $"La cuenta de correo {cuenta.Direccion} está desactivada.");
        var adjuntos = new List<(string, string, byte[])>();
        foreach (var a in detalle.Adjuntos)
        {
            if (a.RutaCompleta is null || !File.Exists(a.RutaCompleta)) return (null, cuenta, null, $"No encuentro la copia del adjunto «{a.Nombre}» ({a.RutaCompleta ?? a.RutaRelativa}).");
            var bytes = await File.ReadAllBytesAsync(a.RutaCompleta);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), a.Huella, StringComparison.OrdinalIgnoreCase))
                return (null, cuenta, null, $"La copia del adjunto «{a.Nombre}» ha cambiado desde que se preparó el mensaje (su huella no coincide), así que no la envío.");
            adjuntos.Add((a.Nombre, a.TipoContenido, bytes));
        }
        var desvio = await DesvioAsync();
        var reales = detalle.Destinatarios.Select(d => new Destinatario(d.Tipo, d.Direccion, d.Nombre)).ToList();
        var transmision = new MensajeTransmision(detalle.MessageId, detalle.Remitente, detalle.NombreRemitente, detalle.ResponderA,
            desvio is null ? detalle.Asunto : DesvioPruebas.Asunto(detalle.Asunto),
            desvio is null ? detalle.CuerpoHtml : DesvioPruebas.CuerpoHtml(detalle.CuerpoHtml, reales),
            desvio is null ? detalle.CuerpoTexto : DesvioPruebas.CuerpoTexto(detalle.CuerpoTexto ?? (detalle.CuerpoHtml is null ? "" : null), reales),
            desvio is null ? reales : [new Destinatario(TipoDestinatario.Para, desvio)],
            adjuntos);
        return (transmision, cuenta, desvio, null);
    }

    /// <summary>Guarda el intento, el resultado de cada destinatario y el nuevo estado del mensaje.</summary>
    private async Task<ResultadoEnvio> RegistrarAsync(int id, int numero, ResultadoTransmision r, string? desviadoA)
    {
        var fin = _reloj.Ahora;
        DateTime? proximo = null;
        var estado = r.Clase switch
        {
            ClaseResultado.Aceptado => EstadoMensaje.Aceptado,
            ClaseResultado.AceptadoEnParte => EstadoMensaje.AceptadoEnParte,
            ClaseResultado.Incierto => EstadoMensaje.Incierto,
            ClaseResultado.FalloTransitorio when (proximo = PoliticaReintentos.Siguiente(numero, fin)) is not null => EstadoMensaje.Pendiente,
            _ => EstadoMensaje.Fallido,
        };
        var explicacion = r.Explicacion + estado switch
        {
            EstadoMensaje.Pendiente => $" Lo vuelvo a intentar a las {proximo:HH:mm}{(proximo!.Value.Date != fin.Date ? $" del {proximo:dd/MM}" : "")}.",
            EstadoMensaje.Fallido when r.Clase == ClaseResultado.FalloTransitorio => " Ya no quedan reintentos automáticos: puedes reintentarlo desde el registro de correo.",
            EstadoMensaje.Incierto => " No lo reintento solo: compruébalo con el destinatario y, desde el registro de correo, dalo por enviado o vuelve a enviarlo.",
            _ => "",
        };
        if (desviadoA is not null && r.Clase is ClaseResultado.Aceptado or ClaseResultado.AceptadoEnParte) explicacion += $" Desviado a {desviadoA} (pruebas).";
        await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE IntentosMensajeCorreo SET Fin = ?, Resultado = ?, Fase = ?, RespuestaServidor = ?, Detalle = ? WHERE IdMensajeCorreo = ? AND Numero = ?",
                fin, r.Clase.ToString(), r.Fase, Recortar(r.RespuestaServidor, 255), explicacion + (r.DetalleTecnico is null ? "" : $" [{r.DetalleTecnico}]")
                    + (desviadoA is null ? "" : $" Desviado a {desviadoA}."), id, numero);
            await s.EjecutarAsync("UPDATE MensajesCorreo SET Estado = ?, ProximoIntento = ?, FechaAceptacion = ?, DesviadoA = ?, UltimoError = ?, Version = Version + 1 WHERE IdMensajeCorreo = ?",
                estado.ToString(), proximo, estado is EstadoMensaje.Aceptado or EstadoMensaje.AceptadoEnParte ? fin : null, desviadoA,
                estado is EstadoMensaje.Aceptado ? null : explicacion, id);
            if (desviadoA is null)
                foreach (var d in r.PorDestinatario)
                    await s.EjecutarAsync("UPDATE DestinatariosMensajeCorreo SET Resultado = ?, RespuestaServidor = ? WHERE IdMensajeCorreo = ? AND Direccion = ?",
                        d.Resultado.ToString(), Recortar(d.Respuesta, 255), id, d.Direccion);
            return true;
        });
        return new ResultadoEnvio(id, estado, explicacion, proximo);
    }

    // ---------- acciones del registro ----------

    /// <summary>Reintenta los fallidos: añade un intento al mismo mensaje, con el mismo Message-ID, para enviarlo ya.</summary>
    public Task<IReadOnlyList<int>> ReintentarAsync(IEnumerable<int> ids) =>
        CambiarEstadoAsync(ids, EstadoMensaje.Fallido, EstadoMensaje.Pendiente, "ProximoIntento = ?, UltimoError = Null", _reloj.Ahora);

    /// <summary>Cancela los pendientes que aún no se han empezado a transmitir; devuelve los cancelados.</summary>
    public Task<IReadOnlyList<int>> CancelarAsync(IEnumerable<int> ids, Actor actor) =>
        CambiarEstadoAsync(ids, EstadoMensaje.Pendiente, EstadoMensaje.Cancelado, "ProximoIntento = Null, ResueltoPor = ?, FechaResolucion = ?", Recortar(actor.Nombre, 30), _reloj.Ahora);

    /// <summary>
    /// Resuelve un incierto: <paramref name="enviado"/> lo da por enviado (Aceptado); si no, vuelve a la cola como otro intento
    /// del mismo mensaje, con el riesgo de duplicado que el operador ha aceptado. Siempre con autor y motivo.
    /// </summary>
    public async Task<Resultado> ResolverInciertoAsync(int id, bool enviado, string motivo, Actor actor)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado.Fallo(new Error("Correo.SinMotivo", "Falta el motivo.", $"Mensaje {id}", null, "Escribe por qué lo das por enviado o lo vuelves a enviar (por ejemplo, «el cliente confirma que lo recibió»)."));
        var ahora = _reloj.Ahora;
        var n = await _bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE MensajesCorreo SET Estado = ?, ProximoIntento = ?, FechaAceptacion = ?, ResueltoPor = ?, FechaResolucion = ?, MotivoResolucion = ?, Version = Version + 1 WHERE IdMensajeCorreo = ? AND Estado = ?",
            (enviado ? EstadoMensaje.Aceptado : EstadoMensaje.Pendiente).ToString(), enviado ? null : ahora, enviado ? ahora : null, Recortar(actor.Nombre, 30), ahora,
            Recortar((enviado ? "Dado por enviado: " : "Vuelto a enviar: ") + motivo.Trim(), 255), id, nameof(EstadoMensaje.Incierto)));
        return n > 0 ? Resultado.Correcto() : Resultado.Fallo(new Error("Correo.NoIncierto", "El mensaje ya no está incierto.", $"Mensaje {id}", "Otra sesión lo ha resuelto mientras tanto.", "Actualiza el registro."));
    }

    /// <summary>
    /// Reenvía los aceptados: crea para cada uno un mensaje nuevo, enlazado al original, con su contenido, destinatarios y
    /// adjuntos tal como se enviaron (las mismas copias inmutables). Devuelve los mensajes nuevos.
    /// </summary>
    public async Task<IReadOnlyList<int>> ReenviarAsync(IEnumerable<int> ids, Actor actor, int? idLote = null)
    {
        var nuevos = new List<int>();
        foreach (var id in ids.Distinct())
        {
            var d = await ObtenerAsync(id);
            if (d is null || d.Estado is not (EstadoMensaje.Aceptado or EstadoMensaje.AceptadoEnParte)) continue;
            var dominio = d.Remitente.Contains('@') ? d.Remitente[(d.Remitente.IndexOf('@') + 1)..] : "procontavf.local";
            var ahora = _reloj.Ahora;
            var nuevo = await _bd.EnTransaccionAsync(async s =>
            {
                await s.EjecutarAsync(
                    "INSERT INTO MensajesCorreo (IdSociedadEjercicio, IdCuentaCorreo, IdLoteCorreo, TipoCorreo, TipoOrigen, IdOrigen, ReferenciaOrigen, MessageId, Remitente, NombreRemitente, " +
                    "ResponderA, Asunto, CuerpoHtml, CuerpoTexto, Estado, NumeroIntentos, ProximoIntento, FechaCreacion, Operador, IdMensajeOriginal, Version) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?, ?, ?, 0)",
                    d.IdSociedadEjercicio, d.IdCuentaCorreo, idLote, d.TipoCorreo, d.TipoOrigen, d.IdOrigen, d.ReferenciaOrigen, $"{Guid.NewGuid():N}@{dominio}", d.Remitente,
                    d.NombreRemitente, d.ResponderA, d.Asunto, d.CuerpoHtml, d.CuerpoTexto, nameof(EstadoMensaje.Pendiente), ahora, ahora, Recortar(actor.Nombre, 30), id);
                var n = await s.UltimoIdAsync();
                foreach (var x in d.Destinatarios)
                    await s.EjecutarAsync("INSERT INTO DestinatariosMensajeCorreo (IdMensajeCorreo, Tipo, Direccion, Nombre, Resultado) VALUES (?, ?, ?, ?, ?)",
                        n, CodigoTipo(x.Tipo), x.Direccion, x.Nombre, nameof(ResultadoDestinatario.SinResultado));
                foreach (var a in d.Adjuntos)
                    await s.EjecutarAsync("INSERT INTO AdjuntosMensajeCorreo (IdMensajeCorreo, Ordinal, Nombre, TipoContenido, TamanoBytes, Huella, RutaRelativa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        n, (short)a.Ordinal, a.Nombre, a.TipoContenido, a.TamanoBytes, a.Huella, a.RutaRelativa);
                return n;
            });
            nuevos.Add(nuevo);
        }
        return nuevos;
    }

    private async Task<IReadOnlyList<int>> CambiarEstadoAsync(IEnumerable<int> ids, EstadoMensaje desde, EstadoMensaje hasta, string asignaciones, params object?[] valores)
    {
        var hechos = new List<int>();
        foreach (var id in ids.Distinct())
        {
            var n = await _bd.EjecutarAsync(s => s.EjecutarAsync(
                $"UPDATE MensajesCorreo SET Estado = ?, {asignaciones}, Version = Version + 1 WHERE IdMensajeCorreo = ? AND Estado = ?",
                [hasta.ToString(), .. valores, id, desde.ToString()]));
            if (n > 0) hechos.Add(id);
        }
        return hechos;
    }

    // ---------- consultas ----------

    public async Task<EstadoMensaje?> EstadoAsync(int id)
    {
        var v = await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT Estado FROM MensajesCorreo WHERE IdMensajeCorreo = ?", id));
        return v is string t && Enum.TryParse<EstadoMensaje>(t, out var e) ? e : null;
    }

    /// <summary>Mensajes del registro, los más recientes primero, con un resumen de sus destinatarios.</summary>
    public async Task<IReadOnlyList<FilaMensaje>> ListarAsync(FiltroRegistro filtro)
    {
        var condiciones = new List<string>();
        var parametros = new List<object?>();
        if (filtro.IdLote is int lote) { condiciones.Add("m.IdLoteCorreo = ?"); parametros.Add(lote); }
        else
        {
            if (filtro.IdSociedadEjercicio is int se) { condiciones.Add("m.IdSociedadEjercicio = ?"); parametros.Add(se); }
            if (filtro.Desde is DateTime desde) { condiciones.Add("m.FechaCreacion >= ?"); parametros.Add(desde.Date); }
        }
        if (filtro.TipoOrigen is { } tipo && filtro.IdOrigen is int origen) { condiciones.Add("m.TipoOrigen = ? AND m.IdOrigen = ?"); parametros.Add(tipo); parametros.Add(origen); }
        var donde = condiciones.Count == 0 ? "" : " WHERE " + string.Join(" AND ", condiciones);
        var filas = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            $"SELECT TOP {Math.Clamp(filtro.Maximo, 1, 5000)} m.IdMensajeCorreo, m.IdLoteCorreo, m.FechaCreacion, m.FechaAceptacion, m.Estado, m.Asunto, m.TipoOrigen, m.IdOrigen, m.ReferenciaOrigen, " +
            "m.NumeroIntentos, m.ProximoIntento, m.UltimoError, m.DesviadoA, m.Remitente, m.Operador, m.IdMensajeOriginal, " +
            "(SELECT COUNT(*) FROM AdjuntosMensajeCorreo AS a WHERE a.IdMensajeCorreo = m.IdMensajeCorreo) AS Adjuntos " +
            $"FROM MensajesCorreo AS m{donde} ORDER BY m.FechaCreacion DESC, m.IdMensajeCorreo DESC",
            f => new FilaMensaje
            {
                IdMensaje = f.Entero("IdMensajeCorreo"),
                IdLote = f.EnteroNulo("IdLoteCorreo"),
                FechaCreacion = f.Fecha("FechaCreacion"),
                FechaAceptacion = f.FechaNula("FechaAceptacion"),
                Estado = Enum.TryParse<EstadoMensaje>(f.Texto("Estado"), out var e) ? e : EstadoMensaje.Pendiente,
                Asunto = f.Texto("Asunto") ?? "",
                TipoOrigen = f.Texto("TipoOrigen"),
                IdOrigen = f.EnteroNulo("IdOrigen"),
                ReferenciaOrigen = f.Texto("ReferenciaOrigen"),
                Intentos = f.Entero("NumeroIntentos"),
                ProximoIntento = f.FechaNula("ProximoIntento"),
                UltimoError = f.Texto("UltimoError"),
                DesviadoA = f.Texto("DesviadoA"),
                Remitente = f.Texto("Remitente") ?? "",
                Operador = f.Texto("Operador") ?? "",
                IdMensajeOriginal = f.EnteroNulo("IdMensajeOriginal"),
                Adjuntos = f.Entero("Adjuntos"),
            }, [.. parametros]));
        if (filas.Count == 0) return filas;
        var destinatarios = new List<(int Id, string Tipo, string Direccion)>();
        foreach (var trozo in filas.Select(f => f.IdMensaje).Chunk(100))
            destinatarios.AddRange(await _bd.EjecutarAsync(s => s.ConsultarAsync(
                $"SELECT IdMensajeCorreo, Tipo, Direccion FROM DestinatariosMensajeCorreo WHERE IdMensajeCorreo IN ({string.Join(",", trozo)}) ORDER BY IdDestinatarioMensaje",
                f => (f.Entero("IdMensajeCorreo"), f.Texto("Tipo") ?? "", f.Texto("Direccion") ?? ""))));
        var porMensaje = destinatarios.ToLookup(d => d.Id);
        foreach (var f in filas)
        {
            var lista = porMensaje[f.IdMensaje].ToList();
            f.Para = string.Join("; ", lista.Where(d => d.Tipo == "PARA").Select(d => d.Direccion));
            f.TotalDestinatarios = lista.Count;
        }
        return filas;
    }

    /// <summary>Detalle de solo lectura de un mensaje: instantánea, destinatarios, adjuntos (con su ruta) e intentos.</summary>
    public async Task<DetalleMensaje?> ObtenerAsync(int id)
    {
        var d = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT m.*, se.Ejercicio, so.RutaDocumentos FROM (MensajesCorreo AS m INNER JOIN SociedadesEjercicios AS se ON m.IdSociedadEjercicio = se.IdSociedadEjercicio) " +
            "LEFT JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad WHERE m.IdMensajeCorreo = ?",
            f => new DetalleMensaje
            {
                IdMensaje = f.Entero("IdMensajeCorreo"),
                IdSociedadEjercicio = f.Entero("IdSociedadEjercicio"),
                IdCuentaCorreo = f.EnteroNulo("IdCuentaCorreo"),
                IdLote = f.EnteroNulo("IdLoteCorreo"),
                TipoCorreo = f.Texto("TipoCorreo") ?? TiposCorreo.Otro,
                TipoOrigen = f.Texto("TipoOrigen"),
                IdOrigen = f.EnteroNulo("IdOrigen"),
                ReferenciaOrigen = f.Texto("ReferenciaOrigen"),
                MessageId = f.Texto("MessageId") ?? "",
                Remitente = f.Texto("Remitente") ?? "",
                NombreRemitente = f.Texto("NombreRemitente"),
                ResponderA = f.Texto("ResponderA"),
                Asunto = f.Texto("Asunto") ?? "",
                CuerpoHtml = f.Texto("CuerpoHtml"),
                CuerpoTexto = f.Texto("CuerpoTexto"),
                Estado = Enum.TryParse<EstadoMensaje>(f.Texto("Estado"), out var e) ? e : EstadoMensaje.Pendiente,
                Intentos = f.Entero("NumeroIntentos"),
                ProximoIntento = f.FechaNula("ProximoIntento"),
                FechaCreacion = f.Fecha("FechaCreacion"),
                FechaAceptacion = f.FechaNula("FechaAceptacion"),
                Operador = f.Texto("Operador") ?? "",
                IdMensajeOriginal = f.EnteroNulo("IdMensajeOriginal"),
                DesviadoA = f.Texto("DesviadoA"),
                UltimoError = f.Texto("UltimoError"),
                ResueltoPor = f.Texto("ResueltoPor"),
                FechaResolucion = f.FechaNula("FechaResolucion"),
                MotivoResolucion = f.Texto("MotivoResolucion"),
                RaizDocumentos = f.Texto("RutaDocumentos"),
            }, id));
        if (d is null) return null;
        return await _bd.EjecutarAsync(async s =>
        {
            d.Destinatarios = await s.ConsultarAsync(
                "SELECT Tipo, Direccion, Nombre, Resultado, RespuestaServidor FROM DestinatariosMensajeCorreo WHERE IdMensajeCorreo = ? ORDER BY IdDestinatarioMensaje",
                f => new DestinatarioRegistrado(TipoDeCodigo(f.Texto("Tipo")), f.Texto("Direccion") ?? "", f.Texto("Nombre"),
                    Enum.TryParse<ResultadoDestinatario>(f.Texto("Resultado"), out var r) ? r : ResultadoDestinatario.SinResultado, f.Texto("RespuestaServidor")), id);
            d.Adjuntos = await s.ConsultarAsync(
                "SELECT IdAdjuntoMensaje, Ordinal, Nombre, TipoContenido, TamanoBytes, Huella, RutaRelativa FROM AdjuntosMensajeCorreo WHERE IdMensajeCorreo = ? ORDER BY Ordinal",
                f => new AdjuntoRegistrado(f.Entero("IdAdjuntoMensaje"), f.Entero("Ordinal"), f.Texto("Nombre") ?? "", f.Texto("TipoContenido") ?? "application/octet-stream",
                    f.Entero("TamanoBytes"), f.Texto("Huella") ?? "", f.Texto("RutaRelativa") ?? "",
                    string.IsNullOrWhiteSpace(d.RaizDocumentos) ? null : Path.Combine(d.RaizDocumentos.Trim(), f.Texto("RutaRelativa") ?? "")), id);
            d.HistorialIntentos = await s.ConsultarAsync(
                "SELECT Numero, Inicio, Fin, Resultado, Fase, RespuestaServidor, Detalle FROM IntentosMensajeCorreo WHERE IdMensajeCorreo = ? ORDER BY Numero",
                f => new IntentoRegistrado(f.Entero("Numero"), f.Fecha("Inicio"), f.FechaNula("Fin"), f.Texto("Resultado") ?? "", f.Texto("Fase"), f.Texto("RespuestaServidor"), f.Texto("Detalle")), id);
            return d;
        });
    }

    /// <summary>Lotes recientes, para el resumen y los filtros del registro.</summary>
    public Task<IReadOnlyList<(int IdLote, string Descripcion, DateTime Fecha)>> LotesAsync(int idSociedadEjercicio) =>
        _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT TOP 200 IdLoteCorreo, Descripcion, FechaCreacion FROM LotesCorreo WHERE IdSociedadEjercicio = ? ORDER BY IdLoteCorreo DESC",
            f => (f.Entero("IdLoteCorreo"), f.Texto("Descripcion") ?? "", f.Fecha("FechaCreacion")), idSociedadEjercicio));

    // ---------- apoyo ----------

    private async Task<(string? Raiz, string Ejercicio, Error? Error)> CarpetaDocumentosAsync(int idSociedadEjercicio)
    {
        var fila = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT se.Ejercicio, so.Nombre, so.RutaDocumentos FROM SociedadesEjercicios AS se LEFT JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad WHERE se.IdSociedadEjercicio = ?",
            f => (Ejercicio: f.Texto("Ejercicio") ?? "", Nombre: f.Texto("Nombre") ?? "", Raiz: f.Texto("RutaDocumentos")), idSociedadEjercicio));
        if (string.IsNullOrWhiteSpace(fila.Raiz))
            return (null, fila.Ejercicio, new Error("Correo.SinCarpetaDocumentos", $"La empresa {fila.Nombre} no tiene carpeta de documentos, y es donde se guarda la copia de los adjuntos enviados.", null, null,
                "Escríbela en Configuración › Parámetros y tablas › Empresas (grupo «Rutas y bases de datos») y vuelve a enviar."));
        return (fila.Raiz.Trim(), fila.Ejercicio.Trim(), null);
    }

    internal static string CodigoTipo(TipoDestinatario t) => t switch { TipoDestinatario.Para => "PARA", TipoDestinatario.Cc => "CC", _ => "CCO" };

    internal static TipoDestinatario TipoDeCodigo(string? c) => c switch { "CC" => TipoDestinatario.Cc, "CCO" => TipoDestinatario.Cco, _ => TipoDestinatario.Para };

    private static string NombreSeguro(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(nombre.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (limpio.Length == 0) limpio = "adjunto";
        return limpio.Length > 120 ? limpio[..120] : limpio;
    }

    internal static string? Recortar(string? texto, int maximo) => texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];

    public static string Texto(EstadoMensaje e) => e switch
    {
        EstadoMensaje.Pendiente => "Pendiente",
        EstadoMensaje.EnProceso => "Enviando",
        EstadoMensaje.Aceptado => "Aceptado por el servidor",
        EstadoMensaje.AceptadoEnParte => "Aceptado en parte",
        EstadoMensaje.Fallido => "Fallido",
        EstadoMensaje.Incierto => "Incierto",
        _ => "Cancelado",
    };
}

/// <summary>Filtro del registro de correo: un lote concreto, o los de un ejercicio desde una fecha, y opcionalmente de un origen.</summary>
public sealed record FiltroRegistro(int? IdSociedadEjercicio = null, int? IdLote = null, DateTime? Desde = null, string? TipoOrigen = null, int? IdOrigen = null, int Maximo = 2000);

/// <summary>Fila del registro de correo.</summary>
public sealed class FilaMensaje
{
    public int IdMensaje { get; init; }
    public int? IdLote { get; init; }
    public DateTime FechaCreacion { get; init; }
    public DateTime? FechaAceptacion { get; init; }
    public EstadoMensaje Estado { get; init; }
    public string Asunto { get; init; } = "";
    public string? TipoOrigen { get; init; }
    public int? IdOrigen { get; init; }
    public string? ReferenciaOrigen { get; init; }
    public int Intentos { get; init; }
    public DateTime? ProximoIntento { get; init; }
    public string? UltimoError { get; init; }
    public string? DesviadoA { get; init; }
    public string Remitente { get; init; } = "";
    public string Operador { get; init; } = "";
    public int? IdMensajeOriginal { get; init; }
    public int Adjuntos { get; init; }
    public string Para { get; set; } = "";
    public int TotalDestinatarios { get; set; }
}

public sealed record DestinatarioRegistrado(TipoDestinatario Tipo, string Direccion, string? Nombre, ResultadoDestinatario Resultado, string? Respuesta);

public sealed record AdjuntoRegistrado(int IdAdjunto, int Ordinal, string Nombre, string TipoContenido, int TamanoBytes, string Huella, string RutaRelativa, string? RutaCompleta);

public sealed record IntentoRegistrado(int Numero, DateTime Inicio, DateTime? Fin, string Resultado, string? Fase, string? RespuestaServidor, string? Detalle);

/// <summary>Detalle de solo lectura de un mensaje del registro: lo que se guardó al prepararlo, no lo que diría la plantilla de hoy.</summary>
public sealed class DetalleMensaje
{
    public int IdMensaje { get; init; }
    public int IdSociedadEjercicio { get; init; }
    public int? IdCuentaCorreo { get; init; }
    public int? IdLote { get; init; }
    public string TipoCorreo { get; init; } = TiposCorreo.Otro;
    public string? TipoOrigen { get; init; }
    public int? IdOrigen { get; init; }
    public string? ReferenciaOrigen { get; init; }
    public string MessageId { get; init; } = "";
    public string Remitente { get; init; } = "";
    public string? NombreRemitente { get; init; }
    public string? ResponderA { get; init; }
    public string Asunto { get; init; } = "";
    public string? CuerpoHtml { get; init; }
    public string? CuerpoTexto { get; init; }
    public EstadoMensaje Estado { get; init; }
    public int Intentos { get; init; }
    public DateTime? ProximoIntento { get; init; }
    public DateTime FechaCreacion { get; init; }
    public DateTime? FechaAceptacion { get; init; }
    public string Operador { get; init; } = "";
    public int? IdMensajeOriginal { get; init; }
    public string? DesviadoA { get; init; }
    public string? UltimoError { get; init; }
    public string? ResueltoPor { get; init; }
    public DateTime? FechaResolucion { get; init; }
    public string? MotivoResolucion { get; init; }
    public string? RaizDocumentos { get; init; }
    public IReadOnlyList<DestinatarioRegistrado> Destinatarios { get; set; } = [];
    public IReadOnlyList<AdjuntoRegistrado> Adjuntos { get; set; } = [];
    public IReadOnlyList<IntentoRegistrado> HistorialIntentos { get; set; } = [];
}
