using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Busqueda;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Remesas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Plataformas;

/// <summary>Un abono de una plataforma tal como lo enseña la propuesta.</summary>
public sealed record LineaCobroPlataforma(int Id, DateTime Fecha, decimal Importe, string Plataforma, string? Concepto);

/// <summary>La factura de una reserva tal como la enseña la propuesta: número, huésped, fechas, total, comisión y cobro esperado.</summary>
public sealed record FacturaReservaPlataforma(int Id, string Numero, string? Titular, DateTime Entrada, DateTime Salida, decimal Total, decimal Comision,
    decimal Esperado, string Plataforma);

/// <summary>
/// Propuesta de «Conciliar plataformas»: los cruces (con sus líneas y facturas para enseñarlos), lo que no cruza con su motivo y las
/// reservas facturadas que siguen sin cobro.
/// </summary>
public sealed record PropuestaCobrosPlataformas(
    IReadOnlyList<CrucePlataforma> Cruces,
    IReadOnlyList<CobroSinCruzar> SinCruzar,
    IReadOnlyDictionary<int, LineaCobroPlataforma> Lineas,
    IReadOnlyDictionary<int, FacturaReservaPlataforma> Facturas)
{
    /// <summary>Facturas de reservas de las plataformas que no cobra ningún cruce.</summary>
    public IReadOnlyList<FacturaReservaPlataforma> SinCobro =>
        Facturas.Values.Where(f => !Cruces.Any(c => c.Facturas.Contains(f.Id))).OrderBy(f => f.Entrada).ThenBy(f => f.Id).ToList();
}

/// <summary>Resultado de conciliar o deshacer: cruces hechos, líneas y facturas tocadas, cruces rechazados con su motivo y avisos.</summary>
public sealed record ResultadoCobrosPlataformas(int Cruces, IReadOnlyList<int> Lineas, IReadOnlyList<int> Facturas, IReadOnlyList<Error> Rechazados,
    IReadOnlyList<string> Avisos);

/// <summary>
/// Conciliación automática de los cobros de las plataformas de venta (norma43/conciliacion/plataformas 2026-11-25): busca los abonos de
/// cada plataforma (por los textos de su origen de venta) y las facturas validadas de sus reservas pendientes de cobro, los cruza con
/// <see cref="ReglasCobrosPlataformas"/> y, cuando el operador acepta, los enlaza: cada cobro que completa una factura va en
/// Facturas.LineaN43 (como en «Buscar por importe…») y los trozos que no la completan en CobrosParcialesN43. A cada línea sin
/// contabilizar le pone la 430 de la plataforma (la del cliente de las facturas, que comparten los huéspedes). El asiento lo sigue
/// haciendo la conciliación de la línea y la comisión la cuadra la compensación 410/430 de la factura de compra de la plataforma.
/// </summary>
public sealed class ServicioCobrosPlataformasN43(IBaseDatos bd)
{
    /// <summary>Origen de la imputación que pone este proceso.</summary>
    public const string OrigenImputacion = "PLATAFORMA";

    /// <summary>Días antes del periodo en que se buscan entradas de reservas: los cobros del principio del periodo pagan reservas anteriores.</summary>
    public const int DiasReservasAntes = 90;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Plataforma: origen de venta que no es venta directa, con los textos que la reconocen en el concepto del banco.</summary>
    private sealed record Plataforma(int Id, string Nombre, IReadOnlyList<string> Textos, PlazosCobroPlataforma Plazos);

    /// <summary>Los textos que reconocen a la plataforma en el banco: los de importación (sin lo que va entre corchetes) y su nombre.</summary>
    internal static IReadOnlyList<string> Textos(string nombre, string? textosImportacion)
    {
        var r = new List<string>();
        foreach (var t in (textosImportacion ?? "").Split(';'))
        {
            var limpio = t;
            var corchete = limpio.IndexOf('[', StringComparison.Ordinal);
            if (corchete >= 0) limpio = limpio[..corchete];
            limpio = limpio.Trim();
            if (limpio.Length >= 3) r.Add(limpio);
        }
        if (!string.IsNullOrWhiteSpace(nombre) && nombre.Trim().Length >= 3) r.Add(nombre.Trim());
        return r.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- propuesta ----------

    /// <summary>
    /// Cruza los abonos de las plataformas entre <paramref name="desde"/> y <paramref name="hasta"/> (fecha de operación) que no están
    /// enlazados con ninguna factura y se pueden enlazar, con las facturas validadas de sus reservas pendientes de cobro (con entrada
    /// desde <see cref="DiasReservasAntes"/> días antes). No graba nada.
    /// </summary>
    public Task<PropuestaCobrosPlataformas> ProponerAsync(string idSociedad, DateTime desde, DateTime hasta) => bd.EjecutarAsync(async s =>
    {
        var plataformas = await PlataformasAsync(s, idSociedad);
        var lineas = await LineasAsync(s, idSociedad, plataformas, desde.Date, hasta.Date);
        var facturas = await FacturasAsync(s, idSociedad, plataformas, desde.Date.AddDays(-DiasReservasAntes), hasta.Date);
        var plazos = plataformas.ToDictionary(p => p.Id, p => p.Plazos);
        var cruce = ReglasCobrosPlataformas.Cruzar(
            lineas.Select(l => l.Cobro).ToList(), facturas.Select(f => f.Reserva).ToList(), plazos);
        return new PropuestaCobrosPlataformas(cruce.Cruces, cruce.SinCruzar,
            lineas.ToDictionary(l => l.Vista.Id, l => l.Vista), facturas.ToDictionary(f => f.Vista.Id, f => f.Vista));
    });

    private static async Task<IReadOnlyList<Plataforma>> PlataformasAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync(
            "SELECT IdOrigenVenta, Codigo, Nombre, TextosImportacion, CargoCobroPorcentaje, ReferenciaCobro, DiasCobroTrasSalida, DiasCobroHabitualDesde, DiasCobroHabitualHasta " +
            "FROM OrigenesVenta WHERE IdSociedad = ? AND EsDirecta = False AND Activo = True",
            f =>
            {
                // Lo que no esté configurado en el origen sale de lo conocido de la plataforma (por su código) o de lo predeterminado.
                var p = PlazosCobroPlataforma.Para(f.Texto("Codigo"));
                var referencia = ConsultaLineasN43.Limpio(f.Texto("ReferenciaCobro"));
                var desde = f.EnteroNulo("DiasCobroHabitualDesde");
                var hasta = f.EnteroNulo("DiasCobroHabitualHasta");
                p = new PlazosCobroPlataforma(f.DecimalNulo("CargoCobroPorcentaje") ?? p.CargoPorcentaje,
                    referencia is null ? p.DesdeSalida : string.Equals(referencia, "SALIDA", StringComparison.OrdinalIgnoreCase),
                    f.EnteroNulo("DiasCobroTrasSalida") ?? p.DiasTrasSalida, desde is null || hasta is null ? p.HabitualDesde : desde, desde is null || hasta is null ? p.HabitualHasta : hasta);
                var nombre = ConsultaLineasN43.Limpio(f.Texto("Nombre")) ?? "";
                return new Plataforma(f.Entero("IdOrigenVenta"), nombre, Textos(nombre, f.Texto("TextosImportacion")), p);
            }, idSociedad))
        .Where(p => p.Textos.Count > 0).ToList();

    private static async Task<IReadOnlyList<(CobroPlataforma Cobro, LineaCobroPlataforma Vista)>> LineasAsync(ISesionDatos s, string idSociedad,
        IReadOnlyList<Plataforma> plataformas, DateTime desde, DateTime hasta)
    {
        if (plataformas.Count == 0) return [];
        var filas = await s.ConsultarAsync(
            "SELECT l.IdLineaExtractoBancario, l.FechaOperacion, l.Importe, l.ConceptoCompleto " +
            "FROM (LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
            "WHERE lo.IdSociedad = ? AND lo.Estado <> ? AND l.Sentido = ? AND l.FechaOperacion >= ? AND l.FechaOperacion < ?",
            f => (Id: f.Entero("IdLineaExtractoBancario"), Fecha: f.Fecha("FechaOperacion").Date, Importe: f.Decimal("Importe"),
                Concepto: ConsultaLineasN43.Limpio(f.Texto("ConceptoCompleto"))),
            idSociedad, Importacion.ServicioImportacionN43.EstadoRevertido, Conciliacion.ServicioConciliacionN43.Abono, desde, hasta.AddDays(1));
        var candidatas = new List<(CobroPlataforma, LineaCobroPlataforma)>();
        foreach (var f in filas)
        {
            if (f.Concepto is null) continue;
            var p = plataformas.FirstOrDefault(p => p.Textos.Any(t => f.Concepto.Contains(t, StringComparison.OrdinalIgnoreCase)));
            if (p is null) continue;
            candidatas.Add((new CobroPlataforma(f.Id, p.Id, f.Fecha, f.Importe), new LineaCobroPlataforma(f.Id, f.Fecha, f.Importe, p.Nombre, f.Concepto)));
        }
        if (candidatas.Count == 0) return [];
        var enlazadas = await EnlazadasAsync(s, candidatas.Select(c => c.Item1.IdLinea).ToList());
        var escritura = await ServicioLineasN43.LeerAsync(s, idSociedad, candidatas.Select(c => c.Item1.IdLinea).ToList());
        return candidatas.Where(c => !enlazadas.Contains(c.Item1.IdLinea)
            && escritura.TryGetValue(c.Item1.IdLinea, out var l) && BusquedaPorImporteN43.BloqueoEnlace(l) is null).ToList();
    }

    /// <summary>De las líneas indicadas, las que ya están enlazadas con alguna factura (de compra o de venta, cobro, devolución, reintento o trozo).</summary>
    internal static async Task<IReadOnlySet<int>> EnlazadasAsync(ISesionDatos s, IReadOnlyCollection<int> ids)
    {
        var r = new HashSet<int>();
        foreach (var bloque in ids.Distinct().Chunk(300))
        {
            var lista = ConsultaLineasN43.Lista(bloque);
            foreach (var (tabla, columna) in new[]
                     {
                         ("FacturasCompra", "LineaN43"), ("Facturas", "LineaN43"), ("Facturas", "LineaN43Devolucion"),
                         ("ReintentosSEPA", "LineaN43"), ("ReintentosSEPA", "LineaN43Devolucion"), ("CobrosParcialesN43", "LineaN43"),
                     })
                r.UnionWith(await s.ConsultarAsync($"SELECT {columna} AS Linea FROM {tabla} WHERE {columna} IN ({lista})", f => f.Entero("Linea")));
        }
        return r;
    }

    private const string ColumnasFactura =
        "f.IdFactura, f.IdCliente, s.Prefijo, f.NumeroFactura, f.Nombre, f.RazonSocial, f.ImporteTotal, v.IdOrigenVenta, v.FechaReferencia, v.FechaFin, v.Comision";

    private const string DesdeFactura =
        "((VentasPorFacturar AS v INNER JOIN Facturas AS f ON v.IdFactura = f.IdFactura) " +
        "INNER JOIN SociedadesEjercicios AS se ON f.IdSociedadEjercicio = se.IdSociedadEjercicio) LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie";

    /// <summary>Facturas con número (validadas, rectificadas o pendientes de rectificar), como en «Buscar por importe…».</summary>
    private const string ConNumero = "f.NumeroFactura IS NOT NULL AND f.EstadoFactura IN (1, 2, 4)";

    private static async Task<IReadOnlyList<(ReservaFacturada Reserva, FacturaReservaPlataforma Vista)>> FacturasAsync(ISesionDatos s, string idSociedad,
        IReadOnlyList<Plataforma> plataformas, DateTime desde, DateTime hasta)
    {
        if (plataformas.Count == 0) return [];
        var porId = plataformas.ToDictionary(p => p.Id);
        var origenes = string.Join(",", plataformas.Select(p => p.Id.ToString(CultureInfo.InvariantCulture)));
        var filas = (await s.ConsultarAsync(
                $"SELECT {ColumnasFactura} FROM {DesdeFactura} WHERE se.IdSociedad = ? AND v.IdSociedad = ? AND v.Anulada = False AND {ConNumero} " +
                $"AND v.IdOrigenVenta IN ({origenes}) AND v.FechaReferencia >= ? AND v.FechaReferencia < ?",
                Fila, idSociedad, idSociedad, desde, hasta.AddDays(1)))
            .GroupBy(f => f.Id).Where(g => g.Count() == 1).Select(g => g.First()) // una factura por reserva: las mensuales agrupadas no se cruzan
            .ToList();
        if (filas.Count == 0) return [];
        var pendientes = await CobrosFacturasN43.PendientesAsync(s, filas.Select(f => f.Id).ToList());
        var conTrozos = await ConTrozosAsync(s, filas.Select(f => f.Id).ToList());
        return filas.Where(f => pendientes.Contains(f.Id) && !conTrozos.Contains(f.Id)).Select(f =>
        {
            var p = porId[f.Origen];
            var reserva = new ReservaFacturada(f.Id, f.Origen, f.Entrada, f.Salida ?? f.Entrada, f.Total, f.Comision);
            return (reserva, new FacturaReservaPlataforma(f.Id, f.Numero, f.Titular, reserva.Entrada, reserva.Salida, f.Total, f.Comision,
                ReglasCobrosPlataformas.CobroEsperado(reserva, p.Plazos), p.Nombre));
        }).ToList();
    }

    private sealed record FilaFactura(int Id, int? IdCliente, string Numero, string? Titular, decimal Total, int Origen, DateTime Entrada, DateTime? Salida, decimal Comision);

    private static FilaFactura Fila(IFilaDatos f) => new(f.Entero("IdFactura"), f.EnteroNulo("IdCliente"),
        (ConsultaLineasN43.Limpio(f.Texto("Prefijo")) ?? "") + (ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? ""),
        ConsultaLineasN43.Limpio(f.Texto("RazonSocial")) ?? ConsultaLineasN43.Limpio(f.Texto("Nombre")), f.Decimal("ImporteTotal"),
        f.Entero("IdOrigenVenta"), f.Fecha("FechaReferencia").Date, f.FechaNula("FechaFin")?.Date, f.DecimalNulo("Comision") ?? 0m);

    private static async Task<IReadOnlySet<int>> ConTrozosAsync(ISesionDatos s, IReadOnlyCollection<int> ids)
    {
        var r = new HashSet<int>();
        foreach (var bloque in ids.Distinct().Chunk(300))
            r.UnionWith(await s.ConsultarAsync($"SELECT IdFactura FROM CobrosParcialesN43 WHERE IdFactura IN ({ConsultaLineasN43.Lista(bloque)})",
                f => f.Entero("IdFactura")));
        return r;
    }

    // ---------- conciliar ----------

    /// <summary>
    /// Enlaza los cruces aceptados, cada uno en su transacción: si una de sus líneas ya está enlazada, se ha revertido o es de un ejercicio
    /// cerrado, o una de sus facturas ya está cobrada, el cruce se rechaza entero con el motivo y los demás siguen. El reparto de cada
    /// cobro sale del cruce: la parte que completa una factura va en Facturas.LineaN43 y las demás en CobrosParcialesN43. Después pone
    /// la 430 de las facturas a las líneas sin contabilizar.
    /// </summary>
    public async Task<ResultadoCobrosPlataformas> ConciliarAsync(string idSociedad, IReadOnlyList<CrucePlataforma> cruces, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(cruces);
        ArgumentNullException.ThrowIfNull(actor);
        var hechos = 0;
        var lineas = new List<int>();
        var facturas = new List<int>();
        var rechazados = new List<Error>();
        var avisos = new List<string>();
        foreach (var c in cruces)
        {
            var r = await bd.EnTransaccionAsync(s => ConciliarCruceAsync(s, idSociedad, c));
            if (r.Error is not null) { rechazados.Add(r.Error); continue; }
            hechos++;
            lineas.AddRange(c.Lineas);
            facturas.AddRange(c.Facturas);
            avisos.AddRange(r.Avisos);
        }
        return new ResultadoCobrosPlataformas(hechos, lineas, facturas, rechazados, avisos);
    }

    private static async Task<(Error? Error, IReadOnlyList<string> Avisos)> ConciliarCruceAsync(ISesionDatos s, string idSociedad, CrucePlataforma c)
    {
        var lineas = await ServicioLineasN43.LeerAsync(s, idSociedad, c.Lineas);
        var rotulo = string.Join(", ", c.Lineas.Select(id => lineas.TryGetValue(id, out var l) ? l.Rotulo : $"Movimiento {id}"));
        foreach (var id in c.Lineas)
        {
            if (!lineas.TryGetValue(id, out var l))
                return (new Error("Plataformas.LineaNoExiste", $"No encuentro el movimiento {id}.", rotulo, "Se ha revertido su extracto o es de otra sociedad.",
                    "Vuelve a pulsar «Conciliar plataformas»."), []);
            if (BusquedaPorImporteN43.BloqueoEnlace(l) is { } bloqueo) return (bloqueo with { RegistroAfectado = rotulo }, []);
        }
        if ((await EnlazadasAsync(s, c.Lineas)).Count > 0)
            return (new Error("Plataformas.LineaEnlazada", "Uno de estos movimientos ya está enlazado con una factura.", rotulo,
                "Se ha enlazado desde otro puesto o con «Buscar por importe…» mientras tenías la propuesta abierta.", "Vuelve a pulsar «Conciliar plataformas»."), []);
        var datos = new Dictionary<int, FilaFactura>();
        foreach (var id in c.Facturas)
        {
            var f = await s.ConsultarPrimeroAsync($"SELECT {ColumnasFactura} FROM {DesdeFactura} WHERE se.IdSociedad = ? AND f.IdFactura = ? AND {ConNumero}", Fila, idSociedad, id);
            if (f is null)
                return (new Error("Plataformas.FacturaNoExiste", $"No encuentro la factura {id} validada.", rotulo, "Se ha borrado, rectificado o es de otra sociedad.",
                    "Vuelve a pulsar «Conciliar plataformas»."), []);
            datos[id] = f;
        }
        var pendientes = await CobrosFacturasN43.PendientesAsync(s, c.Facturas);
        var trozos = await ConTrozosAsync(s, c.Facturas);
        if (c.Facturas.FirstOrDefault(id => !pendientes.Contains(id) || trozos.Contains(id)) is var cobrada and > 0)
            return (new Error("Plataformas.FacturaCobrada", $"La factura {datos[cobrada].Numero} ya está cobrada.", rotulo,
                "Se ha enlazado con otro movimiento mientras tenías la propuesta abierta.", "Vuelve a pulsar «Conciliar plataformas»."), []);

        foreach (var a in c.Reparto)
        {
            if (a.Completa)
            {
                if (!await CobrosFacturasN43.RegistrarCobroAsync(s, a.IdFactura, a.IdLinea))
                    throw new InvalidOperationException($"No pude anotar el cobro de la factura {a.IdFactura}."); // comprobado arriba
            }
            else
                await s.EjecutarAsync("INSERT INTO CobrosParcialesN43 (IdFactura, LineaN43, Importe) VALUES (?, ?, ?)", a.IdFactura, a.IdLinea, a.Importe);
        }

        // La 430 de las facturas (la del cliente de la regla de venta, que comparten los huéspedes) en las líneas sin contabilizar.
        var avisos = new List<string>();
        foreach (var id in c.Lineas)
        {
            var l = lineas[id];
            if (l.IdAsiento is not null) continue;
            var identidades = new HashSet<int?>();
            foreach (var f in c.Reparto.Where(a => a.IdLinea == id).Select(a => datos[a.IdFactura]).Distinct())
                identidades.Add(await IdentidadClienteAsync(s, idSociedad, l.IdSociedadEjercicio ?? 0, f.IdCliente));
            if (identidades.Count != 1 || identidades.Single() is not int identidad)
            {
                avisos.Add($"{l.Rotulo}: queda conciliado, pero no le he puesto la cuenta porque sus facturas no tienen una sola cuenta de cliente en el ejercicio {l.Ejercicio}. " +
                    "Asígnasela con «Asignar cuenta…» (F2).");
                continue;
            }
            var actual = await s.ConsultarAsync(
                "SELECT IdIdentidadContable FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", f => f.Entero("IdIdentidadContable"), id);
            if (actual.Count == 1 && actual[0] == identidad) continue;
            await ServicioLineasN43.ImputarAsync(s, l, identidad, OrigenImputacion);
        }
        return (null, avisos);
    }

    /// <summary>Cuenta de cliente del tercero en la sociedad que existe en el ejercicio (null si no hay).</summary>
    private static async Task<int?> IdentidadClienteAsync(ISesionDatos s, string idSociedad, int idSociedadEjercicio, int? idCliente)
    {
        if (idCliente is not int c) return null;
        foreach (var identidad in await s.ConsultarAsync(
                     "SELECT IdIdentidadContable FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = ? AND Clase = 'CLIENTE' AND IdIdentidadContable IS NOT NULL",
                     f => f.Entero("IdIdentidadContable"), c, idSociedad))
        {
            var n = await s.EscalarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?", idSociedadEjercicio, identidad);
            if (Convert.ToInt32(n, CultureInfo.InvariantCulture) > 0) return identidad;
        }
        return null;
    }

    // ---------- deshacer ----------

    /// <summary>
    /// Quita los cobros de las líneas indicadas y de todo su cruce (las demás líneas y facturas que van con ellas: los trozos de una
    /// misma factura, las facturas de un mismo cobro), en una transacción. Las facturas vuelven a estar pendientes de cobro. La cuenta
    /// de las líneas no se toca (sigue siendo la 430 de la plataforma). No quita devoluciones ni reintentos de recibos.
    /// </summary>
    public Task<ResultadoCobrosPlataformas> DeshacerAsync(string idSociedad, IReadOnlyCollection<int> idsLineas, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(idsLineas);
        ArgumentNullException.ThrowIfNull(actor);
        return bd.EnTransaccionAsync(async s =>
        {
            var lineas = new HashSet<int>(idsLineas);
            var facturas = new HashSet<int>();
            var pendientes = new Queue<int>(lineas);
            while (pendientes.Count > 0)
            {
                var id = pendientes.Dequeue();
                // Solo las facturas de reservas de las plataformas: los recibos de remesas y lo enlazado a mano se quitan desde sus acciones.
                var nuevas = (await s.ConsultarAsync(
                        "SELECT f.IdFactura FROM (Facturas AS f INNER JOIN VentasPorFacturar AS v ON f.IdFactura = v.IdFactura) " +
                        "INNER JOIN OrigenesVenta AS o ON v.IdOrigenVenta = o.IdOrigenVenta WHERE o.EsDirecta = False AND f.LineaN43 = ?", f => f.Entero("IdFactura"), id))
                    .Concat(await s.ConsultarAsync("SELECT IdFactura FROM CobrosParcialesN43 WHERE LineaN43 = ?", f => f.Entero("IdFactura"), id))
                    .Where(facturas.Add).ToList();
                foreach (var f in nuevas)
                {
                    var suyas = (await s.ConsultarAsync("SELECT LineaN43 FROM CobrosParcialesN43 WHERE IdFactura = ?", x => x.Entero("LineaN43"), f))
                        .Concat((await s.ConsultarAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", x => x.EnteroNulo("LineaN43"), f)).OfType<int>().Where(x => x > 0));
                    foreach (var l in suyas)
                        if (lineas.Add(l)) pendientes.Enqueue(l);
                }
            }
            // Solo las facturas cuyas líneas son de esta sociedad; primero se comprueba todo, para hacerlo todo o nada.
            var propias = await ServicioLineasN43.LeerAsync(s, idSociedad, lineas);
            var rechazados = new List<Error>();
            var aQuitar = new List<(int Factura, int? Cobro)>();
            foreach (var f in facturas.Order())
            {
                var ultimo = await CobrosFacturasN43.UltimoAsync(s, f);
                if (ultimo is not null && !propias.ContainsKey(ultimo.LineaCobro)) continue;
                if (ultimo is { Intento: > 1 } or { Devuelto: true })
                {
                    rechazados.Add(new Error("Plataformas.CobroDevuelto", $"No puedo quitar el cobro de la factura {f}.", $"Factura {f}",
                        "Su cobro tiene una devolución o un reintento posterior.", "Quita antes la devolución desde la pantalla de líneas N43."));
                    continue;
                }
                aQuitar.Add((f, ultimo?.LineaCobro));
            }
            if (rechazados.Count > 0) return new ResultadoCobrosPlataformas(0, idsLineas.ToList(), [], rechazados, []);
            foreach (var (f, cobro) in aQuitar)
            {
                if (cobro is int linea && !await CobrosFacturasN43.QuitarCobroAsync(s, f, linea))
                    throw new InvalidOperationException($"No pude quitar el cobro de la factura {f}."); // comprobado arriba
                await s.EjecutarAsync("DELETE FROM CobrosParcialesN43 WHERE IdFactura = ?", f);
            }
            return new ResultadoCobrosPlataformas(aQuitar.Count == 0 ? 0 : 1, lineas.Order().ToList(), aQuitar.Select(x => x.Factura).ToList(), [], []);
        });
    }

    /// <summary>Texto corto de un cobro para la propuesta: fecha, importe y plataforma.</summary>
    public static string Describir(LineaCobroPlataforma l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return $"{l.Fecha:dd/MM/yyyy} · {l.Importe.ToString("N2", Es)} € · {l.Plataforma}";
    }

    /// <summary>Texto corto de una factura para la propuesta: número, huésped, entrada y cobro esperado.</summary>
    public static string Describir(FacturaReservaPlataforma f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return $"{f.Numero} · {f.Titular} · entrada {f.Entrada:dd/MM/yyyy} · espera {f.Esperado.ToString("N2", Es)} €";
    }
}
