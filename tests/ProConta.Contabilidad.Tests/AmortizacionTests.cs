using ProConta.Contabilidad.Amortizacion;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo y propuesta de la amortización sin base de datos (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59): días
/// reales (366 en bisiesto), tope en lo pendiente, lo ya contabilizado se descuenta, doce cuotas mensuales suman la anual, el asiento
/// cuadra y los activos con problemas o excluidos no generan líneas. Datos inventados.
/// </summary>
public sealed class AmortizacionTests
{
    private static PeriodoAmortizacion Ano(int ejercicio) => PeriodoAmortizacion.DelEjercicio(ejercicio, PeriodicidadAmortizacion.Anual)[0];

    private static ActivoAmortizacion Activo(int id, PlanAmortizacion? plan, string gasto = "6820001", string acumulada = "2820001", string? problema = null, int? centro = 7) =>
        new(id, $"ACT-{id}", $"Activo {id}", centro, plan, gasto, acumulada, problema);

    private static void Cuadra(PropuestaAmortizacion p) => Assert.Equal(p.Lineas.Sum(l => l.Debe), p.Lineas.Sum(l => l.Haber));

    [Fact]
    public void Periodos_y_claves()
    {
        Assert.Equal(["2026"], PeriodoAmortizacion.DelEjercicio(2026, PeriodicidadAmortizacion.Anual).Select(p => p.Clave));
        var t = PeriodoAmortizacion.DelEjercicio(2026, PeriodicidadAmortizacion.Trimestral);
        Assert.Equal(["2026-T1", "2026-T2", "2026-T3", "2026-T4"], t.Select(p => p.Clave));
        Assert.Equal(new DateTime(2026, 6, 30), t[1].Hasta);
        var m = PeriodoAmortizacion.DelEjercicio(2028, PeriodicidadAmortizacion.Mensual);
        Assert.Equal(12, m.Count);
        Assert.Equal(new DateTime(2028, 2, 29), m[1].Hasta);
        Assert.Equal("2028-02", m[1].Clave);
        Assert.Equal("febrero 2028", m[1].Nombre);
        Assert.Equal(PeriodicidadAmortizacion.Anual, PeriodoAmortizacion.Leer(null));
        Assert.Equal(PeriodicidadAmortizacion.Mensual, PeriodoAmortizacion.Leer(" mensual "));
    }

    [Fact]
    public void Año_completo_es_la_cuota_anual()
    {
        var c = CalculoAmortizacion.Calcular(new PlanAmortizacion(new DateTime(2010, 5, 3), 100_000m, 0m, 0.03m), Ano(2026), 0m, 20_000m);
        Assert.Equal((365, 365, 3_000m, 3_000m, 80_000m), (c.Dias, c.DiasAno, c.HastaFinPeriodo, c.Importe, c.Pendiente));
    }

    [Fact]
    public void Primer_año_por_dias_reales_en_bisiesto()
    {
        // Del 1/7/2028 al 31/12/2028: 184 de 366 días. 36.600 × 10 % = 3.660 al año → 1.840,00.
        var c = CalculoAmortizacion.Calcular(new PlanAmortizacion(new DateTime(2028, 7, 1), 36_600m, 0m, 0.10m), Ano(2028), 0m, 0m);
        Assert.Equal((184, 366, 1_840m), (c.Dias, c.DiasAno, c.Importe));
        // Un plan que empieza después del año no toca nada.
        Assert.Equal(0, CalculoAmortizacion.Dias(new PlanAmortizacion(new DateTime(2029, 1, 1), 1m, 0m, 0.1m), 2028, new DateTime(2028, 12, 31)));
    }

    [Fact]
    public void La_baja_corta_los_dias()
    {
        // Baja el 30/6/2026: 181 días. 73.000 × 5 % = 3.650 al año → 1.810,00.
        var c = CalculoAmortizacion.Calcular(new PlanAmortizacion(new DateTime(2020, 1, 1), 73_000m, 0m, 0.05m, new DateTime(2026, 6, 30)), Ano(2026), 0m, 0m);
        Assert.Equal((181, 1_810m), (c.Dias, c.Importe));
    }

    [Fact]
    public void Nunca_supera_lo_pendiente()
    {
        var c = CalculoAmortizacion.Calcular(new PlanAmortizacion(new DateTime(2016, 1, 1), 1_000m, 100m, 0.10m), Ano(2026), 0m, 880m);
        Assert.Equal((100m, 20m, 20m), (c.Cuota, c.Pendiente, c.Importe));
        Assert.True(c.Topada);
        var fin = CalculoAmortizacion.Calcular(new PlanAmortizacion(new DateTime(2016, 1, 1), 1_000m, 100m, 0.10m), Ano(2026), 0m, 900m);
        Assert.Equal(0m, fin.Importe);
        Assert.True(fin.TotalmenteAmortizado);
    }

    [Fact]
    public void Lo_ya_contabilizado_a_mano_no_se_duplica()
    {
        var plan = new PlanAmortizacion(new DateTime(2010, 1, 1), 100_000m, 0m, 0.03m);
        var movimientos = new[] { new MovimientoAmortizacion("6820001", new DateTime(2026, 12, 31), 3_000m, 0m), new MovimientoAmortizacion("2820001", new DateTime(2026, 12, 31), 0m, 3_000m) };
        var p = ProcesoAmortizacion.Proponer(Ano(2026), [Activo(1, plan)], movimientos, new HashSet<int>());
        Assert.Empty(p.Lineas);
        Assert.Equal(3_000m, p.Filas.Single().Cuota!.YaContabilizado);
        // A medias: falta la diferencia.
        var medio = ProcesoAmortizacion.Proponer(Ano(2026), [Activo(1, plan)], [new MovimientoAmortizacion("6820001", new DateTime(2026, 6, 30), 1_000m, 0m)], new HashSet<int>());
        Assert.Equal(2_000m, medio.Total);
        Cuadra(medio);
    }

    [Fact]
    public void Doce_cuotas_mensuales_suman_la_anual_y_cada_asiento_cuadra()
    {
        // 10.000 × 7 % = 700 al año: 700 × 31 / 365 no es exacto, pero la suma de los doce meses sí lo es.
        var activos = new[] { Activo(1, new PlanAmortizacion(new DateTime(2015, 1, 1), 10_000m, 0m, 0.07m)),
            Activo(2, new PlanAmortizacion(new DateTime(2026, 3, 17), 12_345.67m, 0m, 0.12m), "6810002", "2810002") };
        var movimientos = new List<MovimientoAmortizacion>();
        foreach (var periodo in PeriodoAmortizacion.DelEjercicio(2026, PeriodicidadAmortizacion.Mensual))
        {
            var p = ProcesoAmortizacion.Proponer(periodo, activos, movimientos, new HashSet<int>());
            Cuadra(p);
            movimientos.AddRange(p.Lineas.Select(l => new MovimientoAmortizacion(l.Cuenta, periodo.Hasta, l.Debe, l.Haber, periodo.Clave)));
        }
        Assert.Equal(700m, movimientos.Where(m => m.Cuenta == "6820001").Sum(m => m.Debe));
        var anual = CalculoAmortizacion.Calcular(activos[1].Plan!, Ano(2026), 0m, 0m).Importe;
        Assert.Equal(anual, movimientos.Where(m => m.Cuenta == "6810002").Sum(m => m.Debe));
        // Repetir un mes ya grabado da lo mismo: su propio lote no cuenta como ya contabilizado.
        var marzo = PeriodoAmortizacion.DelEjercicio(2026, PeriodicidadAmortizacion.Mensual)[2];
        var otraVez = ProcesoAmortizacion.Proponer(marzo, activos, movimientos, new HashSet<int>());
        Assert.Equal(movimientos.Where(m => m.ClaveLote == marzo.Clave).Sum(m => m.Debe), otraVez.Total);
    }

    [Fact]
    public void Problemas_y_excluidos_no_generan_lineas()
    {
        var plan = new PlanAmortizacion(new DateTime(2010, 1, 1), 10_000m, 0m, 0.10m);
        var activos = new[] { Activo(1, plan), Activo(2, plan, "6820002", "2820002"), Activo(3, null, problema: "no tiene plan de amortización activo") };
        var p = ProcesoAmortizacion.Proponer(Ano(2026), activos, [], new HashSet<int> { 2 });
        Assert.Equal(3, p.Filas.Count);
        Assert.Equal([1], p.Incluidas.Select(f => f.Activo.IdActivo));
        Assert.Equal(1_000m, p.Total);
        Assert.Equal([2], p.Excluidos);
        Assert.Equal(2, p.Lineas.Count);
        Assert.Equal(7, p.Lineas[0].IdCentroCoste);
        Assert.Null(p.Lineas[1].IdCentroCoste);
        Assert.Contains(p.Avisos, a => a.Contains("ACT-3"));
        Cuadra(p);
        // Volver a marcar el excluido sin leer nada.
        var todos = ProcesoAmortizacion.Aplicar(p, new HashSet<int>());
        Assert.Equal(2_000m, todos.Total);
        Cuadra(todos);
        Assert.False(p.Igual(todos));
    }

    [Fact]
    public void Subcuentas_compartidas_reparten_lo_contabilizado()
    {
        // Dos activos con la misma 68 y 28: lo cargado (1.500) se reparte según lo que toca a cada uno (1.000 y 500).
        var a = Activo(1, new PlanAmortizacion(new DateTime(2010, 1, 1), 10_000m, 0m, 0.10m));
        var b = Activo(2, new PlanAmortizacion(new DateTime(2010, 1, 1), 5_000m, 0m, 0.10m));
        var p = ProcesoAmortizacion.Proponer(Ano(2026), [a, b], [new MovimientoAmortizacion("6820001", new DateTime(2026, 12, 31), 1_500m, 0m)], new HashSet<int>());
        Assert.Equal(0m, p.Total);
        Assert.Contains(p.Avisos, x => x.Contains("6820001"));
        Assert.Equal([0.34m, 0.33m, 0.33m], CalculoAmortizacion.Repartir(1m, [1m, 1m, 1m]));
        Assert.Equal(10m, CalculoAmortizacion.Repartir(10m, [3m, 7m, 0m]).Sum());
    }
}
