using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Ratios;

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
    private ModeloBalance? _situacion;
    private bool _situacionLeida;
    private readonly Dictionary<int, IReadOnlyList<MovimientoRatio>> _cierres = [];
    private readonly HashSet<int> _cierresLeidos = [];

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
        _situacion = null;
        _situacionLeida = false;
        _cierres.Clear();
        _cierresLeidos.Clear();
    }

    /// <summary>Ejercicios de la sociedad, del más reciente al más antiguo.</summary>
    public async Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        (_olvidar ? null : _ejercicios) ?? (_ejercicios = await fuente.EjerciciosAsync(idSociedad, cancelacion));

    /// <summary>Ejercicios con los que se puede comparar: los anteriores al de trabajo que existen para la sociedad.</summary>
    public async Task<IReadOnlyList<int>> ComparablesAsync(string idSociedad, int ejercicio, CancellationToken cancelacion = default) =>
        (await EjerciciosAsync(idSociedad, cancelacion)).Where(e => e.Ejercicio < ejercicio).Select(e => e.Ejercicio).OrderByDescending(e => e).ToList();

    private readonly SemaphoreSlim _turno = new(1, 1);

    /// <summary>Cuentas bancarias de la sociedad (Norma 43) con su cuenta contable, para marcar las de inversión.</summary>
    public async Task<IReadOnlyList<BancoN43>> BancosAsync(string idSociedad, int ejercicio, CancellationToken cancelacion = default)
    {
        if (_bancos is not null && !_olvidar) return _bancos;
        var e = (await EjerciciosAsync(idSociedad, cancelacion)).FirstOrDefault(x => x.Ejercicio == ejercicio);
        return e is null ? [] : _bancos = await fuente.BancosAsync(idSociedad, e, cancelacion);
    }

    /// <summary>Calcula el panel; las llamadas se atienden de una en una (comparten lo leído).</summary>
    /// <param name="inversiones">Cuentas contables de los bancos marcados como inversión (no son tesorería operativa).</param>
    public async Task<PanelInicio> CalcularAsync(string idSociedad, int ejercicio, int? comparado, IReadOnlySet<string>? inversiones = null, CancellationToken cancelacion = default)
    {
        await _turno.WaitAsync(cancelacion).ConfigureAwait(false);
        try { return await CalcularSinTurnoAsync(idSociedad, ejercicio, comparado, inversiones, cancelacion).ConfigureAwait(false); }
        finally { _turno.Release(); }
    }

    private async Task<PanelInicio> CalcularSinTurnoAsync(string idSociedad, int ejercicio, int? comparado, IReadOnlySet<string>? inversiones, CancellationToken cancelacion)
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
        if (!_situacionLeida)
        {
            _situacion = await fuente.ModeloSituacionAsync(cancelacion);
            _situacionLeida = true;
        }
        // Saldos a cierre de los ejercicios anteriores (series de los ratios): solo se leen los que aún no se han leído.
        List<EjercicioSociedad> faltan = _situacion is null ? [] : ejercicios.Where(e => e.Ejercicio < ejercicio && !_cierresLeidos.Contains(e.Ejercicio)).ToList();
        if (faltan.Count > 0)
        {
            foreach (var (anio, movs) in await fuente.CierresAsync(idSociedad, faltan, cancelacion)) _cierres[anio] = movs;
            foreach (var e in faltan) _cierresLeidos.Add(e.Ejercicio);
        }
        return CalculadoraInicio.Calcular(actual, anterior, datosComparado, Hoy, _bancos, _porCentro, _actividades, inversiones, _situacion, _cierres);
    }

    private async Task<DatosEjercicio> DatosAsync(string idSociedad, EjercicioSociedad e, CancellationToken cancelacion)
    {
        if (_cache.TryGetValue(e.IdSociedadEjercicio, out var d)) return d;
        d = await fuente.DatosAsync(idSociedad, e, cancelacion);
        _cache[e.IdSociedadEjercicio] = d;
        return d;
    }
}
