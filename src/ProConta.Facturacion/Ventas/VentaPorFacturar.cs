namespace ProConta.Facturacion.Ventas;

/// <summary>Estado de una venta por facturar (facturacion/reservas 2026-11-14 11:45). Se deduce de sus datos, no se guarda.</summary>
public enum EstadoVenta
{
    Pendiente,
    EnBorrador,
    Facturada,
    Anulada
}

/// <summary>De dónde viene una venta.</summary>
public static class FuenteVenta
{
    public const string Manual = "MANUAL";
    public const string Avaibook = "AVAIBOOK";
    public const string Erp = "ERP";
}

/// <summary>
/// Venta registrada antes de facturarla (facturacion/reservas): una reserva de un piso turístico, un servicio… Se factura
/// después con «Facturar ventas…»: el concepto, el IVA y el cliente salen de la regla de venta de su origen y activo, salvo los
/// que la venta lleve propios.
/// </summary>
public sealed class VentaPorFacturar
{
    public int IdVentaPorFacturar { get; internal set; }
    public string IdSociedad { get; set; } = "";
    public int IdOrigenVenta { get; set; }
    public int? IdCliente { get; set; }
    public int? IdActivo { get; set; }
    public int? IdConcepto { get; set; }
    public DateTime FechaReferencia { get; set; }
    public DateTime? FechaFin { get; set; }
    public decimal Cantidad { get; set; } = 1m;
    public decimal Importe { get; set; }
    public decimal? Comision { get; set; }
    public decimal? TipoIva { get; set; }
    public string? CausaExencion { get; set; }
    public string? Descripcion { get; set; }
    public string? Referencia { get; set; }
    public string? Titular { get; set; }
    public bool Anulada { get; set; }
    public string? MotivoAnulacion { get; set; }
    public bool FacturadaFuera { get; set; }
    public int? IdFactura { get; internal set; }
    public string Fuente { get; set; } = FuenteVenta.Manual;
    public string? IdExterno { get; set; }
    public string? Huella { get; set; }
    public string? Incidencia { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaAlta { get; internal set; }
    public string? OperadorAlta { get; internal set; }
    public DateTime? FechaModificacion { get; internal set; }
    public int Version { get; internal set; }

    /// <summary>Estado de la factura enlazada (null si no tiene); lo rellena el repositorio.</summary>
    public Facturas.EstadoFactura? EstadoFacturaEnlazada { get; internal set; }

    /// <summary>Número (o rótulo del borrador) de la factura enlazada; lo rellena el repositorio.</summary>
    public string? RotuloFactura { get; internal set; }

    public bool EsNueva => IdVentaPorFacturar == 0;

    public EstadoVenta Estado =>
        Anulada ? EstadoVenta.Anulada
        : FacturadaFuera ? EstadoVenta.Facturada
        : IdFactura is null ? EstadoVenta.Pendiente
        : EstadoFacturaEnlazada == Facturas.EstadoFactura.Borrador ? EstadoVenta.EnBorrador
        : EstadoVenta.Facturada;

    /// <summary>Solo una venta pendiente se puede editar, anular o facturar.</summary>
    public bool EsEditable => Estado == EstadoVenta.Pendiente;

    public string Rotulo => EsNueva ? "Venta nueva" : $"Venta {IdVentaPorFacturar}";

    public static string RotuloDe(int id) => $"Venta {id}";

    public VentaPorFacturar Clonar() => (VentaPorFacturar)MemberwiseClone();

    public static string TextoEstado(EstadoVenta e) => e switch
    {
        EstadoVenta.Pendiente => "Pendiente",
        EstadoVenta.EnBorrador => "En borrador",
        EstadoVenta.Facturada => "Facturada",
        _ => "Anulada"
    };
}

/// <summary>Origen de ventas de una sociedad y con qué textos se reconoce al importar.</summary>
public sealed record OrigenVenta(int IdOrigenVenta, string IdSociedad, string Codigo, string Nombre, string? TextosImportacion, bool EsDirecta, bool Activo)
{
    /// <summary>Textos de importación, sin espacios sobrantes.</summary>
    public IReadOnlyList<string> Textos =>
        (TextosImportacion ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>¿Reconoce este origen el texto con que llega una venta importada (sin distinguir mayúsculas)?</summary>
    public bool Reconoce(string? texto) =>
        !string.IsNullOrWhiteSpace(texto) &&
        (Textos.Any(t => string.Equals(t, texto.Trim(), StringComparison.OrdinalIgnoreCase)) || string.Equals(Codigo, texto.Trim(), StringComparison.OrdinalIgnoreCase)
         || string.Equals(Nombre, texto.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Alojamiento de Avaibook y el activo relacionado de sus reservas.</summary>
public sealed record AlojamientoAvaibook(int IdAlojamientoAvaibook, string IdSociedad, string IdExterno, string? Nombre, int? IdActivo, bool Activo);

/// <summary>
/// Regla de venta (facturacion/reservas 2026-11-14 14:20): concepto, IVA y cliente que se aplican al facturar las ventas de
/// un origen y un activo relacionado. Sin activo, vale para todos los activos del origen que no tengan regla propia.
/// </summary>
public sealed record ReglaVenta(int IdReglaVenta, string IdSociedad, int IdOrigenVenta, int? IdActivo, int? IdConcepto, string? CausaExencion, decimal? TipoIva, int? IdCliente, bool Activa,
    string Agrupar = AgrupacionVentas.Mensual, bool SepararPorActivo = true, string AlFacturar = ReglaVenta.Borrador)
{
    public const string Borrador = "BORRADOR";
    public const string Validar = "VALIDAR";

    /// <summary>«Facturar todo» valida y contabiliza las facturas de esta regla en vez de dejarlas en borrador.</summary>
    public bool ValidaAlFacturar => string.Equals(AlFacturar, Validar, StringComparison.OrdinalIgnoreCase);

    /// <summary>La regla que corresponde a un origen y un activo: la del activo o, si no hay, la general del origen.</summary>
    public static ReglaVenta? Para(IEnumerable<ReglaVenta> reglas, int idOrigen, int? idActivo)
    {
        ReglaVenta? general = null;
        foreach (var r in reglas)
        {
            if (!r.Activa || r.IdOrigenVenta != idOrigen) continue;
            if (r.IdActivo is int a && a == idActivo) return r;
            if (r.IdActivo is null) general ??= r;
        }
        return general;
    }
}

/// <summary>Periodo de una factura de ventas: sus fechas y cómo se nombra en la descripción.</summary>
public sealed record PeriodoVentas(DateTime Desde, DateTime Hasta, string Texto);

/// <summary>
/// Cada cuánto se agrupan las ventas de una regla en una factura (facturacion/reservas 2026-11-14 17:15), por la fecha de
/// referencia de la venta: semanas de lunes a domingo, quincenas del 1 al 15 y del 16 al fin de mes, meses, trimestres y
/// años naturales. Sin agrupar, cada venta va en su factura con el periodo de la propia venta.
/// </summary>
public static class AgrupacionVentas
{
    public const string No = "NO";
    public const string Semanal = "SEMANAL";
    public const string Quincenal = "QUINCENAL";
    public const string Mensual = "MENSUAL";
    public const string Trimestral = "TRIMESTRAL";
    public const string Anual = "ANUAL";

    public static readonly IReadOnlyList<(string Codigo, string Texto)> Todas =
        [(No, "No agrupar (una factura por venta)"), (Semanal, "Semanales"), (Quincenal, "Quincenales"), (Mensual, "Mensuales"), (Trimestral, "Trimestrales"), (Anual, "Anuales")];

    private static readonly System.Globalization.CultureInfo Es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");

    public static string Normalizar(string? codigo) =>
        Todas.FirstOrDefault(t => string.Equals(t.Codigo, codigo?.Trim(), StringComparison.OrdinalIgnoreCase)).Codigo ?? Mensual;

    /// <summary>Periodo al que pertenece una venta según la agrupación.</summary>
    public static PeriodoVentas Periodo(string? agrupar, DateTime fecha, DateTime? fechaFin = null)
    {
        var d = fecha.Date;
        switch (Normalizar(agrupar))
        {
            case No:
                var fin = fechaFin is DateTime f && f.Date > d ? f.Date : d;
                return new(d, fin, fin > d ? $"{d:dd/MM/yyyy} – {fin:dd/MM/yyyy}" : d.ToString("dd/MM/yyyy", Es));
            case Semanal:
                var lunes = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
                return new(lunes, lunes.AddDays(6), $"semana del {lunes:dd/MM/yyyy} al {lunes.AddDays(6):dd/MM/yyyy}");
            case Quincenal:
                var mes = new DateTime(d.Year, d.Month, 1);
                return d.Day <= 15
                    ? new(mes, mes.AddDays(14), $"1.ª quincena de {mes.ToString("MMMM yyyy", Es)}")
                    : new(mes.AddDays(15), mes.AddMonths(1).AddDays(-1), $"2.ª quincena de {mes.ToString("MMMM yyyy", Es)}");
            case Trimestral:
                var t = (d.Month - 1) / 3;
                var inicio = new DateTime(d.Year, t * 3 + 1, 1);
                return new(inicio, inicio.AddMonths(3).AddDays(-1), $"{t + 1}.º trimestre de {d.Year}");
            case Anual:
                return new(new DateTime(d.Year, 1, 1), new DateTime(d.Year, 12, 31), $"año {d.Year}");
            default:
                var m = new DateTime(d.Year, d.Month, 1);
                return new(m, m.AddMonths(1).AddDays(-1), m.ToString("MMMM yyyy", Es));
        }
    }
}
