using ProConta.Compras.Facturas;

namespace ProConta.Compras.Tests;

/// <summary>Centro de coste de la factura de compra según el activo de su cuenta de gasto (activos 2026-11-17 20:35).</summary>
public sealed class CentroActivoCompraTests
{
    private static readonly ActivoDeCuenta PisoA = new(1, "Piso A", 300);
    private static readonly ActivoDeCuenta PisoB = new(2, "Piso B", 301);
    private static readonly ActivoDeCuenta SinCentro = new(3, "Software", null);

    [Fact]
    public void Sin_centro_en_la_factura_se_propone_el_del_activo()
    {
        Assert.Equal(new PropuestaCentroActivo(300, null), CentroActivoCompra.Proponer([PisoA], null, "6250001"));
    }

    [Fact]
    public void Con_el_mismo_centro_no_hace_nada()
    {
        Assert.Equal(new PropuestaCentroActivo(null, null), CentroActivoCompra.Proponer([PisoA], 300, "6250001"));
    }

    [Fact]
    public void Con_otro_centro_no_lo_toca_y_avisa_sin_bloquear()
    {
        var p = CentroActivoCompra.Proponer([PisoA], 301, "6250001", id => id == 300 ? "300 Piso A" : "301 Piso B");
        Assert.Null(p.CentroPropuesto);
        Assert.NotNull(p.Aviso);
        Assert.Contains("«Piso A»", p.Aviso);
        Assert.Contains("300 Piso A", p.Aviso);
        Assert.Contains("301 Piso B", p.Aviso);
    }

    [Fact]
    public void Subcuenta_de_varios_activos_de_ninguno_o_activo_sin_centro_no_hace_nada()
    {
        var nada = new PropuestaCentroActivo(null, null);
        Assert.Equal(nada, CentroActivoCompra.Proponer([PisoA, PisoB], null, "6280001"));
        Assert.Equal(nada, CentroActivoCompra.Proponer([], null, "6000000"));
        Assert.Equal(nada, CentroActivoCompra.Proponer([SinCentro], null, "6290003"));
        Assert.Equal(nada, CentroActivoCompra.Proponer([SinCentro], 300, "6290003"));
    }
}
