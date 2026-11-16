using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Norma43.Remesas;

/// <summary>Qué se puede hacer con una factura de la remesa que el banco cobró por otro importe (norma43/remesas 2026-11-16 12:08).</summary>
/// <param name="BaseDiferencia">Base de la factura de diferencia que se emitiría (solo si el banco cobró más y se puede).</param>
public sealed record PropuestaAjusteN43(int IdFactura, string Numero, decimal Importe, decimal Cobrado,
    bool PuedeProgramada, string? MotivoSinProgramada, decimal? TotalProgramada, bool PuedeDiferencia, string? MotivoSinDiferencia, decimal? BaseDiferencia,
    bool PuedeAsumir = false, string? MotivoSinAsumir = null)
{
    public decimal Diferencia => Cobrado - Importe;
    public bool CobraMas => Diferencia > 0m;
}

/// <summary>Qué hacer con la diferencia cuando el banco cobró menos que la factura.</summary>
public enum ModoMenorN43
{
    /// <summary>La diferencia queda pendiente en la 430 del cliente, para cobrársela después.</summary>
    CobrarAlCliente,
    /// <summary>La empresa asume la diferencia: se anula la factura con una rectificativa completa y se emite otra por lo cobrado (propietario 2026-11-16 13:02).</summary>
    Asumir,
}

/// <summary>Lo que el operador elige para una factura: actualizar su programada, emitir la factura de diferencia o qué hacer si cobró menos.</summary>
public sealed record AjusteElegidoN43(int IdFactura, decimal Cobrado, int IdLineaRecibo, bool ActualizarProgramada, bool EmitirDiferencia, ModoMenorN43 Menor);

/// <summary>Lo que se hizo, lo que falló (con qué hacer) y los recordatorios (actualizar la remesa del banco).</summary>
public sealed record ResultadoAjustesN43(IReadOnlyList<string> Hechos, IReadOnlyList<string> Fallos, IReadOnlyList<string> Recordatorios);

/// <summary>
/// Puerto de los ajustes de importe de una remesa: lo implementa facturación (facturas de diferencia y programadas) y lo conecta la
/// aplicación, porque Norma 43 no depende de facturación. Sin él, la diferencia queda en la 430 del cliente.
/// </summary>
public interface IAjustesRemesaN43
{
    /// <summary>Qué se puede hacer con cada factura cobrada por otro importe.</summary>
    Task<IReadOnlyList<PropuestaAjusteN43>> ProponerAsync(IReadOnlyList<(int IdFactura, decimal Cobrado)> facturas);

    /// <summary>Hace lo elegido, factura a factura (lo que falla no impide lo demás), y enlaza las facturas de diferencia con su recibo.</summary>
    Task<ResultadoAjustesN43> AplicarAsync(IReadOnlyList<AjusteElegidoN43> ajustes, ContextoSociedadEjercicio trabajo, Actor actor);
}
