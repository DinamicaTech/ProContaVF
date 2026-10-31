using ProConta.Nucleo.Identificacion;

namespace ProConta.Nucleo.Tests;

// Todos los NIF de estas pruebas son sintéticos (calculados), no de personas ni empresas reales.
public class NifTests
{
    [Theory]
    [InlineData("12345678Z", TipoNif.Dni)]
    [InlineData("00000000T", TipoNif.Dni)]
    [InlineData("X1234567L", TipoNif.Nie)]
    [InlineData("Y1234567X", TipoNif.Nie)]
    [InlineData("K1234567L", TipoNif.Especial)]
    [InlineData("B12345674", TipoNif.Entidad)]
    [InlineData("Q1234567D", TipoNif.Entidad)]
    [InlineData("G1234567D", TipoNif.Entidad)]
    [InlineData("G12345674", TipoNif.Entidad)]
    public void NifValidoSeAcepta(string texto, TipoNif tipo)
    {
        var r = Nif.Validar(texto);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(texto, r.Valor.Valor);
        Assert.Equal(tipo, r.Valor.Tipo);
    }

    [Theory]
    [InlineData(" 12345678-z ", "12345678Z")]
    [InlineData("es b12345674", "B12345674")]
    [InlineData("x.1234567.l", "X1234567L")]
    public void SeNormalizaAntesDeValidar(string texto, string esperado)
    {
        var r = Nif.Validar(texto);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(esperado, r.Valor.Valor);
    }

    [Theory]
    [InlineData("12345678A", "NIF.Control")]
    [InlineData("X1234567A", "NIF.Control")]
    [InlineData("B1234567D", "NIF.Control")]   // B exige cifra
    [InlineData("Q12345674", "NIF.Control")]   // Q exige letra
    [InlineData("B12345675", "NIF.Control")]
    [InlineData("1234567Z", "NIF.Longitud")]
    [InlineData("", "NIF.Vacio")]
    [InlineData(null, "NIF.Vacio")]
    [InlineData("I12345674", "NIF.Formato")]
    [InlineData("1234A678Z", "NIF.Formato")]
    public void NifIncorrectoDaErrorExplicado(string? texto, string codigo)
    {
        var r = Nif.Validar(texto);
        Assert.False(r.EsCorrecto);
        Assert.Equal(codigo, r.Error!.Codigo);
        Assert.False(string.IsNullOrWhiteSpace(r.Error.Causa));
        Assert.False(string.IsNullOrWhiteSpace(r.Error.AccionCorrectora));
    }

    [Fact]
    public void ErrorDeControlIndicaElCaracterEsperado()
    {
        var r = Nif.Validar("12345678A");
        Assert.Contains("Z", r.Error!.Causa);
    }
}
