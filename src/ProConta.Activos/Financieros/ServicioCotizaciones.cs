using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Financieros;

/// <summary>
/// Servicio de cotizaciones de los activos financieros (activos/activos-financieros 2026-11-23 20:11, 20:24 y 20:32):
/// <list type="bullet">
/// <item>búsqueda por ISIN en Yahoo Finance y, si no está, en EODHD; cada resultado con su bolsa y su moneda;</item>
/// <item>precio en una fecha: el día en curso de EODHD (si hay clave y símbolo) y si no de Yahoo; las fechas pasadas, del histórico diario de Yahoo y si
/// no de EODHD; si la fecha no es hábil, el último día hábil anterior (hasta <see cref="DiasHaciaAtras"/> días);</item>
/// <item>conversión a euros con el cambio del BCE de ese día (<see cref="ServicioCambiosDivisa"/>); los peniques (GBp, GBX) pasan a libras.</item>
/// </list>
/// </summary>
/// <param name="claveEodhd">Lee la clave de EODHD de la sociedad (null si no hay o está desactivada).</param>
/// <param name="hoy">El día de hoy (en pruebas, fijo).</param>
public sealed class ServicioCotizaciones(ClienteYahooFinance yahoo, ClienteEodhd eodhd, ServicioCambiosDivisa cambios, Func<Task<string?>> claveEodhd, Func<DateTime>? hoy = null)
    : ICotizaciones
{
    public const int DiasHaciaAtras = 10;

    /// <summary>Decimales del precio unitario en euros (el importe del lote se redondea a céntimos aparte).</summary>
    public const int DecimalesPrecio = 6;

    private DateTime Hoy => (hoy?.Invoke() ?? DateTime.Today).Date;

    public async Task<Resultado<BusquedaIsin>> BuscarPorIsinAsync(string isin, CancellationToken cancelacion = default)
    {
        isin = Isin.Normalizar(isin);
        if (Isin.Error(isin) is { } error) return Resultado<BusquedaIsin>.Fallo(new Error("Isin.NoValido", error));

        // 1. Yahoo: la búsqueda da símbolo, nombre y bolsa; la moneda sale del gráfico de cada símbolo.
        var busqueda = await yahoo.BuscarAsync(isin, cancelacion);
        var errorYahoo = busqueda.EsCorrecto ? null : busqueda.Error;
        var deYahoo = new List<CotizacionIsin>();
        if (busqueda.EsCorrecto)
            foreach (var y in busqueda.Valor.Take(6))
            {
                var g = await yahoo.GraficoAsync(y.Simbolo, Hoy.AddDays(-7), Hoy, cancelacion);
                if (!g.EsCorrecto) continue;
                deYahoo.Add(new CotizacionIsin(isin, y.Nombre, y.Simbolo, TickerEodhdDesdeYahoo(y.Simbolo, isin, y.Tipo), y.Bolsa.Length > 0 ? y.Bolsa : g.Valor.Bolsa,
                    g.Valor.Moneda, y.Tipo, FuenteCotizacion.Yahoo));
            }
        // La búsqueda de Yahoo a veces da solo la bolsa de Londres (IWDA.L en dólares): se prueba el mismo símbolo en bolsas en euros y se
        // acepta si el gráfico da el mismo nombre de instrumento.
        if (deYahoo.Count > 0 && !deYahoo.Any(c => c.EnEuros))
            foreach (var (baseSimbolo, nombre, tipo) in deYahoo.Select(c => (Base: c.TickerYahoo!.Split('.')[0], c.Nombre, c.TipoInstrumento)).DistinctBy(x => x.Base).ToList())
                foreach (var sufijo in SufijosEuro)
                {
                    var simbolo = baseSimbolo + sufijo;
                    if (deYahoo.Any(c => string.Equals(c.TickerYahoo, simbolo, StringComparison.OrdinalIgnoreCase))) continue;
                    var g = await yahoo.GraficoAsync(simbolo, Hoy.AddDays(-7), Hoy, cancelacion);
                    if (!g.EsCorrecto || !string.Equals(g.Valor.Moneda, "EUR", StringComparison.OrdinalIgnoreCase) || !MismoNombre(g.Valor.Nombre, nombre)) continue;
                    deYahoo.Add(new CotizacionIsin(isin, nombre, simbolo, TickerEodhdDesdeYahoo(simbolo, isin, tipo), g.Valor.Bolsa, "EUR", tipo, FuenteCotizacion.Yahoo));
                }
        if (deYahoo.Count > 0) return Resultado<BusquedaIsin>.Correcto(new BusquedaIsin(isin, Ordenar(deYahoo), FuenteCotizacion.Yahoo, null));

        // 2. EODHD, si hay clave.
        var clave = await claveEodhd();
        var porQueYahoo = errorYahoo is null ? "Yahoo Finance no encuentra este ISIN" : $"Yahoo Finance no ha respondido ({errorYahoo.Mensaje.TrimEnd('.')})";
        if (string.IsNullOrWhiteSpace(clave))
            return Resultado<BusquedaIsin>.Correcto(new BusquedaIsin(isin, [], null,
                $"{porQueYahoo} y no puedo probar con EODHD porque no hay clave guardada (Parámetros y tablas › Integraciones › «Cotizaciones · EODHD»). " +
                "Búscalo en Yahoo con el navegador para ver con qué símbolo cotiza; si prefieres seguir, escribe el nombre y el símbolo a mano."));
        var e = await eodhd.BuscarAsync(clave, isin, cancelacion);
        if (!e.EsCorrecto)
            return Resultado<BusquedaIsin>.Correcto(new BusquedaIsin(isin, [], null,
                $"{porQueYahoo} y EODHD tampoco ha podido buscarlo: {e.Error!.TextoOperador()} Búscalo en Yahoo con el navegador; si prefieres seguir, escribe el nombre y el símbolo a mano."));
        if (e.Valor.Count == 0)
            return Resultado<BusquedaIsin>.Correcto(new BusquedaIsin(isin, [], null,
                $"{porQueYahoo} ni EODHD. Sin un ISIN que encuentre algún proveedor no se podrán consultar sus cotizaciones. Búscalo en Yahoo con el navegador " +
                "por si cotiza con otro código; si prefieres seguir, escribe el nombre y el símbolo a mano."));
        var deEodhd = e.Valor.Select(x => new CotizacionIsin(isin, x.Nombre, null, x.Simbolo, x.Bolsa, x.Moneda.Length > 0 ? x.Moneda : "EUR", x.Tipo, FuenteCotizacion.Eodhd)).ToList();
        return Resultado<BusquedaIsin>.Correcto(new BusquedaIsin(isin, Ordenar(deEodhd), FuenteCotizacion.Eodhd,
            $"{porQueYahoo}, pero EODHD sí. Las cotizaciones del día se pedirán a EODHD (20 consultas al día en el plan gratuito) y no habrá respaldo de Yahoo. " +
            "Si lo encuentras en Yahoo con el navegador, escribe su símbolo de Yahoo para tener los dos."));
    }

    /// <summary>Bolsas en euros donde se prueba el símbolo de Yahoo cuando la búsqueda no da ninguna: Ámsterdam, Xetra, Milán y París.</summary>
    private static readonly string[] SufijosEuro = [".AS", ".DE", ".MI", ".PA"];

    internal static bool MismoNombre(string? a, string? b)
    {
        static string N(string? t) => new((t ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return N(a).Length > 0 && N(a) == N(b);
    }

    public async Task<Resultado<PrecioEnEuros>> PrecioEnEurosAsync(ReferenciaCotizacion referencia, DateTime fecha, CancellationToken cancelacion = default)
    {
        fecha = fecha.Date;
        if (fecha > Hoy)
            return Resultado<PrecioEnEuros>.Fallo(new Error("Cotizacion.FechaFutura", $"No hay cotización del {fecha:dd/MM/yyyy}: es una fecha futura.", null, null,
                "Escribe una fecha de hoy o anterior."));
        if (string.IsNullOrWhiteSpace(referencia.TickerYahoo) && string.IsNullOrWhiteSpace(referencia.TickerEodhd))
            return Resultado<PrecioEnEuros>.Fallo(new Error("Cotizacion.SinSimbolo", "Sin un símbolo de Yahoo o de EODHD no puedo pedir la cotización.", null, null,
                "Escribe el símbolo de Yahoo (por ejemplo IWDA.AS) o el precio a mano."));

        var errores = new List<Error>();
        // Sin moneda (un símbolo escrito a mano), la da el gráfico de Yahoo; si tampoco, euros.
        var monedaReferencia = string.IsNullOrWhiteSpace(referencia.Moneda) ? null : referencia.Moneda;
        (decimal Precio, DateTime Fecha, string Origen, FuenteCotizacion Fuente)? hallado = null;
        var clave = string.IsNullOrWhiteSpace(referencia.TickerEodhd) ? null : await claveEodhd();
        var pasos = fecha == Hoy
            ? new Func<Task<(decimal, DateTime, string, FuenteCotizacion)?>>[] { () => EodhdHoyAsync(), () => YahooAsync(), () => EodhdCierreAsync() }
            : [() => YahooAsync(), () => EodhdCierreAsync()];
        foreach (var paso in pasos)
            if ((hallado = await paso()) is not null) break;
        if (hallado is not { } h)
            return Resultado<PrecioEnEuros>.Fallo(errores.Count > 0 ? errores
                : [new Error("Cotizacion.SinPrecio", $"No encuentro la cotización del {fecha:dd/MM/yyyy} ni de los {DiasHaciaAtras} días anteriores.", null,
                    "El proveedor no tiene datos de esas fechas para este símbolo.", "Escribe el precio a mano.")]);

        var (precio, moneda) = AMonedaIso(h.Precio, monedaReferencia ?? "EUR");
        var cambio = await cambios.CambioAsync(moneda, h.Fecha, cancelacion);
        if (!cambio.EsCorrecto) return Resultado<PrecioEnEuros>.Fallo(cambio.Errores);
        return Resultado<PrecioEnEuros>.Correcto(new PrecioEnEuros(precio, moneda, h.Fecha, cambio.Valor.UnidadesPorEuro, cambio.Valor.Fecha,
            AEuros(precio, cambio.Valor.UnidadesPorEuro), h.Origen, h.Fuente));

        async Task<(decimal, DateTime, string, FuenteCotizacion)?> EodhdHoyAsync()
        {
            if (string.IsNullOrWhiteSpace(clave)) return null;
            var r = await eodhd.TiempoRealAsync(clave, referencia.TickerEodhd!, cancelacion);
            if (!r.EsCorrecto) { errores.AddRange(r.Errores); return null; }
            return r.Valor is { } v && v.Fecha <= fecha && v.Fecha > fecha.AddDays(-DiasHaciaAtras) ? (v.Precio, v.Fecha, "EODHD · del momento", FuenteCotizacion.Eodhd) : null;
        }

        async Task<(decimal, DateTime, string, FuenteCotizacion)?> YahooAsync()
        {
            if (string.IsNullOrWhiteSpace(referencia.TickerYahoo)) return null;
            var r = await yahoo.GraficoAsync(referencia.TickerYahoo!, fecha.AddDays(-DiasHaciaAtras), fecha, cancelacion);
            if (!r.EsCorrecto) { errores.AddRange(r.Errores); return null; }
            monedaReferencia ??= r.Valor.Moneda;
            if (fecha == Hoy && r.Valor.UltimoPrecio is decimal p && r.Valor.FechaUltimoPrecio is DateTime fp && fp <= fecha && fp > fecha.AddDays(-DiasHaciaAtras))
                return (p, fp, "Yahoo Finance · del momento", FuenteCotizacion.Yahoo);
            return UltimoCierre(r.Valor.Cierres, fecha) is { } c ? (c.Cierre, c.Fecha, "Yahoo Finance · cierre", FuenteCotizacion.Yahoo) : null;
        }

        async Task<(decimal, DateTime, string, FuenteCotizacion)?> EodhdCierreAsync()
        {
            clave ??= string.IsNullOrWhiteSpace(referencia.TickerEodhd) ? null : await claveEodhd();
            if (string.IsNullOrWhiteSpace(clave)) return null;
            var r = await eodhd.CierresAsync(clave, referencia.TickerEodhd!, fecha.AddDays(-DiasHaciaAtras), fecha, cancelacion);
            if (!r.EsCorrecto) { errores.AddRange(r.Errores); return null; }
            return UltimoCierre(r.Valor, fecha) is { } c ? (c.Cierre, c.Fecha, "EODHD · cierre", FuenteCotizacion.Eodhd) : null;
        }
    }

    /// <summary>El cierre del día pedido o, si no lo hay, el del último día anterior dentro del margen.</summary>
    public static (DateTime Fecha, decimal Cierre)? UltimoCierre(IReadOnlyList<(DateTime Fecha, decimal Cierre)> cierres, DateTime fecha)
    {
        var c = cierres.Where(x => x.Fecha <= fecha && x.Fecha > fecha.AddDays(-DiasHaciaAtras)).OrderBy(x => x.Fecha).ToList();
        return c.Count == 0 ? null : c[^1];
    }

    /// <summary>Precio / unidades por euro, a <see cref="DecimalesPrecio"/> decimales (al alza en el medio).</summary>
    public static decimal AEuros(decimal precio, decimal unidadesPorEuro) =>
        unidadesPorEuro <= 0m ? 0m : Math.Round(precio / unidadesPorEuro, DecimalesPrecio, MidpointRounding.AwayFromZero);

    /// <summary>Los peniques (GBp, GBX) pasan a libras; el resto de monedas, igual (en mayúsculas).</summary>
    public static (decimal Precio, string Moneda) AMonedaIso(decimal precio, string moneda) =>
        moneda is "GBp" or "GBX" or "GBx" ? (precio / 100m, "GBP") : (precio, moneda.Trim().ToUpperInvariant());

    /// <summary>Primero los que cotizan en euros; dentro, el orden del proveedor.</summary>
    private static IReadOnlyList<CotizacionIsin> Ordenar(List<CotizacionIsin> c) => c.Select((x, i) => (x, i)).OrderBy(t => t.x.EnEuros ? 0 : 1).ThenBy(t => t.i).Select(t => t.x).ToList();

    /// <summary>Sufijo de bolsa de Yahoo → bolsa de EODHD.</summary>
    private static readonly IReadOnlyDictionary<string, string> BolsasEodhd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = "XETRA", ["F"] = "F", ["AS"] = "AS", ["PA"] = "PA", ["MI"] = "MI", ["MC"] = "MC", ["L"] = "LSE", ["SW"] = "SW", ["BR"] = "BR",
        ["LS"] = "LS", ["VI"] = "VI", ["IR"] = "IR", ["CO"] = "CO", ["ST"] = "ST", ["HE"] = "HE", ["OL"] = "OL", ["TO"] = "TO",
    };

    /// <summary>
    /// Símbolo de EODHD que corresponde a uno de Yahoo: los fondos van por ISIN (ISIN.EUFUND); el resto, el mismo código con la bolsa de EODHD (sin sufijo,
    /// EE. UU.). Null si la bolsa no se conoce.
    /// </summary>
    public static string? TickerEodhdDesdeYahoo(string simboloYahoo, string isin, string tipo)
    {
        if (tipo == "FONDO" || simboloYahoo.StartsWith("0P", StringComparison.OrdinalIgnoreCase)) return $"{isin}.EUFUND";
        var punto = simboloYahoo.LastIndexOf('.');
        if (punto < 0) return $"{simboloYahoo}.US";
        return BolsasEodhd.TryGetValue(simboloYahoo[(punto + 1)..], out var b) ? $"{simboloYahoo[..punto]}.{b}" : null;
    }
}
