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
    public async Task ReiniciarElCuadreMarcaLasCuentasDentroDelUmbralDesmarcaLasDemasYNoTocaLasNotas()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        fuente.Umbral = 5m;
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        // En 2025: 4300001 debe 5 €, 4300002 tiene 0,03 €, 4300003 debe -200 € y el banco 3.110 €.
        await s.GuardarCuadreAsync("5720001", true, "Banco conciliado con el extracto", "ronald", Ahora);
        await s.GuardarCuadreAsync("4300002", true, null, "ronald", Ahora);
        await s.GuardarCuadreAsync("4300003", false, "Recibo devuelto", "ronald", Ahora);
        Assert.Equal(3, fuente.CuadresGuardados.Count);

        var p = await s.ProponerReinicioCuadreAsync("ana", Ahora);
        Assert.Equal(5m, p.Umbral);
        Assert.Equal(2025, p.Trabajo!.Ejercicio);
        // La 4300002 ya está cuadrada con el mismo saldo: no cambia. La propuesta no graba nada.
        Assert.Equal(1, p.SiguenCuadradas);
        Assert.Equal(3, fuente.CuadresGuardados.Count);
        Assert.Equal(["4300001", "5720001"], p.Cambios.Select(c => c.Cuenta));
        var cliente = p.Cambios[0];
        Assert.Equal(("Se marca", "Cliente uno", 5m), (cliente.Cambio, cliente.Nombre, cliente.Saldo));
        var banco = p.Cambios[1];
        Assert.Equal("Se desmarca", banco.Cambio);
        Assert.Equal((1, 1), (p.SeMarcan, p.SeDesmarcan));

        await s.GuardarReinicioCuadreAsync(p);
        Assert.Equal(4, fuente.CuadresGuardados.Count);
        var b = fuente.CuadresGuardados.Single(c => c.Cuenta == "5720001");
        Assert.False(b.Cuadrada);
        Assert.Null(b.Saldo);
        Assert.Equal(("Banco conciliado con el extracto", "ana"), (b.Notas, b.Operador));
        var c1 = fuente.CuadresGuardados.Single(c => c.Cuenta == "4300001");
        Assert.True(c1.Cuadrada);
        Assert.Equal(5m, c1.Saldo);
        Assert.Equal(EstadoCuadre.Cuadrada, s.Catalogo.Single(c => c.Codigo == "4300001").Cuadre);
        // La 4300003 (fuera del umbral, sin la casilla) no cambia y conserva sus notas.
        Assert.DoesNotContain(p.Cambios, c => c.Cuenta == "4300003");
        Assert.Equal("Recibo devuelto", fuente.CuadresGuardados.Single(c => c.Cuenta == "4300003").Notas);
        // Una cuenta sin movimientos en el ejercicio no se marca.
        Assert.DoesNotContain(p.Cambios, c => c.Cuenta == "5720009");

        // Repetirlo sin cambios no propone nada; si el saldo de una cuadrada cambia sin salir del umbral, se actualiza su saldo.
        Assert.Empty((await s.ProponerReinicioCuadreAsync("ana", Ahora)).Cambios);
        fuente.Asiento(new(30, 2, new DateTime(2025, 6, 1), "Redondeo", [new("4300002", 0.01m, 0m), new("7000001", 0m, 0.01m)]));
        var p2 = await s.ProponerReinicioCuadreAsync("ana", Ahora);
        var actualizada = Assert.Single(p2.Cambios);
        Assert.Equal(("4300002", "Se actualiza", 0.04m), (actualizada.Cuenta, actualizada.Cambio, actualizada.Nuevo.Saldo!.Value));
    }

    [Fact]
    public async Task ReiniciarElCuadreConElUmbralPredeterminadoSoloMarcaLosCentimos()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        // Notas del ejercicio anterior: al marcarla en el de trabajo, se copian.
        await fuente.GuardarCuadresAsync([new CuadreCuenta(1, "4300002", false, null, "2024: revisar remesa", "ronald", Ahora)]);
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        var p = await s.ProponerReinicioCuadreAsync("ana", Ahora);
        var unico = Assert.Single(p.Cambios);
        Assert.Equal(("4300002", true, 0.03m), (unico.Cuenta, unico.Marcar, unico.Saldo));
        Assert.Equal(("2024: revisar remesa", 2), (unico.Nuevo.Notas, unico.Nuevo.IdSociedadEjercicio));
    }
}
