using ProConta.Contabilidad.Mayor;

namespace ProConta.Contabilidad.Tests;

/// <summary>Datos sintéticos del Mayor: dos ejercicios con apertura, facturas, cobros, una remesa y un cierre que no se ve.</summary>
internal static class DatosMayorSinteticos
{
    public const string Sociedad = "S01";

    public static FuenteMayorMemoria Fuente(decimal aperturaCliente2025 = 605m)
    {
        var f = new FuenteMayorMemoria(Sociedad).Ejercicio(1, 2024, cerrado: true).Ejercicio(2, 2025);
        foreach (var ej in new[] { 1, 2 })
        {
            f.Cuenta(ej, "430", "Clientes", imputable: false).Cuenta(ej, "4300001", "Cliente uno").Cuenta(ej, "4300002", "Cliente dos").Cuenta(ej, "4300003", "Cliente tres")
             .Cuenta(ej, "5720001", "Banco sintético").Cuenta(ej, "5720009", "Banco sin movimientos").Cuenta(ej, "7000001", "Ventas").Cuenta(ej, "4770021", "IVA repercutido")
             .Cuenta(ej, "1000000", "Capital").Cuenta(ej, "1290000", "Resultado");
        }
        DateTime D(int a, int m, int d) => new(a, m, d);
        f.Asiento(new(1, 1, D(2024, 1, 1), "Apertura 2024", [new("5720001", 1000m, 0m), new("1000000", 0m, 1000m)], Tipo: 1));
        f.Asiento(new(2, 1, D(2024, 2, 15), "Factura F2024001",
            [new("4300001", 1210m, 0m, "Factura F2024001", "F2024001"), new("7000001", 0m, 1000m, "Factura F2024001", "F2024001"), new("4770021", 0m, 210m)],
            FechaDocumento: D(2024, 2, 14), GrabadoEl: D(2024, 2, 16), ModificadoEl: D(2024, 3, 1)));
        f.Asiento(new(3, 1, D(2024, 3, 10), "Cobro factura 2024001", [new("5720001", 1210m, 0m), new("4300001", 0m, 1210m, null, "2024001")]));
        f.Asiento(new(4, 1, D(2024, 4, 20), "Factura F2024002",
            [new("4300001", 605m, 0m, null, "F2024002"), new("7000001", 0m, 300m), new("7000001", 0m, 200m), new("4770021", 0m, 105m)]));
        f.Asiento(new(5, 1, D(2024, 12, 31), "Cierre 2024",
            [new("4300001", 0m, 605m), new("5720001", 0m, 2210m), new("1000000", 1000m, 0m), new("7000001", 1500m, 0m), new("4770021", 315m, 0m)], Tipo: 3));
        f.Asiento(new(6, 2, D(2025, 1, 1), "Apertura 2025",
            [new("5720001", 2210m, 0m), new("4300001", aperturaCliente2025, 0m), new("1000000", 0m, 1000m), new("1290000", 0m, 1815m - (605m - aperturaCliente2025))], Tipo: 1));
        f.Asiento(new(7, 2, D(2025, 2, 5), "Cobro parcial 2024002", [new("5720001", 600m, 0m), new("4300001", 0m, 600m, null, "2024002")]));
        f.Asiento(new(8, 2, D(2025, 3, 10), "Remesa de recibos", [new("5720001", 300m, 0m), new("4300002", 0m, 100m, null, "F2025001"), new("4300003", 0m, 200m, null, "F2025002")]));
        f.Asiento(new(9, 2, D(2025, 4, 1), "Factura con céntimos", [new("4300002", 100.03m, 0m, null, "F2025001"), new("7000001", 0m, 100.03m)]));
        return f;
    }

    public static async Task<SesionMayor> SesionAsync(FuenteMayorMemoria fuente, params int[] anios)
    {
        var s = new SesionMayor(fuente, Sociedad);
        await s.IniciarAsync(2025, anios.Length > 0 ? anios : null);
        return s;
    }

    /// <summary>Invariante de toda la vista: los totales son la suma de lo que suma cada fila y el saldo final es el acumulado.</summary>
    public static void ComprobarTotales(ResultadoMayor r)
    {
        Assert.Equal(r.Filas.Where(f => !f.AperturaContinua).Sum(f => f.Debe), r.TotalDebe);
        Assert.Equal(r.Filas.Where(f => !f.AperturaContinua).Sum(f => f.Haber), r.TotalHaber);
        Assert.Equal(r.Saldo, r.SaldoFinal);
    }
}

public sealed class MayorTests
{
    [Theory]
    [InlineData("F2024001", "2024001")]
    [InlineData("2024001", "2024001")]
    [InlineData(" f-2025-0102 ", "2025-0102")]
    [InlineData("FRA-7", "FRA-7")]
    [InlineData(null, "")]
    public void LasFacturasConYSinFSonLaMisma(string? documento, string esperado) => Assert.Equal(esperado, Facturas.Normalizar(documento));

    [Fact]
    public async Task VariosEjerciciosLaAperturaDelSegundoContinuaElSaldoSinSumarYElCierreNoSeVe()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        var r = s.Resultado;
        Assert.Equal([2, 3, 4, 6, 7], r.Filas.Select(f => f.IdAsiento));
        var apertura = r.Filas.Single(f => f.EsApertura);
        Assert.True(apertura.AperturaContinua);
        Assert.Null(apertura.AvisoApertura);
        Assert.Equal(605m, apertura.Saldo); // el saldo con que acabó 2024
        Assert.Equal(605m, apertura.Debe);   // se ve, pero no suma
        Assert.Equal(1815m, r.TotalDebe);
        Assert.Equal(1810m, r.TotalHaber);
        Assert.Equal(5m, r.Saldo);
        Assert.Equal(4, r.NumeroApuntes);
        Assert.Null(r.AvisoParcial);
        Assert.True(r.Filas.First().EjercicioCerrado);
        Assert.False(r.Filas.Last().EjercicioCerrado);
        DatosMayorSinteticos.ComprobarTotales(r);
    }

    [Fact]
    public async Task ConUnSoloEjercicioLaAperturaSumaComoSaldoInicial()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2025);
        await s.VerCuentasAsync(["4300001"]);
        var r = s.Resultado;
        Assert.False(r.Filas[0].AperturaContinua);
        Assert.Equal(605m, r.TotalDebe);
        Assert.Equal(600m, r.TotalHaber);
        Assert.Equal(5m, r.Saldo);
        DatosMayorSinteticos.ComprobarTotales(r);
    }

    [Fact]
    public async Task UnaAperturaQueNoCoincideConElSaldoAnteriorSeAvisaEnSuFila()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(aperturaCliente2025: 600m), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        var apertura = s.Resultado.Filas.Single(f => f.EsApertura);
        Assert.Contains("no coincide", apertura.AvisoApertura);
        Assert.Equal(605m, apertura.Saldo);
    }

    [Fact]
    public async Task LaContrapartidaIgnoraElIvaYDiceVariasSiQuedanVarias()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        var factura = s.Resultado.Filas.First(f => f.IdAsiento == 2);
        Assert.Equal("7000001", factura.Contrapartida);
        Assert.Equal("Ventas", factura.NombreContrapartida);
        Assert.Equal([("7000001", -1000m), ("4770021", -210m)], factura.DetalleContrapartida.Select(d => (d.Cuenta, d.Importe)));

        await s.VerCuentasAsync(["5720001"]);
        var remesa = s.Resultado.Filas.Single(f => f.IdAsiento == 8);
        Assert.Equal(Contrapartidas.Varias, remesa.Contrapartida);
        Assert.Equal(["4300002", "4300003"], remesa.CuentasContrapartida);
        Assert.Equal(Contrapartidas.Ninguna, s.Resultado.Filas.First(f => f.EsApertura).Contrapartida);

        // El propio apunte de IVA se contrapone al lado contrario: el cliente.
        await s.VerCuentasAsync(["4770021"]);
        Assert.Equal("4300001", s.Resultado.Filas.First(f => f.IdAsiento == 2).Contrapartida);
    }

    [Fact]
    public async Task CompactoJuntaLaFacturaYSusCobrosYSoloPendientesOcultaLasQueNeteanACero()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        s.CambiarVista(VistaMayor.Compacto);
        var r = s.Resultado;
        Assert.Equal(2, r.Filas.Count); // F2024001 y F2024002; la apertura de 2025 no se repite
        Assert.Equal((1210m, 1210m, 2), (r.Filas[0].Debe, r.Filas[0].Haber, r.Filas[0].NumeroApuntes));
        Assert.Equal((605m, 600m), (r.Filas[1].Debe, r.Filas[1].Haber));
        Assert.Equal(5m, r.Saldo);
        DatosMayorSinteticos.ComprobarTotales(r);

        s.CambiarVista(VistaMayor.SoloPendientes);
        Assert.Equal(["2024002"], s.Resultado.Filas.Select(f => f.FacturaNormalizada));
        Assert.Equal(5m, s.Resultado.Saldo);

        // Una factura que netea a 0,03 € (dentro de ±0,05 €) también se oculta.
        await s.VerCuentasAsync(["4300002"]);
        s.CambiarVista(VistaMayor.SoloPendientes);
        Assert.Empty(s.Resultado.Filas);
    }

    [Fact]
    public async Task Agrupar700JuntaLasLineasDeLaMismaCuentaFacturaYLado()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024);
        await s.VerCuentasAsync(["7000001"]);
        Assert.Equal(3, s.Resultado.Filas.Count);
        s.CambiarVista(VistaMayor.Agrupar700);
        var r = s.Resultado;
        Assert.Equal(2, r.Filas.Count);
        Assert.Equal(500m, r.Filas.Single(f => f.IdAsiento == 4).Haber);
        Assert.Equal(1500m, r.TotalHaber);
        DatosMayorSinteticos.ComprobarTotales(r);
    }

    [Fact]
    public async Task FiltrarFacturaYQuitarFilasDanSaldoParcialConAvisoYSeQuitanConSuEtiqueta()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        s.FiltrarFacturas(["2024002"]);
        Assert.Equal([4, 7], s.Resultado.Filas.Select(f => f.IdAsiento));
        Assert.Equal(5m, s.Resultado.Saldo);
        Assert.Contains("Saldo parcial", s.Resultado.AvisoParcial);
        Assert.Equal(TipoEtiquetaMayor.FiltrarFactura, s.Etiquetas.Single().Tipo);
        DatosMayorSinteticos.ComprobarTotales(s.Resultado);

        await s.QuitarEtiquetaAsync(TipoEtiquetaMayor.FiltrarFactura);
        Assert.Null(s.Resultado.AvisoParcial);

        // Supr: quita la fila solo de la vista y recalcula el saldo acumulado de las siguientes.
        s.Quitar([s.Resultado.Filas.First(f => f.IdAsiento == 3).Clave]);
        Assert.Equal(4, s.Resultado.Filas.Count);
        Assert.Equal(1215m, s.Resultado.Saldo);
        Assert.Equal(1815m, s.Resultado.Filas.First(f => f.IdAsiento == 4).Saldo);
        Assert.Equal(1, s.Resultado.Quitadas);
        Assert.Contains("1 fila quitada", s.Resultado.AvisoParcial);
        await s.QuitarEtiquetaAsync(TipoEtiquetaMayor.FilasQuitadas);
        Assert.Equal(5m, s.Resultado.Saldo);
    }

    [Fact]
    public async Task BuscarFacturaEnTodasLasCuentasYVolverALaVista()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        await s.BuscarFacturasAsync(["F2024001"]);
        Assert.Equal(["4300001", "7000001", "4300001"], s.Resultado.Filas.Select(f => f.Cuenta));
        Assert.Equal(TipoEtiquetaMayor.BuscarFactura, s.Etiquetas.Single().Tipo);
        await s.QuitarEtiquetaAsync(TipoEtiquetaMayor.BuscarFactura);
        Assert.Equal(5, s.Resultado.Filas.Count);
    }

    [Fact]
    public async Task ElFiltroDeTextoBuscaConceptoAsientoEImporte()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        await s.VerCuentasAsync(["4300001"]);
        s.FiltrarTexto("cobro");
        Assert.Equal([3, 7], s.Resultado.Filas.Select(f => f.IdAsiento));
        s.FiltrarTexto("600,00 €");
        Assert.Equal([7], s.Resultado.Filas.Select(f => f.IdAsiento));
        s.FiltrarTexto("7");
        Assert.Equal([7], s.Resultado.Filas.Select(f => f.IdAsiento));
        s.FiltrarTexto(null);
        Assert.Null(s.Resultado.AvisoParcial);
    }

    [Fact]
    public async Task UnCodigoCortoMuestraTodasSusSubcuentasYUnaSubcuentaSoloElla()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2025);
        await s.VerCuentasAsync(["430"]);
        Assert.Equal(["4300001", "4300002", "4300003"], s.Resultado.Filas.Select(f => f.Cuenta).Distinct().OrderBy(x => x));
        Assert.Equal(["4300001", "4300002", "4300003"], s.CuentasDeLaVista().Select(c => c.Codigo));
        Assert.True(SelectorCuentasMayor.EsExacta("4300001", s.Catalogo));
        Assert.False(SelectorCuentasMayor.EsExacta("430", s.Catalogo));
    }

    [Fact]
    public async Task ElMayorCombinadoDeVariasCuentasVaEnOrdenDeFechaYSinColorSiMezclaGrupos()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2025);
        await s.VerCuentasAsync(["4300001", "5720001"]);
        var fechas = s.Resultado.Filas.Select(f => f.Fecha).ToList();
        Assert.Equal(fechas.OrderBy(x => x), fechas);
        Assert.All(s.Resultado.Filas, f => Assert.Equal("0", f.GrupoSaldo));
        await s.VerCuentasAsync(["4300001"]);
        Assert.All(s.Resultado.Filas, f => Assert.Equal("43", f.GrupoSaldo));
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2024, 2025)]
    public async Task TodasLasCuentasJuntasCuadranDebeConHaber(params int[] anios)
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), anios);
        await s.VerCuentasAsync(["1", "4", "5", "7"]);
        Assert.Equal(s.Resultado.TotalDebe, s.Resultado.TotalHaber);
        foreach (var v in Enum.GetValues<VistaMayor>().Where(v => v != VistaMayor.SoloPendientes))
        {
            s.CambiarVista(v);
            Assert.Equal(s.Resultado.TotalDebe, s.Resultado.TotalHaber);
            DatosMayorSinteticos.ComprobarTotales(s.Resultado);
        }
    }

    [Fact]
    public async Task ElSelectorCalculaSaldoYApuntesSobreLosEjerciciosElegidos()
    {
        var s = await DatosMayorSinteticos.SesionAsync(DatosMayorSinteticos.Fuente(), 2024, 2025);
        var banco = s.Catalogo.Single(c => c.Codigo == "5720001");
        Assert.Equal(3110m, banco.Saldo);           // apertura de 2024 + movimientos; la de 2025 continúa
        Assert.Equal(2110m, banco.SaldoSinApertura);
        Assert.Equal(4, banco.Apuntes);
        var clientes = s.Catalogo.Single(c => c.Codigo == "430");
        Assert.False(clientes.EsSubcuenta);
        Assert.Equal(5m + 0.03m - 200m, clientes.Saldo);

        var conMovimientos = SelectorCuentasMayor.Filtrar(s.Catalogo, new FiltroCuentasMayor());
        Assert.DoesNotContain(conMovimientos, c => c.Codigo == "5720009");
        Assert.DoesNotContain(conMovimientos, c => c.Codigo == "430");
        var todas = SelectorCuentasMayor.Filtrar(s.Catalogo, new FiltroCuentasMayor(SoloSubcuentas: false, ConMovimientos: false));
        Assert.Contains(todas, c => c.Codigo == "5720009");
        Assert.Equal(["4300001", "4300002", "4300003"], SelectorCuentasMayor.Filtrar(s.Catalogo, new FiltroCuentasMayor(ConSaldo: true, Texto: "4300")).Select(c => c.Codigo));
        Assert.Equal(["5720001"], SelectorCuentasMayor.Filtrar(s.Catalogo, new FiltroCuentasMayor(Texto: "banco")).Select(c => c.Codigo));

        // Solo 2025: la apertura de 2025 suma y el banco tiene solo esos movimientos.
        await s.ElegirEjerciciosAsync([2025]);
        Assert.Equal(3110m, s.Catalogo.Single(c => c.Codigo == "5720001").Saldo);
        Assert.Equal(900m, s.Catalogo.Single(c => c.Codigo == "5720001").SaldoSinApertura);
    }

    [Fact]
    public async Task CadaVistaHaceUnaSolaCargaYCambiarDeVistaNoVuelveALeer()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2024, 2025);
        await s.VerCuentasAsync(["430"]);
        Assert.Equal(1, fuente.Cargas);
        s.CambiarVista(VistaMayor.Compacto);
        s.FiltrarTexto("factura");
        s.Quitar([s.Resultado.Filas[0].Clave]);
        Assert.Equal(1, fuente.Cargas);
    }

    [Fact]
    public async Task UnErrorDeCargaLlegaAQuienLlamaSinPerderLaVistaAnterior()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var s = await DatosMayorSinteticos.SesionAsync(fuente, 2025);
        await s.VerCuentasAsync(["4300001"]);
        var antes = s.Resultado;
        fuente.FalloSiguiente = new InvalidOperationException("BD no disponible");
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.CargarAsync());
        Assert.Same(antes, s.Resultado);
    }

    /// <summary>Aceptación del nodo: el mayor embebido (ficha de terceros, filtrado por sus cuentas) coincide con el completo.</summary>
    [Fact]
    public async Task ElMayorEmbebidoCoincideConElCompletoBajoIgualesFiltros()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var completo = await DatosMayorSinteticos.SesionAsync(fuente, 2024, 2025);
        var embebido = await DatosMayorSinteticos.SesionAsync(fuente, 2024, 2025);
        string[] cuentasDelTercero = ["4300001", "4300002"];
        await completo.VerCuentasAsync(cuentasDelTercero);
        await embebido.VerCuentasAsync(cuentasDelTercero);
        foreach (var v in Enum.GetValues<VistaMayor>())
        {
            completo.CambiarVista(v);
            embebido.CambiarVista(v);
            completo.FiltrarTexto("factura");
            embebido.FiltrarTexto("factura");
            Assert.Equal(Huella(completo.Resultado), Huella(embebido.Resultado));
            completo.FiltrarTexto(null);
            embebido.FiltrarTexto(null);
            Assert.Equal(Huella(completo.Resultado), Huella(embebido.Resultado));
        }

        static string Huella(ResultadoMayor r) =>
            string.Join("\n", r.Filas.Select(f => $"{f.Clave}|{f.Cuenta}|{f.Fecha:d}|{f.Debe}|{f.Haber}|{f.Saldo}|{f.Contrapartida}")) + $"|{r.TotalDebe}|{r.TotalHaber}|{r.AvisoParcial}";
    }

    [Fact]
    public async Task SinCuentasNiEjerciciosLaVistaQuedaVacia()
    {
        var s = new SesionMayor(DatosMayorSinteticos.Fuente(), "OTRA");
        await s.IniciarAsync(2025);
        Assert.Empty(s.Ejercicios);
        await s.VerCuentasAsync(["4300001"]);
        Assert.Empty(s.Resultado.Filas);
        Assert.Equal(0m, s.Resultado.Saldo);
    }

    [Fact]
    public async Task VerAsientoEHistorialLeenDeLaFuente()
    {
        var fuente = DatosMayorSinteticos.Fuente();
        var a = await fuente.AsientoAsync(2);
        Assert.NotNull(a);
        Assert.Equal(a!.TotalDebe, a.TotalHaber);
        Assert.Equal("Cliente uno", a.Lineas[0].NombreCuenta);
        Assert.Null(await fuente.AsientoAsync(999));
    }
}
