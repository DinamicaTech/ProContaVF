using ProConta.Nucleo.Identificacion;

namespace ProConta.Nucleo.Tests;

// IBAN sintéticos: dígitos de control calculados sobre cuentas inventadas.
public class IbanesTests
{
    [Theory]
    [InlineData("ES8700491500050012345678")]
    [InlineData("es87 0049 1500 0500 1234 5678")]
    [InlineData("ES40-2100-0001-0000-0000-0001")]
    public void UnIbanBienFormadoEsValido(string iban) => Assert.True(Ibanes.EsValido(iban));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ES8800491500050012345678")]
    [InlineData("ES870049150005001234567")]
    [InlineData("8700491500050012345678ES")]
    public void UnIbanMalFormadoNoEsValido(string? iban) => Assert.False(Ibanes.EsValido(iban));

    [Fact]
    public void SeGuardaSinEspaciosYSeMuestraDeCuatroEnCuatro()
    {
        Assert.Equal("ES8700491500050012345678", Ibanes.Normalizar(" es87 0049-1500 0500 1234 5678 "));
        Assert.Equal("ES87 0049 1500 0500 1234 5678", Ibanes.Agrupar("ES8700491500050012345678"));
        Assert.Null(Ibanes.Normalizar("  "));
    }

    [Fact]
    public void EnmascaradoDejaElPaisYLosCuatroUltimos() =>
        Assert.Equal("ES** **** **** **** **** 5678", Ibanes.Enmascarar("ES8700491500050012345678"));

    [Fact]
    public void ElIbanEspanolSeCalculaDesdeElCcc()
    {
        Assert.Equal("ES8700491500050012345678", Ibanes.DesdeCcc("0049", "1500", "05", "0012345678"));
        Assert.Null(Ibanes.DesdeCcc("49", "1500", "05", "0012345678"));
    }
}
