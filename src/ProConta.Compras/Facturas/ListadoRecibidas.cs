using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Factura recibida contabilizada para el informe de la gestoría (compras/factura-compra): fecha contable de su asiento y
/// trimestre, asiento, proveedor, número, operación e inversión del sujeto pasivo (los de la factura), importes, centro de coste
/// (<paramref name="Centro"/>, y su raíz en <paramref name="Area"/>) y la ruta de su PDF, con <paramref name="PdfExiste"/> si el
/// fichero está en disco.
/// </summary>
public sealed record FacturaRecibidaListado(
    int IdRegistro, DateTime Fecha, int Trimestre, int? IdAsiento, string? Nif, string Proveedor, string Numero, OperacionCompra Operacion, bool Isp,
    decimal Base, decimal Iva, decimal Retencion, string? Centro, string? Area, string? RutaPdf, bool PdfExiste);

/// <summary>
/// Listado de facturas recibidas contabilizadas del ejercicio hasta una fecha (solo SELECT): las exportadas y no duplicadas, con
/// la fecha contable de su asiento (sin asiento enlazado, la de FECAPU). El PDF se localiza como en Compras › Facturas recibidas.
/// </summary>
public sealed class ListadoRecibidas(IBaseDatos bd)
{
    public Task<IReadOnlyList<FacturaRecibidaListado>> LeerAsync(ContextoSociedadEjercicio ctx, DateTime hasta, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync<IReadOnlyList<FacturaRecibidaListado>>(async s =>
        {
            var filas = await s.ConsultarAsync(
                "SELECT fc.IdRegistro, a.FechaContable, fc.FECAPU, fc.IdAsientoContable, fc.NIF, fc.NombreProveedorAeat, t.RazonSocial, t.Nombre, fc.Concepto, fc.NumeroFactura, " +
                "fc.TipoOperacion, fc.InversionSujetoPasivo, fc.ImporteBI, fc.ImporteIVA, fc.ImporteRetencion, fc.IdCentroCoste, fc.ArchivoOriginal, fc.Archivo " +
                "FROM ((FacturasCompra AS fc LEFT JOIN Asientos AS a ON fc.IdAsientoContable = a.IdAsiento) LEFT JOIN Proveedores AS p ON fc.IdProveedor = p.IdProveedor) " +
                "LEFT JOIN Terceros AS t ON p.IdTercero = t.IdTercero " +
                "WHERE (fc.IdSociedadEjercicio = ? OR (fc.IdSociedadEjercicio IS NULL AND fc.Sociedad = ? AND fc.Ejercicio = ?)) AND fc.Exportada = True AND fc.Duplicada = False",
                f => (Id: f.Entero("IdRegistro"), Fecha: f.FechaNula("FechaContable") ?? f.FechaNula("FECAPU"), Asiento: f.EnteroNulo("IdAsientoContable"), Nif: f.Texto("NIF")?.Trim(),
                    Proveedor: (f.Texto("NombreProveedorAeat") is { Length: > 0 } n ? n : f.Texto("RazonSocial") is { Length: > 0 } rs ? rs : f.Texto("Nombre") is { Length: > 0 } no ? no : f.Texto("Concepto") ?? "").Trim(),
                    Numero: (f.Texto("NumeroFactura") ?? "").Trim(), Operacion: f.EnteroNulo("TipoOperacion"), Isp: !f.EsNulo("InversionSujetoPasivo") && f.Logico("InversionSujetoPasivo"),
                    Base: f.DecimalNulo("ImporteBI") ?? 0m, Iva: f.DecimalNulo("ImporteIVA") ?? 0m, Retencion: f.DecimalNulo("ImporteRetencion") ?? 0m, Centro: f.EnteroNulo("IdCentroCoste"),
                    Rutas: new[] { f.Texto("ArchivoOriginal"), f.Texto("Archivo") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct().ToList()),
                ctx.IdSociedadEjercicio, ctx.IdSociedad, ctx.Ejercicio.ToString(CultureInfo.InvariantCulture));
            var centros = await s.ConsultarAsync("SELECT IdCentroCoste, IdCentroCosteRaiz, Nombre FROM CentrosCoste WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdCentroCoste"), Raiz: f.EnteroNulo("IdCentroCosteRaiz"), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedad);
            var porId = centros.ToDictionary(c => c.Id);
            var (_, rutaDocumentos) = await RepositorioFacturasCompra.EmpresaAsync(s, ctx.IdSociedad);
            var raiz = string.IsNullOrWhiteSpace(rutaDocumentos) ? null : ServicioFacturasCompra.RaizEmpresa(bd.Ruta, rutaDocumentos);
            var carpeta = ServicioFacturasCompra.CarpetaEjercicio(raiz, await RepositorioFacturasCompra.CarpetaComprasEjercicioAsync(s, ctx.IdSociedadEjercicio), ctx.Ejercicio);
            return filas.Where(f => f.Fecha is not null && f.Fecha.Value.Date <= hasta.Date)
                .OrderBy(f => f.Fecha).ThenBy(f => f.Asiento)
                .Select(f =>
                {
                    var fecha = f.Fecha!.Value.Date;
                    string? nombre = null, area = null;
                    if (f.Centro is int c && porId.TryGetValue(c, out var centro))
                    {
                        nombre = centro.Nombre;
                        area = centro.Raiz is int r && porId.TryGetValue(r, out var cr) ? cr.Nombre : centro.Nombre;
                    }
                    var ruta = f.Rutas.Count == 0 ? null : ServicioFacturasCompra.LocalizarPdf(f.Rutas, raiz, carpeta);
                    var operacion = f.Operacion is int op && Enum.IsDefined((OperacionCompra)op) ? (OperacionCompra)op : OperacionCompra.Nacional;
                    return new FacturaRecibidaListado(f.Id, fecha, fecha.Year == ctx.Ejercicio ? (fecha.Month - 1) / 3 + 1 : 0, f.Asiento, f.Nif, f.Proveedor, f.Numero,
                        operacion, f.Isp, f.Base, f.Iva, f.Retencion, nombre, area, ruta, ruta is not null && File.Exists(ruta));
                }).ToList();
        }, cancelacion);
}
