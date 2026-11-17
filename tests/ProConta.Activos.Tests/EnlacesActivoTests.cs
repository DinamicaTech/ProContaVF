using ProConta.Activos.Gestion;
using ProConta.Datos.Esquema.Migraciones;

namespace ProConta.Activos.Tests;

/// <summary>Reglas sin BD de los enlaces con compras y facturación (activos 2026-11-17 20:35): relleno inicial, facturas de compra del activo y ventas.</summary>
public sealed class EnlacesActivoTests
{
    [Fact]
    public void Relleno_por_subcuenta_de_un_solo_activo_si_no_por_centro_de_un_solo_activo()
    {
        Assert.Equal(7, RellenoActivoLineas.Elegir([7], [9]));
        Assert.Equal(9, RellenoActivoLineas.Elegir([], [9]));
        // Subcuenta de varios activos: decide el centro si es de uno solo.
        Assert.Equal(9, RellenoActivoLineas.Elegir([7, 8], [9]));
        // Centro compartido y cuenta sin activo, o nada: sin activo.
        Assert.Null(RellenoActivoLineas.Elegir([], [8, 9]));
        Assert.Null(RellenoActivoLineas.Elegir([7, 8], [8, 9]));
        Assert.Null(RellenoActivoLineas.Elegir([], []));
    }

    [Fact]
    public void Una_compra_es_del_activo_por_subcuenta_o_por_su_centro_si_no_lo_comparte()
    {
        var subcuentas = new HashSet<string>(["6250001", "6310001"]);
        Assert.True(ReglasEnlacesActivo.EsCompraDelActivo("6250001", null, subcuentas, 300, false));
        Assert.True(ReglasEnlacesActivo.EsCompraDelActivo(" 6250001 ", 999, subcuentas, 300, true));
        Assert.True(ReglasEnlacesActivo.EsCompraDelActivo("6280000", 300, subcuentas, 300, false));
        // Centro compartido: solo por subcuenta.
        Assert.False(ReglasEnlacesActivo.EsCompraDelActivo("6280000", 300, subcuentas, 300, true));
        Assert.False(ReglasEnlacesActivo.EsCompraDelActivo("6280000", 301, subcuentas, 300, false));
        Assert.False(ReglasEnlacesActivo.EsCompraDelActivo(null, null, subcuentas, null, false));
    }

    [Fact]
    public void Una_programada_entra_en_el_ejercicio_en_que_puede_emitir()
    {
        Assert.True(ReglasEnlacesActivo.ProgramadaEnEjercicio(new DateTime(2025, 3, 1), null, 2026));
        Assert.True(ReglasEnlacesActivo.ProgramadaEnEjercicio(new DateTime(2026, 12, 31), null, 2026));
        Assert.False(ReglasEnlacesActivo.ProgramadaEnEjercicio(new DateTime(2027, 1, 1), null, 2026));
        Assert.False(ReglasEnlacesActivo.ProgramadaEnEjercicio(new DateTime(2024, 1, 1), new DateTime(2025, 12, 31), 2026));
        Assert.True(ReglasEnlacesActivo.ProgramadaEnEjercicio(new DateTime(2024, 1, 1), new DateTime(2026, 1, 1), 2026));
        Assert.True(ReglasEnlacesActivo.ProgramadaEnEjercicio(new DateTime(2020, 1, 1), new DateTime(2020, 2, 1), null));
    }

    [Fact]
    public void La_base_de_una_linea_de_programada_se_calcula_como_en_la_factura()
    {
        // 3 × 33,33 = 99,99; −10 % = 89,991; −5 = 84,991 → 84,99 (el mismo ejemplo que el cálculo de la factura).
        Assert.Equal(84.99m, ReglasEnlacesActivo.BaseLinea(3m, 33.33m, 10m, 5m));
        Assert.Equal(900m, ReglasEnlacesActivo.BaseLinea(1m, 900m, 0m, 0m));
    }

    [Fact]
    public void Textos_de_estado_y_periodicidad()
    {
        Assert.Equal("Borrador", ReglasEnlacesActivo.EstadoVenta(3));
        Assert.Equal("Validada", ReglasEnlacesActivo.EstadoVenta(1));
        Assert.Equal("Contabilizada", ReglasEnlacesActivo.EstadoCompra(true));
        Assert.Equal("Pendiente de contabilizar", ReglasEnlacesActivo.EstadoCompra(false));
        Assert.Equal("Trimestral", ReglasEnlacesActivo.Periodicidad("TRIMESTRAL"));
        Assert.Equal("Mensual", ReglasEnlacesActivo.Periodicidad(null));
    }
}
