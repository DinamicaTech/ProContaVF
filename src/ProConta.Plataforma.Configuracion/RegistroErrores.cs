using System.Globalization;
using System.Text;

namespace ProConta.Plataforma.Configuracion;

/// <summary>
/// Lo que se sabe de dónde ocurrió un error inesperado: programa, versión, entorno, BD, operador, sociedad y pantalla
/// (docs/plataforma/registro-errores.md). Los campos que no se conocen se quedan en null y no se escriben.
/// </summary>
public sealed record ContextoError(
    string Programa,
    string Version,
    string? Entorno = null,
    string? BaseDatos = null,
    string? Operador = null,
    string? Sociedad = null,
    string? Pantalla = null);

/// <summary>
/// Fichero de registro de los errores inesperados de ProContaVF y de su conector (docs/plataforma/registro-errores.md):
/// uno por mes en <c>%LocalAppData%\ProContaVF\errores\errores-AAAA-MM.log</c>, compartido por los dos programas, y se borran
/// solos los de más de 12 meses. Registrar nunca lanza: si no puede escribir devuelve null y el aviso al operador lo dice.
/// </summary>
public sealed class RegistroErrores
{
    /// <summary>Meses que se conservan los ficheros, contando el actual.</summary>
    public const int MesesConservados = 12;

    private const string Prefijo = "errores-";
    private const string Extension = ".log";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly Func<DateTime> _ahora;
    private readonly object _cerrojo = new();

    /// <param name="carpeta">Carpeta de los ficheros; por defecto, la de errores del perfil de Windows.</param>
    /// <param name="contexto">Contexto del momento del error (se pide al registrar, para que la pantalla sea la de ese momento).</param>
    /// <param name="ahora">Reloj (las pruebas lo fijan).</param>
    public RegistroErrores(Func<ContextoError> contexto, string? carpeta = null, Func<DateTime>? ahora = null)
    {
        Contexto = contexto;
        Carpeta = carpeta ?? CarpetaPorDefecto;
        _ahora = ahora ?? (() => DateTime.Now);
    }

    /// <summary><c>%LocalAppData%\ProContaVF\errores</c>, junto a la configuración local del puesto.</summary>
    public static string CarpetaPorDefecto { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProContaVF", "errores");

    public string Carpeta { get; }

    public Func<ContextoError> Contexto { get; set; }

    /// <summary>Fichero del mes de esa fecha.</summary>
    public string Ruta(DateTime fecha) => Path.Combine(Carpeta, $"{Prefijo}{fecha:yyyy-MM}{Extension}");

    /// <summary>Fichero del mes en curso.</summary>
    public string RutaActual => Ruta(_ahora());

    /// <summary>
    /// Añade el error al fichero del mes y devuelve su ruta, o null si no se ha podido escribir (disco lleno, sin permiso…).
    /// <paramref name="donde"/> dice en qué parte del programa se capturó («Pantalla», «Tarea en segundo plano», «Herramienta ver_asiento»…).
    /// </summary>
    public string? Registrar(Exception error, string donde) => Registrar(error, donde, null);

    /// <summary>Como <see cref="Registrar(Exception, string)"/>, cambiando el contexto general por uno propio de esta llamada (p. ej. la sociedad que pidió la herramienta).</summary>
    public string? Registrar(Exception error, string donde, Func<ContextoError, ContextoError>? ajustar)
    {
        ContextoError contexto;
        try { contexto = Contexto(); }
        catch (Exception ex) { contexto = new ContextoError("ProContaVF", "?", Pantalla: $"(no se pudo leer el contexto: {ex.GetType().Name})"); }
        if (ajustar is not null) contexto = ajustar(contexto);
        var ahora = _ahora();
        var texto = Formatear(ahora, contexto, donde, error);
        var ruta = Ruta(ahora);
        lock (_cerrojo)
        {
            // ProContaVF y el conector escriben en el mismo fichero: si el otro lo tiene abierto en ese instante, se reintenta.
            for (var intento = 0; intento < 5; intento++)
            {
                try
                {
                    Directory.CreateDirectory(Carpeta);
                    using var f = new FileStream(ruta, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(texto);
                    f.Write(bytes, 0, bytes.Length);
                    return ruta;
                }
                catch (IOException) when (intento < 4) { Thread.Sleep(40 * (intento + 1)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
                {
                    return null;
                }
            }
            return null;
        }
    }

    /// <summary>Borra los ficheros de más de <see cref="MesesConservados"/> meses. Devuelve cuántos ha borrado; nunca lanza.</summary>
    public int Purgar()
    {
        var limite = new DateTime(_ahora().Year, _ahora().Month, 1).AddMonths(-(MesesConservados - 1));
        var borrados = 0;
        try
        {
            if (!Directory.Exists(Carpeta)) return 0;
            foreach (var f in Directory.EnumerateFiles(Carpeta, Prefijo + "*" + Extension))
            {
                var nombre = Path.GetFileNameWithoutExtension(f)[Prefijo.Length..];
                if (!DateTime.TryParseExact(nombre, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mes) || mes >= limite) continue;
                try { File.Delete(f); borrados++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return borrados;
    }

    /// <summary>Una entrada del registro: cabecera con la fecha, los datos de contexto conocidos y el detalle técnico completo.</summary>
    public static string Formatear(DateTime fecha, ContextoError c, string donde, Exception error)
    {
        var sb = new StringBuilder();
        sb.Append("==== ").Append(fecha.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" · ").Append(c.Programa).AppendLine(" · error inesperado");
        void Linea(string etiqueta, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor)) sb.Append(etiqueta).Append(": ").AppendLine(valor);
        }
        Linea("Versión", c.Version);
        Linea("Entorno", c.Entorno);
        Linea("BD", c.BaseDatos);
        Linea("Operador", c.Operador);
        Linea("Sociedad", c.Sociedad);
        Linea("Pantalla", c.Pantalla);
        Linea("Dónde", donde);
        Linea("Equipo", $"{Environment.MachineName} · usuario de Windows {Environment.UserName} · {Environment.OSVersion.VersionString}");
        Linea("Error", Resumen(error));
        sb.AppendLine("Detalle técnico:");
        sb.AppendLine(error.ToString());
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Tipo y mensaje del error más interno, que suele ser la causa («InvalidCastException: …»).</summary>
    public static string Resumen(Exception error)
    {
        var e = error;
        while (e is AggregateException { InnerExceptions.Count: 1 } a) e = a.InnerExceptions[0];
        while (e.InnerException is not null && e is System.Reflection.TargetInvocationException or TypeInitializationException) e = e.InnerException;
        return $"{e.GetType().Name}: {e.Message}";
    }

    /// <summary>Fecha corta para los avisos al operador.</summary>
    public static string FechaCorta(DateTime fecha) => fecha.ToString("dd/MM/yyyy HH:mm", Es);
}
