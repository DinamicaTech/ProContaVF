using System.Globalization;
using ProConta.Activos.Gestion;
using ProConta.Datos.Persistencia;

namespace ProConta.Activos.Financieros;

/// <summary>Una compra o venta de un activo financiero que aún no está conciliada con el banco, para elegirla desde una línea N43.</summary>
/// <param name="ImporteTotalEur">Compra: coste del lote; venta: importe cobrado (el escrito o el estimado con el precio del día).</param>
/// <param name="CosteVendido">Venta: coste medio de las participaciones vendidas; null en las compras.</param>
public sealed record OperacionPorConciliar(int IdOperacion, int IdActivo, string CodigoActivo, string NombreActivo, int Tipo, DateTime Fecha, decimal Participaciones,
    decimal ImporteTotalEur, string? Notas, decimal? CosteVendido)
{
    public bool EsCompra => Tipo == OperacionDeInversion.TipoCompra;
}

/// <summary>Una subcuenta de un activo con su código en un ejercicio y el rol de su línea de plantilla.</summary>
public sealed record SubcuentaDeActivo(int IdIdentidadContable, string Codigo, string Nombre, string Rol);

/// <summary>
/// Conciliación de las compras y ventas de activos financieros con el banco (activos/activos-financieros 2026-11-24 09:14 y 09:24): las que faltan, la
/// marca de conciliada con su movimiento (OperacionesInversion.LineaN43) y deshacerla. El importe del banco es el real: al conciliar sustituye al de la
/// operación, que se guarda en ImporteAntesConciliar para recuperarlo al deshacer el asiento de la línea. Lo usan «Compra de activo…» y «Venta de
/// activo…» de Líneas N43.
/// </summary>
public static class ConciliacionOperacionesInversion
{
    /// <summary>Compras (tipo 1) o ventas (tipo 2) de activos financieros de la sociedad sin conciliar, con el coste de lo vendido en las ventas.</summary>
    public static async Task<IReadOnlyList<OperacionPorConciliar>> PendientesAsync(ISesionDatos s, string idSociedad, int tipo)
    {
        ArgumentNullException.ThrowIfNull(s);
        var filas = await s.ConsultarAsync(
            "SELECT o.IdOperacionInversion, o.IdActivo, a.Codigo, a.Nombre, o.FechaOperacion, o.Participaciones, o.ImporteTotalEur, o.Notas FROM OperacionesInversion AS o " +
            "INNER JOIN Activos AS a ON o.IdActivo = a.IdActivo WHERE a.IdSociedad = ? AND o.Tipo = ? AND o.LineaN43 IS NULL ORDER BY o.FechaOperacion, o.IdOperacionInversion",
            f => new OperacionPorConciliar(f.Entero("IdOperacionInversion"), f.Entero("IdActivo"), (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), tipo,
                f.Fecha("FechaOperacion").Date, f.Decimal("Participaciones"), f.Decimal("ImporteTotalEur"), f.Texto("Notas")?.Trim(), null), idSociedad, (byte)tipo);
        if (tipo != OperacionDeInversion.TipoVenta) return filas;
        var r = new List<OperacionPorConciliar>();
        foreach (var g in filas.GroupBy(f => f.IdActivo))
        {
            var coste = await CosteAsync(s, g.Key);
            r.AddRange(g.Select(f => f with { CosteVendido = coste.CosteVendido.GetValueOrDefault(f.IdOperacion) }));
        }
        return r.OrderBy(f => f.Fecha).ThenBy(f => f.IdOperacion).ToList();
    }

    /// <summary>La operación (de cualquier estado) con el coste de lo vendido si es venta; null si no existe en la sociedad.</summary>
    public static async Task<(OperacionPorConciliar Operacion, int? LineaN43)?> OperacionAsync(ISesionDatos s, string idSociedad, int idOperacion)
    {
        ArgumentNullException.ThrowIfNull(s);
        var f = await s.ConsultarPrimeroAsync<(OperacionPorConciliar, int?)?>(
            "SELECT o.IdOperacionInversion, o.IdActivo, o.Tipo, a.Codigo, a.Nombre, o.FechaOperacion, o.Participaciones, o.ImporteTotalEur, o.Notas, o.LineaN43 " +
            "FROM OperacionesInversion AS o INNER JOIN Activos AS a ON o.IdActivo = a.IdActivo WHERE o.IdOperacionInversion = ? AND a.IdSociedad = ?",
            x => (new OperacionPorConciliar(x.Entero("IdOperacionInversion"), x.Entero("IdActivo"), (x.Texto("Codigo") ?? "").Trim(), (x.Texto("Nombre") ?? "").Trim(),
                Convert.ToInt32(x.Valor("Tipo"), CultureInfo.InvariantCulture), x.Fecha("FechaOperacion").Date, x.Decimal("Participaciones"), x.Decimal("ImporteTotalEur"),
                x.Texto("Notas")?.Trim(), null), x.EnteroNulo("LineaN43")), idOperacion, idSociedad);
        if (f is not { } r) return null;
        if (r.Item1.Tipo != OperacionDeInversion.TipoVenta) return r;
        var coste = await CosteAsync(s, r.Item1.IdActivo);
        return (r.Item1 with { CosteVendido = coste.CosteVendido.GetValueOrDefault(r.Item1.IdOperacion) }, r.Item2);
    }

    /// <summary>Subcuentas del activo con su código en el ejercicio indicado (las que no existen en él no salen) y el rol de su línea de plantilla.</summary>
    public static async Task<IReadOnlyList<SubcuentaDeActivo>> SubcuentasAsync(ISesionDatos s, int idActivo, int idSociedadEjercicio)
    {
        ArgumentNullException.ThrowIfNull(s);
        return await s.ConsultarAsync(
            "SELECT sa.IdIdentidadContable, c.Codigo, c.Nombre, t.Rol FROM ((SubcuentasActivo AS sa INNER JOIN PlantillasSubcuentasActivo AS p ON sa.IdPlantillaSubcuentaActivo = p.IdPlantillaSubcuentaActivo) " +
            "INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo) " +
            "INNER JOIN CuentasContables AS c ON sa.IdIdentidadContable = c.IdIdentidadContable WHERE sa.IdActivo = ? AND c.IdSociedadEjercicio = ? ORDER BY c.Codigo",
            f => new SubcuentaDeActivo(f.Entero("IdIdentidadContable"), (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), (f.Texto("Rol") ?? "").Trim()),
            idActivo, idSociedadEjercicio);
    }

    /// <summary>
    /// Marca la operación como conciliada con la línea y le pone el importe del banco (guardando el que tenía para deshacerlo); recalcula el activo.
    /// </summary>
    public static async Task MarcarAsync(ISesionDatos s, int idOperacion, int idLinea, decimal importeBanco)
    {
        ArgumentNullException.ThrowIfNull(s);
        await s.EjecutarAsync("UPDATE OperacionesInversion SET ImporteAntesConciliar = ImporteTotalEur, ImporteTotalEur = ?, LineaN43 = ? WHERE IdOperacionInversion = ?",
            importeBanco, idLinea, idOperacion);
        if (await s.EscalarAsync("SELECT IdActivo FROM OperacionesInversion WHERE IdOperacionInversion = ?", idOperacion) is { } v and not DBNull)
            await ServicioActivos.RecalcularInversionAsync(s, Convert.ToInt32(v, CultureInfo.InvariantCulture), cambiarVersion: true);
    }

    /// <summary>
    /// Al deshacer el asiento de una línea: las operaciones conciliadas con ella vuelven a estar sin conciliar y recuperan el importe que tenían; recalcula
    /// sus activos. Devuelve cuántas había.
    /// </summary>
    public static async Task<int> DesmarcarLineaAsync(ISesionDatos s, int idLinea)
    {
        ArgumentNullException.ThrowIfNull(s);
        var activos = await s.ConsultarAsync("SELECT DISTINCT IdActivo FROM OperacionesInversion WHERE LineaN43 = ?", f => f.Entero("IdActivo"), idLinea);
        if (activos.Count == 0) return 0;
        var n = await s.EjecutarAsync("UPDATE OperacionesInversion SET ImporteTotalEur = IIf(ImporteAntesConciliar IS NULL, ImporteTotalEur, ImporteAntesConciliar), " +
            "ImporteAntesConciliar = NULL, LineaN43 = NULL WHERE LineaN43 = ?", idLinea);
        foreach (var a in activos) await ServicioActivos.RecalcularInversionAsync(s, a, cambiarVersion: true);
        return n;
    }

    private static async Task<CosteInversion> CosteAsync(ISesionDatos s, int idActivo) => CosteMedioInversion.Calcular(await s.ConsultarAsync(
        "SELECT IdOperacionInversion, Tipo, FechaOperacion, Participaciones, ImporteTotalEur FROM OperacionesInversion WHERE IdActivo = ?",
        f => new MovimientoCoste(f.Entero("IdOperacionInversion"), Convert.ToInt32(f.Valor("Tipo"), CultureInfo.InvariantCulture), f.Fecha("FechaOperacion").Date,
            f.Decimal("Participaciones"), f.Decimal("ImporteTotalEur")), idActivo));
}
