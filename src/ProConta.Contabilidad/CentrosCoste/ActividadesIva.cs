using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.CentrosCoste;

/// <summary>Actividad a efectos de IVA de un centro de coste (contabilidad/procesos 2026-11-08).</summary>
/// <param name="Deducible">0 no deducible, 1 por prorrata, 2 deducible al 100 %.</param>
/// <param name="Serie">Serie de 472 de la actividad, por ejemplo 4728; null si usa las cuentas generales.</param>
public sealed record ActividadIva(string Codigo, string Nombre, byte Deducible, string? Serie);

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
            "SELECT c.IdCentroCoste, c.IdCentroCostePadre, a.Codigo, a.Nombre, a.Deducible, a.SerieCuentaIva, a.Activa FROM CentrosCoste AS c " +
            "LEFT JOIN Actividades AS a ON c.IdActividad = a.IdActividad WHERE c.IdSociedad = ?",
            f => (Id: f.Entero("IdCentroCoste"), Padre: f.EnteroNulo("IdCentroCostePadre"),
                Propia: f.Texto("Codigo") is { } codigo && !f.EsNulo("Activa") && f.Logico("Activa")
                    ? new ActividadIva(codigo.Trim(), (f.Texto("Nombre") ?? codigo).Trim(),
                        f.EsNulo("Deducible") ? (byte)2 : Convert.ToByte(f.Valor("Deducible"), CultureInfo.InvariantCulture),
                        string.IsNullOrWhiteSpace(f.Texto("SerieCuentaIva")) ? null : f.Texto("SerieCuentaIva")!.Trim())
                    : null),
            idSociedad);
        return ResolverHerencia(filas.Select(f => (f.Id, f.Padre, f.Propia)));
    });

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
