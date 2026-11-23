using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

/// <summary>Mandato SEPA del cliente (terceros/roles/mandato-sepa 2026-11-23). Datos inventados.</summary>
public class MandatoSepaTests
{
    private static DatosMandatoSepa Datos() => new("1234", "Cliente <Inventado> SL",
        new DomicilioSepa("Calle Falsa 1", "08001", "Barcelona", "Barcelona", "España"), "ES9121000418450200051332",
        new AcreedorSepa("Sociedad de Prueba SL", "B00000000", new DomicilioSepa("Avenida Inventada 2", "08860", "Castelldefels", "Barcelona", "España")),
        new DateTime(2026, 11, 23));

    [Fact]
    public void ElIdentificadorDelAcreedorSigueLaNormaSepa()
    {
        // Ejemplo del EPC (EPC262-08): identificador alemán con el sufijo ZZZ.
        Assert.Equal("DE98ZZZ09999999999", MandatoSepa.IdentificadorAcreedor("09999999999", "ZZZ", "DE"));
        var id = MandatoSepa.IdentificadorAcreedor("B00000000");
        Assert.StartsWith("ES", id);
        Assert.EndsWith("000B00000000", id);
        Assert.Equal(id, MandatoSepa.IdentificadorAcreedor("ES-B00000000"));
        Assert.Equal(id, MandatoSepa.IdentificadorAcreedor(" b 00000000 "));
    }

    [Fact]
    public void ElMandatoLlevaLosDatosDeLasDosPartesSinSwiftYConPagoPeriodico()
    {
        var d = Datos();
        var html = MandatoSepa.Html(d);
        Assert.Contains("Cliente &lt;Inventado&gt; SL", html);
        Assert.Contains("ES91 2100 0418 4502 0005 1332", html);
        Assert.Contains(d.Acreedor.Identificador, html);
        Assert.Contains("Avenida Inventada 2", html);
        Assert.Contains(">1234<", html);
        Assert.Contains("23/11/2026", html);
        Assert.Contains("Pago periódico<span class=\"casilla\">X</span>", html);
        Assert.DoesNotContain("SWIFT", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Castelldefels", d.Localidad);
        Assert.Equal("Mandato SEPA 1234 Cliente Inventado SL 2026-11-23.pdf", MandatoSepa.NombreFichero(d).Replace("<", "").Replace(">", "").Replace("  ", " "));
    }
}
