using ProConta.Organizacion.FormasPago;
using ProConta.Organizacion.Geografia;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;

namespace ProConta.Organizacion.Tests;

public class GeografiaTests
{
    private static readonly CatalogoGeografico _catalogo = new(
    [
        new("ESP", "28001", "Madrid", "Madrid"),
        new("ESP", "28001", "MADRID", "Madrid"),
        new("ESP", "08001", "Barcelona", "Barcelona"),
        new("ESP", "03001", "Alicante", "Alicante"),
        new("ESP", "03001", "Alicante", "Alacant"),
        new(null, "99999", "Provincia A", "Pueblo A"),
        new(null, "99999", "Provincia B", "Pueblo B"),
        new("ESP", " ", "Nada", "Nada"),
    ]);

    [Fact]
    public void CpConUnaSolaProvinciaLaSelecciona()
    {
        var r = _catalogo.Resolver(" 28001 ");
        Assert.Equal("Madrid", r.ProvinciaUnica);
        Assert.Equal("Madrid", r.PoblacionUnica);
    }

    [Fact]
    public void CpAmbiguoDejaElegir()
    {
        var poblaciones = _catalogo.Resolver("03001");
        Assert.Equal("Alicante", poblaciones.ProvinciaUnica);
        Assert.Null(poblaciones.PoblacionUnica);
        Assert.Equal(2, poblaciones.Poblaciones.Count);

        var provincias = _catalogo.Resolver("99999");
        Assert.Null(provincias.ProvinciaUnica);
        Assert.Equal(["Provincia A", "Provincia B"], provincias.Provincias);
    }

    [Fact]
    public void SinCorrespondenciaNoInventaNada()
    {
        var r = _catalogo.Resolver("46001");
        Assert.True(r.SinCorrespondencia);
        Assert.Null(r.ProvinciaUnica);
    }

    [Fact]
    public void ListasDeProvinciasYPoblaciones()
    {
        Assert.Contains("Barcelona", _catalogo.Provincias("ESP"));
        Assert.DoesNotContain("Nada", _catalogo.Provincias());
        Assert.Equal(["Alacant", "Alicante"], _catalogo.Poblaciones("alicante"));
    }
}

[Trait("Categoria", "Access")]
public sealed class OrganizacionAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task ContextoValidadoYEjercicioCerrado()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética uno");
        await DatosSinteticos.SociedadAsync(bd, "T02", "Sociedad sintética dos", "Q1234567D");
        await DatosSinteticos.EjercicioAsync(bd, 9001, "T01", 2025, cerrado: true);
        await DatosSinteticos.EjercicioAsync(bd, 9002, "T01", 2026);
        var servicio = new ServicioSociedadesAccess(bd);

        Assert.Equal(["T02", "T01"], (await servicio.ListarSociedadesAsync()).Select(x => x.IdSociedad)); // por razón social
        Assert.Equal([2026, 2025], (await servicio.ListarEjerciciosAsync("T01")).Select(e => e.Ejercicio));

        var contexto = await servicio.ObtenerContextoAsync("T01", 9002);
        Assert.Equal(new Nucleo.Contexto.ContextoSociedadEjercicio("T01", 9002, 2026), contexto.Valor);
        Assert.Equal("Organizacion.ContextoNoValido", (await servicio.ObtenerContextoAsync("T02", 9002)).Error!.Codigo);

        Assert.True((await servicio.ComprobarEjercicioAbiertoAsync(contexto.Valor)).EsCorrecto);
        var cerrado = await servicio.ComprobarEjercicioAbiertoAsync(new("T01", 9001, 2025));
        Assert.Equal("Organizacion.EjercicioCerrado", cerrado.Error!.Codigo);
        Assert.Contains("reabre", cerrado.Error.AccionCorrectora);
    }

    [Fact]
    public async Task FormasPagoAltaModificacionYBajaRestringidaPorUso()
    {
        var bd = _copia.Bd();
        var catalogo = new CatalogoFormasPagoAccess(bd);
        Assert.Equal(BdPruebas.FormasPagoSinteticas.Count, (await catalogo.ListarAsync()).Count);

        var alta = await catalogo.AltaAsync("  Bizum  ", false);
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        Assert.Equal("Bizum", alta.Valor.Descripcion);
        Assert.Equal(BdPruebas.FormasPagoSinteticas.Max(f => f.Codigo) + 1, alta.Valor.Id);

        Assert.Equal("FormasPago.Repetida", (await catalogo.AltaAsync("BIZUM", false)).Error!.Codigo);
        Assert.Equal("FormasPago.SinDescripcion", (await catalogo.AltaAsync(" ", false)).Error!.Codigo);

        Assert.True((await catalogo.ModificarAsync(alta.Valor with { Sepa = true })).EsCorrecto);
        Assert.True((await catalogo.ObtenerAsync(alta.Valor.Id))!.Sepa);

        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET FormaPagoDefecto = ? WHERE IdSociedad = ?", (int)alta.Valor.Id, "T01"));
        var enUso = await catalogo.BajaAsync(alta.Valor.Id);
        Assert.Equal("FormasPago.EnUso", enUso.Error!.Codigo);
        Assert.Contains("1 sociedades", enUso.Error.Causa);

        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET FormaPagoDefecto = NULL"));
        Assert.True((await catalogo.BajaAsync(alta.Valor.Id)).EsCorrecto);
        Assert.Null(await catalogo.ObtenerAsync(alta.Valor.Id));
    }

    [Fact]
    public async Task GeografiaSeCargaDeLosDomiciliosDeLaBd()
    {
        var bd = _copia.Bd();
        await bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO Terceros (Nombre, CodigoPostal, Provincia, Poblacion, Pais, Activo) VALUES (?, ?, ?, ?, ?, ?)",
            "Tercero sintético", "28001", "Madrid", "Madrid", "ESP", true));
        var catalogo = await new FuenteGeografiaAccess(bd).CargarAsync();
        Assert.Equal("Madrid", catalogo.Resolver("28001").ProvinciaUnica);
        Assert.True(catalogo.Resolver("08001").SinCorrespondencia);
    }
}
