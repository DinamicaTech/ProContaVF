using static ProConta.Inicio.Salud.CalendarioFiscal;

namespace ProConta.Inicio.Salud;

/// <summary>Cada cuánto amortiza la sociedad, deducido de sus asientos de amortización.</summary>
public enum PeriodicidadAmortizacion { Mensual, Trimestral, Anual }

/// <summary>
/// Amortizaciones (activos; pantalla-de-inicio 2026-11-15 16:37): fila «Amortizaciones» de «Estado de los periodos» y aviso de
/// atención cuando un periodo ya cerrado del ejercicio no tiene asiento de amortización (apuntes en una 68 contra una 28,
/// contabilizado, sin apertura, regularización ni cierre). Solo si la sociedad tiene activos amortizables (marcados como amortizables,
/// propios y sin dar de baja) o amortizó en el ejercicio o en el anterior. La periodicidad no es fija: se deduce de los meses en que
/// amortiza (los del ejercicio si ya tiene alguno; si no, los del anterior): solo en diciembre es anual (la copia de validación
/// amortiza así, el 31/12), solo en fin de trimestre es trimestral y, si no, mensual. Sin ningún asiento, anual. Un periodo está
/// cerrado cuando ha terminado su último mes.
/// </summary>
public sealed class RevisionAmortizaciones : IRevisionSalud
{
    public string Clave => "amortizaciones";
    public string Nombre => "Amortizaciones contabilizadas";
    public string Nodo => "activos";

    private static readonly DestinoSalud Destino = new(PantallaSalud.Diario, "Contabilidad › Diario");

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var ejercicios = ctx.Ejercicios.Where(e => e.Ejercicio == ctx.Ejercicio.Ejercicio || e.Ejercicio == ctx.Ejercicio.Ejercicio - 1).ToList();
        var asientos = await fuente.AmortizacionesAsync(ejercicios, c);
        var activos = await fuente.ActivosAmortizablesAsync(ctx.IdSociedad, c);
        return Evaluar(ctx.Ejercicio.Ejercicio, asientos, activos, ctx.Hoy);
    }

    /// <summary>Periodicidad deducida de los meses en que se amortiza.</summary>
    public static PeriodicidadAmortizacion Periodicidad(IEnumerable<int> meses)
    {
        var m = meses.Distinct().ToList();
        if (m.Count == 0 || m.All(x => x == 12)) return PeriodicidadAmortizacion.Anual;
        return m.All(x => x % 3 == 0) ? PeriodicidadAmortizacion.Trimestral : PeriodicidadAmortizacion.Mensual;
    }

    public static string Texto(PeriodicidadAmortizacion p) => p switch
    {
        PeriodicidadAmortizacion.Mensual => "mensual",
        PeriodicidadAmortizacion.Trimestral => "trimestral",
        _ => "anual",
    };

    private sealed record Periodo(int Desde, int Hasta, string Nombre);

    public static HallazgosSalud Evaluar(int ejercicio, IReadOnlyList<AsientoAmortizacion> asientos, int activosAmortizables, DateTime hoy)
    {
        hoy = hoy.Date;
        if (activosAmortizables == 0 && asientos.Count == 0) return HallazgosSalud.Nada;
        var delEjercicio = asientos.Where(a => a.Fecha.Year == ejercicio).ToList();
        var anteriores = asientos.Where(a => a.Fecha.Year == ejercicio - 1).ToList();
        var referencia = delEjercicio.Count > 0 ? delEjercicio : anteriores;
        var periodicidad = Periodicidad(referencia.Select(a => a.Fecha.Month));
        var meses = delEjercicio.GroupBy(a => a.Fecha.Month).ToDictionary(g => g.Key, g => (Fecha: g.Max(a => a.Fecha), Importe: g.Sum(a => a.Importe)));
        var origen = delEjercicio.Count > 0 ? $"según los asientos de amortización de {ejercicio}"
            : anteriores.Count > 0 ? $"según los asientos de amortización de {ejercicio - 1}" : "porque aún no hay asientos de amortización";

        var periodos = periodicidad switch
        {
            PeriodicidadAmortizacion.Mensual => Enumerable.Range(1, 12).Select(m => new Periodo(m, m, RevisionPlazosIva.Mes(m))).ToList(),
            PeriodicidadAmortizacion.Trimestral => Enumerable.Range(1, 4).Select(q => new Periodo(q * 3 - 2, q * 3, $"{q}T")).ToList(),
            _ => [new Periodo(1, 12, $"{ejercicio}")],
        };
        bool Hecho(Periodo p) => meses.Keys.Any(m => m >= p.Desde && m <= p.Hasta);
        bool Cerrado(Periodo p) => new DateTime(ejercicio, p.Hasta, 1).AddMonths(1) <= hoy;
        var faltan = periodos.Where(p => Cerrado(p) && !Hecho(p)).ToList();

        var fila = Fila(ejercicio, periodicidad, periodos, meses, hoy, origen);
        if (faltan.Count == 0)
        {
            if (!periodos.Any(Hecho)) return new HallazgosSalud([], [], [], [fila]);
            var texto = periodicidad == PeriodicidadAmortizacion.Anual
                ? $"Amortización anual de {ejercicio} contabilizada"
                : $"Amortización {Texto(periodicidad)} contabilizada en todos los periodos cerrados de {ejercicio}";
            return new HallazgosSalud([], [], [new ComprobacionCorrecta(texto, "activos")], [fila]);
        }
        var lista = string.Join(", ", faltan.Select(p => p.Nombre));
        var elemento = new ElementoSalud("amortizaciones", TipoSalud.Aviso, GravedadSalud.Atencion,
            periodicidad == PeriodicidadAmortizacion.Anual ? $"Falta la amortización de {ejercicio}" : $"Amortización sin contabilizar: {lista}",
            $"La amortización es {Texto(periodicidad)} ({origen}) y {(faltan.Count == 1 ? "falta la de" : "faltan las de")} {lista}" +
            (activosAmortizables > 0 ? $"; hay {N(activosAmortizables, "activo amortizable", "activos amortizables")}." : "."),
            null, Destino, "activos",
            $"Algún periodo ya terminado de {ejercicio} no tiene asiento de amortización (un asiento contabilizado que carga una 68 y abona una 28). " +
            "Se mira solo si la sociedad tiene activos amortizables o amortizó en el ejercicio o en el anterior; la periodicidad se deduce de los meses en que amortiza.",
            "Contabiliza el asiento de amortización de ese periodo (68 al debe contra 28 al haber) desde Contabilidad › Diario o Asiento manual: ProContaVF aún no lo calcula solo. " +
            "El aviso desaparece en cuanto existe el asiento.") { Cantidad = faltan.Count };
        return new HallazgosSalud([elemento], [], [], [fila]);
    }

    private static FilaPeriodos Fila(int ejercicio, PeriodicidadAmortizacion periodicidad, IReadOnlyList<Periodo> periodos,
        IReadOnlyDictionary<int, (DateTime Fecha, decimal Importe)> meses, DateTime hoy, string origen)
    {
        CeldaPeriodo Celda(int desde, int hasta, string nombre)
        {
            var hecho = meses.Where(m => m.Key >= desde && m.Key <= hasta).ToList();
            if (hecho.Count > 0)
                return new CeldaPeriodo(EstadoPeriodo.Hecho, $"Amortización · {nombre}: asiento del {Fecha(hecho.Max(h => h.Value.Fecha))} por {Imp(hecho.Sum(h => h.Value.Importe))}");
            if (new DateTime(ejercicio, desde, 1) > hoy) return new CeldaPeriodo(EstadoPeriodo.NoToca, $"Amortización · {nombre}: todavía no toca");
            if (new DateTime(ejercicio, hasta, 1).AddMonths(1) > hoy) return new CeldaPeriodo(EstadoPeriodo.EnCurso, $"Amortización · {nombre}: periodo en curso");
            return new CeldaPeriodo(EstadoPeriodo.Incompleto, $"Amortización · {nombre}: sin asiento de amortización");
        }
        List<CeldaPeriodo> celdas;
        if (periodicidad != PeriodicidadAmortizacion.Anual) celdas = periodos.Select(p => Celda(p.Desde, p.Hasta, p.Nombre)).ToList();
        else
        {
            // Anual: 12 celdas; la de diciembre dice si el año está amortizado, las demás «no toca» salvo que tengan un asiento.
            var anioHecho = meses.Count > 0;
            celdas = Enumerable.Range(1, 12).Select(m =>
            {
                var nombre = RevisionPlazosIva.Mes(m);
                if (meses.ContainsKey(m)) return Celda(m, m, nombre);
                if (m < 12) return new CeldaPeriodo(EstadoPeriodo.NoToca, $"Amortización · {nombre}: no toca (amortización anual, {origen})");
                if (anioHecho) return new CeldaPeriodo(EstadoPeriodo.Hecho, $"Amortización de {ejercicio}: contabilizada en otro mes del año");
                return Celda(1, 12, $"{ejercicio} (anual, en diciembre)");
            }).ToList();
        }
        return new FilaPeriodos("Amortizaciones", celdas, Destino, "activos", 4);
    }
}

/// <summary>
/// Céntimos residuales (contabilidad/procesos/centimos-residuales; pantalla-de-inicio 2026-11-15 16:37): subcuentas de clientes y
/// proveedores (40, 41 y 43) cuyo saldo a la fecha de corte, redondeado a céntimos, no es cero y es menor de 1 € en valor
/// absoluto. Aviso informativo: no deforman las cuentas, pero ensucian el Mayor y los listados de pendientes. Abre el Mayor ya con
/// esas subcuentas. Solo avisa; el proceso que los limpia es de contabilidad/procesos/centimos-residuales.
/// </summary>
public sealed class RevisionCentimosResiduales : IRevisionSalud
{
    public const decimal Umbral = 1m;
    public string Clave => "centimos-residuales";
    public string Nombre => "Sin céntimos residuales en clientes y proveedores";
    public string Nodo => "contabilidad/procesos/centimos-residuales";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.SaldosAsync(ctx.Ejercicio, "40", "43", ctx.Corte, c), ctx.Corte);

    public static HallazgosSalud Evaluar(IReadOnlyDictionary<string, decimal> saldos, DateTime corte)
    {
        var residuales = saldos
            .Where(s => s.Key.StartsWith("40", StringComparison.Ordinal) || s.Key.StartsWith("41", StringComparison.Ordinal) || s.Key.StartsWith("43", StringComparison.Ordinal))
            .Select(s => (Cuenta: s.Key, Saldo: Math.Round(s.Value, 2, MidpointRounding.AwayFromZero)))
            .Where(s => s.Saldo != 0 && Math.Abs(s.Saldo) < Umbral).OrderBy(s => s.Cuenta, StringComparer.Ordinal).ToList();
        if (residuales.Count == 0)
            return HallazgosSalud.Correcta($"Ningún cliente ni proveedor tiene céntimos residuales (saldo de menos de 1 €) al {Fecha(corte)}", "contabilidad/procesos/centimos-residuales");
        var total = residuales.Sum(s => Math.Abs(s.Saldo));
        var clientes = residuales.Count(s => s.Cuenta.StartsWith("43", StringComparison.Ordinal));
        var detalle = string.Join(", ", residuales.Take(4).Select(s => $"{s.Cuenta} {Imp(s.Saldo)}")) + (residuales.Count > 4 ? "…" : "");
        return HallazgosSalud.De(new ElementoSalud("centimos-residuales", TipoSalud.Aviso, GravedadSalud.Aviso,
            $"{N(residuales.Count, "subcuenta", "subcuentas")} de clientes o proveedores con céntimos residuales",
            $"{clientes} de clientes y {residuales.Count - clientes} de proveedores o acreedores con un saldo de menos de 1 € (en total {Imp(total)} en valor absoluto): {detalle}.",
            null, DestinoSalud.Mayor(residuales.Select(s => s.Cuenta).ToList(), "céntimos residuales"), "contabilidad/procesos/centimos-residuales",
            $"Alguna subcuenta 40, 41 o 43 tiene al {Fecha(corte)} un saldo distinto de cero y menor de 1 € en valor absoluto (diferencias de redondeo o de cobro y pago).",
            "Revisa esos saldos en el Mayor y sáldalos con el proceso de céntimos residuales (Contabilidad › Procesos contables) cuando esté en ProContaVF, o con un asiento contra la 669 o la 769. " +
            "El aviso desaparece cuando quedan a cero.") { Cantidad = residuales.Count, Importe = total });
    }
}
