using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Facturas;

/// <summary>Activo propio (no ajeno) al que pertenece una subcuenta, con su centro de coste.</summary>
public sealed record ActivoDeCuenta(int IdActivo, string Nombre, int? IdCentroCoste);

/// <summary>Lo que hace la ficha al elegir la cuenta de gasto: el centro que pone (o null) y el aviso que enseña (o null).</summary>
public sealed record PropuestaCentroActivo(int? CentroPropuesto, string? Aviso);

/// <summary>
/// Centro de coste de una factura de compra según el activo de su cuenta de gasto (activos 2026-11-17 20:35; compras/factura-compra): si la cuenta
/// es una subcuenta de un único activo con centro y la factura no tiene centro, se propone el del activo (se puede cambiar); si ya tiene otro
/// centro, no se toca y se avisa sin bloquear. Si la subcuenta es de varios activos, de ninguno o el activo no tiene centro, no se hace nada.
/// </summary>
public static class CentroActivoCompra
{
    public static PropuestaCentroActivo Proponer(IReadOnlyCollection<ActivoDeCuenta> activosDeLaCuenta, int? centroActual, string? cuenta = null,
        Func<int, string>? textoCentro = null)
    {
        if (activosDeLaCuenta.Count != 1 || activosDeLaCuenta.First() is not { IdCentroCoste: int centro } activo) return new(null, null);
        if (centroActual is null) return new(centro, null);
        if (centroActual == centro) return new(null, null);
        string Texto(int id) => textoCentro?.Invoke(id) ?? $"el centro {id}";
        return new(null,
            $"La cuenta {cuenta} es del activo «{activo.Nombre}», cuyo centro de coste es {Texto(centro)}, pero la factura tiene {Texto(centroActual.Value)}. " +
            "Si el gasto es de ese activo, cambia el centro; si no, déjalo así.");
    }

    /// <summary>Activos propios de cada subcuenta (por su código en el ejercicio de trabajo).</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<ActivoDeCuenta>>> LeerAsync(IBaseDatos bd, ContextoSociedadEjercicio ctx) =>
        await bd.EjecutarAsync<IReadOnlyDictionary<string, IReadOnlyList<ActivoDeCuenta>>>(async s =>
            (await s.ConsultarAsync(
                "SELECT DISTINCT c.Codigo, a.IdActivo, a.Nombre, a.Codigo AS CodigoActivo, a.IdCentroCoste FROM (SubcuentasActivo AS sa INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo) " +
                "INNER JOIN CuentasContables AS c ON sa.IdIdentidadContable = c.IdIdentidadContable WHERE a.IdSociedad = ? AND a.EsAjeno = False AND c.IdSociedadEjercicio = ?",
                f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Activo: new ActivoDeCuenta(f.Entero("IdActivo"),
                    (f.Texto("Nombre") ?? f.Texto("CodigoActivo") ?? "").Trim(), f.EnteroNulo("IdCentroCoste"))),
                ctx.IdSociedad, ctx.IdSociedadEjercicio))
            .Where(x => x.Codigo.Length > 0)
            .GroupBy(x => x.Codigo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ActivoDeCuenta>)g.Select(x => x.Activo).DistinctBy(a => a.IdActivo).ToList(), StringComparer.Ordinal));
}
