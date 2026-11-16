using System.Globalization;
using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Remesas;

/// <summary>Propuesta para explotar una remesa: la línea, las facturas candidatas y, si no se puede, por qué.</summary>
public sealed record PropuestaRemesaN43(LineaN43 Linea, IReadOnlyList<CandidataRemesa> Candidatas, Error? Bloqueo);

/// <summary>Remesa explotada anterior a una devolución, para elegir de cuál salen los recibos devueltos.</summary>
public sealed record RemesaAnteriorN43(int IdExtracto, int IdLineaRemesa, DateTime Fecha, decimal Importe, int Recibos)
{
    public string Texto => $"Remesa del {Fecha:dd/MM/yyyy} · {Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} € · {Recibos} recibo(s)";
}

/// <summary>Propuesta para conciliar una devolución: la línea, las remesas anteriores, la elegida y sus recibos cobrados.</summary>
public sealed record PropuestaDevolucionN43(LineaN43 Linea, IReadOnlyList<RemesaAnteriorN43> Remesas, RemesaAnteriorN43? Remesa, IReadOnlyList<CandidataDevolucion> Candidatas, Error? Bloqueo);

/// <summary>Resultado de explotar, conciliar o deshacer: las líneas de recibo creadas (o quitadas), el texto para el operador y el error si no se hizo.</summary>
public sealed record ResultadoRemesaN43(int? IdExtracto, IReadOnlyList<int> Recibos, string Texto, Error? Error)
{
    public bool Hecho => Error is null;
    public static ResultadoRemesaN43 Fallo(Error e) => new(null, [], e.Mensaje, e);
}

/// <summary>Resultado de «Explotar remesas pendientes»: las explotadas y las que se quedan para el diálogo, con su motivo.</summary>
public sealed record ResultadoPendientesN43(IReadOnlyList<int> Explotadas, IReadOnlyDictionary<int, string> Pendientes)
{
    public string Texto => (Explotadas.Count, Pendientes.Count) switch
    {
        (0, 0) => "No hay remesas pendientes de explotar en los ejercicios elegidos.",
        (_, 0) => $"Explotadas {Explotadas.Count} remesa(s): todas cuadraban.",
        _ => $"Explotadas {Explotadas.Count} remesa(s). {Pendientes.Count} no cuadran solas: ábrelas con «Explotar remesa…» para elegir sus facturas. " +
             string.Join(" ", Pendientes.Values.Take(5)) + (Pendientes.Count > 5 ? $" (y {Pendientes.Count - 5} más)." : ""),
    };
}

/// <summary>
/// Explosión de remesas de recibos y conciliación de recibos devueltos (norma43/remesas 2026-11-16). Una remesa (abono del banco con
/// palabra clave Especial 5) se desglosa como hacía el ERP: un extracto ficticio en la cuenta bancaria de remesas de su banco (misma
/// entidad, cuenta 0000000000) con una línea por recibo, llevada a la 430 del cliente, y cada factura queda cobrada por su línea de
/// recibo; la línea original sigue con su cuenta (la 555 de remesas). Una devolución (cargo, Especial 6) se concilia con recibos de la
/// remesa anterior: con un solo recibo, la propia línea va a la 430 del cliente; con varios se desglosa en la cuenta ficticia de recibos
/// devueltos (cuenta 0000000001). Deshacer revierte el extracto ficticio (auditado, sin borrar filas) y libera las facturas.
/// </summary>
public sealed partial class ServicioRemesasN43
{
    /// <summary>Cuenta (CCC) de la cuenta bancaria ficticia de remesas de cada banco.</summary>
    public const string CuentaRemesas = "0000000000";
    /// <summary>Cuenta (CCC) de la cuenta bancaria ficticia de recibos devueltos de cada banco.</summary>
    public const string CuentaDevueltos = "0000000001";
    /// <summary>Formato de los lotes de los extractos ficticios.</summary>
    public const string Formato = "REMESA";
    public const string OrigenRemesa = "REMESA";
    public const string OrigenDevolucion = "DEVOLUCION";
    /// <summary>Remesas explotadas anteriores a una devolución que se ofrecen para elegir.</summary>
    public const int RemesasAnteriores = 6;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly IBaseDatos _bd;
    private readonly IReloj _reloj;
    private readonly ConsultaLineasN43 _consulta;

    public ServicioRemesasN43(IBaseDatos bd, IReloj? reloj = null)
    {
        _bd = bd;
        _reloj = reloj ?? RelojSistema.Instancia;
        _consulta = new ConsultaLineasN43(bd);
    }

    // ---------- remesas ----------

    /// <summary>Facturas propuestas para la remesa de la línea (las SEPA del mes pendientes de cobro), o por qué no se puede explotar.</summary>
    public async Task<PropuestaRemesaN43?> ProponerRemesaAsync(string idSociedad, int idLinea)
    {
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return null;
        if (BloqueoRemesa(l) is { } e) return new PropuestaRemesaN43(l, [], e);
        var facturas = await _bd.EjecutarAsync(s => FacturasDelMesAsync(s, idSociedad, l.FechaOperacion));
        return new PropuestaRemesaN43(l, ReglasRemesasN43.ProponerRemesa(l, facturas), null);
    }

    /// <summary>Por qué no se puede explotar la línea como remesa (null si se puede).</summary>
    public static Error? BloqueoRemesa(LineaN43 l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return l.EsRecibo ? new Error("Remesas.EsRecibo", "Este movimiento ya es un recibo de una remesa explotada.", l.Rotulo, null, "Elige la línea de la remesa del banco.")
            : l.Explotada ? new Error("Remesas.YaExplotada", "Esta remesa ya está explotada.", l.Rotulo, "Sus recibos están en un extracto ficticio aparte.",
                "Si hay que repetirla, usa antes «Deshacer explosión».")
            : l.EsCargo ? new Error("Remesas.NoEsAbono", "Una remesa de recibos es un abono del banco y este movimiento es un cargo.", l.Rotulo, null,
                "Si es un recibo devuelto, usa «Conciliar facturas devueltas…».")
            : l.EjercicioCerrado ? new Error("Remesas.EjercicioCerrado", $"El ejercicio {l.Ejercicio} está cerrado.", l.Rotulo, "Sus líneas solo se consultan.", null)
            : null;
    }

    /// <summary>
    /// Explota la remesa con los recibos que deja el operador (factura e importe cobrado), todo o nada: la suma tiene que ser la del
    /// movimiento al céntimo, cada factura de la sociedad, pendiente de cobro y con cuenta de cliente en el ejercicio.
    /// </summary>
    public async Task<ResultadoRemesaN43> ExplotarRemesaAsync(string idSociedad, int idLinea, IReadOnlyList<ReciboRemesa> recibos, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(recibos);
        ArgumentNullException.ThrowIfNull(actor);
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return ResultadoRemesaN43.Fallo(NoExiste(idLinea));
        if (BloqueoRemesa(l) is { } bloqueo) return ResultadoRemesaN43.Fallo(bloqueo);
        if (ErrorRecibos(l, recibos.Select(r => (r.IdFactura, r.Importe)).ToList()) is { } e) return ResultadoRemesaN43.Fallo(e);
        return await _bd.EnTransaccionAsync(async s =>
        {
            var preparados = new List<(FacturaRemesa Factura, decimal Importe, int Identidad)>();
            foreach (var r in recibos)
            {
                var f = await FacturaAsync(s, idSociedad, r.IdFactura);
                if (f is null) return ResultadoRemesaN43.Fallo(new Error("Remesas.FacturaNoExiste", "No encuentro una de las facturas elegidas.", l.Rotulo, "Se ha borrado o es de otra sociedad.", "Vuelve a abrir «Explotar remesa…»."));
                if (!await CobrosFacturasN43.PendienteAsync(s, f.Id))
                    return ResultadoRemesaN43.Fallo(new Error("Remesas.FacturaCobrada", $"La factura {f.Numero} ya está cobrada con otro movimiento.", l.Rotulo,
                        "Se ha enlazado desde otro puesto o con «Buscar por importe» mientras tenías el diálogo abierto.", "Vuelve a abrir «Explotar remesa…»; si no le toca a ese movimiento, quítasela allí antes."));
                if (await IdentidadClienteAsync(s, idSociedad, l.Ejercicio, f.IdCliente) is not int identidad)
                    return ResultadoRemesaN43.Fallo(SinCuentaCliente(l, f));
                preparados.Add((f, r.Importe, identidad));
            }
            var ficticia = await CuentaFicticiaAsync(s, idSociedad, l, CuentaRemesas);
            if (ficticia.Error is { } ef) return ResultadoRemesaN43.Fallo(ef);
            var (idExtracto, ids) = await EscribirDesgloseAsync(s, idSociedad, l, ficticia.Id, devolucion: false,
                preparados.Select(p => (p.Factura, p.Importe, p.Identidad)).ToList(), actor);
            for (var i = 0; i < ids.Count; i++)
                if (!await CobrosFacturasN43.RegistrarCobroAsync(s, preparados[i].Factura.Id, ids[i]))
                    throw new InvalidOperationException($"La factura {preparados[i].Factura.Numero} dejó de estar pendiente durante la explosión.");
            await ImputarOrigenSiFaltaAsync(s, l, ficticia.Identidad, OrigenRemesa);
            return new ResultadoRemesaN43(idExtracto, ids,
                $"{l.Rotulo} explotado en {ids.Count} recibo(s) en «{ficticia.Nombre}»; las facturas quedan cobradas.", null);
        });
    }

    /// <summary>
    /// Explota las remesas sin explotar de los ejercicios elegidos (líneas con palabra clave de remesa) cuyas facturas marcadas cuadran
    /// solas; el resto se queda para el diálogo, con su motivo.
    /// </summary>
    public async Task<ResultadoPendientesN43> ExplotarPendientesAsync(string idSociedad, IReadOnlyCollection<int> anios, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(anios);
        var lineas = (await _consulta.LineasAsync(idSociedad, anios))
            .Where(l => l.Especial == ReglasPalabrasClave.EspecialRemesa && !l.Explotada && !l.EsRecibo && !l.EsCargo && !l.EjercicioCerrado).ToList();
        var hechas = new List<int>();
        var pendientes = new Dictionary<int, string>();
        foreach (var l in lineas)
        {
            var p = await ProponerRemesaAsync(idSociedad, l.Id);
            if (p is null || p.Bloqueo is not null) continue;
            var marcadas = p.Candidatas.Where(c => c.Marcada).ToList();
            if (marcadas.Count == 0 || marcadas.Sum(c => c.Factura.Importe) != l.Importe)
            {
                pendientes[l.Id] = $"{l.Rotulo}: " + ReglasRemesasN43.TextoCuadre(l.Importe, marcadas.Select(c => c.Factura.Importe).ToList());
                continue;
            }
            var r = await ExplotarRemesaAsync(idSociedad, l.Id, marcadas.Select(c => new ReciboRemesa(c.Factura.Id, c.Factura.Importe)).ToList(), actor);
            if (r.Hecho) hechas.Add(l.Id);
            else pendientes[l.Id] = $"{l.Rotulo}: {r.Error!.Mensaje}";
        }
        return new ResultadoPendientesN43(hechas, pendientes);
    }

    // ---------- devoluciones ----------

    /// <summary>
    /// Recibos que pueden haberse devuelto en la línea: los aún cobrados de la remesa explotada más cercana anterior al movimiento
    /// (o de la indicada), con la combinación que suma su importe ya marcada.
    /// </summary>
    public async Task<PropuestaDevolucionN43?> ProponerDevolucionAsync(string idSociedad, int idLinea, int? idExtractoRemesa = null)
    {
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return null;
        if (BloqueoDevolucion(l) is { } e) return new PropuestaDevolucionN43(l, [], null, [], e);
        return await _bd.EjecutarAsync(async s =>
        {
            var remesas = await RemesasAnterioresAsync(s, idSociedad, l);
            var remesa = idExtractoRemesa is int id ? remesas.FirstOrDefault(r => r.IdExtracto == id) : remesas.FirstOrDefault();
            if (remesa is null)
                return new PropuestaDevolucionN43(l, remesas, null, [], new Error("Remesas.SinRemesaAnterior", "No hay ninguna remesa explotada anterior a este movimiento.", l.Rotulo,
                    "Los recibos devueltos se buscan entre los de las remesas explotadas.", "Explota antes la remesa en que se cobraron (menú contextual, «Explotar remesa…»)."));
            var recibos = await RecibosCobradosAsync(s, idSociedad, remesa.IdExtracto);
            return new PropuestaDevolucionN43(l, remesas, remesa, ReglasRemesasN43.ProponerDevolucion(l.Importe, recibos), null);
        });
    }

    /// <summary>Por qué no se puede conciliar la línea como devolución (null si se puede).</summary>
    public static Error? BloqueoDevolucion(LineaN43 l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return l.EsRecibo ? new Error("Remesas.EsRecibo", "Este movimiento ya es un recibo de una devolución explotada.", l.Rotulo, null, "Elige la línea de la devolución del banco.")
            : l.Explotada ? new Error("Remesas.YaExplotada", "Esta devolución ya está conciliada con sus facturas.", l.Rotulo, null, "Si hay que repetirla, usa antes «Deshacer explosión».")
            : !l.EsCargo ? new Error("Remesas.NoEsCargo", "Un recibo devuelto es un cargo del banco y este movimiento es un abono.", l.Rotulo, null, "Si es una remesa, usa «Explotar remesa…».")
            : l.EjercicioCerrado ? new Error("Remesas.EjercicioCerrado", $"El ejercicio {l.Ejercicio} está cerrado.", l.Rotulo, "Sus líneas solo se consultan.", null)
            : null;
    }

    /// <summary>
    /// Concilia la devolución con los recibos elegidos (líneas de recibo cobradas), todo o nada: la suma tiene que ser la del movimiento.
    /// Con un recibo, la línea va a la 430 de su cliente; con varios, se desglosa en la cuenta ficticia de recibos devueltos.
    /// </summary>
    public async Task<ResultadoRemesaN43> ConciliarDevolucionAsync(string idSociedad, int idLinea, IReadOnlyList<int> idsRecibos, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(idsRecibos);
        ArgumentNullException.ThrowIfNull(actor);
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return ResultadoRemesaN43.Fallo(NoExiste(idLinea));
        if (BloqueoDevolucion(l) is { } bloqueo) return ResultadoRemesaN43.Fallo(bloqueo);
        if (await _bd.EjecutarAsync(s => CobrosFacturasN43.DevueltasPorAsync(s, idLinea)) is { Count: > 0 })
            return ResultadoRemesaN43.Fallo(new Error("Remesas.YaConciliada", "Esta devolución ya está conciliada con su factura.", l.Rotulo, null, "Si hay que repetirla, usa antes «Deshacer explosión»."));
        if (idsRecibos.Count == 0 || idsRecibos.Distinct().Count() != idsRecibos.Count)
            return ResultadoRemesaN43.Fallo(new Error("Remesas.SinRecibos", "Marca los recibos devueltos (cada uno una vez).", l.Rotulo, null, "Marca en la lista los recibos que suman el cargo del banco."));
        return await _bd.EnTransaccionAsync(async s =>
        {
            var recibos = new List<(ReciboCobrado Recibo, int Identidad)>();
            foreach (var id in idsRecibos)
            {
                var r = await ReciboAsync(s, idSociedad, id);
                if (r is null)
                    return ResultadoRemesaN43.Fallo(new Error("Remesas.ReciboNoCobrado", $"El recibo {id} ya no está cobrado (o se ha devuelto ya).", l.Rotulo,
                        "Se ha deshecho su remesa o se ha conciliado otra devolución mientras tenías el diálogo abierto.", "Vuelve a abrir «Conciliar facturas devueltas…»."));
                if (await IdentidadClienteAsync(s, idSociedad, l.Ejercicio, r.Factura.IdCliente) is not int identidad)
                    return ResultadoRemesaN43.Fallo(SinCuentaCliente(l, r.Factura));
                recibos.Add((r, identidad));
            }
            var suma = recibos.Sum(r => r.Recibo.Importe);
            if (suma != l.Importe)
                return ResultadoRemesaN43.Fallo(new Error("Remesas.NoCuadra", "Los recibos marcados no suman el cargo del banco. " +
                    ReglasRemesasN43.TextoCuadre(l.Importe, recibos.Select(r => r.Recibo.Importe).ToList()), l.Rotulo,
                    "Cada recibo devuelto tiene que ser uno de los que cobró la remesa, por el importe que cobró.", "Marca o desmarca recibos hasta que la diferencia sea cero."));

            if (recibos.Count == 1)
            {
                var (r, identidad) = recibos[0];
                if (l.IdAsiento is not null)
                    return ResultadoRemesaN43.Fallo(new Error("Remesas.Contabilizada", "La devolución ya está contabilizada con otra cuenta.", l.Rotulo,
                        "Con un solo recibo, el movimiento va a la cuenta del cliente.", "Deshaz antes su asiento («Deshacer contabilización») y vuelve a conciliarla."));
                await DescartarImputacionesAsync(s, l.Id);
                await ImputarAsync(s, l.Id, identidad, l.Sentido, l.Importe, OrigenDevolucion, ReglasRemesasN43.ConceptoRecibo(r.Factura, devolucion: true));
                if (!await CobrosFacturasN43.RegistrarDevolucionAsync(s, r.Factura.Id, r.IdLineaRecibo, l.Id))
                    throw new InvalidOperationException($"El recibo de la factura {r.Factura.Numero} dejó de estar cobrado durante la conciliación.");
                return new ResultadoRemesaN43(null, [], $"{l.Rotulo} conciliado con la factura {r.Factura.Numero}: va a la cuenta del cliente y la factura queda pendiente de cobro.", null);
            }

            var ficticia = await CuentaFicticiaAsync(s, idSociedad, l, CuentaDevueltos);
            if (ficticia.Error is { } ef) return ResultadoRemesaN43.Fallo(ef);
            var (idExtracto, ids) = await EscribirDesgloseAsync(s, idSociedad, l, ficticia.Id, devolucion: true,
                recibos.Select(r => (r.Recibo.Factura, r.Recibo.Importe, r.Identidad)).ToList(), actor);
            for (var i = 0; i < ids.Count; i++)
                if (!await CobrosFacturasN43.RegistrarDevolucionAsync(s, recibos[i].Recibo.Factura.Id, recibos[i].Recibo.IdLineaRecibo, ids[i]))
                    throw new InvalidOperationException($"El recibo de la factura {recibos[i].Recibo.Factura.Numero} dejó de estar cobrado durante la conciliación.");
            await ImputarOrigenSiFaltaAsync(s, l, ficticia.Identidad, OrigenDevolucion);
            return new ResultadoRemesaN43(idExtracto, ids,
                $"{l.Rotulo} conciliado con {ids.Count} facturas devueltas, desglosado en «{ficticia.Nombre}»; las facturas quedan pendientes de cobro.", null);
        });
    }

    // ---------- deshacer ----------

    /// <summary>
    /// Deshace la explosión de una remesa o la conciliación de una devolución: revierte el extracto ficticio (con fecha, operador y
    /// motivo), descarta las cuentas de sus recibos y libera las facturas. Se bloquea si algún recibo tiene asiento, si un recibo de la
    /// remesa se devolvió después (hay que deshacer antes esa devolución) o si una factura devuelta se volvió a cobrar.
    /// </summary>
    public async Task<ResultadoRemesaN43> DeshacerAsync(string idSociedad, int idLinea, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return ResultadoRemesaN43.Fallo(NoExiste(idLinea));
        if (l.EjercicioCerrado) return ResultadoRemesaN43.Fallo(new Error("Remesas.EjercicioCerrado", $"El ejercicio {l.Ejercicio} está cerrado.", l.Rotulo, "Sus líneas solo se consultan.", null));
        return await _bd.EnTransaccionAsync(async s =>
        {
            if (l.IdExtractoDesglose is int idExtracto)
            {
                var hijas = await s.ConsultarAsync("SELECT IdLineaExtractoBancario FROM LineasExtractoBancario WHERE IdExtractoBancario = ?", f => f.Entero("IdLineaExtractoBancario"), idExtracto);
                var lista = ConsultaLineasN43.Lista(hijas.DefaultIfEmpty(-1));
                var contabilizadas = Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM ContabilizacionesLineaExtracto WHERE IdLineaExtractoBancario IN ({lista})"), CultureInfo.InvariantCulture);
                if (contabilizadas > 0)
                    return ResultadoRemesaN43.Fallo(new Error("Remesas.RecibosContabilizados", $"{contabilizadas} recibo(s) del desglose ya tienen asiento.", l.Rotulo,
                        "Deshacer dejaría asientos sin el movimiento que los justifica.", "Filtra por esta remesa, deshaz los asientos de sus recibos y vuelve a deshacer la explosión."));
                // Primero se comprueba que cada enlace se puede quitar; después se quitan.
                var cobros = new List<(int Factura, int Linea)>();
                var devoluciones = new List<(int Factura, int Linea)>();
                foreach (var h in hijas)
                {
                    foreach (var f in await CobrosFacturasN43.CobradasPorAsync(s, h))
                    {
                        var u = await CobrosFacturasN43.UltimoAsync(s, f);
                        if (u is null || u.LineaCobro != h || u.Devuelto)
                            return ResultadoRemesaN43.Fallo(new Error("Remesas.ReciboDevuelto", $"Un recibo de esta remesa (factura {await NumeroAsync(s, f)}) se devolvió o se volvió a cobrar después.",
                                l.Rotulo, "Si se deshace la remesa, esa devolución se quedaría sin el cobro que devuelve.", "Deshaz antes la conciliación de esa devolución y vuelve a intentarlo."));
                        cobros.Add((f, h));
                    }
                    foreach (var f in await CobrosFacturasN43.DevueltasPorAsync(s, h))
                    {
                        var u = await CobrosFacturasN43.UltimoAsync(s, f);
                        if (u is null || u.LineaDevolucion != h)
                            return ResultadoRemesaN43.Fallo(new Error("Remesas.DevueltaRecobrada", $"La factura {await NumeroAsync(s, f)} devuelta aquí se volvió a cobrar después.", l.Rotulo,
                                "Si se deshace la devolución, ese cobro posterior se quedaría sin la devolución anterior.", "Deshaz antes ese cobro y vuelve a intentarlo."));
                        devoluciones.Add((f, h));
                    }
                }
                foreach (var (f, h) in cobros) await CobrosFacturasN43.QuitarCobroAsync(s, f, h);
                foreach (var (f, h) in devoluciones) await CobrosFacturasN43.QuitarDevolucionAsync(s, f, h);
                await s.EjecutarAsync($"UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE Estado <> 'DESCARTADA' AND IdLineaExtractoBancario IN ({lista})");
                var idLote = Convert.ToInt32(await s.EscalarAsync("SELECT IdLoteImportacionBancaria FROM ExtractosBancarios WHERE IdExtractoBancario = ?", idExtracto), CultureInfo.InvariantCulture);
                await s.EjecutarAsync("UPDATE LotesImportacionBancaria SET Estado = ?, FechaHoraReversion = ?, OperadorReversion = ?, MotivoReversion = ? WHERE IdLoteImportacionBancaria = ?",
                    ServicioImportacionN43.EstadoRevertido, SinMilisegundos(_reloj.Ahora), Recortar(actor.ToString(), 100), $"Deshecha la explosión de la línea {l.Id} ({l.Rotulo}).", idLote);
                if (await s.EscalarAsync("SELECT Formato FROM LotesImportacionBancaria WHERE IdLoteImportacionBancaria = ?", idLote) is string formatoLote && formatoLote == FormatoDividendos)
                {
                    if (l.IdAsiento is null)
                        await s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA' AND Origen = ?",
                            l.Id, OrigenDividendos);
                    return new ResultadoRemesaN43(idExtracto, hijas,
                        $"Deshecho el pago de dividendos: {l.Rotulo} vuelve a estar sin desglosar y sin cuenta; ponle la suya con «Reasignar por palabras clave», «Asignar cuenta…» o «Pago de dividendos…».", null);
                }
                return new ResultadoRemesaN43(idExtracto, hijas, $"Deshecha: {l.Rotulo} vuelve a estar sin explotar y sus {cobros.Count + devoluciones.Count} factura(s) quedan como estaban.", null);
            }

            var devueltas = await CobrosFacturasN43.DevueltasPorAsync(s, l.Id);
            if (devueltas.Count == 0)
                return ResultadoRemesaN43.Fallo(new Error("Remesas.NoExplotada", "Este movimiento no está explotado ni conciliado con recibos devueltos.", l.Rotulo, null, "No hay nada que deshacer."));
            if (l.IdAsiento is not null)
                return ResultadoRemesaN43.Fallo(new Error("Remesas.Contabilizada", "La devolución ya está contabilizada.", l.Rotulo, "Su asiento lleva la cuenta del cliente.",
                    "Deshaz antes su asiento («Deshacer contabilización») y vuelve a intentarlo."));
            foreach (var f in devueltas)
                if (!await CobrosFacturasN43.QuitarDevolucionAsync(s, f, l.Id))
                    return ResultadoRemesaN43.Fallo(new Error("Remesas.DevueltaRecobrada", $"La factura {await NumeroAsync(s, f)} se volvió a cobrar después de esta devolución.", l.Rotulo,
                        null, "Deshaz antes ese cobro y vuelve a intentarlo."));
            await s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA' AND Origen = ?", l.Id, OrigenDevolucion);
            return new ResultadoRemesaN43(null, [], $"Deshecha: {l.Rotulo} queda sin cuenta; ponle la suya con «Reasignar por palabras clave» o «Asignar cuenta…».", null);
        });
    }

    // ---------- lecturas ----------

    private async Task<LineaN43?> LineaAsync(string idSociedad, int idLinea) =>
        (await _consulta.LineasAsync(idSociedad, null, [idLinea])).FirstOrDefault();

    private static Error NoExiste(int id) => new("Lineas.NoExiste", $"No encuentro el movimiento {id}.", $"Movimiento {id}", "Se ha revertido su extracto o es de otra sociedad.", "Actualiza la lista (F5).");

    private static Error SinCuentaCliente(LineaN43 l, FacturaRemesa f) => new("Remesas.SinCuentaCliente",
        $"El cliente de la factura {f.Numero} ({f.Tercero ?? "sin cliente"}) no tiene cuenta 430 en el ejercicio {l.Ejercicio}.", l.Rotulo,
        "Cada recibo va a la cuenta del cliente que lo paga.", $"Dale cuenta de cliente en su ficha (o créala en el ejercicio {l.Ejercicio}) y vuelve a intentarlo.");

    /// <summary>Por qué no valen los recibos (null si valen): sin repetir factura, con importes positivos y cuadrando al céntimo.</summary>
    internal static Error? ErrorRecibos(LineaN43 l, IReadOnlyList<(int IdFactura, decimal Importe)> recibos)
    {
        if (recibos.Count == 0)
            return new Error("Remesas.SinRecibos", "Marca las facturas que van en la remesa.", l.Rotulo, null, "Marca en la lista las facturas cuyos recibos cobra el banco.");
        if (recibos.Select(r => r.IdFactura).Distinct().Count() != recibos.Count)
            return new Error("Remesas.FacturaRepetida", "Una factura está dos veces en la remesa.", l.Rotulo, null, "Deja cada factura una sola vez.");
        if (recibos.Any(r => r.Importe <= 0m || decimal.Round(r.Importe, 2) != r.Importe))
            return new Error("Remesas.ImporteNoValido", "Algún importe cobrado es cero, negativo o tiene más de dos decimales.", l.Rotulo, null, "Corrige el importe cobrado de esa factura.");
        if (recibos.Sum(r => r.Importe) != l.Importe)
            return new Error("Remesas.NoCuadra", "Los recibos no suman el abono del banco. " + ReglasRemesasN43.TextoCuadre(l.Importe, recibos.Select(r => r.Importe).ToList()), l.Rotulo,
                "La remesa solo se explota si los recibos suman exactamente lo que cobró el banco.",
                "Marca o desmarca facturas, o cambia el importe cobrado de las que el banco cobró por otro importe, hasta que la diferencia sea cero.");
        return null;
    }

    private const string ColumnasFactura =
        "f.IdFactura, s.Prefijo, f.NumeroFactura, f.FechaFactura, f.ImporteTotal, f.DiaDePago, f.FormaDePago, fp.SEPA, f.IdCliente, f.Nombre, f.RazonSocial, f.Descripcion";

    private const string DesdeFactura =
        "((Facturas AS f INNER JOIN SociedadesEjercicios AS se ON f.IdSociedadEjercicio = se.IdSociedadEjercicio) LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie) " +
        "LEFT JOIN FormasPago AS fp ON f.FormaDePago = fp.Id";

    /// <summary>Facturas de venta con número: validadas o pendientes de rectificar (los borradores y las rectificadas no se cobran por remesa).</summary>
    private const string ConNumero = "f.NumeroFactura IS NOT NULL AND f.EstadoFactura IN (1, 4) AND f.ImporteTotal > 0";

    private static FacturaRemesa Factura(IFilaDatos f) => new(f.Entero("IdFactura"),
        (ConsultaLineasN43.Limpio(f.Texto("Prefijo")) ?? "") + (ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? ""), f.Fecha("FechaFactura").Date, f.Decimal("ImporteTotal"),
        f.EnteroNulo("DiaDePago"), !f.EsNulo("SEPA") && f.Logico("SEPA"), f.EnteroNulo("IdCliente"),
        ConsultaLineasN43.Limpio(f.Texto("RazonSocial")) ?? ConsultaLineasN43.Limpio(f.Texto("Nombre")), ConsultaLineasN43.Limpio(f.Texto("Descripcion")));

    private static async Task<IReadOnlyList<FacturaRemesa>> FacturasDelMesAsync(ISesionDatos s, string idSociedad, DateTime fecha)
    {
        var desde = new DateTime(fecha.Year, fecha.Month, 1);
        var todas = await s.ConsultarAsync($"SELECT {ColumnasFactura} FROM {DesdeFactura} WHERE se.IdSociedad = ? AND {ConNumero} AND f.FechaFactura >= ? AND f.FechaFactura < ?",
            Factura, idSociedad, desde, desde.AddMonths(1));
        var pendientes = await CobrosFacturasN43.PendientesAsync(s, todas.Select(f => f.Id).ToList());
        return todas.Where(f => pendientes.Contains(f.Id)).ToList();
    }

    private static Task<FacturaRemesa?> FacturaAsync(ISesionDatos s, string idSociedad, int id) =>
        s.ConsultarPrimeroAsync($"SELECT {ColumnasFactura} FROM {DesdeFactura} WHERE se.IdSociedad = ? AND {ConNumero} AND f.IdFactura = ?", f => (FacturaRemesa?)Factura(f), idSociedad, id);

    private static async Task<string> NumeroAsync(ISesionDatos s, int idFactura) =>
        await s.ConsultarPrimeroAsync("SELECT s.Prefijo, f.NumeroFactura FROM Facturas AS f LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie WHERE f.IdFactura = ?",
            f => (ConsultaLineasN43.Limpio(f.Texto("Prefijo")) ?? "") + (ConsultaLineasN43.Limpio(f.Texto("NumeroFactura")) ?? ""), idFactura) ?? idFactura.ToString(CultureInfo.InvariantCulture);

    /// <summary>Remesas explotadas (abonos con extracto ficticio no revertido) de la misma cuenta bancaria y no posteriores a la devolución, de la más reciente a la más antigua.</summary>
    private static async Task<IReadOnlyList<RemesaAnteriorN43>> RemesasAnterioresAsync(ISesionDatos s, string idSociedad, LineaN43 l)
    {
        var filas = await s.ConsultarAsync(
            "SELECT TOP " + RemesasAnteriores.ToString(CultureInfo.InvariantCulture) + " d.IdExtractoBancario, o.IdLineaExtractoBancario, o.FechaOperacion, o.Importe, d.NumeroApuntesHaberCalculado " +
            "FROM (((ExtractosBancarios AS d INNER JOIN LotesImportacionBancaria AS lo ON d.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria) " +
            "INNER JOIN LineasExtractoBancario AS o ON d.IdLineaExtractoOrigen = o.IdLineaExtractoBancario) " +
            "INNER JOIN ExtractosBancarios AS eo ON o.IdExtractoBancario = eo.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS loo ON eo.IdLoteImportacionBancaria = loo.IdLoteImportacionBancaria " +
            "WHERE lo.IdSociedad = ? AND lo.Estado <> ? AND loo.Estado <> ? AND o.Sentido = ? AND eo.IdCuentaBancaria = ? AND o.FechaOperacion <= ? " +
            "ORDER BY o.FechaOperacion DESC, d.IdExtractoBancario DESC",
            f => new RemesaAnteriorN43(f.Entero("IdExtractoBancario"), f.Entero("IdLineaExtractoBancario"), f.Fecha("FechaOperacion").Date, f.Decimal("Importe"), f.Entero("NumeroApuntesHaberCalculado")),
            idSociedad, ServicioImportacionN43.EstadoRevertido, ServicioImportacionN43.EstadoRevertido, ServicioConciliacionN43.Abono, l.IdCuentaBancaria ?? 0, l.FechaOperacion);
        return filas;
    }

    /// <summary>Recibos del extracto ficticio cuya factura sigue cobrada por ellos (último intento, sin devolver).</summary>
    private static async Task<IReadOnlyList<ReciboCobrado>> RecibosCobradosAsync(ISesionDatos s, string idSociedad, int idExtracto)
    {
        var lineas = await s.ConsultarAsync("SELECT IdLineaExtractoBancario FROM LineasExtractoBancario WHERE IdExtractoBancario = ? ORDER BY OrdinalMovimiento",
            f => f.Entero("IdLineaExtractoBancario"), idExtracto);
        var r = new List<ReciboCobrado>();
        foreach (var id in lineas)
            if (await ReciboAsync(s, idSociedad, id) is { } recibo) r.Add(recibo);
        return r;
    }

    /// <summary>El recibo de la línea si cobra una factura de la sociedad y ese cobro es el último y no se ha devuelto; null si no.</summary>
    private static async Task<ReciboCobrado?> ReciboAsync(ISesionDatos s, string idSociedad, int idLinea)
    {
        var linea = await s.ConsultarPrimeroAsync(
            "SELECT l.FechaOperacion, l.Importe, lo.IdSociedad, lo.Estado FROM (LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria WHERE l.IdLineaExtractoBancario = ?",
            f => (Fecha: f.Fecha("FechaOperacion").Date, Importe: f.Decimal("Importe"), Sociedad: f.Texto("IdSociedad"), Estado: f.Texto("Estado")), idLinea);
        if (linea.Sociedad is null || !string.Equals(linea.Sociedad, idSociedad, StringComparison.OrdinalIgnoreCase) || linea.Estado == ServicioImportacionN43.EstadoRevertido) return null;
        foreach (var idFactura in await CobrosFacturasN43.CobradasPorAsync(s, idLinea))
        {
            var u = await CobrosFacturasN43.UltimoAsync(s, idFactura);
            if (u is null || u.LineaCobro != idLinea || u.Devuelto) continue;
            var f = await s.ConsultarPrimeroAsync($"SELECT {ColumnasFactura} FROM {DesdeFactura} WHERE se.IdSociedad = ? AND f.IdFactura = ?", x => (FacturaRemesa?)Factura(x), idSociedad, idFactura);
            if (f is not null) return new ReciboCobrado(idLinea, linea.Fecha, linea.Importe, f);
        }
        return null;
    }

    /// <summary>Identidad contable de la cuenta de cliente (CuentasClientesSociedad, clase CLIENTE) que existe en el ejercicio; null si no.</summary>
    private static async Task<int?> IdentidadClienteAsync(ISesionDatos s, string idSociedad, int ejercicio, int? idCliente)
    {
        if (idCliente is not int c) return null;
        var idEjercicio = (await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdSociedadEjercicio"), Anio: ConsultaLineasN43.Anio(f.Texto("Ejercicio"))), idSociedad))
            .FirstOrDefault(e => e.Anio == ejercicio).Id;
        if (idEjercicio == 0) return null;
        var candidatas = await s.ConsultarAsync(
            "SELECT IdIdentidadContable FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = ? AND Clase = 'CLIENTE' AND IdIdentidadContable IS NOT NULL",
            f => f.Entero("IdIdentidadContable"), c, idSociedad);
        foreach (var identidad in candidatas)
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?", idEjercicio, identidad), CultureInfo.InvariantCulture) > 0)
                return identidad;
        return null;
    }

    /// <summary>
    /// Cuenta bancaria ficticia del banco de la línea (misma entidad, cuenta <paramref name="cuenta"/>) con su cuenta contable vigente en la
    /// fecha del movimiento; error accionable si no está dada de alta o no tiene cuenta contable.
    /// </summary>
    private static async Task<(int Id, int Identidad, string Nombre, Error? Error)> CuentaFicticiaAsync(ISesionDatos s, string idSociedad, LineaN43 l, string cuenta)
    {
        var que = cuenta == CuentaRemesas ? "remesas" : "recibos devueltos";
        var banco = await s.ConsultarPrimeroAsync("SELECT EntidadOriginal, Oficina, Nombre FROM CuentasBancarias WHERE IdCuentaBancaria = ?",
            f => (Entidad: (f.Texto("EntidadOriginal") ?? "").Trim(), Oficina: (f.Texto("Oficina") ?? "").Trim(), Nombre: ConsultaLineasN43.Limpio(f.Texto("Nombre"))), l.IdCuentaBancaria ?? 0);
        var entidad = banco.Entidad is { Length: > 0 } en ? en : l.Entidad;
        var ficticia = await s.ConsultarPrimeroAsync(
            "SELECT IdCuentaBancaria, Nombre FROM CuentasBancarias WHERE IdSociedad = ? AND EntidadOriginal = ? AND Cuenta = ? ORDER BY IdCuentaBancaria",
            f => (Id: f.Entero("IdCuentaBancaria"), Nombre: ConsultaLineasN43.Limpio(f.Texto("Nombre"))), idSociedad, entidad, cuenta);
        if (ficticia.Id == 0)
            return (0, 0, "", new Error("Remesas.SinCuentaFicticia", $"El banco {entidad} no tiene la cuenta bancaria ficticia de {que} (cuenta {cuenta}).", l.Rotulo,
                $"Los recibos de la {(cuenta == CuentaRemesas ? "remesa" : "devolución")} se desglosan en esa cuenta, como hacía el ERP.",
                $"Dala de alta en Bancos: entidad {entidad}, oficina {banco.Oficina}, cuenta {cuenta}, nombre «{banco.Nombre ?? entidad} {que.ToUpperInvariant()}» y su cuenta 555, y vuelve a intentarlo."));
        var fecha = l.FechaOperacion;
        var cfg = await s.ConsultarAsync("SELECT IdIdentidadContableBanco, FechaDesde, FechaHasta FROM ConfiguracionesContablesCuentaBancaria WHERE IdCuentaBancaria = ? ORDER BY FechaDesde DESC",
            f => (Id: f.EnteroNulo("IdIdentidadContableBanco"), Desde: f.FechaNula("FechaDesde"), Hasta: f.FechaNula("FechaHasta")), ficticia.Id);
        var vigente = cfg.FirstOrDefault(x => x.Id is not null && (x.Desde is null || x.Desde.Value.Date <= fecha) && (x.Hasta is null || x.Hasta.Value.Date >= fecha));
        if (vigente.Id is not int identidad)
            return (0, 0, "", new Error("Remesas.CuentaFicticiaSinContable", $"La cuenta bancaria «{ficticia.Nombre}» no tiene cuenta contable el {fecha:dd/MM/yyyy}.", l.Rotulo,
                "Sus movimientos se contabilizan contra esa cuenta (la 555 de " + que + ").", "Ponle su cuenta contable en Bancos y vuelve a intentarlo."));
        return (ficticia.Id, identidad, ficticia.Nombre ?? cuenta, null);
    }

    // ---------- escrituras ----------

    /// <summary>Lote, extracto ficticio (con la línea de origen) y una línea por recibo, cada una llevada a la cuenta de su cliente.</summary>
    private Task<(int IdExtracto, IReadOnlyList<int> Lineas)> EscribirDesgloseAsync(ISesionDatos s, string idSociedad, LineaN43 l, int idCuenta, bool devolucion,
        IReadOnlyList<(FacturaRemesa Factura, decimal Importe, int Identidad)> recibos, Actor actor)
    {
        var que = devolucion ? "Devolución" : "Remesa";
        _ = actor;
        return EscribirExtractoAsync(s, idSociedad, l, idCuenta, devolucion, Formato,
            $"{que} del {l.FechaOperacion:dd/MM/yyyy} · {l.Importe.ToString("N2", Es)} € · {recibos.Count} recibo(s) (línea {l.Id})", $"({que} de la línea {l.Id})",
            devolucion ? OrigenDevolucion : OrigenRemesa,
            recibos.Select(r => new LineaDesglose(Recortar(r.Factura.Numero, 10), ReglasRemesasN43.ConceptoRecibo(r.Factura, devolucion),
                $"(Recibo de la factura {r.Factura.Numero}, {que.ToLowerInvariant()} de la línea {l.Id})", r.Importe, r.Identidad)).ToList());
    }

    /// <summary>Una línea de un extracto ficticio: documento, concepto, texto original, importe y la cuenta (identidad) a la que va.</summary>
    private sealed record LineaDesglose(string? Documento, string Concepto, string TextoOriginal, decimal Importe, int Identidad);

    /// <summary>
    /// Lote (con su <paramref name="formato"/>), extracto ficticio en la cuenta <paramref name="idCuenta"/> enlazado con la línea de origen y una línea por
    /// desglose, cada una llevada a su cuenta. <paramref name="cargo"/>: las líneas son cargos (devoluciones, pagos de dividendos) o abonos (remesas).
    /// </summary>
    private async Task<(int IdExtracto, IReadOnlyList<int> Lineas)> EscribirExtractoAsync(ISesionDatos s, string idSociedad, LineaN43 l, int idCuenta, bool cargo, string formato,
        string nombreLote, string cabecera, string origen, IReadOnlyList<LineaDesglose> lineas)
    {
        var ahora = SinMilisegundos(_reloj.Ahora);
        var cuenta = await s.ConsultarPrimeroAsync("SELECT EntidadOriginal, Oficina, Cuenta, Divisa FROM CuentasBancarias WHERE IdCuentaBancaria = ?",
            f => (Entidad: (f.Texto("EntidadOriginal") ?? "").Trim(), Oficina: (f.Texto("Oficina") ?? "").Trim(), Cuenta: (f.Texto("Cuenta") ?? "").Trim(), Divisa: (f.Texto("Divisa") ?? "EUR").Trim()), idCuenta);
        var nombre = Recortar(nombreLote, 255);
        var huella = ServicioImportacionN43.Huella(Encoding.UTF8.GetBytes($"{formato}|{idSociedad}|{l.Id}|{ahora:O}|{Guid.NewGuid():N}"));
        await s.EjecutarAsync(
            "INSERT INTO LotesImportacionBancaria (IdSociedad, Formato, VersionEsquema, NombreFichero, RutaOrigen, HashSha256Contenido, TamanoBytes, Codificacion, Estado, " +
            "FechaHoraInicio, FechaHoraFin, NumeroRegistrosDeclarado, NumeroRegistrosLeido, NumeroExtractos, NumeroMovimientos, NumeroErrores, NumeroAdvertencias) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idSociedad, formato, "PROCONTAVF", nombre, null, huella, 0, "N/A", ServicioImportacionN43.EstadoImportado, ahora, ahora, 0, 0, 1, lineas.Count, 0, 0);
        var idLote = await s.UltimoIdAsync();
        var total = lineas.Sum(r => r.Importe);
        var n = lineas.Count;
        var devolucion = cargo;
        await s.EjecutarAsync(
            "INSERT INTO ExtractosBancarios (IdLoteImportacionBancaria, OrdinalExtracto, IdCuentaBancaria, Entidad, Oficina, Cuenta, Divisa, FechaInicio, FechaFin, SentidoSaldoInicial, " +
            "SaldoInicial, Modalidad, Nombre, NumeroApuntesDebeDeclarado, TotalDebeDeclarado, NumeroApuntesHaberDeclarado, TotalHaberDeclarado, NumeroApuntesDebeCalculado, " +
            "TotalDebeCalculado, NumeroApuntesHaberCalculado, TotalHaberCalculado, CodigoSeguridad, SaldoFinal, EstadoCuadre, TextoCabeceraOriginal, TextoCierreOriginal, IdLineaExtractoOrigen) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idLote, 1, idCuenta, Recortar(cuenta.Entidad, 4), Recortar(cuenta.Oficina, 4), Recortar(cuenta.Cuenta, 10), Recortar(cuenta.Divisa, 3), l.FechaOperacion, l.FechaOperacion,
            ServicioConciliacionN43.Abono, 0m, null, Recortar(nombre, 150),
            devolucion ? n : 0, devolucion ? total : 0m, devolucion ? 0 : n, devolucion ? 0m : total,
            devolucion ? n : 0, devolucion ? total : 0m, devolucion ? 0 : n, devolucion ? 0m : total, null, devolucion ? -total : total,
            // Descuadrado a propósito: su saldo final no es el de una cuenta (pantalla-de-inicio no lo compara), como los del ERP.
            "DESCUADRADO", Recortar(cabecera, 80), Recortar(cabecera, 80), l.Id);
        var idExtracto = await s.UltimoIdAsync();
        var sentido = ReglasRemesasN43.Sentido(devolucion);
        var ids = new List<int>(n);
        for (var i = 0; i < n; i++)
        {
            var d = lineas[i];
            await s.EjecutarAsync(
                "INSERT INTO LineasExtractoBancario (IdExtractoBancario, OrdinalMovimiento, NumeroLineaFisica, OficinaOrigen, FechaOperacion, FechaValor, ConceptoComun, ConceptoPropio, " +
                "Sentido, Importe, Documento, Referencia1, Referencia2, ConceptoCompleto, EntidadOficinaDestino, TextoOriginal) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                idExtracto, i + 1, i + 1, null, l.FechaOperacion, l.FechaValor, l.ConceptoComun, null, sentido, d.Importe, d.Documento,
                l.Referencia1 is null ? null : Recortar(l.Referencia1, 12), l.Referencia2 is null ? null : Recortar(l.Referencia2, 16), d.Concepto, null,
                Recortar(d.TextoOriginal, 80));
            var id = await s.UltimoIdAsync();
            await ImputarAsync(s, id, d.Identidad, sentido, d.Importe, origen, d.Concepto);
            ids.Add(id);
        }
        return (idExtracto, ids);
    }

    private static async Task ImputarAsync(ISesionDatos s, int idLinea, int identidad, string sentido, decimal importe, string origen, string? concepto) =>
        await s.EjecutarAsync(
            "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Confianza, Estado, Concepto, IdCentroCoste, IdPalabraClaveBancaria) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idLinea, identidad, AsignadorCuentasN43.Rol, sentido == ServicioConciliacionN43.Cargo ? importe : -importe, origen, null, AsignadorCuentasN43.Estado, concepto, null, null);

    private static Task DescartarImputacionesAsync(ISesionDatos s, int idLinea) =>
        s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", idLinea);

    /// <summary>Si la línea del banco no tiene cuenta y no está contabilizada, la lleva a la cuenta contable de la cuenta ficticia (la 555).</summary>
    private static async Task ImputarOrigenSiFaltaAsync(ISesionDatos s, LineaN43 l, int identidad, string origen)
    {
        if (l.Destinos.Count > 0 || l.IdAsiento is not null) return;
        await ImputarAsync(s, l.Id, identidad, l.Sentido, l.Importe, origen, l.Concepto);
    }

    private static string Recortar(string t, int largo) => t.Length <= largo ? t : t[..largo];
    private static DateTime SinMilisegundos(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Kind);
}
