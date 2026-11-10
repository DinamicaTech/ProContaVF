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
    public void LasDescripcionesSiguenElFormatoDeCadaApunte()
    {
        var f = Factura();
        f.Cliente = InstantaneaCliente.Vacia with { Nombre = "Inquilina", RazonSocial = "Inquilina Fiscal SL" };
        var a = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 3, 2));
        string Texto(string cuenta) => a.Apuntes.Single(x => x.CodigoCuenta == cuenta).Descripcion!;
        Assert.Equal("Fra. F20260001 - Inquilina - Alquiler", a.Descripcion);
        Assert.Equal("Fra. F20260001 - Inquilina - Alquiler", Texto("4300001"));
        Assert.Equal("Retención 19 % - Inquilina - Fra. F20260001 - Alquiler", Texto("4735079"));
        Assert.Equal("L - Inquilina - Fra. F20260001", Texto("7520001"));
        Assert.Equal("IVA repercutido 21 % - Inquilina - Fra. F20260001 - Alquiler", Texto("4770021"));
        Assert.Equal("Suplidos - Inquilina - Fra. F20260001 - Alquiler", Texto("5550001"));
    }

    [Fact]
    public void LaRectificativaUsaFraRect()
    {
        var r = new FacturaVenta { IdFactura = 6, IdCliente = 3, NumeroFactura = "R20260001", TipoFactura = "R4", Descripcion = "Rect" };
        foreach (var l in Factura().Lineas) r.AnadirLinea(l.Copiar(invertir: true));
        r.Cliente = InstantaneaCliente.Vacia with { Nombre = "Inquilina" };
        var a = AsientoVenta.Construir(r, Ctx, Cuentas, new DateTime(2026, 3, 5));
        Assert.Equal("FraRect. R20260001 - Inquilina - Rect", a.Apuntes.Single(x => x.CodigoCuenta == "4300001").Descripcion);
    }

    [Fact]
    public void Varias_lineas_en_la_misma_cuenta_llevan_la_descripcion_de_la_primera()
    {
        var f = new FacturaVenta { IdFactura = 5, IdCliente = 3, NumeroFactura = "F1", Descripcion = "D" };
        foreach (var nombre in new[] { "Primera", "Segunda" })
        {
            var l = new LineaFacturaVenta { Nombre = nombre, PrecioUnitario = 10m, TipoIva = 21m, CuentaContable = "7520001" };
            l.Recalcular();
            f.AnadirLinea(l);
        }
        f.Cliente = InstantaneaCliente.Vacia with { Nombre = "C" };
        var a = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 3, 2));
        Assert.Equal("Primera - C - Fra. F1", a.Apuntes.Single(x => x.CodigoCuenta == "7520001").Descripcion);
    }

    [Fact]
    public void Al_recortar_solo_se_corta_la_descripcion_y_se_ven_cliente_y_numero()
    {
        var f = Factura();
        f.Descripcion = new string('x', 400) + "\r\n fin";
        f.Lineas[0].Nombre = new string('y', 400);
        f.Cliente = InstantaneaCliente.Vacia with { Nombre = "Cliente largo SL" };
        var a = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 3, 2));
        foreach (var apunte in a.Apuntes)
        {
            Assert.True(apunte.Descripcion!.Length <= ReglasAsiento.LongitudDescripcion);
            Assert.Contains("Cliente largo SL", apunte.Descripcion);
            Assert.Contains("Fra. F20260001", apunte.Descripcion);
        }
        Assert.EndsWith(" - Cliente largo SL - Fra. F20260001", a.Apuntes.Single(x => x.CodigoCuenta == "7520001").Descripcion);
        Assert.StartsWith("Fra. F20260001 - Cliente largo SL - xxx", a.Apuntes.Single(x => x.CodigoCuenta == "4300001").Descripcion);
    }

    [Fact]
    public void El_iva_se_reparte_por_la_477_de_la_actividad_de_cada_linea()
    {
        var f = new FacturaVenta { IdFactura = 5, IdCliente = 3, NumeroFactura = "F1", Descripcion = "Reparto" };
        foreach (var (precio, iva, centro) in new[] { (100m, 21m, (int?)1), (200m, 21m, 2), (300m, 21m, 3), (400m, 21m, null), (50m, 10m, 1) })
        {
            var l = new LineaFacturaVenta { Nombre = "L", PrecioUnitario = precio, TipoIva = iva, CuentaContable = "7520001", IdCentroCoste = centro };
            l.Recalcular();
            f.AnadirLinea(l);
        }
        // Centro 1 turístico, 2 locales; 3 y las líneas sin centro, la cuenta general.
        var cuentas = Cuentas with { SeriesPorCentro = new Dictionary<int, string> { [1] = "4728", [2] = "4727" } };
        var a = AsientoVenta.Construir(f, Ctx, cuentas, new DateTime(2026, 3, 2));
        Invariantes.Asiento(a);
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta).EsCorrecto);
        Assert.Equal(21m, a.Apuntes.Single(x => x.CodigoCuenta == "4778021").Haber.Valor);
        Assert.Equal(42m, a.Apuntes.Single(x => x.CodigoCuenta == "4777021").Haber.Valor);
        Assert.Equal(147m, a.Apuntes.Single(x => x.CodigoCuenta == "4770021").Haber.Valor);
        Assert.Equal(5m, a.Apuntes.Single(x => x.CodigoCuenta == "4778010").Haber.Valor);
    }

    [Theory]
    [InlineData("4770021", "4728", "4778021")]
    [InlineData("4770021", "4720", "4770021")]
    [InlineData("4770010", "4722", "4772010")]
    [InlineData("¿IVA 21 %?", "4728", "¿IVA 21 %?")]
    public void La_477_de_la_actividad_cambia_el_2_por_el_7_y_conserva_la_terminacion(string general, string serie, string esperada) =>
        Assert.Equal(esperada, AsientoVenta.CuentaIvaActividad(general, serie));

    [Theory]
    [InlineData(true, "Ana", "Ruiz", "Comercial", "Razón", "Ana Ruiz Gil", "Ana Ruiz")]
    [InlineData(true, null, null, null, null, "Ana Ruiz Gil", "Ana Ruiz Gil")]
    [InlineData(false, null, null, "Comercial", "Razón SL", "Razón SL", "Comercial")]
    [InlineData(false, null, null, " ", "Razón SL", "Otro", "Razón SL")]
    [InlineData(false, null, null, null, null, "Otro", "Otro")]
    public void El_nombre_del_cliente_depende_de_su_tipo(bool fisica, string? nombre, string? apellido, string? comercial, string? razon, string? general, string esperado) =>
        Assert.Equal(esperado, Instantaneas.NombreCliente(fisica, nombre, apellido, comercial, razon, general));

    [Fact]
    public void FechaDelAsientoEsLaDeLaFacturaSalvoQueSeaAnteriorAlTrimestreActivo()
    {
        var f = Factura();
        Assert.Equal(new DateTime(2026, 3, 1), f.FechaFactura);
        Assert.Equal(new DateTime(2026, 3, 1), AsientoVenta.Fecha(f, 2026, 1, new DateTime(2026, 4, 10, 9, 30, 0)));
        Assert.Equal(new DateTime(2026, 4, 1), AsientoVenta.Fecha(f, 2026, 2, new DateTime(2026, 4, 10)));
        // Sin trimestre activo configurado y con el ejercicio ya pasado, cuenta el cuarto.
        Assert.Equal(new DateTime(2026, 10, 1), AsientoVenta.Fecha(f, 2026, null, new DateTime(2027, 1, 5)));
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
