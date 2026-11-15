using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Factura emitida para el informe de la gestoría (facturacion/factura-venta): número con su serie, fecha y trimestre, cliente,
/// importes (base antes de retención, IVA, retención y total = base + IVA − retención) y el centro de coste de sus líneas: la
/// raíz (<paramref name="Centro"/>) y el centro (<paramref name="Departamento"/>) de la línea de mayor base.
/// <paramref name="Contabilizada"/>: la factura tiene asiento.
/// </summary>
public sealed record FacturaEmitidaListado(
    int IdFactura, string Numero, DateTime Fecha, int Trimestre, string? Nif, string Cliente, string? Descripcion,
    decimal Base, decimal Iva, decimal Retencion, decimal Total, string? Centro, string? Departamento, bool Contabilizada);

/// <summary>
/// Listado de facturas emitidas del ejercicio hasta una fecha (solo SELECT): las validadas, rectificadas y con rectificación
/// pendiente (estados 1, 2 y 4; ni borradores ni pendientes de validar), por fecha de factura.
/// </summary>
public sealed class ListadoEmitidas(IBaseDatos bd)
{
    public Task<IReadOnlyList<FacturaEmitidaListado>> LeerAsync(ContextoSociedadEjercicio ctx, DateTime hasta, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync<IReadOnlyList<FacturaEmitidaListado>>(async s =>
        {
            var facturas = await s.ConsultarAsync(
                "SELECT f.IdFactura, s.Prefijo, f.NumeroFactura, f.FechaFactura, f.CIF, f.Nombre, f.RazonSocial, f.Descripcion, f.BaseImponible, f.IVA, f.Retencion, " +
                "f.ImporteTotal, f.IdAsientoContable FROM Facturas AS f LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie " +
                "WHERE f.IdSociedadEjercicio = ? AND f.EstadoFactura IN (1, 2, 4) AND f.FechaFactura IS NOT NULL AND f.FechaFactura < ? ORDER BY f.FechaFactura, f.NumeroFactura",
                f => (Id: f.Entero("IdFactura"), Prefijo: f.Texto("Prefijo")?.Trim(), Numero: (f.Texto("NumeroFactura") ?? "").Trim(), Fecha: f.Fecha("FechaFactura").Date,
                    Nif: f.Texto("CIF")?.Trim(), Cliente: (f.Texto("RazonSocial") is { Length: > 0 } rs ? rs : f.Texto("Nombre") ?? "").Trim(), Descripcion: f.Texto("Descripcion")?.Trim(),
                    Base: f.DecimalNulo("BaseImponible") ?? 0m, Iva: f.DecimalNulo("IVA") ?? 0m, Retencion: f.DecimalNulo("Retencion") ?? 0m, Total: f.DecimalNulo("ImporteTotal") ?? 0m,
                    Asiento: f.EnteroNulo("IdAsientoContable")),
                ctx.IdSociedadEjercicio, hasta.Date.AddDays(1));
            // Centro de cada factura: el de la línea con más base (una factura de alquiler lleva un solo inmueble).
            var lineas = await s.ConsultarAsync(
                "SELECT l.IdFactura, l.IdCentroCoste, SUM(l.BaseImponible) AS B FROM LineasFactura AS l INNER JOIN Facturas AS f ON l.IdFactura = f.IdFactura " +
                "WHERE f.IdSociedadEjercicio = ? AND l.IdCentroCoste IS NOT NULL GROUP BY l.IdFactura, l.IdCentroCoste",
                f => (Factura: f.Entero("IdFactura"), Centro: f.Entero("IdCentroCoste"), Base: f.DecimalNulo("B") ?? 0m), ctx.IdSociedadEjercicio);
            var centroDe = lineas.GroupBy(l => l.Factura).ToDictionary(g => g.Key, g => g.OrderByDescending(l => Math.Abs(l.Base)).First().Centro);
            var centros = await s.ConsultarAsync("SELECT IdCentroCoste, IdCentroCosteRaiz, Nombre FROM CentrosCoste WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdCentroCoste"), Raiz: f.EnteroNulo("IdCentroCosteRaiz"), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedad);
            var porId = centros.ToDictionary(c => c.Id);
            return facturas.Select(f =>
            {
                string? raiz = null, nombre = null;
                if (centroDe.TryGetValue(f.Id, out var c) && porId.TryGetValue(c, out var centro))
                {
                    nombre = centro.Nombre;
                    raiz = centro.Raiz is int r && porId.TryGetValue(r, out var cr) ? cr.Nombre : centro.Nombre;
                }
                return new FacturaEmitidaListado(f.Id, (f.Prefijo ?? "") + f.Numero, f.Fecha, (f.Fecha.Month - 1) / 3 + 1, f.Nif, f.Cliente, f.Descripcion,
                    f.Base, f.Iva, f.Retencion, f.Total, raiz, nombre, f.Asiento is not null);
            }).ToList();
        }, cancelacion);
}
