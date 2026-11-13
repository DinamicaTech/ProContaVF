using System.Text.RegularExpressions;
using ProConta.Datos.Persistencia;

namespace ProConta.Norma43.Conciliacion;

/// <summary>Qué nombre sigue a la frase de la descripción del asiento.</summary>
public enum NombreConceptoN43
{
    /// <summary>El de la contrapartida: el tercero (cliente o proveedor) de la cuenta, o la cuenta.</summary>
    Contrapartida,

    /// <summary>El de la cuenta bancaria (p. ej. «Comisión bancaria La Caixa EUR»).</summary>
    Banco,
}

/// <summary>
/// Regla de la tabla ConceptosN43 (norma43/conciliacion 2026-11-13 10:50): criterios (null = cualquiera) y el tipo y la frase que da.
/// </summary>
public sealed record ConceptoN43(
    int IdConceptoN43,
    string? CodigoConcepto,
    string? Sentido,
    string? Entidad,
    string? PatronTexto,
    string? PrefijoCuenta,
    int Prioridad,
    string Concepto,
    string ConceptoReducido,
    string Frase,
    NombreConceptoN43 NombreDe = NombreConceptoN43.Contrapartida)
{
    /// <summary>Número de criterios informados: a igual prioridad gana la regla más concreta.</summary>
    public int Criterios => new[] { CodigoConcepto, Sentido, Entidad, PatronTexto, PrefijoCuenta }.Count(c => !string.IsNullOrWhiteSpace(c));
}

/// <summary>Datos de un movimiento con los que se elige su tipo.</summary>
/// <param name="CuentaDestino">Cuenta de la contrapartida (la imputación), si se conoce: activa las reglas por cuenta.</param>
public sealed record MovimientoConceptoN43(string? CodigoConcepto, string Sentido, string? Entidad, string? Texto, string? CuentaDestino = null);

/// <summary>
/// Elige el tipo de un movimiento N43 con las reglas de ConceptosN43: de las que encajan (código, sentido, entidad, texto con * y prefijo
/// de la cuenta de destino), la de mayor prioridad y, a igualdad, la más concreta. Sin ninguna, no hay tipo: nunca se supone uno.
/// </summary>
public static class ReglasConceptoN43
{
    /// <summary>Frase si ninguna regla encaja: un cargo es un pago y un abono, un cobro.</summary>
    public static string FraseSinTipo(string sentido) => sentido == ServicioConciliacionN43.Abono ? "Cobro de" : "Pago a";

    public static ConceptoN43? Elegir(IEnumerable<ConceptoN43> reglas, MovimientoConceptoN43 m) =>
        reglas.Where(r => Encaja(r, m)).OrderByDescending(r => r.Prioridad).ThenByDescending(r => r.Criterios).ThenBy(r => r.IdConceptoN43).FirstOrDefault();

    public static bool Encaja(ConceptoN43 r, MovimientoConceptoN43 m) =>
        Igual(r.CodigoConcepto, m.CodigoConcepto) && Igual(r.Sentido, m.Sentido) && Igual(r.Entidad, m.Entidad)
        && (string.IsNullOrWhiteSpace(r.PatronTexto) || Patron(r.PatronTexto).IsMatch(m.Texto ?? ""))
        && (string.IsNullOrWhiteSpace(r.PrefijoCuenta) || (m.CuentaDestino?.Trim().StartsWith(r.PrefijoCuenta.Trim(), StringComparison.Ordinal) ?? false));

    private static bool Igual(string? regla, string? valor) =>
        string.IsNullOrWhiteSpace(regla) || string.Equals(regla.Trim(), valor?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>El patrón con * como comodín, sin distinguir mayúsculas; sin * al principio o al final, el texto debe empezar o acabar así.</summary>
    private static Regex Patron(string patron) =>
        new("^" + string.Join(".*", patron.Trim().Split('*').Select(Regex.Escape)) + "$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Lee las reglas activas de ConceptosN43 (ninguna si la tabla aún no existe).</summary>
    public static Task<IReadOnlyList<ConceptoN43>> LeerAsync(IBaseDatos bd) => bd.EjecutarAsync(async s =>
    {
        try
        {
            return (IReadOnlyList<ConceptoN43>)await s.ConsultarAsync(
                "SELECT IdConceptoN43, CodigoConcepto, Sentido, Entidad, PatronTexto, PrefijoCuenta, Prioridad, Concepto, ConceptoReducido, Frase, NombreDe " +
                "FROM ConceptosN43 WHERE Activo = True",
                f => new ConceptoN43(f.Entero("IdConceptoN43"), Limpio(f.Texto("CodigoConcepto")), Limpio(f.Texto("Sentido")), Limpio(f.Texto("Entidad")),
                    Limpio(f.Texto("PatronTexto")), Limpio(f.Texto("PrefijoCuenta")), f.Entero("Prioridad"), (f.Texto("Concepto") ?? "").Trim(),
                    (f.Texto("ConceptoReducido") ?? "").Trim(), (f.Texto("Frase") ?? "").Trim(),
                    string.Equals(Limpio(f.Texto("NombreDe")), "BANCO", StringComparison.OrdinalIgnoreCase) ? NombreConceptoN43.Banco : NombreConceptoN43.Contrapartida));
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            return [];
        }
    });

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
