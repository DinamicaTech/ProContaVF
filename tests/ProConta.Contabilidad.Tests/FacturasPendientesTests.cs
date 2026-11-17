using ProConta.Contabilidad.FacturasPendientes;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo de las facturas pendientes de recibir (contabilidad/procesos/facturas-pendientes-recibir 2026-11-17 16:40), sin BD: qué
/// proveedores entran, la base supuesta (por la última factura o por el tipo), la cuenta de gasto, los ajustes a mano y los dos asientos
/// (Debe = Haber, la reversión es la provisión al revés). Datos sintéticos.
/// </summary>
public sealed class FacturasPendientesTests
{
    private static readonly ConfiguracionFacturasPendientes Cfg = ConfiguracionFacturasPendientes.Defecto;

    private static FacturaReferencia Factura(decimal bases, decimal total, string? gasto = "6230000", int? centro = null) =>
        new(new DateTime(2026, 10, 5), "F-1", bases, total, gasto, centro);

    private static void Invariantes(PropuestaFacturasPendientes p)
    {
        Assert.Equal(p.TotalDebe, p.TotalHaber);
        Assert.Equal(p.Total, p.TotalDebe);
        Assert.Equal(p.Provision.Sum(l => l.Debe), p.Reversion.Sum(l => l.Haber));
        Assert.Equal(p.Provision.Sum(l => l.Haber), p.Reversion.Sum(l => l.Debe));
        Assert.Equal(p.Provision.Count, p.Reversion.Count);
        Assert.All(p.Provision.Zip(p.Reversion), x => Assert.Equal((x.First.Cuenta, x.First.Debe, x.First.Haber), (x.Second.Cuenta, x.Second.Haber, x.Second.Debe)));
        Assert.All(p.Provision, l => Assert.True((l.Debe == 0m) != (l.Haber == 0m)));
        Assert.Equal(new DateTime(p.Ejercicio, 12, 31), p.Fecha);
        Assert.Equal(new DateTime(p.Ejercicio + 1, 1, 1), p.FechaReversion);
    }

    [Fact]
    public void Con_la_ultima_factura_de_100_mas_21_un_saldo_de_121_da_base_100()
    {
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg, [new SubcuentaProveedor("4000001", "Proveedor sintético", 121m, true, UltimaFactura: Factura(100m, 121m))]);
        var f = Assert.Single(p.Filas);
        Assert.Equal((100m, OrigenBase.UltimaFactura, "6230000", OrigenGasto.UltimaFactura), (f.Base, f.Origen, f.CuentaGasto, f.OrigenGasto));
        Assert.Equal(2, p.Provision.Count);
        Assert.Equal(("6230000", 100m, 0m), (p.Provision[0].Cuenta, p.Provision[0].Debe, p.Provision[0].Haber));
        Assert.Equal(("4009000", 0m, 100m), (p.Provision[1].Cuenta, p.Provision[1].Debe, p.Provision[1].Haber));
        Assert.Contains("Proveedor sintético", p.Provision[0].Concepto);
        Invariantes(p);
    }

    [Fact]
    public void Con_retencion_la_proporcion_sale_de_la_factura()
    {
        // Profesional: base 100, IVA 21, retención 15 → total 106. Un saldo de 212 son dos facturas sin recibir: base 200.
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg, [new SubcuentaProveedor("4100001", "Asesor sintético", 212m, true, UltimaFactura: Factura(100m, 106m, "6230000"))]);
        Assert.Equal(200m, Assert.Single(p.Filas).Base);
        // Con IVA reducido (10 %): 110 de saldo → 100 de base.
        var r = CalculoFacturasPendientes.Calcular(2026, Cfg, [new SubcuentaProveedor("4000002", "Reducido", 110m, true, UltimaFactura: Factura(50m, 55m))]);
        Assert.Equal(100m, Assert.Single(r.Filas).Base);
        Invariantes(p);
        Invariantes(r);
    }

    [Fact]
    public void Sin_facturas_usa_el_tipo_supuesto_y_la_cuenta_del_ejercicio_sin_centro()
    {
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg, [new SubcuentaProveedor("4000003", "Sin facturas", 121m, true)]);
        var f = Assert.Single(p.Filas);
        Assert.Equal((100m, OrigenBase.TipoSupuesto, "6299000", OrigenGasto.Ejercicio, (int?)null), (f.Base, f.Origen, f.CuentaGasto, f.OrigenGasto, f.IdCentroCoste));
        // Otro tipo y otras cuentas configuradas en el ejercicio; redondeo alejándose de cero: 100 / 1,1 = 90,909… → 90,91.
        var cfg = ConfiguracionFacturasPendientes.Con("4009001", "6290000", 10m);
        var q = CalculoFacturasPendientes.Calcular(2026, cfg, [new SubcuentaProveedor("4000003", "Sin facturas", 100m, true)]);
        Assert.Equal((90.91m, "6290000"), (Assert.Single(q.Filas).Base, q.Filas[0].CuentaGasto));
        Assert.Equal("4009001", q.Provision[1].Cuenta);
        Assert.Equal(0.01m, CalculoFacturasPendientes.BaseSupuesta(0.01m, null, 100m).Base); // 0,005 → 0,01
        Invariantes(p);
        Invariantes(q);
    }

    [Fact]
    public void La_cuenta_de_gasto_y_el_centro_del_proveedor_mandan_sobre_los_de_la_factura()
    {
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg,
        [
            new SubcuentaProveedor("4000004", "Con defecto", 121m, true, UltimaFactura: Factura(100m, 121m, "6230000", 7), CuentaGastoDefecto: "6280000", IdCentroDefecto: 3),
            new SubcuentaProveedor("4000005", "Solo factura", 121m, true, UltimaFactura: Factura(100m, 121m, "6220000", 7)),
        ]);
        Assert.Equal(("6280000", OrigenGasto.Proveedor, (int?)3), (p.Filas[0].CuentaGasto, p.Filas[0].OrigenGasto, p.Filas[0].IdCentroCoste));
        Assert.Equal(("6220000", OrigenGasto.UltimaFactura, (int?)7), (p.Filas[1].CuentaGasto, p.Filas[1].OrigenGasto, p.Filas[1].IdCentroCoste));
        Assert.Equal(3, p.Provision[0].IdCentroCoste);
        Assert.Null(p.Provision[1].IdCentroCoste);
        Invariantes(p);
    }

    [Fact]
    public void Los_no_cuadrados_los_de_saldo_acreedor_y_las_cuentas_que_no_son_de_proveedor_no_entran()
    {
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg,
        [
            new SubcuentaProveedor("4000010", "Sin cuadrar", 121m, false),
            new SubcuentaProveedor("4000011", "Cuadrado con otro saldo", 121m, false, CuadradaConOtroSaldo: true),
            new SubcuentaProveedor("4000012", "Acreedor", -121m, true),
            new SubcuentaProveedor("4000013", "A cero", 0.004m, true),
            new SubcuentaProveedor("4009000", "La propia 4009", 50m, true),
            new SubcuentaProveedor("4070001", "Anticipo", 50m, true),
            new SubcuentaProveedor("400001", "Seis dígitos", 50m, true),
            new SubcuentaProveedor("4300001", "Cliente", 50m, true),
            new SubcuentaProveedor("4110001", "Efectos", 121m, true),
        ]);
        var f = Assert.Single(p.Filas);
        Assert.Equal("4110001", f.Cuenta);
        Assert.Contains(p.Avisos, a => a.Contains("4000011"));
        Invariantes(p);

        var vacia = CalculoFacturasPendientes.Calcular(2026, Cfg, [new SubcuentaProveedor("4000010", "Sin cuadrar", 121m, false)]);
        Assert.Empty(vacia.Filas);
        Assert.Empty(vacia.Provision);
        Assert.Contains(vacia.Avisos, a => a.Contains("Ningún proveedor"));
    }

    [Fact]
    public void Las_exclusiones_y_bases_a_mano_se_respetan_y_se_aplican_sin_volver_a_leer()
    {
        var subcuentas = new[]
        {
            new SubcuentaProveedor("4000020", "Uno", 121m, true),
            new SubcuentaProveedor("4000021", "Dos", 242m, true),
            new SubcuentaProveedor("4000022", "Tres", 363m, true),
        };
        var ajustes = new[] { new AjusteFacturaPendiente("4000021", true, null), new AjusteFacturaPendiente("4000022", false, 250m), new AjusteFacturaPendiente("4000099", true, null) };
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg, subcuentas, ajustes);
        Assert.Equal([true, false, true], p.Filas.Select(f => f.Incluido));
        Assert.Equal((300m, 250m), (p.Filas[2].BaseCalculada, p.Filas[2].Base));
        Assert.Equal(350m, p.Total);
        Assert.Equal(4, p.Provision.Count);
        // Los ajustes de la propuesta son los que se guardan (el del proveedor que ya no está se pierde).
        Assert.Equal([new AjusteFacturaPendiente("4000021", true, null), new AjusteFacturaPendiente("4000022", false, 250m)], p.Ajustes);
        Invariantes(p);

        // Desde la pantalla: se vuelve a incluir el 2 y la base del 3 vuelve a la calculada (deja de ser manual).
        var q = CalculoFacturasPendientes.Aplicar(p, [new AjusteFacturaPendiente("4000022", false, 300m)]);
        Assert.True(q.Filas.All(f => f.Incluido && f.BaseManual is null));
        Assert.Equal(600m, q.Total);
        Assert.Empty(q.Ajustes);
        Invariantes(q);
        Assert.False(p.Igual(q));
        Assert.True(q.Igual(CalculoFacturasPendientes.Calcular(2026, Cfg, subcuentas)));

        // Todos excluidos: sin asiento.
        var nada = CalculoFacturasPendientes.Aplicar(p, subcuentas.Select(s => new AjusteFacturaPendiente(s.Cuenta, true, null)));
        Assert.Empty(nada.Provision);
        Assert.Contains(nada.Avisos, a => a.Contains("excluidos"));
    }

    [Fact]
    public void Una_base_mayor_que_el_saldo_avisa()
    {
        var p = CalculoFacturasPendientes.Calcular(2026, Cfg, [new SubcuentaProveedor("4000030", "Uno", 121m, true)], [new AjusteFacturaPendiente("4000030", false, 130m)]);
        Assert.Contains(p.Avisos, a => a.Contains("mayor que el saldo"));
        Invariantes(p);
    }
}
