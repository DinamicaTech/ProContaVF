using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Financieros;

/// <summary>De dónde salen los datos de un instrumento o una cotización.</summary>
public enum FuenteCotizacion
{
    /// <summary>Yahoo Finance (sin API oficial): búsqueda por ISIN, cotización del momento e histórico diario.</summary>
    Yahoo,

    /// <summary>EOD Historical Data con la clave de Integraciones (plan gratuito: 20 consultas al día).</summary>
    Eodhd,

    /// <summary>Escrito por el operador porque ningún proveedor lo encuentra.</summary>
    Manual,
}

/// <summary>
/// Un instrumento encontrado por su ISIN en una bolsa y una moneda. Un mismo ETF puede cotizar en varias (por ejemplo, en Xetra en euros y en Londres en
/// dólares): el operador elige una y se le propone la de euros (activos/activos-financieros 2026-11-23 20:32, A).
/// </summary>
/// <param name="TickerYahoo">Símbolo en Yahoo Finance (IWDA.AS); null si Yahoo no lo tiene.</param>
/// <param name="TickerEodhd">Símbolo en EODHD (IWDA.AS, EUNL.XETRA o el ISIN.EUFUND de un fondo); null si no se conoce.</param>
/// <param name="Moneda">Moneda de cotización en ISO 4217 (EUR, USD…); GBp o GBX son peniques.</param>
/// <param name="TipoInstrumento">ETF, FONDO, ACCION u otro texto del proveedor.</param>
public sealed record CotizacionIsin(string Isin, string Nombre, string? TickerYahoo, string? TickerEodhd, string Bolsa, string Moneda, string TipoInstrumento, FuenteCotizacion Fuente)
{
    public bool EnEuros => string.Equals(Moneda, "EUR", StringComparison.OrdinalIgnoreCase);

    /// <summary>Texto para elegirla en una lista: bolsa, moneda y símbolo.</summary>
    public string Texto => $"{Bolsa} · {Moneda} · {TickerYahoo ?? TickerEodhd ?? "sin símbolo"}";
}

/// <summary>
/// Resultado de buscar un ISIN (2026-11-23 20:32, D): primero en Yahoo; si no está, en EODHD (aviso leve, porque las cotizaciones siguientes se piden a
/// Yahoo); si tampoco, ninguna cotización y un aviso grave. En los dos casos con aviso se ofrece abrir Yahoo en el navegador para buscarlo a mano.
/// </summary>
public sealed record BusquedaIsin(string Isin, IReadOnlyList<CotizacionIsin> Cotizaciones, FuenteCotizacion? EncontradoEn, string? Aviso)
{
    /// <summary>Ningún proveedor lo encuentra: el operador puede seguir escribiendo nombre y ticker a mano.</summary>
    public bool AvisoGrave => Cotizaciones.Count == 0;

    /// <summary>Página de búsqueda de Yahoo Finance para ese ISIN.</summary>
    public string UrlYahoo => UrlsCotizaciones.BusquedaYahoo(Isin);

    /// <summary>La que se propone: la primera en euros, o la primera.</summary>
    public CotizacionIsin? Propuesta => Cotizaciones.FirstOrDefault(c => c.EnEuros) ?? Cotizaciones.FirstOrDefault();
}

/// <summary>
/// Con qué se pide el precio de un instrumento: sus símbolos y su moneda (vacía si no se sabe: la da Yahoo). No hace falta que exista en la BD.
/// </summary>
public sealed record ReferenciaCotizacion(string? TickerYahoo, string? TickerEodhd, string Moneda);

/// <summary>
/// Precio de un instrumento en una fecha, en su moneda y en euros con el cambio del BCE (activos/activos-financieros 2026-11-23 20:24).
/// </summary>
/// <param name="Precio">Precio en la moneda de cotización (en libras si cotiza en peniques).</param>
/// <param name="Moneda">Moneda del precio (GBP si cotizaba en peniques).</param>
/// <param name="FechaCotizacion">Día de la cotización: el pedido o el último hábil anterior.</param>
/// <param name="UnidadesPorEuro">Cambio del BCE: unidades de la moneda por un euro (1 si es EUR).</param>
/// <param name="FechaCambio">Día del cambio del BCE (el de la cotización o el último publicado antes).</param>
/// <param name="PrecioEur">Precio / cambio, a 6 decimales (al alza en el medio).</param>
/// <param name="Origen">Texto corto del origen: «Yahoo Finance · cierre», «EODHD · tiempo real»…</param>
public sealed record PrecioEnEuros(decimal Precio, string Moneda, DateTime FechaCotizacion, decimal UnidadesPorEuro, DateTime FechaCambio, decimal PrecioEur, string Origen,
    FuenteCotizacion Fuente);

/// <summary>
/// Servicio de cotizaciones de los activos financieros. Lo usan el alta por ISIN y «Nueva compra»; la vista de análisis lo reutiliza
/// (activos/activos-financieros 2026-11-23 20:24).
/// </summary>
public interface ICotizaciones
{
    /// <summary>Busca el ISIN en Yahoo y, si no está, en EODHD. El ISIN ya debe ser válido (<see cref="Isin.Error"/>).</summary>
    Task<Resultado<BusquedaIsin>> BuscarPorIsinAsync(string isin, CancellationToken cancelacion = default);

    /// <summary>
    /// Precio en una fecha y en euros. El día en curso sale de EODHD (si hay clave y símbolo) y si no de Yahoo; las fechas pasadas, del histórico diario
    /// de Yahoo y si no de EODHD. Si la fecha no es hábil, la del último día hábil anterior.
    /// </summary>
    Task<Resultado<PrecioEnEuros>> PrecioEnEurosAsync(ReferenciaCotizacion referencia, DateTime fecha, CancellationToken cancelacion = default);
}

/// <summary>Direcciones públicas de los proveedores, para abrirlas en el navegador.</summary>
public static class UrlsCotizaciones
{
    public static string BusquedaYahoo(string isin) => $"https://finance.yahoo.com/lookup?s={Uri.EscapeDataString(isin)}";

    public static string FichaYahoo(string ticker) => $"https://finance.yahoo.com/quote/{Uri.EscapeDataString(ticker)}";
}
