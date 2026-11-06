using ProConta.Datos.Persistencia;

namespace ProConta.Organizacion.Sociedades;

/// <summary>
/// Tipo de retención (tabla TiposRetencion, organizacion/sociedades-ejercicios 2026-11-06). Clase 1: actividades y
/// profesionales (modelo 111); 2: arrendamientos (modelo 115).
/// </summary>
public sealed record TipoRetencion(int IdTipoRetencion, decimal Porcentaje, string Descripcion, byte Clase, DateTime? VigenteDesde, DateTime? VigenteHasta);

/// <summary>Lectura de los tipos de retención; se escriben con el mantenimiento genérico.</summary>
public sealed class CatalogoTiposRetencion(IBaseDatos bd)
{
    /// <summary>Los activos y vigentes en <paramref name="fecha"/>, por porcentaje de mayor a menor.</summary>
    public async Task<IReadOnlyList<TipoRetencion>> VigentesAsync(DateTime fecha)
    {
        var todos = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdTipoRetencion, Porcentaje, Descripcion, Clase, VigenteDesde, VigenteHasta FROM TiposRetencion WHERE Activo = True",
            f => new TipoRetencion(f.Entero("IdTipoRetencion"), f.DecimalNulo("Porcentaje") ?? 0m, f.Texto("Descripcion") ?? "", (byte)f.Entero("Clase"),
                f.FechaNula("VigenteDesde"), f.FechaNula("VigenteHasta"))));
        var dia = fecha.Date;
        return todos.Where(t => (t.VigenteDesde is null || t.VigenteDesde.Value.Date <= dia) && (t.VigenteHasta is null || t.VigenteHasta.Value.Date >= dia))
            .OrderByDescending(t => t.Porcentaje).ToList();
    }
}
