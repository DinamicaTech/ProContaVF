using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Programadas;

namespace ProConta.Facturacion.Tests;

/// <summary>Calendario y reglas de las programadas, sin BD (RF-PRO-01, 02, 06).</summary>
public sealed class ProgramadasTests
{
    private static DateTime D(int a, int m, int d) => new(a, m, d);

    [Theory]
    // RF-PRO-02: del 1 al 28 es fijo; 29 a 31 cuentan desde el final del mes.
    [InlineData(31, 2026, 1, 31)]
    [InlineData(31, 2026, 2, 28)]
    [InlineData(31, 2026, 3, 31)]
    [InlineData(31, 2026, 4, 30)]
    [InlineData(29, 2026, 1, 29)]
    [InlineData(29, 2026, 2, 26)]
    [InlineData(29, 2026, 3, 29)]
    [InlineData(30, 2026, 2, 27)]
    [InlineData(30, 2026, 4, 29)]
    [InlineData(28, 2026, 2, 28)]
    [InlineData(5, 2026, 2, 5)]
    // Bisiestos.
    [InlineData(31, 2028, 2, 29)]
    [InlineData(29, 2028, 2, 27)]
    [InlineData(28, 2028, 2, 28)]
    public void AnclaDelDia(int dia, int anio, int mes, int esperado) =>
        Assert.Equal(D(anio, mes, esperado), CalendarioProgramacion.Anclar(D(anio, mes, 1), dia));

    [Fact]
    public void LasEmisionesSeCalculanDesdeLaPrimeraYNoArrastranFebrero()
    {
        // 31 ene → 28 feb → 31 mar (no 28 mar); 29 ene → 26 feb → 29 mar.
        Assert.Equal([D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 31), D(2026, 4, 30)],
            Enumerable.Range(0, 4).Select(i => CalendarioProgramacion.Emision(D(2026, 1, 1), 31, Periodicidad.Mensual, i)));
        Assert.Equal([D(2026, 1, 29), D(2026, 2, 26), D(2026, 3, 29)],
            Enumerable.Range(0, 3).Select(i => CalendarioProgramacion.Emision(D(2026, 1, 1), 29, Periodicidad.Mensual, i)));
    }

    [Fact]
    public void SaltosDeVariosMesesYDeAnio()
    {
        Assert.Equal([D(2026, 11, 29), D(2027, 2, 27), D(2027, 5, 30), D(2027, 8, 30)],
            Enumerable.Range(0, 4).Select(i => CalendarioProgramacion.Emision(D(2026, 11, 1), 30, Periodicidad.Trimestral, i)));
        Assert.Equal([D(2027, 12, 31), D(2028, 2, 29), D(2028, 4, 30)],
            Enumerable.Range(0, 3).Select(i => CalendarioProgramacion.Emision(D(2027, 12, 1), 31, Periodicidad.Bimestral, i)));
        Assert.Equal(D(2028, 2, 29), CalendarioProgramacion.Emision(D(2027, 2, 1), 31, Periodicidad.Anual, 1));
        Assert.Equal(D(2027, 3, 15), CalendarioProgramacion.Emision(D(2026, 3, 1), 15, Periodicidad.Cuatrimestral, 3));
        Assert.Equal(D(2027, 1, 10), CalendarioProgramacion.Emision(D(2026, 1, 1), 10, Periodicidad.Semestral, 2));
    }

    [Fact]
    public void LaPrimeraEmisionEsElDiaDeEmisionDesdeElInicio()
    {
        Assert.Equal(D(2026, 11, 5), CalendarioProgramacion.PrimeraEmision(D(2026, 11, 4), 5));
        Assert.Equal(D(2026, 12, 1), CalendarioProgramacion.PrimeraEmision(D(2026, 11, 4), 1));
        Assert.Equal(D(2026, 11, 4), CalendarioProgramacion.PrimeraEmision(D(2026, 11, 4), 4));
        Assert.Equal(D(2026, 11, 4), CalendarioProgramacion.PrimeraEmision(D(2026, 11, 4), null));
        Assert.Equal(D(2027, 2, 28), CalendarioProgramacion.PrimeraEmision(D(2027, 2, 1), 31));
    }

    [Fact]
    public void ElPeriodoAvanzaConservandoElFinalDeMes()
    {
        var p = new FacturaProgramada { FechaInicio = D(2026, 1, 1), DiaEmision = 1, Periodicidad = Periodicidad.Mensual };
        p.Plantilla.FechaInicioValidez = D(2026, 1, 1);
        p.Plantilla.FechaFinValidez = D(2026, 1, 31);
        Assert.Equal((D(2026, 2, 1), D(2026, 2, 28)), p.Periodo(1));
        Assert.Equal((D(2026, 3, 1), D(2026, 3, 31)), p.Periodo(2));
        Assert.Equal((D(2027, 1, 1), D(2027, 1, 31)), p.Periodo(12));

        // Estilo del ERP: «hasta» es el primer día del periodo siguiente.
        p.Plantilla.FechaFinValidez = D(2026, 2, 1);
        Assert.Equal((D(2026, 4, 1), D(2026, 5, 1)), p.Periodo(3));
    }

    [Fact]
    public void FinalPorRepeticionesPorFechaYPuntual()
    {
        var p = new FacturaProgramada { FechaInicio = D(2026, 1, 1), DiaEmision = 1, Periodicidad = Periodicidad.Mensual, NumeroRepeticiones = 3 };
        Assert.True(p.ExisteVencimiento(2));
        Assert.False(p.ExisteVencimiento(3));
        Assert.Equal([D(2026, 1, 1), D(2026, 2, 1), D(2026, 3, 1)], p.ProximasEmisiones(10));

        p.NumeroRepeticiones = null;
        p.FechaFin = D(2026, 4, 15);
        Assert.Equal([D(2026, 1, 1), D(2026, 2, 1), D(2026, 3, 1), D(2026, 4, 1)], p.ProximasEmisiones(10));
        p.FacturasGeneradas = 4;
        Assert.True(p.Finalizada);
        Assert.Null(p.ProximaCalculada);
        Assert.Equal("Finalizada", p.TextoEstado);

        var puntual = new FacturaProgramada { FechaInicio = D(2026, 6, 10), Periodicidad = Periodicidad.Puntual, Activa = true };
        Assert.Equal([D(2026, 6, 10)], puntual.ProximasEmisiones(5));
        Assert.Equal("Activa", puntual.TextoEstado);
        Assert.Equal("Una sola factura", ReglasProgramada.TextoFinal(puntual));
    }

    [Fact]
    public void ActivarExigeLosDatosDeUnaFacturaYUnaProgramacionCoherente()
    {
        var p = new FacturaProgramada { Activa = true, DiaEmision = 40, NumeroRepeticiones = 0 };
        var codigos = ReglasProgramada.ValidarParaActivar(p).Select(i => i.Codigo).ToList();
        Assert.Contains("Facturas.SinCliente", codigos);
        Assert.Contains("Facturas.SinDescripcion", codigos);
        Assert.Contains("Facturas.SinLineas", codigos);
        Assert.Contains("Programadas.SinInicio", codigos);
        Assert.Contains("Programadas.DiaEmision", codigos);
        Assert.Contains("Programadas.Repeticiones", codigos);
        Assert.DoesNotContain("Facturas.NoEsBorrador", codigos);

        var fin = new FacturaProgramada { FechaInicio = D(2026, 5, 10), DiaEmision = 1, FechaFin = D(2026, 5, 20) };
        Assert.Contains(ReglasProgramada.ValidarProgramacion(fin), i => i.Codigo == "Programadas.SinVencimientos");
    }

    [Fact]
    public void LaPeriodicidadSeLeeDelCodigoGuardado()
    {
        Assert.Equal(Periodicidad.Anual, Periodicidad.De("ANUAL"));
        Assert.Equal(Periodicidad.Cuatrimestral, Periodicidad.De("cuatrimestral "));
        Assert.Equal(Periodicidad.Mensual, Periodicidad.De(null));
        Assert.All(Periodicidad.Todas, x => Assert.True(x.Codigo.Length <= 15));
    }

    [Fact]
    public void LaLineaDeLaPlantillaSeCopiaSinFechaYRecalculada()
    {
        var l = new LineaFacturaVenta { Nombre = "Renta", PrecioUnitario = 1000m, Fecha = D(2026, 1, 1), TipoIva = 21m };
        l.Recalcular();
        var c = LineasProgramada.Copiar(l);
        Assert.Null(c.Fecha);
        Assert.Equal(1210m, c.ImporteTotal.Valor);
        Assert.NotSame(l, c);
    }

    [Fact]
    public void LosMarcadoresDeFechaSeSustituyenPorElPeriodo()
    {
        Assert.Equal("Alquiler local 01/08/2026 - 31/08/2026",
            MarcadoresPeriodo.Sustituir("Alquiler local %FECHAINICIO% - %FECHAFIN%", D(2026, 8, 1), D(2026, 8, 31)));
        Assert.Equal("Sin periodo  - ", MarcadoresPeriodo.Sustituir("Sin periodo %fechainicio% - %FECHAFIN%", null, null));
        Assert.Equal("", MarcadoresPeriodo.Sustituir(null, null, null));
    }

    [Fact]
    public void LaLineaDeUnaFacturaDelErpConservaSuTotalAlProgramarla()
    {
        // Como las del ERP: el precio es el total con IVA y el descuento fijo no es un descuento (1.002,00 + 210,42).
        var alquiler = new LineaFacturaVenta { Nombre = "Alquiler", Cantidad = 1m, PrecioUnitario = 1212.42m, DescuentoImporte = -1212.42m, TipoIva = 21m };
        alquiler.CargarImportes(1002m, 210.42m, 0m, 1212.42m);
        var c = LineasProgramada.DesdeLineaFactura(alquiler);
        Assert.Equal(1212.42m, c.ImporteTotal.Valor);
        Assert.Equal(1002m, c.BaseImponible.Valor);
        Assert.Equal(210.42m, c.CuotaIva.Valor);
        Assert.Equal(0m, c.DescuentoImporte);

        // La línea de retención: 0,19 × -1.002,00 con un 100 % que el ERP no aplicaba.
        var retencion = new LineaFacturaVenta { Nombre = "Retención", Cantidad = 0.19m, PrecioUnitario = -1002m, DescuentoPorcentaje = 100m, DescuentoImporte = 1002m, TipoIva = 0m };
        retencion.CargarImportes(-190.38m, 0m, 0m, -190.38m);
        var r = LineasProgramada.DesdeLineaFactura(retencion);
        Assert.Equal(-190.38m, r.ImporteTotal.Valor);
        Assert.Equal(0m, r.DescuentoPorcentaje);

        // Lo que al recalcular es igual no se toca.
        var normal = new LineaFacturaVenta { Nombre = "Cuota", Cantidad = 2m, PrecioUnitario = 50m, DescuentoPorcentaje = 10m, TipoIva = 21m };
        normal.Recalcular();
        var n = LineasProgramada.DesdeLineaFactura(normal);
        Assert.Equal(10m, n.DescuentoPorcentaje);
        Assert.Equal(normal.ImporteTotal, n.ImporteTotal);

        var t = CalculoFactura.Totales([c, r], 0m);
        Assert.Equal(1022.04m, t.ImporteTotal.Valor);
        Invariantes.Totales(t);
    }

    [Fact]
    public void UnaPlantillaConDescuentosDelErpNoSeActiva()
    {
        var p = new FacturaProgramada { FechaInicio = D(2026, 1, 1), DiaEmision = 1 };
        p.Plantilla.AnadirLinea(new LineaFacturaVenta { Nombre = "Alquiler", PrecioUnitario = 1212.42m, DescuentoImporte = -1212.42m });
        p.Plantilla.AnadirLinea(new LineaFacturaVenta { Nombre = "Retención", Cantidad = 0.19m, PrecioUnitario = -1002m, DescuentoPorcentaje = 100m });
        p.Plantilla.AnadirLinea(new LineaFacturaVenta { Nombre = "Normal", PrecioUnitario = 100m, DescuentoPorcentaje = 10m, DescuentoImporte = 5m });
        var malas = ReglasProgramada.ValidarParaActivar(p).Where(i => i.Codigo == "Programadas.DescuentoNoValido").ToList();
        Assert.Equal(["Linea1.DescuentoImporte", "Linea2.DescuentoImporte"], malas.Select(i => i.Campo));
    }
}
