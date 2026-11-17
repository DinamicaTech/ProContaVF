using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Programadas;

namespace ProConta.Facturacion.Tests;

/// <summary>Activo de las líneas de venta y de programada (activos 2026-11-17 20:35): valores por defecto al elegirlo, sin tocar importes.</summary>
public sealed class ActivoLineaTests
{
    private static readonly ActivoLinea Piso = new(18, "PisoA", "Piso A", 34, "7520018");
    private static readonly ActivoLinea SinCentroNiIngreso = new(19, "Local", "Local B", null, null);

    private static LineaFacturaVenta Linea(string? cuenta = null, int? centro = null)
    {
        var l = new LineaFacturaVenta { Nombre = "Alquiler", Cantidad = 1m, PrecioUnitario = 900m, TipoIva = 0m, CausaExencion = "E1", CuentaContable = cuenta, IdCentroCoste = centro };
        l.Recalcular();
        return l;
    }

    [Fact]
    public void Al_elegir_el_activo_la_linea_toma_su_centro_y_su_cuenta_de_ingreso_si_no_tiene()
    {
        var l = Linea();
        var (centro, cuenta) = ReglasActivoLinea.Elegir(l, Piso);
        Assert.True(centro && cuenta);
        Assert.Equal((18, 34, "7520018"), (l.IdActivo!.Value, l.IdCentroCoste!.Value, l.CuentaContable));
        Assert.Equal(900m, l.ImporteTotal.Valor);
    }

    [Fact]
    public void Con_cuenta_no_se_cambia_y_con_otro_centro_se_pone_el_del_activo()
    {
        var l = Linea("7050001", 99);
        var (centro, cuenta) = ReglasActivoLinea.Elegir(l, Piso);
        Assert.True(centro);
        Assert.False(cuenta);
        Assert.Equal(("7050001", 34), (l.CuentaContable, l.IdCentroCoste!.Value));
        // Después el centro se puede cambiar a mano: el activo se queda.
        l.IdCentroCoste = 99;
        Assert.Equal(18, l.IdActivo);
    }

    [Fact]
    public void Un_activo_sin_centro_ni_ingreso_solo_se_asigna_y_quitarlo_no_toca_nada()
    {
        var l = Linea("7050001", 99);
        Assert.Equal((false, false), ReglasActivoLinea.Elegir(l, SinCentroNiIngreso));
        Assert.Equal((19, 99, "7050001"), (l.IdActivo!.Value, l.IdCentroCoste!.Value, l.CuentaContable));
        Assert.Equal((false, false), ReglasActivoLinea.Elegir(l, null));
        Assert.Null(l.IdActivo);
        Assert.Equal((99, "7050001"), (l.IdCentroCoste!.Value, l.CuentaContable));
    }

    [Fact]
    public void Las_copias_de_la_linea_conservan_el_activo()
    {
        var l = Linea("7520018", 34);
        l.IdActivo = 18;
        // Factura generada desde una programada, rectificativa y plantilla de programada desde una factura.
        Assert.Equal(18, LineasProgramada.Copiar(l).IdActivo);
        Assert.Equal(18, l.Copiar(invertir: true).IdActivo);
        Assert.Equal(18, LineasProgramada.DesdeLineaFactura(l).IdActivo);
    }

    [Fact]
    public void El_rotulo_marca_los_activos_de_baja()
    {
        Assert.Equal("Piso A", Piso.Rotulo);
        Assert.Equal("Piso A (de baja)", (Piso with { EnUso = false }).Rotulo);
    }
}
