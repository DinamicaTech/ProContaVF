using System.Globalization;
using ProConta.Activos.Financieros;
using ProConta.Datos.Persistencia;

namespace ProConta.Activos.Gestion;

/// <summary>Un activo financiero de la cartera con sus datos de activo (código, nombre, baja) y su posición calculada.</summary>
public sealed record ActivoDeCartera(int IdActivo, string Codigo, string Nombre, DateTime? FechaBaja, PosicionFinanciera Posicion);

/// <summary>La cartera financiera de la sociedad lista para analizar.</summary>
/// <param name="Avisos">Lo que no se ha podido actualizar (sin conexión, proveedor caído…), en frases para el operador.</param>
/// <param name="ConRed">Si se han consultado los proveedores o solo lo guardado.</param>
public sealed record CarteraCargada(IReadOnlyList<ActivoDeCartera> Activos, IReadOnlyList<string> Avisos, DateTime Hoy, bool ConRed);

/// <summary>Una cotización guardada en CotizacionesInstrumentoFinanciero (en la moneda del instrumento).</summary>
public sealed record CotizacionGuardada(DateTime Fecha, decimal Precio, string Moneda, string? Origen)
{
    /// <summary>Precio del momento (no un cierre): se sustituye por el cierre real cuando el día ya ha pasado.</summary>
    public bool DelMomento => Origen?.Contains("del momento", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Cotizaciones del análisis de activos financieros (activos/activos-financieros 2026-11-23 20:11 y 20:19): el histórico diario se completa con Yahoo
/// Finance y se guarda en CotizacionesInstrumentoFinanciero; el precio del día sale de EODHD la primera vez que se abre la pantalla ese día (y se guarda) y
/// después de Yahoo; si Yahoo falla, queda el de EODHD. Todo se pasa a euros con el cambio del BCE de cada día.
/// </summary>
public sealed class ServicioCarteraFinanciera(IBaseDatos bd, ServicioActivos activos, ICotizaciones cotizaciones, ClienteYahooFinance yahoo,
    ServicioCambiosDivisa cambios, Func<DateTime>? hoy = null)
{
    /// <summary>Años de histórico que se completan antes de la primera compra (comparativa a 5 años y proyección).</summary>
    public const int AniosHistorico = 5;

    private DateTime Hoy => (hoy?.Invoke() ?? DateTime.Today).Date;

    /// <summary>
    /// Carga los activos financieros de la sociedad (también los vendidos o de baja). Con <paramref name="conRed"/>, completa el histórico y pide el precio
    /// de hoy; sin red, usa solo lo guardado.
    /// </summary>
    public async Task<CarteraCargada> CargarAsync(string idSociedad, bool conRed, IProgress<string>? progreso = null, CancellationToken cancelacion = default)
    {
        var hoyDia = Hoy;
        var lista = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT a.IdActivo, a.Codigo, a.Nombre, a.FechaBaja FROM Activos AS a INNER JOIN ActivosInversiones AS ai ON a.IdActivo = ai.IdActivo WHERE a.IdSociedad = ? ORDER BY a.Codigo",
            f => (Id: f.Entero("IdActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Baja: f.FechaNula("FechaBaja")), idSociedad));
        var avisos = new List<string>();
        var resultado = new List<ActivoDeCartera>();
        // Varias sociedades o activos pueden compartir instrumento: se actualiza una vez.
        var hechos = new Dictionary<int, (SeriePrecios Serie, PrecioEnEuros? Hoy)>();
        foreach (var a in lista)
        {
            cancelacion.ThrowIfCancellationRequested();
            var inv = await activos.InversionAsync(a.Id);
            if (inv is null) continue;
            if (!hechos.TryGetValue(inv.IdInstrumento, out var datos))
            {
                progreso?.Report($"Cotizaciones de {a.Codigo}…");
                var primera = inv.Operaciones.Count == 0 ? hoyDia : inv.Operaciones.Min(o => o.Fecha);
                datos = await PreciosAsync(inv, a.Codigo, primera, conRed, avisos, cancelacion);
                hechos[inv.IdInstrumento] = datos;
            }
            resultado.Add(new ActivoDeCartera(a.Id, a.Codigo, a.Nombre, a.Baja, PosicionFinanciera.Crear(a.Id, a.Codigo, inv, datos.Serie, datos.Hoy, hoyDia)));
        }
        return new CarteraCargada(resultado, avisos, hoyDia, conRed);
    }

    /// <summary>
    /// Carga un solo activo financiero con su posición (para su ficha). Con <paramref name="conRed"/>, completa su histórico y pide el precio de hoy; sin
    /// red, usa solo lo guardado. Null si el activo no es financiero o no existe.
    /// </summary>
    public async Task<(ActivoDeCartera? Activo, IReadOnlyList<string> Avisos)> CargarActivoAsync(int idActivo, bool conRed, CancellationToken cancelacion = default)
    {
        var hoyDia = Hoy;
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT a.IdActivo, a.Codigo, a.Nombre, a.FechaBaja FROM Activos AS a INNER JOIN ActivosInversiones AS ai ON a.IdActivo = ai.IdActivo WHERE a.IdActivo = ?",
            f => (Id: f.Entero("IdActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Baja: f.FechaNula("FechaBaja")), idActivo));
        var avisos = new List<string>();
        if (filas.Count == 0) return (null, avisos);
        var a = filas[0];
        var inv = await activos.InversionAsync(a.Id);
        if (inv is null) return (null, avisos);
        var primera = inv.Operaciones.Count == 0 ? hoyDia : inv.Operaciones.Min(o => o.Fecha);
        var (serie, precio) = await PreciosAsync(inv, a.Codigo, primera, conRed, avisos, cancelacion);
        return (new ActivoDeCartera(a.Id, a.Codigo, a.Nombre, a.Baja, PosicionFinanciera.Crear(a.Id, a.Codigo, inv, serie, precio, hoyDia)), avisos);
    }

    private async Task<(SeriePrecios, PrecioEnEuros?)> PreciosAsync(InversionDeActivo inv, string codigo, DateTime primeraCompra, bool conRed, List<string> avisos,
        CancellationToken cancelacion)
    {
        var hoyDia = Hoy;
        var id = inv.IdInstrumento;
        var ins = inv.Instrumento;
        var guardadas = await LeerAsync(id);
        PrecioEnEuros? precioHoy = null;
        if (conRed && (ins.TickerYahoo is { Length: > 0 } || ins.TickerEodhd is { Length: > 0 }))
        {
            var desde = new DateTime(Math.Min(primeraCompra.Ticks, hoyDia.AddYears(-AniosHistorico).Ticks)).AddDays(-ServicioCotizaciones.DiasHaciaAtras);
            GraficoYahoo? grafico = null;
            if (ins.TickerYahoo is { Length: > 0 } ticker)
            {
                var pedirDesde = DesdeQuePedir(guardadas, desde);
                var g = await yahoo.GraficoAsync(ticker, pedirDesde, hoyDia, cancelacion);
                if (g.EsCorrecto)
                {
                    grafico = g.Valor;
                    await GuardarCierresAsync(id, guardadas, g.Valor, hoyDia);
                    guardadas = await LeerAsync(id);
                }
                else avisos.Add($"{codigo}: no he podido actualizar el histórico con Yahoo Finance ({g.Errores.FirstOrDefault()?.Mensaje}). Se usan las cotizaciones guardadas.");
            }

            // Primera apertura del día hábil: precio de EODHD (o de Yahoo si no hay clave), que se guarda. Después, el del momento de Yahoo.
            var habil = UltimoDiaHabil(hoyDia);
            var primeraDelDia = !guardadas.Any(c => c.Fecha >= habil);
            if (primeraDelDia)
            {
                var r = await cotizaciones.PrecioEnEurosAsync(ins.Referencia, hoyDia, cancelacion);
                if (r.EsCorrecto)
                {
                    precioHoy = r.Valor;
                    await GuardarSiNoHayAsync(id, r.Valor);
                    guardadas = await LeerAsync(id);
                }
                else avisos.Add($"{codigo}: no he podido obtener la cotización de hoy ({r.Errores.FirstOrDefault()?.Mensaje}). Se usa la última guardada.");
            }
            else if (grafico is { UltimoPrecio: decimal p, FechaUltimoPrecio: DateTime fp } && fp >= guardadas[^1].Fecha)
            {
                var (precio, moneda) = ServicioCotizaciones.AMonedaIso(p, grafico.Moneda);
                var cambio = await cambios.CambioAsync(moneda, fp, cancelacion);
                if (cambio.EsCorrecto)
                    precioHoy = new PrecioEnEuros(precio, moneda, fp, cambio.Valor.UnidadesPorEuro, cambio.Valor.Fecha, ServicioCotizaciones.AEuros(precio, cambio.Valor.UnidadesPorEuro),
                        "Yahoo Finance · del momento", FuenteCotizacion.Yahoo);
            }
        }

        var serie = await SerieEnEurosAsync(guardadas, conRed, codigo, avisos, cancelacion);
        if (precioHoy is null && guardadas.Count > 0 && serie.Ultimo is { } u)
        {
            var g = guardadas[^1];
            precioHoy = new PrecioEnEuros(g.Precio, g.Moneda, g.Fecha, 0m, g.Fecha, u.Precio, g.Origen is { Length: > 0 } o ? $"{o} (guardada)" : "guardada", ins.Origen);
        }
        return (serie, precioHoy);
    }

    /// <summary>
    /// Desde qué día pedir el histórico: desde <paramref name="desde"/> si no hay nada guardado o lo guardado empieza más tarde; si no, desde unos días antes
    /// del último cierre guardado (para recoger correcciones del proveedor y sustituir los precios del momento).
    /// </summary>
    public static DateTime DesdeQuePedir(IReadOnlyList<CotizacionGuardada> guardadas, DateTime desde)
    {
        var cierres = guardadas.Where(c => !c.DelMomento).ToList();
        if (cierres.Count == 0 || cierres[0].Fecha > desde.AddDays(ServicioCotizaciones.DiasHaciaAtras)) return desde;
        return cierres[^1].Fecha.AddDays(-ServicioCotizaciones.DiasHaciaAtras);
    }

    /// <summary>El día hábil (lunes a viernes) más reciente hasta hoy.</summary>
    public static DateTime UltimoDiaHabil(DateTime hoy) => hoy.DayOfWeek switch
    {
        DayOfWeek.Saturday => hoy.AddDays(-1).Date,
        DayOfWeek.Sunday => hoy.AddDays(-2).Date,
        _ => hoy.Date,
    };

    /// <summary>
    /// Cierres que hay que guardar: los de días anteriores a hoy que no estén guardados, y los que sustituyen a un precio del momento de un día ya pasado.
    /// </summary>
    public static (IReadOnlyList<(DateTime Fecha, decimal Precio)> Nuevos, IReadOnlyList<(DateTime Fecha, decimal Precio)> Sustituyen) CierresAGuardar(
        IReadOnlyList<CotizacionGuardada> guardadas, IReadOnlyList<(DateTime Fecha, decimal Cierre)> cierres, DateTime hoy)
    {
        var porFecha = guardadas.ToDictionary(g => g.Fecha.Date);
        var nuevos = new List<(DateTime, decimal)>();
        var sustituyen = new List<(DateTime, decimal)>();
        foreach (var (f, c) in cierres.Where(x => x.Fecha.Date < hoy.Date))
        {
            if (!porFecha.TryGetValue(f.Date, out var g)) nuevos.Add((f.Date, c));
            else if (g.DelMomento) sustituyen.Add((f.Date, c));
        }
        return (nuevos, sustituyen);
    }

    private Task<IReadOnlyList<CotizacionGuardada>> LeerAsync(int idInstrumento) => bd.EjecutarAsync<IReadOnlyList<CotizacionGuardada>>(async s => await s.ConsultarAsync(
        "SELECT FechaCotizacion, Precio, Moneda, Origen FROM CotizacionesInstrumentoFinanciero WHERE IdInstrumentoFinanciero = ? ORDER BY FechaCotizacion",
        f => new CotizacionGuardada(f.Fecha("FechaCotizacion").Date, f.Decimal("Precio"), (f.Texto("Moneda") ?? "EUR").Trim(), f.Texto("Origen")?.Trim()), idInstrumento));

    private Task GuardarCierresAsync(int idInstrumento, IReadOnlyList<CotizacionGuardada> guardadas, GraficoYahoo g, DateTime hoy)
    {
        var moneda = ServicioCotizaciones.AMonedaIso(0m, g.Moneda).Moneda;
        var cierres = g.Cierres.Select(c => (c.Fecha, ServicioCotizaciones.AMonedaIso(c.Cierre, g.Moneda).Precio)).ToList();
        var (nuevos, sustituyen) = CierresAGuardar(guardadas, cierres, hoy);
        if (nuevos.Count == 0 && sustituyen.Count == 0) return Task.CompletedTask;
        const string origen = "Yahoo Finance · cierre";
        return bd.EnTransaccionAsync(async s =>
        {
            var ahora = DateTime.Now;
            foreach (var (f, p) in nuevos)
                await s.EjecutarAsync("INSERT INTO CotizacionesInstrumentoFinanciero (IdInstrumentoFinanciero, FechaCotizacion, Precio, Moneda, Origen, FechaHoraImportacion) VALUES (?, ?, ?, ?, ?, ?)",
                    idInstrumento, f, Math.Round(p, ServicioCotizaciones.DecimalesPrecio, MidpointRounding.AwayFromZero), moneda, origen, ahora);
            foreach (var (f, p) in sustituyen)
                await s.EjecutarAsync("UPDATE CotizacionesInstrumentoFinanciero SET Precio = ?, Moneda = ?, Origen = ?, FechaHoraImportacion = ? WHERE IdInstrumentoFinanciero = ? AND FechaCotizacion = ?",
                    Math.Round(p, ServicioCotizaciones.DecimalesPrecio, MidpointRounding.AwayFromZero), moneda, origen, ahora, idInstrumento, f);
            return true;
        });
    }

    private Task GuardarSiNoHayAsync(int idInstrumento, PrecioEnEuros p) => bd.EnTransaccionAsync(async s =>
    {
        var hay = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero WHERE IdInstrumentoFinanciero = ? AND FechaCotizacion = ?",
            idInstrumento, p.FechaCotizacion.Date), CultureInfo.InvariantCulture) > 0;
        if (!hay)
            await s.EjecutarAsync("INSERT INTO CotizacionesInstrumentoFinanciero (IdInstrumentoFinanciero, FechaCotizacion, Precio, Moneda, Origen, FechaHoraImportacion) VALUES (?, ?, ?, ?, ?, ?)",
                idInstrumento, p.FechaCotizacion.Date, p.Precio, p.Moneda, p.Origen.Length <= 50 ? p.Origen : p.Origen[..50], DateTime.Now);
        return true;
    });

    private async Task<SeriePrecios> SerieEnEurosAsync(IReadOnlyList<CotizacionGuardada> guardadas, bool conRed, string codigo, List<string> avisos, CancellationToken cancelacion)
    {
        if (guardadas.Count == 0) return new SeriePrecios([]);
        var monedas = guardadas.Select(g => g.Moneda.ToUpperInvariant()).Distinct().Where(m => m != "EUR").ToList();
        var tablas = new Dictionary<string, IReadOnlyList<(DateTime, decimal)>>();
        foreach (var m in monedas)
        {
            if (conRed)
            {
                var (c, aviso) = await cambios.CambiosAsync(m, guardadas[0].Fecha, guardadas[^1].Fecha, cancelacion);
                if (aviso is not null) avisos.Add($"{codigo}: no he podido traer del BCE el cambio del euro con {m} ({aviso}).");
                tablas[m] = c;
            }
            else tablas[m] = await cambios.CambiosGuardadosAsync(m, guardadas[0].Fecha, guardadas[^1].Fecha);
        }
        return HistoricoEnEuros(guardadas, tablas);
    }

    /// <summary>
    /// Pasa a euros las cotizaciones guardadas con el cambio del BCE del mismo día o del último publicado antes (como mucho
    /// <see cref="ServicioCambiosDivisa.DiasHaciaAtras"/> días); un día sin cambio se queda fuera.
    /// </summary>
    public static SeriePrecios HistoricoEnEuros(IReadOnlyList<CotizacionGuardada> guardadas, IReadOnlyDictionary<string, IReadOnlyList<(DateTime Fecha, decimal UnidadesPorEuro)>> cambios)
    {
        var series = cambios.ToDictionary(k => k.Key.ToUpperInvariant(), k => k.Value.OrderBy(x => x.Fecha).ToArray());
        var puntos = new List<(DateTime, decimal)>();
        foreach (var g in guardadas)
        {
            var m = g.Moneda.ToUpperInvariant();
            if (m == "EUR") { puntos.Add((g.Fecha, g.Precio)); continue; }
            if (!series.TryGetValue(m, out var s) || s.Length == 0) continue;
            var i = Array.FindLastIndex(s, x => x.Fecha <= g.Fecha);
            if (i < 0 || (g.Fecha - s[i].Fecha).TotalDays > ServicioCambiosDivisa.DiasHaciaAtras) continue;
            puntos.Add((g.Fecha, ServicioCotizaciones.AEuros(g.Precio, s[i].UnidadesPorEuro)));
        }
        return new SeriePrecios(puntos);
    }
}
