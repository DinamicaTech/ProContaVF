using ProConta.Contabilidad.Asientos;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Contexto;

namespace ProConta.Facturacion.Tests;

public class CalculoTests
{
    private static LineaFacturaVenta Linea(decimal cantidad, decimal precio, decimal iva = 21m, decimal descPct = 0m, decimal descImp = 0m, bool suplido = false)
    {
        var l = new LineaFacturaVenta { Nombre = "Línea", Cantidad = cantidad, PrecioUnitario = precio, TipoIva = iva, DescuentoPorcentaje = descPct, DescuentoImporte = descImp, EsSuplido = suplido, CuentaContable = "7000001" };
        l.Recalcular();
        return l;
    }

    [Fact]
    public void DescuentoPorcentajePrimeroYDespuesFijo()
    {
        // 3 × 33,33 = 99,99; −10 % = 89,991; −5 = 84,991 → 84,99; IVA 21 % = 17,85 (17,8479).
        var l = Linea(3m, 33.33m, descPct: 10m, descImp: 5m);
        Assert.Equal(84.99m, l.BaseImponible.Valor);
        Assert.Equal(17.85m, l.CuotaIva.Valor);
        Assert.Equal(102.84m, l.ImporteTotal.Valor);
        Assert.Equal(l.ImporteTotal.Valor, l.BaseImponible.Valor + l.CuotaIva.Valor);
    }

    [Fact]
    public void RedondeoPorLineaYDespuesSuma()
    {
        // Dos líneas de 0,125 € de base: cada una redondea a 0,13 (AwayFromZero) y la suma es 0,26, no 0,25.
        var lineas = new[] { Linea(1m, 0.125m, iva: 0m), Linea(1m, 0.125m, iva: 0m) };
        Assert.Equal(0.26m, CalculoFactura.Totales(lineas, 0m).BaseImponible.Valor);
    }

    [Fact]
    public void SuplidoSinIvaNiRetencionPeroEnElTotal()
    {
        var lineas = new[] { Linea(1m, 1000m), Linea(1m, 50m, suplido: true) };
        var t = CalculoFactura.Totales(lineas, 19m);
        Assert.Equal(1000m, t.BaseImponible.Valor);
        Assert.Equal(210m, t.Iva.Valor);
        Assert.Equal(50m, t.Suplidos.Valor);
        Assert.Equal(1260m, t.ImporteBruto.Valor);
        Assert.Equal(190m, t.Retencion.Valor);
        Assert.Equal(1070m, t.ImporteTotal.Valor);
        Invariantes.Totales(t);
        Assert.Equal(0m, lineas[1].CuotaIva.Valor);
    }

    [Fact]
    public void EntradaInversaConservaElTotalExacto()
    {
        var l = new LineaFacturaVenta { Cantidad = 1m, TipoIva = 21m };
        l.FijarTotal(100m);
        Assert.Equal(100m, l.ImporteTotal.Valor);
        Assert.Equal(82.64m, l.BaseImponible.Valor);
        Assert.Equal(17.36m, l.CuotaIva.Valor);
        Assert.Equal(0m, l.DescuentoPorcentaje);
    }

    [Fact]
    public void DesglosePorTipoDeMayorAMenor()
    {
        var d = CalculoFactura.Desglose([Linea(1m, 100m, iva: 10m), Linea(1m, 200m), Linea(1m, 50m)]);
        Assert.Equal([21m, 10m], d.Select(x => x.TipoIva));
        Assert.Equal(250m, d[0].BaseImponible.Valor);
    }

    [Fact]
    public void CopiaInvertidaCambiaElSignoDeCantidadesEImportes()
    {
        var l = Linea(2m, 50m, descImp: 10m);
        var c = l.Copiar(invertir: true);
        Assert.Equal(-2m, c.Cantidad);
        Assert.Equal(50m, c.PrecioUnitario);
        Assert.Equal(-l.ImporteTotal.Valor, c.ImporteTotal.Valor);
        Assert.Equal(0, c.IdLineaFactura);
    }
}

public class ReglasFacturaTests
{
    private static FacturaVenta Borrador()
    {
        var f = new FacturaVenta { IdCliente = 1, Descripcion = "Alquiler de local", FechaFactura = new DateTime(2026, 3, 1) };
        var l = new LineaFacturaVenta { Nombre = "Renta marzo", PrecioUnitario = 500m, CuentaContable = "7520001" };
        l.Recalcular();
        f.AnadirLinea(l);
        return f;
    }

    [Fact]
    public void BorradorCompletoNoTieneIncidencias() => Assert.Empty(ReglasFactura.ValidarParaEmision(Borrador(), null));

    [Fact]
    public void RectificativaSinMotivoNoSeValida()
    {
        var f = Borrador();
        f.TipoFactura = "R4";
        Assert.Contains(ReglasFactura.ValidarParaEmision(f, null), i => i.Codigo == "Facturas.SinMotivo" && i.Campo == "MotivoRectificacion");
        f.MotivoRectificacion = "Precio equivocado";
        Assert.DoesNotContain(ReglasFactura.ValidarParaEmision(f, null), i => i.Codigo == "Facturas.SinMotivo");
        f.MotivoRectificacion = new string('x', ReglasFactura.LongitudMotivo + 1);
        Assert.Contains(ReglasFactura.ValidarParaEmision(f, null), i => i.Codigo == "Facturas.MotivoLargo");
    }

    [Fact]
    public void AcumulaTodosLosErroresALaVez()
    {
        var f = new FacturaVenta { FechaInicioValidez = new DateTime(2026, 3, 31), FechaFinValidez = new DateTime(2026, 3, 1) };
        var codigos = ReglasFactura.ValidarParaEmision(f, null).Select(i => i.Codigo).ToList();
        Assert.Contains("Facturas.SinCliente", codigos);
        Assert.Contains("Facturas.SinDescripcion", codigos);
        Assert.Contains("Facturas.SinLineas", codigos);
        Assert.Contains("Facturas.PeriodoAlReves", codigos);
    }

    [Fact]
    public void ImporteCeroEsAvisoNoError()
    {
        var f = Borrador();
        f.Lineas[0].PrecioUnitario = 0m;
        f.Lineas[0].Recalcular();
        var i = Assert.Single(ReglasFactura.ValidarParaEmision(f, null));
        Assert.Equal(Incidencia.ImporteCero, i.Codigo);
        Assert.True(i.EsAviso);
    }

    [Fact]
    public void FechaAnteriorALaUltimaDeLaSerie()
    {
        var i = Assert.Single(ReglasFactura.ValidarParaEmision(Borrador(), new DateTime(2026, 3, 2)));
        Assert.Equal("Facturas.FechaAnteriorSerie", i.Codigo);
    }

    [Fact]
    public void SuplidoNoNecesitaCuentaEnLaLinea()
    {
        var f = Borrador();
        var s = new LineaFacturaVenta { Nombre = "Tasa", PrecioUnitario = 20m, EsSuplido = true, TipoIva = 0m, CalificacionOperacion = "N1" };
        s.Recalcular();
        f.AnadirLinea(s);
        Assert.Empty(ReglasFactura.ValidarParaEmision(f, null));
    }

    [Fact]
    public void ExentaConIvaEsError()
    {
        var f = Borrador();
        f.Lineas[0].CausaExencion = "E1";
        Assert.Contains(ReglasFactura.ValidarParaEmision(f, null), i => i.Codigo == "Facturas.ExentaConTipo");
    }

    [Fact]
    public void SoloLasNotasSeCambianTrasValidar()
    {
        var f = Borrador();
        f.Estado = EstadoFactura.Validada;
        Assert.True(ReglasFactura.CampoModificable(f, nameof(FacturaVenta.NotasInternas)));
        Assert.False(ReglasFactura.CampoModificable(f, nameof(FacturaVenta.Descripcion)));
        Assert.Contains(ReglasFactura.ValidarParaEmision(f, null), i => i.Codigo == "Facturas.NoEsBorrador");
    }

    [Theory]
    [InlineData("F2026", null, 7, "F20260007")]
    [InlineData("F2026", "F2026-00041", 42, "F2026-00042")]
    [InlineData("A", "A123", 124, "A124")]
    [InlineData("R2026", "X0001", 2, "R20260002")]
    public void NumeroSigueElFormatoDeLaUltimaEmitida(string prefijo, string? ultimo, int numero, string esperado)
    {
        var serie = new Serie(1, 1, "Serie", prefijo, 1, 99999, numero - 1, false, true);
        Assert.Equal(esperado, ReglasFactura.ComponerNumero(serie, numero, ultimo));
    }

    [Fact]
    public void SerieSiguienteYAgotada()
    {
        Assert.Equal(1, new Serie(1, 1, "S", "F", 1, 10, null, false, true).Siguiente);
        Assert.True(new Serie(1, 1, "S", "F", 1, 10, 10, false, true).Agotada);
    }

    [Theory]
    [InlineData("F1", "R4")]
    [InlineData("F2", "R5")]
    [InlineData("F3", "R4")]
    public void TipoDeLaRectificativa(string original, string esperado) => Assert.Equal(esperado, ReglasFactura.TipoRectificativaDe(original));

    [Theory]
    [InlineData("724", "ES")]
    [InlineData("ESP", "ES")]
    [InlineData("fr", "FR")]
    [InlineData("", null)]
    [InlineData("ZZZ", null)]
    public void PaisEnIsoDeDosLetras(string pais, string? esperado) => Assert.Equal(esperado, Instantaneas.PaisIso(pais));

    [Fact]
    public void BorradorSinNumeroSeRotulaConSuId()
    {
        Assert.Equal("Borrador B-0007", FacturaVenta.RotuloBorrador(7));
        Assert.Equal("Borrador nuevo", new FacturaVenta().Rotulo);
    }
}

public class AsientoVentaTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private static readonly CuentasVenta Cuentas = new("4300001", "4735079", new Dictionary<decimal, string> { [21m] = "4770021", [10m] = "4770010" }, "5550001");

    private static FacturaVenta Factura(decimal retencion = 19m)
    {
        var f = new FacturaVenta { IdFactura = 5, IdCliente = 3, NumeroFactura = "F20260001", Descripcion = "Alquiler", TipoRetencion = retencion, FechaFactura = new DateTime(2026, 3, 1) };
        foreach (var (precio, iva, suplido, cuenta) in new[] { (1000m, 21m, false, "7520001"), (100m, 10m, false, "7050001"), (30m, 0m, true, (string?)null) })
        {
            var l = new LineaFacturaVenta { Nombre = "L", PrecioUnitario = precio, TipoIva = iva, EsSuplido = suplido, CuentaContable = cuenta };
            l.Recalcular();
            f.AnadirLinea(l);
        }
        return f;
    }

    [Fact]
    public void AsientoCuadradoConClienteRetencionIngresosIvaYSuplidos()
    {
        var f = Factura();
        Invariantes.Factura(f);
        var a = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 3, 2));
        Invariantes.Asiento(a);
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta).EsCorrecto);
        // Bruto: 1000 + 210 + 100 + 10 + 30 = 1350; retención 19 % de 1100 = 209; a cobrar 1141.
        Assert.Equal(1141m, a.Apuntes.Single(x => x.CodigoCuenta == "4300001").Debe.Valor);
        Assert.Equal(209m, a.Apuntes.Single(x => x.CodigoCuenta == "4735079").Debe.Valor);
        Assert.Equal(1000m, a.Apuntes.Single(x => x.CodigoCuenta == "7520001").Haber.Valor);
        Assert.Equal(210m, a.Apuntes.Single(x => x.CodigoCuenta == "4770021").Haber.Valor);
        Assert.Equal(10m, a.Apuntes.Single(x => x.CodigoCuenta == "4770010").Haber.Valor);
        Assert.Equal(30m, a.Apuntes.Single(x => x.CodigoCuenta == "5550001").Haber.Valor);
        Assert.All(a.Apuntes, x => Assert.Equal("F20260001", x.NumeroDocumento));
        Assert.Equal(3, a.Apuntes.Single(x => x.CodigoCuenta == "4300001").IdCliente);
    }

    [Fact]
    public void RectificativaVaEnPositivoAlLadoContrario()
    {
        var original = Factura();
        var r = new FacturaVenta { IdFactura = 6, IdCliente = 3, NumeroFactura = "R20260001", TipoFactura = "R4", TipoRetencion = 19m, Descripcion = "Rect" };
        foreach (var l in original.Lineas) r.AnadirLinea(l.Copiar(invertir: true));
        var a = AsientoVenta.Construir(r, Ctx, Cuentas, new DateTime(2026, 3, 5));
        Assert.Equal(TipoAsiento.Rectificacion, a.Tipo);
        Invariantes.Factura(r);
        Invariantes.Asiento(a);
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta).EsCorrecto);
        Assert.Equal(1141m, a.Apuntes.Single(x => x.CodigoCuenta == "4300001").Haber.Valor);
        Assert.Equal(1000m, a.Apuntes.Single(x => x.CodigoCuenta == "7520001").Debe.Valor);
        Assert.All(a.Apuntes, x => Assert.False(x.Debe.EsNegativo || x.Haber.EsNegativo));
    }

    [Fact]
    public void SinRetencionNoHayApunteDeRetencion()
    {
        var a = AsientoVenta.Construir(Factura(0m), Ctx, Cuentas with { Retencion = null }, new DateTime(2026, 3, 2));
        Assert.DoesNotContain(a.Apuntes, x => x.CodigoCuenta == "4735079");
        Invariantes.Asiento(a);
    }

    [Fact]
    public void FechaDelAsientoHoySiCaeEnElEjercicio()
    {
        var f = Factura();
        Assert.Equal(new DateTime(2026, 4, 10), AsientoVenta.Fecha(f, 2026, new DateTime(2026, 4, 10, 9, 30, 0)));
        Assert.Equal(new DateTime(2026, 3, 1), AsientoVenta.Fecha(f, 2026, new DateTime(2027, 1, 5)));
    }
}

public class FechasLegadasTests
{
    [Fact]
    public void LaFechaCeroDeAccessSeLeeComoVacia()
    {
        // El ERP antiguo dejaba 30/12/1899 en fechas sin rellenar: no debe mostrarse como fecha de operación ni de periodo.
        Assert.Null(RepositorioFacturas.SinFechaCero(new DateTime(1899, 12, 30)));
        Assert.Null(RepositorioFacturas.SinFechaCero(null));
        Assert.Equal(new DateTime(2026, 11, 4), RepositorioFacturas.SinFechaCero(new DateTime(2026, 11, 4)));
    }
}

public class OrdenLineasTests
{
    private static FacturaVenta ConLineas(params string[] nombres)
    {
        var f = new FacturaVenta();
        foreach (var n in nombres) f.AnadirLinea(new LineaFacturaVenta { Nombre = n });
        return f;
    }

    [Fact]
    public void MoverLineaCambiaElOrdenYRenumera()
    {
        var f = ConLineas("A", "B", "C");
        Assert.True(f.MoverLinea(f.Lineas[2], -1));
        Assert.Equal(["A", "C", "B"], f.Lineas.Select(l => l.Nombre));
        Assert.Equal([1, 2, 3], f.Lineas.Select(l => l.NumeroLinea));
        Assert.True(f.MoverLinea(f.Lineas[0], 2));
        Assert.Equal(["C", "B", "A"], f.Lineas.Select(l => l.Nombre));
    }

    [Fact]
    public void NoSeMuevePorEncimaDeLaPrimeraNiPorDebajoDeLaUltima()
    {
        var f = ConLineas("A", "B");
        Assert.False(f.MoverLinea(f.Lineas[0], -1));
        Assert.False(f.MoverLinea(f.Lineas[1], 1));
        Assert.False(f.MoverLinea(new LineaFacturaVenta { Nombre = "X" }, 1));
        Assert.Equal(["A", "B"], f.Lineas.Select(l => l.Nombre));
    }
}
