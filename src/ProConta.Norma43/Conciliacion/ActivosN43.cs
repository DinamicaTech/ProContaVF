using System.Globalization;
using ProConta.Activos.Financieros;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Conciliacion;

/// <summary>Una compra o venta de activo financiero que se ofrece para una línea, con la diferencia entre su importe y el del banco.</summary>
public sealed record OperacionActivoN43(OperacionPorConciliar Operacion, decimal Diferencia);

/// <summary>Propuesta de «Compra de activo…» o «Venta de activo…» para una línea: las operaciones sin conciliar, la más parecida primero, o por qué no se puede.</summary>
public sealed record PropuestaActivoN43(LineaN43 Linea, bool EsCompra, IReadOnlyList<OperacionActivoN43> Operaciones, Error? Bloqueo);

/// <summary>Resultado de conciliar: el asiento generado y el texto para el operador, o los errores.</summary>
public sealed record ResultadoActivoN43(int? IdAsiento, string Texto, IReadOnlyList<Error> Errores)
{
    public bool EsCorrecto => Errores.Count == 0;
    public static ResultadoActivoN43 Fallo(params Error[] e) => new(null, "", e);
    public static ResultadoActivoN43 Fallo(IReadOnlyList<Error> e) => new(null, "", e);
}

/// <summary>
/// «Compra de activo…» (cargos) y «Venta de activo…» (abonos) de Líneas N43 (activos/activos-financieros 2026-11-24 09:14 y 09:24; solo activos
/// financieros). Se elige una compra o venta sin conciliar y se genera el asiento de la línea con el importe del banco, que pasa a ser el de la operación:
/// <list type="bullet">
/// <item>compra: la subcuenta del activo (rol «Valor del bien», la 54001 de su plantilla) al debe contra el banco;</item>
/// <item>venta: el banco al debe contra la subcuenta del activo por el coste medio de lo vendido, y la diferencia a la 766 (beneficio) o a la 666
/// (pérdida) del activo, que se buscan por su código entre sus subcuentas.</item>
/// </list>
/// La operación queda conciliada con la línea; «Deshacer el asiento…» de la línea la deja sin conciliar con su importe anterior.
/// </summary>
public sealed class ServicioActivosN43(IBaseDatos bd, ServicioConciliacionN43 conciliacion)
{
    /// <summary>Origen de las cuentas que pone esta conciliación en la línea.</summary>
    public const string OrigenActivo = "ACTIVO";
    public const string PrefijoBeneficio = "766", PrefijoPerdida = "666";

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly ConsultaLineasN43 _consulta = new(bd);

    /// <summary>Por qué no se puede usar la línea (null si se puede): una sola línea del banco, sin contabilizar ni desglosar, de un ejercicio abierto.</summary>
    public static Error? Bloqueo(LineaN43 l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var que = l.EsCargo ? "Compra de activo" : "Venta de activo";
        return l.EsRecibo ? new Error("ActivoN43.EsRecibo", "Esta línea es parte de un desglose.", l.Rotulo, null, "Elige la línea del banco.")
            : l.Explotada ? new Error("ActivoN43.Explotada", "Este movimiento está desglosado.", l.Rotulo, null, "Usa antes «Deshacer explosión» si hay que repetirlo.")
            : l.IdAsiento is not null ? new Error("ActivoN43.Contabilizada", "El movimiento ya está contabilizado.", l.Rotulo, $"«{que}» genera su asiento.",
                "Deshaz antes su asiento («Deshacer el asiento…») si lo generó ProContaVF, y vuelve a intentarlo.")
            : l.EjercicioCerrado ? new Error("ActivoN43.EjercicioCerrado", $"El ejercicio {l.Ejercicio} está cerrado.", l.Rotulo, "Sus líneas solo se consultan.", null)
            : null;
    }

    /// <summary>Las compras (si la línea es un cargo) o ventas (si es un abono) de activos financieros sin conciliar, la de importe más parecido primero.</summary>
    public async Task<PropuestaActivoN43?> ProponerAsync(string idSociedad, int idLinea)
    {
        if ((await _consulta.LineasAsync(idSociedad, null, [idLinea])).FirstOrDefault() is not { } l) return null;
        var esCompra = l.EsCargo;
        if (Bloqueo(l) is { } b) return new PropuestaActivoN43(l, esCompra, [], b);
        var ops = await bd.EjecutarAsync(s => ConciliacionOperacionesInversion.PendientesAsync(s, idSociedad,
            esCompra ? OperacionDeInversion.TipoCompra : OperacionDeInversion.TipoVenta));
        var lista = ops.Select(o => new OperacionActivoN43(o, l.Importe - o.ImporteTotalEur))
            .OrderBy(o => Math.Abs(o.Diferencia)).ThenBy(o => Math.Abs((o.Operacion.Fecha - l.FechaOperacion).TotalDays)).ToList();
        if (lista.Count == 0)
            return new PropuestaActivoN43(l, esCompra, [], new Error("ActivoN43.SinOperaciones",
                esCompra ? "No hay ninguna compra de activos financieros sin conciliar." : "No hay ninguna venta de activos financieros sin conciliar.", l.Rotulo,
                esCompra ? "Se concilian las compras anotadas con «Nueva compra…» o en el alta del activo." : "Se concilian las ventas anotadas con «Vender…» en Activos.",
                esCompra ? "Anota antes la compra en Activos." : "Anota antes la venta en Activos (botón derecho sobre el activo › «Vender…»)."));
        return new PropuestaActivoN43(l, esCompra, lista, null);
    }

    /// <summary>
    /// Concilia la línea con la operación: le pone las cuentas del activo con el importe del banco, la marca como conciliada y genera el asiento. Si el
    /// asiento no se puede generar, deja la operación como estaba y la línea sin las cuentas del activo.
    /// </summary>
    public async Task<ResultadoActivoN43> ConciliarAsync(string idSociedad, int idLinea, int idOperacion, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if ((await _consulta.LineasAsync(idSociedad, null, [idLinea])).FirstOrDefault() is not { } l)
            return ResultadoActivoN43.Fallo(new Error("ActivoN43.SinLinea", $"No encuentro el movimiento {idLinea}.", $"Movimiento {idLinea}", "Se ha revertido su extracto.", "Actualiza la lista (F5)."));
        if (Bloqueo(l) is { } b) return ResultadoActivoN43.Fallo(b);
        var preparado = await bd.EnTransaccionAsync(async s =>
        {
            if (await ConciliacionOperacionesInversion.OperacionAsync(s, idSociedad, idOperacion) is not { } leida)
                return Resultado<string>.Fallo(new Error("ActivoN43.SinOperacion", "Esa operación del activo ya no existe.", l.Rotulo, null, "Vuelve a abrir el diálogo."));
            var (op, linea) = leida;
            if (linea is not null)
                return Resultado<string>.Fallo(new Error("ActivoN43.YaConciliada", $"Esa operación de {op.CodigoActivo} ya está conciliada con otro movimiento.", l.Rotulo, null,
                    "Vuelve a abrir el diálogo: ya no aparece entre las pendientes."));
            if (op.EsCompra != l.EsCargo)
                return Resultado<string>.Fallo(new Error("ActivoN43.SentidoCambiado", l.EsCargo ? "Un cargo del banco solo se concilia con una compra." : "Un abono del banco solo se concilia con una venta.", l.Rotulo));
            var idEj = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?", idSociedad, l.Ejercicio);
            if (idEj is null or DBNull)
                return Resultado<string>.Fallo(new Error("ActivoN43.SinEjercicio", $"La sociedad no tiene el ejercicio {l.Ejercicio}.", l.Rotulo, null,
                    $"Da de alta el ejercicio {l.Ejercicio} en Parámetros y tablas › Ejercicios."));
            var subcuentas = await ConciliacionOperacionesInversion.SubcuentasAsync(s, op.IdActivo, Convert.ToInt32(idEj, CultureInfo.InvariantCulture));
            var faltan = $"Pulsa «Generar las subcuentas que les falten…» sobre {op.CodigoActivo} en Activos (con la plantilla del tipo «Inversión financiera») y créalas en el ejercicio {l.Ejercicio}.";
            var valor = subcuentas.FirstOrDefault(c => c.Rol == "ACTIVO");
            if (valor is null)
                return Resultado<string>.Fallo(new Error("ActivoN43.SinSubcuentaValor", $"El activo {op.CodigoActivo} no tiene en {l.Ejercicio} la subcuenta de su valor (la 54001 de la plantilla).", l.Rotulo,
                    "El asiento lleva la operación a la subcuenta del activo.", faltan));
            var imputaciones = new List<(int Identidad, decimal Firmado, string Concepto)>();
            string texto;
            if (op.EsCompra)
            {
                imputaciones.Add((valor.IdIdentidadContable, l.Importe, $"Compra de {op.NombreActivo}"));
                texto = $"{l.Rotulo}: compra de {op.CodigoActivo} conciliada; va a la {valor.Codigo}.";
            }
            else
            {
                var coste = op.CosteVendido ?? 0m;
                var diferencia = l.Importe - coste;
                imputaciones.Add((valor.IdIdentidadContable, -coste, $"Venta de {op.NombreActivo}"));
                if (diferencia != 0m)
                {
                    var prefijo = diferencia > 0m ? PrefijoBeneficio : PrefijoPerdida;
                    var resultado = subcuentas.FirstOrDefault(c => c.Codigo.StartsWith(prefijo, StringComparison.Ordinal));
                    if (resultado is null)
                        return Resultado<string>.Fallo(new Error("ActivoN43.SinSubcuentaResultado",
                            $"El activo {op.CodigoActivo} no tiene en {l.Ejercicio} la subcuenta {prefijo} para {(diferencia > 0m ? "el beneficio" : "la pérdida")} de la venta.", l.Rotulo,
                            $"La diferencia entre lo cobrado y el coste de lo vendido ({Math.Abs(diferencia).ToString("N2", Es)} €) va a esa subcuenta.", faltan));
                    imputaciones.Add((resultado.IdIdentidadContable, -diferencia, diferencia > 0m ? $"Beneficio en la venta de {op.NombreActivo}" : $"Pérdida en la venta de {op.NombreActivo}"));
                    texto = $"{l.Rotulo}: venta de {op.CodigoActivo} conciliada; coste de lo vendido {coste.ToString("N2", Es)} € a la {valor.Codigo} y " +
                            $"{(diferencia > 0m ? "beneficio" : "pérdida")} de {Math.Abs(diferencia).ToString("N2", Es)} € a la {resultado.Codigo}.";
                }
                else texto = $"{l.Rotulo}: venta de {op.CodigoActivo} conciliada, sin beneficio ni pérdida; va a la {valor.Codigo}.";
            }
            if (l.Importe != op.ImporteTotalEur)
                texto += $" El importe de la {(op.EsCompra ? "compra" : "venta")} pasa de {op.ImporteTotalEur.ToString("N2", Es)} € al del banco, {l.Importe.ToString("N2", Es)} €.";
            await s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", l.Id);
            foreach (var (identidad, firmado, concepto) in imputaciones)
                await s.EjecutarAsync(
                    "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Confianza, Estado, Concepto, IdCentroCoste, IdPalabraClaveBancaria) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    l.Id, identidad, AsignadorCuentasN43.Rol, firmado, OrigenActivo, null, AsignadorCuentasN43.Estado, concepto, null, null);
            await ConciliacionOperacionesInversion.MarcarAsync(s, idOperacion, l.Id, l.Importe);
            return Resultado<string>.Correcto(texto);
        });
        if (!preparado.EsCorrecto) return ResultadoActivoN43.Fallo(preparado.Errores);
        var r = await conciliacion.ContabilizarLineasAsync(idSociedad, [l.Id], actor);
        if (r.Asientos.TryGetValue(l.Id, out var asiento)) return new ResultadoActivoN43(asiento, preparado.Valor + $" Asiento {asiento}.", []);
        await bd.EnTransaccionAsync(async s =>
        {
            await ConciliacionOperacionesInversion.DesmarcarLineaAsync(s, l.Id);
            await s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Origen = ? AND Estado <> 'DESCARTADA'",
                l.Id, OrigenActivo);
            return Resultado.Correcto();
        });
        return ResultadoActivoN43.Fallo(r.Rechazadas.GetValueOrDefault(l.Id) is { Count: > 0 } e ? e
            : [new Error("ActivoN43.SinAsiento", "No se ha generado el asiento.", l.Rotulo, null, "Vuelve a intentarlo.")]);
    }
}
