using ProConta.Compras.Mantenimiento;
using ProConta.Compras.Proveedores;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Compras.Tests;

/// <summary>Titular y NIF del sujeto de un certificado.</summary>
public sealed class SujetoCertificadoTests
{
    [Theory]
    [InlineData("CN=B12345674 EMISOR FICTICIO (R: 12345678Z), OID.2.5.4.97=VATES-B12345674, O=EMISOR FICTICIO SL, C=ES", "B12345674 EMISOR FICTICIO (R: 12345678Z)", "B12345674")]
    [InlineData("CN=\"APELLIDO NOMBRE - 12345678Z\", SERIALNUMBER=IDCES-12345678Z, C=ES", "APELLIDO NOMBRE - 12345678Z", "12345678Z")]
    [InlineData("O=Solo Organizacion, C=ES", "Solo Organizacion", null)]
    public void Saca_el_titular_y_el_nif(string sujeto, string titular, string? nif)
    {
        Assert.Equal(titular, ProveedorCertificadoEmpresa.Titular(sujeto));
        Assert.Equal(nif, ProveedorCertificadoEmpresa.NifDe(sujeto));
    }
}

/// <summary>Elegir el certificado de la empresa y probarlo, contra una copia de pruebas (sin almacén de Windows ni AEAT).</summary>
[Trait("Categoria", "Access")]
public sealed class CertificadoEmpresaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoMantenimiento _c = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("Pruebas"));

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task Se_elige_de_los_instalados_se_guarda_la_huella_y_se_prueba_con_el_nif_de_la_empresa()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética", Documentos.NifSociedad);
        var instalados = new[] { new CertificadoEquipo("AA11", "EMISOR", "B12345674", new DateTime(2030, 1, 1)) };
        string? consultado = null;
        var p = new ProveedorCertificadoEmpresa(bd, () => instalados, (s, nif, _) => { consultado = nif; return Task.FromResult(new ResultadoNifAeat(true, true, "SOCIEDAD SINTETICA", null)); });

        var r = Assert.Single(await p.ListarAsync(_c));
        Assert.Null(r["Huella"]);
        Assert.Equal(2, p.Campos[0].Opciones!.Count);
        r["Huella"] = "AA11";
        var g = await p.GuardarAsync(r, _c);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal("AA11", g.Valor!["HuellaGuardada"]);

        // En otro equipo sin ese certificado, la huella guardada sigue y se avisa.
        var otro = new ProveedorCertificadoEmpresa(bd, () => []);
        Assert.Equal("AA11", Assert.Single(await otro.ListarAsync(_c))["Huella"]);
        Assert.Contains(otro.Campos[0].Opciones!, o => o.Texto.Contains("No instalado"));

        var prueba = await p.EjecutarAsync("probaraeat", g.Valor, _c);
        Assert.True(prueba.EsCorrecto);
        Assert.Contains("SOCIEDAD SINTETICA", prueba.Valor!.Mensaje);
        Assert.Equal(Documentos.NifSociedad, consultado);
    }
}
