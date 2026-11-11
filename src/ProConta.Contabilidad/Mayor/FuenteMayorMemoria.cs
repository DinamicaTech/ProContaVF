namespace ProConta.Contabilidad.Mayor;

/// <summary>Un asiento en memoria para <see cref="FuenteMayorMemoria"/>.</summary>
public sealed record AsientoMemoria(
    int IdAsiento, int IdSociedadEjercicio, DateTime Fecha, string Descripcion, IReadOnlyList<ApunteMemoria> Apuntes,
    byte Tipo = 0, string? Notas = null, DateTime? FechaDocumento = null, DateTime? GrabadoEl = null, DateTime? ModificadoEl = null, bool EsAutomatico = false);

/// <summary>Un apunte en memoria (el id se asigna al añadir el asiento si es 0).</summary>
public sealed record ApunteMemoria(string Cuenta, decimal Debe, decimal Haber, string? Concepto = null, string? Documento = null, int IdApunte = 0, bool Punteado = false);

/// <summary>
/// Fuente del Mayor en memoria: misma semántica que la de Access (sin cierres, cuentas exactas o por prefijo, facturas en todas
/// las cuentas, todas las líneas de los asientos para la contrapartida). La usan las pruebas sin BD.
/// </summary>
public sealed class FuenteMayorMemoria(string idSociedad) : IFuenteMayor
{
    private readonly List<EjercicioMayor> _ejercicios = [];
    private readonly List<AsientoMemoria> _asientos = [];
    private readonly List<PlanCuenta> _plan = [];
    private readonly List<(int IdAsiento, CambioApunteMayor Cambio, int IdApunte)> _cambios = [];
    private int _siguienteApunte = 1;

    /// <summary>Cargas de apuntes hechas (para comprobar que una vista hace una sola).</summary>
    public int Cargas { get; private set; }

    /// <summary>Si se fija, la siguiente carga falla con esta excepción (escenario de error de carga).</summary>
    public Exception? FalloSiguiente { get; set; }

    public FuenteMayorMemoria Ejercicio(int id, int anio, bool cerrado = false)
    {
        _ejercicios.Add(new EjercicioMayor(id, anio, cerrado));
        return this;
    }

    public FuenteMayorMemoria Cuenta(int idEjercicio, string codigo, string nombre, bool imputable = true)
    {
        _plan.Add(new PlanCuenta(codigo, nombre, imputable, idEjercicio));
        return this;
    }

    public FuenteMayorMemoria Asiento(AsientoMemoria a)
    {
        var apuntes = a.Apuntes.Select(p => p.IdApunte == 0 ? p with { IdApunte = _siguienteApunte++ } : p).ToList();
        _asientos.Add(a with { Apuntes = apuntes });
        return this;
    }

    public FuenteMayorMemoria Cambio(int idAsiento, int idApunte, CambioApunteMayor cambio)
    {
        _cambios.Add((idAsiento, cambio, idApunte));
        return this;
    }

    private int Anio(int idEjercicio) => _ejercicios.First(e => e.IdSociedadEjercicio == idEjercicio).Ejercicio;

    public Task<IReadOnlyList<EjercicioMayor>> EjerciciosAsync(string sociedad, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<EjercicioMayor>>(sociedad == idSociedad
            ? _ejercicios.Select(e => e with { TieneAsientos = _asientos.Any(a => a.IdSociedadEjercicio == e.IdSociedadEjercicio) }).OrderBy(e => e.Ejercicio).ToList()
            : []);

    public Task<(IReadOnlyList<SumaCuenta> Sumas, IReadOnlyList<PlanCuenta> Plan)> CuentasAsync(IReadOnlyList<EjercicioMayor> ejercicios, CancellationToken cancelacion = default)
    {
        var ids = ejercicios.Select(e => e.IdSociedadEjercicio).ToHashSet();
        var sumas = _asientos.Where(a => ids.Contains(a.IdSociedadEjercicio) && a.Tipo != TiposAsientoMayor.Cierre)
            .SelectMany(a => a.Apuntes.Select(p => (a, p)))
            .GroupBy(x => (x.p.Cuenta, x.a.IdSociedadEjercicio, x.a.Tipo))
            .Select(g => new SumaCuenta(g.Key.Cuenta, g.Key.IdSociedadEjercicio, g.Key.Tipo, g.Sum(x => x.p.Debe), g.Sum(x => x.p.Haber), g.Count()))
            .ToList();
        return Task.FromResult<(IReadOnlyList<SumaCuenta>, IReadOnlyList<PlanCuenta>)>((sumas, _plan.Where(p => ids.Contains(p.IdSociedadEjercicio)).ToList()));
    }

    public Task<DatosMayor> ApuntesAsync(SolicitudMayor s, CancellationToken cancelacion = default)
    {
        Cargas++;
        if (FalloSiguiente is { } ex) { FalloSiguiente = null; return Task.FromException<DatosMayor>(ex); }
        var ids = s.Ejercicios.Select(e => e.IdSociedadEjercicio).ToHashSet();
        var facturas = s.Facturas?.ToHashSet(StringComparer.Ordinal);
        var asientos = _asientos.Where(a => ids.Contains(a.IdSociedadEjercicio) && a.Tipo != TiposAsientoMayor.Cierre).ToList();
        var apuntes = new List<ApunteMayor>();
        var conApuntes = new HashSet<int>();
        foreach (var a in asientos)
            for (var i = 0; i < a.Apuntes.Count; i++)
            {
                var p = a.Apuntes[i];
                var entra = facturas is not null ? facturas.Contains(Facturas.Normalizar(p.Documento)) : s.Incluye(p.Cuenta);
                if (!entra) continue;
                conApuntes.Add(a.IdAsiento);
                apuntes.Add(new ApunteMayor(p.IdApunte, a.IdAsiento, i + 1, a.IdSociedadEjercicio, Anio(a.IdSociedadEjercicio), a.Fecha, a.Tipo, p.Cuenta,
                    p.Debe, p.Haber, p.Concepto ?? a.Descripcion, p.Documento, a.Notas, a.FechaDocumento, a.GrabadoEl, a.ModificadoEl, p.Punteado, a.EsAutomatico));
            }
        var lineas = asientos.Where(a => conApuntes.Contains(a.IdAsiento))
            .SelectMany(a => a.Apuntes.Select((p, i) => new LineaContrapartida(a.IdAsiento, i + 1, p.Cuenta, p.Debe, p.Haber, p.IdApunte))).ToList();
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in _plan.Where(p => ids.Contains(p.IdSociedadEjercicio)).OrderBy(p => Anio(p.IdSociedadEjercicio))) nombres[p.Codigo] = p.Nombre;
        return Task.FromResult(new DatosMayor(apuntes, lineas, nombres));
    }

    public Task<AsientoMayor?> AsientoAsync(int idAsiento, CancellationToken cancelacion = default)
    {
        var a = _asientos.FirstOrDefault(x => x.IdAsiento == idAsiento);
        if (a is null) return Task.FromResult<AsientoMayor?>(null);
        var nombres = _plan.Where(p => p.IdSociedadEjercicio == a.IdSociedadEjercicio).ToDictionary(p => p.Codigo, p => p.Nombre);
        return Task.FromResult<AsientoMayor?>(new AsientoMayor(a.IdAsiento, Anio(a.IdSociedadEjercicio), a.Fecha, a.Descripcion, a.Tipo, a.Notas, false, null,
            a.GrabadoEl, null, a.ModificadoEl, null,
            a.Apuntes.Select((p, i) => new LineaAsientoMayor(i + 1, p.Cuenta, nombres.GetValueOrDefault(p.Cuenta, ""), p.Concepto ?? a.Descripcion, p.Documento, p.Debe, p.Haber)).ToList()));
    }

    public Task<IReadOnlyList<CambioApunteMayor>> HistorialAsync(int idAsiento, int idApunte, int orden, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<CambioApunteMayor>>(_cambios.Where(c => c.IdAsiento == idAsiento && c.IdApunte == idApunte).Select(c => c.Cambio).ToList());

    private readonly List<CuadreCuenta> _cuadres = [];

    /// <summary>Umbral de «Reiniciar el cuadre» que devuelve la fuente.</summary>
    public decimal Umbral { get; set; } = CuadresMayor.UmbralPredeterminado;

    public IReadOnlyList<CuadreCuenta> CuadresGuardados => _cuadres;

    public Task<IReadOnlyList<CuadreCuenta>> CuadresAsync(IReadOnlyList<EjercicioMayor> ejercicios, CancellationToken cancelacion = default)
    {
        var ids = ejercicios.Select(e => e.IdSociedadEjercicio).ToHashSet();
        return Task.FromResult<IReadOnlyList<CuadreCuenta>>(_cuadres.Where(c => ids.Contains(c.IdSociedadEjercicio)).ToList());
    }

    public Task GuardarCuadresAsync(IReadOnlyList<CuadreCuenta> marcar, IReadOnlyList<(int IdSociedadEjercicio, string Cuenta)> quitar, CancellationToken cancelacion = default)
    {
        var fuera = quitar.Concat(marcar.Select(m => (m.IdSociedadEjercicio, m.Cuenta))).ToHashSet();
        _cuadres.RemoveAll(c => fuera.Contains((c.IdSociedadEjercicio, c.Cuenta)));
        _cuadres.AddRange(marcar);
        return Task.CompletedTask;
    }

    public Task<decimal> UmbralCuadreAsync(CancellationToken cancelacion = default) => Task.FromResult(Umbral);

    /// <summary>Puntea en memoria (las pruebas simulan lo que haría el motor de asientos).</summary>
    public void Puntear(IEnumerable<int> idsApunte, bool punteado)
    {
        var ids = idsApunte.ToHashSet();
        for (var i = 0; i < _asientos.Count; i++)
            _asientos[i] = _asientos[i] with { Apuntes = _asientos[i].Apuntes.Select(p => ids.Contains(p.IdApunte) ? p with { Punteado = punteado } : p).ToList() };
    }
}
