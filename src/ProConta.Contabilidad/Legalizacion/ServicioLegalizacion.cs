using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.CuentasAnuales;
using ProConta.Contabilidad.Diario;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Legalizacion;

/// <summary>
/// Reúne de la BD lo que va en los libros que se legalizan (contabilidad/procesos/legalizacion-libros) sin recalcular nada por su cuenta:
/// el Diario lo lee como la pantalla Diario (<see cref="FuenteDiarioAccess"/>), los balances de sumas y saldos los calcula
/// <see cref="GeneradorSumasYSaldos"/> y las cuentas anuales con su memoria, <see cref="ServicioCuentasAnuales"/>. Los activos del inventario
/// de cierre los da quien lo compone (el módulo de activos, que depende de contabilidad). Solo lee.
/// </summary>
/// <param name="activos">Los activos de la sociedad con lo amortizado hasta el cierre del ejercicio; null, el inventario va sin el detalle de activos.</param>
public sealed class ServicioLegalizacion(IBaseDatos bd, Func<ContextoSociedadEjercicio, Task<IReadOnlyList<ActivoInventario>>>? activos = null)
{
    private readonly ServicioCuentasAnuales _cuentas = new(bd);

    /// <summary>Los ejercicios de la sociedad, del más reciente al más antiguo, con su marca de cerrado.</summary>
    public Task<IReadOnlyList<EjercicioCuentas>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        _cuentas.EjerciciosAsync(idSociedad, cancelacion);

    /// <summary>
    /// El ejercicio que se propone: el último cerrado que no esté fuera de plazo (lo normal, el anterior al de trabajo); si no hay ninguno,
    /// el anterior al de trabajo o el de trabajo.
    /// </summary>
    public static int EjercicioPropuesto(IReadOnlyList<EjercicioCuentas> ejercicios, int trabajo, DateTime hoy) =>
        ejercicios.Where(e => e.Cerrado && LegalizacionLibros.Plazo(e.Ejercicio) >= hoy.Date).Select(e => (int?)e.Ejercicio).Max()
        ?? ServicioCuentasAnuales.EjercicioPropuesto(ejercicios, trabajo);

    public async Task<DatosLegalizacion> ReunirAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default)
    {
        var fuente = new FuenteDiarioAccess(bd);
        var ejercicio = (await fuente.EjerciciosAsync(ctx.IdSociedad, cancelacion)).FirstOrDefault(e => e.IdSociedadEjercicio == ctx.IdSociedadEjercicio)
            ?? throw new InvalidOperationException($"No encuentro el ejercicio {ctx.Ejercicio} de la sociedad: vuelve a abrir la pantalla.");
        var diario = await fuente.CargarAsync(ctx.IdSociedad, [ejercicio], cancelacion);
        var cuentas = await _cuentas.GenerarAsync(ctx, cancelacion);
        var memoria = await _cuentas.MemoriaAsync(ctx.IdSociedad, ctx.IdSociedadEjercicio, cuentas, cancelacion);
        var libro = LegalizacionLibros.Diario(cuentas.RazonSocial, cuentas.Nif, ctx.Ejercicio, diario.Asientos);
        var sumas = new GeneradorSumasYSaldos(bd);
        var trimestres = new List<ResultadoSumasYSaldos>();
        foreach (var fin in LegalizacionLibros.FinesDeTrimestre(ctx.Ejercicio))
            trimestres.Add(await sumas.GenerarAsync(ctx, cuentas.RazonSocial, LegalizacionLibros.ParametrosTrimestre(ctx.Ejercicio, fin), cancelacion));
        var deActivos = activos is null ? [] : await activos(ctx);
        return new DatosLegalizacion(ctx.IdSociedadEjercicio, ejercicio.Cerrado, libro, LegalizacionLibros.BalanceInicial(libro), trimestres,
            LegalizacionLibros.InventarioCierre(trimestres[^1]), deActivos, cuentas, memoria, DateTime.Now);
    }
}
