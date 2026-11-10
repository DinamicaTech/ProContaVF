using ProConta.Contabilidad.Asientos;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Contexto;

namespace ProConta.Facturacion.Tests;

/// <summary>
/// Paridad con ProConta (docs/global-rules.md, Project rules): importes de facturas reales emitidas por el ERP en
/// octubre de 2026, sin ningún dato del cliente. Los alquileres de vivienda van al 0 % de IVA; el alquiler de local
/// lleva IVA del 21 % y retención del 19 % (en el ERP la retención salía como una línea negativa; aquí es el tipo de
/// retención de la factura, que da el mismo total).
/// </summary>
public class ParidadProContaTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private static readonly CuentasVenta Cuentas = new("4300001", "4735079", new Dictionary<decimal, string> { [21m] = "4770021", [0m] = "4770000" }, null);

    private static FacturaVenta Alquiler(decimal precio, decimal iva, decimal retencion)
    {
        var f = new FacturaVenta { IdFactura = 1, IdCliente = 1, NumeroFactura = "083232", Descripcion = "Alquiler", TipoRetencion = retencion, FechaFactura = new DateTime(2026, 10, 6) };
        var l = new LineaFacturaVenta { Nombre = "Alquiler mensual", Cantidad = 1m, PrecioUnitario = precio, TipoIva = iva, CuentaContable = "7520001" };
        l.Recalcular();
        f.AnadirLinea(l);
        return f;
    }

    [Theory]
    [InlineData(954.00)]
    [InlineData(992.00)]
    [InlineData(975.00)]
    [InlineData(962.00)]
    [InlineData(974.00)]
    [InlineData(1051.85)]
    [InlineData(1030.00)]
    public void AlquilerDeViviendaSinIvaNiRetencion(double importe)
    {
        var precio = (decimal)importe;
        var f = Alquiler(precio, 0m, 0m);
        Invariantes.Factura(f);
        Assert.Equal(precio, f.Totales.BaseImponible.Valor);
        Assert.Equal(0m, f.Totales.Iva.Valor);
        Assert.Equal(0m, f.Totales.Retencion.Valor);
        Assert.Equal(precio, f.Totales.ImporteTotal.Valor);

        var a = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 10, 6));
        Invariantes.Asiento(a);
        Assert.Equal(precio, a.Apuntes.Single(x => x.CodigoCuenta == "4300001").Debe.Valor);
        Assert.Equal(precio, a.Apuntes.Single(x => x.CodigoCuenta == "7520001").Haber.Valor);
    }

    [Fact]
    public void AlquilerDeLocalConIvaYRetencion()
    {
        // ERP: neto 2.800,00; IVA 21 % 588,00; retención 19 % −532,00; total 2.856,00.
        var f = Alquiler(2800m, 21m, 19m);
        Invariantes.Factura(f);
        Assert.Equal(2800m, f.Totales.BaseImponible.Valor);
        Assert.Equal(588m, f.Totales.Iva.Valor);
        Assert.Equal(532m, f.Totales.Retencion.Valor);
        Assert.Equal(2856m, f.Totales.ImporteTotal.Valor);

        var a = AsientoVenta.Construir(f, Ctx, Cuentas, new DateTime(2026, 10, 6));
        Invariantes.Asiento(a);
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta).EsCorrecto);
        Assert.Equal(2856m, a.Apuntes.Single(x => x.CodigoCuenta == "4300001").Debe.Valor);
        Assert.Equal(532m, a.Apuntes.Single(x => x.CodigoCuenta == "4735079").Debe.Valor);
        Assert.Equal(2800m, a.Apuntes.Single(x => x.CodigoCuenta == "7520001").Haber.Valor);
        Assert.Equal(588m, a.Apuntes.Single(x => x.CodigoCuenta == "4770021").Haber.Valor);
    }

    [Fact]
    public void ElNumeroContinuaElFormatoDelErp()
    {
        // El ERP numera sin prefijo y con seis cifras (083232…083239): la siguiente es 083240.
        var serie = new Serie(1, 9001, "Facturas", "", 1, 999999, 83239, false, true);
        Assert.Equal("083240", ReglasFactura.ComponerNumero(serie, serie.Siguiente, "083239"));
    }
}
