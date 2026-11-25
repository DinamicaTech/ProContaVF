namespace ProConta.Norma43.Plataformas;

/// <summary>
/// Cómo paga una plataforma de venta (OrigenesVenta, norma43/conciliacion/plataformas 2026-11-25): el cargo por cobrar que descuenta
/// además de la comisión, desde qué fecha de la reserva puede llegar el cobro y hasta cuántos días después de la salida, y el plazo
/// habitual, que desempata cuando un cobro cuadra con varias reservas.
/// </summary>
/// <param name="CargoPorcentaje">Porcentaje del total que descuenta además de la comisión (Booking, 1,3 %).</param>
/// <param name="DesdeSalida">El cobro llega después de la salida (Booking); si no, desde la entrada (Airbnb, que paga a trozos durante la estancia).</param>
/// <param name="DiasTrasSalida">Último día, contado desde la salida, en que puede llegar el cobro.</param>
/// <param name="HabitualDesde">Inicio del plazo habitual, en días desde la fecha de referencia (entrada o salida); null = sin desempate.</param>
/// <param name="HabitualHasta">Fin del plazo habitual.</param>
public sealed record PlazosCobroPlataforma(decimal CargoPorcentaje, bool DesdeSalida, int DiasTrasSalida, int? HabitualDesde, int? HabitualHasta)
{
    /// <summary>Lo que se usa para una plataforma sin configurar: sin cargo, desde la entrada y hasta 15 días después de la salida.</summary>
    public static PlazosCobroPlataforma Predeterminados { get; } = new(0m, false, 15, null, null);

    /// <summary>Airbnb: sin cargo, desde la entrada (paga a trozos durante la estancia) hasta 7 días tras la salida; lo habitual, de 2 a 4 días tras la entrada.</summary>
    public static PlazosCobroPlataforma Airbnb { get; } = new(0m, false, 7, 2, 4);

    /// <summary>Booking: 1,3 % de cargo por servicio de los pagos, desde la salida hasta 15 días después; lo habitual, de 2 a 12 días.</summary>
    public static PlazosCobroPlataforma Booking { get; } = new(1.3m, true, 15, 2, 12);

    /// <summary>Los plazos conocidos de una plataforma por el código de su origen de venta (AIRBNB, BOOKING) o, si no, los predeterminados.</summary>
    public static PlazosCobroPlataforma Para(string? codigo) => (codigo ?? "").Trim().ToUpperInvariant() switch
    {
        "AIRBNB" => Airbnb,
        "BOOKING" => Booking,
        _ => Predeterminados,
    };
}

/// <summary>Un abono de la plataforma en el banco, sin enlazar con ninguna factura.</summary>
public sealed record CobroPlataforma(int IdLinea, int IdOrigen, DateTime Fecha, decimal Importe);

/// <summary>
/// La factura validada de una reserva de la plataforma, pendiente de cobro: el total que paga el huésped y la comisión de la venta
/// (VentasPorFacturar.Comision).
/// </summary>
public sealed record ReservaFacturada(int IdFactura, int IdOrigen, DateTime Entrada, DateTime Salida, decimal Total, decimal Comision);

/// <summary>Cómo cruzan las líneas con las facturas.</summary>
public enum TipoCrucePlataforma
{
    /// <summary>Un cobro, una reserva.</summary>
    Simple,
    /// <summary>Un cobro paga varias reservas.</summary>
    Agrupado,
    /// <summary>Varios cobros pagan una reserva.</summary>
    Fraccionado,
    /// <summary>Varios cobros pagan entre todos varias reservas.</summary>
    Cruzado,
}

/// <summary>Parte de un cobro que se aplica a una factura; <paramref name="Completa"/> si con ella la factura queda cobrada entera.</summary>
public sealed record AplicacionCobro(int IdFactura, int IdLinea, decimal Importe, bool Completa);

/// <summary>Un cruce: sus líneas, sus facturas y cómo se reparte cada cobro entre ellas.</summary>
public sealed record CrucePlataforma(TipoCrucePlataforma Tipo, IReadOnlyList<int> Lineas, IReadOnlyList<int> Facturas, IReadOnlyList<AplicacionCobro> Reparto);

/// <summary>Un cobro que no cruza, con el motivo para el operador.</summary>
public sealed record CobroSinCruzar(int IdLinea, string Motivo);

/// <summary>Resultado del cruce: lo que cruza y lo que queda para conciliar a mano.</summary>
public sealed record ResultadoCrucePlataformas(IReadOnlyList<CrucePlataforma> Cruces, IReadOnlyList<CobroSinCruzar> SinCruzar);

/// <summary>
/// Cruce de los cobros de las plataformas con las facturas de sus reservas, sin BD (norma43/conciliacion/plataformas 2026-11-25). La
/// N43 no trae el código de la reserva, así que cruza por importe y fechas: el cobro esperado de cada reserva es su total menos la
/// comisión y menos el cargo por cobrar de la plataforma (redondeado a céntimos), y el cobro tiene que llegar dentro del plazo de la
/// plataforma. Solo cruza cuando la combinación es única (o única dentro del plazo habitual); si hay varias, el cobro queda para
/// conciliarlo a mano. Se prueba, por este orden y hasta que no cambia nada: un cobro con 1 a 3 reservas; una reserva con 2 a 8 cobros;
/// y, al final, 2 o 3 cobros cercanos con 2 o 3 reservas.
/// </summary>
public static class ReglasCobrosPlataformas
{
    /// <summary>Diferencia máxima entre lo cobrado y lo esperado, en euros (el redondeo del cargo de Booking).</summary>
    public const decimal Tolerancia = 0.01m;
    /// <summary>Reservas como mucho que paga un cobro.</summary>
    public const int MaximoReservasPorCobro = 3;
    /// <summary>Cobros como mucho en que se paga una reserva (se han visto 7).</summary>
    public const int MaximoCobrosPorReserva = 8;
    /// <summary>Cobros candidatos como mucho para buscar los trozos de una reserva (más, y la búsqueda no acaba).</summary>
    public const int MaximoCandidatosTrozos = 18;
    /// <summary>Días como mucho entre los cobros de un cruce de varios con varios.</summary>
    public const int DiasCruzado = 5;

    /// <summary>Cobro que se espera de la reserva: total − comisión − cargo por cobrar (redondeado a céntimos, alejándose de cero).</summary>
    public static decimal CobroEsperado(ReservaFacturada r, PlazosCobroPlataforma p)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(p);
        return r.Total - r.Comision - Math.Round(r.Total * p.CargoPorcentaje / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>El cobro puede ser de la reserva: misma plataforma y dentro del plazo (desde la entrada o la salida, hasta la salida más los días).</summary>
    public static bool EnPlazo(CobroPlataforma c, ReservaFacturada r, PlazosCobroPlataforma p)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(p);
        if (c.IdOrigen != r.IdOrigen) return false;
        var desde = (p.DesdeSalida ? r.Salida : r.Entrada).Date;
        return c.Fecha.Date >= desde && (c.Fecha.Date - r.Salida.Date).Days <= p.DiasTrasSalida;
    }

    /// <summary>El cobro llega en el plazo habitual de la plataforma (si no tiene, nunca).</summary>
    public static bool EnPlazoHabitual(CobroPlataforma c, ReservaFacturada r, PlazosCobroPlataforma p)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(p);
        if (p.HabitualDesde is not int a || p.HabitualHasta is not int b) return false;
        var dias = (c.Fecha.Date - (p.DesdeSalida ? r.Salida : r.Entrada).Date).Days;
        return dias >= a && dias <= b;
    }

    /// <summary>Cruza los cobros con las reservas. <paramref name="plazos"/> por origen; los que falten usan <see cref="PlazosCobroPlataforma.Predeterminados"/>.</summary>
    public static ResultadoCrucePlataformas Cruzar(IReadOnlyList<CobroPlataforma> cobros, IReadOnlyList<ReservaFacturada> reservas,
        IReadOnlyDictionary<int, PlazosCobroPlataforma> plazos)
    {
        ArgumentNullException.ThrowIfNull(cobros);
        ArgumentNullException.ThrowIfNull(reservas);
        ArgumentNullException.ThrowIfNull(plazos);
        PlazosCobroPlataforma P(int origen) => plazos.TryGetValue(origen, out var p) ? p : PlazosCobroPlataforma.Predeterminados;
        var lineas = cobros.OrderBy(c => c.Fecha).ThenBy(c => c.IdLinea).ToList();
        var facturas = reservas.OrderBy(r => r.Entrada).ThenBy(r => r.IdFactura).ToList();
        var esperado = facturas.ToDictionary(r => r.IdFactura, r => CobroEsperado(r, P(r.IdOrigen)));
        bool Plazo(CobroPlataforma c, ReservaFacturada r) => EnPlazo(c, r, P(r.IdOrigen));
        bool Habitual(CobroPlataforma c, ReservaFacturada r) => EnPlazoHabitual(c, r, P(r.IdOrigen));
        bool Cuadra(decimal a, decimal b) => Math.Abs(a - b) <= Tolerancia;

        var usadasL = new HashSet<int>();
        var usadasF = new HashSet<int>();
        var ambiguas = new HashSet<int>();
        var cruces = new List<CrucePlataforma>();

        void Anotar(TipoCrucePlataforma tipo, IReadOnlyList<CobroPlataforma> ls, IReadOnlyList<ReservaFacturada> fs)
        {
            usadasL.UnionWith(ls.Select(l => l.IdLinea));
            usadasF.UnionWith(fs.Select(f => f.IdFactura));
            foreach (var l in ls) ambiguas.Remove(l.IdLinea);
            cruces.Add(new CrucePlataforma(tipo, ls.Select(l => l.IdLinea).ToList(), fs.Select(f => f.IdFactura).ToList(), Repartir(ls, fs, esperado)));
        }

        bool cambio = true;
        while (cambio)
        {
            cambio = false;
            // Un cobro con 1 a 3 reservas.
            foreach (var l in lineas)
            {
                if (usadasL.Contains(l.IdLinea)) continue;
                var cand = facturas.Where(f => !usadasF.Contains(f.IdFactura) && Plazo(l, f)).ToList();
                var combos = Combinaciones(cand, 1, MaximoReservasPorCobro).Where(c => Cuadra(c.Sum(f => esperado[f.IdFactura]), l.Importe)).ToList();
                if (combos.Count > 1)
                {
                    var habituales = combos.Where(c => c.All(f => Habitual(l, f))).ToList();
                    if (habituales.Count == 1) combos = habituales;
                }
                if (combos.Count == 1)
                {
                    Anotar(combos[0].Count == 1 ? TipoCrucePlataforma.Simple : TipoCrucePlataforma.Agrupado, [l], combos[0]);
                    cambio = true;
                }
                else if (combos.Count > 1) ambiguas.Add(l.IdLinea);
            }
            // Una reserva con 2 a 8 cobros.
            foreach (var f in facturas)
            {
                if (usadasF.Contains(f.IdFactura)) continue;
                var cand = lineas.Where(l => !usadasL.Contains(l.IdLinea) && Plazo(l, f)).ToList();
                if (cand.Count < 2 || cand.Count > MaximoCandidatosTrozos) continue;
                var combos = Combinaciones(cand, 2, MaximoCobrosPorReserva).Where(c => Cuadra(c.Sum(l => l.Importe), esperado[f.IdFactura])).Take(2).ToList();
                if (combos.Count != 1) continue;
                Anotar(TipoCrucePlataforma.Fraccionado, combos[0], [f]);
                cambio = true;
            }
        }

        // Varios cobros cercanos con varias reservas.
        foreach (var l0 in lineas)
        {
            if (usadasL.Contains(l0.IdLinea)) continue;
            var cf = facturas.Where(f => !usadasF.Contains(f.IdFactura) && Plazo(l0, f)).ToList();
            var cl = lineas.Where(l => !usadasL.Contains(l.IdLinea) && l.IdLinea != l0.IdLinea && l.IdOrigen == l0.IdOrigen
                && Math.Abs((l.Fecha.Date - l0.Fecha.Date).Days) <= DiasCruzado).ToList();
            var encontrados = new List<(List<CobroPlataforma> Ls, List<ReservaFacturada> Fs)>();
            foreach (var otras in Combinaciones(cl, 1, 2))
            {
                var ls = new List<CobroPlataforma> { l0 };
                ls.AddRange(otras);
                var total = ls.Sum(l => l.Importe);
                foreach (var fs in Combinaciones(cf, 2, 3))
                    if (ls.All(l => fs.Any(f => Plazo(l, f))) && Cuadra(fs.Sum(f => esperado[f.IdFactura]), total))
                        encontrados.Add((ls.OrderBy(l => l.Fecha).ThenBy(l => l.IdLinea).ToList(), fs));
                if (encontrados.Count > 1) break;
            }
            if (encontrados.Count == 1) Anotar(TipoCrucePlataforma.Cruzado, encontrados[0].Ls, encontrados[0].Fs);
        }

        var sin = lineas.Where(l => !usadasL.Contains(l.IdLinea)).Select(l => new CobroSinCruzar(l.IdLinea, ambiguas.Contains(l.IdLinea)
            ? "Cuadra con varias reservas a la vez y no sé cuál es: concílialo a mano con «Buscar por importe…»."
            : facturas.Any(f => !usadasF.Contains(f.IdFactura) && Plazo(l, f))
                ? "Ninguna combinación de reservas pendientes de su plataforma suma este importe (¿reserva modificada o con un ajuste de la plataforma?): concílialo a mano."
                : "No hay ninguna reserva facturada y pendiente de cobro de su plataforma en esas fechas: comprueba que la reserva está facturada y validada.")).ToList();
        return new ResultadoCrucePlataformas(cruces, sin);
    }

    /// <summary>
    /// Reparte los cobros entre las facturas por orden (facturas por entrada, cobros por fecha): cada cobro paga lo que le queda a la
    /// factura en curso y pasa a la siguiente. La última factura se lleva lo que sobre (la diferencia de redondeo, si la hay), así que
    /// lo aplicado de cada cobro suma su importe.
    /// </summary>
    public static IReadOnlyList<AplicacionCobro> Repartir(IReadOnlyList<CobroPlataforma> cobros, IReadOnlyList<ReservaFacturada> reservas,
        IReadOnlyDictionary<int, decimal> esperado)
    {
        ArgumentNullException.ThrowIfNull(cobros);
        ArgumentNullException.ThrowIfNull(reservas);
        ArgumentNullException.ThrowIfNull(esperado);
        var fs = reservas.OrderBy(r => r.Entrada).ThenBy(r => r.IdFactura).ToList();
        var ls = cobros.OrderBy(c => c.Fecha).ThenBy(c => c.IdLinea).ToList();
        var r = new List<AplicacionCobro>();
        var i = 0;
        var falta = fs.Count > 0 ? esperado[fs[0].IdFactura] : 0m;
        for (var j = 0; j < ls.Count; j++)
        {
            var queda = ls[j].Importe;
            while (queda > 0m && i < fs.Count)
            {
                var ultimaFactura = i == fs.Count - 1;
                var ultimoCobro = j == ls.Count - 1;
                var importe = ultimaFactura ? queda : Math.Min(queda, falta);
                falta -= importe;
                queda -= importe;
                var completa = ultimaFactura ? ultimoCobro : falta <= Tolerancia;
                r.Add(new AplicacionCobro(fs[i].IdFactura, ls[j].IdLinea, importe, completa));
                if (completa && !ultimaFactura)
                {
                    // El céntimo de redondeo que faltara pasa a la siguiente factura.
                    i++;
                    falta = esperado[fs[i].IdFactura] + falta;
                }
            }
        }
        return r;
    }

    /// <summary>Combinaciones de <paramref name="minimo"/> a <paramref name="maximo"/> elementos, en el orden de la lista.</summary>
    private static IEnumerable<List<T>> Combinaciones<T>(IReadOnlyList<T> elementos, int minimo, int maximo)
    {
        for (var k = minimo; k <= Math.Min(maximo, elementos.Count); k++)
            foreach (var c in Elegir(elementos, k, 0))
                yield return c;
    }

    private static IEnumerable<List<T>> Elegir<T>(IReadOnlyList<T> elementos, int k, int desde)
    {
        if (k == 0) { yield return []; yield break; }
        for (var i = desde; i <= elementos.Count - k; i++)
            foreach (var resto in Elegir(elementos, k - 1, i + 1))
            {
                resto.Insert(0, elementos[i]);
                yield return resto;
            }
    }
}
