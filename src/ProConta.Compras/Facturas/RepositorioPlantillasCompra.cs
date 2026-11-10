using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Plantillas de reconocimiento por empresa y proveedor (PlantillasReconocimientoCompra). Desde la versión 27 se guardan
/// por el NIF del proveedor (compras/reconocimiento 2026-11-07): una factura del mismo proveedor en otra carpeta, o en la
/// raíz, usa las mismas plantillas. Las anteriores, sin NIF, siguen valiendo por su carpeta y toman el NIF al acertar.
/// </summary>
internal static class RepositorioPlantillasCompra
{
    private const string Columnas = "IdPlantilla, Campo, Pagina, Ancla, Posicion, Palabras, X, Y, DesplazamientoX, Aciertos";

    private static PlantillaCampo Mapear(IFilaDatos r) => new(r.Entero("IdPlantilla"), r.Texto("Campo") ?? "", r.Entero("Pagina"), r.Texto("Ancla") ?? "",
        r.Texto("Posicion") ?? "", r.Entero("Palabras"), r.Doble("X"), r.Doble("Y"), r.Doble("DesplazamientoX"), r.Entero("Aciertos"));

    /// <summary>NIF tal como se guarda en las plantillas: solo letras y dígitos, en mayúsculas; null si no hay.</summary>
    internal static string? Clave(string? nif)
    {
        var t = new string((nif ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return t.Length == 0 ? null : t;
    }

    /// <summary>Plantillas de la carpeta del proveedor, de más a menos aciertos.</summary>
    public static Task<IReadOnlyList<PlantillaCampo>> ListarAsync(ISesionDatos s, string idSociedad, string carpeta) =>
        s.ConsultarAsync(
            $"SELECT {Columnas} FROM PlantillasReconocimientoCompra WHERE IdSociedad = ? AND Carpeta = ? ORDER BY Aciertos DESC, FechaUso DESC, IdPlantilla DESC",
            Mapear, idSociedad, carpeta);

    /// <summary>Plantillas de la empresa con NIF, agrupadas por NIF, de más a menos aciertos.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<PlantillaCampo>>> PorNifAsync(ISesionDatos s, string idSociedad)
    {
        var filas = await s.ConsultarAsync(
            $"SELECT NIF, {Columnas} FROM PlantillasReconocimientoCompra WHERE IdSociedad = ? AND NIF Is Not Null ORDER BY Aciertos DESC, FechaUso DESC, IdPlantilla DESC",
            r => (Nif: r.Texto("NIF")?.Trim() ?? "", Plantilla: Mapear(r)), idSociedad);
        return filas.Where(f => f.Nif.Length > 0).GroupBy(f => f.Nif, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlantillaCampo>)[.. g.Select(f => f.Plantilla)], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Plantillas del proveedor para leer o aprender: las de su NIF y, con ellas, las antiguas sin NIF de su carpeta. Sin NIF,
    /// solo las de la carpeta.
    /// </summary>
    public static Task<IReadOnlyList<PlantillaCampo>> DelProveedorAsync(ISesionDatos s, string idSociedad, string? nif, string carpeta) =>
        Clave(nif) is { } clave
            ? s.ConsultarAsync(
                $"SELECT {Columnas} FROM PlantillasReconocimientoCompra WHERE IdSociedad = ? AND (NIF = ? OR (NIF Is Null AND Carpeta = ?)) " +
                "ORDER BY Aciertos DESC, FechaUso DESC, IdPlantilla DESC", Mapear, idSociedad, clave, carpeta)
            : ListarAsync(s, idSociedad, carpeta);

    /// <summary>Alta con el NIF del proveedor; la carpeta se guarda como referencia (la del PDF o, en la raíz, el NIF).</summary>
    public static Task<int> InsertarAsync(ISesionDatos s, string idSociedad, string? nif, string carpeta, PlantillaCampo p, DateTime ahora) =>
        s.EjecutarAsync(
            "INSERT INTO PlantillasReconocimientoCompra (IdSociedad, Carpeta, NIF, Campo, Pagina, Ancla, Posicion, Palabras, X, Y, DesplazamientoX, Aciertos, FechaAlta, FechaUso) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idSociedad, carpeta.Length > 0 ? carpeta : Clave(nif) ?? "-", Clave(nif), p.Campo, (short)p.Pagina, p.Ancla, p.Posicion, (short)p.Palabras, p.X, p.Y, p.DesplazamientoX, 1, ahora, ahora);

    /// <summary>Suma un acierto; una plantilla antigua sin NIF toma el del proveedor.</summary>
    public static Task<int> AcertarAsync(ISesionDatos s, int idPlantilla, string? nif, DateTime ahora) =>
        Clave(nif) is { } clave
            ? s.EjecutarAsync("UPDATE PlantillasReconocimientoCompra SET Aciertos = Aciertos + 1, FechaUso = ?, NIF = ? WHERE IdPlantilla = ?", ahora, clave, idPlantilla)
            : s.EjecutarAsync("UPDATE PlantillasReconocimientoCompra SET Aciertos = Aciertos + 1, FechaUso = ? WHERE IdPlantilla = ?", ahora, idPlantilla);

    public static Task<int> BorrarAsync(ISesionDatos s, int idPlantilla) =>
        s.EjecutarAsync("DELETE FROM PlantillasReconocimientoCompra WHERE IdPlantilla = ?", idPlantilla);
}
