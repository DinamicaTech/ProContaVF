using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Activo que se puede asignar a una línea de venta o de programada (activos 2026-11-17 20:35): su centro de coste y su subcuenta con el papel
/// «Ingreso por alquiler» en el ejercicio de trabajo (null si no la tiene). <see cref="EnUso"/> es false si está dado de baja: no se ofrece
/// para elegir, pero una línea que ya lo tiene lo sigue enseñando.
/// </summary>
public sealed record ActivoLinea(int IdActivo, string Codigo, string Nombre, int? IdCentroCoste, string? CuentaIngreso, bool EnUso = true)
{
    public string Rotulo => EnUso ? Nombre : $"{Nombre} (de baja)";
    public override string ToString() => Rotulo;
}

/// <summary>Valores por defecto de una línea de venta al elegir su activo (activos 2026-11-17 20:35). No toca importes ni contabilización.</summary>
public static class ReglasActivoLinea
{
    /// <summary>Rol de la subcuenta de ingreso del activo (TiposSubcuentaActivo.Rol; el texto que ve el operador es «Ingreso por alquiler»).</summary>
    public const string RolIngresoAlquiler = "INGRESO_ALQUILER";

    /// <summary>
    /// Asigna el activo a la línea: el centro de coste pasa a ser el del activo (si lo tiene; después se puede cambiar) y, si la línea no tiene
    /// cuenta, toma la subcuenta de ingreso por alquiler del activo. Quitar el activo (null) solo lo quita: centro y cuenta se quedan como están.
    /// Devuelve qué ha cambiado, para avisar a la pantalla.
    /// </summary>
    public static (bool Centro, bool Cuenta) Elegir(LineaFacturaVenta linea, ActivoLinea? activo)
    {
        linea.IdActivo = activo?.IdActivo;
        if (activo is null) return (false, false);
        var centro = false;
        if (activo.IdCentroCoste is int c && linea.IdCentroCoste != c) { linea.IdCentroCoste = c; centro = true; }
        var cuenta = false;
        if (string.IsNullOrWhiteSpace(linea.CuentaContable) && !string.IsNullOrWhiteSpace(activo.CuentaIngreso))
        {
            linea.CuentaContable = activo.CuentaIngreso.Trim();
            cuenta = true;
        }
        return (centro, cuenta);
    }

    /// <summary>
    /// Activos propios (no ajenos) de la sociedad para las líneas de venta: los que están en uso y, marcados, los dados de baja (para enseñar el
    /// de una línea antigua). La subcuenta de ingreso es la de su papel «Ingreso por alquiler» con su código en el ejercicio de trabajo.
    /// </summary>
    public static async Task<IReadOnlyList<ActivoLinea>> ListarAsync(IBaseDatos bd, ContextoSociedadEjercicio contexto) =>
        await bd.EjecutarAsync<IReadOnlyList<ActivoLinea>>(async s =>
        {
            var ingresos = (await s.ConsultarAsync(
                    "SELECT sa.IdActivo, c.Codigo FROM (((SubcuentasActivo AS sa INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo) " +
                    "INNER JOIN PlantillasSubcuentasActivo AS p ON sa.IdPlantillaSubcuentaActivo = p.IdPlantillaSubcuentaActivo) " +
                    "INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo) " +
                    "INNER JOIN CuentasContables AS c ON sa.IdIdentidadContable = c.IdIdentidadContable " +
                    "WHERE a.IdSociedad = ? AND t.Rol = ? AND c.IdSociedadEjercicio = ?",
                    f => (Activo: f.Entero("IdActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim()), contexto.IdSociedad, RolIngresoAlquiler, contexto.IdSociedadEjercicio))
                .Where(x => x.Codigo.Length > 0).GroupBy(x => x.Activo).ToDictionary(g => g.Key, g => g.Select(x => x.Codigo).Min(StringComparer.Ordinal)!);
            return (await s.ConsultarAsync(
                    "SELECT IdActivo, Codigo, Nombre, IdCentroCoste, FechaBaja FROM Activos WHERE IdSociedad = ? AND EsAjeno = False ORDER BY Nombre",
                    f => new ActivoLinea(f.Entero("IdActivo"), (f.Texto("Codigo") ?? "").Trim(),
                        (f.Texto("Nombre") ?? f.Texto("Codigo") ?? $"Activo {f.Entero("IdActivo")}").Trim(), f.EnteroNulo("IdCentroCoste"), null, f.FechaNula("FechaBaja") is null),
                    contexto.IdSociedad))
                .Select(a => a with { CuentaIngreso = ingresos.GetValueOrDefault(a.IdActivo) }).ToList();
        });
}
