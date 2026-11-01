using ProConta.Datos.Persistencia;

namespace ProConta.Organizacion.Geografia;

/// <summary>Combinación de país, CP, provincia y población que aparece en algún domicilio de la BD.</summary>
public sealed record LugarConocido(string? Pais, string CodigoPostal, string? Provincia, string? Poblacion);

/// <summary>Resolución de un código postal (RF-TER-05).</summary>
/// <param name="Provincias">Provincias conocidas para ese CP; con una sola, se selecciona sola.</param>
/// <param name="Poblaciones">Poblaciones conocidas para ese CP (y provincia, si es única).</param>
public sealed record ResolucionCodigoPostal(string CodigoPostal, IReadOnlyList<string> Provincias, IReadOnlyList<string> Poblaciones)
{
    public bool SinCorrespondencia => Provincias.Count == 0 && Poblaciones.Count == 0;
    public string? ProvinciaUnica => Provincias.Count == 1 ? Provincias[0] : null;
    public string? PoblacionUnica => Poblaciones.Count == 1 ? Poblaciones[0] : null;
}

/// <summary>
/// Catálogo geográfico lo más simple posible: se deduce de los domicilios que ya hay en la BD
/// (sin importar el INE). Con un solo candidato lo propone; con varios deja elegir; sin
/// correspondencia no inventa nada.
/// </summary>
public sealed class CatalogoGeografico
{
    private readonly IReadOnlyList<LugarConocido> _lugares;

    public CatalogoGeografico(IEnumerable<LugarConocido> lugares) =>
        _lugares = lugares
            .Select(l => new LugarConocido(Limpiar(l.Pais)?.ToUpperInvariant(), Limpiar(l.CodigoPostal) ?? "", Limpiar(l.Provincia), Limpiar(l.Poblacion)))
            .Where(l => l.CodigoPostal.Length > 0)
            .ToList();

    public ResolucionCodigoPostal Resolver(string codigoPostal, string? pais = null)
    {
        var cp = Limpiar(codigoPostal) ?? "";
        var candidatos = _lugares.Where(l => l.CodigoPostal == cp && (pais is null || l.Pais is null || l.Pais == pais.Trim().ToUpperInvariant())).ToList();
        var provincias = Distintos(candidatos.Select(l => l.Provincia));
        var deLaProvincia = provincias.Count == 1
            ? candidatos.Where(l => l.Provincia is null || string.Equals(l.Provincia, provincias[0], StringComparison.OrdinalIgnoreCase))
            : candidatos;
        return new ResolucionCodigoPostal(cp, provincias, Distintos(deLaProvincia.Select(l => l.Poblacion)));
    }

    public IReadOnlyList<string> Provincias(string? pais = null) =>
        Distintos(_lugares.Where(l => pais is null || l.Pais is null || l.Pais == pais.ToUpperInvariant()).Select(l => l.Provincia));

    public IReadOnlyList<string> Poblaciones(string provincia) =>
        Distintos(_lugares.Where(l => string.Equals(l.Provincia, provincia?.Trim(), StringComparison.OrdinalIgnoreCase)).Select(l => l.Poblacion));

    /// <summary>Distintos sin distinguir mayúsculas, conservando la forma más frecuente (a igualdad, la escrita en minúsculas).</summary>
    private static List<string> Distintos(IEnumerable<string?> valores) =>
        valores.OfType<string>()
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.GroupBy(v => v).OrderByDescending(x => x.Count()).ThenByDescending(x => x.Key.Count(char.IsLower)).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : string.Join(' ', texto.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Carga el catálogo geográfico de los domicilios guardados (solo lectura).</summary>
public sealed class FuenteGeografiaAccess(IBaseDatos bd)
{
    public async Task<CatalogoGeografico> CargarAsync()
    {
        var lugares = await bd.EjecutarAsync(async s =>
        {
            var deTerceros = await s.ConsultarAsync(
                "SELECT DISTINCT Pais, CodigoPostal, Provincia, Poblacion FROM Terceros WHERE CodigoPostal IS NOT NULL", Mapear);
            var deDomicilios = await s.ConsultarAsync(
                "SELECT DISTINCT Pais, CodigoPostal, Provincia, Poblacion FROM DomiciliosTercero WHERE CodigoPostal IS NOT NULL", Mapear);
            return deTerceros.Concat(deDomicilios).ToList();
        });
        return new CatalogoGeografico(lugares);
    }

    private static LugarConocido Mapear(IFilaDatos f) =>
        new(f.Texto("Pais"), f.Texto("CodigoPostal") ?? "", f.Texto("Provincia"), f.Texto("Poblacion"));
}
