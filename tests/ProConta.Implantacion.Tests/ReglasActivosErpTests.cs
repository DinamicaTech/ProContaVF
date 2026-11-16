using ProConta.Activos;
using ProConta.Implantacion.ERP;

namespace ProConta.Implantacion.Tests;

/// <summary>Reglas de la fase 6 (activos) de la migración del ERP, sin BD (docs/activos.md, fase 1).</summary>
public sealed class ReglasActivosErpTests
{
    [Theory]
    [InlineData(3, RolesSubcuentaActivo.Activo)]
    [InlineData(2, RolesSubcuentaActivo.Terreno)]
    [InlineData(9, RolesSubcuentaActivo.AmortizacionAcumulada)]
    [InlineData(24, RolesSubcuentaActivo.GastoAmortizacion)]
    [InlineData(8, RolesSubcuentaActivo.IngresoAlquiler)]
    [InlineData(4, RolesSubcuentaActivo.Libre)]
    [InlineData(11, RolesSubcuentaActivo.Libre)]
    public void El_rol_sale_del_numero_de_linea_del_ERP(int linea, string rol) => Assert.Equal(rol, EjecucionErp.RolDeLineaErp(linea));

    [Fact]
    public void Cada_rol_tiene_un_texto_descriptivo_y_los_fijos_son_cinco()
    {
        Assert.Equal(5, RolesSubcuentaActivo.Fijos.Count);
        Assert.All(RolesSubcuentaActivo.Fijos.Append(RolesSubcuentaActivo.Libre), r => Assert.False(string.IsNullOrWhiteSpace(RolesSubcuentaActivo.Textos[r])));
        Assert.False(RolesSubcuentaActivo.EsFijo(RolesSubcuentaActivo.Libre));
        Assert.Equal("Valor del bien (se amortiza)", RolesSubcuentaActivo.Textos[RolesSubcuentaActivo.Activo]);
    }

    [Theory]
    [InlineData("2060000", TiposActivoPgc.Aplicacion)]
    [InlineData("2120001", TiposActivoPgc.Instalacion)]
    [InlineData(null, TiposActivoPgc.Instalacion)]
    public void Los_tecnicos_del_ERP_se_reparten_por_su_subcuenta(string? cuenta, string tipo) => Assert.Equal(tipo, EjecucionErp.TipoDeTecnico(cuenta));

    [Fact]
    public void Todos_los_tipos_del_ERP_salvo_inversiones_tienen_tipo_del_PGC()
    {
        foreach (var t in new[] { 1, 2, 3, 4, 5, 7, 9 })
            Assert.Contains(EjecucionErp.TiposActivoErp[t], TiposActivoPgc.Todos);
        Assert.False(EjecucionErp.TiposActivoErp.ContainsKey(6));
        Assert.Equal(TiposActivoPgc.Vivienda, EjecucionErp.TiposActivoErp[9]); // piso particular: vivienda ajena
    }

    [Fact]
    public void Los_prefijos_corregidos_siguen_el_PGC_y_casan_con_las_subcuentas_reales()
    {
        Assert.Equal("21800", EjecucionErp.PrefijosPgc[TiposActivoPgc.Vehiculo][RolesSubcuentaActivo.Activo]);
        Assert.StartsWith(EjecucionErp.PrefijosPgc[TiposActivoPgc.Vehiculo][RolesSubcuentaActivo.AmortizacionAcumulada], "2818080");
        Assert.StartsWith(EjecucionErp.PrefijosPgc[TiposActivoPgc.Instalacion][RolesSubcuentaActivo.Activo], "2120001");
        Assert.StartsWith(EjecucionErp.PrefijosPgc[TiposActivoPgc.Aplicacion][RolesSubcuentaActivo.AmortizacionAcumulada], "2806000");
        Assert.Equal("6800", EjecucionErp.PrefijosPgc[TiposActivoPgc.Aplicacion][RolesSubcuentaActivo.GastoAmortizacion]);
        Assert.All(EjecucionErp.PrefijosPgc.Keys, t => Assert.False(TiposActivoPgc.EsInmueble(t)));
    }

    [Theory]
    [InlineData(262900.0, 239000.0, 0.195, 51265.50)]   // coste de compra por el % de construcción
    [InlineData(null, 6109.0, 1.0, 6109.00)]           // sin coste: el valor de compra
    [InlineData(6640.0, 6640.0, null, 6640.00)]          // sin % de construcción: todo amortizable
    [InlineData(1000.0, 1000.0, 1.5, 1000.00)]           // % fuera de 0–1: se ignora
    public void La_base_amortizable_es_el_coste_por_el_porcentaje_de_construccion(double? coste, double? valor, double? pct, double esperado) =>
        Assert.Equal((decimal)esperado, EjecucionErp.BaseAmortizable((decimal?)coste, (decimal?)valor, (decimal?)pct));

    [Theory]
    [InlineData(0.02, 600)]
    [InlineData(0.16, 75)]
    [InlineData(0.25, 48)]
    [InlineData(0.07, 171)]
    public void La_vida_util_sale_del_porcentaje_anual(double pct, int meses) => Assert.Equal(meses, EjecucionErp.VidaUtilMeses((decimal)pct));
}
