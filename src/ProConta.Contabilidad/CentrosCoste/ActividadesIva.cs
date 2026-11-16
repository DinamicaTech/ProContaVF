using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.CentrosCoste;

/// <summary>Cambio de deducción de una actividad: desde <paramref name="Desde"/> (incluido) deduce como dice <paramref name="Deducible"/>.</summary>
public sealed record CambioDeduccion(DateTime Desde, byte Deducible)
{
    /// <summary>
    /// Deducción vigente en <paramref name="fecha"/>: la del último cambio con fecha igual o anterior o, si no hay ninguno,
    /// <paramref name="inicial"/>. Los cambios van en orden de fecha; se compara solo el día.
    /// </summary>
    public static byte Vigente(byte inicial, IReadOnlyList<CambioDeduccion>? cambios, DateTime fecha)
    {
        var r = inicial;
        if (cambios is null) return r;
        foreach (var c in cambios)
        {
            if (c.Desde.Date > fecha.Date) break;
            r = c.Deducible;
        }
        return r;
    }
}

/// <summary>Actividad a efectos de IVA de un centro de coste (contabilidad/procesos 2026-11-08).</summary>
/// <param name="Deducible">0 no deducible, 1 por prorrata, 2 deducible al 100 %: la deducción antes de su primer cambio.</param>
/// <param name="Serie">Serie de 472 de la actividad, por ejemplo 4728; null si usa las cuentas generales.</param>
/// <param name="Cambios">
/// Cambios de deducción desde una fecha (ActividadesDeduccion, contabilidad/procesos/cambio-deduccion-actividad), en orden de
/// fecha; null o vacía si la actividad deduce siempre igual.
/// </param>
public sealed record ActividadIva(string Codigo, string Nombre, byte Deducible, string? Serie, IReadOnlyList<CambioDeduccion>? Cambios = null)
{
    /// <summary>Deducción vigente en <paramref name="fecha"/> (la de la factura): <see cref="CambioDeduccion.Vigente"/>.</summary>
    public byte DeducibleEn(DateTime fecha) => CambioDeduccion.Vigente(Deducible, Cambios, fecha);

    /// <summary>Si la actividad tiene algún cambio de deducción con fecha.</summary>
    public bool TieneCambios => Cambios is { Count: > 0 };
}

/// <summary>Lee la actividad que aplica a cada centro de coste. La comparten compras y ventas.</summary>
public sealed class LectorActividadesIva(IBaseDatos bd)
{
    /// <summary>
    /// Por centro de la sociedad, su actividad o la del nodo más cercano por encima que la tenga. Una actividad inactiva no
    /// cuenta. El centro sin ninguna no está en el resultado: usa las cuentas generales del ejercicio.
    /// </summary>
    public Task<IReadOnlyDictionary<int, ActividadIva>> PorCentroAsync(string idSociedad) => bd.EjecutarAsync(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT c.IdCentroCoste, c.IdCentroCostePadre, a.IdActividad, a.Codigo, a.Nombre, a.Deducible, a.SerieCuentaIva, a.Activa FROM CentrosCoste AS c " +
            "LEFT JOIN Actividades AS a ON c.IdActividad = a.IdActividad WHERE c.IdSociedad = ?",
            f => (Id: f.Entero("IdCentroCoste"), Padre: f.EnteroNulo("IdCentroCostePadre"), IdActividad: f.EnteroNulo("IdActividad"),
                Propia: f.Texto("Codigo") is { } codigo && !f.EsNulo("Activa") && f.Logico("Activa")
                    ? new ActividadIva(codigo.Trim(), (f.Texto("Nombre") ?? codigo).Trim(),
                        f.EsNulo("Deducible") ? (byte)2 : Convert.ToByte(f.Valor("Deducible"), CultureInfo.InvariantCulture),
                        string.IsNullOrWhiteSpace(f.Texto("SerieCuentaIva")) ? null : f.Texto("SerieCuentaIva")!.Trim())
                    : null),
            idSociedad);
        var cambios = await CambiosAsync(s, idSociedad);
        filas = filas.Select(f => f.Propia is not null && f.IdActividad is int ia && cambios.TryGetValue(ia, out var l) ? f with { Propia = f.Propia with { Cambios = l } } : f).ToList();
        return ResolverHerencia(filas.Select(f => (f.Id, f.Padre, f.Propia)));
    });

    /// <summary>Cambios de deducción de las actividades de la sociedad (ActividadesDeduccion), por actividad y en orden de fecha.</summary>
    public static async Task<IReadOnlyDictionary<int, IReadOnlyList<CambioDeduccion>>> CambiosAsync(ISesionDatos s, string idSociedad)
    {
        var filas = await s.ConsultarAsync(
            "SELECT d.IdActividad, d.FechaDesde, d.Deducible FROM ActividadesDeduccion AS d INNER JOIN Actividades AS a ON d.IdActividad = a.IdActividad " +
            "WHERE a.IdSociedad = ? ORDER BY d.IdActividad, d.FechaDesde",
            f => (Id: f.Entero("IdActividad"), C: new CambioDeduccion(f.Fecha("FechaDesde").Date, Convert.ToByte(f.Valor("Deducible"), CultureInfo.InvariantCulture))),
            idSociedad);
        return filas.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<CambioDeduccion>)g.Select(x => x.C).ToList());
    }

    /// <summary>Calendario legible de la deducción: «No deducible hasta el 30/11/2026 · 100 % deducible desde el 01/12/2026».</summary>
    public static string Calendario(byte inicial, IReadOnlyList<CambioDeduccion> cambios)
    {
        if (cambios.Count == 0) return $"{TextoDeducible(inicial)} siempre.";
        var partes = new List<string> { $"{TextoDeducible(inicial)} hasta el {cambios[0].Desde.AddDays(-1):dd/MM/yyyy}" };
        partes.AddRange(cambios.Select(c => $"{TextoDeducible(c.Deducible)} desde el {c.Desde:dd/MM/yyyy}"));
        return string.Join(" · ", partes);
    }

    public static string TextoDeducible(byte deducible) => deducible switch { 0 => "No deducible", 1 => "Por prorrata", _ => "100 % deducible" };

    /// <summary>Aplica la herencia por la jerarquía; el límite de niveles protege de un ciclo en los datos.</summary>
    public static IReadOnlyDictionary<int, ActividadIva> ResolverHerencia(IEnumerable<(int Id, int? Padre, ActividadIva? Propia)> centros)
    {
        var porId = centros.ToDictionary(c => c.Id);
        var r = new Dictionary<int, ActividadIva>();
        foreach (var id in porId.Keys)
        {
            int? actual = id;
            for (var nivel = 0; actual is int a && nivel < 20 && porId.TryGetValue(a, out var c); nivel++)
            {
                if (c.Propia is not null) { r[id] = c.Propia; break; }
                actual = c.Padre;
            }
        }
        return r;
    }
}
