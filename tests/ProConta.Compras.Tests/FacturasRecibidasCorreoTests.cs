using ProConta.Compras.Facturas;
using ProConta.Correo;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Tests;

/// <summary>
/// Factura de compra recibida por correo (correo/recepcion 2026-11-10 19:40): solo se contabiliza sola si todos los campos
/// alcanzan el 95 % de confianza, tiene proveedor y el reconocimiento no pide revisión.
/// </summary>
public sealed class FacturasRecibidasCorreoTests
{
    private static FacturaCompra Factura(int? proveedor = 7, bool revisar = false) =>
        new() { Ejercicio = new ContextoSociedadEjercicio("C01", 1, 2026), Md5 = "x", RutaPdf = "x.pdf", IdProveedor = proveedor, RequiereRevision = revisar };

    private static IReadOnlyList<CampoReconocido> Campos(params decimal?[] confianzas) =>
        confianzas.Select((c, i) => new CampoReconocido($"Campo{i}", "x", c)).ToList();

    [Fact]
    public void Todo_al_95_por_ciento_con_proveedor_se_contabiliza() =>
        Assert.Empty(ProcesadorFacturasCompraCorreo.MotivosRevision(Factura(), Campos(0.95m, 0.99m, 1m, 0.97m)));

    [Fact]
    public void Un_campo_por_debajo_o_sin_confianza_pide_revision()
    {
        var motivos = ProcesadorFacturasCompraCorreo.MotivosRevision(Factura(), Campos(0.99m, 0.94m, null, 1m));
        var motivo = Assert.Single(motivos);
        Assert.Contains("Campo1", motivo);
        Assert.Contains("Campo2", motivo);
        Assert.DoesNotContain("Campo0", motivo);
    }

    [Fact]
    public void Sin_proveedor_o_con_revision_pedida_no_se_contabiliza()
    {
        Assert.Contains(ProcesadorFacturasCompraCorreo.MotivosRevision(Factura(proveedor: null), Campos(1m)), m => m.Contains("proveedor"));
        Assert.Contains(ProcesadorFacturasCompraCorreo.MotivosRevision(Factura(revisar: true), Campos(1m)), m => m.Contains("revisar"));
    }

    [Fact]
    public void Campos_del_reconocimiento_sin_log_no_tienen_confianza()
    {
        var f = Factura();
        f.NumeroFactura = "F1";
        f.ImporteTotal = 121m;
        var campos = ProcesadorFacturasCompraCorreo.Campos(f);
        Assert.Equal(4, campos.Count);
        Assert.All(campos, c => Assert.Null(c.Confianza));
        Assert.Equal("F1", campos.Single(c => c.Campo == "Número").Valor);
        Assert.NotEmpty(ProcesadorFacturasCompraCorreo.MotivosRevision(f, campos));
    }
}
