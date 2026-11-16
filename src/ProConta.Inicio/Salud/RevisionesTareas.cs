using static ProConta.Inicio.Salud.CalendarioFiscal;

namespace ProConta.Inicio.Salud;

/// <summary>
/// Tarea 1: facturar las ventas pendientes antes de un cambio de tipo de IVA (facturacion/reservas). Grave, con plazo el día
/// anterior al cambio. Se ve desde 90 días antes del cambio mientras queden ventas por facturar (sin factura o con la factura en
/// borrador) con entrada anterior a esa fecha; pasado el cambio sigue como vencida mientras queden (hasta un año después).
/// </summary>
public sealed class RevisionVentasAntesCambioIva(IReadOnlyList<CambioTipoIva>? cambios = null) : IRevisionSalud
{
    public string Clave => "ventas-cambio-iva";
    public string Nombre => "Ventas por facturar antes de un cambio de tipo de IVA";
    public string Nodo => "facturacion/reservas";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var elementos = new List<ElementoSalud>();
        foreach (var cambio in (cambios ?? CambiosTipoIva).Where(x => EnVentana(x, ctx.Hoy)))
            if (Evaluar(cambio, await fuente.VentasPendientesAsync(ctx.IdSociedad, cambio.Desde, c), ctx.Hoy) is { } e) elementos.Add(e);
        return new HallazgosSalud(elementos, [], [], []);
    }

    internal static bool EnVentana(CambioTipoIva cambio, DateTime hoy) => hoy >= cambio.Desde.AddDays(-DiasAntesDelPlazo) && hoy < cambio.Desde.AddYears(1);

    public static ElementoSalud? Evaluar(CambioTipoIva cambio, VentasPendientes v, DateTime hoy)
    {
        if (v.Total == 0 || !EnVentana(cambio, hoy)) return null;
        var d = cambio.Desde;
        var enVigor = hoy >= d;
        var borrador = v.EnBorrador > 0 ? $" ({v.EnBorrador} con la factura en borrador sin validar)" : "";
        var explicacion = (enVigor ? $"Desde el {Fecha(d)} {cambio.Que} lleva un {cambio.TipoNuevo} de IVA y aún quedan" : $"Desde el {Fecha(d)} {cambio.Que} lleva un {cambio.TipoNuevo} de IVA. Quedan") +
                          $" {N(v.Total, "venta", "ventas")} con entrada anterior a esa fecha en Ventas por facturar{borrador}, por {Imp(v.Importe)} con IVA.";
        return new ElementoSalud($"ventas-cambio-iva-{d:yyyyMMdd}", TipoSalud.Tarea, GravedadSalud.Grave, $"Facturar las ventas pendientes antes del {d.Day}/{d.Month}", explicacion,
            d.AddDays(-1), new DestinoSalud(PantallaSalud.VentasPorFacturar, "Ventas › Ventas por facturar"), "facturacion/reservas",
            $"Hay ventas por facturar sin anular ni facturadas fuera, sin factura o con la factura en borrador, cuya fecha de entrada es anterior al {Fecha(d)}, " +
            $"día en que cambia el tipo de IVA de {cambio.Que} ({cambio.Origen}). Se ve desde 90 días antes del cambio.",
            "Factura esas ventas en Ventas por facturar (o anula las que no procedan) y valida las facturas que estén en borrador. La tarea desaparece sola cuando no queda ninguna.")
        { Cantidad = v.Total, Importe = v.Importe };
    }
}

/// <summary>
/// Tarea 2: crear el ejercicio siguiente si aún no existe y faltan menos de 60 días para fin de año (contabilidad/procesos). Plazo
/// el 31/12; aviso, y atención en los 15 últimos días.
/// </summary>
public sealed class RevisionEjercicioSiguiente : IRevisionSalud
{
    public string Clave => "ejercicio-siguiente";
    public string Nombre => "Ejercicio siguiente creado";
    public string Nodo => "contabilidad/procesos";

    public Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Task.FromResult(Evaluar(ctx.Ejercicios.Select(e => e.Ejercicio).ToHashSet(), ctx.Hoy) is { } e ? HallazgosSalud.De(e) : HallazgosSalud.Nada);

    public static ElementoSalud? Evaluar(IReadOnlySet<int> ejercicios, DateTime hoy)
    {
        var siguiente = hoy.Year + 1;
        var fin = new DateTime(hoy.Year, 12, 31);
        var dias = (fin - hoy.Date).Days;
        if (ejercicios.Contains(siguiente) || dias >= 60) return null;
        return new ElementoSalud($"ejercicio-{siguiente}", TipoSalud.Tarea, dias <= 15 ? GravedadSalud.Atencion : GravedadSalud.Aviso, $"Crear el ejercicio {siguiente}",
            $"Necesario antes de contabilizar o emitir la primera factura del 1/1/{siguiente}. La sociedad aún no tiene el ejercicio {siguiente}.",
            fin, new DestinoSalud(PantallaSalud.Ejercicios, "Configuración › Parámetros y tablas › Ejercicios"), "contabilidad/procesos",
            $"La sociedad no tiene el ejercicio {siguiente} en SociedadesEjercicios y faltan menos de 60 días para el 31/12/{hoy.Year}.",
            $"Crea el ejercicio {siguiente} en Configuración › Parámetros y tablas › Ejercicios. La tarea desaparece en cuanto existe.");
    }
}

/// <summary>
/// Tarea 3: plazos fiscales del IVA (contabilidad/modelos-tributarios). El 303 de cada trimestre (del 1 al 20 de abril, julio y
/// octubre; del 1 al 30 de enero el del 4T) y el 390 (hasta el 30 de enero), en los 90 días anteriores al fin del plazo. El 303 se
/// da por hecho si existe el asiento de liquidación del trimestre (uno contabilizado, ni de apertura ni de regularización ni de
/// cierre, que abona la 4750 o carga la 4700: el pago del 303, 4750 al debe contra el banco, no cuenta; se asigna al trimestre
/// cuyo plazo contiene su fecha o, si no, al de su fecha). El 390 no se puede detectar: se ve hasta que pasa el plazo. Con la fila
/// «IVA liquidado» de «Estado de los periodos».
/// </summary>
public sealed class RevisionPlazosIva : IRevisionSalud
{
    public string Clave => "plazos-iva";
    public string Nombre => "Plazos del 303 y del 390";
    public string Nodo => "contabilidad/modelos-tributarios";

    private static readonly DestinoSalud Liquidacion = new(PantallaSalud.LiquidacionIva, "Contabilidad › Procesos contables › Liquidación de IVA");
    private static readonly DestinoSalud Modelos = DestinoSalud.SinPantalla("Contabilidad › Modelos tributarios");

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var anios = new[] { ctx.Hoy.Year - 1, ctx.Hoy.Year, ctx.Ejercicio.Ejercicio }.Distinct().ToList();
        var ejercicios = ctx.Ejercicios.Where(e => anios.Contains(e.Ejercicio)).ToList();
        var liquidaciones = await fuente.LiquidacionesIvaAsync(ejercicios, c);
        var actividades = await fuente.ActividadesAsync(ctx.IdSociedad, c);
        var iva = new Dictionary<int, (EjercicioSociedad E, IReadOnlyList<ApunteIva> A)>();
        foreach (var e in ejercicios) iva[e.Ejercicio] = (e, await fuente.IvaAsync(e, c));
        (decimal Rep, decimal Sop)? Estimado(int anio, int q)
        {
            if (!iva.TryGetValue(anio, out var x)) return null;
            var (desde, hasta) = CalculoInicio.Trimestre(anio, q);
            return CalculoInicio.Iva(x.A, desde, hasta < ctx.Hoy ? hasta : ctx.Hoy, actividades, x.E.Prorrata);
        }
        return Evaluar(ejercicios.Select(e => e.Ejercicio).ToHashSet(), ctx.Ejercicio.Ejercicio, liquidaciones, ctx.Hoy, Estimado);
    }

    /// <summary>Trimestre (año y número) al que se asigna un asiento de liquidación.</summary>
    public static (int Anio, int Trimestre) TrimestreLiquidado(DateTime fecha)
    {
        var q = CalculoInicio.TrimestreDe(fecha);
        var (anioPrevio, qPrevio) = q == 1 ? (fecha.Year - 1, 4) : (fecha.Year, q - 1);
        return fecha >= InicioPlazo303(anioPrevio, qPrevio) && fecha <= FinPlazo303(anioPrevio, qPrevio) ? (anioPrevio, qPrevio) : (fecha.Year, q);
    }

    public static HallazgosSalud Evaluar(IReadOnlySet<int> ejercicios, int ejercicioTrabajo, IReadOnlyList<LiquidacionIva> liquidaciones, DateTime hoy,
        Func<int, int, (decimal Rep, decimal Sop)?> estimado)
    {
        hoy = hoy.Date;
        var hechos = new Dictionary<(int, int), DateTime>();
        foreach (var l in liquidaciones.OrderBy(l => l.Fecha))
            hechos.TryAdd(TrimestreLiquidado(l.Fecha.Date), l.Fecha.Date);
        var elementos = new List<ElementoSalud>();
        var hechas = new List<HechaSalud>();
        var trimestreHoy = CalculoInicio.Trimestre(hoy.Year, CalculoInicio.TrimestreDe(hoy));
        foreach (var anio in new[] { hoy.Year - 1, hoy.Year }.Where(ejercicios.Contains))
            for (var q = 1; q <= 4; q++)
            {
                var fin = FinPlazo303(anio, q);
                var (desdeQ, _) = CalculoInicio.Trimestre(anio, q);
                if (desdeQ > hoy) continue;
                var nombre = anio == hoy.Year ? $"{q}T" : $"{q}T de {anio}";
                if (hechos.TryGetValue((anio, q), out var fecha))
                {
                    if (fin >= trimestreHoy.Desde && fin <= trimestreHoy.Hasta)
                        hechas.Add(new HechaSalud($"303-{anio}-{q}", $"Liquidación del IVA del {nombre}", $"Asiento de liquidación del {Fecha(fecha)}.", Liquidacion, "contabilidad/modelos-tributarios"));
                    continue;
                }
                if (hoy < fin.AddDays(-DiasAntesDelPlazo)) continue;
                var est = estimado(anio, q);
                var cifras = est is var (rep, sop)
                    ? (rep - sop) >= 0 ? $" {(hoy <= CalculoInicio.Trimestre(anio, q).Hasta ? "Estimado hasta hoy" : "Estimado")}: {Imp(rep - sop)} a ingresar (repercutido {Imp(rep)}, soportado deducible {Imp(sop)})."
                                       : $" {(hoy <= CalculoInicio.Trimestre(anio, q).Hasta ? "Estimado hasta hoy" : "Estimado")}: {Imp(sop - rep)} a compensar o devolver (repercutido {Imp(rep)}, soportado deducible {Imp(sop)})."
                    : "";
                var plazo = q == 4 ? $"del 1 al 30 de enero de {anio + 1}" : $"del 1 al 20 de {Mes(fin.Month)}";
                var dias = (fin - hoy).Days;
                elementos.Add(new ElementoSalud($"303-{anio}-{q}", TipoSalud.Tarea, dias < 0 ? GravedadSalud.Grave : dias <= 16 ? GravedadSalud.Atencion : GravedadSalud.Aviso,
                    $"Liquidar el IVA del {nombre} y presentar el 303", $"Plazo {plazo}." + cifras, fin, Liquidacion, "contabilidad/modelos-tributarios",
                    $"No hay asiento de liquidación del IVA del {nombre} (un asiento contabilizado que abona la 4750 o carga la 4700, fechado en el trimestre o en su plazo; " +
                    "el pago del 303 contra el banco no cuenta) " +
                    $"y faltan menos de {DiasAntesDelPlazo} días para el fin del plazo, o ya ha pasado. El estimado es el mismo que el del indicador de IVA: repercutido (477) menos soportado deducible (472).",
                    $"Haz el asiento de liquidación del {nombre} y presenta el 303 en la sede de la AEAT. La pantalla de liquidación de IVA aún no está en ProContaVF: el asiento se hace a mano " +
                    "(Contabilidad › Asiento manual). La tarea desaparece en cuanto existe el asiento de liquidación."));
            }
        foreach (var anio in new[] { hoy.Year - 1, hoy.Year }.Where(ejercicios.Contains))
        {
            var fin = FinPlazo390(anio);
            if (hoy > fin || hoy < fin.AddDays(-DiasAntesDelPlazo)) continue;
            var dias = (fin - hoy).Days;
            elementos.Add(new ElementoSalud($"390-{anio}", TipoSalud.Tarea, dias <= 16 ? GravedadSalud.Atencion : GravedadSalud.Aviso, $"Presentar el resumen anual 390 de {anio}",
                $"Plazo hasta el 30 de enero de {anio + 1}.", fin, Modelos, "contabilidad/modelos-tributarios",
                $"Faltan menos de {DiasAntesDelPlazo} días para el fin del plazo del 390 de {anio}. ProContaVF aún no registra la presentación de los modelos, así que no puede saber si ya está presentado.",
                "Presenta el 390 en la sede de la AEAT. Hasta que exista la pantalla de modelos tributarios, la tarea desaparece sola al pasar el plazo."));
        }
        return new HallazgosSalud(elementos, hechas, [], [FilaIva(ejercicioTrabajo, hechos, hoy)]);
    }

    private static FilaPeriodos FilaIva(int anio, IReadOnlyDictionary<(int, int), DateTime> hechos, DateTime hoy)
    {
        var celdas = Enumerable.Range(1, 4).Select(q =>
        {
            var (desde, hasta) = CalculoInicio.Trimestre(anio, q);
            var fin = FinPlazo303(anio, q);
            if (hechos.TryGetValue((anio, q), out var f)) return new CeldaPeriodo(EstadoPeriodo.Hecho, $"IVA del {q}T: liquidado (asiento del {Fecha(f)})");
            if (desde > hoy) return new CeldaPeriodo(EstadoPeriodo.NoToca, $"IVA del {q}T: todavía no toca");
            if (hoy <= hasta) return new CeldaPeriodo(EstadoPeriodo.EnCurso, $"IVA del {q}T: trimestre en curso, se liquida hasta el {Fecha(fin)}");
            if (hoy <= fin) return new CeldaPeriodo(EstadoPeriodo.Incompleto, $"IVA del {q}T: sin asiento de liquidación; el plazo acaba el {Fecha(fin)}");
            return new CeldaPeriodo(EstadoPeriodo.ConErrores, $"IVA del {q}T: sin asiento de liquidación y con el plazo vencido (el {Fecha(fin)})");
        }).ToList();
        return new FilaPeriodos("IVA liquidado", celdas, Liquidacion, "contabilidad/modelos-tributarios", 3);
    }

    internal static string Mes(int m) => new[] { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" }[m - 1];
}
