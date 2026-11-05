namespace ProConta.Compras.Reconocimiento;

public enum MagnitudColumnaIva { BaseImponible, Cuota, Total }

public sealed record ResultadoColumnaIva(decimal BaseImponible, decimal Cuota, decimal Total);

/// <summary>
/// Calcula las tres magnitudes de una columna de IVA (base, cuota y total) a partir de la que edita el operador.
/// Redondeo a céntimos, mitad hacia fuera, por columna (como el motor VB).
/// </summary>
public static class CalculadoraColumnaIva
{
    /// <param name="tipoIva">Porcentaje de IVA, por ejemplo 21.</param>
    /// <param name="baseActual">Base que se conserva cuando se edita la cuota de una columna al 0 %.</param>
    public static ResultadoColumnaIva Calcular(decimal tipoIva, MagnitudColumnaIva magnitud, decimal valor, decimal baseActual = 0m)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tipoIva);
        var tasa = tipoIva / 100m;
        switch (magnitud)
        {
            case MagnitudColumnaIva.BaseImponible:
                {
                    var cuota = Redondear(valor * tasa);
                    return new(valor, cuota, valor + cuota);
                }
            case MagnitudColumnaIva.Cuota:
                {
                    var cuota = tasa == 0m ? 0m : valor;
                    var @base = tasa == 0m ? baseActual : Redondear(cuota / tasa);
                    return new(@base, cuota, @base + cuota);
                }
            case MagnitudColumnaIva.Total:
                {
                    var @base = Redondear(valor / (1m + tasa));
                    return new(@base, valor - @base, valor);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(magnitud));
        }
    }

    private static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
