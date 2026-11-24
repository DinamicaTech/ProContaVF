namespace ProConta.Nucleo.Ficheros;

/// <summary>
/// Protección de los ficheros de producción desde la copia de pruebas (plataforma/entorno-pruebas 2026-11-24). En pruebas, las carpetas de
/// solo lectura de fuera (el NAS con las facturas de proveedores, nóminas y extractos) se leen tal cual, pero nada se escribe, mueve ni
/// sobrescribe fuera de la carpeta de pruebas: lo que se escribiría allí va a <c>&lt;carpeta de pruebas&gt;\externas\&lt;ruta sin unidad&gt;</c>
/// (por ejemplo <c>D:\SynologyBox\2026\Proveedores\x.pdf</c> → <c>datospruebas\externas\D\SynologyBox\2026\Proveedores\x.pdf</c>).
/// Lo configura el programa al entrar (<see cref="Configurar"/>); sin configurar (producción, pruebas automáticas, herramientas) no cambia nada.
/// </summary>
public static class EntornoFicheros
{
    public const string CarpetaExternas = "externas";

    private static string? _carpetaPruebas;

    /// <summary>Carpeta de pruebas activa, o null en producción.</summary>
    public static string? CarpetaPruebas => _carpetaPruebas;

    /// <summary>En pruebas, la carpeta de la BD de pruebas; en producción, null.</summary>
    public static void Configurar(string? carpetaPruebas) =>
        _carpetaPruebas = string.IsNullOrWhiteSpace(carpetaPruebas) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaPruebas));

    /// <summary>La ruta está fuera de la carpeta de pruebas y estamos en pruebas: no se puede escribir, mover ni borrar.</summary>
    public static bool Protegida(string ruta) => Desviar(_carpetaPruebas, ruta) is not null;

    /// <summary>Dónde escribir <paramref name="ruta"/>: ella misma o, si está protegida, su sitio dentro de <c>externas</c>.</summary>
    public static string ParaEscribir(string ruta) => Desviar(_carpetaPruebas, ruta) ?? ruta;

    /// <summary>
    /// Para cambiar un fichero que puede estar protegido: si lo está, lo copia (una vez) a su sitio en <c>externas</c> y devuelve esa copia,
    /// que es la que se modifica y la ruta que hay que grabar; si no, devuelve la misma ruta.
    /// </summary>
    public static string CopiaEditable(string ruta)
    {
        if (Desviar(_carpetaPruebas, ruta) is not string copia) return ruta;
        if (!File.Exists(copia))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
            File.Copy(ruta, copia);
            File.SetAttributes(copia, FileAttributes.Normal);
        }
        return copia;
    }

    /// <summary>Mueve un fichero; si el origen está protegido, lo copia y deja el original donde estaba.</summary>
    public static void Mover(string origen, string destino)
    {
        destino = ParaEscribir(destino);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        if (Protegida(origen))
        {
            File.Copy(origen, destino);
            File.SetAttributes(destino, FileAttributes.Normal);
        }
        else File.Move(origen, destino);
    }

    /// <summary>La regla, sin estado global: null si <paramref name="ruta"/> se puede tocar, o la ruta desviada a <c>externas</c>.</summary>
    public static string? Desviar(string? carpetaPruebas, string ruta)
    {
        if (carpetaPruebas is null || string.IsNullOrWhiteSpace(ruta)) return null;
        var completa = Path.GetFullPath(ruta);
        var base_ = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaPruebas));
        if (completa.Equals(base_, StringComparison.OrdinalIgnoreCase) ||
            completa.StartsWith(base_ + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return Path.Combine(base_, CarpetaExternas, SinRaiz(completa));
    }

    /// <summary>La ruta sin su raíz, para colgarla de <c>externas</c>: <c>D:\a\b</c> → <c>D\a\b</c>; <c>\\nas\c\a</c> → <c>UNC\nas\c\a</c>.</summary>
    internal static string SinRaiz(string completa)
    {
        var raiz = Path.GetPathRoot(completa) ?? "";
        var resto = completa[raiz.Length..].TrimStart('\\', '/');
        var prefijo = raiz.StartsWith(@"\\", StringComparison.Ordinal)
            ? Path.Combine("UNC", raiz.Trim('\\').Replace('\\', Path.DirectorySeparatorChar))
            : raiz.TrimEnd('\\', '/').Replace(":", "", StringComparison.Ordinal);
        return Path.Combine(prefijo, resto);
    }
}
