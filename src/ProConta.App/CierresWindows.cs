using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;

namespace ProConta.App;

/// <summary>Un cierre brusco de ProContaVF que Windows anotó en su visor de eventos.</summary>
/// <param name="Codigo">Código de la excepción nativa (c0000005 = violación de acceso).</param>
/// <param name="Modulo">Módulo en el que falló (coreclr.dll, ACEOLEDB.DLL…).</param>
/// <param name="Detalle">Texto de los eventos de Windows: el de .NET (con la pila) y el de «Error de aplicación».</param>
internal sealed record CierreBrusco(DateTime Fecha, string Codigo, string Modulo, string Detalle)
{
    public string Resumen => $"Cierre brusco: {(Codigo.Contains("c0000005", StringComparison.OrdinalIgnoreCase) ? "violación de acceso" : "excepción nativa")} ({Codigo}) en {Modulo}";
}

/// <summary>
/// Cierres de ProContaVF que el programa no pudo anotar (plataforma/registro-errores 2026-11-28): un fallo dentro de un
/// componente nativo, como el motor de Access, cierra el proceso sin pasar por .NET y solo queda en el visor de eventos de
/// Windows. Al arrancar se buscan ahí los cierres de este mismo ejecutable desde la última revisión, para copiarlos al registro.
/// </summary>
internal static class CierresWindows
{
    /// <summary>Fichero, junto al registro de errores, con la fecha de la última revisión del visor de eventos.</summary>
    public const string FicheroMarca = "cierres-revisados.txt";

    /// <summary>La primera vez (sin marca) se miran los últimos días.</summary>
    public const int DiasPrimeraRevision = 7;

    /// <summary>Código de las excepciones de .NET: esas ya las guarda el propio programa, no se repiten.</summary>
    private const string CodigoExcepcionNet = "e0434352";

    /// <summary>
    /// Los cierres de <paramref name="rutaExe"/> anotados por Windows desde la última revisión, del más antiguo al más reciente,
    /// y deja la marca en <paramref name="ahora"/>. Si el visor de eventos no se puede leer, no devuelve nada (y no mueve la marca).
    /// </summary>
    public static IReadOnlyList<CierreBrusco> Buscar(string carpetaRegistro, string rutaExe, DateTime ahora)
    {
        var marca = Path.Combine(carpetaRegistro, FicheroMarca);
        var desde = LeerMarca(marca) ?? ahora.AddDays(-DiasPrimeraRevision);
        List<CierreBrusco> cierres;
        try
        {
            cierres = Leer(rutaExe, desde, ahora);
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }
        try
        {
            Directory.CreateDirectory(carpetaRegistro);
            File.WriteAllText(marca, ahora.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return cierres;
    }

    private static DateTime? LeerMarca(string ruta)
    {
        try
        {
            return File.Exists(ruta) && DateTime.TryParse(File.ReadAllText(ruta).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var f) ? f : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static List<CierreBrusco> Leer(string rutaExe, DateTime desde, DateTime hasta)
    {
        var errores = Eventos("Application Error", 1000, desde, hasta);
        var cierres = new List<CierreBrusco>();
        foreach (var e in errores)
        {
            // Propiedades de «Error de aplicación» (1000): 3 módulo, 6 código de excepción, 10 ruta del ejecutable.
            var p = e.Properties;
            if (p.Count < 11 || !string.Equals(p[10].Value?.ToString(), rutaExe, StringComparison.OrdinalIgnoreCase)) continue;
            var codigo = p[6].Value?.ToString() ?? "?";
            if (codigo.Contains(CodigoExcepcionNet, StringComparison.OrdinalIgnoreCase)) continue;
            var fecha = e.TimeCreated ?? hasta;
            // El evento de .NET (1026) con la pila se anota justo antes, en el mismo segundo o pocos segundos antes.
            var net = Eventos(".NET Runtime", 1026, fecha.AddSeconds(-30), fecha.AddSeconds(5))
                .Where(n => Texto(n).Contains(Path.GetFileName(rutaExe), StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => Math.Abs(((n.TimeCreated ?? fecha) - fecha).TotalSeconds))
                .FirstOrDefault();
            var detalle = (net is null ? "" : Texto(net) + Environment.NewLine + Environment.NewLine) + Texto(e);
            cierres.Add(new CierreBrusco(fecha, codigo, p[3].Value?.ToString() ?? "?", detalle));
        }
        return cierres.OrderBy(c => c.Fecha).ToList();
    }

    private static List<EventRecord> Eventos(string proveedor, int id, DateTime desde, DateTime hasta)
    {
        static string Utc(DateTime f) => f.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var xpath = $"*[System[Provider[@Name='{proveedor}'] and (EventID={id}) and TimeCreated[@SystemTime>'{Utc(desde)}' and @SystemTime<='{Utc(hasta)}']]]";
        var lista = new List<EventRecord>();
        using var lector = new EventLogReader(new EventLogQuery("Application", PathType.LogName, xpath));
        for (var e = lector.ReadEvent(); e is not null; e = lector.ReadEvent()) lista.Add(e);
        return lista;
    }

    /// <summary>El texto del evento como lo enseña el visor; si Windows no lo puede componer, sus datos tal cual.</summary>
    private static string Texto(EventRecord e)
    {
        string? texto = null;
        try { texto = e.FormatDescription(); }
        catch (EventLogException) { }
        return string.IsNullOrWhiteSpace(texto) ? string.Join(Environment.NewLine, e.Properties.Select(p => p.Value?.ToString())) : texto.Trim();
    }
}
