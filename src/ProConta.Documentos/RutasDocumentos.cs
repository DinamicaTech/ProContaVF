using System.Security.Cryptography;
using System.Text;

namespace ProConta.Documentos;

/// <summary>
/// Reglas comunes de rutas y nombres del gestor (documentos 2026-11-18): la raíz es la carpeta de documentos de la empresa, los subidos a mano van
/// a «Documentos\…» dentro de ella, los nombres se limpian de lo que Windows no admite y nunca se sobrescribe un fichero (se añade « (2)»).
/// </summary>
public static class RutasDocumentos
{
    /// <summary>Subcarpeta, dentro de la raíz, de los documentos que se suben a mano.</summary>
    public const string CarpetaManuales = "Documentos";

    /// <summary>Subcarpeta de la bandeja «Por clasificar», dentro de <see cref="CarpetaManuales"/>.</summary>
    public const string CarpetaBandeja = "Por clasificar";

    /// <summary>Largo máximo de una ruta completa (el límite clásico de Windows menos un margen).</summary>
    public const int LargoMaximo = 240;

    /// <summary>
    /// Carpeta de documentos de la empresa: Sociedades.RutaDocumentos tal cual si es una ruta completa; si es relativa, dentro de la carpeta que
    /// contiene la BD (como en compras/reconocimiento y en el correo).
    /// </summary>
    public static string Raiz(string rutaDocumentos, string rutaBd)
    {
        var ruta = rutaDocumentos.Trim();
        return Path.IsPathFullyQualified(ruta) ? ruta : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rutaBd)) ?? string.Empty, ruta));
    }

    /// <summary>Ruta guardada en la BD: relativa a la raíz si está dentro de ella; si no, la completa.</summary>
    public static string ParaGuardar(string raiz, string rutaCompleta)
    {
        var r = Path.GetFullPath(raiz).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(rutaCompleta);
        return c.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? c[r.Length..] : c;
    }

    /// <summary>Ruta completa de una ruta guardada.</summary>
    public static string Completa(string raiz, string rutaGuardada) =>
        Path.IsPathFullyQualified(rutaGuardada) ? rutaGuardada : Path.GetFullPath(Path.Combine(raiz, rutaGuardada));

    /// <summary>
    /// Nombre de fichero válido en Windows: quita los caracteres no admitidos y los espacios y puntos del final, evita los nombres reservados
    /// (CON, PRN…) y recorta a <paramref name="largo"/> caracteres sin perder la extensión.
    /// </summary>
    public static string NombreLimpio(string nombre, int largo = 120)
    {
        var ext = Path.GetExtension(nombre ?? "");
        if (ext.Length > 10 || ext.Any(c => Path.GetInvalidFileNameChars().Contains(c))) ext = "";
        var baseNombre = (nombre ?? "")[..((nombre ?? "").Length - ext.Length)];
        var sb = new StringBuilder();
        foreach (var c in baseNombre)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? ' ' : c);
        var limpio = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', ' ');
        if (limpio.Length == 0) limpio = "Documento";
        if (_reservados.Contains(limpio.Split('.')[0].ToUpperInvariant())) limpio = "_" + limpio;
        var max = Math.Max(10, largo - ext.Length);
        if (limpio.Length > max) limpio = limpio[..max].TrimEnd('.', ' ');
        return limpio + ext.ToLowerInvariant();
    }

    private static readonly HashSet<string> _reservados =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>Subcarpeta limpia: cada parte con <see cref="NombreLimpio"/> (sin extensión), sin «..» ni partes vacías.</summary>
    public static string SubcarpetaLimpia(string subcarpeta)
    {
        if (Path.IsPathFullyQualified(subcarpeta)) return subcarpeta;
        var partes = subcarpeta.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != "..")
            .Select(p => NombreLimpio(p + ".x", 60)[..^2]);
        return string.Join(Path.DirectorySeparatorChar, partes);
    }

    /// <summary>
    /// Ruta libre para un fichero nuevo en una carpeta: el nombre tal cual si no existe; si existe, « (2)», « (3)»… Recorta el nombre si la ruta
    /// completa pasaría de <see cref="LargoMaximo"/>. <paramref name="ocupada"/> permite tratar como ocupadas rutas aún no escritas.
    /// </summary>
    public static string RutaLibre(string carpeta, string nombre, Func<string, bool>? ocupada = null)
    {
        ocupada ??= File.Exists;
        var limpio = NombreLimpio(nombre);
        var disponible = LargoMaximo - Path.GetFullPath(carpeta).Length - 1 - 6;
        if (disponible < limpio.Length) limpio = NombreLimpio(limpio, Math.Max(16, disponible));
        var ext = Path.GetExtension(limpio);
        var sinExt = limpio[..^ext.Length];
        var ruta = Path.Combine(carpeta, limpio);
        for (var n = 2; ocupada(ruta); n++) ruta = Path.Combine(carpeta, $"{sinExt} ({n}){ext}");
        return ruta;
    }

    /// <summary>SHA-256 en hexadecimal (mayúsculas), como las huellas del correo y de las nóminas.</summary>
    public static string Huella(byte[] contenido) => Convert.ToHexString(SHA256.HashData(contenido));

    public static async Task<string> HuellaAsync(string ruta, CancellationToken cancelacion = default)
    {
        await using var f = File.OpenRead(ruta);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, cancelacion));
    }

    /// <summary>Subcarpeta, relativa a la raíz, de un documento subido a mano: «Documentos\2026\Modelos» o «Documentos\Empresa».</summary>
    public static string SubcarpetaManual(TipoDocumento tipo, int? ejercicio) =>
        tipo.PorAnio && ejercicio is int a
            ? Path.Combine(CarpetaManuales, a.ToString(System.Globalization.CultureInfo.InvariantCulture), tipo.Carpeta)
            : Path.Combine(CarpetaManuales, tipo.Carpeta);
}
