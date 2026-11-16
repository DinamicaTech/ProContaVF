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
    public async Task ElCuadreDelEjercicioDeTrabajoVaDeRojoAAmbarYVerdeYVuelveAAmbarSiCambiaElSaldo()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        Assert.Equal(EstadoCuadre.SinCuadrar, s.CuadreDeLaVista().Estado);

        await s.GuardarCuadreAsync("4300001", false, "Falta el cobro de la factura 2024002", "ronald", Ahora);
        Assert.Equal(EstadoCuadre.EnCurso, s.CuadreDeLaVista().Estado);

        await s.GuardarCuadreAsync("4300001", true, "Falta el cobro de la factura 2024002\nCobrado en enero", "ronald", Ahora);
        var v = s.CuadreDeLaVista().Cuenta!;
        Assert.Equal(EstadoCuadre.Cuadrada, v.Estado);
        // Solo el ejercicio de trabajo (2025), con su saldo: 5 €.
        var guardado = Assert.Single(fuente.CuadresGuardados);
        Assert.Equal((2, 5m), (guardado.IdSociedadEjercicio, guardado.Saldo!.Value));
        // La cuenta de grupo resume a sus subcuentas: la 4300002 y la 4300003 están sin cuadrar.
        Assert.Equal(EstadoCuadre.SinCuadrar, s.Catalogo.Single(c => c.Codigo == "430").Cuadre);

        // Entra un cobro en 2025: el saldo cambia y la cuenta pasa a ámbar, con el aviso y sus notas.
        fuente.Asiento(new(20, 2, new DateTime(2025, 5, 1), "Otro cobro", [new("5720001", 5m, 0m), new("4300001", 0m, 5m)]));
        await s.RefrescarCatalogoAsync();
        v = s.CuadreDeLaVista().Cuenta!;
        Assert.Equal(EstadoCuadre.EnCurso, v.Estado);
        Assert.Contains("ha cambiado", v.Aviso);
        Assert.Contains("Cobrado en enero", v.Notas);
        Assert.Contains(SelectorCuentasMayor.Filtrar(s.Catalogo, new FiltroCuentasMayor(NoCuadradas: true)), c => c.Codigo == "4300001");
    }

    [Fact]
    public async Task LasNotasSeHeredanDelEjercicioAnteriorHastaQueElDeTrabajoTengaSuCuadre()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var en2024 = new SesionMayor(fuente, DatosMayorSinteticos.Sociedad);
        await en2024.IniciarAsync(2024);
        await en2024.GuardarCuadreAsync("4300001", true, "2024: factura 2024002 pendiente", "ronald", Ahora);

        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        await s.VerCuentasAsync(["4300001"]);
        var v = s.CuadreDeLaVista().Cuenta!;
        Assert.Equal(EstadoCuadre.EnCurso, v.Estado);
        Assert.True(v.Heredadas);
        Assert.Equal("2024: factura 2024002 pendiente", v.Notas);
    }

    [Fact]
    public async Task ReiniciarElCuadreMarcaLasCuentasDentroDelUmbralYSoloQuitaCasillasNoNotas()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        fuente.Umbral = 0.05m;
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        await s.GuardarCuadreAsync("5720001", true, "Banco conciliado con el extracto", "ronald", Ahora);

        var (cambiados, umbral) = await s.ReiniciarCuadreAsync(desmarcarResto: false, "ronald", Ahora);
        Assert.Equal(0.05m, umbral);
        Assert.Equal(1, cambiados);
        // En 2025, la 4300002 tiene 0,03 € (dentro del umbral); la 4300003 debe 200 € y no se marca; el banco sigue cuadrado.
        Assert.True(fuente.CuadresGuardados.Single(c => c.Cuenta == "4300002").Cuadrada);
        Assert.DoesNotContain(fuente.CuadresGuardados, c => c.Cuenta == "4300003");
        Assert.True(fuente.CuadresGuardados.Single(c => c.Cuenta == "5720001").Cuadrada);

        await s.ReiniciarCuadreAsync(desmarcarResto: true, "ronald", Ahora);
        var banco = fuente.CuadresGuardados.Single(c => c.Cuenta == "5720001");
        Assert.False(banco.Cuadrada);
        Assert.Equal("Banco conciliado con el extracto", banco.Notas);
        Assert.True(fuente.CuadresGuardados.Single(c => c.Cuenta == "4300002").Cuadrada);
    }
}