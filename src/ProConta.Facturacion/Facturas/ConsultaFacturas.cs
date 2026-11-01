using ProConta.Datos.Persistencia;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Fila ligera del listado de Facturas emitidas (facturacion/pantallas): lo que se ve en la tabla y en los filtros, sin
/// líneas. El cliente es el de la instantánea en las numeradas y el del tercero actual en los borradores.
/// </summary>
public sealed record FilaFactura(
    int IdFactura,
    string? NumeroFactura,
    DateTime? FechaFactura,
    int? IdCliente,
    string Cliente,
    string? Nif,
    string Descripcion,
    string? NotasInternas,
    decimal BaseImponible,
    decimal TipoRetencion,
    decimal ImporteTotal,
    EstadoFactura Estado,
    string TipoFactura,
    int IdSerie,
    string? PrefijoSerie,
    bool SerieRectificativa,
    bool SinVeriFactu,
    int? IdFacturaProgramada,
    int? IdFacturaRectificada,
    string? NumeroRectificada,
    string? NumeroRectificativa,
    int? IdAsientoContable,
    int Version)
{
    public string Rotulo => NumeroFactura ?? FacturaVenta.RotuloBorrador(IdFactura);
    public bool EsBorrador => Estado == EstadoFactura.Borrador;
    public bool EsRectificativa => TipoFactura.StartsWith('R');
    public bool EsProgramada => IdFacturaProgramada is not null;
    public string TextoEstado => TextosEstado.Texto(Estado);

    /// <summary>Situación VeriFactu mientras no hay VeriFactu: no aplica en borradores; «Sin VeriFactu» en las validadas con el paso provisional.</summary>
    public string TextoVeriFactu => EsBorrador ? "No aplica" : SinVeriFactu ? "Sin VeriFactu" : "Pendiente";

    /// <summary>Texto de búsqueda: número, cliente, NIF, descripción y notas.</summary>
    public bool Contiene(string texto) =>
        string.IsNullOrWhiteSpace(texto) || new[] { Rotulo, Cliente, Nif, Descripcion, NotasInternas, NumeroRectificada, NumeroRectificativa }
            .Any(c => c?.Contains(texto.Trim(), StringComparison.CurrentCultureIgnoreCase) == true);
}

/// <summary>Consultas ligeras del listado de facturas de un ejercicio.</summary>
public sealed class ConsultaFacturas(IBaseDatos bd)
{
    private const string Sql =
        "SELECT f.IdFactura, f.NumeroFactura, f.FechaFactura, f.IdCliente, f.Nombre, f.RazonSocial, f.CIF, t.Nombre AS NombreTercero, t.RazonSocial AS RazonTercero, t.NIF AS NifTercero, " +
        "f.Descripcion, f.NotasInternas, f.BaseImponible, f.TipoRetencion, f.ImporteTotal, f.EstadoFactura, f.TipoFactura, f.IdSerie, s.Prefijo, s.EsRectificativa, f.SinVeriFactu, " +
        "f.IdFacturaProgramada, f.IdFacturaRectificada, o.NumeroFactura AS NumeroOriginal, r.NumeroFactura AS NumeroRectificativa, r.IdFactura AS IdRectificativa, f.IdAsientoContable, f.Version " +
        "FROM ((((Facturas AS f LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie) " +
        "LEFT JOIN Clientes AS c ON f.IdCliente = c.IdCliente) LEFT JOIN Terceros AS t ON c.IdTercero = t.IdTercero) " +
        "LEFT JOIN Facturas AS o ON f.IdFacturaRectificada = o.IdFactura) " +
        "LEFT JOIN Facturas AS r ON r.IdFacturaRectificada = f.IdFactura ";

    /// <summary>Facturas y borradores de un ejercicio, de la más reciente a la más antigua (los borradores primero).</summary>
    public async Task<IReadOnlyList<FilaFactura>> ListarAsync(int idSociedadEjercicio, int? idCliente = null)
    {
        var filas = await bd.EjecutarAsync(s => idCliente is int c
            ? s.ConsultarAsync(Sql + "WHERE f.IdSociedadEjercicio = ? AND f.IdCliente = ?", Mapear, idSociedadEjercicio, c)
            : s.ConsultarAsync(Sql + "WHERE f.IdSociedadEjercicio = ?", Mapear, idSociedadEjercicio));
        return Ordenar(filas);
    }

    public Task<FilaFactura?> FilaAsync(int idFactura) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(Sql + "WHERE f.IdFactura = ?", Mapear, idFactura));

    /// <summary>Orden por defecto: borradores (los más nuevos arriba) y después por fecha y número descendentes.</summary>
    public static IReadOnlyList<FilaFactura> Ordenar(IEnumerable<FilaFactura> filas) =>
        filas.OrderByDescending(f => f.EsBorrador).ThenByDescending(f => f.FechaFactura ?? DateTime.MaxValue)
            .ThenByDescending(f => f.NumeroFactura, StringComparer.Ordinal).ThenByDescending(f => f.IdFactura).ToList();

    private static FilaFactura Mapear(IFilaDatos r)
    {
        var numerada = !r.EsNulo("NumeroFactura");
        static string Visible(string? razon, string? nombre) => (string.IsNullOrWhiteSpace(razon) ? nombre : razon)?.Trim() ?? "";
        var cliente = numerada ? Visible(r.Texto("RazonSocial"), r.Texto("Nombre")) : "";
        if (cliente.Length == 0) cliente = Visible(r.Texto("RazonTercero"), r.Texto("NombreTercero"));
        return new FilaFactura(
            r.Entero("IdFactura"),
            r.Texto("NumeroFactura"),
            r.FechaNula("FechaFactura"),
            r.EnteroNulo("IdCliente"),
            cliente,
            (numerada ? r.Texto("CIF") : null) ?? r.Texto("NifTercero"),
            r.Texto("Descripcion") ?? "",
            r.Texto("NotasInternas"),
            r.Decimal("BaseImponible"),
            r.EsNulo("TipoRetencion") ? 0m : Math.Round((decimal)r.Doble("TipoRetencion"), 2),
            r.Decimal("ImporteTotal"),
            (EstadoFactura)r.Entero("EstadoFactura"),
            r.Texto("TipoFactura")?.Trim() ?? "F1",
            r.Entero("IdSerie"),
            r.Texto("Prefijo")?.Trim(),
            !r.EsNulo("EsRectificativa") && r.Logico("EsRectificativa"),
            r.Logico("SinVeriFactu"),
            r.EnteroNulo("IdFacturaProgramada"),
            r.EnteroNulo("IdFacturaRectificada"),
            r.Texto("NumeroOriginal"),
            r.Texto("NumeroRectificativa") ?? (r.EsNulo("IdRectificativa") ? null : FacturaVenta.RotuloBorrador(r.Entero("IdRectificativa"))),
            r.EnteroNulo("IdAsientoContable"),
            r.EnteroNulo("Version") ?? 0);
    }
}
