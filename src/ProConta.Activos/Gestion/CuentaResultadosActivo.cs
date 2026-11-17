using ProConta.Contabilidad.Amortizacion;
using ProConta.Contabilidad.Impuestos;

namespace ProConta.Activos.Gestion;

/// <summary>
/// Un apunte del ejercicio leído para la cuenta de resultados: cuenta, fecha, importes, centro de coste, tipo de asiento (0 normal, 1 apertura,
/// 2 regularización, 3 cierre) y si el asiento está en firme.
/// </summary>
public sealed record ApunteResultado(string Cuenta, DateTime Fecha, decimal Debe, decimal Haber, int? IdCentroCoste, int TipoAsiento = 0, bool EnFirme = true);

/// <summary>
/// Qué apuntes son de un activo: los de sus subcuentas en el ejercicio y los que llevan su centro de coste; si el centro lo comparte con otro activo
/// (activos migrados), solo sus subcuentas (activos 2026-11-17 18:50).
/// </summary>
public sealed record CriterioActivo(IReadOnlySet<string> Subcuentas, int? IdCentroCoste, bool CentroCompartido);

/// <summary>Clase de fila de la cuenta de resultados.</summary>
public enum TipoFilaResultado
{
    /// <summary>Una subcuenta con sus apuntes del ejercicio.</summary>
    Subcuenta,
    /// <summary>«Amortización prevista (sin contabilizar)»: lo que propondría el proceso para el año, en diciembre.</summary>
    AmortizacionPrevista,
    /// <summary>«Parte de los gastos generales de la sociedad», con el interruptor encendido.</summary>
    GastosGenerales,
}

/// <summary>Una fila: subcuenta (o fila calculada), nombre, si es ingreso, el importe de cada mes (enero a diciembre) y su total.</summary>
public sealed record FilaResultado(string Cuenta, string Nombre, bool EsIngreso, IReadOnlyList<decimal> Meses, TipoFilaResultado Tipo = TipoFilaResultado.Subcuenta)
{
    public decimal Total => Meses.Sum();
    public bool EsCalculada => Tipo != TipoFilaResultado.Subcuenta;
}

/// <summary>Cuenta de resultados de un activo en un ejercicio: ingresos y gastos por subcuenta y mes, sus totales y el resultado.</summary>
public sealed record CuentaResultados(int Ejercicio, IReadOnlyList<FilaResultado> Filas)
{
    public IEnumerable<FilaResultado> FilasIngresos => Filas.Where(f => f.EsIngreso);
    public IEnumerable<FilaResultado> FilasGastos => Filas.Where(f => !f.EsIngreso);
    public decimal Ingresos => FilasIngresos.Sum(f => f.Total);
    public decimal Gastos => FilasGastos.Sum(f => f.Total);
    public decimal Resultado => Ingresos - Gastos;

    /// <summary>Rentabilidad sobre el coste (resultado / coste, en tanto por uno); null sin coste.</summary>
    public decimal? Rentabilidad(decimal coste) => coste > 0m ? Resultado / coste : null;

    public decimal IngresosMes(int mes) => FilasIngresos.Sum(f => f.Meses[mes]);
    public decimal GastosMes(int mes) => FilasGastos.Sum(f => f.Meses[mes]);

    /// <summary>Gastos del mes sin la amortización (la de las subcuentas 68 y la prevista), para el gráfico de barras.</summary>
    public decimal GastosMesSinAmortizacion(int mes) => FilasGastos.Where(f => f.Tipo != TipoFilaResultado.AmortizacionPrevista && !CalculoCuentaResultados.EsAmortizacion(f.Cuenta))
        .Sum(f => f.Meses[mes]);

    /// <summary>Ingresos ordinarios del activo (cuentas 70 a 75) en el año.</summary>
    public decimal IngresosOrdinarios => FilasIngresos.Where(f => CalculoCuentaResultados.EsIngresoOrdinario(f.Cuenta)).Sum(f => f.Total);
}

/// <summary>Una fila comparada con el ejercicio anterior: importe del año, del anterior, diferencia y % sobre los ingresos del año.</summary>
public sealed record FilaComparada(string Cuenta, string Nombre, bool EsIngreso, decimal Actual, decimal? Anterior, TipoFilaResultado Tipo, IReadOnlyList<decimal> Meses)
{
    public decimal? Diferencia => Anterior is decimal a ? Actual - a : null;
}

/// <summary>
/// Cuenta de resultados de un activo sin base de datos (activos 2026-11-17 18:50). Entran los apuntes de los grupos 6 y 7 de asientos en firme que
/// no son de regularización ni de cierre, sin las cuentas del impuesto (630, 633 y 638), que están en una subcuenta del activo o llevan su centro
/// (si el centro es compartido, solo sus subcuentas). Una fila por subcuenta: ingresos (grupo 7, Haber − Debe) y gastos (grupo 6, Debe − Haber).
/// Redondeo: los importes de los apuntes ya vienen en céntimos y solo se suman; la parte de gastos generales se redondea a céntimos una vez
/// (al año) y su reparto por meses suma exactamente lo del año (resto mayor al céntimo).
/// </summary>
public static class CalculoCuentaResultados
{
    public const string TextoPrevista = "Amortización prevista (sin contabilizar)";
    public const string TextoGenerales = "Parte de los gastos generales de la sociedad";
    private const int TipoRegularizacion = 2, TipoCierre = 3;

    /// <summary>Cuentas 70 a 75: los ingresos ordinarios con los que se reparten los gastos generales (como en el Impuesto sobre Sociedades).</summary>
    public static bool EsIngresoOrdinario(string cuenta) => cuenta.Length >= 2 && cuenta[0] == '7' && cuenta[1] is >= '0' and <= '5';

    public static bool EsAmortizacion(string cuenta) => cuenta.StartsWith("68", StringComparison.Ordinal);

    /// <summary>Si el apunte cuenta para el activo.</summary>
    public static bool Entra(ApunteResultado a, CriterioActivo c)
    {
        if (!a.EnFirme || a.TipoAsiento is TipoRegularizacion or TipoCierre) return false;
        var cuenta = a.Cuenta.Trim();
        if (!CalculoImpuestoSociedades.EntraEnResultado(cuenta)) return false;
        if (c.Subcuentas.Contains(cuenta)) return true;
        return !c.CentroCompartido && c.IdCentroCoste is int centro && a.IdCentroCoste == centro;
    }

    /// <summary>
    /// La cuenta de resultados del activo en <paramref name="ejercicio"/>. <paramref name="amortizacionPrevista"/> es lo que falta por contabilizar de
    /// la amortización del año (fila en diciembre si es mayor que cero); con <paramref name="generales"/>, añade su parte de los gastos generales.
    /// </summary>
    public static CuentaResultados Calcular(int ejercicio, IEnumerable<ApunteResultado> apuntes, CriterioActivo criterio, Func<string, string> nombre,
        decimal amortizacionPrevista = 0m, GastosGeneralesSociedad? generales = null)
    {
        var filas = apuntes.Where(a => Entra(a, criterio))
            .GroupBy(a => a.Cuenta.Trim(), StringComparer.Ordinal)
            .Select(g =>
            {
                var ingreso = g.Key[0] == '7';
                var meses = new decimal[12];
                foreach (var a in g) meses[Math.Clamp(a.Fecha.Month, 1, 12) - 1] += ingreso ? a.Haber - a.Debe : a.Debe - a.Haber;
                return new FilaResultado(g.Key, nombre(g.Key), ingreso, meses);
            })
            .Where(f => f.Meses.Any(m => m != 0m))
            .OrderBy(f => f.EsIngreso ? 0 : 1).ThenBy(f => f.Cuenta, StringComparer.Ordinal)
            .ToList();
        if (amortizacionPrevista > 0m)
        {
            var meses = new decimal[12];
            meses[11] = amortizacionPrevista;
            filas.Add(new FilaResultado("", TextoPrevista, false, meses, TipoFilaResultado.AmortizacionPrevista));
        }
        if (generales is not null)
        {
            var parcial = new CuentaResultados(ejercicio, filas);
            var ordinariosMes = Enumerable.Range(0, 12).Select(m => parcial.FilasIngresos.Where(f => EsIngresoOrdinario(f.Cuenta)).Sum(f => f.Meses[m])).ToList();
            var parte = ParteGastosGenerales(generales, parcial.IngresosOrdinarios);
            if (parte != 0m) filas.Add(new FilaResultado("", TextoGenerales, false, RepartirPorMeses(parte, ordinariosMes), TipoFilaResultado.GastosGenerales));
        }
        return new CuentaResultados(ejercicio, filas);
    }

    /// <summary>
    /// Parte de los gastos generales que carga el activo: gastos generales × ingresos ordinarios del activo / ingresos ordinarios de toda la sociedad,
    /// redondeada a céntimos (al alza en el medio). Un activo sin ingresos ordinarios (o una sociedad sin ellos) no recibe nada.
    /// </summary>
    public static decimal ParteGastosGenerales(GastosGeneralesSociedad g, decimal ingresosOrdinariosActivo) =>
        ingresosOrdinariosActivo <= 0m || g.IngresosOrdinarios <= 0m ? 0m
            : Math.Round(g.GastosGenerales * ingresosOrdinariosActivo / g.IngresosOrdinarios, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Reparte la parte anual por meses en proporción a los ingresos ordinarios del activo en cada mes (los meses negativos cuentan como cero), con
    /// el resto mayor al céntimo: los doce meses suman exactamente el total.
    /// </summary>
    public static IReadOnlyList<decimal> RepartirPorMeses(decimal total, IReadOnlyList<decimal> ingresosOrdinariosMes)
    {
        if (total == 0m) return new decimal[12];
        var signo = total < 0m ? -1m : 1m;
        var partes = CalculoAmortizacion.Repartir(Math.Abs(total), ingresosOrdinariosMes);
        return partes.Select(p => p * signo).ToList();
    }

    /// <summary>
    /// Une el año con el anterior por cuenta (las filas calculadas, por su texto), en el orden del año: primero las que tiene el año y después las que
    /// solo tenía el anterior.
    /// </summary>
    public static IReadOnlyList<FilaComparada> Comparar(CuentaResultados actual, CuentaResultados? anterior)
    {
        static string Clave(FilaResultado f) => (f.EsIngreso ? "I|" : "G|") + (f.EsCalculada ? f.Tipo + "|" : "") + f.Cuenta;
        var previas = anterior?.Filas.GroupBy(Clave, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                      ?? new Dictionary<string, FilaResultado>(StringComparer.Ordinal);
        var r = new List<FilaComparada>();
        var usadas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in actual.Filas)
        {
            var clave = Clave(f);
            usadas.Add(clave);
            r.Add(new FilaComparada(f.Cuenta, f.Nombre, f.EsIngreso, f.Total, anterior is null ? null : previas.TryGetValue(clave, out var p) ? p.Total : 0m, f.Tipo, f.Meses));
        }
        if (anterior is not null)
            foreach (var p in anterior.Filas.Where(p => !usadas.Contains(Clave(p))))
                r.Add(new FilaComparada(p.Cuenta, p.Nombre, p.EsIngreso, 0m, p.Total, p.Tipo, new decimal[12]));
        return r.OrderBy(f => f.EsIngreso ? 0 : 1).ThenBy(f => f.Tipo).ThenBy(f => f.Cuenta, StringComparer.Ordinal).ToList();
    }
}
