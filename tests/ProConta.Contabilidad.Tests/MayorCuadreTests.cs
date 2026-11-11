using ProConta.Contabilidad.Mayor;

namespace ProConta.Contabilidad.Tests;

/// <summary>Punteo y cuadre del Mayor (contabilidad/mayor, PR 2) sobre la fuente en memoria.</summary>
public sealed class MayorCuadreTests
{
    private static readonly DateTime Ahora = new(2025, 6, 30, 10, 0, 0);

    [Fact]
    public async Task SoloNoPunteadosOcultaLasFilasPunteadasYElSaldoPasaASerParcial()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        await s.VerCuentasAsync(["5720001"]);
        var cobro = s.Resultado.Filas.Single(f => f.IdAsiento == 7);
        fuente.Puntear(cobro.IdsApunte, true);
        await s.CargarAsync();
        Assert.True(s.Resultado.Filas.Single(f => f.IdAsiento == 7).Punteado);
        Assert.False(s.Resultado.EsParcial);

        s.FiltrarNoPunteados(true);
        Assert.DoesNotContain(s.Resultado.Filas, f => f.IdAsiento == 7);
        Assert.True(s.Resultado.EsParcial);
        DatosMayorSinteticos.ComprobarTotales(s.Resultado);

        // En Compacto, una factura con unos apuntes punteados y otros no sale con punteo parcial y no se oculta.
        s.FiltrarNoPunteados(false);
        await s.VerCuentasAsync(["4300002"]);
        var factura = s.Resultado.Filas.First(f => f.FacturaNormalizada == "2025001");
        fuente.Puntear([factura.IdsApunte[0]], true);
        await s.CargarAsync();
        s.CambiarVista(VistaMayor.Compacto);
        var compacta = s.Resultado.Filas.Single(f => f.FacturaNormalizada == "2025001");
        Assert.True(compacta.PunteoParcial);
        Assert.False(compacta.Punteado);
    }

    [Fact]
    public async Task UnaCuentaMarcadaComoCuadradaVuelveASinCuadrarSiCambiaSuSaldo()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        Assert.Equal(EstadoCuadre.NoCuadrada, s.CuadreDeLaVista());

        Assert.Equal(2, await s.MarcarCuadradasAsync(["4300001"], "Revisado con el cliente", "ronald", Ahora));
        Assert.Equal(EstadoCuadre.Cuadrada, s.CuadreDeLaVista());
        Assert.Equal(EstadoCuadre.Cuadrada, s.Catalogo.Single(c => c.Codigo == "4300001").Cuadre);
        Assert.Contains("Revisado con el cliente", s.Catalogo.Single(c => c.Codigo == "4300001").AyudaCuadre);
        // Se guarda el saldo de cada ejercicio, con su apertura: 605 € al cierre de 2024 y 5 € en 2025.
        Assert.Equal([605m, 5m], fuente.CuadresGuardados.OrderBy(c => c.IdSociedadEjercicio).Select(c => c.Saldo));
        // La cuenta de grupo resume a sus subcuentas: la 4300002 y la 4300003 no están cuadradas.
        Assert.Equal(EstadoCuadre.NoCuadrada, s.Catalogo.Single(c => c.Codigo == "430").Cuadre);

        // Entra un cobro en 2025: el saldo cambia y la cuenta ya no cuadra en ese ejercicio.
        fuente.Asiento(new(20, 2, new DateTime(2025, 5, 1), "Otro cobro", [new("5720001", 5m, 0m), new("4300001", 0m, 5m)]));
        await s.RefrescarCatalogoAsync();
        var cuenta = s.Catalogo.Single(c => c.Codigo == "4300001");
        Assert.Equal(EstadoCuadre.NoCuadrada, cuenta.Cuadre);
        Assert.Contains("ya no cuadra", cuenta.AyudaCuadre);
        Assert.Contains(SelectorCuentasMayor.Filtrar(s.Catalogo, new FiltroCuentasMayor(NoCuadradas: true)), c => c.Codigo == "4300001");

        Assert.Equal(2, await s.DesmarcarCuadradasAsync(["4300001"]));
        Assert.Empty(fuente.CuadresGuardados);
    }

    [Fact]
    public async Task ReiniciarElCuadreMarcaLasCuentasDentroDelUmbralYPuedeDesmarcarLasDemas()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        fuente.Umbral = 0.05m;
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        await s.MarcarCuadradasAsync(["5720001"], null, "ronald", Ahora);

        var (marcadas, quitadas, umbral) = await s.ReiniciarCuadreAsync(desmarcarResto: false, "ronald", Ahora);
        Assert.Equal(0.05m, umbral);
        Assert.Equal(0, quitadas);
        // En 2025, la 4300002 tiene 0,03 € (dentro del umbral); la 4300003 debe 200 € y no se marca.
        var cuadradas = fuente.CuadresGuardados.Select(c => c.Cuenta).ToHashSet();
        Assert.Contains("4300002", cuadradas);
        Assert.DoesNotContain("4300003", cuadradas);
        Assert.Contains("5720001", cuadradas); // la marcada a mano se queda
        Assert.True(marcadas > 0);

        (_, quitadas, _) = await s.ReiniciarCuadreAsync(desmarcarResto: true, "ronald", Ahora);
        Assert.Equal(1, quitadas);
        Assert.DoesNotContain(fuente.CuadresGuardados, c => c.Cuenta == "5720001");
    }
}
