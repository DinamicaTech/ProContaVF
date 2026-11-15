using ProConta.Compras.Facturas;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Tests;

/// <summary>Validaciones V1 a V3 de la factura de compra (avisan sin bloquear; incidencias con la gestoría), sin BD.</summary>
public sealed class ValidacionesCompraTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);

    private static FacturaCompra F(OperacionCompra op = OperacionCompra.Nacional, string? nif = "B00000000", decimal total = 121m, decimal? retencion = null,
        int? rectifica = null, int proveedor = 7) => new()
    {
        Ejercicio = Ctx, IdRegistro = 100, IdProveedor = proveedor, NifProveedor = nif, Operacion = op, ImporteTotal = total, BaseImponible = total / 1.21m,
        ImporteRetencion = retencion, Fecha = new DateTime(2026, 6, 1), IdRegistroFacturaRectificada = rectifica, RutaPdf = @"X:\f.pdf", Md5 = "0123456789abcdef0123456789abcdef",
    };

    private static FilaFacturaCompra Anterior(int id, bool retencion, int proveedor = 7) =>
        new() { IdRegistro = id, IdProveedor = proveedor, NumeroFactura = $"A{id}", Fecha = new DateTime(2026, 1, id), ImporteTotal = 100m, TieneRetencion = retencion };

    [Theory]
    [InlineData("12345678Z", "ES")]
    [InlineData("ES12345678Z", "ES")]
    [InlineData("IE6388047V", "IE")]
    [InlineData("US123456", "US")]
    [InlineData("", null)]
    [InlineData("12345", null)]
    public void PaisDelNif(string nif, string? pais) => Assert.Equal(pais, ValidacionesCompra.PaisNif(nif));

    [Theory]
    [InlineData(OperacionCompra.Nacional, "12345678Z", false)]
    [InlineData(OperacionCompra.Nacional, "IE6388047V", true)]        // proveedor de la UE como nacional
    [InlineData(OperacionCompra.Nacional, "US123456", true)]
    [InlineData(OperacionCompra.Intracomunitaria, "IE6388047V", false)]
    [InlineData(OperacionCompra.Intracomunitaria, "12345678Z", true)] // intracomunitaria con NIF español
    [InlineData(OperacionCompra.Intracomunitaria, "US123456", true)]
    [InlineData(OperacionCompra.Importacion, "US123456", false)]
    [InlineData(OperacionCompra.Importacion, "DE123456789", true)]
    [InlineData(OperacionCompra.Intracomunitaria, "", false)]         // sin NIF no se sabe
    public void V1_OperacionFrenteAlPaisDelNif(OperacionCompra op, string nif, bool avisa) =>
        Assert.Equal(avisa, ValidacionesCompra.AvisoOperacion(op, nif) is not null);

    [Fact]
    public void V2_AvisaSiElProveedorSueleLlevarRetencionYEstaNo()
    {
        var otras = new[] { Anterior(1, true), Anterior(2, true), Anterior(3, false), Anterior(4, true, proveedor: 8) };
        Assert.Contains(ValidacionesCompra.Avisos(F(), otras), a => a.Contains("retención", StringComparison.Ordinal));
        // Con retención, o con pocas facturas anteriores, no avisa.
        Assert.DoesNotContain(ValidacionesCompra.Avisos(F(retencion: 15m), otras), a => a.Contains("retención", StringComparison.Ordinal));
        Assert.DoesNotContain(ValidacionesCompra.Avisos(F(), otras.Take(2)), a => a.Contains("retención", StringComparison.Ordinal));
        // Si la mayoría no la llevaban, tampoco.
        Assert.DoesNotContain(ValidacionesCompra.Avisos(F(), [Anterior(1, true), Anterior(2, false), Anterior(3, false)]), a => a.Contains("retención", StringComparison.Ordinal));
    }

    [Fact]
    public void V3_AvisaDeUnAbonoSinFacturaRectificada()
    {
        Assert.NotNull(ValidacionesCompra.AvisoAbono(F(total: -50m)));
        Assert.Null(ValidacionesCompra.AvisoAbono(F(total: -50m, rectifica: 3)));
        Assert.Null(ValidacionesCompra.AvisoAbono(F()));
    }

    [Fact]
    public void LosAvisosNoImpidenGuardar()
    {
        var f = F(op: OperacionCompra.Intracomunitaria, nif: "12345678Z", total: -50m);
        Assert.NotEmpty(ValidacionesCompra.Avisos(f, []));
        Assert.True(f.Validar().EsCorrecto);
    }
}
