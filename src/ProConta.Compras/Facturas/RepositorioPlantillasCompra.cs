using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;

namespace ProConta.Compras.Facturas;

/// <summary>Plantillas de reconocimiento por empresa y carpeta del proveedor (PlantillasReconocimientoCompra).</summary>
internal static class RepositorioPlantillasCompra
{
    /// <summary>Plantillas de la carpeta del proveedor, de más a menos aciertos.</summary>
    public static Task<IReadOnlyList<PlantillaCampo>> ListarAsync(ISesionDatos s, string idSociedad, string carpeta) =>
        s.ConsultarAsync(
            "SELECT IdPlantilla, Campo, Pagina, Ancla, Posicion, Palabras, X, Y, DesplazamientoX, Aciertos FROM PlantillasReconocimientoCompra " +
            "WHERE IdSociedad = ? AND Carpeta = ? ORDER BY Aciertos DESC, FechaUso DESC, IdPlantilla DESC",
            r => new PlantillaCampo(r.Entero("IdPlantilla"), r.Texto("Campo") ?? "", r.Entero("Pagina"), r.Texto("Ancla") ?? "", r.Texto("Posicion") ?? "",
                r.Entero("Palabras"), r.Doble("X"), r.Doble("Y"), r.Doble("DesplazamientoX"), r.Entero("Aciertos")),
            idSociedad, carpeta);

    public static Task<int> InsertarAsync(ISesionDatos s, string idSociedad, string carpeta, PlantillaCampo p, DateTime ahora) =>
        s.EjecutarAsync(
            "INSERT INTO PlantillasReconocimientoCompra (IdSociedad, Carpeta, Campo, Pagina, Ancla, Posicion, Palabras, X, Y, DesplazamientoX, Aciertos, FechaAlta, FechaUso) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idSociedad, carpeta, p.Campo, (short)p.Pagina, p.Ancla, p.Posicion, (short)p.Palabras, p.X, p.Y, p.DesplazamientoX, 1, ahora, ahora);

    public static Task<int> AcertarAsync(ISesionDatos s, int idPlantilla, DateTime ahora) =>
        s.EjecutarAsync("UPDATE PlantillasReconocimientoCompra SET Aciertos = Aciertos + 1, FechaUso = ? WHERE IdPlantilla = ?", ahora, idPlantilla);

    public static Task<int> BorrarAsync(ISesionDatos s, int idPlantilla) =>
        s.EjecutarAsync("DELETE FROM PlantillasReconocimientoCompra WHERE IdPlantilla = ?", idPlantilla);
}
