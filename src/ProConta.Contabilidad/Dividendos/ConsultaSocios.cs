using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.Dividendos;

/// <summary>Cuenta de una identidad contable en un ejercicio (null si aún no existe allí) y la última conocida.</summary>
public sealed record CuentaSocio(int IdIdentidadContable, string? CodigoEnEjercicio, string? UltimoCodigo)
{
    public string Codigo => CodigoEnEjercicio ?? UltimoCodigo ?? "";
    public bool ExisteEnEjercicio => CodigoEnEjercicio is not null;
}

/// <summary>
/// Un socio activo de la sociedad (rol Socio de terceros/roles, tablas Socios y SociosSociedad) con su % de participación y sus subcuentas 526
/// (dividendo a pagar) y 4751 (retención) resueltas en un ejercicio.
/// </summary>
public sealed record SocioSociedad(int IdSocio, int IdTercero, string Nombre, decimal Participacion, CuentaSocio? Dividendos, CuentaSocio? Retencion);

/// <summary>
/// Lecturas de los socios de una sociedad para el reparto de dividendos y el pago de dividendos de Norma 43. Solo lee: el rol lo escribe la
/// ficha del tercero (terceros/roles), que es la dueña de Socios y SociosSociedad.
/// </summary>
public sealed class ConsultaSocios(IBaseDatos bd)
{
    /// <summary>Socios activos de la sociedad, por nombre, con sus subcuentas en el ejercicio <paramref name="idSociedadEjercicio"/>.</summary>
    public Task<IReadOnlyList<SocioSociedad>> ActivosAsync(string idSociedad, int idSociedadEjercicio) => bd.EjecutarAsync(s => ActivosAsync(s, idSociedad, idSociedadEjercicio));

    public static async Task<IReadOnlyList<SocioSociedad>> ActivosAsync(ISesionDatos s, string idSociedad, int idSociedadEjercicio)
    {
        var filas = await s.ConsultarAsync(
            "SELECT so.IdSocio, so.IdTercero, t.Nombre, ss.Participacion, ss.IdIdentidadDividendos, ss.IdIdentidadRetencion " +
            "FROM (SociosSociedad AS ss INNER JOIN Socios AS so ON ss.IdSocio = so.IdSocio) INNER JOIN Terceros AS t ON so.IdTercero = t.IdTercero " +
            "WHERE ss.IdSociedad = ? AND ss.Activo = True ORDER BY t.Nombre, so.IdSocio",
            f => (Id: f.Entero("IdSocio"), Tercero: f.Entero("IdTercero"), Nombre: (f.Texto("Nombre") ?? "").Trim(), Participacion: f.DecimalNulo("Participacion") ?? 0m,
                Dividendos: f.EnteroNulo("IdIdentidadDividendos"), Retencion: f.EnteroNulo("IdIdentidadRetencion")), idSociedad);
        var r = new List<SocioSociedad>(filas.Count);
        foreach (var f in filas)
            r.Add(new SocioSociedad(f.Id, f.Tercero, f.Nombre, f.Participacion,
                f.Dividendos is int d ? await CuentaAsync(s, d, idSociedadEjercicio) : null,
                f.Retencion is int ret ? await CuentaAsync(s, ret, idSociedadEjercicio) : null));
        return r;
    }

    private static async Task<CuentaSocio> CuentaAsync(ISesionDatos s, int identidad, int idSociedadEjercicio)
    {
        var enEjercicio = await s.ConsultarPrimeroAsync("SELECT Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?",
            f => f.Texto("Codigo")?.Trim(), idSociedadEjercicio, identidad);
        var ultima = enEjercicio ?? await s.ConsultarPrimeroAsync(
            "SELECT c.Codigo FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
            "WHERE c.IdIdentidadContable = ? ORDER BY se.Ejercicio DESC", f => f.Texto("Codigo")?.Trim(), identidad);
        return new CuentaSocio(identidad, enEjercicio, ultima);
    }

    /// <summary>
    /// Saldo (Debe − Haber) de unas cuentas hasta <paramref name="hasta"/> desde los apuntes contabilizados del ejercicio, sin el cierre y sin los
    /// asientos del lote <paramref name="sinLote"/> («motor|proceso|clave»; el propio reparto al corregirlo).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, decimal>> SaldosAsync(ISesionDatos s, int idSociedadEjercicio, DateTime hasta, IReadOnlyCollection<string> cuentas,
        string? sinLote = null)
    {
        var r = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var lista = cuentas.Select(c => c.Trim()).Where(c => c.Length > 0 && c.All(char.IsAsciiDigit)).Distinct().ToList();
        if (lista.Count == 0) return r;
        var excluir = sinLote is null ? "" : "AND (IIF(a.MotorPropietario IS NULL, '', a.MotorPropietario) & '|' & IIF(a.CodigoProceso IS NULL, '', a.CodigoProceso) & '|' & IIF(a.ClaveEjecucion IS NULL, '', a.ClaveEjecucion)) <> ? ";
        var parametros = new List<object?> { idSociedadEjercicio, hasta.Date };
        if (sinLote is not null) parametros.Add(sinLote);
        var filas = await s.ConsultarAsync(
            "SELECT p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS Saldo " +
            "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.FechaContable <= ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3 " +
            $"AND p.CuentaContable IN ({string.Join(", ", lista.Select(c => "'" + c + "'"))}) " + excluir +
            "GROUP BY p.CuentaContable",
            f => ((f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("Saldo") ?? 0m), [.. parametros]);
        foreach (var (cuenta, saldo) in filas) r[cuenta] = r.GetValueOrDefault(cuenta) + saldo;
        return r;
    }

    /// <summary>Configuración de los dividendos del ejercicio (vacía: 19 % y 1130000).</summary>
    public Task<ConfiguracionDividendos> ConfiguracionAsync(int idSociedadEjercicio) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync("SELECT PorcentajeRetencionDividendos, CuentaReservasDividendos FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => ConfiguracionDividendos.Con(f.DecimalNulo("PorcentajeRetencionDividendos"), f.Texto("CuentaReservasDividendos")), idSociedadEjercicio)
        ?? ConfiguracionDividendos.Defecto);

    /// <summary>Ejercicio (IdSociedadEjercicio) de la sociedad para un año; null si no existe.</summary>
    public static async Task<int?> EjercicioAsync(ISesionDatos s, string idSociedad, int anio)
    {
        var filas = await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdSociedadEjercicio"), Anio: f.Texto("Ejercicio")), idSociedad);
        foreach (var (id, texto) in filas)
            if (int.TryParse((texto ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) && a == anio) return id;
        return null;
    }
}
