using ProConta.Activos.Gestion;

namespace ProConta.Activos.Tests;

/// <summary>Rentabilidad del ejercicio y anual proyectada del listado de activos, sin BD (activos 2026-11-28).</summary>
public sealed class RentabilidadActivoTests
{
    private static readonly CriterioActivo Piso = new(new HashSet<string> { "7520001", "6250001" }, 30, false);
    private static ApunteResultado A(string cuenta, int mes, decimal debe, decimal haber) => new(cuenta, new DateTime(2026, mes, 10), debe, haber, null, 0, true);
    private static readonly DateTime Hoy = new(2026, 11, 28);

    [Fact]
    public void Rentabilidad_del_ejercicio_es_resultado_entre_coste()
    {
        Assert.Equal(0.04m, RentabilidadActivo.DelEjercicio(8000m, 200000m));
        Assert.Equal(-0.01m, RentabilidadActivo.DelEjercicio(-2000m, 200000m));
        Assert.Null(RentabilidadActivo.DelEjercicio(8000m, 0m));
    }

    [Fact]
    public void Proyeccion_es_la_media_mensual_por_doce()
    {
        // Ejemplo del propietario: último ingreso en octubre → × 12/10 (8.000 € en diez meses son 9.600 € al año).
        Assert.Equal(9600m, RentabilidadActivo.Proyectar(8000m, 10));
        Assert.Equal(0.048m, RentabilidadActivo.Proyectar(0.04m, 10));
        Assert.Equal(0.04m, RentabilidadActivo.Proyectar(0.04m, 12));
        Assert.Null(RentabilidadActivo.Proyectar(null, 10));
        Assert.Null(RentabilidadActivo.Proyectar(0.04m, null));
    }

    [Fact]
    public void Meses_del_ultimo_ingreso_y_ejercicio_terminado_entero()
    {
        Assert.Equal(10, RentabilidadActivo.MesesTranscurridos(2026, 10, Hoy));
        Assert.Null(RentabilidadActivo.MesesTranscurridos(2026, null, Hoy)); // sin ingresos en el año en curso
        Assert.Equal(12, RentabilidadActivo.MesesTranscurridos(2025, 7, Hoy)); // ejercicio terminado: la proyectada es la real
        Assert.Equal(12, RentabilidadActivo.MesesTranscurridos(2025, null, Hoy));
    }

    [Fact]
    public void Ultimo_mes_con_ingresos_sale_de_la_cuenta_de_resultados_e_ignora_meses_solo_con_abonos_o_gastos()
    {
        var c = CalculoCuentaResultados.Calcular(2026, [
            A("7520001", 1, 0m, 900m), A("7520001", 10, 0m, 900m),
            A("7520001", 11, 100m, 0m), // un abono en noviembre no es un ingreso
            A("6250001", 12, 50m, 0m), // un gasto en diciembre tampoco
        ], Piso, c => c);
        Assert.Equal(10, ServicioActivos.UltimoMesConIngresos(c));
        Assert.Null(ServicioActivos.UltimoMesConIngresos(CalculoCuentaResultados.Calcular(2026, [A("6250001", 3, 50m, 0m)], Piso, c => c)));
    }

    [Fact]
    public void Grupo_suma_resultados_entre_suma_de_costes()
    {
        var (real, proyectada) = RentabilidadActivo.DeGrupo([
            (8000m, 200000m, 10), // proyecta 9.600
            (2000m, 100000m, null), // sin ingresos: entra con su resultado real
        ]);
        Assert.Equal(10000m / 300000m, real);
        Assert.Equal(11600m / 300000m, proyectada);
        Assert.Equal((null, null), RentabilidadActivo.DeGrupo([(100m, 0m, 10)]));
    }

    [Fact]
    public void Proyeccion_por_dias_de_los_financieros()
    {
        // +9 % del 31/12/2025 al 15/10/2026 (288 días) → +11,4 % anual.
        var p = RentabilidadActivo.ProyectarPorDias(0.09m, new DateTime(2025, 12, 31), new DateTime(2026, 10, 15));
        Assert.Equal(0.114m, Math.Round(p!.Value, 3));
        Assert.Null(RentabilidadActivo.ProyectarPorDias(0.09m, Hoy, Hoy));
    }
}
