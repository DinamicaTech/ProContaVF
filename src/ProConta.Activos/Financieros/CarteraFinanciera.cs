namespace ProConta.Activos.Financieros;

/// <summary>
/// Un lote de un activo financiero: cada compra es un lote. Al vender, todos los lotes vivos se reducen en la misma proporción, porque el coste de lo
/// vendido es el coste medio ponderado (activos 2026-11-11 18:21, Q5 a; activos/activos-financieros 2026-11-24 09:27).
/// </summary>
/// <param name="Numero">1, 2… por orden de compra.</param>
/// <param name="Compradas">Participaciones compradas en el lote.</param>
/// <param name="Vivas">Las que quedan hoy después de las ventas (pueden tener decimales: cada venta se reparte entre los lotes).</param>
/// <param name="CosteUnitario">Coste de una participación del lote en euros, con comisiones: coste del lote / participaciones (sin redondear).</param>
/// <param name="Consumos">Parte de cada venta que sale de este lote: fecha y participaciones.</param>
public sealed record LoteInversion(int Numero, DateTime Fecha, decimal Compradas, decimal Vivas, decimal CosteUnitario, IReadOnlyList<(DateTime Fecha, decimal Participaciones)> Consumos)
{
    /// <summary>Coste de lo que queda del lote, a céntimos.</summary>
    public decimal CosteVivo => Math.Round(Vivas * CosteUnitario, 2, MidpointRounding.AwayFromZero);

    /// <summary>Participaciones del lote al final del día <paramref name="fecha"/> (cero antes de comprarlo).</summary>
    public decimal ParticipacionesEn(DateTime fecha) =>
        fecha.Date < Fecha.Date ? 0m : Compradas - Consumos.Where(c => c.Fecha.Date <= fecha.Date).Sum(c => c.Participaciones);
}

/// <summary>Una venta con el coste medio de lo vendido y su resultado (a 766 si gana, a 666 si pierde).</summary>
/// <param name="Importe">Lo cobrado en euros (el importe total de la operación).</param>
/// <param name="CosteVendido">Coste medio ponderado de las participaciones vendidas, a céntimos (<see cref="CosteMedioInversion"/>).</param>
public sealed record VentaInversion(DateTime Fecha, decimal Participaciones, decimal Importe, decimal CosteVendido)
{
    public decimal Resultado => Importe - CosteVendido;
}

/// <summary>Lotes de un activo financiero a coste medio ponderado, a partir de sus operaciones (compras y ventas).</summary>
public static class LotesCosteMedio
{
    /// <summary>
    /// Lotes (uno por compra) y ventas con su coste. Las operaciones se recorren como en <see cref="CosteMedioInversion"/>: por fecha y, el mismo día,
    /// las compras antes que las ventas. Cada venta reduce todos los lotes vivos en la proporción vendida (vender todo los deja a cero) y su coste es
    /// el de <see cref="CosteMedioInversion"/>, el mismo que usan la venta y su asiento. Una venta de más participaciones de las que hay las vacía.
    /// </summary>
    public static (IReadOnlyList<LoteInversion> Lotes, IReadOnlyList<VentaInversion> Ventas) Calcular(IEnumerable<OperacionDeInversion> operaciones)
    {
        var ops = operaciones.Where(o => o.Participaciones > 0m && (o.EsCompra || o.EsVenta))
            .OrderBy(o => o.Fecha.Date).ThenBy(o => o.Tipo).ThenBy(o => o.IdOperacion).ToList();
        var coste = CosteMedioInversion.Calcular(ops.Select(o => new MovimientoCoste(o.IdOperacion, o.Tipo, o.Fecha, o.Participaciones, o.ImporteTotalEur)));
        var lotes = new List<(DateTime Fecha, decimal Compradas, decimal Coste, List<(DateTime, decimal)> Consumos)>();
        var ventas = new List<VentaInversion>();
        foreach (var o in ops)
        {
            if (o.EsCompra)
            {
                lotes.Add((o.Fecha.Date, o.Participaciones, o.ImporteTotalEur, []));
                continue;
            }
            var vivas = lotes.Select(l => l.Compradas - l.Consumos.Sum(c => c.Item2)).ToList();
            var total = vivas.Sum();
            if (total > 0m)
            {
                var todo = o.Participaciones >= total;
                for (var i = 0; i < lotes.Count; i++)
                {
                    if (vivas[i] <= 0m) continue;
                    var sale = todo ? vivas[i] : vivas[i] * o.Participaciones / total;
                    lotes[i].Consumos.Add((o.Fecha.Date, sale));
                }
            }
            ventas.Add(new VentaInversion(o.Fecha.Date, o.Participaciones, o.ImporteTotalEur, coste.CosteVendido.GetValueOrDefault(o.IdOperacion)));
        }
        var r = lotes.Select((l, i) => new LoteInversion(i + 1, l.Fecha, l.Compradas, l.Compradas - l.Consumos.Sum(c => c.Item2),
            l.Compradas == 0m ? 0m : l.Coste / l.Compradas, l.Consumos)).ToList();
        return (r, ventas);
    }
}

/// <summary>
/// Rentabilidad anual por tasa interna de retorno (TIR): el tipo anual que iguala lo invertido con lo cobrado y lo que vale hoy, teniendo en cuenta
/// cuándo entró y salió cada euro. Es una tasa, no un importe: se calcula en coma flotante y se devuelve en tanto por uno redondeado a 6 decimales.
/// </summary>
public static class Tir
{
    /// <summary>Menos de un año entre el primer y el último flujo: la tasa anualizada no es representativa y no se calcula.</summary>
    public const int DiasMinimos = 365;

    /// <summary>
    /// Flujos: negativos lo pagado (compras) y positivos lo cobrado (ventas y el valor de hoy). Null si no hay pagos y cobros, si abarcan menos de
    /// <see cref="DiasMinimos"/> días o si no hay solución entre −99 % y +1.000 % anual.
    /// </summary>
    public static decimal? Calcular(IReadOnlyList<(DateTime Fecha, decimal Importe)> flujos)
    {
        var f = flujos.Where(x => x.Importe != 0m).OrderBy(x => x.Fecha).ToList();
        if (f.Count < 2 || !f.Any(x => x.Importe < 0m) || !f.Any(x => x.Importe > 0m)) return null;
        var t0 = f[0].Fecha.Date;
        if ((f[^1].Fecha.Date - t0).TotalDays < DiasMinimos) return null;
        var datos = f.Select(x => ((x.Fecha.Date - t0).TotalDays / 365.0, (double)x.Importe)).ToList();
        double Van(double r) => datos.Sum(d => d.Item2 / Math.Pow(1 + r, d.Item1));
        double lo = -0.99, hi = 10.0;
        var flo = Van(lo);
        if (flo * Van(hi) > 0) return null;
        for (var i = 0; i < 200; i++)
        {
            var m = (lo + hi) / 2;
            var fm = Van(m);
            if (flo * fm <= 0) hi = m;
            else { lo = m; flo = fm; }
        }
        return Math.Round((decimal)((lo + hi) / 2), 6, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Serie de precios diarios en euros de un instrumento, ordenada por fecha.</summary>
public sealed class SeriePrecios
{
    private readonly DateTime[] _fechas;
    private readonly decimal[] _precios;

    public SeriePrecios(IEnumerable<(DateTime Fecha, decimal PrecioEur)> puntos)
    {
        var p = puntos.Where(x => x.PrecioEur > 0m).GroupBy(x => x.Fecha.Date).Select(g => (Fecha: g.Key, Precio: g.Last().PrecioEur)).OrderBy(x => x.Fecha).ToList();
        _fechas = p.Select(x => x.Fecha).ToArray();
        _precios = p.Select(x => x.Precio).ToArray();
    }

    public int Count => _fechas.Length;
    public IReadOnlyList<DateTime> Fechas => _fechas;
    public IReadOnlyList<decimal> Precios => _precios;
    public DateTime? Primera => _fechas.Length == 0 ? null : _fechas[0];
    public (DateTime Fecha, decimal Precio)? Ultimo => _fechas.Length == 0 ? null : (_fechas[^1], _precios[^1]);

    /// <summary>Último precio conocido el día <paramref name="fecha"/> o antes; null si no hay ninguno.</summary>
    public decimal? En(DateTime fecha)
    {
        var i = Array.BinarySearch(_fechas, fecha.Date);
        if (i >= 0) return _precios[i];
        i = ~i - 1;
        return i >= 0 ? _precios[i] : null;
    }

    /// <summary>Los puntos desde <paramref name="desde"/> (incluido).</summary>
    public IEnumerable<(DateTime Fecha, decimal Precio)> Desde(DateTime desde)
    {
        for (var i = 0; i < _fechas.Length; i++)
            if (_fechas[i] >= desde.Date) yield return (_fechas[i], _precios[i]);
    }
}

/// <summary>Un activo financiero analizado: su instrumento, lotes, ventas, precio de hoy y cifras.</summary>
/// <param name="Precio">Último precio en euros (null sin cotización: participaciones no cotizadas o sin datos; se valora a coste).</param>
/// <param name="FechaPrecio">Día de ese precio.</param>
/// <param name="OrigenPrecio">De dónde sale («Yahoo Finance · del momento», «EODHD · cierre»…).</param>
public sealed record PosicionFinanciera(int IdActivo, string Codigo, InversionDeActivo Inversion, IReadOnlyList<LoteInversion> Lotes, IReadOnlyList<VentaInversion> Ventas,
    decimal? Precio, DateTime? FechaPrecio, string? OrigenPrecio, SeriePrecios Historico, DateTime Hoy)
{
    public DatosInstrumento Instrumento => Inversion.Instrumento;
    public bool TieneCotizacion => Precio is not null;
    public decimal Participaciones => Lotes.Sum(l => l.Vivas);
    public bool Viva => Participaciones > 0m;
    public int LotesVivos => Lotes.Count(l => l.Vivas > 0m);

    /// <summary>Coste de lo que queda en cartera a coste medio ponderado, a céntimos (el de la ficha y la contabilidad).</summary>
    public decimal Coste => Inversion.Coste;

    /// <summary>Coste medio por participación de lo que queda (con comisiones).</summary>
    public decimal? PrecioMedio => Participaciones > 0m ? Coste / Participaciones : null;

    /// <summary>Valor de mercado: participaciones × último precio, a céntimos; sin cotización, el coste.</summary>
    public decimal Valor => Precio is decimal p ? Math.Round(Participaciones * p, 2, MidpointRounding.AwayFromZero) : Coste;

    public decimal Plusvalia => Valor - Coste;
    public decimal? PlusvaliaPorcentaje => Coste > 0m ? Plusvalia / Coste : null;

    public decimal ResultadoVentas => Ventas.Sum(v => v.Resultado);
    public decimal ResultadoVentasEjercicio(int ejercicio) => Ventas.Where(v => v.Fecha.Year == ejercicio).Sum(v => v.Resultado);

    /// <summary>
    /// Lo ganado o perdido en el ejercicio (propietario 2026-11-27): el resultado de sus ventas del año más lo que ha subido o bajado su plusvalía latente
    /// entre el 31/12 anterior y el final del año (hoy, si el año está en curso). Las compras y las ventas no cuentan como ganancia. Sin cotización
    /// se valora a coste, así que solo cuentan las ventas.
    /// </summary>
    public decimal ResultadoEjercicio(int ejercicio)
    {
        var inicio = new DateTime(ejercicio - 1, 12, 31);
        var fin = new DateTime(ejercicio, 12, 31);
        var latenteFin = fin >= Hoy.Date ? Plusvalia : ValorEn(fin) - CosteEn(fin);
        var latenteInicio = ValorEn(inicio) - CosteEn(inicio);
        return Math.Round(ResultadoVentasEjercicio(ejercicio) + latenteFin - latenteInicio, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Rentabilidad del ejercicio en curso o pasado (activos 2026-11-28, D1–D2): la ponderada en el tiempo (TWR) desde el 31/12 anterior, o desde la
    /// primera compra si se compró en el año, hasta la última cotización (en un año pasado, hasta el 31/12); la proyectada, lineal × 365 / días. Null
    /// sin cotización o si no tuvo participaciones en el año.
    /// </summary>
    public RentabilidadPeriodo? RentabilidadEjercicio(int ejercicio) => RentabilidadPeriodo.De([this], ejercicio);

    /// <summary>Flujos de la TIR: compras (−), ventas (+) y el valor de hoy (+).</summary>
    public IReadOnlyList<(DateTime Fecha, decimal Importe)> Flujos
    {
        get
        {
            var f = Inversion.Operaciones.Where(o => o.EsCompra || o.Tipo == OperacionDeInversion.TipoVenta)
                .Select(o => (o.Fecha, o.EsCompra ? -o.ImporteTotalEur : o.ImporteTotalEur)).ToList();
            if (Valor > 0m) f.Add((Hoy, Valor));
            return f;
        }
    }

    /// <summary>Rentabilidad anual (TIR) desde la primera compra; null sin cotización o con menos de un año.</summary>
    public decimal? RentabilidadAnual => TieneCotizacion ? Tir.Calcular(Flujos) : null;

    /// <summary>Valor de mercado de un lote vivo, a céntimos.</summary>
    public decimal ValorLote(LoteInversion l) => Precio is decimal p ? Math.Round(l.Vivas * p, 2, MidpointRounding.AwayFromZero) : l.CosteVivo;

    /// <summary>TIR de un lote: su coste vivo el día de la compra contra su valor de hoy.</summary>
    public decimal? RentabilidadAnualLote(LoteInversion l) =>
        TieneCotizacion && l.Vivas > 0m ? Tir.Calcular([(l.Fecha, -l.CosteVivo), (Hoy, ValorLote(l))]) : null;

    /// <summary>Participaciones al final de un día.</summary>
    public decimal ParticipacionesEn(DateTime fecha) => Lotes.Sum(l => l.ParticipacionesEn(fecha));

    /// <summary>Coste de lo que había en cartera al final de un día (coste unitario de cada lote por sus participaciones de ese día).</summary>
    public decimal CosteEn(DateTime fecha) => Lotes.Sum(l => l.ParticipacionesEn(fecha) * l.CosteUnitario);

    /// <summary>Valor de lo que había en cartera al final de un día con el último precio conocido (sin precio, a coste).</summary>
    public decimal ValorEn(DateTime fecha)
    {
        var p = Historico.En(fecha);
        return Lotes.Sum(l => l.ParticipacionesEn(fecha) * (p ?? l.CosteUnitario));
    }

    /// <summary>Precio medio de compra de lo que había en cartera ese día; null si no había participaciones.</summary>
    public decimal? PrecioMedioEn(DateTime fecha)
    {
        var u = ParticipacionesEn(fecha);
        return u > 0m ? CosteEn(fecha) / u : null;
    }

    /// <summary>Crea la posición a partir de sus operaciones, el histórico de precios en euros y el precio de hoy.</summary>
    public static PosicionFinanciera Crear(int idActivo, string codigo, InversionDeActivo inversion, SeriePrecios historico, PrecioEnEuros? hoy, DateTime fechaHoy)
    {
        var (lotes, ventas) = LotesCosteMedio.Calcular(inversion.Operaciones);
        decimal? precio = hoy?.PrecioEur;
        DateTime? fecha = hoy?.FechaCotizacion;
        var origen = hoy?.Origen;
        if (precio is null && historico.Ultimo is { } u)
        {
            precio = u.Precio;
            fecha = u.Fecha;
            origen = "histórico guardado";
        }
        return new PosicionFinanciera(idActivo, codigo, inversion, lotes, ventas, precio, fecha, origen, historico, fechaHoy.Date);
    }
}

/// <summary>Rentabilidad ponderada en el tiempo de un periodo del ejercicio y su proyección anual lineal (activos 2026-11-28).</summary>
public sealed record RentabilidadPeriodo(decimal Rentabilidad, DateTime Desde, DateTime Hasta)
{
    public int Dias => (Hasta.Date - Desde.Date).Days;

    /// <summary>La del periodo llevada a un año: × 365 / días; null sin días.</summary>
    public decimal? Proyectada => Dias > 0 ? Rentabilidad * 365m / Dias : null;

    /// <summary>
    /// TWR de unas posiciones (una sola, o las de un grupo) en el ejercicio: desde el 31/12 anterior, o desde la primera compra del año de las que no
    /// tenían nada entonces, hasta la última cotización conocida sin pasar del 31/12 del ejercicio. Null si ninguna cotiza o no tuvieron participaciones.
    /// </summary>
    public static RentabilidadPeriodo? De(IReadOnlyList<PosicionFinanciera> posiciones, int ejercicio)
    {
        var inicio = new DateTime(ejercicio - 1, 12, 31);
        var finAno = new DateTime(ejercicio, 12, 31);
        var cotizadas = posiciones.Where(p => p.Historico.Count > 0 || p.FechaPrecio is not null).ToList();
        if (cotizadas.Count == 0) return null;
        var hasta = cotizadas.Max(p => p.FechaPrecio ?? p.Historico.Ultimo?.Fecha ?? inicio);
        if (hasta > finAno) hasta = finAno;
        if (hasta <= inicio) return null;
        DateTime? desde = cotizadas.Any(p => p.ParticipacionesEn(inicio) > 0m) ? inicio
            : cotizadas.SelectMany(p => p.Lotes).Where(l => l.Fecha > inicio && l.Fecha <= hasta).Select(l => (DateTime?)l.Fecha.Date).Min();
        if (desde is not DateTime d || d >= hasta) return null;
        var dias = SeriesCartera.Dias(d, hasta);
        if (dias.Count == 0 || dias[0] != d) dias = [d, .. dias.Where(x => x > d)];
        var r = SeriesCartera.RentabilidadPonderada(cotizadas, dias);
        return r.Count > 0 && r[^1] is decimal v ? new RentabilidadPeriodo(v, d, hasta) : null;
    }
}

/// <summary>Un punto de la revalorización de la cartera: lo invertido (coste de lo que había ese día) y lo que valía.</summary>
public sealed record PuntoCartera(DateTime Fecha, decimal Invertido, decimal Valor)
{
    public decimal Plusvalia => Valor - Invertido;
    public decimal? PlusvaliaPorcentaje => Invertido > 0m ? Plusvalia / Invertido : null;
}

/// <summary>Series de los gráficos de la cartera: revalorización sumada y comparativa de variación relativa.</summary>
public static class SeriesCartera
{
    /// <summary>
    /// Días hábiles (lunes a viernes) entre dos fechas, ambas incluidas; con <paramref name="cadaN"/> &gt; 1, uno de cada N y siempre el último.
    /// </summary>
    public static IReadOnlyList<DateTime> Dias(DateTime desde, DateTime hasta, int cadaN = 1)
    {
        var r = new List<DateTime>();
        for (var d = desde.Date; d <= hasta.Date; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) r.Add(d);
        if (cadaN <= 1 || r.Count == 0) return r;
        var s = r.Where((_, i) => i % cadaN == 0).ToList();
        if (s[^1] != r[^1]) s.Add(r[^1]);
        return s;
    }

    /// <summary>Paso para que una serie no pase de unos <paramref name="maximo"/> puntos.</summary>
    public static int Paso(DateTime desde, DateTime hasta, int maximo = 600) =>
        Math.Max(1, (int)Math.Ceiling(Dias(desde, hasta).Count / (double)maximo));

    /// <summary>
    /// Revalorización de las posiciones cotizadas: cada día, el coste de lo que había en cartera (cada lote cuenta desde su compra y deja de contar al
    /// venderse) y su valor con el último precio conocido. Las participaciones sin cotización no entran (se valoran a coste y no se revalorizan).
    /// </summary>
    public static IReadOnlyList<PuntoCartera> Revalorizacion(IReadOnlyList<PosicionFinanciera> posiciones, IReadOnlyList<DateTime> dias)
    {
        var cotizadas = posiciones.Where(p => p.Historico.Count > 0).ToList();
        return dias.Select(d => new PuntoCartera(d,
            Math.Round(cotizadas.Sum(p => p.CosteEn(d)), 2, MidpointRounding.AwayFromZero),
            Math.Round(cotizadas.Sum(p => p.ValorEn(d)), 2, MidpointRounding.AwayFromZero))).ToList();
    }

    /// <summary>
    /// Rentabilidad de la cartera ponderada en el tiempo (ergonomía de activos 2026-11-26), acumulada en tanto por uno desde el primer día con valor:
    /// las compras y las ventas no la mueven, solo lo que cambian los precios. Cada tramo es r = (V − F) / V anterior − 1, con V el valor de mercado y F
    /// el dinero que entró en el tramo (compras menos lo cobrado en ventas), y los tramos se encadenan. Null hasta el primer día con valor; los días sin
    /// nada en cartera mantienen la última.
    /// </summary>
    public static IReadOnlyList<decimal?> RentabilidadPonderada(IReadOnlyList<PuntoCartera> puntos, IEnumerable<(DateTime Fecha, decimal Flujo)> flujos)
    {
        var f = flujos.OrderBy(x => x.Fecha).ToList();
        var r = new List<decimal?>(puntos.Count);
        decimal? acumulado = null, anterior = null;
        for (var i = 0; i < puntos.Count; i++)
        {
            var v = puntos[i].Valor;
            if (acumulado is null)
            {
                if (v > 0m) acumulado = 1m;
            }
            else if (anterior > 0m)
            {
                var desde = puntos[i - 1].Fecha.Date;
                var hasta = puntos[i].Fecha.Date;
                var flujo = f.Where(x => x.Fecha.Date > desde && x.Fecha.Date <= hasta).Sum(x => x.Flujo);
                acumulado *= (v - flujo) / anterior.Value;
            }
            anterior = v;
            r.Add(acumulado - 1m);
        }
        return r;
    }

    /// <summary>La rentabilidad ponderada en el tiempo de las posiciones cotizadas en esos días (las mismas que <see cref="Revalorizacion"/>).</summary>
    public static IReadOnlyList<decimal?> RentabilidadPonderada(IReadOnlyList<PosicionFinanciera> posiciones, IReadOnlyList<DateTime> dias)
    {
        var cotizadas = posiciones.Where(p => p.Historico.Count > 0).ToList();
        var flujos = cotizadas.SelectMany(p => p.Inversion.Operaciones.Where(o => o.EsCompra || o.EsVenta)
            .Select(o => (o.Fecha, Flujo: o.EsCompra ? o.ImporteTotalEur : -o.ImporteTotalEur)));
        return RentabilidadPonderada(Revalorizacion(cotizadas, dias), flujos);
    }

    /// <summary>
    /// Variación acumulada en tanto por uno de un precio desde el primer día de la serie: precio del día / precio del primer día − 1. Null los días
    /// sin precio conocido (antes del primer dato del instrumento).
    /// </summary>
    public static IReadOnlyList<decimal?> Variacion(SeriePrecios serie, IReadOnlyList<DateTime> dias)
    {
        decimal? baseP = null;
        var r = new List<decimal?>(dias.Count);
        foreach (var d in dias)
        {
            var p = serie.En(d);
            if (p is null) { r.Add(null); continue; }
            baseP ??= p;
            r.Add(p.Value / baseP.Value - 1m);
        }
        return r;
    }
}
