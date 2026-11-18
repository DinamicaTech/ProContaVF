using ProConta.Facturacion.Programadas;
using ProConta.Inicio.Bloques;
using ProConta.Inicio.Salud;
using ProConta.Inicio.UI;

namespace ProConta.Inicio.Tests;

/// <summary>Reglas de la fase 4 de Inicio sin BD: amortizaciones, céntimos residuales, ocupación, agenda y tendencia «desde el lunes».</summary>
public sealed class Fase4Tests
{
    private static readonly DateTime Hoy = new(2026, 11, 15);

    // ------------------------------------------------------------ amortizaciones

    [Fact]
    public void LaPeriodicidadSeDeduceDeLosMeses()
    {
        Assert.Equal(PeriodicidadAmortizacion.Anual, RevisionAmortizaciones.Periodicidad([12, 12]));
        Assert.Equal(PeriodicidadAmortizacion.Anual, RevisionAmortizaciones.Periodicidad([]));
        Assert.Equal(PeriodicidadAmortizacion.Trimestral, RevisionAmortizaciones.Periodicidad([3, 6, 9]));
        Assert.Equal(PeriodicidadAmortizacion.Mensual, RevisionAmortizaciones.Periodicidad([1, 2, 3]));
        Assert.Equal(PeriodicidadAmortizacion.Trimestral, RevisionAmortizaciones.Periodicidad([6, 12]));
    }

    [Fact]
    public void SinUnPatronClaroLaPeriodicidadEsAnual()
    {
        // Un asiento suelto (una baja a mitad de año, una corrección) o meses salteados no muestran otra periodicidad.
        Assert.Null(RevisionAmortizaciones.Deducir([5]));
        Assert.Null(RevisionAmortizaciones.Deducir([3]));
        Assert.Null(RevisionAmortizaciones.Deducir([5, 12]));
        Assert.Null(RevisionAmortizaciones.Deducir([2, 7, 12]));
        Assert.Equal(PeriodicidadAmortizacion.Anual, RevisionAmortizaciones.Periodicidad([5]));
        Assert.Equal(PeriodicidadAmortizacion.Anual, RevisionAmortizaciones.Periodicidad([5, 12]));
        Assert.Equal(PeriodicidadAmortizacion.Anual, RevisionAmortizaciones.Periodicidad([3]));
    }

    [Fact]
    public void SiElEjercicioNoMuestraPatronSeMiraElAnterior()
    {
        // Mensual en 2025; en 2026 solo el de enero (aún no hay dos meses seguidos): sigue siendo mensual y avisa de febrero en adelante.
        var mensual2025 = Enumerable.Range(1, 12).Select(m => new AsientoAmortizacion(2025, new DateTime(2025, m, 28), 10m));
        var h = RevisionAmortizaciones.Evaluar(2026, [.. mensual2025, new AsientoAmortizacion(2026, new DateTime(2026, 1, 31), 10m)], 1, Hoy);
        Assert.Equal(12, h.Periodos[0].Celdas.Count);
        Assert.StartsWith("Amortización sin contabilizar: febrero", Assert.Single(h.Elementos).Titulo);
        Assert.Contains("según los asientos de amortización de 2025", h.Elementos[0].Explicacion);
        // Anual en 2025 y una baja en mayo de 2026: sigue siendo anual, sin aviso hasta que termine el año.
        var anual = new[] { new AsientoAmortizacion(2025, new DateTime(2025, 12, 31), 500m), new AsientoAmortizacion(2026, new DateTime(2026, 5, 20), 80m) };
        var a = RevisionAmortizaciones.Evaluar(2026, anual, 1, Hoy);
        Assert.Empty(a.Elementos);
        Assert.Contains("anual", a.Periodos[0].Celdas[0].Ayuda);
        Assert.Equal(EstadoPeriodo.Hecho, a.Periodos[0].Celdas[4].Estado);
        // Sin patrón en ninguno de los dos (un asiento suelto en junio de 2025): anual por defecto; 2025 se da por amortizado.
        var suelto = RevisionAmortizaciones.Evaluar(2025, [new AsientoAmortizacion(2025, new DateTime(2025, 6, 30), 80m)], 1, Hoy);
        Assert.Empty(suelto.Elementos);
        Assert.Equal(12, suelto.Periodos[0].Celdas.Count);
        Assert.Contains("por defecto", suelto.Periodos[0].Celdas[0].Ayuda);
    }

    [Fact]
    public void AmortizacionAnualNoAvisaHastaQueTerminaElAnio()
    {
        // Como en la copia de validación: amortiza el 31/12 de 2025 y en 2026 todavía no.
        var asientos = new[] { new AsientoAmortizacion(2025, new DateTime(2025, 12, 31), 69639.09m) };
        var h = RevisionAmortizaciones.Evaluar(2026, asientos, 30, Hoy);
        Assert.Empty(h.Elementos);
        var fila = Assert.Single(h.Periodos);
        Assert.Equal("Amortizaciones", fila.Nombre);
        Assert.Equal(12, fila.Celdas.Count);
        Assert.Equal(EstadoPeriodo.NoToca, fila.Celdas[5].Estado);
        Assert.Equal(EstadoPeriodo.EnCurso, fila.Celdas[11].Estado); // el año 2026 aún no ha terminado
        Assert.Contains("anual", fila.Celdas[5].Ayuda);
        // En 2025, con el asiento de diciembre, está hecho; trabajando en 2025 desde 2026 el año está cerrado.
        var h2025 = RevisionAmortizaciones.Evaluar(2025, asientos, 30, Hoy);
        Assert.Empty(h2025.Elementos);
        Assert.Equal(EstadoPeriodo.Hecho, h2025.Periodos[0].Celdas[11].Estado);
        Assert.Single(h2025.Correctas);
        // Sin el asiento, el año 2025 ya cerrado avisa.
        var falta = RevisionAmortizaciones.Evaluar(2025, [], 30, Hoy);
        var e = Assert.Single(falta.Elementos);
        Assert.Equal(GravedadSalud.Atencion, e.Gravedad);
        Assert.Equal("Falta la amortización de 2025", e.Titulo);
        Assert.Equal(EstadoPeriodo.Incompleto, falta.Periodos[0].Celdas[11].Estado);
    }

    [Fact]
    public void AmortizacionMensualAvisaDeLosMesesCerradosSinAsiento()
    {
        var asientos = Enumerable.Range(1, 9).Select(m => new AsientoAmortizacion(2026, new DateTime(2026, m, 28), 100m)).ToList();
        var h = RevisionAmortizaciones.Evaluar(2026, asientos, 3, Hoy);
        var e = Assert.Single(h.Elementos);
        Assert.Equal("Amortización sin contabilizar: octubre", e.Titulo);
        Assert.Equal(1, e.Cantidad);
        var celdas = h.Periodos[0].Celdas;
        Assert.Equal(EstadoPeriodo.Hecho, celdas[0].Estado);
        Assert.Equal(EstadoPeriodo.Incompleto, celdas[9].Estado);
        Assert.Equal(EstadoPeriodo.EnCurso, celdas[10].Estado);
        Assert.Equal(EstadoPeriodo.NoToca, celdas[11].Estado);
    }

    [Fact]
    public void AmortizacionTrimestralUsaCuatroCeldas()
    {
        var asientos = new[] { new AsientoAmortizacion(2026, new DateTime(2026, 3, 31), 1m), new AsientoAmortizacion(2026, new DateTime(2026, 6, 30), 1m) };
        var h = RevisionAmortizaciones.Evaluar(2026, asientos, 1, Hoy);
        Assert.Equal(4, h.Periodos[0].Celdas.Count);
        Assert.True(h.Periodos[0].Trimestral);
        Assert.Equal("Amortización sin contabilizar: 3T", Assert.Single(h.Elementos).Titulo);
    }

    [Fact]
    public void SinActivosNiAmortizacionesNoSeMira()
    {
        Assert.Same(HallazgosSalud.Nada, RevisionAmortizaciones.Evaluar(2026, [], 0, Hoy));
        // Con activos y sin ningún asiento: anual; a mitad de año no avisa.
        Assert.Empty(RevisionAmortizaciones.Evaluar(2026, [], 2, Hoy).Elementos);
    }

    // ------------------------------------------------------------ céntimos residuales

    [Fact]
    public void CentimosResidualesEnClientesYProveedores()
    {
        var saldos = new Dictionary<string, decimal>
        {
            ["4300001"] = 0.01m, ["4300002"] = -0.4m, ["4300003"] = 120m, ["4000001"] = -0.999m, ["4100001"] = 0.004m, ["4100002"] = 1m, ["4200001"] = 0.5m,
        };
        var e = Assert.Single(RevisionCentimosResiduales.Evaluar(saldos, Hoy).Elementos);
        Assert.Equal(GravedadSalud.Aviso, e.Gravedad);
        // 0,004 redondea a 0,00 (no cuenta); −0,999 redondea a −1,00 (no es menor de 1 €); la 42 no es de clientes ni proveedores.
        Assert.Equal(2, e.Cantidad);
        Assert.Equal(0.41m, e.Importe);
        Assert.Equal(PantallaSalud.CentimosResiduales, e.Destino.Pantalla);
        Assert.Equal(["4300001", "4300002"], e.Destino.Cuentas);
        Assert.StartsWith("2 subcuentas", e.Titulo);
        var bien = RevisionCentimosResiduales.Evaluar(new Dictionary<string, decimal> { ["4300001"] = 5m }, Hoy);
        Assert.Empty(bien.Elementos);
        Assert.Single(bien.Correctas);
        // Con el umbral del ejercicio (contabilidad/procesos/centimos-residuales 2026-11-17 19:05): a 0,10 € solo queda la 4300001.
        var conUmbral = Assert.Single(RevisionCentimosResiduales.Evaluar(saldos, Hoy, 0.10m).Elementos);
        Assert.Equal(1, conUmbral.Cantidad);
        Assert.Contains("menor de 0,10", conUmbral.Explicacion);
    }

    // ------------------------------------------------------------ ocupación

    private static readonly AlojamientoOcupacion PisoA = new(48, "PisoA", new DateTime(2021, 7, 14), null);
    private static readonly AlojamientoOcupacion PisoB = new(49, "PisoB", new DateTime(2026, 6, 1), null);

    [Fact]
    public void LasNochesVanDeLaEntradaAlDiaAnteriorALaSalida()
    {
        var noches = CalculoOcupacion.Noches(new ReservaOcupacion(48, new DateTime(2026, 1, 30), new DateTime(2026, 2, 2), 3m)).ToList();
        Assert.Equal([new DateTime(2026, 1, 30), new DateTime(2026, 1, 31), new DateTime(2026, 2, 1)], noches);
        // Sin salida, tantas noches como la cantidad.
        Assert.Equal(2, CalculoOcupacion.Noches(new ReservaOcupacion(48, new DateTime(2026, 1, 1), null, 2m)).Count());
    }

    [Fact]
    public void OcupacionPorMesYPorAlojamiento()
    {
        var reservas = new List<ReservaOcupacion>
        {
            new(48, new DateTime(2026, 1, 30), new DateTime(2026, 2, 2), 3m), // 2 noches en enero, 1 en febrero
            new(49, new DateTime(2026, 6, 10), new DateTime(2026, 6, 20), 10m),
            new(null, new DateTime(2026, 6, 1), new DateTime(2026, 6, 4), 3m), // del ERP, sin piso
            new(48, new DateTime(2026, 12, 20), new DateTime(2026, 12, 24), 4m), // reserva ya hecha para diciembre
            new(48, new DateTime(2025, 1, 10), new DateTime(2025, 1, 13), 3m),
        };
        var p = CalculoOcupacion.Calcular(2026, 2025, Hoy, reservas, [PisoA, PisoB])!;
        Assert.Equal(2, p.Actual.Ocupadas[0]);
        Assert.Equal(31, p.Actual.Disponibles[0]); // el piso B aún no estaba
        Assert.Equal(6.5m, p.Actual.Porcentaje(1));
        Assert.Equal(13, p.Actual.Ocupadas[5]); // 10 del piso B + 3 sin piso
        Assert.Equal(60, p.Actual.Disponibles[5]);
        Assert.Equal(12, p.UltimoMes); // hay reservas hechas para diciembre
        Assert.Equal(3, p.Comparado!.Ocupadas[0]);
        var a = p.Alojamientos.Single(x => x.IdActivo == 48);
        Assert.Equal(3, a.Ocupadas); // hasta el 15/11, sin la de diciembre
        Assert.Equal(319, a.Disponibles);
        Assert.Equal(3, a.OcupadasComparado);
        Assert.Equal(319, a.DisponiblesComparado);
        var sin = p.Alojamientos.Single(x => x.IdActivo is null);
        Assert.Equal(CalculoOcupacion.SinAlojamiento, sin.Nombre);
        Assert.Equal(3, sin.Ocupadas);
        Assert.Null(sin.Porcentaje);
        Assert.Equal(16, p.Total.Ocupadas);
    }

    [Fact]
    public void SiElComparadoTieneReservasSinPisoNoSeDaSuOcupacionPorPiso()
    {
        var reservas = new List<ReservaOcupacion>
        {
            new(48, new DateTime(2026, 7, 1), new DateTime(2026, 7, 11), 10m),
            new(null, new DateTime(2025, 7, 1), new DateTime(2025, 7, 21), 20m), // del ERP
        };
        var p = CalculoOcupacion.Calcular(2026, 2025, Hoy, reservas, [PisoA])!;
        var piso = p.Alojamientos.Single(a => a.IdActivo == 48);
        Assert.Null(piso.OcupadasComparado);
        Assert.Null(piso.PorcentajeComparado);
        // El total sí: todas las noches entre las disponibles.
        Assert.Equal(10, p.Total.Ocupadas);
        Assert.Equal(20, p.Total.OcupadasComparado);
        Assert.Equal(319, p.Total.DisponiblesComparado);
    }

    [Fact]
    public void SinReservasNoHayBloque() => Assert.Null(CalculoOcupacion.Calcular(2026, 2025, Hoy, [], [PisoA]));

    // ------------------------------------------------------------ agenda

    [Fact]
    public void AgendaConPlazosFiscalesYProgramadasEnOrden()
    {
        var programadas = new List<VencimientoProgramado>
        {
            new(1, "P-0001", "Cliente A", new DateTime(2026, 12, 5), 121m),
            new(2, "P-0002", "Cliente B", new DateTime(2026, 12, 5), 242m),
            new(3, "P-0003", "Cliente C", new DateTime(2027, 1, 1), 100m),
            new(4, "P-0004", "Cliente D", new DateTime(2026, 11, 5), 50m), // atrasada: sin generar
            new(5, "P-0005", "Cliente E", new DateTime(2027, 3, 1), 10m), // fuera de los 90 días
        };
        var cambio = new CambioTipoIva(new DateTime(2026, 12, 1), "el alquiler turístico", "10 %", "prueba");
        var agenda = CalculoAgenda.Calcular(Hoy, new HashSet<int> { 2025, 2026 }, programadas, [cambio]);
        Assert.Equal(new DateTime(2026, 11, 15), agenda[0].Fecha);
        Assert.Contains("pendiente de generar", agenda[0].Texto);
        Assert.Equal(TipoAgenda.CambioIva, agenda[1].Tipo);
        Assert.Equal("El alquiler turístico pasa al 10 % de IVA", agenda[1].Texto);
        var dic5 = agenda.Single(e => e.Fecha == new DateTime(2026, 12, 5));
        Assert.Equal("Se emiten 2 facturas programadas", dic5.Texto);
        Assert.Equal(363m, dic5.Importe);
        Assert.Contains(agenda, e => e.Tipo == TipoAgenda.Ejercicio && e.Fecha == new DateTime(2026, 12, 31));
        Assert.Contains(agenda, e => e.Texto == "Se emite la factura programada P-0003 (Cliente C)");
        Assert.Contains(agenda, e => e.Texto == "Último día del 303 del 4T de 2026" && e.Fecha == new DateTime(2027, 2, 1));   // el 30/1 es sábado
        Assert.Contains(agenda, e => e.Texto == "Último día del resumen anual 390 de 2026");
        Assert.DoesNotContain(agenda, e => e.Texto.Contains("P-0005"));
        Assert.Equal(agenda.OrderBy(e => e.Fecha).Select(e => e.Fecha), agenda.Select(e => e.Fecha));
        // Con el ejercicio siguiente creado, no hay recordatorio de crearlo.
        Assert.DoesNotContain(CalculoAgenda.Calcular(Hoy, new HashSet<int> { 2026, 2027 }, [], [cambio]), e => e.Tipo == TipoAgenda.Ejercicio);
    }

    // ------------------------------------------------------------ tendencia

    [Fact]
    public void ElLunesDeLaSemana()
    {
        Assert.Equal(new DateTime(2026, 11, 9), TendenciaSalud.Lunes(new DateTime(2026, 11, 15))); // domingo
        Assert.Equal(new DateTime(2026, 11, 9), TendenciaSalud.Lunes(new DateTime(2026, 11, 9)));
        Assert.Equal(new DateTime(2026, 11, 9), TendenciaSalud.Lunes(new DateTime(2026, 11, 11)));
    }

    [Fact]
    public void VariacionesYTexto()
    {
        var hoy = new[] { new FilaHistoricoSalud("r", "a", 40, null), new FilaHistoricoSalud("r", "b", 3, 10m), new FilaHistoricoSalud("r", "c", 1, null), new FilaHistoricoSalud("r", "d", 2, null) };
        var lunes = new[] { new FilaHistoricoSalud("r", "a", 37, null), new FilaHistoricoSalud("r", "b", 5, 20m), new FilaHistoricoSalud("r", "d", 2, null) };
        var v = TendenciaSalud.Variaciones(hoy, lunes);
        Assert.Equal(3, v["a"]);
        Assert.Equal(-2, v["b"]);
        Assert.False(v.ContainsKey("c")); // sin dato el lunes
        Assert.False(v.ContainsKey("d")); // sin cambio
        Assert.Equal("+3 desde el lunes", TendenciaSalud.Texto(3));
        Assert.Equal("−2 desde el lunes", TendenciaSalud.Texto(-2));
        Assert.Null(TendenciaSalud.Texto(0));
        Assert.Null(TendenciaSalud.Texto(null));
    }

    private sealed class HistoricoEnMemoria : IHistoricoSalud
    {
        public Dictionary<DateTime, IReadOnlyList<FilaHistoricoSalud>> Dias { get; } = [];
        public bool Falla { get; init; }

        public Task GuardarAsync(string idSociedad, int ejercicio, DateTime fecha, IReadOnlyList<FilaHistoricoSalud> filas, CancellationToken c = default)
        {
            if (Falla) throw new InvalidOperationException("La tabla HistoricoSalud no existe");
            Dias[fecha.Date] = filas;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FilaHistoricoSalud>> LeerAsync(string idSociedad, int ejercicio, DateTime fecha, CancellationToken c = default) =>
            Task.FromResult(Dias.TryGetValue(fecha.Date, out var f) ? f : (IReadOnlyList<FilaHistoricoSalud>)[]);
    }

    [Fact]
    public async Task LaRevisionGuardaElDiaYComparaConElLunes()
    {
        var historico = new HistoricoEnMemoria();
        historico.Dias[new DateTime(2026, 11, 9)] = [new FilaHistoricoSalud("compras-pendientes", "compras-pendientes", 6, 100m)];
        var fuente = new FuenteSaludFija { Compras = new ComprasPendientes(8, 120m, null) };
        var servicio = new ServicioSalud(fuente, hoy: () => Hoy, historico: historico);
        var informe = await servicio.RevisarAsync("T01", 2026);
        Assert.Equal(2, informe.Tendencias["compras-pendientes"]);
        var guardado = historico.Dias[Hoy].Single(f => f.ClaveElemento == "compras-pendientes");
        Assert.Equal(8, guardado.Elementos);
        Assert.Equal(120m, guardado.Importe);
        Assert.Null(servicio.ErrorHistorico);
        // Las revisiones que fallan no se guardan.
        Assert.DoesNotContain(historico.Dias[Hoy], f => f.ClaveElemento.StartsWith("error-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SiElHistoricoFallaLaRevisionSaleIgual()
    {
        var fuente = new FuenteSaludFija { Compras = new ComprasPendientes(8, 120m, null) };
        var servicio = new ServicioSalud(fuente, hoy: () => Hoy, historico: new HistoricoEnMemoria { Falla = true });
        var informe = await servicio.RevisarAsync("T01", 2026);
        Assert.Contains(informe.Elementos, e => e.Clave == "compras-pendientes");
        Assert.Empty(informe.Tendencias);
        Assert.Contains("HistoricoSalud", servicio.ErrorHistorico);
    }

    [Fact]
    public async Task LaTendenciaSeVeEnElAviso()
    {
        var historico = new HistoricoEnMemoria();
        historico.Dias[new DateTime(2026, 11, 9)] = [new FilaHistoricoSalud("compras-pendientes", "compras-pendientes", 10, null)];
        var fuente = new FuenteSaludFija { Compras = new ComprasPendientes(8, 120m, null) };
        var vm = new SaludVM(new ServicioSalud(fuente, hoy: () => Hoy, historico: historico), new ContextoInicio("T01", 2026, "Prueba", _ => { }), new AccionesInicio(),
            new DialogosFalsos(), () => new HashSet<string>());
        await vm.RevisarAsync();
        var aviso = vm.Elementos.Single(e => e.Clave == "compras-pendientes");
        Assert.Equal("−2 desde el lunes", aviso.Tendencia);
        Assert.Equal("Baja", aviso.TonoTendencia);
        Assert.Contains("−2 desde el lunes", aviso.TextoCopiar);
        Assert.Null(vm.Elementos.Single(e => e.Clave.StartsWith("303-2026-1", StringComparison.Ordinal)).Tendencia);
    }

    private sealed class DialogosFalsos : IDialogosInicio
    {
        public EleccionInicio? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos, IReadOnlyList<CuentaBancoInicio> bancos) => null;
        public void VerRatio(FichaRatioVM ficha) { }
    }

    // ------------------------------------------------------------ bloques en la pantalla

    [Fact]
    public void ElBloqueDeOcupacionSoloApareceConReservas()
    {
        var vm = new BloquesFase4VM(new ServicioBloques(new FuenteBloquesVacia()), new ContextoInicio("T01", 2026, "Prueba", _ => { }), new AccionesInicio());
        vm.Aplicar(null);
        Assert.False(vm.HayOcupacion);
        vm.Aplicar(CalculoOcupacion.Calcular(2026, 2025, Hoy, [new ReservaOcupacion(48, new DateTime(2026, 3, 1), new DateTime(2026, 3, 5), 4m)], [PisoA]));
        Assert.True(vm.HayOcupacion);
        Assert.Equal(["PisoA", "Total"], vm.FilasOcupacion.Select(f => f.Alojamiento));
        Assert.Equal("2025", vm.CabeceraComparado);
        Assert.NotNull(vm.DatosOcupacion);
    }

    [Fact]
    public void LaAgendaAbreLasProgramadasSiLaPantallaExiste()
    {
        var abiertas = 0;
        var vm = new BloquesFase4VM(new ServicioBloques(new FuenteBloquesVacia()), new ContextoInicio("T01", 2026, "Prueba", _ => { }),
            new AccionesInicio { AbrirProgramadas = () => abiertas++ });
        vm.AplicarAgenda(CalculoAgenda.Calcular(Hoy, new HashSet<int> { 2026, 2027 }, [new VencimientoProgramado(1, "P-0001", "Cliente A", new DateTime(2026, 12, 5), 121m)], []));
        var e = vm.Agenda.Single(x => x.Texto.StartsWith("Se emite", StringComparison.Ordinal));
        Assert.Equal("sáb 05/12", e.Dia);
        Assert.True(e.PuedeAbrir);
        e.Abrir!.Execute(null);
        Assert.Equal(1, abiertas);
        Assert.False(vm.Agenda.First(x => x.Texto.Contains("303")).PuedeAbrir);
    }

    private sealed class FuenteBloquesVacia : IFuenteBloques
    {
        public Task<IReadOnlyList<int>> EjerciciosAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<int>>([2026]);
        public Task<IReadOnlyList<ReservaOcupacion>> ReservasAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ReservaOcupacion>>([]);
        public Task<IReadOnlyList<AlojamientoOcupacion>> AlojamientosAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<AlojamientoOcupacion>>([]);
        public Task<IReadOnlyList<VencimientoProgramado>> ProgramadasAsync(string idSociedad, DateTime hasta, CancellationToken c = default) => Task.FromResult<IReadOnlyList<VencimientoProgramado>>([]);
    }
}
