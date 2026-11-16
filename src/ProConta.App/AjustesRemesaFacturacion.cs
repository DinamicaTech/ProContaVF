using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;
using ProConta.Norma43.Remesas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.App;

/// <summary>
/// Conecta los ajustes de importe de las remesas (norma43/remesas) con facturación: la factura de diferencia y la programada las hace
/// <see cref="AjustesCobroFacturas"/>, y la factura nueva se enlaza con el mismo recibo que la original.
/// </summary>
public sealed class AjustesRemesaFacturacion(AjustesCobroFacturas ajustes, IBaseDatos bd) : IAjustesRemesaN43
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public async Task<IReadOnlyList<PropuestaAjusteN43>> ProponerAsync(IReadOnlyList<(int IdFactura, decimal Cobrado)> facturas)
    {
        ArgumentNullException.ThrowIfNull(facturas);
        var r = new List<PropuestaAjusteN43>();
        foreach (var (id, cobrado) in facturas)
            if (await ajustes.PlanAsync(id, cobrado) is { } p)
                r.Add(new PropuestaAjusteN43(p.IdFactura, p.Numero, p.Importe, p.Cobrado, p.PuedeProgramada, p.MotivoSinProgramada, p.TotalProgramada,
                    p.PuedeDiferencia, p.MotivoSinDiferencia, p.TotalesDiferencia?.BaseImponible.Valor));
        return r;
    }

    public async Task<ResultadoAjustesN43> AplicarAsync(IReadOnlyList<AjusteElegidoN43> elegidos, ContextoSociedadEjercicio trabajo, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(elegidos);
        var hechos = new List<string>();
        var fallos = new List<string>();
        var recordatorios = new List<string>();
        foreach (var a in elegidos)
        {
            var plan = await ajustes.PlanAsync(a.IdFactura, a.Cobrado);
            if (plan is null) { fallos.Add($"La factura {a.IdFactura} ya no existe."); continue; }
            if (a.ActualizarProgramada)
            {
                var p = await ajustes.ActualizarProgramadaAsync(a.IdFactura, a.Cobrado, actor);
                if (p.EsCorrecto) hechos.Add(p.Valor);
                else fallos.AddRange(p.Errores.Select(e => e.TextoOperador()));
            }
            if (plan.CobraMas && a.EmitirDiferencia)
            {
                var d = await ajustes.EmitirDiferenciaAsync(a.IdFactura, a.Cobrado, trabajo, actor);
                if (!d.EsCorrecto) { fallos.AddRange(d.Errores.Select(e => e.TextoOperador())); continue; }
                var enlazada = await bd.EnTransaccionAsync(s => CobrosFacturasN43.RegistrarCobroAsync(s, d.Valor.IdFactura, a.IdLineaRecibo));
                var total = d.Valor.Totales.ImporteTotal.Valor;
                hechos.Add($"Emitida la factura {d.Valor.Rotulo} por la diferencia de {total.ToString("N2", Es)} € de {plan.Numero}" +
                    (enlazada ? ", cobrada con el mismo recibo" : ", pero no se ha podido enlazar con el recibo: enlázala con «Buscar por importe»") +
                    (total == plan.Diferencia ? "." : $" (con su IVA no se llega a {plan.Diferencia.ToString("N2", Es)} €: el céntimo de redondeo queda en la cuenta del cliente)."));
            }
            if (!plan.CobraMas)
            {
                if (a.Menor == ModoMenorN43.CobrarAlCliente)
                    hechos.Add($"{plan.Numero}: la diferencia de {(-plan.Diferencia).ToString("N2", Es)} € queda pendiente en la cuenta del cliente.");
                recordatorios.Add($"Actualiza en el banco la remesa SEPA de {plan.Numero}: cobró {plan.Cobrado.ToString("N2", Es)} € y la factura es de {plan.Importe.ToString("N2", Es)} €.");
            }
        }
        return new ResultadoAjustesN43(hechos, fallos, recordatorios);
    }
}
