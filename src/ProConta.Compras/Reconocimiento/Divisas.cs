namespace ProConta.Compras.Reconocimiento;

/// <summary>Cambio oficial de una divisa a otra en una fecha efectiva.</summary>
public sealed record TipoCambioHistorico(DateTime Fecha, string DivisaOrigen, string DivisaDestino, decimal Tipo);

/// <summary>Cambio histórico (Frankfurter con datos del BCE en producción).</summary>
public interface IProveedorTiposCambioHistoricos
{
    /// <summary>Cambio de la fecha o del último día hábil anterior (hasta 7 días); null si no hay.</summary>
    Task<TipoCambioHistorico?> ObtenerAsync(string divisaOrigen, string divisaDestino, DateTime fecha, CancellationToken cancelacion);
}

/// <summary>Pasa a euros el total en divisa con el cambio de la fecha de la factura (redondeo a 2 decimales, mitad hacia fuera).</summary>
public sealed class ConversorDivisaFacturaCompra(IProveedorTiposCambioHistoricos proveedor)
{
    public async Task<bool> ConvertirAEurosAsync(ResultadoReconocimientoFacturaCompra resultado, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        if (!resultado.RequiereConversionDivisa || !resultado.ImporteTotalDivisa.HasValue || !resultado.FechaFactura.HasValue) return false;
        var cambio = await proveedor.ObtenerAsync(resultado.Divisa, "EUR", resultado.FechaFactura.Value.Date, cancelacion).ConfigureAwait(false);
        if (cambio is null) return false;
        resultado.TipoCambioAplicado = cambio.Tipo;
        resultado.FechaTipoCambio = cambio.Fecha;
        resultado.ImporteTotalEuros = Math.Round(resultado.ImporteTotalDivisa.Value * cambio.Tipo, 2, MidpointRounding.AwayFromZero);
        return true;
    }
}
