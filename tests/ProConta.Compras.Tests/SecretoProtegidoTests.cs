using ProConta.Compras.Facturas;
using ProConta.Compras.Integraciones;

namespace ProConta.Compras.Tests;

/// <summary>Cifrado de las claves de AWS que se guardan en la configuración de la sociedad.</summary>
public sealed class SecretoProtegidoTests
{
    [Fact]
    public void Se_descifra_lo_cifrado_y_no_se_guarda_en_claro()
    {
        var protegido = SecretoProtegido.Proteger("secreto-de-prueba");
        Assert.DoesNotContain("secreto-de-prueba", protegido);
        Assert.NotEqual(protegido, SecretoProtegido.Proteger("secreto-de-prueba"));
        Assert.Equal("secreto-de-prueba", SecretoProtegido.Desproteger(protegido));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en claro")]
    [InlineData("pcv1:no-es-base64")]
    [InlineData("pcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Lo_que_no_es_un_secreto_valido_no_se_descifra(string? protegido) => Assert.Null(SecretoProtegido.Desproteger(protegido));

    [Fact]
    public void Las_claves_guardadas_valen_solo_completas()
    {
        var bueno = SecretoProtegido.Proteger("s3cr3t");
        Assert.Equal(("eu-west-1", "AKIA1", "s3cr3t"), RepositorioFacturasCompra.Credenciales(" eu-west-1 ", "AKIA1", bueno).Credenciales is { } c ? (c.Region, c.ClaveAcceso, c.Secreto) : default);
        Assert.Equal((null, null), RepositorioFacturasCompra.Credenciales(null, null, null));
        Assert.Contains("región", RepositorioFacturasCompra.Credenciales(null, "AKIA1", bueno).Motivo);
        Assert.Contains("descifrar", RepositorioFacturasCompra.Credenciales("eu-west-1", "AKIA1", "pcv1:roto").Motivo);
    }
}
