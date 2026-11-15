namespace ProConta.Inicio;

/// <summary>
/// Calcula la pantalla de inicio de una sociedad y ejercicio: lee el ejercicio de trabajo, el anterior (tesorería de los últimos
/// 12 meses) y el comparado, y los guarda en memoria para que cambiar «Comparar con» no vuelva a leer lo ya leído;
/// «Actualizar» lo vuelve a leer todo.
/// </summary>
public sealed class ServicioInicio(IFuenteInicio fuente, Func<DateTime>? hoy = null)
{
    private readonly Dictionary<int, DatosEjercicio> _cache = [];
    private IReadOnlyList<EjercicioSociedad>? _ejercicios;
    private IReadOnlyList<BancoN43>? _bancos;
    private IReadOnlyDictionary<int, ActividadInicio>? _porCentro;
    private IReadOnlyList<ActividadInicio>? _actividades;

    public DateTime Hoy => (hoy ?? (() => DateTime.Today))().Date;

    /// <summary>Olvida lo leído: la próxima consulta vuelve a la BD.</summary>
    public void Olvidar() => _olvidar = true;

    private volatile bool _olvidar;

    private void OlvidarAhora()
    {
        _olvidar = false;
        _cache.Clear();
        _ejercicios = null;
        _bancos = null;
        _porCentro = null;
        _actividades = null;
    }

    /// <summary>Ejercicios de la sociedad, del más reciente al más antiguo.</summary>
    public async Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        (_olvidar ? null : _ejercicios) ?? (_ejercicios = await fuente.EjerciciosAsync(idSociedad, cancelacion));

    /// <summary>Ejercicios con los que se puede comparar: los anteriores al de trabajo que existen para la sociedad.</summary>
    public async Task<IReadOnlyList<int>> ComparablesAsync(string idSociedad, int ejercicio, CancellationToken cancelacion = default) =>
        (await EjerciciosAsync(idSociedad, cancelacion)).Where(e => e.Ejercicio < ejercicio).Select(e => e.Ejercicio).OrderByDescending(e => e).ToList();

    private readonly SemaphoreSlim _turno = new(1, 1);

    /// <summary>Calcula el panel; las llamadas se atienden de una en una (comparten lo leído).</summary>
    public async Task<PanelInicio> CalcularAsync(string idSociedad, int ejercicio, int? comparado, CancellationToken cancelacion = default)
    {
        await _turno.WaitAsync(cancelacion).ConfigureAwait(false);
        try { return await CalcularSinTurnoAsync(idSociedad, ejercicio, comparado, cancelacion).ConfigureAwait(false); }
        finally { _turno.Release(); }
    }

    private async Task<PanelInicio> CalcularSinTurnoAsync(string idSociedad, int ejercicio, int? comparado, CancellationToken cancelacion)
    {
        if (_olvidar) OlvidarAhora();
        var ejercicios = await EjerciciosAsync(idSociedad, cancelacion);
        var actualE = ejercicios.FirstOrDefault(e => e.Ejercicio == ejercicio)
            ?? throw new InvalidOperationException($"La sociedad no tiene el ejercicio {ejercicio} en SociedadesEjercicios.");
        var actual = await DatosAsync(idSociedad, actualE, cancelacion);
        var anteriorE = ejercicios.FirstOrDefault(e => e.Ejercicio == ejercicio - 1);
        var anterior = anteriorE is null ? null : await DatosAsync(idSociedad, anteriorE, cancelacion);
        var comparadoE = comparado is int c ? ejercicios.FirstOrDefault(e => e.Ejercicio == c) : null;
        var datosComparado = comparadoE is null ? null : await DatosAsync(idSociedad, comparadoE, cancelacion);
        _bancos ??= await fuente.BancosAsync(idSociedad, actualE, cancelacion);
        _porCentro ??= await fuente.ActividadesPorCentroAsync(idSociedad, cancelacion);
        _actividades ??= await fuente.ActividadesAsync(idSociedad, cancelacion);
        return CalculadoraInicio.Calcular(actual, anterior, datosComparado, Hoy, _bancos, _porCentro, _actividades);
    }

    private async Task<DatosEjercicio> DatosAsync(string idSociedad, EjercicioSociedad e, CancellationToken cancelacion)
    {
        if (_cache.TryGetValue(e.IdSociedadEjercicio, out var d)) return d;
        d = await fuente.DatosAsync(idSociedad, e, cancelacion);
        _cache[e.IdSociedadEjercicio] = d;
        return d;
    }
}
