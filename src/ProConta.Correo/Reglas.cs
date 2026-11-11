using System.Net.Mail;
using ProConta.Nucleo.Resultados;

namespace ProConta.Correo;

/// <summary>Comprobaciones de un mensaje antes de encolarlo, sin BD ni red.</summary>
public static class ReglasMensaje
{
    /// <summary>Longitud máxima de una dirección (RFC 5321) y del asunto (MensajesCorreo.Asunto).</summary>
    public const int LongitudDireccion = 254, LongitudAsunto = 255;

    /// <summary>
    /// Dirección normalizada si es válida (usuario@dominio con punto en el dominio, sin espacios); si no, null y el motivo
    /// para el operador.
    /// </summary>
    public static (string? Direccion, string? Motivo) Normalizar(string? texto)
    {
        var d = texto?.Trim().Trim(';', ',').Trim();
        if (string.IsNullOrEmpty(d)) return (null, "Está vacía.");
        if (d.Length > LongitudDireccion) return (null, $"Tiene más de {LongitudDireccion} caracteres.");
        if (d.Any(char.IsWhiteSpace)) return (null, "Tiene espacios: separa las direcciones con «;».");
        var arroba = d.IndexOf('@');
        if (arroba <= 0 || arroba != d.LastIndexOf('@') || arroba == d.Length - 1) return (null, "Le falta la @ o le sobra alguna: debe ser como nombre@empresa.es.");
        var dominio = d[(arroba + 1)..];
        if (!dominio.Contains('.') || dominio.StartsWith('.') || dominio.EndsWith('.') || dominio.Contains(".."))
            return (null, $"El dominio «{dominio}» está incompleto: debe ser como empresa.es.");
        try
        {
            var m = new MailAddress(d);
            if (!string.Equals(m.Address, d, StringComparison.OrdinalIgnoreCase)) return (null, "No es una dirección de correo válida.");
        }
        catch (FormatException)
        {
            return (null, "No es una dirección de correo válida.");
        }
        return (d, null);
    }

    /// <summary>Separa un texto con varias direcciones («;», «,» o saltos de línea).</summary>
    public static IReadOnlyList<string> Separar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? [] : texto.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Errores del mensaje: destinatarios mal escritos o repetidos, sin destinatario «Para», sin asunto, adjuntos vacíos.</summary>
    public static IReadOnlyList<Error> Validar(MensajeNuevo m)
    {
        var errores = new List<Error>();
        var registro = m.ReferenciaOrigen ?? m.Asunto;
        if (!m.Destinatarios.Any(d => d.Tipo == TipoDestinatario.Para))
            errores.Add(new Error("Correo.SinDestinatario", "El mensaje no tiene ningún destinatario en «Para».", registro, null, "Escribe al menos una dirección en «Para»."));
        foreach (var d in m.Destinatarios)
            if (Normalizar(d.Direccion) is { Direccion: null, Motivo: var motivo })
                errores.Add(new Error("Correo.DireccionNoValida", $"La dirección «{d.Direccion}» de {Campo(d.Tipo)} no vale. {motivo}", registro, null, "Corrígela o quítala."));
        foreach (var g in m.Destinatarios.GroupBy(d => d.Direccion.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errores.Add(new Error("Correo.DireccionRepetida", $"La dirección {g.Key} está repetida ({string.Join(" y ", g.Select(d => Campo(d.Tipo)).Distinct())}).", registro, null,
                "Déjala solo en un campo: se enviaría dos veces a la misma persona."));
        if (string.IsNullOrWhiteSpace(m.Asunto))
            errores.Add(new Error("Correo.SinAsunto", "El mensaje no tiene asunto.", registro, null, "Escribe el asunto: muchos servidores tratan como sospechoso un correo sin él."));
        else if (m.Asunto.Length > LongitudAsunto)
            errores.Add(new Error("Correo.AsuntoLargo", $"El asunto tiene {m.Asunto.Length} caracteres y el máximo es {LongitudAsunto}.", registro, null, "Acórtalo."));
        foreach (var a in m.Adjuntos.Where(a => a.Contenido.Length == 0))
            errores.Add(new Error("Correo.AdjuntoVacio", $"El adjunto «{a.Nombre}» está vacío.", registro, null, "Quítalo o vuelve a adjuntar el fichero correcto."));
        return errores;
    }

    public static string Campo(TipoDestinatario t) => t switch { TipoDestinatario.Para => "Para", TipoDestinatario.Cc => "CC", _ => "CCO" };
}

/// <summary>
/// Reintentos automáticos (correo/envio 2026-11-10 19:40): solo ante fallo confirmado y transitorio, hasta cuatro, a los
/// 5 minutos, 30 minutos, 2 horas y 16 horas del intento anterior. Nunca ante un mensaje incierto ni aceptado.
/// </summary>
public static class PoliticaReintentos
{
    public static IReadOnlyList<TimeSpan> Esperas { get; } =
        [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(16)];

    /// <summary>
    /// Cuándo toca el siguiente intento tras el fallo transitorio del intento <paramref name="intentosHechos"/> (1 = el
    /// primero); null si ya no quedan reintentos automáticos.
    /// </summary>
    public static DateTime? Siguiente(int intentosHechos, DateTime finUltimo) =>
        intentosHechos >= 1 && intentosHechos <= Esperas.Count ? finUltimo + Esperas[intentosHechos - 1] : null;
}

/// <summary>
/// Desvío de pruebas (correo/envio 2026-11-10 19:40): con Configuracion.CorreoDesviarA, el mensaje sale solo a esa dirección,
/// con los destinatarios reales anotados al principio del asunto y del cuerpo para que se vea a quién habría ido.
/// </summary>
public static class DesvioPruebas
{
    public static string Asunto(string asunto) => $"[PRUEBAS] {asunto}";

    public static string Nota(IEnumerable<Destinatario> reales) =>
        "Correo de pruebas de ProContaVF. Destinatarios reales: " +
        string.Join("; ", reales.Select(d => $"{ReglasMensaje.Campo(d.Tipo)} {d.Direccion}")) + ".";

    public static string? CuerpoHtml(string? html, IEnumerable<Destinatario> reales) =>
        html is null ? null : $"<p style=\"background:#fff3cd;padding:6px\">{System.Net.WebUtility.HtmlEncode(Nota(reales))}</p>{html}";

    public static string? CuerpoTexto(string? texto, IEnumerable<Destinatario> reales) =>
        texto is null ? null : Nota(reales) + Environment.NewLine + Environment.NewLine + texto;
}
