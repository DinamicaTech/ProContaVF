using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Lineas;

namespace ProConta.Norma43.Conciliacion;

/// <summary>Factura (de venta o de compra) enlazada a una línea N43: su número y su descripción abreviada, si la tiene.</summary>
public sealed record FacturaEnlazadaN43(string Numero, string? DescripcionAbreviada);

/// <summary>
/// Facturas enlazadas a cada línea N43, para el concepto y el documento de los apuntes de su asiento (norma43/conciliacion 2026-11-13
/// 10:50). El enlace lo guarda la factura (norma43/busqueda-por-importe/enlace-facturas): <see cref="FacturasEnlazadasN43"/> lo lee.
/// </summary>
public interface IFacturasLineaN43
{
    Task<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>> FacturasAsync(IReadOnlyCollection<int> idsLineas);
}

/// <summary>Ninguna línea tiene facturas (pruebas del motor sin BD).</summary>
public sealed class SinFacturasEnlazadas : IFacturasLineaN43
{
    public static SinFacturasEnlazadas Instancia { get; } = new();

    public Task<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>> FacturasAsync(IReadOnlyCollection<int> idsLineas) =>
        Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>>(new Dictionary<int, IReadOnlyList<FacturaEnlazadaN43>>());
}

/// <summary>
/// Facturas enlazadas a cada línea según la BD (norma43/busqueda-por-importe/enlace-facturas 2026-11-16): las de compra que paga
/// (FacturasCompra.LineaN43, con su ConceptoAbreviado) y las de venta que cobra o devuelve (Facturas.LineaN43 y LineaN43Devolucion, y
/// los reintentos de ReintentosSEPA), con su número de serie y su DescripcionAbreviada (facturacion/descripcion-abreviada). Los
/// borradores no tienen número y no cuentan.
/// </summary>
public sealed class FacturasEnlazadasN43(IBaseDatos bd) : IFacturasLineaN43
{
    private const string DesdeVenta = "Facturas AS f LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie";
    private const string Venta = "s.Prefijo, f.NumeroFactura, f.DescripcionAbreviada";

    public Task<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>> FacturasAsync(IReadOnlyCollection<int> idsLineas)
    {
        ArgumentNullException.ThrowIfNull(idsLineas);
        return bd.EjecutarAsync<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>>(async s =>
        {
            var filas = new List<(int Linea, FacturaEnlazadaN43 Factura)>();
            foreach (var bloque in idsLineas.Distinct().Chunk(200))
            {
                var lista = string.Join(",", bloque.Select(i => i.ToString(CultureInfo.InvariantCulture)));
                filas.AddRange(await s.ConsultarAsync(
                    $"SELECT fc.LineaN43 AS Linea, fc.NumeroFactura, fc.ConceptoAbreviado FROM FacturasCompra AS fc WHERE fc.LineaN43 IN ({lista})",
                    f => (f.Entero("Linea"), new FacturaEnlazadaN43(ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? "(sin número)",
                        ConsultaLineasN43.Limpio(f.Texto("ConceptoAbreviado"))))));
                foreach (var columna in new[] { "f.LineaN43", "f.LineaN43Devolucion" })
                    filas.AddRange(await s.ConsultarAsync(
                        $"SELECT {columna} AS Linea, {Venta} FROM {DesdeVenta} WHERE f.NumeroFactura IS NOT NULL AND {columna} IN ({lista})", LeerVenta));
                foreach (var columna in new[] { "r.LineaN43", "r.LineaN43Devolucion" })
                    filas.AddRange(await s.ConsultarAsync(
                        $"SELECT {columna} AS Linea, {Venta} FROM ({DesdeVenta}) INNER JOIN ReintentosSEPA AS r ON f.IdFactura = r.IdFactura " +
                        $"WHERE f.NumeroFactura IS NOT NULL AND {columna} IN ({lista})", LeerVenta));
            }
            return filas.GroupBy(x => x.Linea).ToDictionary(g => g.Key,
                g => (IReadOnlyList<FacturaEnlazadaN43>)g.Select(x => x.Factura).Distinct().OrderBy(x => x.Numero, StringComparer.Ordinal).ToList());
        });
    }

    private static (int, FacturaEnlazadaN43) LeerVenta(IFilaDatos f) =>
        (f.Entero("Linea"), new FacturaEnlazadaN43((ConsultaLineasN43.Limpio(f.Texto("Prefijo")) ?? "") + (ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? ""),
            ConsultaLineasN43.Limpio(f.Texto("DescripcionAbreviada"))));
}
