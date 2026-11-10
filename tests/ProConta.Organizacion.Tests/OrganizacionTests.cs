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
        Assert.Equal(BdPruebas.FormasPagoSinteticas.Count + 1, (await catalogo.ListarAsync()).Count); // + la 1 «Indefinida»
        Assert.Equal("Indefinida", (await catalogo.ObtenerAsync(1))!.Descripcion);

        var alta = await catalogo.AltaAsync("  Bizum  ", false);
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        Assert.Equal("Bizum", alta.Valor.Descripcion);
        Assert.Equal(BdPruebas.FormasPagoSinteticas.Max(f => f.Codigo) + 1, alta.Valor.Id);

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
    public async Task LaSubcuentaDeUnaFormaDePagoSeGuardaPorIdentidadYSeMuestraConElCodigoDelEjercicio()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(bd, 9002, "T01", 2026);
        var identidad = await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, Naturaleza, FechaAlta) VALUES (?, ?, ?)", "T01", (byte)0, DateTime.Today);
            var id = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                id, 9002, "5720001", "Banco sintético", true, false, true, 0m, 0m, 0);
            await s.EjecutarAsync("INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                id, 9001, "5720009", "Banco sintético antes", true, false, true, 0m, 0m, 0);
            return id;
        });
        var catalogo = new CatalogoFormasPagoSociedad(bd);
        var t2026 = new Nucleo.Contexto.ContextoSociedadEjercicio("T01", 9002, 2026);
        var t2025 = new Nucleo.Contexto.ContextoSociedadEjercicio("T01", 9001, 2025);

        var todas = await catalogo.ListarAsync(t2026);
        Assert.Equal(BdPruebas.FormasPagoSinteticas.Count + 1, todas.Count);
        Assert.All(todas, f => Assert.Null(f.Cuenta));

        Assert.Equal("FormasPago.CuentaNoExiste", (await catalogo.AsignarAsync(1, "5729999", t2026)).Error!.Codigo);
        Assert.True((await catalogo.AsignarAsync(1, "5720001", t2026)).EsCorrecto);
        var una = (await catalogo.ListarAsync(t2026)).Single(f => f.IdFormaPago == 1);
        Assert.Equal((identidad, "5720001", "Banco sintético", true), (una.IdIdentidadContable!.Value, una.Cuenta!, una.NombreCuenta!, una.CuentaEnEjercicio));
        // En otro ejercicio la misma identidad tiene otro código.
        Assert.Equal("5720009", (await catalogo.ListarAsync(t2025)).Single(f => f.IdFormaPago == 1).Cuenta);

        Assert.True((await catalogo.AsignarAsync(1, "", t2026)).EsCorrecto);
        Assert.Null((await catalogo.ListarAsync(t2026)).Single(f => f.IdFormaPago == 1).Cuenta);
    }

    [Fact]
    public async Task LosTiposDeRetencionVigentesVienenDeLaMigracionYRespetanLaVigencia()
    {
        var bd = _copia.Bd();
        var catalogo = new CatalogoTiposRetencion(bd);
        var hoy = await catalogo.VigentesAsync(new DateTime(2026, 11, 6));
        Assert.Equal([19m, 15m, 7m, 2m, 1m], hoy.Select(t => t.Porcentaje));
        Assert.Equal((byte)2, hoy[0].Clase);
        Assert.All(hoy.Skip(1), t => Assert.Equal((byte)1, t.Clase));

        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE TiposRetencion SET VigenteHasta = ? WHERE Porcentaje = 7", new DateTime(2026, 12, 31)));
        await bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO TiposRetencion (Porcentaje, Descripcion, Clase, VigenteDesde, Activo) VALUES (?, ?, ?, ?, ?)",
            8m, "Inicio de actividad (nuevo)", (byte)1, new DateTime(2027, 1, 1), true));
        Assert.DoesNotContain(8m, (await catalogo.VigentesAsync(new DateTime(2026, 12, 31))).Select(t => t.Porcentaje));
        var en2027 = (await catalogo.VigentesAsync(new DateTime(2027, 1, 1))).Select(t => t.Porcentaje).ToList();
        Assert.Contains(8m, en2027);
        Assert.DoesNotContain(7m, en2027);
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

/// <summary>Trimestre contable activo (compras/pantallas 2026-11-06 12:10).</summary>
public class TrimestreActivoTests
{
    [Fact]
    public void Nombres_y_aviso_fuera_del_trimestre_activo()
    {
        Assert.Equal("jul-sep", ServicioTrimestreActivo.Nombre(3));
        Assert.True(ServicioTrimestreActivo.FueraDelActivo(3, 2026, new DateTime(2026, 10, 10)));
        Assert.False(ServicioTrimestreActivo.FueraDelActivo(4, 2026, new DateTime(2026, 10, 10)));
        Assert.False(ServicioTrimestreActivo.FueraDelActivo(4, 2026, new DateTime(2027, 1, 5)));
        Assert.False(ServicioTrimestreActivo.FueraDelActivo(null, 2026, new DateTime(2026, 10, 10)));
    }

    [Fact]
    public void El_anterior_solo_se_reactiva_si_hoy_mas_diez_dias_cae_en_el_activo()
    {
        Assert.True(ServicioTrimestreActivo.PuedeReactivar(4, 2026, new DateTime(2026, 9, 25)));
        Assert.True(ServicioTrimestreActivo.PuedeReactivar(4, 2026, new DateTime(2026, 10, 15)));
        Assert.False(ServicioTrimestreActivo.PuedeReactivar(4, 2026, new DateTime(2026, 9, 10)));
        Assert.False(ServicioTrimestreActivo.PuedeReactivar(1, 2026, new DateTime(2026, 1, 15)));
    }
}

public class CuentasIvaRepercutidoTests
{
    [Fact]
    public void Sin_cuenta_de_repercutido_las_ventas_usan_la_477_con_la_terminacion_de_la_472()
    {
        Assert.Equal("4770021", LectorConfiguracionEjercicio.RepercutidoDe("4720021"));
        Assert.Equal("4770001", LectorConfiguracionEjercicio.RepercutidoDe(" 4720001 "));
        Assert.Equal("4770010", LectorConfiguracionEjercicio.RepercutidoDe("4770010"));
        Assert.Null(LectorConfiguracionEjercicio.RepercutidoDe(null));
    }
}
