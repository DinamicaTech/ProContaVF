namespace ProConta.Contabilidad.Mayor;

/// <summary>De dónde lee el Mayor: la BD (<see cref="FuenteMayorAccess"/>) o una fuente en memoria en las pruebas.</summary>
public interface IFuenteMayor
{
    /// <summary>Ejercicios de la sociedad, en orden.</summary>
    Task<IReadOnlyList<EjercicioMayor>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default);

    /// <summary>Sumas por cuenta, ejercicio y tipo de asiento, y el plan de cuentas de los ejercicios (para el selector).</summary>
    Task<(IReadOnlyList<SumaCuenta> Sumas, IReadOnlyList<PlanCuenta> Plan)> CuentasAsync(IReadOnlyList<EjercicioMayor> ejercicios, CancellationToken cancelacion = default);

    /// <summary>Apuntes de la solicitud con todas las líneas de sus asientos y los nombres de las cuentas, en una sola carga.</summary>
    Task<DatosMayor> ApuntesAsync(SolicitudMayor solicitud, CancellationToken cancelacion = default);

    /// <summary>Un asiento completo para verlo; null si ya no existe.</summary>
    Task<AsientoMayor?> AsientoAsync(int idAsiento, CancellationToken cancelacion = default);

    /// <summary>Cambios guardados de un apunte (por su id o, en asientos regenerados, por su orden dentro del asiento).</summary>
    Task<IReadOnlyList<CambioApunteMayor>> HistorialAsync(int idAsiento, int idApunte, int orden, CancellationToken cancelacion = default);
}

/// <summary>Tipo de una etiqueta de filtro de la vista.</summary>
public enum TipoEtiquetaMayor
{
    /// <summary>Se buscan unas facturas en todas las cuentas (sustituye a las cuentas de la vista hasta quitarla).</summary>
    BuscarFactura,
    /// <summary>Solo las filas de unas facturas, dentro de las cuentas de la vista.</summary>
    FiltrarFactura,
    /// <summary>Filas quitadas con Supr.</summary>
    FilasQuitadas
}

/// <summary>Etiqueta de filtro que se ve encima de la tabla y se quita con su aspa.</summary>
public sealed record EtiquetaMayor(TipoEtiquetaMayor Tipo, string Texto, string Ayuda);

/// <summary>
/// Estado de una pantalla del Mayor, sin interfaz: ejercicios elegidos, cuentas de la vista, vista, filtros y filas quitadas,
/// con los datos cargados y el resultado calculado. La usan igual el Mayor completo y el embebido en la ficha de terceros,
/// así que bajo los mismos filtros muestran lo mismo (aceptación de contabilidad/mayor).
/// </summary>
public sealed class SesionMayor(IFuenteMayor fuente, string idSociedad)
{
    private readonly List<string> _filtroFacturas = [];
    private readonly HashSet<string> _quitadas = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _cuentas = [];
    private IReadOnlyList<string>? _busqueda;
    private string? _texto;

    public IFuenteMayor Fuente => fuente;
    public IReadOnlyList<EjercicioMayor> Ejercicios { get; private set; } = [];
    public IReadOnlyList<EjercicioMayor> Elegidos { get; private set; } = [];
    public IReadOnlyList<CuentaMayor> Catalogo { get; private set; } = [];
    /// <summary>Cuentas de la vista (códigos exactos o cortos).</summary>
    public IReadOnlyList<string> Cuentas => _cuentas;
    public VistaMayor Vista { get; private set; } = VistaMayor.Normal;
    public string? Texto => _texto;
    public IReadOnlyList<string>? Busqueda => _busqueda;
    public IReadOnlyList<string> FiltroFacturas => _filtroFacturas;
    public DatosMayor Datos { get; private set; } = DatosMayor.Vacio;
    public ResultadoMayor Resultado { get; private set; } = ResultadoMayor.Vacio;

    /// <summary>Algún ejercicio elegido está cerrado: el Mayor es de solo consulta en él.</summary>
    public bool HayCerrados => Elegidos.Any(e => e.Cerrado);

    public IReadOnlyList<EtiquetaMayor> Etiquetas
    {
        get
        {
            var l = new List<EtiquetaMayor>();
            if (_busqueda is { Count: > 0 } b)
                l.Add(new EtiquetaMayor(TipoEtiquetaMayor.BuscarFactura, "Factura " + string.Join(", ", b) + " en todas las cuentas",
                    "Se ven los apuntes de esta factura en todas las cuentas de los ejercicios elegidos. Quita la etiqueta para volver a las cuentas de la vista"));
            if (_filtroFacturas.Count > 0)
                l.Add(new EtiquetaMayor(TipoEtiquetaMayor.FiltrarFactura, "Solo la factura " + string.Join(", ", _filtroFacturas),
                    "Se ven solo las filas de esta factura en las cuentas de la vista. Quita la etiqueta para verlas todas"));
            if (_quitadas.Count > 0)
                l.Add(new EtiquetaMayor(TipoEtiquetaMayor.FilasQuitadas, (_quitadas.Count == 1 ? "1 fila quitada" : $"{_quitadas.Count} filas quitadas"),
                    "Filas quitadas de la vista con Supr (no se borra nada). Quita la etiqueta para volver a verlas"));
            return l;
        }
    }

    /// <summary>Carga los ejercicios de la sociedad y elige el de trabajo y el anterior (como el prototipo validado).</summary>
    public async Task IniciarAsync(int ejercicioTrabajo, IReadOnlyCollection<int>? ejerciciosElegidos = null, CancellationToken c = default)
    {
        var todos = await fuente.EjerciciosAsync(idSociedad, c);
        Ejercicios = todos.Where(e => e.TieneAsientos || e.Ejercicio == ejercicioTrabajo).OrderBy(e => e.Ejercicio).ToList();
        var elegidos = ejerciciosElegidos is { Count: > 0 }
            ? Ejercicios.Where(e => ejerciciosElegidos.Contains(e.Ejercicio)).ToList()
            : Ejercicios.Where(e => e.Ejercicio == ejercicioTrabajo || e.Ejercicio == ejercicioTrabajo - 1).ToList();
        if (elegidos.Count == 0) elegidos = Ejercicios.Where(e => e.Ejercicio == ejercicioTrabajo).ToList();
        if (elegidos.Count == 0 && Ejercicios.Count > 0) elegidos = [Ejercicios[^1]];
        Elegidos = elegidos;
        await CargarCatalogoAsync(c);
    }

    /// <summary>Cambia los ejercicios de la vista: recalcula el selector y vuelve a cargar los apuntes.</summary>
    public async Task ElegirEjerciciosAsync(IReadOnlyCollection<int> anios, CancellationToken c = default)
    {
        var elegidos = Ejercicios.Where(e => anios.Contains(e.Ejercicio)).ToList();
        if (elegidos.Count == 0) return;
        Elegidos = elegidos;
        _quitadas.Clear();
        await CargarCatalogoAsync(c);
        await CargarAsync(c);
    }

    private async Task CargarCatalogoAsync(CancellationToken c)
    {
        var (sumas, plan) = await fuente.CuentasAsync(Elegidos, c);
        Catalogo = SelectorCuentasMayor.Catalogo(sumas, plan, Elegidos);
    }

    /// <summary>Muestra el mayor de unas cuentas (una, varias = mayor combinado, o un código corto). Quita los filtros.</summary>
    public async Task VerCuentasAsync(IReadOnlyList<string> cuentas, CancellationToken c = default)
    {
        _cuentas = cuentas.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        _busqueda = null;
        _filtroFacturas.Clear();
        _quitadas.Clear();
        _texto = null;
        await CargarAsync(c);
    }

    /// <summary>Busca unas facturas en todas las cuentas de los ejercicios elegidos (etiqueta que se quita).</summary>
    public async Task BuscarFacturasAsync(IEnumerable<string> documentos, CancellationToken c = default)
    {
        var f = documentos.Select(Facturas.Normalizar).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (f.Count == 0) return;
        _busqueda = f;
        _filtroFacturas.Clear();
        _quitadas.Clear();
        await CargarAsync(c);
    }

    /// <summary>Deja solo las filas de unas facturas dentro de las cuentas de la vista.</summary>
    public void FiltrarFacturas(IEnumerable<string> documentos)
    {
        var f = documentos.Select(Facturas.Normalizar).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (f.Count == 0) return;
        _filtroFacturas.Clear();
        _filtroFacturas.AddRange(f);
        Recalcular();
    }

    public async Task QuitarEtiquetaAsync(TipoEtiquetaMayor tipo, CancellationToken c = default)
    {
        switch (tipo)
        {
            case TipoEtiquetaMayor.BuscarFactura:
                _busqueda = null;
                _quitadas.Clear();
                await CargarAsync(c);
                return;
            case TipoEtiquetaMayor.FiltrarFactura: _filtroFacturas.Clear(); break;
            case TipoEtiquetaMayor.FilasQuitadas: _quitadas.Clear(); break;
        }
        Recalcular();
    }

    public void CambiarVista(VistaMayor vista)
    {
        if (vista == Vista) return;
        Vista = vista;
        _quitadas.Clear(); // las claves de fila cambian de una vista a otra
        Recalcular();
    }

    public void FiltrarTexto(string? texto)
    {
        _texto = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        Recalcular();
    }

    /// <summary>Quita filas solo de la vista (Supr) y recalcula el saldo.</summary>
    public void Quitar(IEnumerable<string> claves)
    {
        foreach (var k in claves) _quitadas.Add(k);
        Recalcular();
    }

    /// <summary>Quita todos los filtros, la búsqueda y las filas quitadas.</summary>
    public async Task QuitarFiltrosAsync(CancellationToken c = default)
    {
        var buscaba = _busqueda is not null;
        _busqueda = null;
        _filtroFacturas.Clear();
        _quitadas.Clear();
        _texto = null;
        if (buscaba) await CargarAsync(c); else Recalcular();
    }

    /// <summary>Vuelve a leer los apuntes (una carga: apuntes, líneas de sus asientos y nombres).</summary>
    public async Task CargarAsync(CancellationToken c = default)
    {
        if (Elegidos.Count == 0 || (_cuentas.Count == 0 && _busqueda is null))
        {
            Datos = DatosMayor.Vacio;
            Recalcular();
            return;
        }
        var exactas = _cuentas.Where(x => SelectorCuentasMayor.EsExacta(x, Catalogo)).ToHashSet(StringComparer.Ordinal);
        var solicitud = _busqueda is { } b ? new SolicitudMayor([], new HashSet<string>(), Elegidos, b) : new SolicitudMayor(_cuentas, exactas, Elegidos);
        Datos = await fuente.ApuntesAsync(solicitud, c);
        Recalcular();
    }

    public void Recalcular() =>
        Resultado = CalculadoraMayor.Calcular(Datos, Elegidos, new OpcionesMayor(Vista, _filtroFacturas.Count > 0 ? _filtroFacturas.ToList() : null, _texto,
            _quitadas.Count > 0 ? new HashSet<string>(_quitadas, StringComparer.Ordinal) : null));

    /// <summary>Subcuentas de la vista (las del catálogo que entran por código exacto o corto), en orden.</summary>
    public IReadOnlyList<CuentaMayor> CuentasDeLaVista()
    {
        var exactas = _cuentas.Where(x => SelectorCuentasMayor.EsExacta(x, Catalogo)).ToHashSet(StringComparer.Ordinal);
        var s = new SolicitudMayor(_cuentas, exactas, Elegidos);
        return Catalogo.Where(c => c.EsSubcuenta && s.Incluye(c.Codigo)).ToList();
    }

    /// <summary>Nombre de una cuenta del catálogo (o vacío).</summary>
    public string Nombre(string codigo) => Catalogo.FirstOrDefault(c => c.Codigo == codigo)?.Nombre ?? Datos.Nombres.GetValueOrDefault(codigo, "");
}
