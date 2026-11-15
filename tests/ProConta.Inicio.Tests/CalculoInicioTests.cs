using ProConta.Inicio;

namespace ProConta.Inicio.Tests;

/// <summary>Definiciones de cálculo de la pantalla de inicio, sin BD.</summary>
public sealed class CalculoInicioTests
{
    private static MovimientoDia M(int anio, int mes, int dia, string cuenta, decimal debe, decimal haber, byte tipo = 0) =>
        new(new DateTime(anio, mes, dia), cuenta, tipo, debe, haber);

    [Fact]
    public void FechaDeCorteEsHoyEnElEjercicioEnCursoYEl31DeDiciembreEnLosDemas()
    {
        var hoy = new DateTime(2026, 11, 15, 10, 30, 0);
        Assert.Equal(new DateTime(2026, 11, 15), CalculoInicio.FechaCorte(2026, hoy));
        Assert.Equal(new DateTime(2025, 12, 31), CalculoInicio.FechaCorte(2025, hoy));
        Assert.Equal(new DateTime(2027, 12, 31), CalculoInicio.FechaCorte(2027, hoy));
        Assert.True(CalculoInicio.Terminado(CalculoInicio.FechaCorte(2025, hoy)));
        Assert.False(CalculoInicio.Terminado(CalculoInicio.FechaCorte(2026, hoy)));
    }

    [Fact]
    public void LaMismaFechaDelComparadoRespetaLosAniosNoBisiestos()
    {
        Assert.Equal(new DateTime(2025, 11, 15), CalculoInicio.MismaFecha(new DateTime(2026, 11, 15), 2025));
        Assert.Equal(new DateTime(2023, 2, 28), CalculoInicio.MismaFecha(new DateTime(2024, 2, 29), 2023));
    }

    [Fact]
    public void ProyeccionDeCierreConSusCasosLimite()
    {
        // 1.200 del comparado completo × (550 / 500) = 1.320.
        Assert.Equal(1320m, CalculoInicio.Proyeccion(1200m, 550m, 500m, terminado: false));
        Assert.Null(CalculoInicio.Proyeccion(1200m, 550m, 0m, terminado: false));
        Assert.Null(CalculoInicio.Proyeccion(null, 550m, null, terminado: false));
        Assert.Null(CalculoInicio.Proyeccion(1200m, 550m, 500m, terminado: true));
        // Sin facturación propia, la proyección es cero (no se inventa).
        Assert.Equal(0m, CalculoInicio.Proyeccion(1200m, 0m, 500m, terminado: false));
    }

    [Fact]
    public void LaProyeccionPorMesSumadaAlAcumuladoDaLaProyeccionDeCierre()
    {
        var comparado = new decimal[] { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100 };
        var corte = new DateTime(2026, 11, 15);
        // Comparado hasta el 15/11: 10 meses + 50 de noviembre = 1.050; actual 1.260 → razón 1,2.
        var razon = 1260m / 1050m;
        var porMes = CalculoInicio.ProyeccionPorMes(comparado, 50m, razon, corte);
        Assert.All(porMes.Take(10), v => Assert.Null(v));
        Assert.Equal(60m, porMes[10]);
        Assert.Equal(120m, porMes[11]);
        Assert.Equal(CalculoInicio.Proyeccion(1200m, 1260m, 1050m, false), 1260m + porMes.Sum(v => v ?? 0));
    }

    [Fact]
    public void SerieMensualConHuecosDespuesDelCorte()
    {
        var corte = new DateTime(2026, 3, 10);
        var r = CalculoInicio.PorMes([(new DateTime(2026, 1, 5), 10m), (new DateTime(2026, 3, 10), 5m), (new DateTime(2026, 3, 11), 99m), (new DateTime(2025, 3, 1), 7m)], corte);
        Assert.Equal([10m, 0m, 5m], r.Take(3).Select(v => v!.Value));
        Assert.All(r.Skip(3), v => Assert.Null(v));
        Assert.Equal([10m, 10m, 15m, null], CalculoInicio.Acumulado(r).Take(4));
    }

    [Fact]
    public void ResultadoSinLa630NiRegularizacionNiCierreYConLosAsientosCuadrados()
    {
        var movs = new[]
        {
            M(2025, 3, 1, "4300001", 1210, 0), M(2025, 3, 1, "7000001", 0, 1000), M(2025, 3, 1, "4770021", 0, 210),
            M(2025, 4, 1, "6290001", 100, 0), M(2025, 4, 1, "5720001", 0, 100),
            M(2025, 12, 30, "6300001", 50, 0), M(2025, 12, 30, "4752001", 0, 50),
            M(2025, 12, 31, "7000001", 1000, 0, 2), M(2025, 12, 31, "6290001", 0, 100, 2), M(2025, 12, 31, "6300001", 0, 50, 2), M(2025, 12, 31, "1290000", 0, 850, 2),
        };
        // Invariante contable: cada asiento sintético cuadra (por fecha, que aquí es un asiento por día).
        Assert.All(movs.GroupBy(m => (m.Fecha, m.TipoAsiento)), g => Assert.Equal(g.Sum(m => m.Debe), g.Sum(m => m.Haber)));
        Assert.Equal(900m, CalculoInicio.Resultado(movs, new DateTime(2025, 12, 31)));
        Assert.Equal(1000m, CalculoInicio.Resultado(movs, new DateTime(2025, 3, 31)));
    }

    [Fact]
    public void PendienteDeCobroYDePagoSoloSumanLosSaldosDeSuSigno()
    {
        var corte = new DateTime(2026, 6, 30);
        var movs = new[]
        {
            M(2026, 1, 1, "4300001", 500, 0, 1), M(2026, 2, 1, "4300002", 0, 80), M(2026, 3, 1, "4360001", 20, 0), M(2026, 3, 1, "4380001", 999, 0),
            M(2026, 1, 1, "4000001", 0, 300, 1), M(2026, 2, 1, "4100001", 0, 40), M(2026, 2, 1, "4000002", 15, 0), M(2026, 2, 1, "4110001", 0, 777),
            M(2026, 12, 31, "4300001", 0, 500, CalculoInicio.TipoCierre),
        };
        Assert.Equal(520m, CalculoInicio.PendienteCobro(movs, corte));
        Assert.Equal(340m, CalculoInicio.PendientePago(movs, corte));
        // El cierre nunca cuenta.
        Assert.Equal(520m, CalculoInicio.PendienteCobro(movs, new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void AntiguedadPorVencimientoOFechaConLosCobrosSaldandoLoMasAntiguo()
    {
        var corte = new DateTime(2026, 11, 15);
        var apuntes = new[]
        {
            // Cliente 1: cuatro cargos y un cobro de 150 que salda el más antiguo y parte del siguiente. Quedan 250: 100 que vence el 30/11 (no vencido), 100 del 1/10 (45 días) y 50 del 1/9 (75 días).
            new ApunteCartera("4300001", new DateTime(2026, 8, 1), null, 100, 0),
            new ApunteCartera("4300001", new DateTime(2026, 9, 1), null, 100, 0),
            new ApunteCartera("4300001", new DateTime(2026, 9, 30), new DateTime(2026, 9, 30), 0, 150),
            new ApunteCartera("4300001", new DateTime(2026, 10, 1), null, 100, 0),
            new ApunteCartera("4300001", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), 100, 0),
            // Cliente 2: saldo acreedor, no cuenta.
            new ApunteCartera("4300002", new DateTime(2026, 5, 1), null, 0, 40),
            // Cliente 3: 70 del 1/1 (más de 90 días).
            new ApunteCartera("4300003", new DateTime(2026, 1, 1), null, 70, 0),
        };
        var (tramos, clientes) = CalculoInicio.Antiguedad(apuntes, corte);
        Assert.Equal(100m, tramos[0]); // 30/11 aún no vence
        Assert.Equal(0m, tramos[1]);
        Assert.Equal(100m, tramos[2]); // 1/10: 45 días
        Assert.Equal(50m, tramos[3]); // 1/9: 75 días
        Assert.Equal(70m, tramos[4]); // 1/1 del cliente 3
        Assert.Equal(320m, tramos.Sum());
        Assert.Equal((250m, 150m), clientes["4300001"]);
        Assert.False(clientes.ContainsKey("4300002"));
        Assert.Equal((70m, 70m), clientes["4300003"]);
    }

    [Fact]
    public void GastosPorNaturalezaLasOchoMayoresYElRestoSinLa630()
    {
        var corte = new DateTime(2026, 6, 30);
        var cuentas = new[] { "600", "621", "622", "623", "624", "625", "626", "627", "628", "629" };
        var movs = cuentas.Select((c, i) => M(2026, 2, 1, c + "0001", 100 + i * 10, 0)).ToList();
        movs.Add(M(2026, 2, 1, "6300001", 5000, 0));
        movs.Add(M(2026, 12, 31, "6290001", 0, 190, CalculoInicio.TipoRegularizacion));
        var comparado = new[] { M(2025, 2, 1, "6290001", 150, 0), M(2025, 7, 1, "6290001", 999, 0) };
        var r = CalculoInicio.GastosPorNaturaleza(movs, corte, comparado, new DateTime(2025, 6, 30), new Dictionary<string, string> { ["629"] = "Otros servicios" });
        Assert.Equal(9, r.Count);
        Assert.Equal("629", r[0].Codigo);
        Assert.Equal("629 · Otros servicios", r[0].Texto);
        Assert.Equal(190m, r[0].Actual);
        Assert.Equal(150m, r[0].Comparado);
        Assert.Equal("Resto", r[^1].Nombre);
        Assert.Equal(100m + 110m, r[^1].Actual);
        Assert.DoesNotContain(r, g => g.Codigo == "630");
    }

    [Fact]
    public void IvaDeducibleSegunLaActividadDeCadaSerie()
    {
        var actividades = new[] { new ActividadInicio("Viviendas", 0, "4726"), new ActividadInicio("Inversión", 1, "4722"), new ActividadInicio("Locales", 2, "4727") };
        Assert.Equal(1m, CalculoInicio.FraccionDeducible("4720021", actividades, 40m));
        Assert.Equal(0m, CalculoInicio.FraccionDeducible("4726021", actividades, 40m));
        Assert.Equal(0.4m, CalculoInicio.FraccionDeducible("4722021", actividades, 40m));
        Assert.Equal(1m, CalculoInicio.FraccionDeducible("4722021", actividades, null));
        var apuntes = new[]
        {
            new ApunteIva(new DateTime(2026, 10, 5), "4770021", 0, 210), new ApunteIva(new DateTime(2026, 10, 6), "4770021", 21, 0),
            new ApunteIva(new DateTime(2026, 10, 7), "4720021", 42, 0), new ApunteIva(new DateTime(2026, 10, 8), "4726021", 30, 0),
            new ApunteIva(new DateTime(2026, 10, 9), "4722021", 10, 0), new ApunteIva(new DateTime(2026, 9, 30), "4770021", 0, 500),
        };
        var (rep, sop) = CalculoInicio.Iva(apuntes, new DateTime(2026, 10, 1), new DateTime(2026, 11, 15), actividades, 50m);
        Assert.Equal(189m, rep);
        Assert.Equal(47m, sop);
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 12, 31)), CalculoInicio.Trimestre(2026, 4));
    }

    [Fact]
    public void FacturacionPorActividadAgrupaOtrasYSinActividad()
    {
        var porCentro = new Dictionary<int, ActividadInicio>();
        for (var i = 1; i <= 6; i++) porCentro[i] = new ActividadInicio($"Actividad {i}", 2, null);
        var lineas = Enumerable.Range(1, 6).Select(i => new LineaVenta(new DateTime(2026, 3, 1), i * 100m, i)).Append(new LineaVenta(new DateTime(2026, 3, 1), 50m, null)).ToList();
        var r = CalculoInicio.PorActividad(lineas, new DateTime(2026, 6, 30), null, null, porCentro);
        Assert.Equal(5, r.Count);
        Assert.Equal("Actividad 6", r[0].Actividad);
        Assert.Equal(CalculoInicio.OtrasActividades, r[^1].Actividad);
        Assert.Equal(100m + 200m + 50m, r[^1].Actual);
        Assert.Equal(lineas.Sum(l => l.Base), r.Sum(x => x.Actual));
        var pocas = CalculoInicio.PorActividad([new LineaVenta(new DateTime(2026, 1, 1), 10, null)], new DateTime(2026, 6, 30), [], new DateTime(2025, 6, 30), porCentro);
        Assert.Equal(CalculoInicio.SinActividad, pocas.Single().Actividad);
        Assert.Equal(0m, pocas.Single().Comparado);
    }
}
