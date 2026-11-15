using ProConta.Inicio;
using ProConta.Inicio.Salud;
using ProConta.Inicio.UI;

namespace ProConta.Inicio.Tests;

/// <summary>Fuente de salud en memoria: cada lectura devuelve lo que se le ponga.</summary>
internal sealed class FuenteSaludFija : IFuenteSalud
{
    public List<EjercicioSociedad> Ejercicios { get; set; } = [new(9001, 2025), new(9002, 2026)];
    public VentasPendientes Ventas { get; set; } = new(0, 0, 0, 0);
    public List<ApunteIva> Iva { get; set; } = [];
    public List<LiquidacionIva> Liquidaciones { get; set; } = [];
    public List<BancoSalud> Bancos { get; set; } = [];
    public List<MesBanco> Lineas { get; set; } = [];
    public ComprasPendientes Compras { get; set; } = new(0, 0, null);
    public List<DateTime> Borradores { get; set; } = [];
    public Dictionary<string, decimal> Saldos { get; set; } = [];
    public int Asientos { get; set; } = 10;
    public List<AsientoDescuadrado> Descuadrados { get; set; } = [];
    public List<NumeroFactura> Numeros { get; set; } = [];
    public List<IvaLibrosTrimestre> Libros { get; set; } = [];
    public List<TerceroSinNif> SinNif { get; set; } = [];
    public List<CuentaConProblema> CuentasMal { get; set; } = [];
    public List<NominaMes> Nominas { get; set; } = [];
    public bool FallaNominas { get; set; }
    public int LecturasBancos;

    public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<EjercicioSociedad>>(Ejercicios);
    public Task<VentasPendientes> VentasPendientesAsync(string idSociedad, DateTime antesDe, CancellationToken c = default) => Task.FromResult(Ventas);
    public Task<IReadOnlyList<ApunteIva>> IvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<ApunteIva>>(Iva.Where(a => a.Fecha.Year == ejercicio.Ejercicio).ToList());
    public Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ActividadInicio>>([]);
    public Task<IReadOnlyList<LiquidacionIva>> LiquidacionesIvaAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) => Task.FromResult<IReadOnlyList<LiquidacionIva>>(Liquidaciones);
    public Task<IReadOnlyList<BancoSalud>> BancosAsync(string idSociedad, ContextoSalud contexto, CancellationToken c = default)
    {
        Interlocked.Increment(ref LecturasBancos);
        return Task.FromResult<IReadOnlyList<BancoSalud>>(Bancos);
    }
    public Task<IReadOnlyList<MesBanco>> LineasBancoAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<MesBanco>>(Lineas);
    public Task<ComprasPendientes> ComprasPendientesAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult(Compras);
    public Task<IReadOnlyList<DateTime>> BorradoresVentaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<IReadOnlyList<DateTime>>(Borradores);
    public Task<IReadOnlyDictionary<string, decimal>> SaldosAsync(EjercicioSociedad ejercicio, string prefijoDesde, string prefijoHasta, DateTime hasta, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyDictionary<string, decimal>>(Saldos.Where(s => string.CompareOrdinal(s.Key[..prefijoDesde.Length], prefijoDesde) >= 0 && string.CompareOrdinal(s.Key[..prefijoHasta.Length], prefijoHasta) <= 0)
            .ToDictionary(s => s.Key, s => s.Value));
    public Task<(int Asientos, IReadOnlyList<AsientoDescuadrado> Descuadrados)> CuadreDiarioAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult((Asientos, (IReadOnlyList<AsientoDescuadrado>)Descuadrados));
    public Task<IReadOnlyList<NumeroFactura>> NumerosFacturasAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<IReadOnlyList<NumeroFactura>>(Numeros);
    public Task<IReadOnlyList<IvaLibrosTrimestre>> LibrosIvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<IReadOnlyList<IvaLibrosTrimestre>>(Libros);
    public Task<IReadOnlyList<TerceroSinNif>> TercerosSinNifAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<IReadOnlyList<TerceroSinNif>>(SinNif);
    public Task<IReadOnlyList<CuentaConProblema>> CuentasConProblemaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<IReadOnlyList<CuentaConProblema>>(CuentasMal);
    public Task<IReadOnlyList<NominaMes>> NominasAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) =>
        FallaNominas ? throw new InvalidOperationException("Tabla de nóminas bloqueada") : Task.FromResult<IReadOnlyList<NominaMes>>(Nominas);
}

/// <summary>Las revisiones de la salud de la contabilidad (pantalla-de-inicio, fase 3), sin BD: casos que disparan y que no.</summary>
public sealed class RevisionesSaludTests
{
    private static readonly DateTime Hoy = new(2026, 11, 15);
    private static readonly CambioTipoIva Turistico = new(new DateTime(2026, 12, 1), "el alquiler turístico", "10 %", "prueba");

    // ------------------------------------------------------------ tareas

    [Fact]
    public void VentasAntesDelCambioDeIvaEsGraveConPlazoElDiaAnterior()
    {
        var e = RevisionVentasAntesCambioIva.Evaluar(Turistico, new VentasPendientes(12, 10000m, 2, 380m), Hoy)!;
        Assert.Equal(GravedadSalud.Grave, e.Gravedad);
        Assert.Equal(TipoSalud.Tarea, e.Tipo);
        Assert.Equal(new DateTime(2026, 11, 30), e.Plazo);
        Assert.Equal(15, e.DiasHastaPlazo(Hoy));
        Assert.Equal("Facturar las ventas pendientes antes del 1/12", e.Titulo);
        Assert.Contains("14 ventas", e.Explicacion);
        Assert.Contains("2 con la factura en borrador", e.Explicacion);
        Assert.Contains("10.380,00 €", e.Explicacion);
        Assert.Equal(PantallaSalud.VentasPorFacturar, e.Destino.Pantalla);
        Assert.Equal("facturacion/reservas", e.Nodo);
    }

    [Fact]
    public void VentasAntesDelCambioDeIvaNoSaleSinVentasNiFueraDeSuVentana()
    {
        Assert.Null(RevisionVentasAntesCambioIva.Evaluar(Turistico, new VentasPendientes(0, 0, 0, 0), Hoy));
        // 90 días antes del cambio empieza a verse: el 2/9 todavía no, el 2/9 + 1 sí... el 3/9 ya.
        Assert.Null(RevisionVentasAntesCambioIva.Evaluar(Turistico, new VentasPendientes(1, 100m, 0, 0), new DateTime(2026, 9, 1)));
        Assert.NotNull(RevisionVentasAntesCambioIva.Evaluar(Turistico, new VentasPendientes(1, 100m, 0, 0), new DateTime(2026, 9, 2)));
        // Pasado el cambio sigue, vencida, mientras queden ventas.
        var vencida = RevisionVentasAntesCambioIva.Evaluar(Turistico, new VentasPendientes(1, 100m, 0, 0), new DateTime(2026, 12, 3))!;
        Assert.True(vencida.DiasHastaPlazo(new DateTime(2026, 12, 3)) < 0);
        Assert.Contains("aún quedan", vencida.Explicacion);
        // El dato del cambio de tipo está en un solo sitio.
        Assert.Contains(CalendarioFiscal.CambiosTipoIva, c => c.Desde == new DateTime(2026, 12, 1) && c.TipoNuevo == "10 %");
    }

    [Fact]
    public void EjercicioSiguienteFaltaConMenosDe60Dias()
    {
        var ejercicios = new HashSet<int> { 2025, 2026 };
        var e = RevisionEjercicioSiguiente.Evaluar(ejercicios, Hoy)!;
        Assert.Equal("Crear el ejercicio 2027", e.Titulo);
        Assert.Equal(GravedadSalud.Aviso, e.Gravedad);
        Assert.Equal(new DateTime(2026, 12, 31), e.Plazo);
        Assert.Equal(GravedadSalud.Atencion, RevisionEjercicioSiguiente.Evaluar(ejercicios, new DateTime(2026, 12, 16))!.Gravedad);
        Assert.Null(RevisionEjercicioSiguiente.Evaluar(ejercicios, new DateTime(2026, 11, 1)));   // 60 días: aún no
        Assert.NotNull(RevisionEjercicioSiguiente.Evaluar(ejercicios, new DateTime(2026, 11, 2)));
        Assert.Null(RevisionEjercicioSiguiente.Evaluar(new HashSet<int> { 2026, 2027 }, Hoy));
    }

    [Theory]
    [InlineData("2026-03-31", 2026, 1)]
    [InlineData("2026-04-20", 2026, 1)]   // último día del plazo del 1T
    [InlineData("2026-04-21", 2026, 2)]
    [InlineData("2026-12-31", 2026, 4)]
    [InlineData("2027-01-30", 2026, 4)]   // el 4T se liquida en enero del año siguiente
    [InlineData("2027-01-31", 2027, 1)]
    public void ElAsientoDeLiquidacionSeAsignaAlTrimestreDeSuPlazo(string fecha, int anio, int trimestre) =>
        Assert.Equal((anio, trimestre), RevisionPlazosIva.TrimestreLiquidado(DateTime.Parse(fecha)));

    private static HallazgosSalud Plazos(DateTime hoy, params LiquidacionIva[] liquidaciones) =>
        RevisionPlazosIva.Evaluar(new HashSet<int> { 2025, 2026 }, 2026, liquidaciones, hoy, (_, _) => (1000m, 400m));

    [Fact]
    public void El303EnSusFechasLimite()
    {
        var liq = new[] { new LiquidacionIva(2025, new DateTime(2025, 3, 31)), new LiquidacionIva(2025, new DateTime(2025, 6, 30)), new LiquidacionIva(2025, new DateTime(2025, 9, 30)),
            new LiquidacionIva(2025, new DateTime(2025, 12, 31)), new LiquidacionIva(2026, new DateTime(2026, 3, 31)), new LiquidacionIva(2026, new DateTime(2026, 7, 15)) };
        // Último día del plazo del 3T sin liquidar: atención, «hoy».
        var ultimo = Plazos(new DateTime(2026, 10, 20), liq).Elementos.Single(e => e.Clave == "303-2026-3");
        Assert.Equal(GravedadSalud.Atencion, ultimo.Gravedad);
        Assert.Equal(0, ultimo.DiasHastaPlazo(new DateTime(2026, 10, 20)));
        Assert.Contains("600,00 € a ingresar", ultimo.Explicacion);
        // Un día después: vencida y grave.
        var vencida = Plazos(new DateTime(2026, 10, 21), liq).Elementos.Single(e => e.Clave == "303-2026-3");
        Assert.Equal(GravedadSalud.Grave, vencida.Gravedad);
        // Con el asiento de liquidación (fechado en el plazo) está hecha y sale en «Hechas este trimestre».
        var hechas = Plazos(new DateTime(2026, 10, 21), [.. liq, new LiquidacionIva(2026, new DateTime(2026, 10, 20))]);
        Assert.DoesNotContain(hechas.Elementos, e => e.Clave == "303-2026-3");
        Assert.Contains(hechas.Hechas, h => h.Clave == "303-2026-3");
        // El 4T se ve desde 90 días antes del 30 de enero (el 1/11), igual que el 390.
        Assert.DoesNotContain(Plazos(new DateTime(2026, 10, 31), liq).Elementos, e => e.Clave is "303-2026-4" or "390-2026");
        var noviembre = Plazos(new DateTime(2026, 11, 1), [.. liq, new LiquidacionIva(2026, new DateTime(2026, 9, 30))]);
        Assert.Equal(["303-2026-4", "390-2026"], noviembre.Elementos.Select(e => e.Clave).OrderBy(x => x));
        Assert.Equal(GravedadSalud.Aviso, noviembre.Elementos[0].Gravedad);
        // El 390 de 2026 se ve hasta el 30/1/2027 y después desaparece (no se puede saber si está presentado).
        Assert.Contains(Plazos(new DateTime(2027, 1, 30)).Elementos, e => e.Clave == "390-2026");
    }

    [Fact]
    public void LaFilaDeIvaLiquidadoPorTrimestre()
    {
        var fila = Plazos(new DateTime(2026, 11, 15), new LiquidacionIva(2026, new DateTime(2026, 4, 18))).Periodos.Single();
        Assert.Equal("IVA liquidado", fila.Nombre);
        Assert.True(fila.Trimestral);
        Assert.Equal([EstadoPeriodo.Hecho, EstadoPeriodo.ConErrores, EstadoPeriodo.ConErrores, EstadoPeriodo.EnCurso], fila.Celdas.Select(c => c.Estado));
        var octubre = Plazos(new DateTime(2026, 10, 10), new LiquidacionIva(2026, new DateTime(2026, 4, 18))).Periodos.Single();
        Assert.Equal(EstadoPeriodo.Incompleto, octubre.Celdas[2].Estado);   // 3T: aún en plazo
    }

    // ------------------------------------------------------------ avisos

    private static BancoSalud Banco(string cuenta, DateTime? extracto, decimal? saldoBanco, decimal? contable, bool cuadrado = true, bool enUso = true, DateTime? movimiento = null) =>
        new($"Banco {cuenta}", cuenta, true, extracto, saldoBanco, contable, enUso ? 100m : 0m, enUso, cuadrado, movimiento ?? extracto);

    [Fact]
    public void UnBancoQueNoCuadraEsGrave()
    {
        var h = RevisionBancosCuadre.Evaluar([Banco("5720001", new DateTime(2026, 10, 31), 1000m, 1012.40m), Banco("5720002", new DateTime(2026, 10, 31), 50m, 50m)]);
        var e = h.Elementos.Single();
        Assert.Equal(GravedadSalud.Grave, e.Gravedad);
        Assert.Contains("12,40 € mayor", e.Explicacion);
        Assert.Equal(PantallaSalud.LineasN43, e.Destino.Pantalla);
        // Cuadran: comprobación correcta. Un extracto descuadrado por dentro (remesas) o un banco sin uso no se comparan.
        Assert.Single(RevisionBancosCuadre.Evaluar([Banco("5720002", new DateTime(2026, 10, 31), 50m, 50m)]).Correctas);
        Assert.Empty(RevisionBancosCuadre.Evaluar([Banco("5550025", new DateTime(2026, 9, 4), 1036700m, -11341m, cuadrado: false)]).Elementos);
        Assert.Empty(RevisionBancosCuadre.Evaluar([Banco("5720003", new DateTime(2023, 12, 31), 10m, 0m, enUso: false)]).Elementos);
    }

    [Fact]
    public void MovimientosSinAsientoPorBancoYFilaDeBancos()
    {
        var meses = new List<MesBanco>
        {
            new("Santander", null, 2026, 7, 100, 29, new DateTime(2026, 7, 12)),
            new("BBVA", null, 2026, 8, 50, 8, new DateTime(2026, 8, 2)),
            new("BBVA", null, 2026, 9, 40, 0, null),
            new("BBVA", null, 2026, 11, 10, 0, null),
        };
        var h = RevisionBancosSinAsiento.Evaluar(meses, 2026, Hoy);
        var e = h.Elementos.Single();
        Assert.Equal("37 movimientos bancarios sin asiento", e.Titulo);
        Assert.Equal("Santander 29, BBVA 8. El más antiguo es del 12/07/2026.", e.Explicacion);
        var fila = h.Periodos.Single();
        Assert.Equal(12, fila.Celdas.Count);
        Assert.Equal(EstadoPeriodo.NoToca, fila.Celdas[0].Estado);       // enero sin movimientos
        Assert.Equal(EstadoPeriodo.Incompleto, fila.Celdas[6].Estado);
        Assert.Equal(EstadoPeriodo.Hecho, fila.Celdas[8].Estado);
        Assert.Equal(EstadoPeriodo.Hecho, fila.Celdas[10].Estado);       // noviembre en curso y todo con asiento
        Assert.Equal(EstadoPeriodo.NoToca, fila.Celdas[11].Estado);
        Assert.Empty(RevisionBancosSinAsiento.Evaluar([new("BBVA", null, 2026, 9, 40, 0, null)], 2026, Hoy).Elementos);
    }

    [Fact]
    public void ExtractoAntiguoMasDe40DiasSoloConMovimientoRecienteYSinLasDeInversion()
    {
        var bancos = new List<BancoSalud>
        {
            Banco("5720001", new DateTime(2026, 10, 5), 1, 1),                                          // 41 días: avisa
            Banco("5720002", new DateTime(2026, 10, 6), 1, 1),                                          // 40 días: aún no
            Banco("5720019", new DateTime(2026, 1, 1), 1, 1),                                           // inversión
            Banco("5720003", new DateTime(2023, 12, 31), 1, 1),                                         // parada desde 2023: no avisa
            Banco("5720004", new DateTime(2023, 12, 31), 1, 1, movimiento: new DateTime(2026, 9, 30)),  // sin extracto desde 2023 pero con apuntes: avisa
            Banco("5720005", new DateTime(2025, 11, 10), 1, 1, movimiento: new DateTime(2025, 11, 14)), // último movimiento hace más de 12 meses
            Banco("5720006", new DateTime(2025, 11, 15), 1, 1),                                         // justo 12 meses: avisa
        };
        var e = RevisionExtractoAntiguo.Evaluar(bancos, new HashSet<string> { "5720019" }, Hoy).Elementos;
        Assert.Equal(["banco-extracto-5720001", "banco-extracto-5720004", "banco-extracto-5720006"], e.Select(x => x.Clave));
        Assert.Equal("Último extracto de Banco 5720001 de hace 41 días", e[0].Titulo);
        Assert.Equal(PantallaSalud.Bancos, e[0].Destino.Pantalla);
        // Sin ningún movimiento conocido tampoco avisa.
        Assert.Empty(RevisionExtractoAntiguo.Evaluar([Banco("5720001", new DateTime(2026, 1, 1), 1, 1) with { UltimoMovimiento = null }], new HashSet<string>(), Hoy).Elementos);
    }

    [Fact]
    public void ElCuadreDeLibrosDeIvaDejaFueraElAutorrepercutidoDeLasFacturasRecibidas()
    {
        var feb = new DateTime(2026, 2, 10);
        var t = CuadreLibrosIva.Trimestres(
            [(feb, 210m)],
            [(feb, 21m), (feb, 30.37m)],   // la segunda es una factura recibida con autorrepercutido (ISP o comisión de plataforma)
            [
                new ApunteLibroIva(feb, "4770021", 0m, 210m, false),
                new ApunteLibroIva(feb, "4720021", 21m, 0m, true),
                new ApunteLibroIva(feb, "4720021", 30.37m, 0m, true),
                new ApunteLibroIva(feb, "4771021", 0m, 30.37m, true),        // autorrepercutido: fuera del cuadre
                new ApunteLibroIva(new DateTime(2026, 5, 4), "4770021", 0m, 15m, false),   // 2T: 477 a mano sin libro
            ]);
        Assert.Equal(new IvaLibrosTrimestre(1, 210m, 210m, 51.37m, 51.37m, 3), t[0]);
        Assert.Equal(new IvaLibrosTrimestre(2, 0m, 15m, 0m, 0m, 0), t[1]);
        Assert.Single(RevisionLibrosIva.Evaluar(t, 2026).Correctas);   // el 2T no tiene libros: no se compara
    }

    [Fact]
    public void ComprasBorradoresClientesYPartidas()
    {
        Assert.Empty(RevisionComprasPendientes.Evaluar(new ComprasPendientes(0, 0, null)).Elementos);
        var compras = RevisionComprasPendientes.Evaluar(new ComprasPendientes(6, 1234.5m, new DateTime(2026, 2, 5))).Elementos.Single();
        Assert.Equal("6 facturas de compra sin contabilizar", compras.Titulo);
        Assert.Contains("1.234,50 €", compras.Explicacion);

        var borradores = RevisionBorradoresVenta.Evaluar([new DateTime(2026, 11, 2), new DateTime(2026, 11, 5), new DateTime(2026, 11, 8)], Hoy).Elementos.Single();
        Assert.Equal("2 facturas de venta en borrador desde hace más de 7 días", borradores.Titulo);   // el del 8/11 tiene 7 días: no
        Assert.Equal("Borradores sin número del 02/11 y el 05/11.", borradores.Explicacion);

        var clientes = RevisionClientesAcreedores.Evaluar(new Dictionary<string, decimal> { ["4300001"] = -100m, ["4300002"] = 50m, ["4300003"] = -15.8m }).Elementos.Single();
        Assert.Equal("2 clientes con saldo acreedor", clientes.Titulo);
        Assert.Contains("115,80 €", clientes.Explicacion);
        Assert.Equal(["4300001", "4300003"], clientes.Destino.Cuentas);

        // La 555 que Norma 43 usa como banco no cuenta.
        var partidas = RevisionPartidasPendientes.Evaluar(new Dictionary<string, decimal> { ["5550015"] = 53229.95m, ["5550025"] = -974m, ["5550001"] = 0m },
            new HashSet<string> { "5550025" }).Elementos.Single();
        Assert.Equal("1 partida pendiente de aplicación con saldo", partidas.Titulo);
        Assert.StartsWith("5550015 53.229,95 € deudor", partidas.Explicacion);
    }

    // ------------------------------------------------------------ comprobaciones

    [Fact]
    public void ElDiarioCuadraONo()
    {
        Assert.Equal("El Diario cuadra: debe = haber en los 1.606 asientos de 2026", RevisionDiarioCuadra.Evaluar(1606, [], 2026).Correctas.Single().Texto);
        var e = RevisionDiarioCuadra.Evaluar(10, [new AsientoDescuadrado(7, new DateTime(2026, 3, 1), "Venta", 121m, 120m)], 2026).Elementos.Single();
        Assert.Equal(GravedadSalud.Grave, e.Gravedad);
        Assert.Contains("debe 121,00 €, haber 120,00 €", e.Explicacion);
        Assert.Equal(PantallaSalud.Diario, e.Destino.Pantalla);
    }

    [Fact]
    public void NumeracionSinHuecosPorSerie()
    {
        var ok = RevisionNumeracionFacturas.Evaluar([new(1, "S2026", "S20260001"), new(1, "S2026", "S20260002"), new(2, "R2026", "R2026-0001")]);
        Assert.Equal("Numeración de las facturas validadas sin huecos en 2 series", ok.Correctas.Single().Texto);
        var e = RevisionNumeracionFacturas.Evaluar([new(1, "S2026", "S20260001"), new(1, "S2026", "S20260002"), new(1, "S2026", "S20260004")]).Elementos.Single();
        Assert.Equal("Huecos en la numeración de la serie S2026", e.Titulo);
        Assert.Equal("Falta el número 3 (de 1 a 4).", e.Explicacion);
        Assert.Equal(GravedadSalud.Grave, e.Gravedad);
    }

    [Fact]
    public void LibrosDeIvaFrenteALasCuentas()
    {
        Assert.Empty(RevisionLibrosIva.Evaluar([new(1, 0, 100, 0, 50, 0)], 2026).Elementos);   // sin libros no se comprueba
        Assert.Single(RevisionLibrosIva.Evaluar([new(1, 100, 100, 50, 50, 4)], 2026).Correctas);
        var e = RevisionLibrosIva.Evaluar([new(1, 100, 100, 50, 50, 4), new(2, 100, 120, 50, 50, 3)], 2026).Elementos.Single();
        Assert.Equal("Los libros de IVA no coinciden con las cuentas en el 2T", e.Titulo);
        Assert.Equal("2T repercutido: libro 100,00 €, 477 120,00 €.", e.Explicacion);
    }

    [Fact]
    public void TercerosSinNifYCuentasDeBaja()
    {
        Assert.Single(RevisionTercerosNif.Evaluar([], 2026).Correctas);
        var t = RevisionTercerosNif.Evaluar([new(1, "Cliente uno", "4300001", true), new(1, "Cliente uno", "4300002", true), new(2, "Proveedor", "4000001", false)], 2026).Elementos.Single();
        Assert.Equal("2 clientes y proveedores con movimientos sin NIF", t.Titulo);
        Assert.StartsWith("1 cliente y 1 proveedor:", t.Explicacion);

        Assert.Single(RevisionCuentasBaja.Evaluar([], 2026).Correctas);
        var c = RevisionCuentasBaja.Evaluar([new("6290099", 3, false), new("5720005", 1, true)], 2026).Elementos.Single();
        Assert.Contains("no existen en el plan de 2026: 6290099", c.Explicacion);
        Assert.Contains("dadas de baja: 5720005", c.Explicacion);
        Assert.Equal(["6290099", "5720005"], c.Destino.Cuentas);
    }

    [Fact]
    public void NominasHastaElMesAnterior()
    {
        Assert.Equal(HallazgosSalud.Nada, RevisionNominas.Evaluar([], 2026, Hoy));   // la sociedad no usa nóminas
        var nueve = Enumerable.Range(1, 9).Select(m => new NominaMes(2026, m, RevisionNominas.Contabilizada, "MENSUAL")).ToList();
        var h = RevisionNominas.Evaluar([.. nueve, new NominaMes(2026, 6, "PENDIENTE", "EXTRA")], 2026, Hoy);
        Assert.Equal("Nóminas sin contabilizar: octubre", h.Elementos.Single().Titulo);
        Assert.Contains("falta octubre", h.Elementos.Single().Explicacion);
        var fila = h.Periodos.Single();
        Assert.Equal(EstadoPeriodo.Hecho, fila.Celdas[8].Estado);
        Assert.Equal(EstadoPeriodo.ConErrores, fila.Celdas[9].Estado);
        Assert.Equal(EstadoPeriodo.EnCurso, fila.Celdas[10].Estado);
        var completas = RevisionNominas.Evaluar([.. nueve, new NominaMes(2026, 10, RevisionNominas.Contabilizada, null)], 2026, Hoy);
        Assert.Equal("Nóminas contabilizadas hasta octubre", completas.Correctas.Single().Texto);
        var pendiente = RevisionNominas.Evaluar([.. nueve, new NominaMes(2026, 10, "INCIDENCIAS", "MENSUAL")], 2026, Hoy);
        Assert.Equal(EstadoPeriodo.Incompleto, pendiente.Periodos.Single().Celdas[9].Estado);
    }
}

/// <summary>Orden, «Lo más urgente» y el servicio que ejecuta las revisiones.</summary>
public sealed class ServicioSaludTests
{
    private static ElementoSalud E(string clave, TipoSalud tipo, GravedadSalud g, DateTime? plazo = null) =>
        new(clave, tipo, g, clave, "", plazo, DestinoSalud.SinPantalla("x"), "nodo", "", "");

    [Fact]
    public void OrdenYLoMasUrgente()
    {
        var elementos = new[]
        {
            E("aviso-grave", TipoSalud.Aviso, GravedadSalud.Grave),
            E("tarea-info", TipoSalud.Tarea, GravedadSalud.Aviso, new DateTime(2026, 12, 31)),
            E("tarea-grave-tarde", TipoSalud.Tarea, GravedadSalud.Grave, new DateTime(2026, 11, 30)),
            E("tarea-grave-vencida", TipoSalud.Tarea, GravedadSalud.Grave, new DateTime(2026, 4, 20)),
            E("aviso-atencion", TipoSalud.Aviso, GravedadSalud.Atencion),
        };
        Assert.Equal(["tarea-grave-vencida", "tarea-grave-tarde", "tarea-info"], OrdenSalud.Tareas(elementos).Select(e => e.Clave));
        Assert.Equal(["aviso-grave", "aviso-atencion"], OrdenSalud.Avisos(elementos).Select(e => e.Clave));
        // Primero los graves con plazo, por fecha; un grave sin plazo va después.
        Assert.Equal("tarea-grave-vencida", OrdenSalud.LoMasUrgente(elementos)!.Clave);
        Assert.Equal("aviso-grave", OrdenSalud.LoMasUrgente([elementos[0], elementos[1]])!.Clave);
        Assert.Null(OrdenSalud.LoMasUrgente([elementos[1], elementos[4]]));
        Assert.Equal((3, 1, 1), OrdenSalud.Recuento(elementos));
    }

    [Fact]
    public async Task UnaRevisionQueFallaNoTapaALasDemas()
    {
        var fuente = new FuenteSaludFija
        {
            FallaNominas = true,
            Compras = new ComprasPendientes(2, 10m, null),
            Bancos = [new BancoSalud("Banco", "5720001", true, new DateTime(2026, 11, 14), 10m, 10m, 10m, true)],
        };
        var avisadas = new List<string>();
        var informe = await new ServicioSalud(fuente, hoy: () => new DateTime(2026, 11, 15)).RevisarAsync("T01", 2026, null, r => { lock (avisadas) avisadas.Add(r.Clave); });
        Assert.Equal(ServicioSalud.Todas.Count, informe.Revisiones.Count);
        Assert.Equal(ServicioSalud.Todas.Count, avisadas.Count);
        var nominas = informe.Revisiones.Single(r => r.Clave == "nominas");
        Assert.Contains("bloqueada", nominas.Error);
        Assert.Contains(informe.Elementos, e => e.Clave == "error-nominas" && e.Titulo == "No he podido comprobar «Nóminas contabilizadas»");
        Assert.Contains(informe.Elementos, e => e.Clave == "compras-pendientes");
        // Los bancos se leen una sola vez para las tres revisiones que los usan.
        Assert.Equal(1, fuente.LecturasBancos);
    }

    [Fact]
    public async Task TodoAlDia()
    {
        var fuente = new FuenteSaludFija { Ejercicios = [new(9002, 2026), new(9003, 2027)], Liquidaciones = [new LiquidacionIva(2026, new DateTime(2026, 3, 31))] };
        var informe = await new ServicioSalud(fuente, hoy: () => new DateTime(2026, 6, 1)).RevisarAsync("T01", 2026);
        // En junio de 2026, sin nada pendiente: solo el 303 del 2T (plazo el 20/7, dentro de los 90 días).
        Assert.Equal(["303-2026-2"], informe.Elementos.Select(e => e.Clave));
        Assert.Contains(informe.Correctas, c => c.Texto.StartsWith("El Diario cuadra"));
    }
}

/// <summary>El panel de salud de Inicio (modelo de vista, sin ventanas).</summary>
public sealed class SaludVMTests
{
    private sealed class Dialogos : IDialogosInicio
    {
        public PorQueSaludVM? PorQueVisto { get; private set; }
        public string? Copiado { get; private set; }
        public EleccionInicio? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos, IReadOnlyList<CuentaBancoInicio> bancos) =>
            new(new HashSet<string> { "salud" }, new HashSet<string>());
        public void VerRatio(FichaRatioVM ficha) { }
        public void PorQue(PorQueSaludVM porQue) => PorQueVisto = porQue;
        public void Copiar(string texto) => Copiado = texto;
    }

    private static (SaludVM Vm, Dialogos D, List<string> Abiertas) Crear(FuenteSaludFija fuente)
    {
        var abiertas = new List<string>();
        var acciones = new AccionesInicio(AbrirMayor: c => abiertas.Add("mayor " + string.Join(",", c))) { AbrirVentas = () => abiertas.Add("ventas"), AbrirFacturasCompra = () => abiertas.Add("compras") };
        var d = new Dialogos();
        var vm = new SaludVM(new ServicioSalud(fuente, hoy: () => new DateTime(2026, 11, 15)), new ContextoInicio("T01", 2026, "Sociedad", _ => { }), acciones, d,
            () => new HashSet<string>());
        return (vm, d, abiertas);
    }

    private static FuenteSaludFija ConCosas() => new()
    {
        Ejercicios = [new(9002, 2026)],
        Ventas = new VentasPendientes(3, 900m, 0, 0),
        Compras = new ComprasPendientes(2, 50m, new DateTime(2026, 10, 1)),
        Saldos = new Dictionary<string, decimal> { ["4300001"] = -20m },
        Liquidaciones = [new LiquidacionIva(2026, new DateTime(2026, 3, 31)), new LiquidacionIva(2026, new DateTime(2026, 6, 30)), new LiquidacionIva(2026, new DateTime(2026, 10, 20))],
    };

    [Fact]
    public async Task ResumenUrgenteYSecciones()
    {
        var (vm, _, abiertas) = Crear(ConCosas());
        await vm.RevisarAsync();
        Assert.False(vm.Cargando);
        Assert.Equal("Facturar las ventas pendientes antes del 1/12", vm.Urgente!.Titulo);
        Assert.Equal("en 15 días", vm.Urgente.Plazo);
        Assert.Equal("Abrir Ventas por facturar", vm.TextoBotonUrgente);
        vm.AbrirUrgenteCommand.Execute(null);
        Assert.Equal(["ventas"], abiertas);
        Assert.Equal("Hay cosas urgentes", vm.TituloResumen);
        Assert.Equal("3", vm.Numero);   // 1 grave + 2 atención; los avisos (ejercicio 2027, 4T, 390) no cuentan
        Assert.Equal("i 3 avisos", vm.RecuentoAvisos);
        Assert.Contains(vm.Elementos, e => e.Seccion == SeccionSalud.Hechas && e.Titulo == "Liquidación del IVA del 3T");
        Assert.Equal("IVA liquidado", vm.Periodos.Single().Nombre);
        Assert.Contains("Revisada", vm.TextoPie);

        vm.FiltroAvisos = true;
        Assert.All(vm.Elementos, e => Assert.Equal(SeccionSalud.Avisos, e.Seccion));
        vm.FiltroTareas = true;
        Assert.DoesNotContain(vm.Elementos, e => e.Seccion == SeccionSalud.Avisos);
    }

    [Fact]
    public async Task SeleccionMenuContextualYPorQue()
    {
        var (vm, d, abiertas) = Crear(ConCosas());
        await vm.RevisarAsync();
        var clientes = vm.Elementos.Single(e => e.Clave == "clientes-acreedores");
        var plazo303 = vm.Elementos.Single(e => e.Clave == "303-2026-4");
        Assert.False(plazo303.PuedeAbrir);   // la pantalla de liquidación de IVA aún no existe
        Assert.Contains("aún no existe", plazo303.Ayuda);

        vm.Seleccionar([clientes]);
        Assert.True(clientes.PuedeAbrir, clientes.Ayuda + " | " + clientes.Destino);
        Assert.Equal("Abrir Mayor", vm.TextoAbrirSeleccion);
        Assert.True(vm.AbrirCommand.CanExecute(null));
        vm.AbrirCommand.Execute(null);
        Assert.Equal(["mayor 4300001"], abiertas);
        vm.PorQueCommand.Execute(null);
        Assert.Equal("contabilidad/mayor", d.PorQueVisto!.Nodo);
        Assert.Contains("desaparece solo", d.PorQueVisto.ComoSeResuelve);

        // Con dos seleccionados: abrir y «¿Por qué aparece?» no; copiar sí, los dos textos.
        vm.Seleccionar([clientes, plazo303]);
        Assert.False(vm.AbrirCommand.CanExecute(null));
        Assert.False(vm.PorQueCommand.CanExecute(null));
        vm.CopiarCommand.Execute(null);
        Assert.Contains("Atención: 1 cliente con saldo acreedor", d.Copiado);
        Assert.Contains("Liquidar el IVA del 4T", d.Copiado);
        vm.Seleccionar([plazo303]);
        Assert.False(vm.AbrirCommand.CanExecute(null));
    }

    [Fact]
    public async Task SinNadaGraveNoHayUrgenteYTodoAlDia()
    {
        var (vm, _, _) = Crear(new FuenteSaludFija { Ejercicios = [new(9002, 2026), new(9003, 2027)],
            Liquidaciones = [new LiquidacionIva(2026, new DateTime(2026, 3, 31)), new LiquidacionIva(2026, new DateTime(2026, 6, 30)), new LiquidacionIva(2026, new DateTime(2026, 9, 30))] });
        await vm.RevisarAsync();
        Assert.Null(vm.Urgente);
        Assert.Equal("Nada urgente", vm.TituloResumen);   // quedan los avisos del 4T y del 390
        Assert.False(vm.TodoAlDia);
    }

    [Fact]
    public async Task OcultarLaSaludQuitaLoMasUrgente()
    {
        var d = new Dialogos();
        var vm = new InicioVM(new ServicioInicio(new FuenteSintetica(false), () => new DateTime(2026, 11, 15)), new ContextoInicio("T01", 2026, "Sociedad", _ => { }), d,
            new AccionesInicio() { AbrirVentas = () => { } }, new ProConta.UI.Base.Preferencias.PreferenciasEnMemoria(),
            new ServicioSalud(ConCosas(), hoy: () => new DateTime(2026, 11, 15)));
        await vm.IniciarAsync();
        Assert.True(vm.VerSalud);
        Assert.True(vm.VerUrgente);
        await vm.PersonalizarAsync();
        Assert.False(vm.VerSalud);
        Assert.False(vm.VerUrgente);
    }
}
