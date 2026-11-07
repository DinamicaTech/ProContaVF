using ProConta.Compras.Facturas;
using ProConta.Compras.Mantenimiento;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Compras.Tests;

/// <summary>Parámetros del reconocimiento de compras con las claves de AWS cifradas, contra una copia de pruebas.</summary>
[Trait("Categoria", "Access")]
public sealed class ReconocimientoCompraAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoMantenimiento _c = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("Pruebas"));

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task El_secreto_se_guarda_cifrado_no_vuelve_a_la_pantalla_y_se_conserva_si_se_deja_vacio()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        var p = new ProveedorReconocimientoCompra(bd);
        var r = Assert.Single(await p.ListarAsync(_c));
        Assert.True(r.EsNuevo);
        r["UsarTextract"] = true;
        r["RegionAWS"] = "eu-west-1";
        r["ClaveAccesoAWS"] = "AKIAPRUEBA";
        Assert.Contains(p.Validar(r), e => e.Campo == "SecretoAWS");
        r["SecretoAWS"] = "secreto-de-prueba";
        Assert.Empty(p.Validar(r));
        var guardado = (await p.GuardarAsync(r, _c)).Valor!;
        Assert.Equal((null, "Sí"), (guardado["SecretoAWS"], guardado["EstadoSecreto"]));
        var enBd = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT SecretoAWSProtegido FROM ConfiguracionReconocimientoFacturasCompra WHERE IdSociedad = ?", f => f.Texto("SecretoAWSProtegido"), "T01"));
        Assert.DoesNotContain("secreto-de-prueba", enBd);

        guardado["UsarTextract"] = true;
        Assert.Empty(p.Validar(guardado));
        await p.GuardarAsync(guardado, _c);
        var config = await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, "T01"));
        Assert.Equal(("eu-west-1", "AKIAPRUEBA", "secreto-de-prueba"), (config.CredencialesOcr!.Region, config.CredencialesOcr.ClaveAcceso, config.CredencialesOcr.Secreto));
        Assert.True(config.UsarOcr);

        await p.EjecutarAsync("quitarclaves", guardado, _c);
        config = await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, "T01"));
        Assert.Null(config.CredencialesOcr);
        Assert.Equal("No", Assert.Single(await p.ListarAsync(_c))["EstadoSecreto"]);
    }
}
