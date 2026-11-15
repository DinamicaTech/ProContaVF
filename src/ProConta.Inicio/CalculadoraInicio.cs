using System.Globalization;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Ratios;

namespace ProConta.Inicio;

/// <summary>
/// Monta el <see cref="PanelInicio"/> de un ejercicio a partir de lo leído (pura, sin BD): indicadores con su comparativo y
/// proyección, facturación por mes y por actividad, ingresos y gastos, gastos por naturaleza, antigüedad de lo pendiente de
/// cobro, tesorería de los últimos 12 meses y saldo por banco.
/// </summary>
public static class CalculadoraInicio
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly string[] Meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    /// <param name="actual">El ejercicio de trabajo.</param>
    /// <param name="anterior">El ejercicio anterior, para los meses de tesorería que caen en él; null si no existe.</param>
    /// <param name="comparado">El ejercicio con el que se compara; null para «Sin comparar».</param>
    /// <param name="inversiones">Cuentas contables de los bancos marcados como inversión: no son tesorería operativa.</param>
    /// <param name="situacion">Modelo del balance de situación para los ratios; null, sin ratios.</param>
    /// <param name="cierres">Saldos de los ejercicios anteriores por año (series de los ratios a cierre).</param>
    public static PanelInicio Calcular(
        DatosEjercicio actual, DatosEjercicio? anterior, DatosEjercicio? comparado, DateTime hoy, IReadOnlyList<BancoN43> bancos,
        IReadOnlyDictionary<int, ActividadInicio> actividadPorCentro, IReadOnlyList<ActividadInicio> actividades,
        IReadOnlySet<string>? inversiones = null, ModeloBalance? situacion = null, IReadOnlyDictionary<int, IReadOnlyList<MovimientoRatio>>? cierres = null)
    {
        inversiones ??= new HashSet<string>(StringComparer.Ordinal);
        var anio = actual.Ejercicio.Ejercicio;
        var corte = CalculoInicio.FechaCorte(anio, hoy);
        var terminado = CalculoInicio.Terminado(corte);
        DateTime? fc = comparado is null ? null : CalculoInicio.MismaFecha(corte, comparado.Ejercicio.Ejercicio);
        var mov = actual.Movimientos;
        var movC = comparado?.Movimientos;

        // Facturación
        var ventas = actual.Ventas.Select(v => (v.Fecha, v.Base)).ToList();
        var mensual = CalculoInicio.PorMes(ventas, corte);
        var acumulado = actual.Ventas.Where(v => v.Fecha <= corte).Sum(v => v.Base);
        decimal[]? compMensual = null;
        decimal? compAcumulado = null, compTotal = null, proyeccion = null;
        var proyMensual = new decimal?[12];
        if (comparado is not null && fc is DateTime f)
        {
            var ventasC = comparado.Ventas.Select(v => (v.Fecha, v.Base)).ToList();
            compMensual = CalculoInicio.PorMesCompleto(ventasC, comparado.Ejercicio.Ejercicio);
            compAcumulado = comparado.Ventas.Where(v => v.Fecha <= f).Sum(v => v.Base);
            compTotal = compMensual.Sum();
            proyeccion = CalculoInicio.Proyeccion(compTotal, acumulado, compAcumulado, terminado);
            if (proyeccion is not null)
            {
                var mesCorte = comparado.Ventas.Where(v => v.Fecha.Month == f.Month && v.Fecha <= f).Sum(v => v.Base);
                proyMensual = CalculoInicio.ProyeccionPorMes(compMensual, mesCorte, acumulado / compAcumulado!.Value, corte);
            }
        }

        // Resultado
        var resultado = CalculoInicio.Resultado(mov, corte);
        decimal? resultadoC = null, resultadoTotalC = null;
        if (movC is not null && fc is DateTime f2)
        {
            resultadoC = CalculoInicio.Resultado(movC, f2);
            resultadoTotalC = CalculoInicio.Resultado(movC, new DateTime(comparado!.Ejercicio.Ejercicio, 12, 31));
        }
        var ingresos = CalculoInicio.PorMes(mov.Where(m => CalculoInicio.EntraEnResultado(m) && CalculoInicio.EsIngreso(m.Cuenta)).Select(m => (m.Fecha, m.Haber - m.Debe)), corte);
        var gastos = CalculoInicio.PorMes(mov.Where(m => CalculoInicio.EntraEnResultado(m) && CalculoInicio.EsGasto(m.Cuenta)).Select(m => (m.Fecha, m.Debe - m.Haber)), corte);
        // EBITDA y su proyección de cierre (la proyección del indicador de resultado).
        var ebitda = CalculoInicio.Ebitda(mov, corte);
        decimal? ebitdaC = null, ebitdaTotalC = null;
        if (movC is not null && fc is DateTime f7)
        {
            ebitdaC = CalculoInicio.Ebitda(movC, f7);
            ebitdaTotalC = CalculoInicio.Ebitda(movC, new DateTime(comparado!.Ejercicio.Ejercicio, 12, 31));
        }
        var ebitdaInicio = new EbitdaInicio(ebitda, ebitdaC, ebitdaTotalC, CalculoInicio.Proyeccion(ebitdaTotalC, ebitda, ebitdaC, terminado));
        var resultadoMensual = ingresos.Select((v, i) => v is decimal x && gastos[i] is decimal g ? x - g : (decimal?)null).ToList();

        // Tesorería operativa: 572 y las cuentas contables de los bancos de Norma 43, sin las cuentas de inversión.
        var codigosBanco = bancos.Where(b => b.CuentaContable is { Length: > 0 }).Select(b => b.CuentaContable!).ToHashSet(StringComparer.Ordinal);
        bool EsTesoreria(string c) => (c.StartsWith("572", StringComparison.Ordinal) || codigosBanco.Contains(c)) && !inversiones.Contains(c);
        var tesoreria = CalculoInicio.Saldo(mov, EsTesoreria, corte);
        decimal? tesoreriaC = movC is not null && fc is DateTime f3 ? CalculoInicio.Saldo(movC, EsTesoreria, f3) : null;
        var puntos = PuntosTesoreria(actual, anterior, EsTesoreria, corte);
        var saldosBanco = CalculoInicio.SaldosPorCuenta(mov, codigosBanco.Contains, corte);
        var todosBancos = bancos.Where(b => b.CuentaContable is { Length: > 0 })
            .GroupBy(b => b.CuentaContable!, StringComparer.Ordinal)
            .Select(g => new SaldoBanco(g.First().Nombre, g.Key, saldosBanco.GetValueOrDefault(g.Key), g.Max(b => b.UltimoExtracto)))
            .OrderByDescending(b => b.Saldo).ToList();
        var listaBancos = todosBancos.Where(b => !inversiones.Contains(b.Cuenta!)).ToList();
        var listaInversiones = todosBancos.Where(b => inversiones.Contains(b.Cuenta!)).ToList();
        var otras = tesoreria - listaBancos.Sum(b => b.Saldo);

        // Pendiente de cobro y de pago
        var cobro = CalculoInicio.PendienteCobro(mov, corte);
        var pago = CalculoInicio.PendientePago(mov, corte);
        decimal? cobroC = movC is not null && fc is DateTime f4 ? CalculoInicio.PendienteCobro(movC, f4) : null;
        decimal? pagoC = movC is not null && fc is DateTime f5 ? CalculoInicio.PendientePago(movC, f5) : null;
        var (porTramo, porCliente) = CalculoInicio.Antiguedad(actual.Cartera, corte);
        var clientes = porCliente.OrderByDescending(c => c.Value.Saldo).Take(5)
            .Select(c => new ClientePendiente(c.Key, actual.Nombres.GetValueOrDefault(c.Key, ""), c.Value.Saldo, c.Value.MasDe30)).ToList();

        // IVA estimado del trimestre activo
        var trimestre = actual.Ejercicio.TrimestreActivo is int t && t is >= 1 and <= 4 ? t : CalculoInicio.TrimestreDe(corte);
        var (desde, finTrimestre) = CalculoInicio.Trimestre(anio, trimestre);
        var hasta = finTrimestre < corte ? finTrimestre : corte;
        var (rep, sop) = CalculoInicio.Iva(actual.Iva, desde, hasta, actividades, actual.Ejercicio.Prorrata);
        decimal? ivaC = null;
        if (comparado is not null && fc is DateTime f6)
        {
            var (dC, hC) = CalculoInicio.Trimestre(comparado.Ejercicio.Ejercicio, trimestre);
            var finC = hC < f6 ? hC : f6;
            var (r, s) = CalculoInicio.Iva(comparado.Iva, dC, finC, actividades, comparado.Ejercicio.Prorrata);
            ivaC = r - s;
        }
        var evolucionIva = Enumerable.Range(1, 4).Select(q =>
        {
            var (d, h) = CalculoInicio.Trimestre(anio, q);
            if (d > corte) return (decimal?)null;
            var (r, s) = CalculoInicio.Iva(actual.Iva, d, h < corte ? h : corte, actividades, actual.Ejercicio.Prorrata);
            return r - s;
        }).ToList();

        var (ratios, serie) = Ratios(actual, anterior, corte, terminado, inversiones, situacion, cierres, codigosBanco);

        return new PanelInicio
        {
            Ejercicio = anio,
            Comparado = comparado?.Ejercicio.Ejercicio,
            FechaCorte = corte,
            FechaComparada = fc,
            Terminado = terminado,
            HayMovimientos = actual.HayMovimientos,
            Facturacion = new Indicador(acumulado, compAcumulado, mensual, proyeccion, compTotal),
            Resultado = new Indicador(resultado, resultadoC, CalculoInicio.Acumulado(resultadoMensual), null, resultadoTotalC),
            Tesoreria = new Indicador(tesoreria, tesoreriaC, puntos.Select(p => p.Saldo).ToList()),
            PendienteCobro = new Indicador(cobro, cobroC, CalculoInicio.SaldoFinDeMes(mov, (m, d) => CalculoInicio.PendienteCobro(m, d), corte)),
            PendientePago = new Indicador(pago, pagoC, CalculoInicio.SaldoFinDeMes(mov, (m, d) => CalculoInicio.PendientePago(m, d), corte)),
            Iva = new IvaTrimestre(trimestre, desde, hasta, rep, sop, ivaC),
            EvolucionIva = evolucionIva,
            FacturacionMensual = mensual,
            ProyeccionMensual = proyMensual,
            FacturacionComparadaMensual = compMensual,
            FacturacionDeContabilidad = actual.VentasDeContabilidad,
            ComparadoDeContabilidad = comparado?.VentasDeContabilidad ?? false,
            Actividades = CalculoInicio.PorActividad(actual.Ventas, corte, comparado?.Ventas, fc, actividadPorCentro),
            IngresosMensuales = ingresos,
            GastosMensuales = gastos,
            Gastos = CalculoInicio.GastosPorNaturaleza(mov, corte, movC, fc, actual.Nombres),
            Antiguedad = CalculoInicio.Tramos.Select((e, i) => new TramoAntiguedad(e, porTramo[i])).ToList(),
            Clientes = clientes,
            HayVencimientos = actual.Cartera.Any(a => a.Vencimiento is not null),
            TesoreriaMensual = puntos,
            Bancos = listaBancos,
            OtrasCuentasTesoreria = otras,
            Inversiones = listaInversiones,
            Ebitda = ebitdaInicio,
            Ratios = ratios,
            SerieRatios = serie,
        };
    }

    /// <summary>
    /// Los ratios del ejercicio de trabajo a la fecha de su balance (fin del último mes completo; el 31/12 si está terminado) con
    /// el resultado de los 12 meses hasta esa fecha, y la serie a cierre de cada ejercicio anterior con datos.
    /// </summary>
    internal static (ResultadoRatios? Actual, IReadOnlyList<RatiosEjercicio> Serie) Ratios(
        DatosEjercicio actual, DatosEjercicio? anterior, DateTime corte, bool terminado, IReadOnlySet<string> inversiones,
        ModeloBalance? situacion, IReadOnlyDictionary<int, IReadOnlyList<MovimientoRatio>>? cierres, IReadOnlySet<string>? bancos = null)
    {
        if (situacion is null) return (null, []);
        var anio = actual.Ejercicio.Ejercicio;
        var fb = CalculoRatios.FechaBalance(corte, terminado);
        var movActual = CalculoInicio.ParaRatios(actual.Movimientos);
        var hayAnterior = anterior is { HayMovimientos: true };
        IReadOnlyList<MovimientoRatio> movAnterior = anterior is null ? [] : CalculoInicio.ParaRatios(anterior.Movimientos);
        ResultadoRatios r;
        if (fb.Year == anio)
        {
            var cierreAnio = fb.Month == 12 && fb.Day == 31;
            string? sinResultado = cierreAnio || hayAnterior ? null
                : $"Faltan los datos de {anio - 1} para completar el resultado de los 12 meses hasta el {fb:dd/MM/yyyy}.";
            IEnumerable<MovimientoRatio> doce = cierreAnio ? movActual : movActual.Concat(movAnterior);
            r = CalculoRatios.Calcular(situacion, movActual, doce, fb, inversiones, sinResultado, bancos);
        }
        else r = CalculoRatios.Calcular(situacion, movAnterior, movAnterior, fb, inversiones, null, bancos);
        var serie = new List<RatiosEjercicio>();
        if (cierres is not null)
            foreach (var (ej, movs) in cierres.Where(c => c.Key < anio && c.Value.Count > 0).OrderBy(c => c.Key))
                serie.Add(new RatiosEjercicio(ej, CalculoRatios.Calcular(situacion, movs, movs, new DateTime(ej, 12, 31), inversiones, null, bancos), false));
        serie.Add(new RatiosEjercicio(anio, r, true));
        return (r, serie);
    }

    /// <summary>
    /// Saldo de tesorería a fin de cada uno de los últimos 12 meses, terminando en la fecha de corte. Los meses del ejercicio
    /// anterior salen de sus apuntes; si no existe, son huecos.
    /// </summary>
    internal static IReadOnlyList<PuntoTesoreria> PuntosTesoreria(DatosEjercicio actual, DatosEjercicio? anterior, Func<string, bool> esTesoreria, DateTime corte)
    {
        var r = new List<PuntoTesoreria>();
        for (var k = 11; k >= 0; k--)
        {
            var mes = new DateTime(corte.Year, corte.Month, 1).AddMonths(-k);
            var esCorte = k == 0;
            var fecha = esCorte ? corte : new DateTime(mes.Year, mes.Month, DateTime.DaysInMonth(mes.Year, mes.Month));
            var datos = mes.Year == actual.Ejercicio.Ejercicio ? actual : anterior?.Ejercicio.Ejercicio == mes.Year ? anterior : null;
            decimal? saldo = datos is null || !datos.HayMovimientos ? null : CalculoInicio.Saldo(datos.Movimientos, esTesoreria, fecha);
            var etiqueta = esCorte && fecha.Day != DateTime.DaysInMonth(fecha.Year, fecha.Month)
                ? fecha.Day.ToString(Es) + " " + Meses[fecha.Month - 1]
                : Meses[fecha.Month - 1];
            r.Add(new PuntoTesoreria(fecha, etiqueta, saldo, esCorte));
        }
        return r;
    }
}
