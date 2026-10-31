using ProConta.UI.Base.Formato;

namespace ProConta.UI.Base.Tests;

public class FormatosTests
{
    [Theory]
    [InlineData("1234.56", "1.234,56 €")]
    [InlineData("0", "0,00 €")]
    [InlineData("-741", "−741,00 €")]
    [InlineData("1234567.005", "1.234.567,01 €")]
    public void Importe_en_euros_con_miles_y_dos_decimales(string v, string esperado) =>
        Assert.Equal(esperado, Formatos.Importe(decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Importe_cero_vacio_en_debe_y_haber() => Assert.Equal("", Formatos.ImporteOVacio(0m));

    [Fact]
    public void Fecha_corta() => Assert.Equal("31/10/2026", Formatos.Fecha(new DateOnly(2026, 10, 31)));

    [Fact]
    public void Recuento_con_miles() => Assert.Equal("5.242 apuntes", Formatos.Plural(5242, "apunte", "apuntes"));

    [Fact]
    public void Recuento_singular() => Assert.Equal("1 apunte", Formatos.Plural(1, "apunte", "apuntes"));
}

public class ReglaSaldoTests
{
    [Theory]
    [InlineData("5720001", 100, SentidoSaldo.AFavor)]     // banco con dinero
    [InlineData("43000012", 100, SentidoSaldo.AFavor)]    // cliente que nos debe
    [InlineData("40000031", -100, SentidoSaldo.EnContra)] // proveedor al que debemos
    [InlineData("4000004", 100, SentidoSaldo.AFavor)]     // proveedor pagado de más
    [InlineData("4750001", -100, SentidoSaldo.EnContra)]  // Hacienda acreedora
    [InlineData("6290001", 100, SentidoSaldo.EnContra)]   // gasto
    [InlineData("7050001", -100, SentidoSaldo.AFavor)]    // ingreso
    [InlineData("1000000", -100, SentidoSaldo.Neutro)]    // capital: negro
    [InlineData("2130000", 100, SentidoSaldo.Neutro)]
    [InlineData("3000000", 100, SentidoSaldo.Neutro)]
    [InlineData("5720001", 0, SentidoSaldo.Neutro)]
    public void Color_por_grupo_del_PGC(string cuenta, int saldo, SentidoSaldo esperado) =>
        Assert.Equal(esperado, ReglaSaldo.Sentido(cuenta, saldo));

    [Fact]
    public void Varias_cuentas_de_distinto_grupo_sin_color()
    {
        Assert.Null(ReglaSaldo.GrupoComun(new[] { "43000012", "5720001" }));
        Assert.Equal('4', ReglaSaldo.GrupoComun(new[] { "43000012", "40000031" }));
    }

    [Fact]
    public void Descripcion_deudor_acreedor()
    {
        Assert.Equal("Saldo deudor", ReglaSaldo.Descripcion(1));
        Assert.Equal("Saldo acreedor", ReglaSaldo.Descripcion(-1));
        Assert.Equal("Saldo cero", ReglaSaldo.Descripcion(0.001m));
    }
}
