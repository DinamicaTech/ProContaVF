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
/// Paso provisional «sin VeriFactu», solo para la BD de pruebas: deja la factura Validada y marcada como no comunicada
/// (Facturas.SinVeriFactu), visible en la factura y en el listado. En producción se sigue facturando con el ERP.
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
