namespace ProConta.Activos.Financieros;

/// <summary>El instrumento financiero de un activo (InstrumentosFinancieros): ISIN, nombre, símbolos, bolsa, moneda, tipo y de dónde salieron sus datos.</summary>
public sealed record DatosInstrumento(string Isin, string Nombre, string? TickerYahoo, string? TickerEodhd, string? Bolsa, string Moneda, string TipoInstrumento,
    FuenteCotizacion Origen)
{
    public ReferenciaCotizacion Referencia => new(TickerYahoo, TickerEodhd, Moneda);

    public static DatosInstrumento De(CotizacionIsin c) => new(c.Isin, c.Nombre, c.TickerYahoo, c.TickerEodhd, c.Bolsa, c.Moneda, c.TipoInstrumento, c.Fuente);
}

/// <summary>
/// Una compra de participaciones (activos/activos-financieros 2026-11-23 20:32, B): el precio del día es un coste orientativo y el coste total del lote se
/// corrige después con el importe que confirma el bróker. No genera asiento (2026-11-23 20:38): el de banco (572 contra 540) sale de las líneas bancarias.
/// </summary>
/// <param name="PrecioUnitario">Precio de una participación en <paramref name="Moneda"/>.</param>
/// <param name="UnidadesPorEuro">Cambio del BCE usado (1 en euros).</param>
/// <param name="ImporteTotalEur">Coste total del lote en euros: participaciones × precio en euros, a céntimos, o el que confirma el bróker.</param>
/// <param name="Cotizacion">El precio que se pidió al proveedor (se guarda en el histórico de cotizaciones); null si se escribió a mano.</param>
public sealed record CompraInversion(DateTime Fecha, decimal Participaciones, decimal PrecioUnitario, string Moneda, decimal UnidadesPorEuro, decimal ImporteTotalEur,
    PrecioEnEuros? Cotizacion)
{
    public const int DecimalesParticipaciones = 6;

    /// <summary>Coste del lote propuesto: participaciones × precio en euros, a céntimos (al alza en el medio).</summary>
    public static decimal CostePropuesto(decimal participaciones, decimal precioEur) =>
        Math.Round(participaciones * precioEur, 2, MidpointRounding.AwayFromZero);

    /// <summary>Errores de la compra, con qué hacer.</summary>
    public IReadOnlyList<string> Validar(DateTime hoy)
    {
        var e = new List<string>();
        if (Fecha.Date > hoy.Date) e.Add("La fecha de compra no puede ser posterior a hoy.");
        if (Participaciones <= 0m) e.Add("Escribe cuántas participaciones has comprado (más de cero; pueden llevar decimales, por ejemplo 12,345).");
        else if (decimal.Round(Participaciones, DecimalesParticipaciones) != Participaciones)
            e.Add($"Las participaciones admiten como mucho {DecimalesParticipaciones} decimales.");
        if (PrecioUnitario <= 0m) e.Add("Falta el precio de la participación: pulsa «Consultar precio» o escríbelo a mano.");
        if (UnidadesPorEuro <= 0m) e.Add("Falta el cambio de la moneda a euros.");
        if (ImporteTotalEur <= 0m) e.Add("El coste total del lote en euros debe ser mayor que cero.");
        return e;
    }
}

/// <summary>Una operación de un activo financiero (OperacionesInversion) para el apartado Operaciones de la ficha.</summary>
public sealed record OperacionDeInversion(int IdOperacion, int Tipo, DateTime Fecha, decimal Participaciones, decimal PrecioUnitario, string Moneda, decimal UnidadesPorEuro,
    decimal ImporteTotalEur, string? Notas)
{
    public const int TipoCompra = 1, TipoVenta = 2;
    public bool EsCompra => Tipo == TipoCompra;

    /// <summary>Precio en euros que resulta del coste del lote (lo que de verdad costó cada participación).</summary>
    public decimal PrecioEurEfectivo => Participaciones == 0m ? 0m : Math.Round(ImporteTotalEur / Participaciones, ServicioCotizaciones.DecimalesPrecio, MidpointRounding.AwayFromZero);
}

/// <summary>El instrumento y las operaciones de un activo financiero.</summary>
public sealed record InversionDeActivo(int IdActivo, int IdInstrumento, DatosInstrumento Instrumento, decimal Participaciones, IReadOnlyList<OperacionDeInversion> Operaciones)
{
    public decimal Coste => Operaciones.Where(o => o.EsCompra).Sum(o => o.ImporteTotalEur);
}
