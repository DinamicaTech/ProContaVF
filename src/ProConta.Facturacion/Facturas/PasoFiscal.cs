using ProConta.Datos.Persistencia;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Paso fiscal de la emisión (facturacion/factura-venta, 2026-11-01 16:18): recibe la factura ya numerada, dentro de la
/// misma transacción, y decide su estado. Lo sustituirá el de facturacion/verifactu sin tocar el resto de la validación.
/// </summary>
public interface IPasoFiscal
{
    /// <summary>Texto para el historial y las pantallas («sin VeriFactu», «VeriFactu»…).</summary>
    string Nombre { get; }

    /// <summary>Estado en que queda la factura recién numerada; puede marcarla (por ejemplo <see cref="FacturaVenta.SinVeriFactu"/>).</summary>
    Task<EstadoFactura> AlEmitirAsync(ISesionDatos sesion, FacturaVenta factura);
}

/// <summary>
/// Paso «sin VeriFactu», el de producción mientras el parámetro general VeriFactu está desactivado
/// (facturacion/verifactu 2026-11-05): deja la factura Validada y marcada como no comunicada (Facturas.SinVeriFactu).
/// Las facturas así marcadas no se envían después aunque se active VeriFactu.
/// </summary>
public sealed class PasoFiscalSinVeriFactu : IPasoFiscal
{
    public static readonly PasoFiscalSinVeriFactu Instancia = new();

    public string Nombre => "sin VeriFactu";

    public Task<EstadoFactura> AlEmitirAsync(ISesionDatos sesion, FacturaVenta factura)
    {
        factura.SinVeriFactu = true;
        return Task.FromResult(EstadoFactura.Validada);
    }
}
