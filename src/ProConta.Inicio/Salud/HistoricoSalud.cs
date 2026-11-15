using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Inicio.Salud;

/// <summary>Recuento de un elemento de la salud en un día: revisión, clave del elemento, número de cosas que cuenta e importe.</summary>
public sealed record FilaHistoricoSalud(string ClaveRevision, string ClaveElemento, int Elementos, decimal? Importe);

/// <summary>
/// Histórico diario del recuento de la salud (tabla HistoricoSalud, esquema 54), para la tendencia «desde el lunes» de cada aviso
/// (pantalla-de-inicio 2026-11-15 16:37). Es lo único que Inicio escribe en la BD.
/// </summary>
public interface IHistoricoSalud
{
    /// <summary>Guarda el recuento del día: sustituye lo guardado antes ese mismo día para la sociedad y el ejercicio.</summary>
    Task GuardarAsync(string idSociedad, int ejercicio, DateTime fecha, IReadOnlyList<FilaHistoricoSalud> filas, CancellationToken c = default);

    /// <summary>El recuento guardado un día (vacío si no hay).</summary>
    Task<IReadOnlyList<FilaHistoricoSalud>> LeerAsync(string idSociedad, int ejercicio, DateTime fecha, CancellationToken c = default);
}

/// <summary>Reglas de la tendencia «desde el lunes», sin BD.</summary>
public static class TendenciaSalud
{
    public const int LargoClaveRevision = 60;
    public const int LargoClaveElemento = 120;

    /// <summary>El lunes de la semana de <paramref name="dia"/> (el mismo día si es lunes).</summary>
    public static DateTime Lunes(DateTime dia) => dia.Date.AddDays(-(((int)dia.DayOfWeek + 6) % 7));

    /// <summary>
    /// Filas del histórico de una revisión completa: una por elemento, con su cantidad (1 si no cuenta nada) y su importe. Las
    /// revisiones que han fallado no se guardan (su recuento no es real).
    /// </summary>
    public static IReadOnlyList<FilaHistoricoSalud> Filas(IEnumerable<ResultadoRevision> revisiones) =>
        revisiones.Where(r => r.Error is null)
            .SelectMany(r => r.Hallazgos.Elementos.Select(e => new FilaHistoricoSalud(Cortar(r.Clave, LargoClaveRevision), Cortar(e.Clave, LargoClaveElemento),
                e.Cantidad ?? 1, e.Importe is decimal i ? Math.Round(i, 2, MidpointRounding.AwayFromZero) : null)))
            .GroupBy(f => f.ClaveElemento, StringComparer.Ordinal).Select(g => g.First()).ToList();

    /// <summary>
    /// Variación de cada elemento de hoy respecto al lunes: solo los que tienen dato el lunes y cambian. Si hoy es lunes no hay
    /// tendencia (el dato del lunes es el de hoy).
    /// </summary>
    public static IReadOnlyDictionary<string, int> Variaciones(IReadOnlyList<FilaHistoricoSalud> hoy, IReadOnlyList<FilaHistoricoSalud> lunes)
    {
        var antes = lunes.GroupBy(f => f.ClaveElemento, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Elementos, StringComparer.Ordinal);
        var r = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in hoy)
            if (antes.TryGetValue(f.ClaveElemento, out var n) && f.Elementos != n) r[f.ClaveElemento] = f.Elementos - n;
        return r;
    }

    /// <summary>«+3 desde el lunes» / «−2 desde el lunes»; null sin variación.</summary>
    public static string? Texto(int? variacion) => variacion switch
    {
        null or 0 => null,
        > 0 => $"+{variacion.Value.ToString("#,##0", CultureInfo.GetCultureInfo("es-ES"))} desde el lunes",
        _ => $"−{(-variacion.Value).ToString("#,##0", CultureInfo.GetCultureInfo("es-ES"))} desde el lunes",
    };

    /// <summary>Clave del elemento tal como se guarda (cortada a la longitud del campo).</summary>
    public static string Clave(string clave) => Cortar(clave, LargoClaveElemento);

    private static string Cortar(string s, int largo) => s.Length <= largo ? s : s[..largo];
}

/// <summary>Histórico de la salud en Access (tabla HistoricoSalud).</summary>
public sealed class HistoricoSaludAccess(IBaseDatos bd) : IHistoricoSalud
{
    public Task GuardarAsync(string idSociedad, int ejercicio, DateTime fecha, IReadOnlyList<FilaHistoricoSalud> filas, CancellationToken c = default) =>
        bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM HistoricoSalud WHERE IdSociedad = ? AND Ejercicio = ? AND Fecha = ?", idSociedad, (short)ejercicio, fecha.Date);
            var ahora = DateTime.Now;
            ahora = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, ahora.Minute, ahora.Second);
            foreach (var f in filas)
                await s.EjecutarAsync(
                    "INSERT INTO HistoricoSalud (IdSociedad, Ejercicio, Fecha, ClaveRevision, ClaveElemento, Elementos, Importe, FechaHoraRegistro) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    idSociedad, (short)ejercicio, fecha.Date, f.ClaveRevision, f.ClaveElemento, f.Elementos, f.Importe, ahora);
            return true;
        }, c);

    public Task<IReadOnlyList<FilaHistoricoSalud>> LeerAsync(string idSociedad, int ejercicio, DateTime fecha, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<FilaHistoricoSalud>>(async s => await s.ConsultarAsync(
            "SELECT ClaveRevision, ClaveElemento, Elementos, Importe FROM HistoricoSalud WHERE IdSociedad = ? AND Ejercicio = ? AND Fecha = ?",
            f => new FilaHistoricoSalud((f.Texto("ClaveRevision") ?? "").Trim(), (f.Texto("ClaveElemento") ?? "").Trim(), f.Entero("Elementos"), f.DecimalNulo("Importe")),
            idSociedad, (short)ejercicio, fecha.Date), c);
}
