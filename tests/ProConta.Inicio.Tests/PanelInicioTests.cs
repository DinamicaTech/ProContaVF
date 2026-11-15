using ProConta.Inicio;
using ProConta.Inicio.UI;
using ProConta.UI.Base.Preferencias;

namespace ProConta.Inicio.Tests;

/// <summary>Datos sintéticos de dos ejercicios cuadrados (2025 completo y 2026 hasta el 15/11) para el panel y el modelo de vista.</summary>
internal sealed class FuenteSintetica : IFuenteInicio
{
    public static readonly EjercicioSociedad E2025 = new(1, 2025, 4, null);
    public static readonly EjercicioSociedad E2026 = new(2, 2026, 4, null);
    public int Lecturas { get; private set; }

    private static MovimientoDia M(int anio, int mes, int dia, string cuenta, decimal debe, decimal haber, byte tipo = 0) => new(new DateTime(anio, mes, dia), cuenta, tipo, debe, haber);

    /// <summary>Cada mes: una venta de 1.000 (+210 IVA) cobrada a fin de mes y un gasto de 400 pagado; 2026 vende un 10 % más.</summary>
    public static DatosEjercicio Datos(EjercicioSociedad e, int hastaMes, int hastaDia, decimal factor)
    {
        var movs = new List<MovimientoDia> { M(e.Ejercicio, 1, 1, "5720001", 5000, 0, 1), M(e.Ejercicio, 1, 1, "1000000", 0, 5000, 1) };
        var ventas = new List<LineaVenta>();
        var cartera = new List<ApunteCartera>();
        var iva = new List<ApunteIva>();
        for (var m = 1; m <= hastaMes; m++)
        {
            var dia = m == hastaMes ? Math.Min(hastaDia, 10) : 10;
            var venta = 1000m * factor;
            var f = new DateTime(e.Ejercicio, m, dia);
            movs.Add(new MovimientoDia(f, "4300001", 0, venta * 1.21m, 0));
            movs.Add(new MovimientoDia(f, "7000001", 0, 0, venta));
            movs.Add(new MovimientoDia(f, "4770021", 0, 0, venta * 0.21m));
            cartera.Add(new ApunteCartera("4300001", f, null, venta * 1.21m, 0));
            iva.Add(new ApunteIva(f, "4770021", 0, venta * 0.21m));
            ventas.Add(new LineaVenta(f, venta, m % 2 == 0 ? 10 : 20));
            movs.Add(new MovimientoDia(f, "6290001", 0, 400, 0));
            movs.Add(new MovimientoDia(f, "4720021", 0, 84, 0));
            movs.Add(new MovimientoDia(f, "4000001", 0, 0, 484));
            iva.Add(new ApunteIva(f, "4720021", 84, 0));
            if (m < hastaMes)
            {
                var fin = new DateTime(e.Ejercicio, m, 28);
                movs.Add(new MovimientoDia(fin, "4300001", 0, 0, venta * 1.21m));
                movs.Add(new MovimientoDia(fin, "4000001", 0, 484, 0));
                movs.Add(new MovimientoDia(fin, "5720001", 0, venta * 1.21m, 484));
                cartera.Add(new ApunteCartera("4300001", fin, null, 0, venta * 1.21m));
            }
        }
        return new DatosEjercicio(e, movs, ventas, false, cartera, iva, new Dictionary<string, string> { ["4300001"] = "Cliente sintético", ["629"] = "Otros servicios" });
    }

    public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<EjercicioSociedad>>([E2026, E2025]);

    public Task<DatosEjercicio> DatosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default)
    {
        Lecturas++;
        return Task.FromResult(ejercicio.Ejercicio == 2025 ? Datos(E2025, 12, 31, 1m) : Datos(E2026, 11, 15, 1.1m));
    }

    public Task<IReadOnlyList<BancoN43>> BancosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<BancoN43>>([new BancoN43("Banco sintético", "5720001", new DateTime(2026, 11, 1))]);

    public Task<IReadOnlyDictionary<int, ActividadInicio>> ActividadesPorCentroAsync(string idSociedad, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyDictionary<int, ActividadInicio>>(new Dictionary<int, ActividadInicio> { [10] = new("Locales", 2, null), [20] = new("Viviendas", 0, "4726") });

    public Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<ActividadInicio>>([new("Locales", 2, null), new("Viviendas", 0, "4726")]);
}

public sealed class PanelInicioTests
{
    private static readonly DateTime Hoy = new(2026, 11, 15);

    private static PanelInicio Calcular(bool comparar = true, bool conAnterior = true)
    {
        var f = new FuenteSintetica();
        var a = FuenteSintetica.Datos(FuenteSintetica.E2026, 11, 15, 1.1m);
        var c = FuenteSintetica.Datos(FuenteSintetica.E2025, 12, 31, 1m);
        return CalculadoraInicio.Calcular(a, conAnterior ? c : null, comparar ? c : null, Hoy, f.BancosAsync("T01", FuenteSintetica.E2026).Result,
            f.ActividadesPorCentroAsync("T01").Result, f.ActividadesAsync("T01").Result);
    }

    [Fact]
    public void LosDatosSinteticosCuadran()
    {
        foreach (var d in new[] { FuenteSintetica.Datos(FuenteSintetica.E2026, 11, 15, 1.1m), FuenteSintetica.Datos(FuenteSintetica.E2025, 12, 31, 1m) })
            Assert.All(d.Movimientos.GroupBy(m => m.Fecha), g => Assert.Equal(g.Sum(m => m.Debe), g.Sum(m => m.Haber)));
    }

    [Fact]
    public void FacturacionConComparativoAMismaFechaYProyeccion()
    {
        var p = Calcular();
        Assert.Equal(new DateTime(2026, 11, 15), p.FechaCorte);
        Assert.Equal(new DateTime(2025, 11, 15), p.FechaComparada);
        Assert.Equal(12100m, p.Facturacion.Valor);
        Assert.Equal(11000m, p.Facturacion.Comparado);
        Assert.Equal(12000m, p.Facturacion.TotalComparado);
        Assert.Equal(13200m, p.Facturacion.Proyeccion);
        Assert.Equal(10m, Math.Round(p.Facturacion.Variacion!.Value, 6));
        // Real + proyección mensual = proyección de cierre; diciembre se proyecta con la misma razón.
        Assert.Equal(p.Facturacion.Proyeccion, p.FacturacionMensual.Sum(v => v ?? 0) + p.ProyeccionMensual.Sum(v => v ?? 0));
        Assert.Equal(1100m, p.ProyeccionMensual[11]);
        Assert.Null(p.FacturacionMensual[11]);
        Assert.Equal(2, p.Actividades.Count);
        Assert.Equal(12100m, p.Actividades.Sum(a => a.Actual));
    }

    [Fact]
    public void SinCompararNoHayComparativoNiProyeccion()
    {
        var p = Calcular(comparar: false);
        Assert.Null(p.Comparado);
        Assert.Null(p.Facturacion.Proyeccion);
        Assert.Null(p.Facturacion.Comparado);
        Assert.All(p.ProyeccionMensual, v => Assert.Null(v));
        Assert.All(p.Gastos, g => Assert.Null(g.Comparado));
    }

    [Fact]
    public void ResultadoTesoreriaYPendientes()
    {
        var p = Calcular();
        // 11 meses: 11 × (1.100 − 400).
        Assert.Equal(7700m, p.Resultado.Valor);
        Assert.Equal(6600m, p.Resultado.Comparado);
        Assert.Equal(7200m, p.Resultado.TotalComparado);
        Assert.Equal(8400m, Math.Round(p.Resultado.Proyeccion!.Value, 2));
        // Tesorería: 5.000 de apertura + 10 meses cobrados y pagados.
        Assert.Equal(5000m + 10 * (1331m - 484m), p.Tesoreria.Valor);
        Assert.Equal(p.Tesoreria.Valor, p.Bancos.Single().Saldo);
        Assert.Equal(0m, p.OtrasCuentasTesoreria);
        Assert.Equal(1331m, p.PendienteCobro.Valor);
        Assert.Equal(484m, p.PendientePago.Valor);
        Assert.Equal(1331m, p.Antiguedad.Sum(t => t.Importe));
        Assert.Equal(1331m, p.Antiguedad[0].Importe + p.Antiguedad[1].Importe);
        Assert.Equal("Cliente sintético", p.Clientes.Single().Nombre);
    }

    [Fact]
    public void TesoreriaDeLosUltimos12MesesConElAnteriorOConHuecos()
    {
        var p = Calcular();
        Assert.Equal(12, p.TesoreriaMensual.Count);
        Assert.Equal(new DateTime(2025, 12, 31), p.TesoreriaMensual[0].Fecha);
        Assert.Equal("15 nov", p.TesoreriaMensual[^1].Etiqueta);
        Assert.True(p.TesoreriaMensual[^1].EsCorte);
        Assert.Equal(p.Tesoreria.Valor, p.TesoreriaMensual[^1].Saldo);
        Assert.NotNull(p.TesoreriaMensual[0].Saldo);
        var sinAnterior = Calcular(conAnterior: false);
        Assert.Null(sinAnterior.TesoreriaMensual[0].Saldo);
        Assert.NotNull(sinAnterior.TesoreriaMensual[1].Saldo);
    }

    [Fact]
    public void IvaEstimadoDelTrimestreActivo()
    {
        var p = Calcular();
        Assert.Equal(4, p.Iva.Trimestre);
        Assert.Equal(new DateTime(2026, 10, 1), p.Iva.Desde);
        Assert.Equal(new DateTime(2026, 11, 15), p.Iva.Hasta);
        Assert.Equal(2 * 231m, p.Iva.Repercutido);
        Assert.Equal(2 * 84m, p.Iva.Soportado);
        // 4T de 2025 hasta el 15/11: octubre y noviembre.
        Assert.Equal(2 * (210m - 84m), p.Iva.Comparado);
        Assert.Equal(3 * (231m - 84m), p.EvolucionIva[0]);
        Assert.Equal(p.Iva.Neto, p.EvolucionIva[3]);
    }

    [Fact]
    public void EjercicioTerminadoNoProyecta()
    {
        var f = new FuenteSintetica();
        var a = FuenteSintetica.Datos(FuenteSintetica.E2025, 12, 31, 1m);
        var p = CalculadoraInicio.Calcular(a, null, a, Hoy, [], new Dictionary<int, ActividadInicio>(), []);
        Assert.True(p.Terminado);
        Assert.Equal(new DateTime(2025, 12, 31), p.FechaCorte);
        Assert.Null(p.Facturacion.Proyeccion);
        Assert.Null(p.Resultado.Proyeccion);
    }

    [Fact]
    public void EjercicioSinMovimientos()
    {
        var vacio = DatosEjercicio.Vacio(new EjercicioSociedad(3, 2027));
        var p = CalculadoraInicio.Calcular(vacio, null, null, new DateTime(2027, 1, 2), [], new Dictionary<int, ActividadInicio>(), []);
        Assert.False(p.HayMovimientos);
        Assert.Equal(0m, p.Facturacion.Valor);
        Assert.Empty(p.Gastos);
        Assert.Empty(p.Actividades);
        Assert.All(p.TesoreriaMensual, t => Assert.Null(t.Saldo));
    }
}

/// <summary>Modelo de vista de la pantalla de inicio con datos sintéticos (sin ventanas).</summary>
public sealed class InicioVMTests
{
    private sealed class DialogosFijos(IReadOnlySet<string>? respuesta) : IDialogosInicio
    {
        public IReadOnlySet<string>? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos) => respuesta;
    }

    private static (InicioVM Vm, FuenteSintetica Fuente, PreferenciasEnMemoria Prefs, List<string> Abiertas) Crear(AccionesInicio? acciones = null, IReadOnlySet<string>? personalizar = null)
    {
        var fuente = new FuenteSintetica();
        var prefs = new PreferenciasEnMemoria();
        var abiertas = new List<string>();
        acciones ??= new AccionesInicio(AbrirFacturas: () => abiertas.Add("facturas"), AbrirMayor: c => abiertas.Add("mayor " + string.Join(",", c)));
        var vm = new InicioVM(new ServicioInicio(fuente, () => new DateTime(2026, 11, 15)), new ContextoInicio("T01", 2026, "Sociedad sintética", _ => { }),
            new DialogosFijos(personalizar), acciones, prefs);
        return (vm, fuente, prefs, abiertas);
    }

    [Fact]
    public async Task CargaSeisIndicadoresComparandoPorDefectoConElAnterior()
    {
        var (vm, _, _, abiertas) = Crear();
        await vm.IniciarAsync();
        Assert.Equal(ProConta.UI.Base.Controles.EstadoVista.Contenido, vm.Estado);
        Assert.Equal(2025, vm.Comparar!.Ejercicio);
        Assert.Equal(["2025", "Sin comparar"], vm.Comparables.Select(c => c.Texto));
        Assert.Equal(6, vm.Indicadores.Count);
        Assert.Equal(ProConta.UI.Base.Formato.Formatos.Importe(12100m), vm.Indicadores[0].Valor);
        Assert.Equal("▲ 10,0 %", vm.Indicadores[0].Variacion);
        Assert.Equal(Tono.Bueno, vm.Indicadores[0].Tono);
        Assert.Equal("Proyección de cierre: " + ProConta.UI.Base.Formato.Formatos.Importe(13200m), vm.Indicadores[0].Proyeccion);
        Assert.Contains("hasta hoy", vm.Subtitulo);
        // Solo son pulsables los indicadores con pantalla conectada: facturas y Mayor, no resultado ni bancos.
        Assert.Equal([true, false, false, true, true, true], vm.Indicadores.Select(i => i.Pulsable));
        vm.Indicadores[3].Comando!.Execute(null);
        Assert.Equal("mayor 430,431,432,433,434,435,436", abiertas.Single());
    }

    [Fact]
    public async Task CambiarElComparadoNoVuelveALeerYActualizarSi()
    {
        var (vm, fuente, _, _) = Crear();
        await vm.IniciarAsync();
        var lecturas = fuente.Lecturas;
        vm.Comparar = vm.Comparables.Single(c => c.Ejercicio is null);
        await Task.Delay(50);
        while (vm.Cargando) await Task.Delay(20);
        Assert.Equal(lecturas, fuente.Lecturas);
        Assert.Null(vm.Indicadores[0].Variacion);
        Assert.Null(vm.TextoProyeccion);
        await vm.ActualizarAsync();
        Assert.True(fuente.Lecturas > lecturas);
    }

    [Fact]
    public async Task VistasDeFacturacion()
    {
        var (vm, _, _, _) = Crear();
        await vm.IniciarAsync();
        Assert.Equal(2, vm.DatosFacturacion!.Series.Count);
        Assert.NotNull(vm.DatosFacturacion.Series[0].Proyeccion);
        Assert.Contains(vm.LeyendaFacturacion, l => l.Rayado);
        vm.VistaAcumulada = true;
        Assert.Contains(vm.DatosFacturacion!.Series, s => s.Discontinua);
        Assert.Contains(vm.LeyendaFacturacion, l => l.Discontinuo);
        vm.VistaTabla = true;
        Assert.False(vm.VerGraficoFacturacion);
        Assert.Equal(12, vm.FilasFacturacion.Count);
        Assert.Equal(13200m, vm.FilasFacturacion[^1].ImporteAcumulado);
        Assert.Equal(12000m, vm.FilasFacturacion[^1].ImporteAcumuladoComparado);
        var tooltip = vm.DatosFacturacion!.TooltipDe(10, v => v.ToString());
        Assert.StartsWith("noviembre", tooltip.Titulo);
    }

    [Fact]
    public void LineaAcumuladaProyectadaArrancaEnElRealYTerminaEnLaProyeccion()
    {
        var proy = new decimal?[12];
        proy[10] = 50m;
        proy[11] = 100m;
        var r = InicioVM.AcumuladoProyectado(1000m, proy, 10);
        Assert.Equal(1000m, r[10]);
        Assert.Equal(1150m, r[11]);
        Assert.Null(r[9]);
        var diciembre = InicioVM.AcumuladoProyectado(1000m, proy, 11);
        Assert.Equal(1100m, diciembre[11]);
    }

    [Fact]
    public async Task PersonalizarGuardaLosBloquesOcultosPorOperador()
    {
        var (vm, _, prefs, _) = Crear(personalizar: new HashSet<string> { "gastos", "cobros" });
        await vm.IniciarAsync();
        Assert.True(vm.VerGastos);
        vm.PersonalizarCommand.Execute(null);
        Assert.False(vm.VerGastos);
        Assert.False(vm.VerCobros);
        Assert.True(vm.VerFacturacion);
        Assert.Equal(["cobros", "gastos"], prefs.Leer<List<string>>(InicioVM.ClaveOcultos));
        // Otro modelo de vista con las mismas preferencias arranca con esos bloques ocultos.
        var otro = new InicioVM(new ServicioInicio(new FuenteSintetica()), new ContextoInicio("T01", 2026, "S", _ => { }), new DialogosFijos(null), null, prefs);
        Assert.False(otro.VerGastos);
    }

    private sealed class FuenteQueFalla : IFuenteInicio
    {
        public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) => throw new InvalidOperationException("BD no disponible");
        public Task<DatosEjercicio> DatosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BancoN43>> BancosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, ActividadInicio>> ActividadesPorCentroAsync(string idSociedad, CancellationToken cancelacion = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task UnErrorDeLecturaExplicaQueHacerYOfreceReintentar()
    {
        var vm = new InicioVM(new ServicioInicio(new FuenteQueFalla()), new ContextoInicio("T01", 2026, "S", _ => { }), new DialogosFijos(null), null, new PreferenciasEnMemoria());
        await vm.IniciarAsync();
        Assert.Equal(ProConta.UI.Base.Controles.EstadoVista.Error, vm.Estado);
        Assert.Contains("BD no disponible", vm.MensajeEstado);
        Assert.Contains("Reintentar", vm.MensajeEstado);
        Assert.True(vm.ReintentarCommand.CanExecute(null));
    }
}
