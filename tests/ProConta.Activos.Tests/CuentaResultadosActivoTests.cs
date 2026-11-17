using ProConta.Activos.Gestion;
using ProConta.Contabilidad.Impuestos;

namespace ProConta.Activos.Tests;

/// <summary>Cuenta de resultados de un activo sin BD (activos 2026-11-17 18:50).</summary>
public sealed class CuentaResultadosActivoTests
{
    private static readonly CriterioActivo Piso = new(new HashSet<string> { "7520001", "6250001", "6820001" }, 30, false);
    private static string Nombre(string c) => "Cuenta " + c;
    private static ApunteResultado A(string cuenta, int mes, decimal debe, decimal haber, int? centro = null, int tipo = 0, bool firme = true) =>
        new(cuenta, new DateTime(2026, mes, 10), debe, haber, centro, tipo, firme);

    [Fact]
    public void Clasifica_ingresos_del_grupo_7_y_gastos_del_grupo_6_por_subcuenta()
    {
        var c = CalculoCuentaResultados.Calcular(2026, [
            A("7520001", 1, 0m, 1000m), A("7520001", 2, 0m, 1000m), A("7520001", 3, 50m, 0m), // un abono resta
            A("6250001", 3, 200m, 0m),
            A("6290000", 4, 80m, 0m, centro: 30), // gasto general con el centro del piso
            A("4300001", 1, 1000m, 0m, centro: 30), // grupo 4: no entra
        ], Piso, Nombre);
        Assert.Equal(1950m, c.Ingresos);
        Assert.Equal(280m, c.Gastos);
        Assert.Equal(1670m, c.Resultado);
        Assert.Equal(["7520001", "6250001", "6290000"], c.Filas.Select(f => f.Cuenta));
        Assert.True(c.Filas[0].EsIngreso);
        Assert.Equal(-50m, c.Filas[0].Meses[2]);
        Assert.Equal("Cuenta 6290000", c.Filas[2].Nombre);
        // La suma de los meses es el total de cada fila y el de la cuenta.
        Assert.Equal(c.Ingresos, Enumerable.Range(0, 12).Sum(c.IngresosMes));
        Assert.Equal(c.Gastos, Enumerable.Range(0, 12).Sum(c.GastosMes));
    }

    [Fact]
    public void Excluye_regularizacion_cierre_borradores_y_cuentas_del_impuesto()
    {
        var c = CalculoCuentaResultados.Calcular(2026, [
            A("7520001", 12, 3000m, 0m, tipo: 2), // regularización
            A("7520001", 12, 0m, 3000m, tipo: 3), // cierre
            A("7520001", 5, 0m, 700m, firme: false), // asiento no en firme
            A("6300000", 12, 900m, 0m, centro: 30), // impuesto sobre beneficios
            A("6330000", 12, 10m, 0m, centro: 30),
            A("6380000", 12, 10m, 0m, centro: 30),
            A("7520001", 6, 0m, 500m),
        ], Piso, Nombre);
        Assert.Equal(500m, c.Ingresos);
        Assert.Equal(0m, c.Gastos);
        Assert.Single(c.Filas);
    }

    [Fact]
    public void Con_centro_compartido_solo_cuentan_sus_subcuentas()
    {
        var compartido = Piso with { CentroCompartido = true };
        var apuntes = new[] { A("6250001", 1, 100m, 0m, centro: 30), A("6290000", 1, 80m, 0m, centro: 30), A("7050000", 1, 0m, 40m, centro: 30) };
        Assert.Equal(180m, CalculoCuentaResultados.Calcular(2026, apuntes, Piso, Nombre).Gastos);
        var c = CalculoCuentaResultados.Calcular(2026, apuntes, compartido, Nombre);
        Assert.Equal(100m, c.Gastos);
        Assert.Equal(0m, c.Ingresos);
        // Un activo sin centro (ajeno) solo cuenta sus subcuentas.
        Assert.Equal(100m, CalculoCuentaResultados.Calcular(2026, apuntes, Piso with { IdCentroCoste = null }, Nombre).Gastos);
    }

    [Fact]
    public void La_amortizacion_prevista_va_en_diciembre_y_suma_al_resultado()
    {
        var c = CalculoCuentaResultados.Calcular(2026, [A("7520001", 1, 0m, 1000m)], Piso, Nombre, amortizacionPrevista: 312.45m);
        var prevista = Assert.Single(c.Filas, f => f.Tipo == TipoFilaResultado.AmortizacionPrevista);
        Assert.Equal(CalculoCuentaResultados.TextoPrevista, prevista.Nombre);
        Assert.False(prevista.EsIngreso);
        Assert.Equal(312.45m, prevista.Meses[11]);
        Assert.Equal(312.45m, prevista.Total);
        Assert.Equal(687.55m, c.Resultado);
        // El gráfico no lleva la amortización.
        Assert.Equal(0m, c.GastosMesSinAmortizacion(11));
        // Ya contabilizada del todo: no hay fila.
        Assert.DoesNotContain(CalculoCuentaResultados.Calcular(2026, [], Piso, Nombre, 0m).Filas, f => f.Tipo == TipoFilaResultado.AmortizacionPrevista);
    }

    [Fact]
    public void Gastos_generales_en_proporcion_a_los_ingresos_ordinarios_a_centimos()
    {
        var g = new GastosGeneralesSociedad(10000m, 30000m);
        Assert.Equal(3333.33m, CalculoCuentaResultados.ParteGastosGenerales(g, 10000m));
        Assert.Equal(0.01m, CalculoCuentaResultados.ParteGastosGenerales(new GastosGeneralesSociedad(1m, 300m), 1.5m)); // 0,005 → 0,01 (al alza en el medio)
        Assert.Equal(0m, CalculoCuentaResultados.ParteGastosGenerales(g, 0m)); // sin ingresos, nada
        Assert.Equal(0m, CalculoCuentaResultados.ParteGastosGenerales(g, -10m));
        Assert.Equal(0m, CalculoCuentaResultados.ParteGastosGenerales(new GastosGeneralesSociedad(500m, 0m), 100m));
    }

    [Fact]
    public void Solo_los_ingresos_70_a_75_reparten_gastos_generales_y_por_meses_suman_exacto()
    {
        var apuntes = new List<ApunteResultado>();
        foreach (var mes in new[] { 1, 2, 3, 4, 5, 6, 7 }) apuntes.Add(A("7520001", mes, 0m, 1000m));
        apuntes.Add(A("7690000", 3, 0m, 5000m, centro: 30)); // financiero: no es ordinario
        var g = new GastosGeneralesSociedad(1000m, 21000m);
        var c = CalculoCuentaResultados.Calcular(2026, apuntes, Piso, Nombre, generales: g);
        Assert.Equal(7000m, c.IngresosOrdinarios);
        var fila = Assert.Single(c.Filas, f => f.Tipo == TipoFilaResultado.GastosGenerales);
        Assert.Equal(CalculoCuentaResultados.TextoGenerales, fila.Nombre);
        Assert.Equal(333.33m, fila.Total); // 1000 × 7000 / 21000
        Assert.Equal(333.33m, fila.Meses.Sum());
        Assert.All(fila.Meses.Take(7), m => Assert.InRange(m, 47.61m, 47.62m));
        Assert.All(fila.Meses.Skip(7), m => Assert.Equal(0m, m));
        Assert.Equal(c.Ingresos - c.Gastos, c.Resultado);
        Assert.Equal(12000m - 333.33m, c.Resultado);
    }

    [Fact]
    public void Un_activo_sin_ingresos_no_recibe_gastos_generales()
    {
        var c = CalculoCuentaResultados.Calcular(2026, [A("6250001", 1, 100m, 0m)], Piso, Nombre, generales: new GastosGeneralesSociedad(1000m, 21000m));
        Assert.DoesNotContain(c.Filas, f => f.Tipo == TipoFilaResultado.GastosGenerales);
    }

    [Fact]
    public void Reparto_por_meses_con_resto_mayor_y_meses_negativos_a_cero()
    {
        var r = CalculoCuentaResultados.RepartirPorMeses(100m, [1m, 1m, 1m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, -5m]);
        Assert.Equal(100m, r.Sum());
        Assert.Equal([33.34m, 33.33m, 33.33m], r.Take(3));
        Assert.Equal(0m, r[11]);
        var negativo = CalculoCuentaResultados.RepartirPorMeses(-10m, [1m, 2m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m]);
        Assert.Equal(-10m, negativo.Sum());
        Assert.Equal(12, CalculoCuentaResultados.RepartirPorMeses(0m, new decimal[12]).Count);
    }

    [Fact]
    public void Compara_con_el_ejercicio_anterior_por_cuenta()
    {
        var actual = CalculoCuentaResultados.Calcular(2026, [A("7520001", 1, 0m, 1200m), A("6250001", 2, 300m, 0m)], Piso, Nombre, 100m);
        var anterior = CalculoCuentaResultados.Calcular(2025, [A("7520001", 1, 0m, 1000m), A("6820001", 12, 100m, 0m)], Piso, Nombre);
        var filas = CalculoCuentaResultados.Comparar(actual, anterior);
        var ingreso = filas.Single(f => f.Cuenta == "7520001");
        Assert.Equal(1200m, ingreso.Actual);
        Assert.Equal(1000m, ingreso.Anterior);
        Assert.Equal(200m, ingreso.Diferencia);
        Assert.Equal(0m, filas.Single(f => f.Cuenta == "6250001").Anterior);
        var soloAnterior = filas.Single(f => f.Cuenta == "6820001");
        Assert.Equal(0m, soloAnterior.Actual);
        Assert.Equal(100m, soloAnterior.Anterior);
        Assert.Null(CalculoCuentaResultados.Comparar(actual, null)[0].Anterior);
        Assert.Contains(filas, f => f.Tipo == TipoFilaResultado.AmortizacionPrevista && f.Actual == 100m);
    }
}
