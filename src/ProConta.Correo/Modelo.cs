namespace ProConta.Correo;

/// <summary>Cifrado de la conexión con el servidor de correo. Nunca hay conexión en claro (correo/envio 2026-11-10 19:40).</summary>
public enum SeguridadConexion
{
    /// <summary>Conexión normal que pasa a cifrada con STARTTLS (SMTP 587, POP3 110); si el servidor no lo admite, no se conecta.</summary>
    StartTls,
    /// <summary>Cifrada desde el principio (SMTP 465, POP3 995).</summary>
    TlsImplicito,
}

/// <summary>Cuenta de correo de una sociedad y un ejercicio: envía por SMTP y recibe por POP3 (correo/envio 2026-11-10 19:40).</summary>
public sealed record CuentaCorreo
{
    public int IdCuentaCorreo { get; init; }
    public int IdSociedadEjercicio { get; init; }
    public bool Activa { get; init; } = true;
    public string Direccion { get; init; } = "";
    public string? NombreVisible { get; init; }
    /// <summary>Usuario con el que se inicia sesión; vacío, la propia dirección.</summary>
    public string? Usuario { get; init; }
    /// <summary>Contraseña en claro, solo en memoria mientras se usa; null si no hay o no se puede descifrar.</summary>
    public string? Contrasena { get; init; }
    public string? ResponderA { get; init; }
    public string? ServidorSmtp { get; init; }
    public int PuertoSmtp { get; init; } = 587;
    public SeguridadConexion SeguridadSmtp { get; init; } = SeguridadConexion.StartTls;
    public string? ServidorPop3 { get; init; }
    public int PuertoPop3 { get; init; } = 110;
    public SeguridadConexion SeguridadPop3 { get; init; } = SeguridadConexion.StartTls;
    public bool EliminarTrasProcesar { get; init; } = true;
    public int IntervaloRecepcionMinutos { get; init; } = 5;
    public int TiempoEsperaSegundos { get; init; } = 60;

    public string UsuarioEfectivo => string.IsNullOrWhiteSpace(Usuario) ? Direccion : Usuario.Trim();
}

/// <summary>Estado de transporte de un mensaje saliente. No es su entrega en el buzón del destinatario (correo/envio 2026-11-10 19:40).</summary>
public enum EstadoMensaje
{
    /// <summary>En la cola, esperando su turno o su próximo reintento.</summary>
    Pendiente,
    /// <summary>Una sesión lo está transmitiendo ahora.</summary>
    EnProceso,
    /// <summary>Nuestro servidor SMTP lo aceptó para todos los destinatarios.</summary>
    Aceptado,
    /// <summary>Nuestro servidor lo aceptó para unos destinatarios y rechazó otros.</summary>
    AceptadoEnParte,
    /// <summary>Consta que no se envió a nadie y no se va a reintentar solo.</summary>
    Fallido,
    /// <summary>La conexión se cortó después de mandar el contenido y antes de la respuesta: no se sabe si salió.</summary>
    Incierto,
    /// <summary>Se canceló antes de transmitirlo.</summary>
    Cancelado,
}

/// <summary>Campo de destinatario.</summary>
public enum TipoDestinatario { Para, Cc, Cco }

/// <summary>Resultado de un destinatario en el último intento.</summary>
public enum ResultadoDestinatario
{
    SinResultado,
    Aceptado,
    Rechazado,
}

/// <summary>Una dirección de un mensaje, con el campo en que va.</summary>
public sealed record Destinatario(TipoDestinatario Tipo, string Direccion, string? Nombre = null);

/// <summary>Adjunto que el originador entrega al preparar el mensaje: el contenido exacto que saldrá.</summary>
public sealed record AdjuntoNuevo(string Nombre, byte[] Contenido, string? TipoContenido = null)
{
    /// <summary>Lee un fichero para adjuntarlo tal como está ahora.</summary>
    public static AdjuntoNuevo DesdeFichero(string ruta, string? nombre = null) =>
        new(nombre ?? Path.GetFileName(ruta), File.ReadAllBytes(ruta), TiposContenido.De(ruta));
}

/// <summary>
/// Mensaje que prepara un originador (facturación, terceros…) para encolarlo. El contexto de sociedad y ejercicio queda fijado
/// aquí: un cambio posterior de la empresa activa no lo altera.
/// </summary>
public sealed record MensajeNuevo
{
    public required int IdSociedadEjercicio { get; init; }
    /// <summary>Código de TiposCorreo: FACTURA, OTRO…</summary>
    public string TipoCorreo { get; init; } = TiposCorreo.Otro;
    /// <summary>Origen funcional («Factura», «Tercero»…) y su identificador, para abrirlo desde el registro.</summary>
    public string? TipoOrigen { get; init; }
    public int? IdOrigen { get; init; }
    /// <summary>Texto con que se reconoce el origen en el registro (número de factura, nombre del tercero).</summary>
    public string? ReferenciaOrigen { get; init; }
    public IReadOnlyList<Destinatario> Destinatarios { get; init; } = [];
    public string Asunto { get; init; } = "";
    public string? CuerpoHtml { get; init; }
    public string? CuerpoTexto { get; init; }
    public IReadOnlyList<AdjuntoNuevo> Adjuntos { get; init; } = [];
}

/// <summary>Tipos de correo de TiposCorreo que usa el programa.</summary>
public static class TiposCorreo
{
    public const string Factura = "FACTURA";
    public const string Otro = "OTRO";
}

/// <summary>Tipo MIME de un adjunto por su extensión.</summary>
public static class TiposContenido
{
    public static string De(string nombre) => Path.GetExtension(nombre).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xml" => "application/xml",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };
}
