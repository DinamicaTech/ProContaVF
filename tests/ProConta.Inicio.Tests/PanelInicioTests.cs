using ProConta.Inicio;
using ProConta.Inicio.UI;
using ProConta.UI.Base.Preferencias;

namespace ProConta.Inicio.Tests;

/// <summary>Datos sintéticos de dos ejercicios cuadrados (2025 completo y 2026 hasta el 15/11) para el panel y el modelo de vista.</summary>
internal sealed class FuenteSintetica(bool conBroker = false) : IFuenteInicio
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
        var d = ejercicio.Ejercicio == 2025 ? Datos(E2025, 12, 31, 1m) : Datos(E2026, 11, 15, 1.1m);
        return Task.FromResult(conBroker ? ConBroker(d) : d);
    }

    /// <summary>Añade la cuenta del bróker (5720019) con 3.000 aportados el 1 de enero de cada ejercicio.</summary>
    public static DatosEjercicio ConBroker(DatosEjercicio d) => d with
    {
        Movimientos = [.. d.Movimientos, new MovimientoDia(new DateTime(d.Ejercicio.Ejercicio, 1, 1), "5720019", 1, 3000m, 0), new MovimientoDia(new DateTime(d.Ejercicio.Ejercicio, 1, 1), "1000000", 1, 0, 3000m)],
    };

    public static IReadOnlyList<BancoN43> Bancos(bool conBroker) => conBroker
        ? [new BancoN43("Banco sintético", "5720001", new DateTime(2026, 11, 1)), new BancoN43("Bróker sintético", "5720019", null)]
        : [new BancoN43("Banco sintético", "5720001", new DateTime(2026, 11, 1))];

    public Task<IReadOnlyList<BancoN43>> BancosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default) =>
        Task.FromResult(Bancos(conBroker));

    public Task<ProConta.Contabilidad.Balances.ModeloBalance?> ModeloSituacionAsync(CancellationToken cancelacion = default) =>
        Task.FromResult<ProConta.Contabilidad.Balances.ModeloBalance?>(ModeloSintetico.Situacion);

    public Task<IReadOnlyDictionary<int, IReadOnlyList<ProConta.Contabilidad.Ratios.MovimientoRatio>>> CierresAsync(string idSociedad, IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<ProConta.Contabilidad.Ratios.MovimientoRatio>>>(ejercicios.Where(e => e.Ejercicio == 2025)
            .ToDictionary(e => e.Ejercicio, e => CalculoInicio.ParaRatios(Datos(E2025, 12, 31, 1m).Movimientos)));

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
        // La proyección del indicador de resultado es la del EBITDA (aquí no hay amortizaciones: coincide con el resultado).
        Assert.Null(p.Resultado.Proyeccion);
        Assert.Equal(7700m, p.Ebitda.Actual);
        Assert.Equal(6600m, p.Ebitda.Comparado);
        Assert.Equal(7200m, p.Ebitda.TotalComparado);
        Assert.Equal(8400m, Math.Round(p.Ebitda.Proyeccion!.Value, 2));
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
        Assert.Null(p.Ebitda.Proyeccion);
    }

    [Fact]
    public void LaProyeccionDelEbitdaNoCuentaAmortizacionesNiFinancierosYRespetaLosCasosLimite()
    {
        var f = new FuenteSintetica();
        var a = FuenteSintetica.Datos(FuenteSintetica.E2026, 11, 15, 1.1m);
        var c = FuenteSintetica.Datos(FuenteSintetica.E2025, 12, 31, 1m);
        // Una amortización y unos intereses en el comparado completo (diciembre) y en el actual: no cambian el EBITDA.
        a = a with { Movimientos = [.. a.Movimientos, new MovimientoDia(new DateTime(2026, 6, 30), "6810001", 0, 500m, 0), new MovimientoDia(new DateTime(2026, 6, 30), "6620001", 0, 80m, 0)] };
        c = c with { Movimientos = [.. c.Movimientos, new MovimientoDia(new DateTime(2025, 12, 31), "6810001", 0, 1200m, 0), new MovimientoDia(new DateTime(2025, 12, 31), "6300001", 0, 300m, 0)] };
        var p = CalculadoraInicio.Calcular(a, c, c, Hoy, f.BancosAsync("T01", FuenteSintetica.E2026).Result, new Dictionary<int, ActividadInicio>(), []);
        Assert.Equal(7700m - 580m, p.Resultado.Valor);
        Assert.Equal(7700m, p.Ebitda.Actual);
        Assert.Equal(7200m, p.Ebitda.TotalComparado);
        Assert.Equal(7200m * (7700m / 6600m), p.Ebitda.Proyeccion);
        // Sin comparado no hay proyección.
        Assert.Null(CalculadoraInicio.Calcular(a, c, null, Hoy, [], new Dictionary<int, ActividadInicio>(), []).Ebitda.Proyeccion);
        // Comparado sin EBITDA a la misma fecha (divisor cero): no hay proyección.
        var vacio = DatosEjercicio.Vacio(FuenteSintetica.E2025);
        Assert.Null(CalculadoraInicio.Calcular(a, vacio, vacio, Hoy, [], new Dictionary<int, ActividadInicio>(), []).Ebitda.Proyeccion);
    }

    [Fact]
    public void LasCuentasDeInversionSalenDeLaTesoreriaOperativaYSeVenAparte()
    {
        var a = FuenteSintetica.ConBroker(FuenteSintetica.Datos(FuenteSintetica.E2026, 11, 15, 1.1m));
        var c = FuenteSintetica.ConBroker(FuenteSintetica.Datos(FuenteSintetica.E2025, 12, 31, 1m));
        var bancos = FuenteSintetica.Bancos(conBroker: true);
        var sinMarca = CalculadoraInicio.Calcular(a, c, c, Hoy, bancos, new Dictionary<int, ActividadInicio>(), []);
        var conMarca = CalculadoraInicio.Calcular(a, c, c, Hoy, bancos, new Dictionary<int, ActividadInicio>(), [], new HashSet<string> { "5720019" });
        // Sin marcar, el bróker suma; marcado, la tesorería operativa es el total anterior menos el bróker.
        Assert.Equal(5000m + 10 * (1331m - 484m) + 3000m, sinMarca.Tesoreria.Valor);
        Assert.Equal(sinMarca.Tesoreria.Valor - 3000m, conMarca.Tesoreria.Valor);
        Assert.Equal(sinMarca.Tesoreria.Comparado - 3000m, conMarca.Tesoreria.Comparado);
        Assert.Equal(sinMarca.TesoreriaMensual[^1].Saldo - 3000m, conMarca.TesoreriaMensual[^1].Saldo);
        Assert.Empty(sinMarca.Inversiones);
        Assert.Equal("5720019", conMarca.Inversiones.Single().Cuenta);
        Assert.Equal(3000m, conMarca.TotalInversiones);
        Assert.DoesNotContain(conMarca.Bancos, b => b.Cuenta == "5720019");
        Assert.Equal(conMarca.Tesoreria.Valor, conMarca.Bancos.Sum(b => b.Saldo) + conMarca.OtrasCuentasTesoreria);
    }

    [Fact]
    public void RatiosDelPanelABalanceDeFinDelMesAnteriorYSerieACierre()
    {
        var f = new FuenteSintetica();
        var a = FuenteSintetica.Datos(FuenteSintetica.E2026, 11, 15, 1.1m);
        var c = FuenteSintetica.Datos(FuenteSintetica.E2025, 12, 31, 1m);
        var cierres = f.CierresAsync("T01", [FuenteSintetica.E2025]).Result;
        var p = CalculadoraInicio.Calcular(a, c, c, Hoy, f.BancosAsync("T01", FuenteSintetica.E2026).Result, new Dictionary<int, ActividadInicio>(), [], null, ModeloSintetico.Situacion, cierres);
        Assert.NotNull(p.Ratios);
        Assert.Equal(new DateTime(2026, 10, 31), p.Ratios!.FechaBalance);
        Assert.Equal(new DateTime(2025, 11, 1), p.Ratios.DesdeResultado);
        Assert.Equal(12, p.Ratios.Ratios.Count);
        Assert.Equal([2025, 2026], p.SerieRatios.Select(s => s.Ejercicio));
        Assert.True(p.SerieRatios[^1].EsActual);
        // Margen neto de los 12 meses: (2 meses de 2025 + 10 de 2026) × (venta − 400) / ventas.
        var ventas = 2 * 1000m + 10 * 1100m;
        var resultado = 2 * 600m + 10 * 700m;
        Assert.Equal(resultado / ventas * 100m, p.Ratios.Ratio("neto")!.Valor);
        // Sin el ejercicio anterior no se completan los 12 meses: los ratios de resultados salen sin datos, los de balance no.
        var sinAnterior = CalculadoraInicio.Calcular(a, null, null, Hoy, [], new Dictionary<int, ActividadInicio>(), [], null, ModeloSintetico.Situacion, null);
        Assert.Equal(ProConta.Contabilidad.Ratios.EstadoRatio.SinDatos, sinAnterior.Ratios!.Ratio("roe")!.Estado);
        Assert.Contains("2025", sinAnterior.Ratios.Ratio("roe")!.Motivo);
        Assert.NotNull(sinAnterior.Ratios.Ratio("liquidez")!.Valor);
        // Sin modelo de balance no hay ratios.
        Assert.Null(CalculadoraInicio.Calcular(a, c, c, Hoy, [], new Dictionary<int, ActividadInicio>(), []).Ratios);
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
    private sealed class DialogosFijos(IReadOnlySet<string>? respuesta, IReadOnlySet<string>? inversiones = null) : IDialogosInicio
    {
        public IReadOnlyList<CuentaBancoInicio>? BancosOfrecidos { get; private set; }
        public FichaRatioVM? Ficha { get; private set; }

        public EleccionInicio? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos, IReadOnlyList<CuentaBancoInicio> bancos)
        {
            BancosOfrecidos = bancos;
            return respuesta is null ? null : new EleccionInicio(respuesta, inversiones ?? bancos.Where(b => b.Inversion).Select(b => b.Cuenta).ToHashSet());
        }

        public void VerRatio(FichaRatioVM ficha) => Ficha = ficha;
    }

    private static (InicioVM Vm, FuenteSintetica Fuente, PreferenciasEnMemoria Prefs, List<string> Abiertas) Crear(
        AccionesInicio? acciones = null, IReadOnlySet<string>? personalizar = null, DialogosFijos? dialogos = null, bool conBroker = false)
    {
        var fuente = new FuenteSintetica(conBroker);
        var prefs = new PreferenciasEnMemoria();
        var abiertas = new List<string>();
        acciones ??= new AccionesInicio(AbrirFacturas: () => abiertas.Add("facturas"), AbrirMayor: c => abiertas.Add("mayor " + string.Join(",", c)),
            AbrirBalanceSituacion: () => abiertas.Add("balance"));
        var vm = new InicioVM(new ServicioInicio(fuente, () => new DateTime(2026, 11, 15)), new ContextoInicio("T01", 2026, "Sociedad sintética", _ => { }),
            dialogos ?? new DialogosFijos(personalizar), acciones, prefs);
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
        Assert.Equal("EBITDA proyectado al cierre: " + ProConta.UI.Base.Formato.Formatos.Importe(8400m), vm.Indicadores[1].Proyeccion);
        Assert.Contains("no cuenta las amortizaciones", vm.Indicadores[1].Ayuda);
        Assert.Equal("Tesorería operativa", vm.Indicadores[2].Titulo);
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
        await vm.PersonalizarAsync();
        Assert.False(vm.VerGastos);
        Assert.False(vm.VerCobros);
        Assert.True(vm.VerFacturacion);
        Assert.Equal(["cobros", "gastos"], prefs.Leer<List<string>>(InicioVM.ClaveOcultos));
        // Otro modelo de vista con las mismas preferencias arranca con esos bloques ocultos.
        var otro = new InicioVM(new ServicioInicio(new FuenteSintetica()), new ContextoInicio("T01", 2026, "S", _ => { }), new DialogosFijos(null), null, prefs);
        Assert.False(otro.VerGastos);
    }

    [Fact]
    public async Task MarcarUnaCuentaDeInversionLaSacaDeLaTesoreriaYSeGuardaPorSociedad()
    {
        var dialogos = new DialogosFijos(new HashSet<string>(), new HashSet<string> { "5720019" });
        var (vm, fuente, prefs, _) = Crear(dialogos: dialogos, conBroker: true);
        await vm.IniciarAsync();
        var antes = vm.Panel!.Tesoreria.Valor;
        Assert.False(vm.HayInversiones);
        var lecturas = fuente.Lecturas;
        await vm.PersonalizarAsync();
        Assert.Equal(["5720001", "5720019"], dialogos.BancosOfrecidos!.Select(b => b.Cuenta));
        Assert.Equal(["5720019"], prefs.Leer<List<string>>(InicioVM.ClaveInversiones("T01")));
        Assert.Equal(antes - 3000m, vm.Panel!.Tesoreria.Valor);
        Assert.True(vm.HayInversiones);
        Assert.Equal("Bróker sintético", vm.Inversiones.Single().Nombre);
        Assert.StartsWith("Inversiones · ", vm.TextoInversiones);
        Assert.Contains("5720019", vm.Indicadores[2].Ayuda);
        // Recalcula sin volver a leer la BD.
        Assert.Equal(lecturas, fuente.Lecturas);
        // Otro modelo de vista de la misma sociedad arranca con la marca.
        var otro = new InicioVM(new ServicioInicio(new FuenteSintetica(true), () => new DateTime(2026, 11, 15)), new ContextoInicio("T01", 2026, "S", _ => { }), new DialogosFijos(null), null, prefs);
        Assert.Contains("5720019", otro.CuentasInversion);
    }

    [Fact]
    public async Task BloqueDeRatiosConRecuentoFranjaYFicha()
    {
        var dialogos = new DialogosFijos(null);
        var (vm, _, _, abiertas) = Crear(dialogos: dialogos);
        await vm.IniciarAsync();
        Assert.True(vm.VerRatios);
        Assert.Null(vm.SinRatios);
        Assert.Equal("balance a 31/10/2026 · resultado de los últimos 12 meses", vm.TextoRatios);
        Assert.Equal(["Liquidez", "Solvencia y endeudamiento", "Rentabilidad y márgenes", "Gestión"], vm.FamiliasRatios.Select(f => f.Nombre));
        Assert.Equal(12, vm.FamiliasRatios.Sum(f => f.Filas.Count));
        var p = vm.Panel!.Ratios!;
        Assert.Equal(p.Bien + p.Vigilar + p.Mal + p.Ratios.Count(r => r.Estado == ProConta.Contabilidad.Ratios.EstadoRatio.SinDatos), 12);
        Assert.Equal($"✓ {p.Bien} bien", vm.RecuentoBien);
        // La raya gris es el comparado (2025 a cierre).
        var margen = vm.FamiliasRatios[2].Filas.Single(f => f.Nombre == "Margen neto");
        Assert.Equal(vm.Panel.SerieRatios[0].Ratios.Ratio("neto")!.Valor, margen.Comparado);
        Assert.Contains("Raya gris: 2025", vm.NotaRatios);
        // Ficha: partidas con su Mayor, evolución por ejercicio y abrir el balance.
        margen.Comando.Execute(null);
        var ficha = dialogos.Ficha!;
        Assert.Equal("Margen neto", ficha.Titulo);
        Assert.Equal(["Resultado neto (12 meses)", "Ventas (12 meses)"], ficha.Partidas.Select(x => x.Nombre));
        Assert.Equal(["2025", "2026*"], ficha.Evolucion.Select(e => e.Ejercicio));
        var cerrada = false;
        ficha.Cerrar = () => cerrada = true;
        ficha.Partidas[1].AbrirMayor!.Execute(null);
        Assert.True(cerrada);
        Assert.Equal("mayor 7000001", abiertas[^1]);
        ficha.AbrirBalanceCommand!.Execute(null);
        Assert.Equal("balance", abiertas[^1]);
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
