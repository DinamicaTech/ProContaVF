using System.Globalization;
using ProConta.Facturacion.Programadas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Qué se puede hacer con una factura que el banco cobró por otro importe (norma43/remesas 2026-11-16 12:08): la factura de diferencia
/// que se emitiría y la programada que se actualizaría, o por qué no.
/// </summary>
/// <param name="LineaDiferencia">La línea de la factura de diferencia (base calculada hacia atrás para que el total sea la diferencia); null si no se puede.</param>
public sealed record PlanAjusteCobro(int IdFactura, string Numero, decimal Importe, decimal Cobrado,
    int? IdProgramada, decimal? TotalProgramada, string? MotivoSinProgramada,
    LineaFacturaVenta? LineaDiferencia, TotalesFactura? TotalesDiferencia, string? MotivoSinDiferencia)
{
    public decimal Diferencia => Cobrado - Importe;
    public bool CobraMas => Diferencia > 0m;
    public bool PuedeProgramada => IdProgramada is not null && MotivoSinProgramada is null;
    public bool PuedeDiferencia => LineaDiferencia is not null;
}

/// <summary>
/// Ajustes de una factura cobrada por otro importe que el suyo (norma43/remesas): emitir una factura por la diferencia (misma serie,
/// cliente, IVA y retención que la original, con la base calculada hacia atrás para que el total sea exactamente la diferencia) y
/// actualizar su programada al importe cobrado. No sabe nada del banco: quien la llama enlaza la factura nueva con el recibo.
/// </summary>
public sealed class AjustesCobroFacturas(ServicioFacturas facturas, ServicioProgramadas programadas)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Lo que se haría con la factura y el importe cobrado; null si la factura no existe.</summary>
    public async Task<PlanAjusteCobro?> PlanAsync(int idFactura, decimal cobrado)
    {
        if (await facturas.ObtenerAsync(idFactura) is not { } f) return null;
        var total = f.Totales.ImporteTotal.Valor;
        var diferencia = cobrado - total;
        LineaFacturaVenta? linea = null;
        TotalesFactura? totales = null;
        string? sinDiferencia = null;
        if (diferencia <= 0m) sinDiferencia = "El banco no cobró más que la factura.";
        else if (Modelo(f) is not { } modelo) sinDiferencia = "La factura tiene líneas con IVA distinto (o solo suplidos): emite la diferencia a mano.";
        else if (LineaPorTotal(modelo, diferencia, f.TipoRetencion) is not { } l)
            sinDiferencia = $"No hay una base que dé {diferencia.ToString("N2", Es)} € (ni a un céntimo) con su IVA y su retención: emítela a mano.";
        else
        {
            linea = l;
            totales = CalculoFactura.Totales([l], f.TipoRetencion);
        }

        int? idProgramada = f.IdFacturaProgramada;
        decimal? totalProgramada = null;
        string? sinProgramada = null;
        if (idProgramada is not int ip) sinProgramada = "La factura no viene de una factura programada.";
        else if (await programadas.ObtenerAsync(ip) is not { } p) sinProgramada = "Su factura programada ya no existe.";
        else
        {
            totalProgramada = p.Plantilla.Totales.ImporteTotal.Valor;
            if (p.Plantilla.Lineas.Count(x => !x.EsSuplido) != 1) sinProgramada = "La programada tiene varias líneas: actualízala a mano.";
            else if (totalProgramada == cobrado) sinProgramada = "La programada ya tiene ese importe.";
            else if (LineaPorTotal(p.Plantilla.Lineas.Single(x => !x.EsSuplido), cobrado - p.Plantilla.Lineas.Where(x => x.EsSuplido).Sum(x => x.ImporteTotal.Valor), p.Plantilla.TipoRetencion) is null)
                sinProgramada = $"No hay un precio que dé {cobrado.ToString("N2", Es)} € (ni a un céntimo) con su IVA y su retención: actualízala a mano.";
        }
        return new PlanAjusteCobro(f.IdFactura, f.Rotulo, total, cobrado, idProgramada, totalProgramada, sinProgramada, linea, totales, sinDiferencia);
    }

    /// <summary>
    /// Emite y valida la factura por la diferencia entre lo cobrado y la factura: fecha de hoy (para no romper el orden de la serie), su
    /// periodo, cliente, forma y día de pago, y una línea como la principal de la original por la diferencia.
    /// </summary>
    public async Task<Resultado<FacturaVenta>> EmitirDiferenciaAsync(int idFactura, decimal cobrado, ContextoSociedadEjercicio trabajo, Actor actor)
    {
        var plan = await PlanAsync(idFactura, cobrado);
        var original = await facturas.ObtenerAsync(idFactura);
        if (plan is null || original is null) return Resultado<FacturaVenta>.Fallo(new Error("Ajustes.FacturaNoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura)));
        if (plan.LineaDiferencia is not { } linea)
            return Resultado<FacturaVenta>.Fallo(new Error("Ajustes.SinDiferencia", $"No se puede emitir la factura de diferencia de {plan.Numero}.", plan.Numero, plan.MotivoSinDiferencia,
                "Emítela a mano desde Facturas y enlázala al recibo con «Buscar por importe»."));
        var nuevo = await facturas.NuevoBorradorAsync(trabajo);
        if (!nuevo.EsCorrecto) return nuevo;
        var f = nuevo.Valor;
        if (original.IdSociedadEjercicio == trabajo.IdSociedadEjercicio) f.IdSerie = original.IdSerie;
        f.IdCliente = original.IdCliente;
        f.FechaOperacion = original.FechaOperacion ?? f.FechaOperacion;
        f.FechaInicioValidez = original.FechaInicioValidez;
        f.FechaFinValidez = original.FechaFinValidez;
        f.FormaDePago = original.FormaDePago;
        f.DiaDePago = original.DiaDePago;
        f.TipoRetencion = original.TipoRetencion;
        f.TipoFactura = original.TipoFactura;
        f.Descripcion = Recortar($"Diferencia de importe de la factura {original.NumeroFactura}: {original.Descripcion}".Trim(), 4000);
        f.NotasInternas = $"Emitida desde la remesa: el banco cobró {cobrado.ToString("N2", Es)} € por la factura {original.NumeroFactura} de {plan.Importe.ToString("N2", Es)} €.";
        linea.Nombre = "Diferencia de importe";
        linea.Descripcion = Recortar($"Diferencia entre lo cobrado ({cobrado.ToString("N2", Es)} €) y la factura {original.NumeroFactura} ({plan.Importe.ToString("N2", Es)} €)", 4000);
        linea.NotasInternas = null;
        f.AnadirLinea(linea);
        var borrador = await facturas.GuardarBorradorAsync(f, actor);
        if (!borrador.EsCorrecto) return borrador;
        var v = await facturas.ValidarAsync(borrador.Valor.IdFactura, borrador.Valor.Version, actor, aceptarAvisos: true);
        if (!v.Validada)
            return Resultado<FacturaVenta>.Fallo(new Error("Ajustes.NoValidada", $"La factura de diferencia de {plan.Numero} se ha quedado en borrador ({borrador.Valor.Rotulo}).", plan.Numero,
                string.Join(" ", v.Incidencias.Select(i => i.Mensaje)), "Ábrela en Facturas, corrige lo que dice y valídala; después enlázala al recibo con «Buscar por importe»."));
        return Resultado<FacturaVenta>.Correcto(v.Factura!);
    }

    /// <summary>Pone la programada de la factura al importe cobrado (su única línea, con su IVA y su retención); afecta a las facturas que genere desde ahora.</summary>
    public async Task<Resultado<string>> ActualizarProgramadaAsync(int idFactura, decimal cobrado, Actor actor)
    {
        var plan = await PlanAsync(idFactura, cobrado);
        if (plan is null) return Resultado<string>.Fallo(new Error("Ajustes.FacturaNoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura)));
        if (!plan.PuedeProgramada || await programadas.ObtenerAsync(plan.IdProgramada!.Value) is not { } p)
            return Resultado<string>.Fallo(new Error("Ajustes.SinProgramada", $"No se actualiza la programada de {plan.Numero}.", plan.Numero, plan.MotivoSinProgramada, "Revísala en Facturas programadas."));
        var linea = p.Plantilla.Lineas.Single(x => !x.EsSuplido);
        var suplidos = p.Plantilla.Lineas.Where(x => x.EsSuplido).Sum(x => x.ImporteTotal.Valor);
        var nueva = LineaPorTotal(linea, cobrado - suplidos, p.Plantilla.TipoRetencion)!;
        var antes = plan.TotalProgramada ?? 0m;
        linea.Cantidad = nueva.Cantidad;
        linea.PrecioUnitario = nueva.PrecioUnitario;
        linea.DescuentoPorcentaje = 0m;
        linea.DescuentoImporte = 0m;
        linea.Recalcular();
        var r = await programadas.GuardarAsync(p, actor,
            $"Importe actualizado de {antes.ToString("N2", Es)} € a {cobrado.ToString("N2", Es)} € desde la remesa (lo que cobró el banco por la factura {plan.Numero})");
        return r.Guardada
            ? Resultado<string>.Correcto($"Programada de {plan.Numero} actualizada de {antes.ToString("N2", Es)} € a {cobrado.ToString("N2", Es)} €.")
            : Resultado<string>.Fallo(new Error("Ajustes.ProgramadaNoGuardada", $"No se ha actualizado la programada de {plan.Numero}.", plan.Numero,
                string.Join(" ", r.Incidencias.Select(i => i.Mensaje)), "Ábrela en Facturas programadas y cambia el importe a mano."));
    }

    /// <summary>La línea principal de la factura (la de más base) si todas las no suplidas tienen el mismo IVA; null si no.</summary>
    internal static LineaFacturaVenta? Modelo(FacturaVenta f)
    {
        var lineas = f.Lineas.Where(l => !l.EsSuplido).ToList();
        if (lineas.Count == 0 || lineas.Select(l => (l.TipoIva, l.TipoRecargo, l.Impuesto, l.CalificacionOperacion, l.CausaExencion)).Distinct().Count() != 1) return null;
        return lineas.OrderByDescending(l => l.BaseImponible.Valor).First();
    }

    /// <summary>
    /// Copia de la línea modelo (cantidad 1, sin descuentos) cuya base da <paramref name="total"/> con su IVA, su recargo y la retención de la
    /// factura: total = base + cuotas − retención, cada importe redondeado a céntimos como en la factura. Se prueba la base calculada hacia
    /// atrás y los céntimos de alrededor y gana la que da exactamente el total; si con ese IVA ninguna lo da (hay totales a los que no se
    /// llega por el redondeo de la cuota), la que se queda a un céntimo, por arriba antes que por abajo. Null si ni eso.
    /// </summary>
    public static LineaFacturaVenta? LineaPorTotal(LineaFacturaVenta modelo, decimal total, decimal tipoRetencion)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        if (total <= 0m) return null;
        var factor = 1m + (modelo.EsSuplido ? 0m : (modelo.TipoIva + modelo.TipoRecargo) / 100m) - (modelo.EsSuplido ? 0m : tipoRetencion / 100m);
        if (factor <= 0m) return null;
        var estimada = Math.Round(total / factor, 2, MidpointRounding.AwayFromZero);
        LineaFacturaVenta? mejor = null;
        decimal? distancia = null;
        foreach (var delta in new[] { 0m, 0.01m, -0.01m, 0.02m, -0.02m, 0.03m, -0.03m })
        {
            var l = modelo.Copiar();
            l.Cantidad = 1m;
            l.DescuentoPorcentaje = 0m;
            l.DescuentoImporte = 0m;
            l.PrecioUnitario = estimada + delta;
            if (l.PrecioUnitario <= 0m) continue;
            l.Recalcular();
            var obtenido = CalculoFactura.Totales([l], tipoRetencion).ImporteTotal.Valor;
            if (obtenido == total) return l;
            var d = obtenido - total;
            // Un céntimo como mucho; a igual distancia, por arriba (el cliente no paga de menos).
            if (Math.Abs(d) <= 0.01m && (distancia is null || Math.Abs(d) < Math.Abs(distancia.Value) || (Math.Abs(d) == Math.Abs(distancia.Value) && d > 0m)))
            {
                mejor = l;
                distancia = d;
            }
        }
        return mejor;
    }

    private static string Recortar(string t, int largo) => t.Length <= largo ? t : t[..largo];
}
