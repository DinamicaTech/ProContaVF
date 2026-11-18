using ProConta.Contabilidad.Balances;

namespace ProConta.Contabilidad.Tests;

/// <summary>Balance de sumas y saldos (contabilidad/balances/sumas-y-saldos): cálculo sin BD con sus invariantes.</summary>
public sealed class SumasYSaldosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 18, 19, 30, 0);

    private static readonly Dictionary<string, string> Plan = new(StringComparer.Ordinal)
    {
        ["100"] = "CAPITAL SOCIAL", ["1000000"] = "Capital",
        ["430"] = "CLIENTES", ["4300001"] = "Cliente uno", ["4300002"] = "Cliente dos",
        ["477"] = "H.P. IVA REPERCUTIDO", ["4770021"] = "IVA repercutido 21",
        ["572"] = "BANCOS", ["5720001"] = "Banco",
        ["700"] = "VENTAS", ["7000001"] = "Ventas",
    };

    /// <summary>Asientos cuadrados: apertura, dos facturas, un cobro. Debe = Haber en total.</summary>
    private static readonly MovimientoSubcuenta[] Movimientos =
    [
        new("5720001", 1000m, 0m), new("1000000", 0m, 1000m),
        new("4300001", 1210m, 0m), new("7000001", 0m, 1000m), new("4770021", 0m, 210m),
        new("4300002", 605m, 0m), new("7000001", 0m, 500m), new("4770021", 0m, 105m),
        new("5720001", 1210m, 0m), new("4300001", 0m, 1210m),
        new("9999999", 0m, 0m),
    ];

    private static ResultadoSumasYSaldos Calcular(params NivelSumasYSaldos[] niveles) =>
        CalculadoraSumasYSaldos.Calcular("Sociedad sintética", 2026,
            new ParametrosSumasYSaldos(new(2026, 1, 1), new(2026, 12, 31), niveles), Movimientos, Plan, Ahora);

    [Fact]
    public void Las_sumas_del_debe_igualan_a_las_del_haber_y_los_saldos_cuadran()
    {
        var r = Calcular(NivelSumasYSaldos.Grupo, NivelSumasYSaldos.Subgrupo, NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta);
        Assert.Equal(r.TotalDebe, r.TotalHaber);
        Assert.Equal(4025m, r.TotalDebe);
        Assert.Equal(r.TotalSaldoDeudor, r.TotalSaldoAcreedor);
        foreach (var nivel in Enum.GetValues<NivelSumasYSaldos>())
        {
            var filas = r.Filas.Where(f => f.Nivel == nivel).ToList();
            Assert.Equal(filas.Sum(f => f.Debe), filas.Sum(f => f.Haber));
            Assert.Equal(filas.Sum(f => f.SaldoDeudor) - filas.Sum(f => f.SaldoAcreedor), filas.Sum(f => f.Debe - f.Haber));
        }
    }

    [Fact]
    public void Cada_nivel_suma_sus_subcuentas()
    {
        var r = Calcular(NivelSumasYSaldos.Grupo, NivelSumasYSaldos.Subgrupo, NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta);
        var subcuentas = r.Filas.Where(f => f.Nivel == NivelSumasYSaldos.Subcuenta).ToList();
        foreach (var f in r.Filas.Where(f => f.Nivel != NivelSumasYSaldos.Subcuenta))
        {
            var hijas = subcuentas.Where(s => s.Cuenta.StartsWith(f.Cuenta, StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(hijas);
            Assert.Equal(hijas.Sum(h => h.Debe), f.Debe);
            Assert.Equal(hijas.Sum(h => h.Haber), f.Haber);
        }
        var c430 = Assert.Single(r.Filas, f => f.Cuenta == "430");
        Assert.Equal((1815m, 1210m, 605m, 0m), (c430.Debe, c430.Haber, c430.SaldoDeudor, c430.SaldoAcreedor));
        Assert.Equal("CLIENTES", c430.Nombre);
    }

    [Fact]
    public void Las_filas_van_en_orden_de_codigo_con_cada_nivel_antes_de_sus_hijos_y_sin_cuentas_sin_movimiento()
    {
        var r = Calcular(NivelSumasYSaldos.Grupo, NivelSumasYSaldos.Subgrupo, NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta);
        Assert.Equal(["1", "10", "100", "1000000", "4", "43", "430", "4300001", "4300002", "47", "477", "4770021", "5", "57", "572", "5720001", "7", "70", "700", "7000001"],
            r.Filas.Select(f => f.Cuenta));
        Assert.DoesNotContain(r.Filas, f => f.Cuenta.StartsWith('9'));
        Assert.Equal("ADMINISTRACIONES PÚBLICAS", r.Filas.Single(f => f.Cuenta == "47").Nombre);
        Assert.Equal(6, r.NumeroSubcuentas);
    }

    [Fact]
    public void Los_totales_son_los_del_nivel_mas_detallado_que_se_muestra()
    {
        var r = Calcular(NivelSumasYSaldos.Cuenta);
        Assert.All(r.Filas, f => Assert.Equal(NivelSumasYSaldos.Cuenta, f.Nivel));
        Assert.Equal(5, r.Filas.Count);
        Assert.Equal(r.Filas.Sum(f => f.SaldoDeudor), r.TotalSaldoDeudor);
        Assert.Equal(r.Filas.Sum(f => f.SaldoAcreedor), r.TotalSaldoAcreedor);
        Assert.Equal(2815m, r.TotalSaldoDeudor); // 430: 605 + 572: 2210
        Assert.Equal("cuentas", r.TextoNiveles);
    }

    [Fact]
    public void Una_subcuenta_fuera_del_plan_lo_dice_en_su_nombre()
    {
        var r = CalculadoraSumasYSaldos.Calcular("S", 2026, new(new(2026, 1, 1), new(2026, 12, 31), [NivelSumasYSaldos.Subcuenta]),
            [new("6290001", 10m, 0m), new("5720001", 0m, 10m)], Plan, Ahora);
        Assert.Equal(CalculadoraSumasYSaldos.FueraDelPlan, r.Filas.Single(f => f.Cuenta == "6290001").Nombre);
    }

    [Fact]
    public void Los_parametros_se_validan_con_un_mensaje_que_dice_como_arreglarlos()
    {
        Assert.Null(ParametrosSumasYSaldos.PorDefecto(2026, Ahora).Error(2026));
        Assert.Equal(new DateTime(2026, 11, 18), ParametrosSumasYSaldos.PorDefecto(2026, Ahora).Hasta);
        Assert.Equal(new DateTime(2025, 12, 31), ParametrosSumasYSaldos.PorDefecto(2025, Ahora).Hasta);
        Assert.Contains("cámbialas de orden", new ParametrosSumasYSaldos(new(2026, 5, 1), new(2026, 4, 1), [NivelSumasYSaldos.Cuenta]).Error(2026));
        Assert.Contains("ejercicio de trabajo", new ParametrosSumasYSaldos(new(2025, 5, 1), new(2026, 4, 1), [NivelSumasYSaldos.Cuenta]).Error(2026));
        Assert.Contains("al menos un nivel", new ParametrosSumasYSaldos(new(2026, 1, 1), new(2026, 4, 1), []).Error(2026));
    }

    [Fact]
    public void El_nombre_del_fichero_lleva_el_periodo_y_la_hora()
    {
        var r = Calcular(NivelSumasYSaldos.Cuenta);
        Assert.Equal("Sumas y saldos 2026 01-01 a 31-12 (20261118-1930).pdf", CalculadoraSumasYSaldos.NombreFichero(r, ".pdf"));
    }
}
