using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Una línea del catálogo de descripciones: la descripción, su área y si está pendiente de confirmar por el propietario.</summary>
public sealed record DescripcionCatalogo(string Area, string Tabla, string? Columna, string Texto, bool PorConfirmar)
{
    public DescripcionEsquema ComoEsquema => new(Tabla, Columna, Texto);
}

/// <summary>
/// Descripciones de las tablas y campos que no las traen de su migración, sobre todo los heredados de la maestra y del ERP
/// (datos/esquema/descripciones-campos). Están en <c>Descripciones/&lt;área&gt;.txt</c>, incrustados en el ensamblado, con una
/// línea por descripción: «Tabla: texto» para la tabla y «Tabla.Campo: texto» para el campo; las que empiezan por «?» están
/// pendientes de confirmar y se escriben igual, sin el «?». El ActualizadorEsquema las escribe después de cada actualización y
/// con el comando «descripciones».
/// </summary>
public static class CatalogoDescripciones
{
    private const string PrefijoRecurso = "ProConta.Datos.Esquema.Descripciones.";

    private static readonly Lazy<IReadOnlyList<DescripcionCatalogo>> _todas = new(Cargar);

    public static IReadOnlyList<DescripcionCatalogo> Todas => _todas.Value;

    /// <summary>Huella de todo el catálogo: cambia con cualquier descripción (las pruebas rehacen su plantilla con ella).</summary>
    public static string Huella => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Todas.Select(d => $"{d.Tabla}.{d.Columna}={d.Texto}")))));

    /// <summary>Lee las líneas de un fichero del catálogo; lanza si alguna no tiene el formato.</summary>
    public static IReadOnlyList<DescripcionCatalogo> Leer(string area, TextReader lector)
    {
        var lista = new List<DescripcionCatalogo>();
        var numero = 0;
        while (lector.ReadLine() is { } linea)
        {
            numero++;
            if (string.IsNullOrWhiteSpace(linea) || linea.TrimStart().StartsWith('#')) continue;
            var porConfirmar = linea.StartsWith('?');
            var cuerpo = porConfirmar ? linea[1..] : linea;
            var dosPuntos = cuerpo.IndexOf(": ", StringComparison.Ordinal);
            if (dosPuntos <= 0)
                throw new FormatException($"Descripciones/{area}.txt, línea {numero}: falta «Tabla: texto» o «Tabla.Campo: texto».");
            var clave = cuerpo[..dosPuntos];
            var texto = cuerpo[(dosPuntos + 2)..].Trim();
            var punto = clave.IndexOf('.');
            var tabla = punto < 0 ? clave : clave[..punto];
            var columna = punto < 0 ? null : clave[(punto + 1)..];
            if (tabla.Length == 0 || columna is { Length: 0 } || clave.Contains(' ') || texto.Length == 0)
                throw new FormatException($"Descripciones/{area}.txt, línea {numero}: «{clave}» no es «Tabla» ni «Tabla.Campo», o falta el texto.");
            lista.Add(new DescripcionCatalogo(area, tabla, columna, texto, porConfirmar));
        }
        return lista;
    }

    private static IReadOnlyList<DescripcionCatalogo> Cargar()
    {
        var ensamblado = typeof(CatalogoDescripciones).Assembly;
        var lista = new List<DescripcionCatalogo>();
        foreach (var recurso in ensamblado.GetManifestResourceNames().Where(r => r.StartsWith(PrefijoRecurso, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var area = recurso[PrefijoRecurso.Length..^".txt".Length];
            using var flujo = ensamblado.GetManifestResourceStream(recurso)!;
            using var lector = new StreamReader(flujo, Encoding.UTF8);
            lista.AddRange(Leer(area, lector));
        }
        return lista;
    }
}
