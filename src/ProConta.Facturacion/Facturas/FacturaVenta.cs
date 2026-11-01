using ProConta.Nucleo.Importes;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Estados de negocio de una factura (RF-FAC-06), con los códigos de Facturas.EstadoFactura. Un error técnico es de
/// un intento, no un estado.
/// </summary>
public enum EstadoFactura : byte
{
    PendienteValidar = 0,
    Validada = 1,
    Rectificada = 2,
    Borrador = 3,
    RectificacionPendiente = 4
}

public static class TextosEstado
{
    public static string Texto(EstadoFactura estado) => estado switch
    {
        EstadoFactura.Borrador => "Borrador",
        EstadoFactura.PendienteValidar => "Pendiente de validar",
        EstadoFactura.Validada => "Validada",
        EstadoFactura.RectificacionPendiente => "Rectificación pendiente",
        _ => "Rectificada"
    };
}

/// <summary>
/// Datos del cliente congelados al validar (RF-FAC-05): cambiar después el tercero no altera la factura emitida, su
/// PDF ni la rectificativa que se derive de ella.
/// </summary>
public sealed record InstantaneaCliente(
    string? Nif,
    string? Nombre,
    string? RazonSocial,
    string? Pais,
    byte? TipoIdentificacion,
    string? Direccion,
    string? CodigoPostal,
    string? Poblacion,
    string? Provincia,
    string? Email)
{
    public static readonly InstantaneaCliente Vacia = new(null, null, null, null, null, null, null, null, null, null);

    public bool EstaVacia => Nif is null && Nombre is null && RazonSocial is null;

    /// <summary>Nombre que se muestra: la razón social o, si no hay, el nombre.</summary>
    public string NombreVisible => (string.IsNullOrWhiteSpace(RazonSocial) ? Nombre : RazonSocial)?.Trim() ?? "";

    public string DomicilioEnUnaLinea =>
        string.Join(" · ", new[] { Direccion, string.Join(" ", new[] { CodigoPostal, Poblacion }.Where(x => !string.IsNullOrWhiteSpace(x))),
            string.IsNullOrWhiteSpace(Provincia) ? null : $"({Provincia})" }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>
/// Línea de una factura de venta. La base imponible es el dato maestro (RF-FAC-02): se calcula desde cantidad, precio y
/// descuentos (porcentaje y después importe fijo) y de ella salen las cuotas; o se fija desde el total con impuestos,
/// conservando exactamente ese total. Un suplido no lleva IVA ni retención (RF-FAC-03).
/// </summary>
public sealed class LineaFacturaVenta
{
    public int IdLineaFactura { get; internal set; }
    public int NumeroLinea { get; internal set; }
    public int? IdConcepto { get; set; }
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string? NotasInternas { get; set; }
    public DateTime? Fecha { get; set; }
    public decimal Cantidad { get; set; } = 1m;
    public decimal PrecioUnitario { get; set; }
    public decimal DescuentoPorcentaje { get; set; }
    public decimal DescuentoImporte { get; set; }
    public string Impuesto { get; set; } = "01";
    public string ClaveRegimen { get; set; } = "01";
    public string CalificacionOperacion { get; set; } = "S1";
    public string? CausaExencion { get; set; }
    public decimal TipoIva { get; set; } = 21m;
    public decimal TipoRecargo { get; set; }
    public bool EsSuplido { get; set; }
    public string? CuentaContable { get; set; }
    public int? IdDepartamento { get; set; }
    public int? IdCentroCoste { get; set; }

    public Importe BaseImponible { get; internal set; }
    public Importe CuotaIva { get; internal set; }
    public Importe CuotaRecargo { get; internal set; }
    public Importe ImporteTotal { get; internal set; }

    /// <summary>Recalcula base, cuotas y total desde cantidad, precio y descuentos.</summary>
    public void Recalcular()
    {
        var bruto = Cantidad * PrecioUnitario;
        var conPorcentaje = bruto - bruto * DescuentoPorcentaje / 100m;
        FijarBase(Importe.Redondear(conPorcentaje - DescuentoImporte));
    }

    /// <summary>
    /// Entrada inversa: el operador escribe el total con impuestos y se calcula la base y las cuotas conservando ese total
    /// exacto (la diferencia de redondeo va a la cuota). El precio pasa a ser base / cantidad y los descuentos, cero.
    /// </summary>
    public void FijarTotal(decimal total)
    {
        var importeTotal = Importe.Redondear(total);
        if (EsSuplido)
        {
            FijarBase(importeTotal);
        }
        else
        {
            var tipos = TipoIva + TipoRecargo;
            var @base = Importe.Redondear(importeTotal.Valor * 100m / (100m + tipos));
            BaseImponible = @base;
            CuotaRecargo = @base.Porcentaje(TipoRecargo);
            CuotaIva = importeTotal - @base - CuotaRecargo;
            ImporteTotal = importeTotal;
        }
        DescuentoPorcentaje = 0m;
        DescuentoImporte = 0m;
        if (Cantidad == 0m) Cantidad = 1m;
        PrecioUnitario = Math.Round(BaseImponible.Valor / Cantidad, 4, MidpointRounding.AwayFromZero);
    }

    private void FijarBase(Importe @base)
    {
        BaseImponible = @base;
        CuotaIva = EsSuplido ? Importe.Cero : @base.Porcentaje(TipoIva);
        CuotaRecargo = EsSuplido ? Importe.Cero : @base.Porcentaje(TipoRecargo);
        ImporteTotal = @base + CuotaIva + CuotaRecargo;
    }

    /// <summary>Carga los importes guardados sin recalcular (pueden venir de una entrada inversa).</summary>
    internal void CargarImportes(decimal @base, decimal cuotaIva, decimal cuotaRecargo, decimal total)
    {
        BaseImponible = Importe.Redondear(@base);
        CuotaIva = Importe.Redondear(cuotaIva);
        CuotaRecargo = Importe.Redondear(cuotaRecargo);
        ImporteTotal = Importe.Redondear(total);
    }

    /// <summary>Copia para duplicar o rectificar; con <paramref name="invertir"/>, cantidades e importes cambian de signo y precios y tipos se mantienen.</summary>
    public LineaFacturaVenta Copiar(bool invertir = false)
    {
        var copia = (LineaFacturaVenta)MemberwiseClone();
        copia.IdLineaFactura = 0;
        if (invertir)
        {
            copia.Cantidad = -Cantidad;
            copia.DescuentoImporte = -DescuentoImporte;
            copia.BaseImponible = -BaseImponible;
            copia.CuotaIva = -CuotaIva;
            copia.CuotaRecargo = -CuotaRecargo;
            copia.ImporteTotal = -ImporteTotal;
        }
        return copia;
    }
}

/// <summary>Totales de una factura (RF-FAC-03). No son editables: salen siempre de las líneas.</summary>
public sealed record TotalesFactura(Importe BaseImponible, Importe Iva, Importe Suplidos, Importe ImporteBruto, Importe Retencion, Importe ImporteTotal)
{
    public static readonly TotalesFactura Cero = new(Importe.Cero, Importe.Cero, Importe.Cero, Importe.Cero, Importe.Cero, Importe.Cero);
}

/// <summary>Base y cuotas de un tipo de IVA (líneas no suplidas).</summary>
public sealed record DesgloseIva(decimal TipoIva, decimal TipoRecargo, Importe BaseImponible, Importe CuotaIva, Importe CuotaRecargo);

public static class CalculoFactura
{
    /// <summary>
    /// Totales: base = bases no suplidas; IVA = cuotas de IVA y recargo; bruto = suma de los totales de línea (con
    /// suplidos); retención = porcentaje sobre la base no suplida; total = bruto menos retención. Primero se redondea cada
    /// línea y después se suma, para que pantalla, PDF, Access y VeriFactu den los mismos céntimos.
    /// </summary>
    public static TotalesFactura Totales(IEnumerable<LineaFacturaVenta> lineas, decimal tipoRetencion)
    {
        var lista = lineas.ToList();
        var @base = Importe.Sumar(lista.Where(l => !l.EsSuplido).Select(l => l.BaseImponible));
        var iva = Importe.Sumar(lista.Where(l => !l.EsSuplido).Select(l => l.CuotaIva + l.CuotaRecargo));
        var suplidos = Importe.Sumar(lista.Where(l => l.EsSuplido).Select(l => l.ImporteTotal));
        var bruto = Importe.Sumar(lista.Select(l => l.ImporteTotal));
        var retencion = @base.Porcentaje(tipoRetencion);
        return new TotalesFactura(@base, iva, suplidos, bruto, retencion, bruto - retencion);
    }

    /// <summary>Desglose por tipo de IVA y recargo de las líneas no suplidas, de mayor a menor tipo.</summary>
    public static IReadOnlyList<DesgloseIva> Desglose(IEnumerable<LineaFacturaVenta> lineas) =>
        lineas.Where(l => !l.EsSuplido)
            .GroupBy(l => (l.TipoIva, l.TipoRecargo))
            .Select(g => new DesgloseIva(g.Key.TipoIva, g.Key.TipoRecargo, Importe.Sumar(g.Select(l => l.BaseImponible)),
                Importe.Sumar(g.Select(l => l.CuotaIva)), Importe.Sumar(g.Select(l => l.CuotaRecargo))))
            .OrderByDescending(d => d.TipoIva).ThenByDescending(d => d.TipoRecargo)
            .ToList();
}

/// <summary>
/// Ente FacturaVenta (facturacion/factura-venta): cabecera, líneas y totales, guardados juntos en una transacción.
/// Nace como borrador sin número; al validarla recibe número, fecha y la instantánea del cliente y deja de poder
/// cambiarse salvo sus notas internas (RF-FAC-07).
/// </summary>
public sealed class FacturaVenta
{
    public int IdFactura { get; internal set; }
    public int IdSociedadEjercicio { get; internal set; }
    public int IdSerie { get; set; }
    public string? NumeroFactura { get; internal set; }
    public int? IdCliente { get; set; }
    public int? IdContrato { get; internal set; }
    public InstantaneaCliente Cliente { get; internal set; } = InstantaneaCliente.Vacia;
    public string? NifEmisor { get; internal set; }
    public string? NombreEmisor { get; internal set; }
    public DateTime? FechaFactura { get; set; }
    public DateTime? FechaOperacion { get; set; }
    public DateTime? FechaInicioValidez { get; set; }
    public DateTime? FechaFinValidez { get; set; }
    public DateTime FechaCreacion { get; internal set; }
    public DateTime? FechaHoraEmision { get; internal set; }
    public byte? FormaDePago { get; set; }
    public byte? DiaDePago { get; set; }
    public string Descripcion { get; set; } = "";
    public string? NotasInternas { get; set; }
    public decimal TipoRetencion { get; set; }
    public string TipoFactura { get; set; } = "F1";
    public string? TipoRectificativa { get; internal set; }
    public EstadoFactura Estado { get; internal set; } = EstadoFactura.Borrador;
    public int? IdFacturaRectificada { get; internal set; }
    public string? NumeroFacturaRectificada { get; internal set; }
    public int? IdAsientoContable { get; internal set; }
    public DateTime? FechaExportacionContable { get; internal set; }
    public string? Historial { get; internal set; }
    public bool SinVeriFactu { get; internal set; }
    public int? IdFacturaProgramada { get; internal set; }
    public int Version { get; internal set; }

    public List<LineaFacturaVenta> Lineas { get; private set; } = [];

    public bool EsNueva => IdFactura == 0;
    public bool EsBorrador => Estado == EstadoFactura.Borrador;
    public bool EsRectificativa => TipoFactura.StartsWith('R');

    public TotalesFactura Totales => CalculoFactura.Totales(Lineas, TipoRetencion);

    /// <summary>Número visible: el fiscal o, en un borrador, «Borrador B-0007» con el IdFactura.</summary>
    public string Rotulo => NumeroFactura ?? RotuloBorrador(IdFactura);

    public static string RotuloBorrador(int idFactura) => idFactura > 0 ? $"Borrador B-{idFactura:0000}" : "Borrador nuevo";

    /// <summary>Añade una línea al final con su número de orden y la recalcula.</summary>
    public LineaFacturaVenta AnadirLinea(LineaFacturaVenta linea)
    {
        Lineas.Add(linea);
        Renumerar();
        return linea;
    }

    public void QuitarLinea(LineaFacturaVenta linea)
    {
        Lineas.Remove(linea);
        Renumerar();
    }

    internal void Renumerar()
    {
        for (var i = 0; i < Lineas.Count; i++) Lineas[i].NumeroLinea = i + 1;
    }

    /// <summary>Añade una línea al historial interno (fecha, hora, operador y acción).</summary>
    internal void Anotar(DateTime ahora, string operador, string accion)
    {
        var linea = $"{ahora:dd/MM/yyyy HH:mm} · {operador} · {accion}";
        Historial = string.IsNullOrEmpty(Historial) ? linea : Historial + Environment.NewLine + linea;
    }

    public IReadOnlyList<string> LineasHistorial =>
        (Historial ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public FacturaVenta Clonar()
    {
        var copia = (FacturaVenta)MemberwiseClone();
        copia.Lineas = Lineas.Select(l => { var c = l.Copiar(); c.IdLineaFactura = l.IdLineaFactura; return c; }).ToList();
        return copia;
    }
}
