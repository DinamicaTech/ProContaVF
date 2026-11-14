using ProConta.Facturacion.Documentos;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Tests;

/// <summary>Bloque de pago del PDF y sus avisos al validar (facturacion/factura-venta 2026-11-13), sin BD; IBAN sintéticos.</summary>
public sealed class PagoFacturaTests
{
    private const string IbanCliente = "ES4021000001000000000001";
    private const string IbanEmpresa = "ES8700491500050012345678";

    [Fact]
    public void ConReciboSaleLaCuentaDelClienteEnmascarada()
    {
        var b = ConstructorPdfFactura.BloquePago(true, IbanCliente, IbanEmpresa, "F2026/0007")!;
        Assert.Equal(("SE CARGARÁ EN SU CUENTA", "ES** **** **** **** **** 0001", ""), (b.Etiqueta, b.Cuenta, b.Detalle));
    }

    [Fact]
    public void ConTransferenciaSaleLaCuentaDeLaEmpresaEnteraYElNumeroEnElConcepto()
    {
        var b = ConstructorPdfFactura.BloquePago(false, IbanCliente, IbanEmpresa, "F2026/0007")!;
        Assert.Equal(("INGRESAR EN LA CUENTA", "ES87 0049 1500 0500 1234 5678"), (b.Etiqueta, b.Cuenta));
        Assert.Contains("«F2026/0007»", b.Detalle);
        Assert.Contains("número de factura", ConstructorPdfFactura.BloquePago(false, null, IbanEmpresa, null)!.Detalle);
    }

    [Fact]
    public void SinCuentaNoHayBloqueDePago()
    {
        Assert.Null(ConstructorPdfFactura.BloquePago(true, null, IbanEmpresa, null));
        Assert.Null(ConstructorPdfFactura.BloquePago(false, IbanCliente, null, null));
    }

    [Fact]
    public void AvisaDeLaTransferenciaSinCuentaYDelReciboSinIban()
    {
        Assert.Equal("Facturas.TransferenciaSinCuenta", Assert.Single(ReglasFactura.AvisosPago("Transferencia Santander", false, false, false, "Cliente inventado")).Codigo);
        Assert.Equal("Facturas.TransferenciaSinCuenta", Assert.Single(ReglasFactura.AvisosPago("Trasferencia BBVA", false, false, false, "Cliente inventado")).Codigo);
        var recibo = Assert.Single(ReglasFactura.AvisosPago("Recibo domiciliado", true, false, true, "Cliente inventado"));
        Assert.Equal(("Facturas.ReciboSinIban", true, "AbrirFichaCliente"), (recibo.Codigo, recibo.EsAviso, recibo.Accion));
    }

    [Fact]
    public void NoAvisaSiTieneCuentaOSiLaFormaNoLaNecesita()
    {
        Assert.Empty(ReglasFactura.AvisosPago("Transferencia Santander", false, false, true, "Cliente inventado"));
        Assert.Empty(ReglasFactura.AvisosPago("Recibo domiciliado", true, true, false, "Cliente inventado"));
        Assert.Empty(ReglasFactura.AvisosPago("Contado", false, false, false, "Cliente inventado"));
        Assert.Empty(ReglasFactura.AvisosPago(null, true, false, false, "Cliente inventado"));
    }
}
