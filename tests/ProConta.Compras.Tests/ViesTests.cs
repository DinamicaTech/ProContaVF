using ProConta.Compras.Integraciones;
using ProConta.Compras.Proveedores;

namespace ProConta.Compras.Tests;

/// <summary>VIES (propietario 2026-11-07 20:25): qué NIF son comunitarios y cómo se lee la respuesta del servicio.</summary>
public sealed class ViesTests
{
    [Theory]
    [InlineData("DE 123 456 789", "DE", "123456789")]
    [InlineData("GR123456789", "EL", "123456789")]
    [InlineData("FR12345678901", "FR", "12345678901")]
    public void Un_nif_comunitario_da_pais_y_numero(string nif, string pais, string numero) =>
        Assert.Equal((pais, numero), ServicioAltaProveedorCompra.NifComunitario(nif)!.Value);

    [Theory]
    [InlineData("B61465332")]
    [InlineData("ESN2763708A")]
    [InlineData("GB123456789")]
    [InlineData("")]
    public void Un_nif_espanol_o_de_fuera_de_la_ue_no_es_comunitario(string nif) =>
        Assert.Null(ServicioAltaProveedorCompra.NifComunitario(nif));

    [Fact]
    public void Lee_valido_con_nombre_valido_sin_nombre_no_valido_y_servicio_caido()
    {
        var conNombre = ConsultaViesRest.Interpretar("""{"isValid":true,"userError":"VALID","name":"VENDEUR FICTIF SARL","address":"1 RUE X"}""", 200);
        Assert.Equal((true, true, "VENDEUR FICTIF SARL"), (conNombre.Consultado, conNombre.Valido, conNombre.Nombre));
        var aleman = ConsultaViesRest.Interpretar("""{"isValid":true,"userError":"VALID","name":"---","address":"---"}""", 200);
        Assert.Equal((true, true, (string?)null), (aleman.Consultado, aleman.Valido, aleman.Nombre));
        var noValido = ConsultaViesRest.Interpretar("""{"isValid":false,"userError":"INVALID","name":"---"}""", 200);
        Assert.Equal((true, false), (noValido.Consultado, noValido.Valido));
        Assert.False(ConsultaViesRest.Interpretar("""{"isValid":false,"userError":"MS_UNAVAILABLE"}""", 200).Consultado);
        Assert.False(ConsultaViesRest.Interpretar("<html>error</html>", 500).Consultado);
    }
}
