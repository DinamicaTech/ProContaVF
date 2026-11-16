using System.Globalization;

namespace ProConta.Contabilidad.Dividendos;

/// <summary>
/// Configuración de los dividendos del ejercicio (organizacion/sociedades-ejercicios, esquema 62): % de retención y cuenta de reservas de la que
/// salen; vacías, 19 % y 1130000.
/// </summary>
public sealed record ConfiguracionDividendos(decimal PorcentajeRetencion, string CuentaReservas)
{
    public const decimal RetencionDefecto = 19m;
    public const string ReservasDefecto = "1130000";

    public static ConfiguracionDividendos Defecto { get; } = new(RetencionDefecto, ReservasDefecto);

    public static ConfiguracionDividendos Con(decimal? porcentaje, string? cuenta) => new(
        porcentaje is decimal p ? p : RetencionDefecto,
        string.IsNullOrWhiteSpace(cuenta) ? ReservasDefecto : cuenta.Trim());
}

/// <summary>Un socio en el cálculo del reparto: su nombre, su %, sus dos subcuentas y lo que percibe (neto).</summary>
public sealed record SocioCalculoDividendos(int IdSocio, string Nombre, decimal Participacion, string CuentaDividendos, string CuentaRetencion, decimal Neto);

/// <summary>Resultado de un socio: neto, bruto y retención.</summary>
public sealed record ResultadoSocioDividendos(int IdSocio, string Nombre, decimal Participacion, string CuentaDividendos, string CuentaRetencion,
    decimal Neto, decimal Bruto, decimal Retencion);

/// <summary>Una línea del asiento propuesto.</summary>
public sealed record LineaDividendos(string Cuenta, decimal Debe, decimal Haber, string Concepto);

/// <summary>Propuesta de reparto: los socios con sus importes, el asiento cuadrado y los avisos.</summary>
public sealed record PropuestaDividendos(DateTime Fecha, decimal PorcentajeRetencion, string CuentaReservas, IReadOnlyList<ResultadoSocioDividendos> Socios,
    IReadOnlyList<LineaDividendos> Lineas, IReadOnlyList<string> Avisos)
{
    public decimal Neto => Socios.Sum(s => s.Neto);
    public decimal Bruto => Socios.Sum(s => s.Bruto);
    public decimal Retencion => Socios.Sum(s => s.Retencion);
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
}

/// <summary>
/// Cálculo del reparto de dividendos, sin BD (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55). Por socio:
/// bruto = neto / (1 − retención) redondeado a dos decimales (al alza en el medio céntimo, AwayFromZero) y retención = bruto − neto; el
/// total es la suma de los socios, así que cuadra al céntimo. Asiento con la fecha del reparto: la cuenta de reservas al Debe por el bruto
/// total y, por socio, su 526 al Haber por el bruto, su 526 al Debe por la retención y su 4751 al Haber por la retención.
/// </summary>
public static class CalculoDividendos
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Bruto de un neto con la retención en porcentaje (19 = 19 %), al céntimo.</summary>
    public static decimal Bruto(decimal neto, decimal porcentajeRetencion)
    {
        if (porcentajeRetencion is < 0m or >= 100m) throw new ArgumentOutOfRangeException(nameof(porcentajeRetencion), "La retención va de 0 a menos de 100 %.");
        return Math.Round(neto / (1m - porcentajeRetencion / 100m), 2, MidpointRounding.AwayFromZero);
    }

    public static string Descripcion(DateTime fecha) => $"Reparto de dividendos {fecha.ToString("dd/MM/yyyy", Es)}";

    public static string ConceptoBruto(DateTime fecha, string socio) => $"{Descripcion(fecha)} {socio}".Trim();

    public static string ConceptoRetencion(DateTime fecha, string socio) => $"Retención dividendos {fecha.ToString("dd/MM/yyyy", Es)} {socio}".Trim();

    /// <summary>
    /// Calcula los importes y el asiento. Los socios con neto cero no salen. <paramref name="saldoReservas"/>: saldo acreedor de la cuenta de
    /// reservas a la fecha (null: no se comprueba); <paramref name="sumaParticipaciones"/>: suma de los % de los socios activos (null: no se comprueba).
    /// </summary>
    public static PropuestaDividendos Calcular(DateTime fecha, ConfiguracionDividendos config, IReadOnlyList<SocioCalculoDividendos> socios,
        decimal? saldoReservas = null, decimal? sumaParticipaciones = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(socios);
        var r = config.PorcentajeRetencion;
        var resultados = socios.Where(s => s.Neto != 0m).Select(s =>
        {
            var neto = Math.Round(s.Neto, 2, MidpointRounding.AwayFromZero);
            var bruto = Bruto(neto, r);
            return new ResultadoSocioDividendos(s.IdSocio, s.Nombre, s.Participacion, s.CuentaDividendos, s.CuentaRetencion, neto, bruto, bruto - neto);
        }).ToList();
        var lineas = new List<LineaDividendos>();
        var avisos = new List<string>();
        if (resultados.Count > 0)
        {
            lineas.Add(new(config.CuentaReservas, resultados.Sum(x => x.Bruto), 0m, Descripcion(fecha)));
            foreach (var s in resultados)
            {
                lineas.Add(new(s.CuentaDividendos, 0m, s.Bruto, ConceptoBruto(fecha, s.Nombre)));
                if (s.Retencion != 0m)
                {
                    lineas.Add(new(s.CuentaDividendos, s.Retencion, 0m, ConceptoRetencion(fecha, s.Nombre)));
                    lineas.Add(new(s.CuentaRetencion, 0m, s.Retencion, ConceptoRetencion(fecha, s.Nombre)));
                }
            }
        }
        if (resultados.Any(x => x.Neto < 0m))
            avisos.Add("Algún socio tiene un neto negativo: el reparto le cargaría dividendos en lugar de abonárselos. Revisa sus importes.");
        var bruto = resultados.Sum(x => x.Bruto);
        if (saldoReservas is decimal saldo && bruto > saldo)
            avisos.Add($"La cuenta de reservas {config.CuentaReservas} tiene {Euros(saldo)} a la fecha y el reparto saca {Euros(bruto)}: quedaría con saldo deudor. " +
                "Revisa el importe o la cuenta de reservas del ejercicio (Parámetros y tablas › Ejercicios).");
        if (sumaParticipaciones is decimal suma && suma != 100m)
            avisos.Add($"Las participaciones de los socios activos suman {suma.ToString("0.####", Es)} % y no 100 %. Revísalas en la ficha de cada socio (Roles › Socio).");
        return new PropuestaDividendos(fecha.Date, r, config.CuentaReservas, resultados, lineas, avisos);
    }

    /// <summary>
    /// Reparte un importe en céntimos en proporción a unos pesos (los % de participación), por el método del resto mayor: la suma de las partes es
    /// exactamente el total. Devuelve una parte por peso, en el mismo orden (cero si no le toca nada). Sin pesos o con suma cero, todo a partes iguales.
    /// </summary>
    public static IReadOnlyList<decimal> Repartir(decimal total, IReadOnlyList<decimal> pesos)
    {
        ArgumentNullException.ThrowIfNull(pesos);
        if (pesos.Count == 0) return [];
        var usados = pesos.Sum() is var suma && suma > 0m && pesos.All(p => p >= 0m) ? pesos : pesos.Select(_ => 1m).ToList();
        var sumaUsada = usados.Sum();
        var centimos = (long)Math.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var partes = usados.Select((p, i) => { var exacto = centimos * p / sumaUsada; var b = Math.Truncate(exacto); return (Base: (long)b, Resto: exacto - b, i); }).ToList();
        var falta = centimos - partes.Sum(p => p.Base);
        var orden = (falta >= 0 ? partes.OrderByDescending(p => p.Resto) : partes.OrderBy(p => p.Resto)).ThenBy(p => p.i).Select(p => p.i).ToList();
        var extra = new long[partes.Count];
        for (var k = 0; k < Math.Abs(falta); k++) extra[orden[k % orden.Count]] += Math.Sign(falta);
        return partes.Select(p => (p.Base + extra[p.i]) / 100m).ToList();
    }

    private static string Euros(decimal v) => v.ToString("#,##0.00", Es) + " €";
}
