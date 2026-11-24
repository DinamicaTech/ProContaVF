using ProConta.Activos.Financieros;

namespace ProConta.Activos.Tests;

/// <summary>Coste medio ponderado de un activo financiero (activos/activos-financieros 2026-11-24 09:14): lo que se lleva cada venta y lo que queda.</summary>
public sealed class CosteMedioInversionTests
{
    private static MovimientoCoste Compra(int id, int dia, decimal p, decimal importe) => new(id, OperacionDeInversion.TipoCompra, new DateTime(2026, 1, dia), p, importe);
    private static MovimientoCoste Venta(int id, int dia, decimal p, decimal importe) => new(id, OperacionDeInversion.TipoVenta, new DateTime(2026, 1, dia), p, importe);

    [Fact]
    public void Una_venta_se_lleva_el_coste_medio_de_lo_que_vende_y_lo_que_queda_es_el_resto()
    {
        var r = CosteMedioInversion.Calcular([Compra(1, 1, 10m, 1000m), Compra(2, 5, 5m, 650m), Venta(3, 10, 3m, 400m)]);
        // 1.650 € / 15 participaciones = 110 €; 3 vendidas = 330 €.
        Assert.Equal(330m, r.CosteVendido[3]);
        Assert.Equal((12m, 1320m, false), (r.Participaciones, r.Coste, r.Descubierto));
    }

    [Fact]
    public void Vender_todo_se_lleva_el_coste_entero_sin_restos_de_redondeo()
    {
        var r = CosteMedioInversion.Calcular([Compra(1, 1, 3m, 100m), Venta(2, 2, 1m, 40m), Venta(3, 3, 2m, 80m)]);
        Assert.Equal(33.33m, r.CosteVendido[2]);
        Assert.Equal(66.67m, r.CosteVendido[3]);
        Assert.Equal((0m, 0m), (r.Participaciones, r.Coste));
    }

    [Fact]
    public void El_mismo_dia_la_compra_va_antes_que_la_venta_y_vender_de_mas_se_marca_como_descubierto()
    {
        var mismoDia = CosteMedioInversion.Calcular([Venta(1, 4, 2m, 300m), Compra(2, 4, 2m, 200m)]);
        Assert.False(mismoDia.Descubierto);
        Assert.Equal(200m, mismoDia.CosteVendido[1]);

        var deMas = CosteMedioInversion.Calcular([Compra(1, 1, 2m, 200m), Venta(2, 2, 3m, 300m)]);
        Assert.True(deMas.Descubierto);
    }
}
