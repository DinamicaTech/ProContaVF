using ProConta.Contabilidad.Saldos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Comparación de la caché de saldos con los apuntes, sin base de datos (contabilidad/procesos/recalcular-saldos): saldo correcto =
/// saldo inicial + Debe − Haber, número de apuntes, códigos sin espacios, cuentas sin apuntes a cero y apuntes de cuentas que no están
/// en el plan del ejercicio. Datos inventados.
/// </summary>
public sealed class RecalcularSaldosTests
{
    [Fact]
    public void Solo_salen_las_cuentas_cuya_cache_no_coincide()
    {
        var cuentas = new[]
        {
            new CacheCuenta("5720001", "Banco", 0m, 240m, 2),       // bien
            new CacheCuenta("4300001", "Cliente", 0m, 121.00m, 1),  // tocada a mano: los apuntes dan 120,99
            new CacheCuenta("7000001", "Ventas", 0m, 0m, 0),        // sin caché
            new CacheCuenta("6290001", "Otros", 0m, 5m, 1),         // ya no tiene apuntes
            new CacheCuenta("572", "Bancos", 0m, 0m, 0),            // cuenta padre: sin apuntes, bien
        };
        var movimientos = new[]
        {
            new MovimientosCuenta("5720001", 240m, 2),
            new MovimientosCuenta("4300001", 120.99m, 1),
            new MovimientosCuenta("7000001", -360.99m, 3),
        };

        var c = ComparacionSaldos.Comparar(cuentas, movimientos);

        Assert.Equal(5, c.CuentasRevisadas);
        Assert.False(c.Cuadra);
        Assert.Equal(["4300001", "6290001", "7000001"], c.Diferencias.Select(d => d.Codigo));
        Assert.Equal(0.01m, c.Diferencias[0].Diferencia);
        Assert.Equal((5m, 0m, 1, 0), (c.Diferencias[1].SaldoGuardado, c.Diferencias[1].SaldoApuntes, c.Diferencias[1].ApuntesGuardados, c.Diferencias[1].ApuntesReales));
        Assert.Equal(360.99m, c.Diferencias[2].Diferencia);
        Assert.Empty(c.SinAlta);
        // Los apuntes cuadran: la suma de Debe − Haber del ejercicio es cero.
        Assert.Equal(0m, movimientos.Sum(m => m.Saldo));
    }

    [Fact]
    public void Cuenta_solo_con_diferencia_en_el_numero_de_apuntes()
    {
        var c = ComparacionSaldos.Comparar([new CacheCuenta("5720001", "Banco", 0m, 0m, 0)], [new MovimientosCuenta("5720001", 0m, 2)]);
        Assert.Equal(["5720001"], c.Diferencias.Select(d => d.Codigo));
        Assert.Equal(0m, c.Diferencias[0].Diferencia);
    }

    [Fact]
    public void El_saldo_inicial_se_suma_a_los_apuntes()
    {
        var c = ComparacionSaldos.Comparar([new CacheCuenta("1000001", "Capital", -3000m, -3000m, 0)], []);
        Assert.True(c.Cuadra);
        c = ComparacionSaldos.Comparar([new CacheCuenta("1000001", "Capital", -3000m, -3000m, 0)], [new MovimientosCuenta("1000001", -100m, 1)]);
        Assert.Equal(-3100m, Assert.Single(c.Diferencias).SaldoApuntes);
    }

    [Fact]
    public void Los_codigos_se_comparan_sin_espacios_y_se_suman_si_se_repiten()
    {
        var c = ComparacionSaldos.Comparar(
            [new CacheCuenta(" 4100001 ", "Proveedor", 0m, -50m, 2)],
            [new MovimientosCuenta("4100001", -30m, 1), new MovimientosCuenta("4100001 ", -20m, 1), new MovimientosCuenta(" ", 7m, 1)]);
        Assert.True(c.Cuadra);
        Assert.Empty(c.SinAlta);
    }

    [Fact]
    public void Los_apuntes_de_cuentas_que_no_estan_en_el_plan_se_avisan_aparte()
    {
        var c = ComparacionSaldos.Comparar([new CacheCuenta("5720001", "Banco", 0m, 100m, 1)],
            [new MovimientosCuenta("5720001", 100m, 1), new MovimientosCuenta("7050009", -100m, 1)]);
        Assert.True(c.Cuadra);
        var sin = Assert.Single(c.SinAlta);
        Assert.Equal(("7050009", -100m, 1), (sin.Codigo, sin.Saldo, sin.NumeroApuntes));
    }
}
