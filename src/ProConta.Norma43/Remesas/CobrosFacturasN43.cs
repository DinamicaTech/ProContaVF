using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Norma43.Remesas;

/// <summary>Último intento de cobro de una factura de venta: su línea de cobro, su devolución y el número de intento (1 = Facturas.LineaN43).</summary>
public sealed record IntentoCobro(int Intento, int LineaCobro, int? LineaDevolucion)
{
    public bool Devuelto => LineaDevolucion is not null;
}

/// <summary>
/// Cobros y devoluciones de los recibos de una factura de venta (norma43/remesas 2026-11-16): el primer recibo cobrado va en
/// Facturas.LineaN43 y su devolución en Facturas.LineaN43Devolucion; cada recibo que se vuelve a girar es un intento más en
/// ReintentosSEPA. La factura está cobrada si su último intento no se devolvió; pendiente si no tiene ninguno o el último se devolvió.
/// Todo dentro de la transacción de quien llama.
/// </summary>
public static class CobrosFacturasN43
{
    /// <summary>Último intento de la factura; null si no tiene ninguno.</summary>
    public static async Task<IntentoCobro?> UltimoAsync(ISesionDatos s, int idFactura)
    {
        ArgumentNullException.ThrowIfNull(s);
        var primero = await s.ConsultarPrimeroAsync("SELECT LineaN43, LineaN43Devolucion FROM Facturas WHERE IdFactura = ?",
            f => (Cobro: f.EnteroNulo("LineaN43"), Devolucion: f.EnteroNulo("LineaN43Devolucion")), idFactura);
        if (primero.Cobro is not int cobro || cobro <= 0) return null;
        var reintento = await s.ConsultarPrimeroAsync(
            "SELECT TOP 1 Intento, LineaN43, LineaN43Devolucion FROM ReintentosSEPA WHERE IdFactura = ? ORDER BY Intento DESC",
            f => (IntentoCobro?)new IntentoCobro(f.Entero("Intento"), f.Entero("LineaN43"), f.EnteroNulo("LineaN43Devolucion")), idFactura);
        return reintento ?? new IntentoCobro(1, cobro, primero.Devolucion is int d and > 0 ? d : null);
    }

    /// <summary>La factura se puede cobrar: no tiene recibos o el último se devolvió.</summary>
    public static async Task<bool> PendienteAsync(ISesionDatos s, int idFactura) => await UltimoAsync(s, idFactura) is null or { Devuelto: true };

    /// <summary>
    /// Anota que la línea cobra la factura: el primer cobro en Facturas.LineaN43 y los siguientes (tras una devolución) en ReintentosSEPA.
    /// Devuelve false si la factura ya está cobrada por otra línea.
    /// </summary>
    public static async Task<bool> RegistrarCobroAsync(ISesionDatos s, int idFactura, int idLinea)
    {
        ArgumentNullException.ThrowIfNull(s);
        var ultimo = await UltimoAsync(s, idFactura);
        if (ultimo is null)
            return await s.EjecutarAsync("UPDATE Facturas SET LineaN43 = ? WHERE IdFactura = ? AND (LineaN43 IS NULL OR LineaN43 = 0)", idLinea, idFactura) == 1;
        if (!ultimo.Devuelto) return ultimo.LineaCobro == idLinea;
        await s.EjecutarAsync("INSERT INTO ReintentosSEPA (IdFactura, Intento, LineaN43, LineaN43Devolucion) VALUES (?, ?, ?, ?)",
            idFactura, (short)(ultimo.Intento + 1), idLinea, null);
        return true;
    }

    /// <summary>Anota la devolución del recibo que cobró la línea <paramref name="idLineaCobro"/>. False si ese no es el último cobro vivo.</summary>
    public static async Task<bool> RegistrarDevolucionAsync(ISesionDatos s, int idFactura, int idLineaCobro, int idLineaDevolucion)
    {
        ArgumentNullException.ThrowIfNull(s);
        var ultimo = await UltimoAsync(s, idFactura);
        if (ultimo is null || ultimo.Devuelto || ultimo.LineaCobro != idLineaCobro) return false;
        return ultimo.Intento == 1
            ? await s.EjecutarAsync("UPDATE Facturas SET LineaN43Devolucion = ? WHERE IdFactura = ? AND LineaN43 = ?", idLineaDevolucion, idFactura, idLineaCobro) == 1
            : await s.EjecutarAsync("UPDATE ReintentosSEPA SET LineaN43Devolucion = ? WHERE IdFactura = ? AND Intento = ?", idLineaDevolucion, idFactura, (short)ultimo.Intento) == 1;
    }

    /// <summary>
    /// Quita el cobro de la línea: solo si es el último intento y no está devuelto (si lo está, hay que quitar antes la devolución).
    /// Devuelve false si no se puede.
    /// </summary>
    public static async Task<bool> QuitarCobroAsync(ISesionDatos s, int idFactura, int idLinea)
    {
        ArgumentNullException.ThrowIfNull(s);
        var ultimo = await UltimoAsync(s, idFactura);
        if (ultimo is null || ultimo.LineaCobro != idLinea || ultimo.Devuelto) return false;
        return ultimo.Intento == 1
            ? await s.EjecutarAsync("UPDATE Facturas SET LineaN43 = NULL WHERE IdFactura = ? AND LineaN43 = ?", idFactura, idLinea) == 1
            : await s.EjecutarAsync("DELETE FROM ReintentosSEPA WHERE IdFactura = ? AND Intento = ?", idFactura, (short)ultimo.Intento) == 1;
    }

    /// <summary>Quita la devolución de la línea: solo si es la del último intento (después no se volvió a cobrar).</summary>
    public static async Task<bool> QuitarDevolucionAsync(ISesionDatos s, int idFactura, int idLineaDevolucion)
    {
        ArgumentNullException.ThrowIfNull(s);
        var ultimo = await UltimoAsync(s, idFactura);
        if (ultimo is null || ultimo.LineaDevolucion != idLineaDevolucion) return false;
        return ultimo.Intento == 1
            ? await s.EjecutarAsync("UPDATE Facturas SET LineaN43Devolucion = NULL WHERE IdFactura = ? AND LineaN43Devolucion = ?", idFactura, idLineaDevolucion) == 1
            : await s.EjecutarAsync("UPDATE ReintentosSEPA SET LineaN43Devolucion = NULL WHERE IdFactura = ? AND Intento = ?", idFactura, (short)ultimo.Intento) == 1;
    }

    /// <summary>Facturas cuyo cobro (de cualquier intento) es la línea.</summary>
    public static async Task<IReadOnlyList<int>> CobradasPorAsync(ISesionDatos s, int idLinea)
    {
        ArgumentNullException.ThrowIfNull(s);
        var r = (await s.ConsultarAsync("SELECT IdFactura FROM Facturas WHERE LineaN43 = ?", f => f.Entero("IdFactura"), idLinea)).ToList();
        r.AddRange(await s.ConsultarAsync("SELECT IdFactura FROM ReintentosSEPA WHERE LineaN43 = ?", f => f.Entero("IdFactura"), idLinea));
        return r.Distinct().ToList();
    }

    /// <summary>Facturas cuya devolución (de cualquier intento) es la línea.</summary>
    public static async Task<IReadOnlyList<int>> DevueltasPorAsync(ISesionDatos s, int idLinea)
    {
        ArgumentNullException.ThrowIfNull(s);
        var r = (await s.ConsultarAsync("SELECT IdFactura FROM Facturas WHERE LineaN43Devolucion = ?", f => f.Entero("IdFactura"), idLinea)).ToList();
        r.AddRange(await s.ConsultarAsync("SELECT IdFactura FROM ReintentosSEPA WHERE LineaN43Devolucion = ?", f => f.Entero("IdFactura"), idLinea));
        return r.Distinct().ToList();
    }

    /// <summary>
    /// De las facturas indicadas, las pendientes de cobro (sin recibos o con el último devuelto), en pocas consultas.
    /// </summary>
    public static async Task<IReadOnlySet<int>> PendientesAsync(ISesionDatos s, IReadOnlyCollection<int> ids)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ids);
        var r = new HashSet<int>();
        foreach (var bloque in ids.Distinct().Chunk(300))
        {
            var lista = string.Join(",", bloque.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            var primeros = await s.ConsultarAsync($"SELECT IdFactura, LineaN43, LineaN43Devolucion FROM Facturas WHERE IdFactura IN ({lista})",
                f => (Id: f.Entero("IdFactura"), Cobro: f.EnteroNulo("LineaN43"), Devolucion: f.EnteroNulo("LineaN43Devolucion")));
            var reintentos = (await s.ConsultarAsync($"SELECT IdFactura, Intento, LineaN43Devolucion FROM ReintentosSEPA WHERE IdFactura IN ({lista})",
                    f => (Id: f.Entero("IdFactura"), Intento: f.Entero("Intento"), Devolucion: f.EnteroNulo("LineaN43Devolucion"))))
                .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Intento).First());
            foreach (var p in primeros)
            {
                if (p.Cobro is not int c || c <= 0) { r.Add(p.Id); continue; }
                var devuelto = reintentos.TryGetValue(p.Id, out var ultimo) ? ultimo.Devolucion is int d1 && d1 > 0 : p.Devolucion is int d2 && d2 > 0;
                if (devuelto) r.Add(p.Id);
            }
        }
        return r;
    }
}
