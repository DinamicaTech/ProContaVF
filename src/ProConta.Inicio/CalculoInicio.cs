namespace ProConta.Inicio;

/// <summary>
/// Definiciones de cálculo de la pantalla de inicio (pantalla-de-inicio, decididas con el propietario el 2026-11-15). Funciones
/// puras, sin BD: fecha de corte, misma fecha del comparado, proyección de cierre, series por mes, saldos por grupos de
/// cuentas, antigüedad y gastos por naturaleza. Importes en decimal; nada se redondea aquí (se redondea a céntimos al mostrar).
/// </summary>
public static class CalculoInicio
{
    public const byte TipoApertura = 1;
    public const byte TipoRegularizacion = 2;
    public const byte TipoCierre = 3;

    /// <summary>Hoy si el ejercicio es el año en curso; si no, el 31/12 de ese ejercicio.</summary>
    public static DateTime FechaCorte(int ejercicio, DateTime hoy) => hoy.Year == ejercicio ? hoy.Date : new DateTime(ejercicio, 12, 31);

    /// <summary>El mismo día y mes de la fecha de corte en otro ejercicio (el 29/2 pasa al 28/2 si ese año no es bisiesto).</summary>
    public static DateTime MismaFecha(DateTime corte, int ejercicio) =>
        new(ejercicio, corte.Month, Math.Min(corte.Day, DateTime.DaysInMonth(ejercicio, corte.Month)));

    /// <summary>La fecha de corte es el último día del año: el ejercicio está terminado.</summary>
    public static bool Terminado(DateTime corte) => corte.Month == 12 && corte.Day == 31;

    /// <summary>
    /// Proyección de cierre = total del comparado completo × (acumulado actual / acumulado del comparado a la misma fecha).
    /// Null sin comparado, con divisor cero o con el ejercicio ya terminado.
    /// </summary>
    public static decimal? Proyeccion(decimal? totalComparado, decimal acumuladoActual, decimal? acumuladoComparado, bool terminado) =>
        terminado || totalComparado is not decimal total || acumuladoComparado is not decimal divisor || divisor == 0
            ? null
            : total * (acumuladoActual / divisor);

    /// <summary>
    /// Lo que falta de ejercicio repartido por meses con la misma razón (acumulado actual / acumulado comparado): en el mes de la
    /// fecha de corte, la parte del comparado posterior a la misma fecha; en los meses siguientes, el mes completo del comparado.
    /// Sumado al acumulado real da exactamente la proyección de cierre. Null en los meses sin proyección.
    /// </summary>
    public static decimal?[] ProyeccionPorMes(IReadOnlyList<decimal> comparadoMensual, decimal comparadoMesCorteHastaMismaFecha, decimal razon, DateTime corte)
    {
        var r = new decimal?[12];
        for (var m = corte.Month; m <= 12; m++)
        {
            var resto = m == corte.Month ? comparadoMensual[m - 1] - comparadoMesCorteHastaMismaFecha : comparadoMensual[m - 1];
            r[m - 1] = resto * razon;
        }
        return r;
    }

    /// <summary>Suma por mes de unos importes fechados del año; los meses posteriores a la fecha de corte son null (sin dato).</summary>
    public static decimal?[] PorMes(IEnumerable<(DateTime Fecha, decimal Importe)> importes, DateTime corte)
    {
        var r = new decimal?[12];
        for (var m = 1; m <= corte.Month; m++) r[m - 1] = 0m;
        foreach (var (f, v) in importes)
            if (f.Year == corte.Year && f.Date <= corte) r[f.Month - 1] = (r[f.Month - 1] ?? 0) + v;
        return r;
    }

    /// <summary>Suma por mes del año completo (para el ejercicio comparado).</summary>
    public static decimal[] PorMesCompleto(IEnumerable<(DateTime Fecha, decimal Importe)> importes, int anio)
    {
        var r = new decimal[12];
        foreach (var (f, v) in importes)
            if (f.Year == anio) r[f.Month - 1] += v;
        return r;
    }

    /// <summary>Acumulado de una serie mensual (los nulos quedan nulos y no cortan la suma de los siguientes).</summary>
    public static decimal?[] Acumulado(IReadOnlyList<decimal?> mensual)
    {
        var r = new decimal?[mensual.Count];
        var suma = 0m;
        for (var i = 0; i < mensual.Count; i++)
        {
            if (mensual[i] is not decimal v) continue;
            suma += v;
            r[i] = suma;
        }
        return r;
    }

    // ------------------------------------------------------------ grupos de cuentas

    /// <summary>Cuentas de ingresos del resultado: grupo 7.</summary>
    public static bool EsIngreso(string cuenta) => cuenta.StartsWith('7');

    /// <summary>Cuentas de gastos del resultado: grupo 6 sin la 630 (impuesto sobre beneficios).</summary>
    public static bool EsGasto(string cuenta) => cuenta.StartsWith('6') && !cuenta.StartsWith("630", StringComparison.Ordinal);

    /// <summary>Clientes: subcuentas de la 430 a la 436.</summary>
    public static bool EsCliente(string cuenta) => EnRango(cuenta, "430", "436");

    /// <summary>Proveedores y acreedores: subcuentas de la 400 a la 410.</summary>
    public static bool EsProveedor(string cuenta) => EnRango(cuenta, "400", "410");

    private static bool EnRango(string cuenta, string desde, string hasta) =>
        cuenta.Length >= 3 && string.CompareOrdinal(cuenta[..3], desde) >= 0 && string.CompareOrdinal(cuenta[..3], hasta) <= 0;

    /// <summary>Movimientos que cuentan para el resultado: sin regularización ni cierre (mismo criterio que contabilidad/balances).</summary>
    public static bool EntraEnResultado(MovimientoDia m) => m.TipoAsiento is not (TipoRegularizacion or TipoCierre);

    /// <summary>Resultado antes de impuestos hasta una fecha: grupo 7 menos grupo 6 sin la 630.</summary>
    public static decimal Resultado(IEnumerable<MovimientoDia> movimientos, DateTime hasta) =>
        movimientos.Where(m => m.Fecha <= hasta && EntraEnResultado(m) && (EsIngreso(m.Cuenta) || EsGasto(m.Cuenta))).Sum(m => m.Haber - m.Debe);

    /// <summary>
    /// EBITDA hasta una fecha: grupos 7 y 6 con los mismos criterios de asientos que el resultado, sin el impuesto (630), las
    /// amortizaciones (68), los deterioros y provisiones (69 y 79) ni los resultados financieros (66 y 76). Definición de
    /// informes/ratios (<see cref="ProConta.Contabilidad.Ratios.CalculoRatios.EsEbitda"/>).
    /// </summary>
    public static decimal Ebitda(IEnumerable<MovimientoDia> movimientos, DateTime hasta) =>
        movimientos.Where(m => m.Fecha <= hasta && EntraEnResultado(m) && ProConta.Contabilidad.Ratios.CalculoRatios.EsEbitda(m.Cuenta)).Sum(m => m.Haber - m.Debe);

    /// <summary>Los movimientos de Inicio como los usan los ratios.</summary>
    public static IReadOnlyList<ProConta.Contabilidad.Ratios.MovimientoRatio> ParaRatios(IEnumerable<MovimientoDia> movimientos) =>
        movimientos.Select(m => new ProConta.Contabilidad.Ratios.MovimientoRatio(m.Fecha, m.Cuenta, m.TipoAsiento, m.Debe, m.Haber)).ToList();

    /// <summary>Saldo (Debe − Haber) de las cuentas que cumplen el filtro hasta una fecha, con la apertura y sin el cierre.</summary>
    public static decimal Saldo(IEnumerable<MovimientoDia> movimientos, Func<string, bool> cuentas, DateTime hasta) =>
        movimientos.Where(m => m.Fecha <= hasta && m.TipoAsiento != TipoCierre && cuentas(m.Cuenta)).Sum(m => m.Saldo);

    /// <summary>Saldo de cada subcuenta que cumple el filtro hasta una fecha.</summary>
    public static Dictionary<string, decimal> SaldosPorCuenta(IEnumerable<MovimientoDia> movimientos, Func<string, bool> cuentas, DateTime hasta)
    {
        var r = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var m in movimientos)
            if (m.Fecha <= hasta && m.TipoAsiento != TipoCierre && cuentas(m.Cuenta))
                r[m.Cuenta] = r.GetValueOrDefault(m.Cuenta) + m.Saldo;
        return r;
    }

    /// <summary>Pendiente de cobro: suma de los saldos deudores de las subcuentas de clientes (los acreedores no restan).</summary>
    public static decimal PendienteCobro(IEnumerable<MovimientoDia> movimientos, DateTime hasta) =>
        SaldosPorCuenta(movimientos, EsCliente, hasta).Values.Where(s => s > 0).Sum();

    /// <summary>Pendiente de pago: suma de los saldos acreedores de las subcuentas de proveedores y acreedores, en positivo.</summary>
    public static decimal PendientePago(IEnumerable<MovimientoDia> movimientos, DateTime hasta) =>
        -SaldosPorCuenta(movimientos, EsProveedor, hasta).Values.Where(s => s < 0).Sum();

    /// <summary>Último día de un mes, o la fecha de corte si es el mes del corte.</summary>
    public static DateTime FinDeMes(int anio, int mes, DateTime corte)
    {
        var fin = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
        return fin > corte && anio == corte.Year && mes == corte.Month ? corte : fin;
    }

    /// <summary>Evolución de un saldo a fin de cada mes del ejercicio hasta la fecha de corte (null después).</summary>
    public static decimal?[] SaldoFinDeMes(IReadOnlyList<MovimientoDia> movimientos, Func<IReadOnlyList<MovimientoDia>, DateTime, decimal> saldo, DateTime corte)
    {
        var r = new decimal?[12];
        for (var m = 1; m <= corte.Month; m++) r[m - 1] = saldo(movimientos, FinDeMes(corte.Year, m, corte));
        return r;
    }

    // ------------------------------------------------------------ IVA

    /// <summary>Primer y último día de un trimestre.</summary>
    public static (DateTime Desde, DateTime Hasta) Trimestre(int anio, int trimestre)
    {
        var desde = new DateTime(anio, (trimestre - 1) * 3 + 1, 1);
        return (desde, desde.AddMonths(3).AddDays(-1));
    }

    /// <summary>Trimestre de una fecha (1 a 4).</summary>
    public static int TrimestreDe(DateTime fecha) => (fecha.Month - 1) / 3 + 1;

    /// <summary>
    /// Fracción deducible del IVA soportado de una subcuenta 472 según la actividad dueña de su serie (por ejemplo 4728): 0 si la
    /// actividad no es deducible, la prorrata del año anterior si es por prorrata (100 % si no está informada) y 1 en los demás
    /// casos (deducible o sin actividad, cuentas generales). La deducibilidad es la vigente en <paramref name="fecha"/>, la del apunte
    /// (contabilidad/procesos/cambio-deduccion-actividad); sin fecha, la anterior a cualquier cambio.
    /// </summary>
    public static decimal FraccionDeducible(string cuenta, IReadOnlyList<ActividadInicio> actividades, decimal? prorrata, DateTime? fecha = null)
    {
        var actividad = actividades.Where(a => a.Serie is { Length: > 3 } s && cuenta.StartsWith(s, StringComparison.Ordinal))
            .OrderByDescending(a => a.Serie!.Length).FirstOrDefault();
        return (actividad is null ? (byte?)null : fecha is DateTime f ? actividad.DeducibleEn(f) : actividad.Deducible) switch
        {
            0 => 0m,
            1 => prorrata is decimal p && p >= 0 && p <= 100 ? p / 100m : 1m,
            _ => 1m,
        };
    }

    /// <summary>Repercutido (477, Haber − Debe) y soportado deducible (472, Debe − Haber) entre dos fechas.</summary>
    public static (decimal Repercutido, decimal Soportado) Iva(IEnumerable<ApunteIva> apuntes, DateTime desde, DateTime hasta, IReadOnlyList<ActividadInicio> actividades, decimal? prorrata)
    {
        decimal rep = 0, sop = 0;
        foreach (var a in apuntes)
        {
            if (a.Fecha < desde || a.Fecha > hasta) continue;
            if (a.Cuenta.StartsWith("477", StringComparison.Ordinal)) rep += a.Haber - a.Debe;
            else if (a.Cuenta.StartsWith("472", StringComparison.Ordinal)) sop += (a.Debe - a.Haber) * FraccionDeducible(a.Cuenta, actividades, prorrata, a.Fecha);
        }
        return (rep, sop);
    }

    // ------------------------------------------------------------ antigüedad de lo pendiente de cobro

    public static readonly IReadOnlyList<string> Tramos = ["No vencido", "1 a 30 días", "31 a 60 días", "61 a 90 días", "Más de 90 días"];

    /// <summary>Tramo (índice en <see cref="Tramos"/>) de unos días de antigüedad: 0 o menos, no vencido.</summary>
    public static int Tramo(int dias) => dias <= 0 ? 0 : dias <= 30 ? 1 : dias <= 60 ? 2 : dias <= 90 ? 3 : 4;

    /// <summary>
    /// Reparte el saldo deudor de cada cliente entre sus cargos (apuntes al Debe) del más reciente al más antiguo —los cobros
    /// saldan primero lo más antiguo— y clasifica cada parte por la antigüedad de su vencimiento a la fecha de corte o, si el
    /// apunte no tiene vencimiento, de su fecha. Los clientes con saldo acreedor o cero no cuentan.
    /// </summary>
    public static (decimal[] PorTramo, Dictionary<string, (decimal Saldo, decimal MasDe30)> PorCliente) Antiguedad(IEnumerable<ApunteCartera> apuntes, DateTime corte)
    {
        var porTramo = new decimal[Tramos.Count];
        var porCliente = new Dictionary<string, (decimal, decimal)>(StringComparer.Ordinal);
        foreach (var g in apuntes.Where(a => a.Fecha <= corte && EsCliente(a.Cuenta)).GroupBy(a => a.Cuenta, StringComparer.Ordinal))
        {
            var saldo = g.Sum(a => a.Debe - a.Haber);
            if (saldo <= 0) continue;
            var pendiente = saldo;
            var masDe30 = 0m;
            foreach (var cargo in g.Where(a => a.Debe - a.Haber > 0).OrderByDescending(a => a.Vencimiento ?? a.Fecha).ThenByDescending(a => a.Fecha))
            {
                if (pendiente <= 0) break;
                var parte = Math.Min(pendiente, cargo.Debe - cargo.Haber);
                var dias = (corte.Date - (cargo.Vencimiento ?? cargo.Fecha).Date).Days;
                porTramo[Tramo(dias)] += parte;
                if (dias > 30) masDe30 += parte;
                pendiente -= parte;
            }
            // Un saldo mayor que los cargos (abonos negativos al Haber) se queda en el tramo más antiguo.
            if (pendiente > 0)
            {
                porTramo[^1] += pendiente;
                masDe30 += pendiente;
            }
            porCliente[g.Key] = (saldo, masDe30);
        }
        return (porTramo, porCliente);
    }

    // ------------------------------------------------------------ gastos por naturaleza

    /// <summary>
    /// Gasto acumulado por cuenta de 3 dígitos (grupo 6 sin la 630, sin regularización ni cierre) hasta la fecha: las
    /// <paramref name="mayores"/> de más importe y el resto agrupado en «Resto», con el comparado a la misma fecha.
    /// </summary>
    public static IReadOnlyList<GastoNaturaleza> GastosPorNaturaleza(
        IEnumerable<MovimientoDia> actual, DateTime corte, IEnumerable<MovimientoDia>? comparado, DateTime? fechaComparada,
        IReadOnlyDictionary<string, string> nombres, int mayores = 8)
    {
        static Dictionary<string, decimal> Por3(IEnumerable<MovimientoDia> ms, DateTime hasta) =>
            ms.Where(m => m.Fecha <= hasta && EntraEnResultado(m) && EsGasto(m.Cuenta) && m.Cuenta.Length >= 3)
              .GroupBy(m => m.Cuenta[..3]).ToDictionary(g => g.Key, g => g.Sum(m => m.Debe - m.Haber));
        var a = Por3(actual, corte);
        var c = comparado is not null && fechaComparada is DateTime fc ? Por3(comparado, fc) : null;
        var orden = a.Keys.Union(c?.Keys ?? Enumerable.Empty<string>())
            .OrderByDescending(k => a.GetValueOrDefault(k)).ThenByDescending(k => c?.GetValueOrDefault(k) ?? 0).ThenBy(k => k, StringComparer.Ordinal)
            .Where(k => a.GetValueOrDefault(k) != 0 || (c?.GetValueOrDefault(k) ?? 0) != 0).ToList();
        var r = orden.Take(mayores).Select(k => new GastoNaturaleza(k, nombres.GetValueOrDefault(k, ""), a.GetValueOrDefault(k), c is null ? null : c.GetValueOrDefault(k))).ToList();
        var resto = orden.Skip(mayores).ToList();
        if (resto.Count > 0)
            r.Add(new GastoNaturaleza("", "Resto", resto.Sum(k => a.GetValueOrDefault(k)), c is null ? null : resto.Sum(k => c.GetValueOrDefault(k))));
        return r;
    }

    // ------------------------------------------------------------ facturación por actividad

    public const string SinActividad = "Sin actividad";
    public const string OtrasActividades = "Otras";

    /// <summary>
    /// Facturación acumulada por actividad de IVA del centro de coste de cada línea (las líneas sin centro o sin actividad van a
    /// «Sin actividad»). Con más de 5 actividades, las 4 de más importe y «Otras» (la paleta tiene 5 colores).
    /// </summary>
    public static IReadOnlyList<FacturacionActividad> PorActividad(
        IEnumerable<LineaVenta> actual, DateTime corte, IEnumerable<LineaVenta>? comparado, DateTime? fechaComparada,
        IReadOnlyDictionary<int, ActividadInicio> actividadPorCentro)
    {
        string Nombre(LineaVenta l) => l.IdCentroCoste is int c && actividadPorCentro.TryGetValue(c, out var a) ? a.Nombre : SinActividad;
        var a = actual.Where(l => l.Fecha <= corte).GroupBy(Nombre).ToDictionary(g => g.Key, g => g.Sum(l => l.Base));
        var c = comparado is not null && fechaComparada is DateTime fc ? comparado.Where(l => l.Fecha <= fc).GroupBy(Nombre).ToDictionary(g => g.Key, g => g.Sum(l => l.Base)) : null;
        var orden = a.Keys.Union(c?.Keys ?? Enumerable.Empty<string>())
            .Where(k => a.GetValueOrDefault(k) != 0 || (c?.GetValueOrDefault(k) ?? 0) != 0)
            .OrderByDescending(k => a.GetValueOrDefault(k)).ThenByDescending(k => c?.GetValueOrDefault(k) ?? 0).ToList();
        var lista = orden.Count > 5 ? orden.Take(4).ToList() : orden;
        var r = lista.Select(k => new FacturacionActividad(k, a.GetValueOrDefault(k), c is null ? null : c.GetValueOrDefault(k))).ToList();
        if (orden.Count > 5)
        {
            var resto = orden.Skip(4).ToList();
            r.Add(new FacturacionActividad(OtrasActividades, resto.Sum(k => a.GetValueOrDefault(k)), c is null ? null : resto.Sum(k => c.GetValueOrDefault(k))));
        }
        return r;
    }
}
