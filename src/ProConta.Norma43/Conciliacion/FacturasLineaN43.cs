namespace ProConta.Norma43.Conciliacion;

/// <summary>Factura (de venta o de compra) enlazada a una línea N43: su número y su descripción abreviada, si la tiene.</summary>
public sealed record FacturaEnlazadaN43(string Numero, string? DescripcionAbreviada);

/// <summary>
/// Facturas enlazadas a cada línea N43, para el concepto y el documento de los apuntes de su asiento (norma43/conciliacion 2026-11-13
/// 10:50). El enlace lo creará norma43/busqueda-por-importe y la descripción abreviada, facturacion (borradores); hasta entonces no hay
/// ninguna (<see cref="SinFacturasEnlazadas"/>).
/// </summary>
public interface IFacturasLineaN43
{
    Task<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>> FacturasAsync(IReadOnlyCollection<int> idsLineas);
}

/// <summary>Sin enlace línea ↔ factura todavía: ninguna línea tiene facturas.</summary>
public sealed class SinFacturasEnlazadas : IFacturasLineaN43
{
    public static SinFacturasEnlazadas Instancia { get; } = new();

    public Task<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>> FacturasAsync(IReadOnlyCollection<int> idsLineas) =>
        Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>>(new Dictionary<int, IReadOnlyList<FacturaEnlazadaN43>>());
}
