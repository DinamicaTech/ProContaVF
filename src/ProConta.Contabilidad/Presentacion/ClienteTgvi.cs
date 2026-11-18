using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>Direcciones de TGVI Online (sources/aeat/Especificaciones_TGVI_Online.pdf, versión 2.8): las mismas en pruebas cambiando el dominio por prewww1/prewww2.</summary>
public static class ServiciosTgvi
{
    private const string Produccion = "https://www1.agenciatributaria.gob.es/wlpl/OVPT-NTGV/";
    private const string Pruebas = "https://prewww1.aeat.es/wlpl/OVPT-NTGV/";

    public static string Operacion(EntornoAeat entorno, string operacion) => (entorno == EntornoAeat.Pruebas ? Pruebas : Produccion) + operacion;

    /// <summary>PDF justificante de una presentación por su CSV.</summary>
    public static string Justificante(EntornoAeat entorno, string csv) =>
        (entorno == EntornoAeat.Pruebas ? "https://prewww2.aeat.es" : "https://www2.agenciatributaria.gob.es") +
        "/wlpl/inwinvoc/es.aeat.dit.adu.eeca.catalogo.VisualizaSc?COMPLETA=SI&ORIGEN=C&CSV=" + Uri.EscapeDataString(csv);

    /// <summary>Registros tipo 2 por bloque (la AEAT no admite más).</summary>
    public const int RegistrosPorBloque = 40_000;
}

/// <summary>
/// Respuesta de una operación de TGVI Online: los parámetros llegan en cabeceras en minúsculas (codigo 0 = correcta) y, en las que
/// devuelven un fichero (errores, avisos, erróneos, vista previa), en el cuerpo.
/// </summary>
public sealed record RespuestaTgvi(int Codigo, string? Mensaje, IReadOnlyDictionary<string, string> Cabeceras, byte[] Cuerpo)
{
    public bool Correcta => Codigo == 0;
    public string? Cabecera(string nombre) => Cabeceras.TryGetValue(nombre, out var v) ? v : null;
    public string? IdEnvio => Cabecera("idenvio");
    public string? Estado => Cabecera("estado");
    public int? Entero(string nombre) => int.TryParse(Cabecera(nombre), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    public string? Csv => Cabecera("csv");
    public string? Expediente => Cabecera("expediente");
    public string Texto => Cuerpo.Length == 0 ? "" : ClienteAeatHttp.Texto(Cuerpo);
    public string Json => JsonSerializer.Serialize(new { codigo = Codigo, mensaje = Mensaje, cabeceras = Cabeceras });
}

/// <summary>Operaciones de TGVI Online, con el mismo certificado y las cookies de sesión en todo el envío.</summary>
public interface IClienteTgvi
{
    Task<RespuestaTgvi> InicializarAsync(EntornoAeat entorno, X509Certificate2 certificado, string modelo, string ejercicio, string periodo, string ndc, int numBloques,
        string registroTipo1, CancellationToken cancelacion = default);
    Task<RespuestaTgvi> EnviarDatosAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, int numBloque, string bloque, CancellationToken cancelacion = default);
    Task<RespuestaTgvi> VistaPreviaAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, CancellationToken cancelacion = default);
    Task<RespuestaTgvi> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, string firNif, string firNombre, CancellationToken cancelacion = default);
    /// <summary>RecuperarErrores, RecuperarAvisos o RecuperarErroneos.</summary>
    Task<RespuestaTgvi> RecuperarAsync(EntornoAeat entorno, X509Certificate2 certificado, string operacion, string idEnvio, CancellationToken cancelacion = default);
    Task<RespuestaTgvi> BajaAsync(EntornoAeat entorno, X509Certificate2 certificado, string modelo, string ejercicio, string periodo, string ndc, string expediente, string firNif,
        string firNombre, CancellationToken cancelacion = default);
    Task<byte[]> DescargarAsync(string url, X509Certificate2? certificado, CancellationToken cancelacion = default);
}

/// <summary>
/// Cliente HTTP de TGVI Online: POST de texto plano en ISO-8859-1, parámetros en cabeceras en minúsculas, mismo certificado y mismas
/// cookies en todas las operaciones del envío (un contenedor de cookies por cliente). Comprueba antes de cada envío que la dirección es
/// del entorno pedido.
/// </summary>
public sealed class ClienteTgviHttp(Func<X509Certificate2?, CookieContainer, HttpMessageHandler>? manejador = null) : IClienteTgvi
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(120);
    private const string Codificacion = "ISO-8859-1";
    private readonly CookieContainer _cookies = new();

    public Task<RespuestaTgvi> InicializarAsync(EntornoAeat entorno, X509Certificate2 certificado, string modelo, string ejercicio, string periodo, string ndc, int numBloques,
        string registroTipo1, CancellationToken cancelacion = default) =>
        PostAsync(entorno, "InicializarEnvio", certificado, new()
        {
            ["modelo"] = modelo, ["ejercicio"] = ejercicio, ["periodo"] = periodo, ["ndc"] = ndc, ["idioma"] = "ES",
            ["numbloques"] = numBloques.ToString(CultureInfo.InvariantCulture), ["codificacion"] = Codificacion,
        }, registroTipo1, cancelacion);

    public Task<RespuestaTgvi> EnviarDatosAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, int numBloque, string bloque, CancellationToken cancelacion = default) =>
        PostAsync(entorno, "EnviarDatos", certificado, new()
        {
            ["idenvio"] = idEnvio, ["numbloque"] = numBloque.ToString(CultureInfo.InvariantCulture), ["codificacion"] = Codificacion,
        }, bloque, cancelacion);

    public Task<RespuestaTgvi> VistaPreviaAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, CancellationToken cancelacion = default) =>
        PostAsync(entorno, "VistaPrevia", certificado, new() { ["idenvio"] = idEnvio }, "", cancelacion);

    public Task<RespuestaTgvi> PresentarAsync(EntornoAeat entorno, X509Certificate2 certificado, string idEnvio, string firNif, string firNombre, CancellationToken cancelacion = default) =>
        PostAsync(entorno, "PresentarEnvio", certificado, new() { ["idenvio"] = idEnvio, ["firnif"] = firNif, ["firnombre"] = firNombre, ["fir"] = "FirmaBasica" }, "", cancelacion);

    public Task<RespuestaTgvi> RecuperarAsync(EntornoAeat entorno, X509Certificate2 certificado, string operacion, string idEnvio, CancellationToken cancelacion = default) =>
        operacion is "RecuperarErrores" or "RecuperarAvisos" or "RecuperarErroneos"
            ? PostAsync(entorno, operacion, certificado, new() { ["idenvio"] = idEnvio, ["codificacion"] = Codificacion }, "", cancelacion)
            : throw new ArgumentException($"La operación {operacion} no es de recuperación.", nameof(operacion));

    public Task<RespuestaTgvi> BajaAsync(EntornoAeat entorno, X509Certificate2 certificado, string modelo, string ejercicio, string periodo, string ndc, string expediente, string firNif,
        string firNombre, CancellationToken cancelacion = default) =>
        PostAsync(entorno, "BajaDeclaracion", certificado, new()
        {
            ["modelo"] = modelo, ["ejercicio"] = ejercicio, ["periodo"] = periodo, ["ndc"] = ndc, ["expediente"] = expediente,
            ["firnif"] = firNif, ["firnombre"] = firNombre, ["fir"] = "FirmaBasica",
        }, "", cancelacion);

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
        if (manejador is not null) return manejador(certificado, _cookies);
        var h = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        if (certificado is not null)
        {
            h.ClientCertificateOptions = ClientCertificateOption.Manual;
            h.ClientCertificates.Add(certificado);
        }
        return h;
    }

    private async Task<RespuestaTgvi> PostAsync(EntornoAeat entorno, string operacion, X509Certificate2 certificado, Dictionary<string, string> cabeceras, string cuerpo,
        CancellationToken cancelacion)
    {
        var url = ServiciosTgvi.Operacion(entorno, operacion);
        if (!ServiciosAeat.EsDelEntorno(url, entorno)) throw new InvalidOperationException($"La dirección {url} no es del entorno de {ServiciosAeat.Nombre(entorno)}.");
        using var handler = Manejador(certificado);
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Espera };
        using var peticion = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(Encoding.Latin1.GetBytes(cuerpo)) };
        peticion.Content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=ISO-8859-1");
        foreach (var (k, v) in cabeceras) peticion.Headers.TryAddWithoutValidation(k, Cabecera(v));
        using var r = await http.SendAsync(peticion, cancelacion);
        var bytes = await r.Content.ReadAsByteArrayAsync(cancelacion);
        var salida = r.Headers.Concat(r.Content.Headers)
            .GroupBy(h => h.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => Decodificar(string.Join(",", g.First().Value)));
        if (!salida.TryGetValue("codigo", out var cod) || !int.TryParse(cod.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var codigo))
        {
            if (!r.IsSuccessStatusCode) throw new HttpRequestException($"TGVI Online ha respondido HTTP {(int)r.StatusCode} {r.ReasonPhrase} a {operacion}.");
            codigo = salida.Count == 0 ? -1 : 0;
        }
        return new RespuestaTgvi(codigo, salida.GetValueOrDefault("mensaje"), salida, bytes);
    }

    /// <summary>Las cabeceras HTTP solo admiten ASCII: los nombres con tildes se envían en ISO-8859-1 tal cual (lo que espera la AEAT).</summary>
    private static string Cabecera(string v) => Encoding.Latin1.GetString(Encoding.Latin1.GetBytes(v));

    /// <summary>Los mensajes de la AEAT llegan en ISO-8859-1; .NET los lee como Latin-1.</summary>
    private static string Decodificar(string v) => v;
}
