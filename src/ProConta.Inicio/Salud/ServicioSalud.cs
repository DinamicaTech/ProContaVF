using System.Collections.Concurrent;
using System.Diagnostics;

namespace ProConta.Inicio.Salud;

/// <summary>
/// Revisión de la salud de la contabilidad (pantalla-de-inicio, fase 3): ejecuta las revisiones a la vez (como mucho 4 lecturas
/// simultáneas contra Access), cada una por su lado, de modo que una lenta o con error no bloquea a las demás; avisa de cada una
/// en cuanto termina. Solo lee. La fase 4 añade revisiones a <see cref="Todas"/>.
/// </summary>
public sealed class ServicioSalud(IFuenteSalud fuente, IReadOnlyList<IRevisionSalud>? revisiones = null, Func<DateTime>? hoy = null)
{
    /// <summary>Las revisiones de la fase 3, en el orden en que se presentan.</summary>
    public static IReadOnlyList<IRevisionSalud> Todas { get; } =
    [
        new RevisionVentasAntesCambioIva(), new RevisionEjercicioSiguiente(), new RevisionPlazosIva(),
        new RevisionBancosCuadre(), new RevisionBancosSinAsiento(), new RevisionExtractoAntiguo(), new RevisionComprasPendientes(), new RevisionBorradoresVenta(),
        new RevisionClientesAcreedores(), new RevisionPartidasPendientes(),
        new RevisionDiarioCuadra(), new RevisionNumeracionFacturas(), new RevisionLibrosIva(), new RevisionTercerosNif(), new RevisionCuentasBaja(), new RevisionNominas(),
    ];

    public const int LecturasSimultaneas = 4;

    public IReadOnlyList<IRevisionSalud> Revisiones { get; } = revisiones ?? Todas;

    public DateTime Hoy => (hoy ?? (() => DateTime.Today))().Date;

    /// <summary>Revisa la salud del ejercicio de trabajo.</summary>
    /// <param name="alTerminar">Se llama con cada revisión en cuanto termina (desde el hilo de la revisión).</param>
    public async Task<InformeSalud> RevisarAsync(string idSociedad, int ejercicio, IReadOnlySet<string>? inversiones = null, Action<ResultadoRevision>? alTerminar = null,
        CancellationToken cancelacion = default)
    {
        var ejercicios = await fuente.EjerciciosAsync(idSociedad, cancelacion).ConfigureAwait(false);
        var actual = ejercicios.FirstOrDefault(e => e.Ejercicio == ejercicio)
            ?? throw new InvalidOperationException($"La sociedad no tiene el ejercicio {ejercicio} en SociedadesEjercicios.");
        var ctx = new ContextoSalud(idSociedad, actual, ejercicios, Hoy, inversiones ?? new HashSet<string>(StringComparer.Ordinal));
        return await RevisarAsync(ctx, alTerminar, cancelacion).ConfigureAwait(false);
    }

    public async Task<InformeSalud> RevisarAsync(ContextoSalud ctx, Action<ResultadoRevision>? alTerminar = null, CancellationToken cancelacion = default)
    {
        var compartida = new FuenteSaludCompartida(fuente);
        using var turno = new SemaphoreSlim(LecturasSimultaneas);
        var tareas = Revisiones.Select(r => Task.Run(async () =>
        {
            await turno.WaitAsync(cancelacion).ConfigureAwait(false);
            var reloj = Stopwatch.StartNew();
            ResultadoRevision resultado;
            try
            {
                var h = await r.RevisarAsync(compartida, ctx, cancelacion).ConfigureAwait(false);
                resultado = new ResultadoRevision(r.Clave, r.Nombre, r.Nodo, h, reloj.Elapsed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                resultado = new ResultadoRevision(r.Clave, r.Nombre, r.Nodo, HallazgosSalud.De(ElementoError(r, ex)), reloj.Elapsed, ex.Message);
            }
            finally
            {
                turno.Release();
            }
            alTerminar?.Invoke(resultado);
            return resultado;
        }, cancelacion)).ToList();
        var resultados = await Task.WhenAll(tareas).ConfigureAwait(false);
        return new InformeSalud(DateTime.Now, resultados);
    }

    /// <summary>Una revisión que falla sale como aviso con la causa, sin tapar a las demás.</summary>
    internal static ElementoSalud ElementoError(IRevisionSalud r, Exception ex) =>
        new($"error-{r.Clave}", TipoSalud.Aviso, GravedadSalud.Aviso, $"No he podido comprobar «{r.Nombre}»",
            $"La lectura ha fallado: {ex.Message}", null, DestinoSalud.SinPantalla("ninguna pantalla"), r.Nodo,
            $"La revisión «{r.Nombre}» ({r.Nodo}) ha dado un error al leer la base de datos; las demás revisiones no se ven afectadas.",
            "Pulsa «Actualizar» para repetir la revisión. Si sigue fallando, copia este texto (clic derecho › Copiar el texto) y envíaselo a soporte.");
}

/// <summary>Comparte entre las revisiones de una misma pasada las lecturas que usan varias (los bancos).</summary>
internal sealed class FuenteSaludCompartida(IFuenteSalud f) : IFuenteSalud
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<BancoSalud>>>> _bancos = new();

    public Task<IReadOnlyList<BancoSalud>> BancosAsync(string idSociedad, ContextoSalud contexto, CancellationToken c = default) =>
        _bancos.GetOrAdd(idSociedad, _ => new Lazy<Task<IReadOnlyList<BancoSalud>>>(() => f.BancosAsync(idSociedad, contexto, c))).Value;

    public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken c = default) => f.EjerciciosAsync(idSociedad, c);
    public Task<VentasPendientes> VentasPendientesAsync(string idSociedad, DateTime antesDe, CancellationToken c = default) => f.VentasPendientesAsync(idSociedad, antesDe, c);
    public Task<IReadOnlyList<ApunteIva>> IvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => f.IvaAsync(ejercicio, c);
    public Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken c = default) => f.ActividadesAsync(idSociedad, c);
    public Task<IReadOnlyList<LiquidacionIva>> LiquidacionesIvaAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) => f.LiquidacionesIvaAsync(ejercicios, c);
    public Task<IReadOnlyList<MesBanco>> LineasBancoAsync(string idSociedad, CancellationToken c = default) => f.LineasBancoAsync(idSociedad, c);
    public Task<ComprasPendientes> ComprasPendientesAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) => f.ComprasPendientesAsync(idSociedad, ejercicio, c);
    public Task<IReadOnlyList<DateTime>> BorradoresVentaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => f.BorradoresVentaAsync(ejercicio, c);
    public Task<IReadOnlyDictionary<string, decimal>> SaldosAsync(EjercicioSociedad ejercicio, string prefijoDesde, string prefijoHasta, DateTime hasta, CancellationToken c = default) =>
        f.SaldosAsync(ejercicio, prefijoDesde, prefijoHasta, hasta, c);
    public Task<(int Asientos, IReadOnlyList<AsientoDescuadrado> Descuadrados)> CuadreDiarioAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => f.CuadreDiarioAsync(ejercicio, c);
    public Task<IReadOnlyList<NumeroFactura>> NumerosFacturasAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => f.NumerosFacturasAsync(ejercicio, c);
    public Task<IReadOnlyList<IvaLibrosTrimestre>> LibrosIvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => f.LibrosIvaAsync(ejercicio, c);
    public Task<IReadOnlyList<TerceroSinNif>> TercerosSinNifAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) => f.TercerosSinNifAsync(idSociedad, ejercicio, c);
    public Task<IReadOnlyList<CuentaConProblema>> CuentasConProblemaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => f.CuentasConProblemaAsync(ejercicio, c);
    public Task<IReadOnlyList<NominaMes>> NominasAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) => f.NominasAsync(ejercicios, c);
}

/// <summary>Orden de la salud y elección de «Lo más urgente».</summary>
public static class OrdenSalud
{
    /// <summary>Tareas con plazo: por fecha (las vencidas primero) y, a igual fecha, por gravedad.</summary>
    public static IReadOnlyList<ElementoSalud> Tareas(IEnumerable<ElementoSalud> elementos) =>
        elementos.Where(e => e.Tipo == TipoSalud.Tarea).OrderBy(e => e.Plazo ?? DateTime.MaxValue).ThenBy(e => e.Gravedad).ToList();

    /// <summary>Avisos: por gravedad y, a igual gravedad, en el orden de las revisiones.</summary>
    public static IReadOnlyList<ElementoSalud> Avisos(IEnumerable<ElementoSalud> elementos) =>
        elementos.Where(e => e.Tipo == TipoSalud.Aviso).Select((e, i) => (e, i)).OrderBy(x => x.e.Gravedad).ThenBy(x => x.e.Plazo ?? DateTime.MaxValue).ThenBy(x => x.i).Select(x => x.e).ToList();

    /// <summary>Lo más urgente: el elemento grave más prioritario; primero los que tienen plazo, por fecha. Null si no hay nada grave.</summary>
    public static ElementoSalud? LoMasUrgente(IEnumerable<ElementoSalud> elementos) =>
        elementos.Where(e => e.Gravedad == GravedadSalud.Grave).Select((e, i) => (e, i))
            .OrderBy(x => x.e.Plazo is null ? 1 : 0).ThenBy(x => x.e.Plazo ?? DateTime.MaxValue).ThenBy(x => x.e.Tipo).ThenBy(x => x.i).Select(x => x.e).FirstOrDefault();

    /// <summary>Recuento por gravedad. El resumen suma graves y atención; los avisos informativos no cuentan.</summary>
    public static (int Graves, int Atencion, int Avisos) Recuento(IEnumerable<ElementoSalud> elementos)
    {
        var l = elementos.ToList();
        return (l.Count(e => e.Gravedad == GravedadSalud.Grave), l.Count(e => e.Gravedad == GravedadSalud.Atencion), l.Count(e => e.Gravedad == GravedadSalud.Aviso));
    }
}
