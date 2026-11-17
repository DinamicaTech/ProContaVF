using System.Globalization;

namespace ProConta.Contabilidad.Amortizacion;

/// <summary>Cada cuánto se graba el asiento de amortización (parámetro del ejercicio; vacío, anual).</summary>
public enum PeriodicidadAmortizacion { Anual, Trimestral, Mensual }

/// <summary>Un periodo de amortización del ejercicio: el año, un trimestre o un mes, con la clave de su lote (AAAA, AAAA-Tn o AAAA-MM).</summary>
public sealed record PeriodoAmortizacion(int Ejercicio, PeriodicidadAmortizacion Periodicidad, int Numero, DateTime Desde, DateTime Hasta)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Clave del lote del motor de asientos: «2026», «2026-T1» o «2026-03».</summary>
    public string Clave => Periodicidad switch
    {
        PeriodicidadAmortizacion.Trimestral => $"{Ejercicio}-T{Numero}",
        PeriodicidadAmortizacion.Mensual => $"{Ejercicio}-{Numero:00}",
        _ => Ejercicio.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Nombre para el operador: «2026», «1T 2026» o «marzo 2026».</summary>
    public string Nombre => Periodicidad switch
    {
        PeriodicidadAmortizacion.Trimestral => $"{Numero}T {Ejercicio}",
        PeriodicidadAmortizacion.Mensual => $"{Es.DateTimeFormat.GetMonthName(Numero)} {Ejercicio}",
        _ => Ejercicio.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Los periodos del ejercicio con esa periodicidad, en orden.</summary>
    public static IReadOnlyList<PeriodoAmortizacion> DelEjercicio(int ejercicio, PeriodicidadAmortizacion periodicidad) => periodicidad switch
    {
        PeriodicidadAmortizacion.Mensual => Enumerable.Range(1, 12)
            .Select(m => new PeriodoAmortizacion(ejercicio, periodicidad, m, new DateTime(ejercicio, m, 1), new DateTime(ejercicio, m, 1).AddMonths(1).AddDays(-1))).ToList(),
        PeriodicidadAmortizacion.Trimestral => Enumerable.Range(1, 4)
            .Select(t => new PeriodoAmortizacion(ejercicio, periodicidad, t, new DateTime(ejercicio, t * 3 - 2, 1), new DateTime(ejercicio, t * 3 - 2, 1).AddMonths(3).AddDays(-1))).ToList(),
        _ => [new PeriodoAmortizacion(ejercicio, PeriodicidadAmortizacion.Anual, 1, new DateTime(ejercicio, 1, 1), new DateTime(ejercicio, 12, 31))],
    };

    /// <summary>Lee el parámetro del ejercicio (ANUAL, TRIMESTRAL o MENSUAL); vacío o desconocido, anual.</summary>
    public static PeriodicidadAmortizacion Leer(string? texto) => (texto ?? "").Trim().ToUpperInvariant() switch
    {
        "TRIMESTRAL" => PeriodicidadAmortizacion.Trimestral,
        "MENSUAL" => PeriodicidadAmortizacion.Mensual,
        _ => PeriodicidadAmortizacion.Anual,
    };

    /// <summary>Texto del parámetro que se guarda en SociedadesEjercicios.PeriodicidadAmortizacion.</summary>
    public static string Codigo(PeriodicidadAmortizacion p) => p.ToString().ToUpperInvariant();
}

/// <summary>
/// Plan de amortización de un activo (PlanesAmortizacionActivo, activos 2026-11-11 18:21): fecha desde la que se amortiza, base
/// amortizable, valor residual, % anual en tanto por uno (0,02 = 2 %) y fecha de baja del activo, si la tiene.
/// </summary>
public sealed record PlanAmortizacion(DateTime FechaInicio, decimal Base, decimal ValorResidual, decimal PorcentajeAnual, DateTime? FechaBaja = null)
{
    /// <summary>Lo máximo que se puede amortizar en toda la vida del activo.</summary>
    public decimal Amortizable => Math.Max(0m, Base - ValorResidual);

    /// <summary>Cuota de un año completo, sin redondear.</summary>
    public decimal CuotaAnual => Base * PorcentajeAnual;
}

/// <summary>
/// Cuota de un activo en un periodo (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59).
/// </summary>
/// <param name="Dias">Días amortizables del año hasta el fin del periodo: desde el 1/1 (o la fecha de inicio del plan, si es posterior) hasta el fin del periodo (o la baja, si es anterior).</param>
/// <param name="DiasAno">Días del año (366 en bisiesto).</param>
/// <param name="HastaFinPeriodo">Lo que toca en el año hasta el fin del periodo: cuota anual × días / días del año, a céntimos.</param>
/// <param name="YaContabilizado">Lo ya cargado en la 68x del activo en el año hasta el fin del periodo, sin el lote de este periodo.</param>
/// <param name="Cuota">Lo que falta para llegar a <see cref="HastaFinPeriodo"/> (nunca negativo).</param>
/// <param name="Acumulada">Amortización acumulada (saldo acreedor de la 28x) hasta el fin del periodo, sin el lote de este periodo.</param>
/// <param name="Pendiente">Lo que queda por amortizar: base − valor residual − acumulada (nunca negativo).</param>
/// <param name="Importe">Lo que se propone: la cuota con tope en lo pendiente.</param>
public sealed record CuotaAmortizacion(int Dias, int DiasAno, decimal HastaFinPeriodo, decimal YaContabilizado, decimal Cuota, decimal Acumulada, decimal Pendiente, decimal Importe)
{
    /// <summary>La cuota se ha recortado porque supera lo pendiente.</summary>
    public bool Topada => Importe < Cuota;

    /// <summary>Ya no queda nada por amortizar.</summary>
    public bool TotalmenteAmortizado => Pendiente == 0m;
}

/// <summary>
/// Cálculo de la amortización sin base de datos (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59). Lo usan el proceso de
/// amortización y la ficha de activos (pestaña Amortización y línea «prevista» de la cuenta de resultados) para que sus cifras coincidan.
/// Por activo y periodo: lo que toca desde el 1/1 hasta el fin del periodo por días reales, menos lo ya contabilizado en el año, con
/// tope en lo pendiente. Así doce cuotas mensuales suman lo mismo que la anual y una amortización hecha a mano no se duplica.
/// </summary>
public static class CalculoAmortizacion
{
    /// <summary>Días del año (366 en bisiesto).</summary>
    public static int DiasAno(int ejercicio) => DateTime.IsLeapYear(ejercicio) ? 366 : 365;

    /// <summary>
    /// Días amortizables del año <paramref name="ejercicio"/> hasta <paramref name="hasta"/>: desde el 1/1 o la fecha de inicio del plan
    /// (la posterior) hasta <paramref name="hasta"/> o la baja (la anterior), ambos incluidos; cero si no se solapan.
    /// </summary>
    public static int Dias(PlanAmortizacion plan, int ejercicio, DateTime hasta)
    {
        var desde = Max(new DateTime(ejercicio, 1, 1), plan.FechaInicio.Date);
        var fin = Min(hasta.Date, new DateTime(ejercicio, 12, 31));
        if (plan.FechaBaja is DateTime baja) fin = Min(fin, baja.Date);
        return fin < desde ? 0 : (fin - desde).Days + 1;
    }

    /// <summary>
    /// Cuota de un activo en un periodo. <paramref name="yaContabilizado"/> es lo cargado en su 68x en el año hasta el fin del periodo y
    /// <paramref name="acumulada"/> el saldo acreedor de su 28x a esa fecha, los dos sin el lote de este periodo.
    /// </summary>
    public static CuotaAmortizacion Calcular(PlanAmortizacion plan, PeriodoAmortizacion periodo, decimal yaContabilizado, decimal acumulada)
    {
        var diasAno = DiasAno(periodo.Ejercicio);
        var dias = Dias(plan, periodo.Ejercicio, periodo.Hasta);
        var hasta = Redondear(plan.CuotaAnual * dias / diasAno);
        var cuota = Math.Max(0m, hasta - yaContabilizado);
        var pendiente = Math.Max(0m, Redondear(plan.Amortizable - acumulada));
        return new CuotaAmortizacion(dias, diasAno, hasta, yaContabilizado, cuota, acumulada, pendiente, Math.Min(cuota, pendiente));
    }

    /// <summary>
    /// Proyección del año completo para la ficha de activos: lo que toca en el ejercicio con la amortización acumulada al empezarlo (sin
    /// nada contabilizado aún en el año).
    /// </summary>
    public static CuotaAmortizacion DelAno(PlanAmortizacion plan, int ejercicio, decimal acumuladaAlEmpezar) =>
        Calcular(plan, PeriodoAmortizacion.DelEjercicio(ejercicio, PeriodicidadAmortizacion.Anual)[0], 0m, acumuladaAlEmpezar);

    /// <summary>
    /// Reparte <paramref name="total"/> en céntimos según los pesos (método del mayor resto), para las subcuentas que comparten varios
    /// activos: la suma de las partes es exactamente el total. Con todos los pesos a cero, a partes iguales.
    /// </summary>
    public static IReadOnlyList<decimal> Repartir(decimal total, IReadOnlyList<decimal> pesos)
    {
        if (pesos.Count == 0) return [];
        if (pesos.Count == 1) return [total];
        var w = pesos.Select(p => Math.Max(0m, p)).ToList();
        var suma = w.Sum();
        if (suma == 0m) { w = pesos.Select(_ => 1m).ToList(); suma = w.Count; }
        var centimos = (long)Math.Round(total * 100m, MidpointRounding.AwayFromZero);
        var exactos = w.Select(x => centimos * x / suma).ToList();
        var partes = exactos.Select(x => (long)Math.Floor(x)).ToList();
        var resto = centimos - partes.Sum();
        foreach (var i in exactos.Select((x, i) => (Fraccion: x - Math.Floor(x), i)).OrderByDescending(x => x.Fraccion).ThenBy(x => x.i).Take((int)Math.Max(0, resto)).Select(x => x.i).ToList())
            partes[i]++;
        return partes.Select(p => p / 100m).ToList();
    }

    public static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
