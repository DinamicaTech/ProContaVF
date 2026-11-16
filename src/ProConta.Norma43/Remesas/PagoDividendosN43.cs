using ProConta.Contabilidad.Dividendos;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Remesas;

/// <summary>Un socio en el diálogo «Pago de dividendos»: su %, su 526 en el ejercicio de la línea y el importe propuesto.</summary>
/// <param name="Error">Por qué no puede cobrar desde aquí (sin 526 en el ejercicio); null si puede.</param>
public sealed record SocioPagoDividendos(int IdSocio, string Nombre, decimal Participacion, int? IdIdentidad526, string? Cuenta526, decimal ImportePropuesto, string? Error);

/// <summary>Propuesta para pagar dividendos con un cargo: la línea, los socios activos y, si no se puede, por qué.</summary>
public sealed record PropuestaPagoDividendosN43(LineaN43 Linea, IReadOnlyList<SocioPagoDividendos> Socios, Error? Bloqueo);

/// <summary>Importe que se paga a un socio.</summary>
public sealed record PagoSocio(int IdSocio, decimal Importe);

/// <summary>
/// «Pago de dividendos…» de Líneas N43 (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55; norma43/remesas y norma43/pantallas): un cargo
/// del banco que paga dividendos a uno o varios socios. Con un socio, la línea va a su 526 con el concepto «Pago de dividendos &lt;socio&gt;». Con varios,
/// se desglosa como una remesa: extracto ficticio en la cuenta bancaria de remesas del mismo banco (formato DIVIDENDOS), la línea original a la 555 de
/// esa cuenta y una línea por socio con su importe, su 526 y la descripción «Pago de dividendos &lt;socio&gt;». «Deshacer explosión» lo deshace.
/// </summary>
public sealed partial class ServicioRemesasN43
{
    /// <summary>Formato del lote del extracto ficticio de un pago de dividendos.</summary>
    public const string FormatoDividendos = "DIVIDENDOS";
    /// <summary>Origen de las cuentas que pone el pago de dividendos.</summary>
    public const string OrigenDividendos = "DIVIDENDOS";

    public static string ConceptoPagoDividendos(string socio) => $"Pago de dividendos {socio}".Trim();

    /// <summary>Por qué no se puede pagar dividendos con la línea (null si se puede): un solo cargo, sin contabilizar ni explotar.</summary>
    public static Error? BloqueoPagoDividendos(LineaN43 l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return l.EsRecibo ? new Error("Dividendos.EsRecibo", "Este movimiento ya es una línea de un desglose.", l.Rotulo, null, "Elige la línea del banco.")
            : l.Explotada ? new Error("Dividendos.YaExplotada", "Este movimiento ya está desglosado.", l.Rotulo, null, "Si hay que repetirlo, usa antes «Deshacer explosión».")
            : !l.EsCargo ? new Error("Dividendos.NoEsCargo", "Un pago de dividendos es un cargo del banco y este movimiento es un abono.", l.Rotulo, null, "Elige el cargo con que se pagaron.")
            : l.IdAsiento is not null ? new Error("Dividendos.Contabilizada", "El movimiento ya está contabilizado.", l.Rotulo, "El pago cambia su cuenta.",
                "Deshaz antes su asiento («Deshacer el asiento…») si lo generó ProContaVF, y vuelve a intentarlo.")
            : l.EjercicioCerrado ? new Error("Dividendos.EjercicioCerrado", $"El ejercicio {l.Ejercicio} está cerrado.", l.Rotulo, "Sus líneas solo se consultan.", null)
            : null;
    }

    /// <summary>Socios activos de la sociedad con el cargo repartido entre todos según su % (resto mayor al céntimo), o por qué no se puede.</summary>
    public async Task<PropuestaPagoDividendosN43?> ProponerPagoDividendosAsync(string idSociedad, int idLinea)
    {
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return null;
        if (BloqueoPagoDividendos(l) is { } e) return new PropuestaPagoDividendosN43(l, [], e);
        var socios = await _bd.EjecutarAsync(s => SociosAsync(s, idSociedad, l.Ejercicio));
        if (socios.Count == 0)
            return new PropuestaPagoDividendosN43(l, [], new Error("Dividendos.SinSocios", "La sociedad no tiene socios activos.", l.Rotulo,
                "El pago se reparte entre los socios.", "Dales el rol Socio en su ficha de Terceros (pestaña Roles), con su participación, y vuelve a intentarlo."));
        var partes = CalculoDividendos.Repartir(l.Importe, socios.Select(s => s.Participacion).ToList());
        return new PropuestaPagoDividendosN43(l, socios.Select((s, i) => s with { ImportePropuesto = partes[i] }).ToList(), null);
    }

    /// <summary>
    /// Paga los dividendos con el cargo, todo o nada: la suma de los importes tiene que ser la del movimiento al céntimo y cada socio activo, con su 526
    /// en el ejercicio de la línea.
    /// </summary>
    public async Task<ResultadoRemesaN43> PagarDividendosAsync(string idSociedad, int idLinea, IReadOnlyList<PagoSocio> pagos, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(pagos);
        ArgumentNullException.ThrowIfNull(actor);
        if (await LineaAsync(idSociedad, idLinea) is not { } l) return ResultadoRemesaN43.Fallo(NoExiste(idLinea));
        if (BloqueoPagoDividendos(l) is { } bloqueo) return ResultadoRemesaN43.Fallo(bloqueo);
        var validos = pagos.Where(p => p.Importe != 0m).ToList();
        if (validos.Count == 0)
            return ResultadoRemesaN43.Fallo(new Error("Dividendos.SinSocios", "Marca los socios que cobran.", l.Rotulo, null, "Marca en la lista a quien o quienes paga el banco."));
        if (validos.Select(p => p.IdSocio).Distinct().Count() != validos.Count)
            return ResultadoRemesaN43.Fallo(new Error("Dividendos.SocioRepetido", "Un socio está dos veces en el pago.", l.Rotulo, null, "Deja cada socio una sola vez."));
        if (validos.Any(p => p.Importe < 0m || decimal.Round(p.Importe, 2) != p.Importe))
            return ResultadoRemesaN43.Fallo(new Error("Dividendos.ImporteNoValido", "Algún importe es negativo o tiene más de dos decimales.", l.Rotulo, null, "Corrige el importe de ese socio."));
        if (validos.Sum(p => p.Importe) != l.Importe)
            return ResultadoRemesaN43.Fallo(new Error("Dividendos.NoCuadra", "Los importes no suman el cargo del banco. " + ReglasRemesasN43.TextoCuadre(l.Importe, validos.Select(p => p.Importe).ToList()),
                l.Rotulo, "El pago solo se graba si lo de los socios suma exactamente lo que pagó el banco.", "Cambia los importes o marca o desmarca socios hasta que la diferencia sea cero."));
        return await _bd.EnTransaccionAsync(async s =>
        {
            var socios = await SociosAsync(s, idSociedad, l.Ejercicio);
            var preparados = new List<(SocioPagoDividendos Socio, decimal Importe)>();
            foreach (var p in validos)
            {
                var so = socios.FirstOrDefault(x => x.IdSocio == p.IdSocio);
                if (so is null)
                    return ResultadoRemesaN43.Fallo(new Error("Dividendos.SocioNoActivo", "Uno de los socios ya no es socio activo de la sociedad.", l.Rotulo,
                        "Se le ha quitado el rol mientras tenías el diálogo abierto.", "Vuelve a abrir «Pago de dividendos…»."));
                if (so.Error is { } e)
                    return ResultadoRemesaN43.Fallo(new Error("Dividendos.SinCuenta526", e, l.Rotulo, "Cada pago va a la 526 del socio que lo cobra.", null));
                preparados.Add((so, p.Importe));
            }
            await s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", l.Id);
            if (preparados.Count == 1)
            {
                var (so, importe) = preparados[0];
                await ImputarAsync(s, l.Id, so.IdIdentidad526!.Value, l.Sentido, importe, OrigenDividendos, ConceptoPagoDividendos(so.Nombre));
                return new ResultadoRemesaN43(null, [], $"{l.Rotulo} va a la {so.Cuenta526} de {so.Nombre} como pago de dividendos.", null);
            }
            var ficticia = await CuentaFicticiaAsync(s, idSociedad, l, CuentaRemesas);
            if (ficticia.Error is { } ef) return ResultadoRemesaN43.Fallo(ef);
            var (idExtracto, ids) = await EscribirExtractoAsync(s, idSociedad, l, ficticia.Id, cargo: true, FormatoDividendos,
                $"Pago de dividendos del {l.FechaOperacion:dd/MM/yyyy} · {l.Importe.ToString("N2", Es)} € · {preparados.Count} socios (línea {l.Id})", $"(Dividendos de la línea {l.Id})",
                OrigenDividendos,
                preparados.Select(p => new LineaDesglose(null, ConceptoPagoDividendos(p.Socio.Nombre), $"(Pago de dividendos a {p.Socio.Nombre}, línea {l.Id})", p.Importe,
                    p.Socio.IdIdentidad526!.Value)).ToList());
            await ImputarAsync(s, l.Id, ficticia.Identidad, l.Sentido, l.Importe, OrigenDividendos, l.Concepto);
            return new ResultadoRemesaN43(idExtracto, ids,
                $"{l.Rotulo} desglosado en {ids.Count} pagos de dividendos en «{ficticia.Nombre}», cada uno a la 526 de su socio.", null);
        });
    }

    /// <summary>Socios activos de la sociedad con su 526 en el ejercicio <paramref name="anio"/> (o por qué no la tienen).</summary>
    private static async Task<IReadOnlyList<SocioPagoDividendos>> SociosAsync(ISesionDatos s, string idSociedad, int anio)
    {
        var idEjercicio = await ConsultaSocios.EjercicioAsync(s, idSociedad, anio);
        if (idEjercicio is not int ej) return [];
        return (await ConsultaSocios.ActivosAsync(s, idSociedad, ej)).Select(so =>
        {
            var error = so.Dividendos is null
                ? $"{so.Nombre} no tiene subcuenta 526: abre su ficha, pestaña Roles › Socio, y guárdala (se crea sola)."
                : !so.Dividendos.ExisteEnEjercicio
                    ? $"La 526 de {so.Nombre} ({so.Dividendos.Codigo}) aún no existe en el ejercicio {anio}: créala desde su ficha (Roles › Socio, «Crearla en {anio}»)."
                    : null;
            return new SocioPagoDividendos(so.IdSocio, so.Nombre, so.Participacion, error is null ? so.Dividendos!.IdIdentidadContable : null,
                so.Dividendos?.CodigoEnEjercicio, 0m, error);
        }).ToList();
    }

}
