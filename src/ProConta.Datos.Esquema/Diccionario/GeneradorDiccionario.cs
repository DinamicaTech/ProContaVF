using System.Text;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Diccionario;

/// <summary>
/// Genera el diccionario de datos publicado: un md por área con sus tablas, campos, tipos,
/// longitudes, índices, relaciones y dueño funcional, leído de la BD. Nunca se edita a mano.
/// </summary>
public static class GeneradorDiccionario
{
    public const string Cabecera = "<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->";

    /// <summary>Escribe los md en <paramref name="carpeta"/> y devuelve las rutas escritas.</summary>
    public static async Task<IReadOnlyList<string>> GenerarAsync(IBaseDatos bd, string carpeta, string? huellaOrigen = null)
    {
        var esquema = await bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        var diagnostico = await DiagnosticadorEsquema.DiagnosticarAsync(bd);
        Directory.CreateDirectory(carpeta);

        var porArea = esquema.Tablas
            .GroupBy(t => DuenosTablas.De(t.Nombre)?.Area ?? "sin-dueno")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var escritos = new List<string>();
        var indice = new StringBuilder();
        indice.AppendLine(Cabecera).AppendLine();
        indice.AppendLine("# Diccionario de datos").AppendLine();
        indice.AppendLine($"Esquema versión {diagnostico.VersionBd} ({diagnostico.Estado}); {esquema.Tablas.Count} tablas." +
            (huellaOrigen is null ? "" : $" SHA-256 de la BD de origen: `{huellaOrigen}`."));
        indice.AppendLine().AppendLine("| Área | Tablas |").AppendLine("|---|---|");

        foreach (var area in porArea)
        {
            var md = new StringBuilder();
            md.AppendLine(Cabecera).AppendLine();
            md.AppendLine($"# Diccionario de datos · {area.Key}").AppendLine();
            if (area.Key == "sin-dueno")
                md.AppendLine("Tablas sin dueño funcional asignado: hay que asignarlas en `DuenosTablas` antes de usarlas.").AppendLine();

            foreach (var tabla in area.OrderBy(t => t.Nombre, StringComparer.OrdinalIgnoreCase))
                EscribirTabla(md, tabla);

            var ruta = Path.Combine(carpeta, area.Key + ".md");
            await File.WriteAllTextAsync(ruta, md.ToString(), new UTF8Encoding(false));
            escritos.Add(ruta);
            indice.AppendLine($"| [{area.Key}]({area.Key}.md) | {string.Join(", ", area.Select(t => t.Nombre).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))} |");
        }

        var rutaIndice = Path.Combine(carpeta, "README.md");
        await File.WriteAllTextAsync(rutaIndice, indice.ToString(), new UTF8Encoding(false));
        escritos.Add(rutaIndice);
        return escritos;
    }

    private static void EscribirTabla(StringBuilder md, TablaFisica tabla)
    {
        var dueno = DuenosTablas.De(tabla.Nombre);
        md.AppendLine($"## {tabla.Nombre}").AppendLine();
        md.AppendLine($"- Dueño funcional: {(dueno is null ? "sin asignar" : $"`{dueno.Nodo}`")}");
        if (tabla.EsVinculada) md.AppendLine("- Tabla **vinculada** a otro fichero: su estructura no se lee desde la maestra.");
        if (!string.IsNullOrWhiteSpace(tabla.Descripcion)) md.AppendLine($"- {Limpiar(tabla.Descripcion)}");
        md.AppendLine();
        if (tabla.Columnas.Count > 0)
        {
            md.AppendLine("| Campo | Tipo | Nulo | Por defecto | Descripción |").AppendLine("|---|---|---|---|---|");
            foreach (var c in tabla.Columnas)
                md.AppendLine($"| {c.Nombre} | {c.Tipo} | {(c.AdmiteNulos ? "sí" : "no")} | {Limpiar(c.PorDefecto)} | {Limpiar(c.Descripcion)} |");
            md.AppendLine();
        }
        if (tabla.Indices.Count > 0)
        {
            md.AppendLine("Índices:");
            foreach (var i in tabla.Indices)
                md.AppendLine($"- {i.Nombre}{(i.EsClavePrimaria ? " (clave primaria)" : i.EsUnico ? " (único)" : "")}: {string.Join(", ", i.Columnas)}");
            md.AppendLine();
        }
        if (tabla.Relaciones.Count > 0)
        {
            md.AppendLine("Relaciones:");
            foreach (var r in tabla.Relaciones)
                md.AppendLine($"- {r.Nombre}: ({string.Join(", ", r.Columnas)}) → {r.TablaReferenciada} ({string.Join(", ", r.ColumnasReferenciadas)}); borrado {r.ReglaBorrado}, actualización {r.ReglaActualizacion}");
            md.AppendLine();
        }
    }

    private static string Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? "" : texto.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
}
