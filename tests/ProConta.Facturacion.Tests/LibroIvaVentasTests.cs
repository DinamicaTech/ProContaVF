using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Contexto;

namespace ProConta.Facturacion.Tests;

/// <summary>
/// Libro de IVA repercutido sin BD (facturacion/factura-venta/contabilizacion 2026-11-11): desglose por tipo,
/// calificación y 477 de la actividad, exentas con su 477 del 0 %, rectificativas en negativo y aviso del trimestre.
/// </summary>
public class LibroIvaVentasTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);

    // Centro 18 = actividad con serie 4727 (locales); centro 26 = serie 4726 (viviendas).
    private static readonly CuentasVenta Cuentas = new("4300001", "4735079",
        new Dictionary<decimal, string> { [21m] = "4770021", [0m] = "4770000" }, "5550001",
        new Dictionary<int, string> { [18] = "4727", [26] = "4726" });

    private static LineaFacturaVenta Linea(decimal precio, decimal iva, int? centro, string? causa = null, bool suplido = false)
    {
        var l = new LineaFacturaVenta { Nombre = "L", PrecioUnitario = precio, TipoIva = iva, IdCentroCoste = centro, CausaExencion = causa, EsSuplido = suplido, CuentaContable = suplido ? null : "7520001" };
        l.Recalcular();
        return l;
    }

    private static FacturaVenta Factura(params LineaFacturaVenta[] lineas)
    {
        var f = new FacturaVenta { IdFactura = 7, IdCliente = 3, NumeroFactura = "F20260007", Descripcion = "Alquileres", FechaFactura = new DateTime(2026, 3, 1) };
        foreach (var l in lineas) f.AnadirLinea(l);
        return f;
    }

    [Fact]
    public void Desglose_por_tipo_y_477_de_la_actividad_con_las_exentas_y_sin_suplidos()
    {
        var f = Factura(Linea(1000m, 21m, 18), Linea(500m, 21m, 18), Linea(200m, 21m, null), Linea(900m, 0m, 26, "E1"), Linea(30m, 0m, null, suplido: true));
        var libro = AsientoVenta.Libro(f, Cuentas);

        Assert.Equal(3, libro.Tramos.Count);
        var locales = libro.Tramos.Single(t => t.Cuenta == "4777021");
        Assert.Equal((1500m, 315m, "S1"), (locales.Base, locales.Cuota, locales.Calificacion));
        var general = libro.Tramos.Single(t => t.Cuenta == "4770021");
        Assert.Equal((200m, 42m), (general.Base, general.Cuota));
        var exenta = libro.Tramos.Single(t => t.Cuenta == "4776000");
        Assert.Equal((900m, 0m, "E1", "E1"), (exenta.Base, exenta.Cuota, exenta.Calificacion, exenta.CausaExencion));

        // El suplido queda fuera de la base; el total es el de la factura. La cuota de cada 477 es la del asiento.
        Assert.Equal(2600m, libro.Base);
        Assert.Equal(f.Totales.ImporteTotal.Valor, libro.Total);
        Assert.Equal(f.Totales.BaseImponible.Valor, libro.Base);
        Assert.Equal(f.Totales.Iva.Valor, libro.Cuota);
        var asiento = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 3, 1));
        foreach (var t in libro.Tramos.Where(t => t.Cuota != 0m))
            Assert.Equal(t.Cuota, asiento.Apuntes.Where(a => a.CodigoCuenta == t.Cuenta).Sum(a => a.Haber.Valor - a.Debe.Valor));
        Assert.DoesNotContain(asiento.Apuntes, a => a.CodigoCuenta == "4776000");
    }

    [Fact]
    public void Una_rectificativa_va_en_negativo_y_la_anulacion_niega_el_registro()
    {
        var original = Factura(Linea(1000m, 21m, 18));
        var r = new FacturaVenta { IdFactura = 8, IdCliente = 3, NumeroFactura = "R20260001", TipoFactura = "R4", FechaFactura = new DateTime(2026, 3, 5) };
        foreach (var l in original.Lineas) r.AnadirLinea(l.Copiar(invertir: true));
        var libro = AsientoVenta.Libro(r, Cuentas);
        Assert.Equal((-1000m, -210m, -1210m), (libro.Base, libro.Cuota, libro.Total));

        var anulacion = AsientoVenta.Libro(original, Cuentas).Negado();
        Assert.Equal((-1000m, -210m, -1210m), (anulacion.Base, anulacion.Cuota, anulacion.Total));
        Assert.Equal("4777021", anulacion.Tramos.Single().Cuenta);
    }

    [Fact]
    public void Sin_cuenta_del_tipo_el_tramo_queda_sin_cuenta()
    {
        var libro = AsientoVenta.Libro(Factura(Linea(100m, 10m, 18)), Cuentas);
        Assert.Null(libro.Tramos.Single().Cuenta);
    }

    [Theory]
    [InlineData("S1", null, "S1")]
    [InlineData("S1", "E1", "E1")]
    [InlineData("N2", null, "N2")]
    [InlineData("", null, "S1")]
    public void La_calificacion_de_una_exenta_es_su_causa(string calificacion, string? causa, string esperada) =>
        Assert.Equal(esperada, AsientoVenta.Calificacion(new LineaFacturaVenta { CalificacionOperacion = calificacion, CausaExencion = causa }));

    [Fact]
    public void Avisa_solo_si_el_asiento_va_a_otro_trimestre()
    {
        Assert.Null(AsientoVenta.AvisoTrimestre(new DateTime(2026, 3, 10), new DateTime(2026, 3, 10)));
        Assert.Null(AsientoVenta.AvisoTrimestre(null, new DateTime(2026, 3, 10)));
        var aviso = AsientoVenta.AvisoTrimestre(new DateTime(2026, 3, 10), new DateTime(2026, 4, 1));
        Assert.NotNull(aviso);
        Assert.Contains("1T de 2026", aviso);
        Assert.Contains("2T de 2026", aviso);
    }
}
