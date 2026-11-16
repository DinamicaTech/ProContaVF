using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Busqueda;

/// <summary>Clase de documento que se enlaza con una línea N43: factura de compra (un cargo la paga) o de venta (un abono la cobra).</summary>
public enum TipoDocumentoN43
{
    Compra,
    Venta,
}

/// <summary>Factura que puede corresponder a un movimiento, tal como la leen las búsquedas.</summary>
/// <param name="LineaN43">Línea N43 con la que ya está enlazada (null si con ninguna).</param>
public sealed record DocumentoN43(TipoDocumentoN43 Tipo, int Id, string Numero, DateTime Fecha, decimal Importe, string? Nif, string? Tercero, string? Descripcion,
    int? LineaN43);

/// <summary>Factura candidata para una línea: el documento, la distancia en días y en importe, y si ya está enlazada con esta línea.</summary>
/// <param name="Dias">Fecha del banco menos fecha de la factura: positivo si la factura es anterior al movimiento.</param>
/// <param name="Diferencia">Importe de la factura menos importe del movimiento.</param>
public sealed record CandidatoN43(DocumentoN43 Documento, int Dias, decimal Diferencia, bool Enlazada)
{
    public int DiasAbsolutos => Math.Abs(Dias);
}

/// <summary>Una línea buscada y sus candidatas, de la más cercana en fecha a la más lejana.</summary>
public sealed record BusquedaLineaN43(LineaN43 Linea, IReadOnlyList<CandidatoN43> Candidatos);

/// <summary>Lo que el operador deja marcado o desmarcado en el diálogo para una factura de una línea.</summary>
public sealed record CambioEnlaceN43(int IdLinea, TipoDocumentoN43 Tipo, int IdDocumento, bool Enlazar);

/// <summary>Resultado de enlazar: cuántas facturas se enlazan y desenlazan, qué líneas cambian, cuáles toman la cuenta del tercero y los avisos.</summary>
public sealed record ResultadoEnlaceN43(int Enlazadas, int Desenlazadas, IReadOnlyList<int> Lineas, IReadOnlyList<int> ConCuenta,
    IReadOnlyDictionary<int, Error> Rechazadas, IReadOnlyList<string> Avisos);

/// <summary>
/// Criterios de la búsqueda por importe (norma43/busqueda-por-importe 2026-10-29 17:18, ajustados el 2026-11-16): sin BD, para que las
/// pruebas los fijen.
/// </summary>
public static class ReglasBusquedaImporteN43
{
    /// <summary>Diferencia máxima de importe, en euros, en los dos sentidos.</summary>
    public const decimal Tolerancia = 0.02m;
    /// <summary>Cargos: días como mucho entre la factura de compra y el movimiento, antes o después.</summary>
    public const int DiasCompra = 180;
    /// <summary>Abonos: días como mucho que la factura de venta es anterior al movimiento.</summary>
    public const int DiasVentaAntes = 270;
    /// <summary>Abonos: días como mucho que la factura de venta es posterior al movimiento.</summary>
    public const int DiasVentaDespues = 5;
    /// <summary>Candidatas que se enseñan por línea.</summary>
    public const int Maximo = 10;
    /// <summary>Las candidatas a este número de días o menos vienen marcadas (2026-11-16 10:50).</summary>
    public const int DiasMarcado = 1;

    /// <summary>Qué facturas busca el movimiento: los cargos pagan compras y los abonos cobran ventas.</summary>
    public static TipoDocumentoN43 TipoPara(LineaN43 linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        return linea.EsCargo ? TipoDocumentoN43.Compra : TipoDocumentoN43.Venta;
    }

    /// <summary>Intervalo de fechas de factura que admite el movimiento.</summary>
    public static (DateTime Desde, DateTime Hasta) Fechas(LineaN43 linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        var f = linea.FechaOperacion.Date;
        return TipoPara(linea) == TipoDocumentoN43.Compra
            ? (f.AddDays(-DiasCompra), f.AddDays(DiasCompra))
            : (f.AddDays(-DiasVentaAntes), f.AddDays(DiasVentaDespues));
    }

    /// <summary>
    /// Candidatas de la línea entre los documentos leídos: del tipo que le toca, con el importe dentro de la tolerancia, la fecha en el
    /// intervalo y sin enlazar con otra línea; las más cercanas en fecha (y, a igual fecha, en importe), como mucho <see cref="Maximo"/>.
    /// Las ya enlazadas con esta línea salen siempre, aunque no cumplan el resto, para poder desenlazarlas.
    /// </summary>
    public static IReadOnlyList<CandidatoN43> Elegir(LineaN43 linea, IEnumerable<DocumentoN43> documentos)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ArgumentNullException.ThrowIfNull(documentos);
        var tipo = TipoPara(linea);
        var (desde, hasta) = Fechas(linea);
        var banco = linea.FechaOperacion.Date;
        var todas = documentos.Where(d => d.Tipo == tipo)
            .Select(d => new CandidatoN43(d, (banco - d.Fecha.Date).Days, d.Importe - linea.Importe, d.LineaN43 == linea.Id))
            .ToList();
        var enlazadas = todas.Where(c => c.Enlazada).ToList();
        var libres = todas
            .Where(c => !c.Enlazada && c.Documento.LineaN43 is null or 0 && Math.Abs(c.Diferencia) <= Tolerancia
                && c.Documento.Fecha.Date >= desde && c.Documento.Fecha.Date <= hasta)
            .OrderBy(c => c.DiasAbsolutos).ThenBy(c => Math.Abs(c.Diferencia)).ThenBy(c => c.Documento.Id)
            .Take(Maximo);
        return enlazadas.Concat(libres).GroupBy(c => c.Documento.Id).Select(g => g.First())
            .OrderBy(c => c.DiasAbsolutos).ThenBy(c => Math.Abs(c.Diferencia)).ThenBy(c => c.Documento.Id).ToList();
    }

    /// <summary>
    /// Qué candidatas vienen marcadas al abrir el diálogo: las ya enlazadas con su línea y, entre las libres, las que están a
    /// <see cref="DiasMarcado"/> días o menos. Una factura que sale para varias líneas solo se marca en la más cercana (y, a igualdad, en la
    /// primera), porque solo puede enlazarse con una.
    /// </summary>
    public static IReadOnlySet<(int IdLinea, TipoDocumentoN43 Tipo, int IdDocumento)> Premarcadas(IReadOnlyList<BusquedaLineaN43> busquedas)
    {
        ArgumentNullException.ThrowIfNull(busquedas);
        var r = new HashSet<(int, TipoDocumentoN43, int)>();
        foreach (var b in busquedas)
            foreach (var c in b.Candidatos.Where(c => c.Enlazada))
                r.Add((b.Linea.Id, c.Documento.Tipo, c.Documento.Id));
        var ocupadas = r.Select(x => (x.Item2, x.Item3)).ToHashSet();
        var libres = busquedas
            .SelectMany((b, orden) => b.Candidatos.Where(c => !c.Enlazada && c.DiasAbsolutos <= DiasMarcado).Select(c => (b.Linea.Id, Orden: orden, c)))
            .GroupBy(x => (x.c.Documento.Tipo, x.c.Documento.Id));
        foreach (var g in libres)
        {
            if (ocupadas.Contains(g.Key)) continue;
            var mejor = g.OrderBy(x => x.c.DiasAbsolutos).ThenBy(x => Math.Abs(x.c.Diferencia)).ThenBy(x => x.Orden).First();
            r.Add((mejor.Id, g.Key.Tipo, g.Key.Id));
        }
        return r;
    }
}

/// <summary>
/// Búsqueda por importe (norma43/busqueda-por-importe): para cada línea N43, las facturas de la sociedad cuyo importe coincide (compras
/// para los cargos, ventas para los abonos) y el enlace de las que elige el operador. Enlazar guarda la línea en la factura
/// (FacturasCompra.LineaN43 o Facturas.LineaN43: la factura queda conciliada) y, si la línea no está contabilizada, le pone la cuenta del
/// proveedor o del cliente. No toca el asiento de la factura ni el del movimiento.
/// </summary>
public sealed class BusquedaPorImporteN43(IBaseDatos bd)
{
    /// <summary>Origen de las imputaciones puestas al enlazar una factura.</summary>
    public const string OrigenFactura = "FACTURA";

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    // ---------- búsqueda ----------

    /// <summary>Las candidatas de cada línea, en el orden en que llegan las líneas.</summary>
    public Task<IReadOnlyList<BusquedaLineaN43>> BuscarAsync(string idSociedad, IReadOnlyList<LineaN43> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        return bd.EjecutarAsync<IReadOnlyList<BusquedaLineaN43>>(async s =>
        {
            var r = new List<BusquedaLineaN43>(lineas.Count);
            foreach (var l in lineas)
            {
                var (desde, hasta) = ReglasBusquedaImporteN43.Fechas(l);
                var docs = ReglasBusquedaImporteN43.TipoPara(l) == TipoDocumentoN43.Compra
                    ? await ComprasAsync(s, idSociedad, l, desde, hasta)
                    : await VentasAsync(s, idSociedad, l, desde, hasta);
                r.Add(new BusquedaLineaN43(l, ReglasBusquedaImporteN43.Elegir(l, docs)));
            }
            return r;
        });
    }

    private const string ColumnasCompra =
        "fc.IdRegistro, fc.NumeroFactura, fc.Fecha, fc.ImporteTotal, fc.LineaN43, fc.NIF, fc.NombreProveedorAeat, fc.Concepto, fc.ConceptoAbreviado, t.Nombre AS NombreTercero, t.NIF AS NifTercero";

    private const string DesdeCompra =
        "((FacturasCompra AS fc INNER JOIN SociedadesEjercicios AS se ON fc.IdSociedadEjercicio = se.IdSociedadEjercicio) " +
        "LEFT JOIN Proveedores AS p ON fc.IdProveedor = p.IdProveedor) LEFT JOIN Terceros AS t ON p.IdTercero = t.IdTercero";

    private static DocumentoN43 Compra(IFilaDatos f) => new(TipoDocumentoN43.Compra, f.Entero("IdRegistro"), ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? "(sin número)",
        f.Fecha("Fecha").Date, f.Decimal("ImporteTotal"), ConsultaLineasN43.Limpio(f.Texto("NIF")) ?? ConsultaLineasN43.Limpio(f.Texto("NifTercero")),
        ConsultaLineasN43.Limpio(f.Texto("NombreTercero")) ?? ConsultaLineasN43.Limpio(f.Texto("NombreProveedorAeat")),
        ConsultaLineasN43.Limpio(f.Texto("ConceptoAbreviado")) ?? ConsultaLineasN43.Limpio(f.Texto("Concepto")), Linea(f));

    private static async Task<IReadOnlyList<DocumentoN43>> ComprasAsync(ISesionDatos s, string idSociedad, LineaN43 l, DateTime desde, DateTime hasta)
    {
        var docs = (await s.ConsultarAsync(
            $"SELECT {ColumnasCompra} FROM {DesdeCompra} WHERE se.IdSociedad = ? AND fc.ImporteTotal >= ? AND fc.ImporteTotal <= ? AND fc.Fecha >= ? AND fc.Fecha < ? " +
            "AND (fc.Duplicada IS NULL OR fc.Duplicada = False)",
            Compra, idSociedad, l.Importe - ReglasBusquedaImporteN43.Tolerancia, l.Importe + ReglasBusquedaImporteN43.Tolerancia, desde, hasta.AddDays(1))).ToList();
        docs.AddRange(await s.ConsultarAsync($"SELECT {ColumnasCompra} FROM {DesdeCompra} WHERE se.IdSociedad = ? AND fc.LineaN43 = ?", Compra, idSociedad, l.Id));
        return docs;
    }

    private const string ColumnasVenta =
        "f.IdFactura, s.Prefijo, f.NumeroFactura, f.FechaFactura, f.ImporteTotal, f.LineaN43, f.CIF, f.Nombre, f.RazonSocial, f.Descripcion";

    private const string DesdeVenta =
        "(Facturas AS f INNER JOIN SociedadesEjercicios AS se ON f.IdSociedadEjercicio = se.IdSociedadEjercicio) LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie";

    /// <summary>Facturas de venta con número: validadas, rectificadas o pendientes de rectificar (los borradores no cuentan).</summary>
    private const string VentasConNumero = "f.NumeroFactura IS NOT NULL AND f.EstadoFactura IN (1, 2, 4)";

    private static DocumentoN43 Venta(IFilaDatos f) => new(TipoDocumentoN43.Venta, f.Entero("IdFactura"),
        (ConsultaLineasN43.Limpio(f.Texto("Prefijo")) ?? "") + (ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? ""),
        f.Fecha("FechaFactura").Date, f.Decimal("ImporteTotal"), ConsultaLineasN43.Limpio(f.Texto("CIF")),
        ConsultaLineasN43.Limpio(f.Texto("RazonSocial")) ?? ConsultaLineasN43.Limpio(f.Texto("Nombre")), ConsultaLineasN43.Limpio(f.Texto("Descripcion")), Linea(f));

    private static async Task<IReadOnlyList<DocumentoN43>> VentasAsync(ISesionDatos s, string idSociedad, LineaN43 l, DateTime desde, DateTime hasta)
    {
        var docs = (await s.ConsultarAsync(
            $"SELECT {ColumnasVenta} FROM {DesdeVenta} WHERE se.IdSociedad = ? AND {VentasConNumero} AND f.ImporteTotal >= ? AND f.ImporteTotal <= ? " +
            "AND f.FechaFactura >= ? AND f.FechaFactura < ?",
            Venta, idSociedad, l.Importe - ReglasBusquedaImporteN43.Tolerancia, l.Importe + ReglasBusquedaImporteN43.Tolerancia, desde, hasta.AddDays(1))).ToList();
        docs.AddRange(await s.ConsultarAsync($"SELECT {ColumnasVenta} FROM {DesdeVenta} WHERE se.IdSociedad = ? AND f.LineaN43 = ?", Venta, idSociedad, l.Id));
        return docs;
    }

    private static int? Linea(IFilaDatos f) => f.EnteroNulo("LineaN43") is int n and > 0 ? n : null;

    // ---------- enlace ----------

    /// <summary>
    /// Aplica lo marcado en el diálogo, todo en una transacción: enlaza las facturas marcadas que no lo estaban y desenlaza las desmarcadas
    /// que sí. Después, a cada línea sin contabilizar con facturas enlazadas le pone la cuenta de su tercero (la del proveedor de la factura de
    /// compra o la de cliente en la sociedad), si todas son del mismo; si no, la deja como está y lo avisa. Una línea de un lote revertido,
    /// sin ejercicio o de un ejercicio cerrado se rechaza entera; una factura que otra línea enlazó mientras tanto, también.
    /// </summary>
    public Task<ResultadoEnlaceN43> EnlazarAsync(string idSociedad, IReadOnlyList<CambioEnlaceN43> cambios, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(cambios);
        ArgumentNullException.ThrowIfNull(actor);
        return bd.EnTransaccionAsync(async s =>
        {
            var lineas = await ServicioLineasN43.LeerAsync(s, idSociedad, cambios.Select(c => c.IdLinea).Distinct().ToList());
            var rechazadas = new Dictionary<int, Error>();
            var avisos = new List<string>();
            var cambiadas = new List<int>();
            var conCuenta = new List<int>();
            int enlazadas = 0, desenlazadas = 0;

            var repetidas = cambios.Where(c => c.Enlazar).GroupBy(c => (c.Tipo, c.IdDocumento)).Where(g => g.Select(c => c.IdLinea).Distinct().Count() > 1).ToList();
            foreach (var g in repetidas)
                foreach (var id in g.Select(c => c.IdLinea).Distinct())
                    rechazadas[id] = new Error("BusquedaImporte.FacturaRepetida", "La misma factura está marcada en dos movimientos.", Rotulo(lineas, id),
                        "Una factura solo se puede enlazar con un movimiento.", "Desmárcala en uno de los dos y vuelve a pulsar «Enlazar».");

            foreach (var porLinea in cambios.GroupBy(c => c.IdLinea))
            {
                var id = porLinea.Key;
                if (rechazadas.ContainsKey(id)) continue;
                if (!lineas.TryGetValue(id, out var l))
                {
                    rechazadas[id] = new Error("Lineas.NoExiste", $"No encuentro el movimiento {id}.", $"Movimiento {id}", "Se ha revertido su extracto o es de otra sociedad.", "Actualiza la lista (F5).");
                    continue;
                }
                if (BloqueoEnlace(l) is { } bloqueo) { rechazadas[id] = bloqueo; continue; }

                // Primero se comprueba todo lo que se enlaza: si una factura falla, la línea queda como estaba.
                Error? error = null;
                var aEnlazar = new List<CambioEnlaceN43>();
                foreach (var c in porLinea.Where(c => c.Enlazar))
                {
                    var doc = await DocumentoAsync(s, idSociedad, c.Tipo, c.IdDocumento);
                    if (doc is null)
                        error ??= new Error("BusquedaImporte.FacturaNoExiste", "No encuentro la factura elegida.", l.Rotulo,
                            "Se ha borrado o es de otra sociedad.", "Vuelve a buscar por importe.");
                    else if (doc.LineaN43 is int otra && otra != id)
                        error ??= new Error("BusquedaImporte.FacturaEnlazada", $"La factura {doc.Numero} ya está enlazada con otro movimiento ({otra}).", l.Rotulo,
                            "Se ha enlazado desde otro puesto o en otra búsqueda mientras tenías el diálogo abierto.",
                            "Vuelve a buscar por importe; si no le corresponde a ese otro movimiento, desmárcala allí antes.");
                    else if (doc.LineaN43 != id) aEnlazar.Add(c);
                }
                if (error is not null) { rechazadas[id] = error; continue; }

                var hechasLinea = 0;
                foreach (var c in porLinea.Where(c => !c.Enlazar))
                {
                    var n = await s.EjecutarAsync($"UPDATE {Tabla(c.Tipo)} SET LineaN43 = NULL WHERE {Clave(c.Tipo)} = ? AND LineaN43 = ?", c.IdDocumento, id);
                    desenlazadas += n;
                    hechasLinea += n;
                }
                foreach (var c in aEnlazar)
                {
                    var n = await s.EjecutarAsync($"UPDATE {Tabla(c.Tipo)} SET LineaN43 = ? WHERE {Clave(c.Tipo)} = ? AND (LineaN43 IS NULL OR LineaN43 = 0)", id, c.IdDocumento);
                    enlazadas += n;
                    hechasLinea += n;
                }
                if (hechasLinea > 0) cambiadas.Add(id);

                // Cuenta del tercero, solo si el movimiento no está contabilizado.
                if (l.IdAsiento is not null || hechasLinea == 0) continue;
                var enlazadasLinea = await EnlazadasAsync(s, id);
                if (enlazadasLinea.Count == 0) continue;
                var identidades = new List<(DocumentoN43 Doc, int? Identidad)>();
                foreach (var d in enlazadasLinea) identidades.Add((d.Doc, await IdentidadTerceroAsync(s, idSociedad, l, d)));
                var sinCuenta = identidades.FirstOrDefault(x => x.Identidad is null);
                if (sinCuenta.Doc is not null)
                {
                    avisos.Add($"{l.Rotulo}: queda enlazado, pero la factura {sinCuenta.Doc.Numero} no tiene cuenta de {(sinCuenta.Doc.Tipo == TipoDocumentoN43.Compra ? "proveedor" : "cliente")} " +
                        $"en el ejercicio {l.Ejercicio}, así que no le he cambiado la cuenta. Dásela al tercero en su ficha (o créala en el ejercicio) y asígnasela con «Asignar cuenta…» (F2).");
                    continue;
                }
                var distintas = identidades.Select(x => x.Identidad!.Value).Distinct().ToList();
                if (distintas.Count > 1)
                {
                    avisos.Add($"{l.Rotulo}: queda enlazado con facturas de terceros distintos ({string.Join(", ", identidades.Select(x => x.Doc.Numero))}), " +
                        "así que no le he cambiado la cuenta. Asígnasela con «Asignar cuenta…» (F2) o repártela.");
                    continue;
                }
                var actual = await s.ConsultarAsync(
                    "SELECT IdIdentidadContable FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", f => f.Entero("IdIdentidadContable"), id);
                if (actual.Count == 1 && actual[0] == distintas[0]) continue;
                await ServicioLineasN43.ImputarAsync(s, l, distintas[0], OrigenFactura);
                conCuenta.Add(id);
            }
            return new ResultadoEnlaceN43(enlazadas, desenlazadas, cambiadas, conCuenta, rechazadas, avisos);
        });
    }

    /// <summary>Por qué no se enlaza nada con la línea (null si se puede): las contabilizadas, también las del ERP, sí se enlazan.</summary>
    internal static Error? BloqueoEnlace(ServicioLineasN43.LineaEscritura l) =>
        l.IdAsiento is not null && !l.AsientoPropio ? null : ServicioLineasN43.Bloqueo(l);

    private static string Rotulo(IReadOnlyDictionary<int, ServicioLineasN43.LineaEscritura> lineas, int id) =>
        lineas.TryGetValue(id, out var l) ? l.Rotulo : $"Movimiento {id}";

    private static string Tabla(TipoDocumentoN43 t) => t == TipoDocumentoN43.Compra ? "FacturasCompra" : "Facturas";
    private static string Clave(TipoDocumentoN43 t) => t == TipoDocumentoN43.Compra ? "IdRegistro" : "IdFactura";

    private static Task<DocumentoN43?> DocumentoAsync(ISesionDatos s, string idSociedad, TipoDocumentoN43 tipo, int id) => tipo == TipoDocumentoN43.Compra
        ? s.ConsultarPrimeroAsync($"SELECT {ColumnasCompra} FROM {DesdeCompra} WHERE se.IdSociedad = ? AND fc.IdRegistro = ?", f => (DocumentoN43?)Compra(f), idSociedad, id)
        : s.ConsultarPrimeroAsync($"SELECT {ColumnasVenta} FROM {DesdeVenta} WHERE se.IdSociedad = ? AND f.IdFactura = ?", f => (DocumentoN43?)Venta(f), idSociedad, id);

    /// <summary>Datos para buscar la cuenta del tercero de una factura enlazada.</summary>
    private sealed record Enlazada(DocumentoN43 Doc, string? CuentaProveedor, int? IdProveedor, int? IdCliente);

    private static async Task<IReadOnlyList<Enlazada>> EnlazadasAsync(ISesionDatos s, int idLinea)
    {
        var r = new List<Enlazada>();
        r.AddRange(await s.ConsultarAsync(
            $"SELECT {ColumnasCompra}, fc.CuentaContableProveedor, fc.IdProveedor FROM {DesdeCompra} WHERE fc.LineaN43 = ?",
            f => new Enlazada(Compra(f), ConsultaLineasN43.Limpio(f.Texto("CuentaContableProveedor")), f.EnteroNulo("IdProveedor"), null), idLinea));
        r.AddRange(await s.ConsultarAsync(
            $"SELECT {ColumnasVenta}, f.IdCliente FROM {DesdeVenta} WHERE f.LineaN43 = ?",
            f => new Enlazada(Venta(f), null, null, f.EnteroNulo("IdCliente")), idLinea));
        return r;
    }

    /// <summary>
    /// Cuenta del tercero de la factura que existe en el ejercicio de la línea: en compras, la del proveedor que lleva la factura
    /// (CuentaContableProveedor) o, si no, la de su rol de proveedor o acreedor en la sociedad; en ventas, la del rol de cliente.
    /// </summary>
    private static async Task<int?> IdentidadTerceroAsync(ISesionDatos s, string idSociedad, ServicioLineasN43.LineaEscritura l, Enlazada e)
    {
        var ejercicio = l.IdSociedadEjercicio ?? 0;
        var candidatas = new List<int>();
        if (e.Doc.Tipo == TipoDocumentoN43.Compra)
        {
            if (e.CuentaProveedor is { } codigo
                && await s.EscalarAsync("SELECT TOP 1 IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", ejercicio, codigo) is { } id and not DBNull)
                return Convert.ToInt32(id, CultureInfo.InvariantCulture);
            if (e.IdProveedor is int p)
                candidatas.AddRange(await s.ConsultarAsync(
                    "SELECT IdIdentidadContable FROM CuentasProveedoresSociedad WHERE IdProveedor = ? AND IdSociedad = ? AND IdIdentidadContable IS NOT NULL ORDER BY Clase DESC",
                    f => f.Entero("IdIdentidadContable"), p, idSociedad));
        }
        else if (e.IdCliente is int c)
        {
            candidatas.AddRange(await s.ConsultarAsync(
                "SELECT IdIdentidadContable FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = ? AND Clase = 'CLIENTE' AND IdIdentidadContable IS NOT NULL",
                f => f.Entero("IdIdentidadContable"), c, idSociedad));
        }
        foreach (var identidad in candidatas)
        {
            var n = await s.EscalarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?", ejercicio, identidad);
            if (Convert.ToInt32(n, CultureInfo.InvariantCulture) > 0) return identidad;
        }
        return null;
    }

    /// <summary>Texto corto de una candidata para el diálogo: número, fecha, importe y tercero.</summary>
    public static string Describir(CandidatoN43 c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var d = c.Documento;
        return $"{d.Numero} · {d.Fecha:dd/MM/yyyy} · {d.Importe.ToString("N2", Es)} € · {d.Tercero}";
    }
}
