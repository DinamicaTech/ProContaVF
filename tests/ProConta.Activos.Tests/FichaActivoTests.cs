using ProConta.Activos.Gestion;
using ProConta.Contabilidad.Amortizacion;

namespace ProConta.Activos.Tests;

/// <summary>Tabla de amortización, regla de bloqueo del tipo y del ajeno y validación de la ficha, sin BD (activos 2026-11-17 18:50).</summary>
public sealed class FichaActivoTests
{
    // ---------- tabla de amortización ----------

    [Fact]
    public void Tabla_con_ejercicios_contabilizados_y_proyeccion_desde_el_de_trabajo()
    {
        // 100.000 al 2 % desde el 1/7/2023: 1.000,00 el primer año (184 de 365 días = 1.008,22).
        var plan = new PlanAmortizacion(new DateTime(2023, 7, 1), 100000m, 0m, 0.02m);
        var reales = new Dictionary<int, AmortizacionContabilizada>
        {
            [2023] = new(2023, 1008.22m, 1008.22m),
            [2024] = new(2024, 2000m, 3008.22m),
            [2025] = new(2025, 2000m, 5008.22m),
        };
        var filas = TablaAmortizacionActivo.Construir(plan, 2026, reales);
        Assert.Equal(EstadoAmortizacionEjercicio.Contabilizada, filas[0].Estado);
        Assert.Equal(184, filas[0].Dias);
        Assert.Equal(1008.22m, filas[0].Cuota);
        var hoy = filas.Single(f => f.Ejercicio == 2026);
        Assert.Equal(EstadoAmortizacionEjercicio.PendienteDeContabilizar, hoy.Estado);
        Assert.Equal(2000m, hoy.Cuota);
        Assert.Equal(7008.22m, hoy.Acumulado);
        Assert.Equal(92991.78m, hoy.Pendiente);
        Assert.Equal(EstadoAmortizacionEjercicio.Prevista, filas.Single(f => f.Ejercicio == 2027).Estado);
        // Termina cuando no queda nada: el último año se recorta a lo pendiente y la suma es la base.
        var ultima = filas[^1];
        Assert.Equal(0m, ultima.Pendiente);
        Assert.Equal(100000m, ultima.Acumulado);
        Assert.Equal(100000m, filas.Sum(f => f.Cuota));
        Assert.Equal(ultima.Ejercicio, TablaAmortizacionActivo.TerminaEn(filas));
    }

    [Fact]
    public void Anios_anteriores_a_los_datos_del_programa_se_calculan_y_el_de_trabajo_contabilizado_se_marca()
    {
        var plan = new PlanAmortizacion(new DateTime(2020, 1, 1), 10000m, 1000m, 0.1m);
        var reales = new Dictionary<int, AmortizacionContabilizada> { [2022] = new(2022, 1000m, 3000m), [2023] = new(2023, 1000m, 4000m) };
        var filas = TablaAmortizacionActivo.Construir(plan, 2023, reales);
        Assert.Equal([EstadoAmortizacionEjercicio.Calculada, EstadoAmortizacionEjercicio.Calculada, EstadoAmortizacionEjercicio.Contabilizada],
            filas.Take(3).Select(f => f.Estado));
        Assert.Equal(EstadoAmortizacionEjercicio.Contabilizada, filas.Single(f => f.Ejercicio == 2023).Estado);
        // El valor residual no se amortiza: lo amortizable es 9.000.
        Assert.Equal(9000m, filas[^1].Acumulado);
    }

    [Fact]
    public void Plan_con_baja_se_corta_en_la_baja_y_sin_base_no_hay_tabla()
    {
        var plan = new PlanAmortizacion(new DateTime(2024, 1, 1), 3650m, 0m, 0.1m, FechaBaja: new DateTime(2026, 6, 30));
        var filas = TablaAmortizacionActivo.Construir(plan, 2026, new Dictionary<int, AmortizacionContabilizada>());
        Assert.Equal([2024, 2025, 2026], filas.Select(f => f.Ejercicio));
        Assert.Equal(181, filas[^1].Dias);
        Assert.Empty(TablaAmortizacionActivo.Construir(plan with { Base = 0m }, 2026, new Dictionary<int, AmortizacionContabilizada>()));
    }

    // ---------- regla de bloqueo del tipo y del ajeno ----------

    private static SubcuentaDelActivo Sub(int id, string codigo, int recientes, int totales, bool inicial = false) =>
        new(id, 100 + id, codigo, "Sub " + codigo, recientes, totales, inicial);

    private static readonly DatosActivo Vivienda = new()
    {
        IdActivo = 7, Codigo = "Piso", Nombre = "Piso de prueba", IdTipoActivo = 1, CodigoTipo = TiposActivoPgc.Vivienda, FechaCompra = new DateTime(2026, 1, 1),
        Coste = 100000m, IdCentroCoste = 30,
    };

    [Fact]
    public void El_tipo_se_bloquea_con_apuntes_en_los_ultimos_cinco_ejercicios()
    {
        Assert.Null(ReglasFichaActivo.MotivoBloqueoTipo(2026, [Sub(1, "6250001", 0, 0), Sub(2, "2210001", 0, 12)]));
        var motivo = ReglasFichaActivo.MotivoBloqueoTipo(2026, [Sub(1, "6250001", 3, 3), Sub(2, "2210001", 0, 12)]);
        Assert.NotNull(motivo);
        Assert.Contains("entre 2022 y 2026", motivo);
        Assert.Contains("6250001 (3 apuntes)", motivo);
        Assert.Contains("da de alta un activo nuevo", motivo);
    }

    [Fact]
    public void Al_cambiar_el_tipo_borra_las_que_no_tienen_apuntes_y_desvincula_las_antiguas()
    {
        var subs = new[] { Sub(1, "6250001", 0, 0), Sub(2, "2210001", 0, 12), Sub(3, "2820001", 0, 0, inicial: true) };
        var c = ReglasFichaActivo.PlanificarCambio(Vivienda, Vivienda with { IdTipoActivo = 2, CodigoTipo = TiposActivoPgc.Local }, subs)!;
        Assert.Equal([1], c.Borrar.Select(s => s.IdSubcuentaActivo));
        Assert.Equal([2, 3], c.Desvincular.Select(s => s.IdSubcuentaActivo));
        Assert.True(c.GenerarNuevas);
        Assert.False(c.QuitarCentroYPlan);
        Assert.Null(ReglasFichaActivo.PlanificarCambio(Vivienda, Vivienda with { Nombre = "Otro" }, subs));
    }

    [Fact]
    public void Al_marcarlo_como_ajeno_quita_todas_sus_subcuentas_su_centro_y_su_plan()
    {
        var c = ReglasFichaActivo.PlanificarCambio(Vivienda, Vivienda with { EsAjeno = true }, [Sub(1, "6250001", 0, 0)])!;
        Assert.Single(c.Borrar);
        Assert.False(c.GenerarNuevas);
        Assert.True(c.QuitarCentroYPlan);
        // De ajeno a propio: solo se generan las de su plantilla.
        var vuelta = ReglasFichaActivo.PlanificarCambio(Vivienda with { EsAjeno = true }, Vivienda, [])!;
        Assert.True(vuelta.GenerarNuevas);
        Assert.Empty(vuelta.Borrar);
    }

    // ---------- validación y cálculos del alta ----------

    [Fact]
    public void Valida_los_datos_con_mensajes_que_dicen_como_arreglarlo()
    {
        Assert.Empty(ReglasFichaActivo.Validar(Vivienda));
        var e = ReglasFichaActivo.Validar(Vivienda with { Codigo = "", Nombre = " ", IdCentroCoste = null, FechaCompra = null, Coste = -1m });
        Assert.Equal([nameof(DatosActivo.Codigo), nameof(DatosActivo.Nombre), nameof(DatosActivo.Coste), nameof(DatosActivo.FechaCompra), nameof(DatosActivo.IdCentroCoste)],
            e.Select(x => x.Campo));
        Assert.Contains("Elegir", e.Single(x => x.Campo == nameof(DatosActivo.IdCentroCoste)).Texto);
        Assert.Contains("Ya hay otro activo", ReglasFichaActivo.Validar(Vivienda, codigoRepetido: true).Single().Texto);
        // Un ajeno no necesita centro ni fecha de compra.
        Assert.Empty(ReglasFichaActivo.Validar(Vivienda with { EsAjeno = true, IdCentroCoste = null, FechaCompra = null }));
        var amortiza = Vivienda with { SeAmortiza = true, InicioAmortizacion = null, PorcentajeAnual = 0m, BaseAmortizable = 100m, ValorResidual = 200m };
        Assert.Equal([nameof(DatosActivo.InicioAmortizacion), nameof(DatosActivo.PorcentajeAnual), nameof(DatosActivo.ValorResidual)],
            ReglasFichaActivo.Validar(amortiza).Select(x => x.Campo));
    }

    [Fact]
    public void Porcentaje_de_construccion_y_base_propuesta()
    {
        Assert.Equal(0.4851m, ReglasFichaActivo.PorcentajeConstruccion(723984.80m, 682126.28m));
        Assert.Null(ReglasFichaActivo.PorcentajeConstruccion(null, null));
        Assert.Equal(48510m, ReglasFichaActivo.BasePropuesta(100000m, true, 0.4851m));
        Assert.Equal(100000m, ReglasFichaActivo.BasePropuesta(100000m, true, null));
        Assert.Equal(20000m, ReglasFichaActivo.BasePropuesta(20000m, false, 0.5m));
        Assert.Equal(1000m, Vivienda with { BaseAmortizable = 33333.33m, PorcentajeAnual = 0.03m } is var d ? d.CuotaAnual : 0m);
        Assert.Equal(600, ReglasFichaActivo.VidaUtilMeses(0.02m));
        Assert.Equal("Calle-Mayor-1", ReglasFichaActivo.CodigoCentro("Calle/Mayor/1"));
        Assert.Equal("Inmuebles", ReglasFichaActivo.Clase(TiposActivoPgc.Parking));
        Assert.Equal("Vehículos", ReglasFichaActivo.Clase(TiposActivoPgc.Vehiculo));
        Assert.Equal("Otros", ReglasFichaActivo.Clase(TiposActivoPgc.Aplicacion));
    }
}
