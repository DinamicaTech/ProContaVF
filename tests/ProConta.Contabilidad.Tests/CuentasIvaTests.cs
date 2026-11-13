using ProConta.Contabilidad.CentrosCoste;

namespace ProConta.Contabilidad.Tests;

/// <summary>Regla única de las cuentas de IVA (organizacion/sociedades-ejercicios 2026-11-13 20:55), sin BD.</summary>
public sealed class CuentasIvaTests
{
    [Theory]
    [InlineData(21, null, "4720021", "4770021")]
    [InlineData(10, null, "4720010", "4770010")]
    [InlineData(4, null, "4720004", "4770004")]
    [InlineData(5, null, "4720005", "4770005")]
    [InlineData(0, null, "4720000", "4770000")]
    [InlineData(21, "4728", "4728021", "4778021")]
    [InlineData(0, "4728", "4728000", "4778000")]
    [InlineData(10, "4727", "4727010", "4777010")]
    [InlineData(21, " 4725 ", "4725021", "4775021")]
    // Una serie que no es de 4 dígitos se ignora: cuentas generales.
    [InlineData(21, "", "4720021", "4770021")]
    [InlineData(21, "472", "4720021", "4770021")]
    public void Serie_mas_porcentaje_con_tres_cifras(int porcentaje, string? serie, string soportado, string repercutido)
    {
        Assert.Equal(soportado, CuentasIva.Soportado(porcentaje, serie));
        Assert.Equal(repercutido, CuentasIva.Repercutido(porcentaje, serie));
    }

    [Fact]
    public void Un_porcentaje_con_decimales_negativo_o_de_mil_no_tiene_cuenta_y_se_explica()
    {
        foreach (var p in new[] { 5.5m, 0.5m, -21m, 1000m })
        {
            Assert.Null(CuentasIva.Soportado(p));
            Assert.Null(CuentasIva.Repercutido(p, "4728"));
            Assert.Null(CuentasIva.Terminacion(p));
        }
        Assert.Equal("021", CuentasIva.Terminacion(21.00m));
        var motivo = CuentasIva.MotivoSinCuenta(5.5m);
        Assert.Contains("5,5 %", motivo);
        Assert.Contains("Tipos de IVA", motivo);
    }
}
