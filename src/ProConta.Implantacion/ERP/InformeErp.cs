using System.Text;
using ProConta.Implantacion.ContaSol;

namespace ProConta.Implantacion.ERP;

/// <summary>Informe en Markdown de una ejecución: recuentos por tabla, controles, tiempos e incidencias.</summary>
public static class InformeErp
{
    public static string Generar(ResultadoMigracionErp r, OrigenErp o, string destino, bool simulacion)
    {
        var md = new StringBuilder();
        md.Append($"# Migración del ERP ({(simulacion ? "simulación" : "ejecución")} {r.IdMigracion}, {r.Inicio:yyyy-MM-dd HH:mm})\n\n");
        md.Append($"- Estado: **{r.Estado}**\n- Destino: `{destino}`\n- Origen: `{o.Ruta}` (SHA-256 {o.Huella[..16]}…)\n");
        if (o.RutaDirecciones is not null) md.Append($"- Direcciones: `{o.RutaDirecciones}` ({o.Direcciones.Count} filas)\n");
        md.Append($"- Duración: {r.Fin - r.Inicio:hh\\:mm\\:ss}\n");
        var porSeveridad = r.Incidencias.GroupBy(i => i.Severidad).ToDictionary(g => g.Key, g => g.Count());
        md.Append($"- Incidencias: {porSeveridad.GetValueOrDefault(Severidad.Bloqueante)} bloqueantes, {porSeveridad.GetValueOrDefault(Severidad.Advertencia)} advertencias, " +
                  $"{porSeveridad.GetValueOrDefault(Severidad.Informativa)} informativas\n\n");

        md.Append("## Tiempos por fase\n\n| Fase | Duración |\n|---|---|\n");
        foreach (var (fase, d) in r.Tiempos) md.Append($"| {fase} | {d:mm\\:ss\\.f} |\n");

        md.Append("\n## Tablas\n\nPor tabla del ERP: filas leídas, cargadas, excluidas (cada exclusión, con su motivo, está en las incidencias) y claves puestas a nulo.\n\n");
        md.Append("| Fase | Tabla | Leídas | Cargadas | Excluidas | Claves a nulo | Otras incidencias |\n|---|---|---|---|---|---|---|\n");
        foreach (var t in r.Tablas)
        {
            var inc = r.Incidencias.Where(i => i.Fase == t.Fase && i.Tabla == t.Tabla).ToList();
            md.Append($"| {t.Fase} | {t.Tabla} | {t.Leidas} | {t.Cargadas} | {t.Excluidas} | {inc.Count(i => i.Codigo == "Erp.ClaveNula")} | {inc.Count(i => i.Codigo is not "Erp.ClaveNula" and not "Erp.Excluido")} |\n");
        }

        foreach (var c in r.Controles)
        {
            md.Append($"\n## {c.Titulo}\n\n| {string.Join(" | ", c.Cabecera)} |\n|{string.Concat(c.Cabecera.Select(_ => "---|"))}\n");
            foreach (var f in c.Filas) md.Append($"| {string.Join(" | ", f)} |\n");
            if (c.Nota is not null) md.Append($"\n{c.Nota}\n");
        }

        md.Append("\n## Incidencias por código\n\n| Gravedad | Código | Número |\n|---|---|---|\n");
        foreach (var g in r.Incidencias.GroupBy(i => (i.Severidad, i.Codigo)).OrderByDescending(g => g.Key.Severidad).ThenBy(g => g.Key.Codigo))
            md.Append($"| {g.Key.Severidad} | {g.Key.Codigo} | {g.Count()} |\n");

        if (r.CamposConstantes.Count > 0)
        {
            md.Append("\n## Campos con el mismo valor en todos los registros (criterio general 1)\n\nNo se migran salvo los que el mapa nombra.\n\n| Tabla | Campos |\n|---|---|\n");
            foreach (var (t, c) in r.CamposConstantes.OrderBy(x => x.Key)) md.Append($"| {t} | {string.Join(", ", c)} |\n");
        }

        foreach (var n in r.Notas) md.Append($"\n{n}\n");

        md.Append("\n## Detalle de incidencias\n\nTodas las exclusiones y claves a nulo, con su clave original (el valor del ERP está en la última columna).\n\n");
        md.Append("| Gravedad | Fase | Tabla | Clave | Campo | Código | Explicación | Valor original |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var i in r.Incidencias.OrderByDescending(i => i.Severidad).ThenBy(i => i.Fase).ThenBy(i => i.Tabla))
            md.Append($"| {i.Severidad} | {i.Fase} | {i.Tabla} | {Celda(i.Clave)} | {Celda(i.Campo)} | {i.Codigo} | {Celda(i.Explicacion)} | {Celda(i.ValorOriginal?.Length > 200 ? i.ValorOriginal[..200] + "…" : i.ValorOriginal)} |\n");
        return md.ToString();
    }

    private static string Celda(string? texto) => (texto ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|");
}
