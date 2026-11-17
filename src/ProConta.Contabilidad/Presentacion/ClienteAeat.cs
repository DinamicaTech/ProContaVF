using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>Entorno de la AEAT al que se envía: el de pruebas (prewww1, sin efectos) o el de producción. Va ligado al de la BD.</summary>
public enum EntornoAeat { Pruebas, Produccion }

/// <summary>
/// Direcciones de los servicios de presentación directa de la AEAT (sources/aeat/EspecificacionesServiciosDeclaracionesAEAT.pdf, versión 30).
/// La validación (ServaliDos) no tiene entorno: es un servicio de pruebas sin certificado que no presenta nada.
/// </summary>
public static class ServiciosAeat
{
    public const string Validacion = "https://prewww2.aeat.es/wlpl/PFTW-PICW/ServValiDos";
    private const string Produccion = "https://www1.agenciatributaria.gob.es";
    private const string Pruebas = "https://prewww1.aeat.es";

    public static string Presentacion(EntornoAeat entorno) => (entorno == EntornoAeat.Pruebas ? Pruebas : Produccion) + "/wlpl/PFTW-PICW/PresBasicaDos";

    public static string Consulta(EntornoAeat entorno) => (entorno == EntornoAeat.Pruebas ? Pruebas : Produccion) + "/wlpl/SCEJ-MANT/ConsultaExt";

    /// <summary>
    /// Si la dirección es del entorno: las de pruebas son de un servidor «prewww» de la AEAT y las de producción de agenciatributaria.gob.es
    /// sin «prewww». Es la última comprobación antes de enviar: una BD de pruebas nunca envía a producción ni al revés.
    /// </summary>
    public static bool EsDelEntorno(string url, EntornoAeat entorno)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        var esPruebas = host.StartsWith("prewww", StringComparison.Ordinal) && host.EndsWith(".aeat.es", StringComparison.Ordinal);
        var esProduccion = !host.StartsWith("prewww", StringComparison.Ordinal) && (host == "agenciatributaria.gob.es" || host.EndsWith(".agenciatributaria.gob.es", StringComparison.Ordinal));
        return entorno == EntornoAeat.Pruebas ? esPruebas : esProduccion;
    }

    public static string Nombre(EntornoAeat entorno) => entorno == EntornoAeat.Pruebas ? "pruebas" : "producción";

    /// <summary>Valor de PresentacionesAeat.Entorno.</summary>
    public static string Codigo(EntornoAeat entorno) => entorno == EntornoAeat.Pruebas ? "PRUEBAS" : "PRODUCCION";
}

/// <summary>Respuesta de ServaliDos: el PDF del modelo (null si hay errores), sus avisos y los errores (hasta 100).</summary>
public sealed record ValidacionAeat(byte[]? Pdf, IReadOnlyList<string> Avisos, IReadOnlyList<string> Errores)
{
    public bool Correcta => Pdf is not null && Errores.Count == 0;
}

/// <summary>Petición a PresBasicaDos. <see cref="Nrc"/> solo con la forma de pago I.</summary>
public sealed record PeticionPresentacionAeat(string Modelo, string Ejercicio, string Periodo, string Fichero, string FirNif, string FirNombre, string? Nrc = null, string Idioma = "ES");

/// <summary>Respuesta de PresBasicaDos: la correcta trae el CSV y la dirección del PDF justificante; la de error, la lista de errores.</summary>
public sealed record RespuestaPresentacionAeat(
    string? Csv, string? Fecha, string? Hora, string? Expediente, string? Justificante, string? FormaPago, decimal? ImporteAIngresar, string? UrlPdf,
    string? NifDeclarante, string? NifPresentador, string? Presentador, IReadOnlyList<string> Avisos, IReadOnlyList<string> Advertencias,
    IReadOnlyList<string> Errores, string Json)
{
    public bool Correcta => !string.IsNullOrWhiteSpace(Csv) && Errores.Count == 0;

    /// <summary>Fecha y hora de la presentación (null si no se entienden).</summary>
    public DateTime? FechaHora =>
        DateTime.TryParseExact($"{Fecha} {Hora}", ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
}

/// <summary>Una declaración presentada según ConsultaExt (esquema consultas.xsd de la AEAT).</summary>
public sealed record DeclaracionPresentadaAeat(string Ejercicio, string Modelo, string Periodo, string Nif, string Csv, string Expediente, string Justificante,
    string? JustificanteAnterior, DateTime? FechaHora);

/// <summary>Respuesta de ConsultaExt: las declaraciones (hasta 100) o el error de la AEAT.</summary>
public sealed record ConsultaAeat(IReadOnlyList<DeclaracionPresentadaAeat> Presentadas, string? Error)
{
    public bool Correcta => Error is null;
}

/// <summary>Servicios de la AEAT que usa la presentación (en pruebas se sustituye por uno simulado).</summary>
public interface IClienteAeat
{
    Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken cancelacion = default);
    Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat peticion, CancellationToken cancelacion = default);
    Task<ConsultaAeat> ConsultarAsync(EntornoAeat entorno, X509Certificate2 certificado, string nif, string nombre, string modelo, string ejercicio, string? periodo,
        CancellationToken cancelacion = default);
    Task<byte[]> DescargarAsync(string url, X509Certificate2? certificado, CancellationToken cancelacion = default);
}

/// <summary>
/// Cliente HTTP de los servicios de la AEAT (contabilidad/modelos-tributarios/presentacion). ServaliDos sin certificado; PresBasicaDos y
/// ConsultaExt con autenticación TLS con el certificado de la sociedad («firma no criptográfica»). Antes de cada envío comprueba que la
/// dirección es del entorno pedido. Nunca guarda ni registra datos del certificado.
/// </summary>
public sealed class ClienteAeatHttp(Func<X509Certificate2?, HttpMessageHandler>? manejador = null) : IClienteAeat
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(90);

    public async Task<ValidacionAeat> ValidarAsync(string modelo, string ejercicio, string periodo, string fichero, CancellationToken cancelacion = default)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["MODELO"] = modelo, ["EJERCICIO"] = ejercicio, ["PERIODO"] = periodo, ["IDI"] = "ES", ["FIC"] = Limpiar(fichero),
        });
        var cuerpo = await PostAsync(ServiciosAeat.Validacion, null, new StringContent(json, new UTF8Encoding(false), "application/json"), cancelacion);
        return LeerValidacion(cuerpo);
    }

    public async Task<RespuestaPresentacionAeat> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, PeticionPresentacionAeat p, CancellationToken cancelacion = default)
    {
        var url = ServiciosAeat.Presentacion(entorno);
        if (!ServiciosAeat.EsDelEntorno(url, entorno)) throw new InvalidOperationException($"La dirección {url} no es del entorno de {ServiciosAeat.Nombre(entorno)}.");
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["MODELO"] = p.Modelo, ["EJERCICIO"] = p.Ejercicio, ["PERIODO"] = p.Periodo, ["NRC"] = p.Nrc ?? "", ["IDI"] = p.Idioma,
            ["F01"] = Limpiar(p.Fichero), ["FIR"] = "FirmaBasica", ["FIRNIF"] = p.FirNif, ["FIRNOMBRE"] = p.FirNombre,
        });
        var cuerpo = await PostAsync(url, certificado, new StringContent(json, new UTF8Encoding(false), "application/json"), cancelacion);
        return LeerPresentacion(cuerpo);
    }

    public async Task<ConsultaAeat> ConsultarAsync(EntornoAeat entorno, X509Certificate2 certificado, string nif, string nombre, string modelo, string ejercicio, string? periodo,
        CancellationToken cancelacion = default)
    {
        var url = ServiciosAeat.Consulta(entorno);
        if (!ServiciosAeat.EsDelEntorno(url, entorno)) throw new InvalidOperationException($"La dirección {url} no es del entorno de {ServiciosAeat.Nombre(entorno)}.");
        // Como un formulario: los nombres tal cual y solo los valores codificados, en el orden de la especificación.
        var campos = new List<(string, string)> { ("NIF", nif), ("ANR", nombre), ("MOD", modelo), ("EJF", ejercicio) };
        if (!string.IsNullOrWhiteSpace(periodo)) campos.Add(("PER", periodo));
        var texto = string.Join("&", campos.Select(c => $"{c.Item1}={Uri.EscapeDataString(c.Item2)}"));
        var cuerpo = await PostAsync(url, certificado, new StringContent(texto, Encoding.UTF8, "application/x-www-form-urlencoded"), cancelacion);
        return LeerConsulta(cuerpo);
    }

    public async Task<byte[]> DescargarAsync(string url, X509Certificate2? certificado, CancellationToken cancelacion = default)
    {
        using var handler = Manejador(certificado);
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Espera };
        using var r = await http.GetAsync(url, cancelacion);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync(cancelacion);
    }

    private HttpMessageHandler Manejador(X509Certificate2? certificado)
    {
        if (manejador is not null) return manejador(certificado);
        var h = new HttpClientHandler();
        if (certificado is not null)
        {
            h.ClientCertificateOptions = ClientCertificateOption.Manual;
            h.ClientCertificates.Add(certificado);
        }
        return h;
    }

    private async Task<string> PostAsync(string url, X509Certificate2? certificado, HttpContent contenido, CancellationToken cancelacion)
    {
        using var handler = Manejador(certificado);
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Espera };
        using var r = await http.PostAsync(url, contenido, cancelacion);
        var bytes = await r.Content.ReadAsByteArrayAsync(cancelacion);
        var texto = Texto(bytes);
        if (!r.IsSuccessStatusCode && !texto.TrimStart().StartsWith('{') && !texto.TrimStart().StartsWith('<'))
            throw new HttpRequestException($"La AEAT ha respondido HTTP {(int)r.StatusCode} {r.ReasonPhrase}.");
        return texto;
    }

    /// <summary>La AEAT responde a veces en UTF-8 y a veces en ISO-8859-1: si no es UTF-8 válido, Latin-1.</summary>
    internal static string Texto(byte[] bytes)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>Sin saltos de línea ni tabuladores (la especificación los prohíbe en el fichero).</summary>
    internal static string Limpiar(string fichero) => fichero.Replace("\r", "").Replace("\n", "").Replace("\t", "");

    // ---------- respuestas ----------

    internal static ValidacionAeat LeerValidacion(string json)
    {
        using var doc = Parsear(json, out var error);
        if (doc is null) return new ValidacionAeat(null, [], [error!]);
        var r = Propiedad(doc.RootElement, "respuesta") ?? doc.RootElement;
        var errores = Lista(r, "errores");
        var avisos = Lista(r, "avisos");
        byte[]? pdf = null;
        if (Propiedad(r, "pdf") is { } p)
        {
            var b64 = p.ValueKind == JsonValueKind.Array ? string.Concat(p.EnumerateArray().Select(x => x.GetString())) : p.GetString();
            if (!string.IsNullOrWhiteSpace(b64))
            {
                try { pdf = Convert.FromBase64String(b64); }
                catch (FormatException) { errores = [.. errores, "La AEAT ha devuelto un PDF que no se puede leer."]; }
            }
        }
        if (pdf is null && errores.Count == 0) errores = ["La AEAT no ha devuelto ni el PDF ni errores."];
        return new ValidacionAeat(errores.Count == 0 ? pdf : null, avisos, errores);
    }

    internal static RespuestaPresentacionAeat LeerPresentacion(string json)
    {
        using var doc = Parsear(json, out var error);
        if (doc is null) return new RespuestaPresentacionAeat(null, null, null, null, null, null, null, null, null, null, null, [], [], [error!], json);
        var r = Propiedad(doc.RootElement, "respuesta") ?? doc.RootElement;
        var errores = Lista(r, "errores");
        var c = Propiedad(r, "correcta");
        if (c is not { } ok)
            return new RespuestaPresentacionAeat(null, null, null, null, null, null, null, null, null, null, null, [], [],
                errores.Count > 0 ? errores : ["La AEAT no ha devuelto ni la presentación correcta ni errores."], json);
        string? T(string nombre) => Propiedad(ok, nombre) is { } v ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())?.Trim() : null;
        decimal? importe = T("ImporteAIngresar") is { Length: > 0 } i && decimal.TryParse(i.Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        return new RespuestaPresentacionAeat(T("CodigoSeguroVerificacion"), T("Fecha"), T("Hora"), T("Expediente"), T("Justificante"), T("FormaPago"), importe, T("urlPdf"),
            T("NIFDeclarante"), T("NIFPresentador"), T("ApellidosNombrePresentador"), Lista(ok, "avisos"), Lista(ok, "advertencias"), errores, json);
    }

    internal static ConsultaAeat LeerConsulta(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return new ConsultaAeat([], "La AEAT ha respondido a la consulta algo que no es XML."); }
        string? V(XElement e, string local) => e.Elements().FirstOrDefault(x => x.Name.LocalName == local)?.Value.Trim();
        var error = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "error");
        if (error is not null)
        {
            var texto = V(error, "descripcionError") ?? error.Value.Trim();
            // Sin declaraciones presentadas no es un fallo de la consulta.
            var t = texto.ToUpperInvariant();
            return t.Contains("NO HA PRESENTADO") || t.Contains("NO EXISTE") || t.Contains("NO SE HAN ENCONTRADO") || t.Contains("NO HAY") || t.Contains("NO SE ENCUENTRA")
                ? new ConsultaAeat([], null)
                : new ConsultaAeat([], texto);
        }
        var lista = doc.Descendants().Where(e => e.Name.LocalName == "respuestaCorrecta").Select(e => new DeclaracionPresentadaAeat(
            V(e, "ejercicio") ?? "", V(e, "modelo") ?? "", V(e, "periodo") ?? "", V(e, "nif") ?? "", V(e, "csv") ?? "", V(e, "expediente") ?? "", V(e, "justificante") ?? "",
            V(e, "just_anterior") is { Length: > 0 } a ? a : null,
            DateTime.TryParse(V(e, "fechaYHoraPresentacion"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null)).ToList();
        return new ConsultaAeat(lista, null);
    }

    private static JsonDocument? Parsear(string json, out string? error)
    {
        error = null;
        try { return JsonDocument.Parse(json); }
        catch (JsonException)
        {
            error = $"La AEAT ha respondido algo que no es JSON: {Recortar(json, 200)}";
            return null;
        }
    }

    /// <summary>Propiedad por nombre sin distinguir mayúsculas ni espacios sobrantes (la AEAT escribe «RESPUESTA» o «respuesta», y «CodigoSeguroVerificacion »).</summary>
    private static JsonElement? Propiedad(JsonElement e, string nombre)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name.Trim(), nombre, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static IReadOnlyList<string> Lista(JsonElement e, string nombre) => Propiedad(e, nombre) switch
    {
        { ValueKind: JsonValueKind.Array } a => [.. a.EnumerateArray().Select(x => (x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()) ?? "").Where(x => x.Length > 0)],
        { ValueKind: JsonValueKind.String } s when s.GetString() is { Length: > 0 } t => [t.Trim()],
        _ => [],
    };

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
