using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Busqueda;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Remesas;
using ProConta.Norma43.Similares;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>Contexto de trabajo de la pantalla de líneas N43 y lo que abre fuera de ella (Mayor y asiento).</summary>
public sealed record ContextoLineasN43(ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Abre el Mayor de unas cuentas en unos ejercicios.</summary>
    public Action<IReadOnlyList<string>, IReadOnlyList<int>>? AbrirMayor { get; init; }
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre una factura de venta (IdFactura).</summary>
    public Action<int>? AbrirFacturaVenta { get; init; }
    /// <summary>Abre una factura de compra (IdRegistro).</summary>
    public Action<int>? AbrirFacturaCompra { get; init; }
}

/// <summary>Cuenta elegida en el buscador de cuentas.</summary>
public sealed record CuentaElegidaN43(int IdIdentidadContable, string Codigo, string Nombre)
{
    public string Texto => $"{Codigo} · {Nombre}";
}

/// <summary>Diálogos de la pantalla de líneas N43 (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosLineasN43
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);
    /// <summary>Pide un motivo obligatorio; null si se cancela.</summary>
    string? PedirMotivo(string titulo, string explicacion, string textoBoton);
    /// <summary>Buscador de subcuentas del ejercicio indicado; null si se cancela.</summary>
    CuentaElegidaN43? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto);
    /// <summary>Muestra el asiento que generaría contabilizar la línea (o por qué no se puede).</summary>
    void VerPropuesta(PropuestaLineaN43 propuesta);
    /// <summary>Diálogo de palabras clave de una línea (modal).</summary>
    void PalabrasClave(PalabrasClaveN43VM vm);
    /// <summary>Diálogo «Buscar por importe» (modal): true si se pulsa «Enlazar».</summary>
    bool BuscarPorImporte(BusquedaImporteN43VM vm) => false;
    /// <summary>Diálogo «Explotar remesa» (modal): true si se pulsa «Explotar».</summary>
    bool ExplotarRemesa(RemesaN43VM vm) => false;
    /// <summary>Diálogo «Conciliar facturas devueltas» (modal): true si se pulsa «Conciliar».</summary>
    bool ConciliarDevolucion(DevolucionN43VM vm) => false;
    /// <summary>Diálogo «Ajustes de importe» tras explotar (modal): true si se pulsa «Aplicar».</summary>
    bool AjustarImportes(AjustesRemesaN43VM vm) => false;
    /// <summary>Diálogo «Movimientos similares» (modal): lo que se elige al cerrarlo (vm.Eleccion), o null.</summary>
    EleccionSimilarN43? MovimientosSimilares(MovimientosSimilaresN43VM vm) => null;
}

/// <summary>Una acción del menú contextual: su texto, si se puede y, si no, por qué (en el tooltip).</summary>
public sealed class AccionLineasVM : ModeloVista
{
    private readonly string _descripcion;
    private string _texto;
    private string? _motivo;

    public AccionLineasVM(string texto, string descripcion, Action ejecutar)
    {
        _texto = texto;
        _descripcion = descripcion;
        Comando = new ComandoDelegado(ejecutar, () => _motivo is null);
    }

    public string Texto { get => _texto; private set => Fijar(ref _texto, value); }
    public bool Habilitada => _motivo is null;
    /// <summary>Lo que hace la acción o, si está desactivada, por qué.</summary>
    public string Ayuda => _motivo is null ? _descripcion : $"{_motivo} ({_descripcion})";
    public string? Motivo => _motivo;
    public ICommand Comando { get; }

    public void Fijar(string texto, string? motivo)
    {
        Texto = texto;
        if (_motivo == motivo) return;
        _motivo = motivo;
        Avisar(nameof(Habilitada), nameof(Ayuda), nameof(Motivo));
    }
}

/// <summary>Fila de la rejilla: una línea bancaria; los datos se sustituyen al refrescarla tras una acción (la fila sigue siendo la misma).</summary>
public sealed class FilaLineaN43VM(LineaN43 datos) : ModeloVista
{
    private LineaN43 _datos = datos;

    public LineaN43 Datos
    {
        get => _datos;
        set { _datos = value; Avisar(string.Empty); }
    }

    public int Id => _datos.Id;
    public string Fecha => Formatos.Fecha(_datos.FechaOperacion);
    public DateTime FechaOrden => _datos.FechaOperacion;
    public string FechaValor => Formatos.Fecha(_datos.FechaValor);
    public DateTime FechaValorOrden => _datos.FechaValor;
    public string CuentaOrigen => _datos.CuentaOrigen ?? "";
    public string NombreCuentaOrigen => _datos.NombreCuentaOrigen ?? _datos.NombreBanco;
    public string AyudaOrigen => _datos.CuentaOrigen is null
        ? $"La cuenta bancaria {_datos.Ccc} no tiene 572 en el ejercicio {_datos.Ejercicio}: asígnasela en Bancos"
        : $"{_datos.CuentaOrigen} · {_datos.NombreCuentaOrigen} (cuenta bancaria {_datos.Ccc}). Doble clic: su Mayor";
    public string Concepto => _datos.Concepto ?? "";
    public string AyudaConcepto => string.Join("\n", new[] { _datos.Concepto, _datos.Referencia1 is null ? null : $"Referencia 1: {_datos.Referencia1}",
        _datos.Referencia2 is null ? null : $"Referencia 2: {_datos.Referencia2}" }.Where(t => t is not null));
    public string Tipo => _datos.Tipo ?? "";
    public string Importe => Formatos.Importe(_datos.ImporteFirmado);
    public decimal ImporteOrden => _datos.ImporteFirmado;
    public bool EsCargo => _datos.EsCargo;
    public string AyudaImporte => _datos.EsCargo ? $"Cargo (sale dinero del banco): {Formatos.Importe(_datos.Importe)}" : $"Abono (entra dinero en el banco): {Formatos.Importe(_datos.Importe)}";
    public string CuentaDestino => _datos.CuentaDestino ?? "";
    public string NombreCuentaDestino => _datos.NombreCuentaDestino ?? "";
    public string AyudaDestino => _datos.Destinos.Count == 0
        ? "Sin cuenta: asígnala con «Asignar cuenta…» (F2) o crea una palabra clave (Ctrl+K)"
        : string.Join("\n", _datos.Destinos.Select(d => $"{d.Cuenta ?? "(no existe en el ejercicio)"} · {d.Nombre} · {Formatos.Importe(Math.Abs(d.ImporteFirmado))} · {Origen(d)}"))
          + "\nDoble clic: su Mayor";
    /// <summary>Documento del banco; vacío si viene a ceros (no aporta nada).</summary>
    public string Documento => _datos.Documento is { } d && d.Trim().Any(c => c != '0') ? d : "";
    public string Facturas => _datos.Facturas ?? "";
    public string AyudaFacturas => _datos.Facturas is null
        ? "Sin facturas enlazadas: búscalas con «Buscar por importe…» (clic derecho)"
        : $"Facturas enlazadas: {_datos.Facturas}";
    public string Estado => FiltroLineasN43.TextoEstado(_datos.Estado);
    public EstadoLineaN43 EstadoDatos => _datos.Estado;
    public string AyudaEstado => _datos.Estado switch
    {
        EstadoLineaN43.SinCuenta => "Sin cuenta de destino: no se puede contabilizar todavía",
        EstadoLineaN43.ConCuenta => "Tiene cuenta y falta contabilizarla (Ctrl+E)",
        EstadoLineaN43.Contabilizada => $"Contabilizada por ProContaVF en el asiento {_datos.IdAsiento}: se puede recontabilizar, actualizar sus textos o deshacer",
        _ => $"Contabilizada en el ERP (asiento {_datos.IdAsiento}): solo se consulta; se corrige desde el Mayor",
    } + (_datos.EjercicioCerrado ? $". El ejercicio {_datos.Ejercicio} está cerrado: solo lectura" : "");
    public string Asiento => _datos.IdAsiento?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public int AsientoOrden => _datos.IdAsiento ?? 0;
    public bool SoloLectura => _datos.EjercicioCerrado;

    public static string Origen(DestinoLineaN43 d) => d.Origen switch
    {
        AsignadorCuentasN43.Origen => d.PalabraClave is { } p ? $"por la palabra clave «{p}»" : "por palabra clave",
        ServicioLineasN43.OrigenManual => "puesta a mano",
        ServicioLineasN43.OrigenTraspaso => "traspaso entre bancos (cuenta puente)",
        BusquedaPorImporteN43.OrigenFactura => "la del tercero de la factura enlazada",
        ServicioRemesasN43.OrigenRemesa => "la del cliente del recibo (remesa explotada)",
        ServicioRemesasN43.OrigenDevolucion => "la del cliente del recibo devuelto",
        _ => $"origen {d.Origen.ToLowerInvariant()} (migración del ERP)",
    };
}

/// <summary>Ejercicio del selector múltiple de la pantalla.</summary>
public sealed class OpcionEjercicioN43VM(EjercicioLineasN43 ejercicio, bool marcado, Action alCambiar) : ModeloVista
{
    private bool _marcado = marcado;
    public EjercicioLineasN43 Ejercicio { get; } = ejercicio;
    public int Anio => Ejercicio.Ejercicio;
    public string Texto => Ejercicio.Cerrado ? $"{Anio} (cerrado)" : Anio.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Ayuda => Ejercicio.Cerrado ? $"Ejercicio {Anio}, cerrado: sus líneas se ven pero no se cambian" : $"Ver las líneas del ejercicio {Anio}";
    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value)) alCambiar(); } }
    internal void FijarSinAvisar(bool v) { _marcado = v; Avisar(nameof(Marcado)); }
}

/// <summary>Opción de un desplegable de filtro.</summary>
public sealed record OpcionFiltroN43<T>(T Valor, string Texto, string Ayuda);

/// <summary>Ficha de un filtro activo: se quita con su aspa.</summary>
public sealed record FichaFiltroN43(string Clave, string Texto, ICommand Quitar)
{
    public string Ayuda => $"Quita el filtro «{Texto}»";
}

/// <summary>
/// Pantalla de líneas N43 (norma43/pantallas): rejilla densa de las líneas bancarias de la sociedad en los ejercicios elegidos, con
/// filtros combinados en Y (fichas que se quitan una a una), detalle de la línea y todas las acciones en el menú contextual. La selección
/// se guarda por Id: filtrar y ordenar no la pierde, y las acciones actúan sobre las seleccionadas que se ven.
/// </summary>
public sealed class LineasN43VM : ModeloVista, IPestanaTrabajo
{
    private readonly ConsultaLineasN43 _consulta;
    private readonly ServicioLineasN43 _servicio;
    private readonly ServicioConciliacionN43 _conciliacion;
    private readonly AsignadorCuentasN43 _asignador;
    private readonly BusquedaPorImporteN43? _busqueda;
    private readonly ServicioRemesasN43? _remesas;
    private readonly IAjustesRemesaN43? _ajustes;
    private readonly ConsultaSimilaresN43? _similares;
    /// <summary>Candidatos de «Movimientos similares» por rango de ejercicios: se leen una vez y se olvidan al volver a leer o tras una acción.</summary>
    private readonly Dictionary<(int Desde, int Hasta), IReadOnlyList<CandidatoSimilarN43>> _candidatos = [];
    private IReadOnlyList<AvanceSimilarN43VM> _avance = [];
    private int _versionAvance;
    private readonly IDialogosLineasN43 _dialogos;
    private readonly ContextoLineasN43 _contexto;
    private readonly Action? _alCerrar;
    private readonly List<FilaLineaN43VM> _todas = [];
    private readonly HashSet<int> _seleccion = [];
    private IReadOnlyList<EjercicioLineasN43> _ejercicios = [];
    private ObservableCollection<FilaLineaN43VM> _filas = [];
    private FilaLineaN43VM? _actual;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _ocupado;
    private object? _vista;
    private OpcionFiltroN43<FiltroEstadoN43> _estadoFiltro;
    private OpcionFiltroN43<int?> _bancoFiltro;
    private string _textoFiltro = "";
    private string _importeFiltro = "";
    private DateTime? _desde;
    private DateTime? _hasta;
    private string _desdeTexto = "";
    private string _hastaTexto = "";
    private AccionesLineasN43 _acciones = AccionesLineasN43.Evaluar([]);

    public LineasN43VM(ConsultaLineasN43 consulta, ServicioLineasN43 servicio, ServicioConciliacionN43 conciliacion, AsignadorCuentasN43 asignador,
        IDialogosLineasN43 dialogos, ContextoLineasN43 contexto, Action? alCerrar = null, BusquedaPorImporteN43? busqueda = null, ServicioRemesasN43? remesas = null,
        IAjustesRemesaN43? ajustes = null, ConsultaSimilaresN43? similares = null)
    {
        _similares = similares;
        _busqueda = busqueda;
        _remesas = remesas;
        _ajustes = ajustes;
        _consulta = consulta;
        _servicio = servicio;
        _conciliacion = conciliacion;
        _asignador = asignador;
        _dialogos = dialogos;
        _contexto = contexto;
        _alCerrar = alCerrar;
        Estados =
        [
            new(FiltroEstadoN43.Todas, "Todas", "Todas las líneas de los ejercicios elegidos"),
            new(FiltroEstadoN43.Pendientes, "Pendientes", "Las que faltan por contabilizar: sin cuenta o con cuenta sin asiento"),
            new(FiltroEstadoN43.SinCuenta, "Sin cuenta", "Las que aún no tienen cuenta de destino"),
            new(FiltroEstadoN43.ConCuenta, "Por contabilizar", "Con cuenta de destino y sin asiento"),
            new(FiltroEstadoN43.Contabilizadas, "Contabilizadas", "Con asiento, de ProContaVF o del ERP"),
        ];
        _estadoFiltro = Estados[0];
        _bancoFiltro = new OpcionFiltroN43<int?>(null, "Todos los bancos", "Líneas de todas las cuentas bancarias");
        Bancos.Add(_bancoFiltro);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        QuitarFiltrosCommand = new ComandoDelegado(QuitarFiltros, () => Filtro != FiltroLineasN43.Ninguno);
        AccionAsignar = new("Asignar cuenta…", "Pone la cuenta de destino de las líneas seleccionadas con el buscador de subcuentas; si ya estaban contabilizadas, regenera su asiento", () => _ = AsignarCuentaAsync());
        AccionContabilizar = new("Contabilizar", "Genera el asiento de cada línea con cuenta, o lo sustituye (mismo número) si ya lo tenía", () => _ = ContabilizarAsync());
        AccionDeshacer = new("Deshacer el asiento…", "Borra el asiento generado por ProContaVF (pide el motivo, que queda registrado) y la línea vuelve a estar pendiente", () => _ = DeshacerAsync());
        AccionTextos = new("Actualizar textos del asiento", "Vuelve a calcular la descripción, el concepto y el documento del asiento sin cambiar su fecha, cuentas ni importes", () => _ = ActualizarTextosAsync());
        AccionReasignar = new("Reasignar por palabras clave", "Vuelve a buscar palabra clave para las líneas sin asiento: las cuentas puestas a mano o por un traspaso no se tocan", () => _ = ReasignarAsync());
        AccionSimilares = new("Movimientos similares…", "Busca las líneas con cuenta cuyo concepto se parece al de esta (en su ejercicio y los dos anteriores) para ponerle la misma cuenta", () => _ = MovimientosSimilaresAsync());
        VerTodosSimilaresCommand = new ComandoDelegado(() => _ = MovimientosSimilaresAsync(desdeDetalle: true), () => _actual is not null && HayAvanceVisible && !Ocupado);
        AccionPalabras = new("Palabras clave de la línea…", "Crea, corrige o borra las palabras clave que asignan la cuenta a movimientos como este", () => _ = PalabrasClaveAsync());
        AccionTraspaso = new("Traspaso entre bancos", "Imputa el cargo de un banco y el abono del otro a la cuenta puente de traspasos, donde sus asientos se compensan", () => _ = TraspasoAsync());
        AccionBuscarImporte = new("Buscar por importe…", "Busca para cada línea seleccionada las facturas de su importe (compras para los cargos, ventas para los abonos) y enlaza las que marques; si la línea no está contabilizada, le pone la cuenta del tercero", () => _ = BuscarPorImporteAsync());
        AccionVerAsiento = new("Ver el asiento", "Muestra el asiento de la línea o, si no está contabilizada, el que generaría (también con doble clic)", () => _ = VerAsientoAsync());
        AccionExplotarRemesa = new("Explotar remesa…", "Desglosa la remesa del banco en un recibo por factura domiciliada del mes, llevado a la cuenta de su cliente; las facturas quedan cobradas", () => _ = ExplotarRemesaAsync());
        AccionConciliarDevueltas = new("Conciliar facturas devueltas…", "Elige los recibos devueltos de la remesa anterior que suman el cargo: vuelven a la cuenta de su cliente y las facturas quedan pendientes de cobro", () => _ = ConciliarDevueltasAsync());
        AccionDeshacerExplosion = new("Deshacer explosión", "Quita el desglose en recibos de la remesa o la devolución y deja sus facturas como estaban (no si algún recibo ya tiene asiento)", () => _ = DeshacerExplosionAsync());
        ExplotarPendientesCommand = new ComandoDelegado(() => _ = ExplotarPendientesAsync(), () => _remesas is not null && !Ocupado && AniosMarcados.Count > 0);
        ActualizarAcciones();
    }

    // ---------- pestaña ----------

    public string Titulo => "Líneas N43";
    public string Ayuda => $"Movimientos bancarios Norma 43 de {_contexto.RazonSocial}: cuentas, asientos y palabras clave";
    public string Icono => "bank";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaLineasN43 { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- estado y datos ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string TituloEstado => Estado == EstadoVista.Vacio ? (_todas.Count == 0 ? "No hay líneas en estos ejercicios" : "Ninguna línea cumple los filtros") : "";
    public string TextoAccionEstado => _todas.Count == 0 ? "" : "Quitar los filtros";
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>Filas visibles (las que cumplen los filtros). Se sustituye entera al filtrar: la vista restaura la selección por Id.</summary>
    public ObservableCollection<FilaLineaN43VM> Filas { get => _filas; private set => Fijar(ref _filas, value); }
    public IReadOnlyList<FilaLineaN43VM> Todas => _todas;

    public ObservableCollection<OpcionEjercicioN43VM> Ejercicios { get; } = [];
    public string TextoEjercicios
    {
        get
        {
            var m = Ejercicios.Where(e => e.Marcado).Select(e => e.Anio).OrderBy(a => a).ToList();
            return m.Count switch { 0 => "Ningún ejercicio", 1 => $"Ejercicio {m[0]}", _ => "Ejercicios " + string.Join(", ", m) };
        }
    }
    public IReadOnlyList<int> AniosMarcados => Ejercicios.Where(e => e.Marcado).Select(e => e.Anio).OrderBy(a => a).ToList();

    public IReadOnlyList<OpcionFiltroN43<FiltroEstadoN43>> Estados { get; }
    public ObservableCollection<OpcionFiltroN43<int?>> Bancos { get; } = [];
    public ObservableCollection<FichaFiltroN43> Fichas { get; } = [];
    public bool HayFichas => Fichas.Count > 0;

    public OpcionFiltroN43<FiltroEstadoN43> EstadoFiltro { get => _estadoFiltro; set { if (value is not null && Fijar(ref _estadoFiltro, value)) Filtrar(); } }
    public OpcionFiltroN43<int?> BancoFiltro { get => _bancoFiltro; set { if (value is not null && Fijar(ref _bancoFiltro, value)) Filtrar(); } }
    public string TextoFiltro { get => _textoFiltro; set { if (Fijar(ref _textoFiltro, value ?? "")) Filtrar(); } }
    public string ImporteFiltro { get => _importeFiltro; set { if (Fijar(ref _importeFiltro, value ?? "")) { Avisar(nameof(ImporteNoValido)); Filtrar(); } } }
    public bool ImporteNoValido => _importeFiltro.Trim().Length > 0 && !Formatos.LeerImporte(_importeFiltro, out _);
    public DateTime? Desde { get => _desde; set { if (Fijar(ref _desde, value)) { _desdeTexto = Texto(value); Avisar(nameof(DesdeTexto), nameof(ErrorFechas)); Filtrar(); } } }
    public DateTime? Hasta { get => _hasta; set { if (Fijar(ref _hasta, value)) { _hastaTexto = Texto(value); Avisar(nameof(HastaTexto), nameof(ErrorFechas)); Filtrar(); } } }
    /// <summary>Fecha desde tal como se escribe (dd/mm/aaaa; también d/m/aa).</summary>
    public string DesdeTexto
    {
        get => _desdeTexto;
        set { if (!Fijar(ref _desdeTexto, value ?? "")) return; var f = LeerFecha(_desdeTexto); if (f.Valida && _desde != f.Fecha) { _desde = f.Fecha; Avisar(nameof(Desde)); Filtrar(); } Avisar(nameof(ErrorFechas)); }
    }
    public string HastaTexto
    {
        get => _hastaTexto;
        set { if (!Fijar(ref _hastaTexto, value ?? "")) return; var f = LeerFecha(_hastaTexto); if (f.Valida && _hasta != f.Fecha) { _hasta = f.Fecha; Avisar(nameof(Hasta)); Filtrar(); } Avisar(nameof(ErrorFechas)); }
    }
    public string? ErrorFechas => !LeerFecha(_desdeTexto).Valida || !LeerFecha(_hastaTexto).Valida ? "Escribe la fecha como dd/mm/aaaa"
        : _desde is DateTime d && _hasta is DateTime h && d > h ? "«Desde» es posterior a «Hasta»" : null;

    private static string Texto(DateTime? f) => f is DateTime x ? Formatos.Fecha(x) : "";

    internal static (bool Valida, DateTime? Fecha) LeerFecha(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return (true, null);
        return DateTime.TryParseExact(texto.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "d/M/yy", "dd/MM/yy", "d-M-yyyy", "ddMMyyyy"], System.Globalization.CultureInfo.GetCultureInfo("es-ES"),
            System.Globalization.DateTimeStyles.None, out var f) ? (true, f.Date) : (false, null);
    }

    public FiltroLineasN43 Filtro => new(_estadoFiltro.Valor, _bancoFiltro.Valor, string.IsNullOrWhiteSpace(_textoFiltro) ? null : _textoFiltro.Trim(),
        Formatos.LeerImporte(_importeFiltro, out var i) && _importeFiltro.Trim().Length > 0 ? i : null, _desde, _hasta,
        FiltroLineasN43.TieneDecimales(_importeFiltro));

    /// <summary>Ids seleccionados, también los que el filtro oculta (vuelven a verse seleccionados al quitarlo).</summary>
    public IReadOnlySet<int> IdsSeleccionados => _seleccion;
    /// <summary>Las seleccionadas que se ven: sobre ellas actúan las acciones.</summary>
    public IReadOnlyList<FilaLineaN43VM> Seleccion => _filas.Where(f => _seleccion.Contains(f.Id)).ToList();
    /// <summary>Mientras la vista restaura la selección tras filtrar, sus cambios no se toman como del operador.</summary>
    public bool Restaurando { get; set; }

    /// <summary>Línea del detalle (la última elegida).</summary>
    public FilaLineaN43VM? Actual
    {
        get => _actual;
        set { if (Fijar(ref _actual, value)) { Avisar(nameof(HayActual), nameof(DetalleDestinos), nameof(DetalleFechas)); _ = CalcularAvanceAsync(); } }
    }
    public bool HayActual => _actual is not null;
    public string DetalleFechas => _actual is not { } a ? "" :
        $"Operación {a.Fecha} · valor {a.FechaValor} · ejercicio {a.Datos.Ejercicio}{(a.Datos.EjercicioCerrado ? " (cerrado: solo lectura)" : "")}";
    public string DetalleDestinos => _actual is not { } a ? "" : a.AyudaDestino.Replace("\nDoble clic: su Mayor", "");

    public string Resumen
    {
        get
        {
            var sel = Seleccion;
            var ocultas = _seleccion.Count - sel.Count;
            var texto = $"{Formatos.Plural(_filas.Count, "línea", "líneas")}" + (_filas.Count != _todas.Count ? $" de {Formatos.Recuento(_todas.Count)}" : "") +
                $" · suma {Formatos.Importe(_filas.Sum(f => f.Datos.ImporteFirmado))}";
            if (sel.Count > 0) texto += $" · {Formatos.Plural(sel.Count, "seleccionada", "seleccionadas")} por {Formatos.Importe(sel.Sum(f => f.Datos.ImporteFirmado))}";
            if (ocultas > 0) texto += $" ({Formatos.Plural(ocultas, "seleccionada oculta", "seleccionadas ocultas")} por los filtros)";
            return texto;
        }
    }

    public AccionesLineasN43 Acciones => _acciones;
    public AccionLineasVM AccionAsignar { get; }
    public AccionLineasVM AccionContabilizar { get; }
    public AccionLineasVM AccionDeshacer { get; }
    public AccionLineasVM AccionTextos { get; }
    public AccionLineasVM AccionReasignar { get; }
    public AccionLineasVM AccionPalabras { get; }
    public AccionLineasVM AccionSimilares { get; }
    public AccionLineasVM AccionTraspaso { get; }
    public AccionLineasVM AccionBuscarImporte { get; }
    public AccionLineasVM AccionVerAsiento { get; }
    public AccionLineasVM AccionExplotarRemesa { get; }
    public AccionLineasVM AccionConciliarDevueltas { get; }
    public AccionLineasVM AccionDeshacerExplosion { get; }
    /// <summary>Botón de cabecera: explota las remesas pendientes de los ejercicios marcados que cuadran solas (acción general, no de la selección).</summary>
    public ICommand ExplotarPendientesCommand { get; }
    public bool HayRemesas => _remesas is not null;
    public ICommand RefrescarCommand { get; }
    public ICommand QuitarFiltrosCommand { get; }

    // ---------- carga ----------

    /// <summary>Primera carga: los ejercicios (marcado el de trabajo) y sus líneas.</summary>
    public async Task IniciarAsync()
    {
        try
        {
            _ejercicios = await _consulta.EjerciciosAsync(_contexto.Trabajo.IdSociedad);
            Ejercicios.Clear();
            foreach (var e in _ejercicios)
                Ejercicios.Add(new OpcionEjercicioN43VM(e, e.Ejercicio == _contexto.Trabajo.Ejercicio, () => _ = CambiarEjerciciosAsync()));
            if (Ejercicios.Count > 0 && !Ejercicios.Any(e => e.Marcado)) Ejercicios[0].FijarSinAvisar(true);
            Avisar(nameof(TextoEjercicios));
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los ejercicios de la sociedad: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
            return;
        }
        await CargarAsync();
    }

    private async Task CambiarEjerciciosAsync()
    {
        if (!Ejercicios.Any(e => e.Marcado))
        {
            _contexto.Avisar("Marca al menos un ejercicio: las líneas se leen de los ejercicios marcados.");
            Ejercicios.FirstOrDefault(e => e.Anio == _contexto.Trabajo.Ejercicio)?.FijarSinAvisar(true);
            if (!Ejercicios.Any(e => e.Marcado)) Ejercicios.FirstOrDefault()?.FijarSinAvisar(true);
        }
        Avisar(nameof(TextoEjercicios));
        await CargarAsync();
    }

    /// <summary>Lee las líneas de los ejercicios marcados y aplica los filtros; conserva la selección por Id.</summary>
    public async Task CargarAsync()
    {
        try
        {
            if (_todas.Count == 0) Estado = EstadoVista.Cargando;
            _candidatos.Clear();
            Mostrar(await _consulta.LineasAsync(_contexto.Trabajo.IdSociedad, AniosMarcados));
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer las líneas bancarias: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    /// <summary>Pone las líneas leídas (conserva la selección de las que siguen) y aplica los filtros.</summary>
    internal void Mostrar(IReadOnlyList<LineaN43> lineas, IReadOnlyList<EjercicioLineasN43>? ejercicios = null)
    {
        if (ejercicios is not null)
        {
            _ejercicios = ejercicios;
            Ejercicios.Clear();
            foreach (var e in ejercicios) Ejercicios.Add(new OpcionEjercicioN43VM(e, e.Ejercicio == _contexto.Trabajo.Ejercicio, () => _ = CambiarEjerciciosAsync()));
        }
        var porId = _todas.ToDictionary(f => f.Id);
        _todas.Clear();
        foreach (var l in lineas)
        {
            // La misma fila (con los datos nuevos) para que la rejilla y el detalle no la pierdan.
            if (porId.TryGetValue(l.Id, out var f)) { f.Datos = l; _todas.Add(f); }
            else _todas.Add(new FilaLineaN43VM(l));
        }
        _seleccion.IntersectWith(_todas.Select(f => f.Id));
        RehacerBancos();
        if (_actual is not null) Actual = _todas.FirstOrDefault(f => f.Id == _actual.Id);
        Filtrar();
    }

    private void RehacerBancos()
    {
        var elegido = _bancoFiltro.Valor;
        var bancos = _todas.Where(f => f.Datos.IdCuentaBancaria is not null).GroupBy(f => f.Datos.IdCuentaBancaria!.Value)
            .Select(g => new OpcionFiltroN43<int?>(g.Key, $"{g.First().Datos.NombreBanco} · {g.First().Datos.Ccc}",
                $"Solo las líneas de la cuenta {g.First().Datos.Ccc} ({g.First().Datos.NombreBanco})"))
            .OrderBy(b => b.Texto, StringComparer.CurrentCulture).ToList();
        while (Bancos.Count > 1) Bancos.RemoveAt(1);
        foreach (var b in bancos) Bancos.Add(b);
        _bancoFiltro = Bancos.FirstOrDefault(b => b.Valor == elegido) ?? Bancos[0];
        Avisar(nameof(BancoFiltro));
    }

    /// <summary>Aplica los filtros sobre las líneas leídas (sin volver a la BD).</summary>
    public void Filtrar()
    {
        var filtro = Filtro;
        Filas = new ObservableCollection<FilaLineaN43VM>(_todas.Where(f => filtro.Cumple(f.Datos)));
        Fichas.Clear();
        foreach (var (clave, texto) in filtro.Fichas(id => Bancos.FirstOrDefault(b => b.Valor == id)?.Texto ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            Fichas.Add(new FichaFiltroN43(clave, texto, new ComandoDelegado(() => QuitarFicha(clave))));
        Estado = _filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = _filas.Count > 0 ? ""
            : _todas.Count == 0 ? $"No hay movimientos importados en {TextoEjercicios.ToLowerInvariant()}. Marca otros ejercicios o importa extractos desde Bancos."
            : "Quita alguna ficha de filtro para ver más líneas.";
        Avisar(nameof(HayFichas), nameof(TituloEstado), nameof(TextoAccionEstado));
        ActualizarAcciones();
    }

    public void QuitarFicha(string clave)
    {
        var f = Filtro.Sin(clave);
        _estadoFiltro = Estados.First(e => e.Valor == f.Estado);
        _bancoFiltro = Bancos.FirstOrDefault(b => b.Valor == f.IdCuentaBancaria) ?? Bancos[0];
        _textoFiltro = f.Texto ?? "";
        if (clave == "importe") _importeFiltro = "";
        _desde = f.Desde;
        _hasta = f.Hasta;
        _desdeTexto = Texto(_desde);
        _hastaTexto = Texto(_hasta);
        Avisar(nameof(EstadoFiltro), nameof(BancoFiltro), nameof(TextoFiltro), nameof(ImporteFiltro), nameof(ImporteNoValido), nameof(Desde), nameof(Hasta),
            nameof(DesdeTexto), nameof(HastaTexto), nameof(ErrorFechas));
        Filtrar();
    }

    public void QuitarFiltros()
    {
        _estadoFiltro = Estados[0];
        _bancoFiltro = Bancos[0];
        _textoFiltro = "";
        _importeFiltro = "";
        _desde = null;
        _hasta = null;
        _desdeTexto = "";
        _hastaTexto = "";
        Avisar(nameof(EstadoFiltro), nameof(BancoFiltro), nameof(TextoFiltro), nameof(ImporteFiltro), nameof(ImporteNoValido), nameof(Desde), nameof(Hasta),
            nameof(DesdeTexto), nameof(HastaTexto), nameof(ErrorFechas));
        Filtrar();
    }

    // ---------- selección ----------

    /// <summary>
    /// La vista avisa de la selección de la rejilla (las filas visibles seleccionadas). Las ocultas por el filtro que estaban
    /// seleccionadas se conservan.
    /// </summary>
    public void CambiarSeleccion(IEnumerable<FilaLineaN43VM> seleccionadasVisibles)
    {
        if (Restaurando) return;
        var visibles = _filas.Select(f => f.Id).ToHashSet();
        _seleccion.RemoveWhere(visibles.Contains);
        foreach (var f in seleccionadasVisibles) _seleccion.Add(f.Id);
        ActualizarAcciones();
    }

    private void ActualizarAcciones()
    {
        _acciones = AccionesLineasN43.Evaluar(Seleccion.Select(f => f.Datos).ToList());
        var a = _acciones;
        AccionAsignar.Fijar("Asignar cuenta…", a.AsignarCuenta ?? Ocupada());
        AccionContabilizar.Fijar(a.TextoContabilizar, a.Contabilizar ?? Ocupada());
        AccionDeshacer.Fijar("Deshacer el asiento…" + (a.ADeshacer.Count > 1 ? $" ({a.ADeshacer.Count})" : ""), a.Deshacer ?? Ocupada());
        AccionTextos.Fijar("Actualizar textos del asiento" + (a.ADeshacer.Count > 1 ? $" ({a.ADeshacer.Count})" : ""), a.ActualizarTextos ?? Ocupada());
        AccionReasignar.Fijar(a.TextoReasignar, a.Reasignar ?? Ocupada());
        AccionPalabras.Fijar("Palabras clave de la línea…", a.PalabrasClave ?? Ocupada());
        AccionSimilares.Fijar("Movimientos similares…", MotivoSimilares() ?? Ocupada());
        AccionTraspaso.Fijar("Traspaso entre bancos", a.Traspaso ?? Ocupada());
        AccionBuscarImporte.Fijar("Buscar por importe…" + (Seleccion.Count > 1 ? $" ({Seleccion.Count})" : ""),
            a.BuscarPorImporte ?? (_busqueda is null ? "La búsqueda por importe no está disponible en esta pantalla." : null) ?? Ocupada());
        AccionVerAsiento.Fijar(Seleccion is [{ Datos.IdAsiento: null }] ? "Ver el asiento propuesto" : "Ver el asiento", a.VerAsiento);
        var sinRemesas = _remesas is null ? "Las remesas no están disponibles en esta pantalla." : null;
        AccionExplotarRemesa.Fijar("Explotar remesa…", a.ExplotarRemesa ?? sinRemesas ?? Ocupada());
        AccionConciliarDevueltas.Fijar("Conciliar facturas devueltas…", a.ConciliarDevueltas ?? sinRemesas ?? Ocupada());
        AccionDeshacerExplosion.Fijar("Deshacer explosión", a.DeshacerExplosion ?? sinRemesas ?? Ocupada());
        Avisar(nameof(Resumen), nameof(Acciones));
    }

    private string? Ocupada() => _ocupado ? "Espera a que termine la acción en curso." : null;

    // ---------- acciones ----------

    private IReadOnlyList<int> IdsSeleccion() => Seleccion.Select(f => f.Id).ToList();

    private async Task Ejecutar(Func<Task> trabajo, string queHacia)
    {
        if (Ocupado) return;
        Ocupado = true;
        ActualizarAcciones();
        try { await trabajo(); }
        catch (Exception ex)
        {
            _dialogos.Preguntar($"No se ha podido {queHacia}", $"La base de datos ha respondido: {ex.Message} No se ha cambiado nada que no se haya confirmado. Actualiza (F5) y vuelve a intentarlo.", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        finally
        {
            Ocupado = false;
            ActualizarAcciones();
        }
    }

    /// <summary>Vuelve a leer las líneas indicadas (después de una acción) y vuelve a filtrar.</summary>
    public async Task RefrescarLineasAsync(IReadOnlyCollection<int> ids)
    {
        _candidatos.Clear();
        var nuevas = (await _consulta.LineasAsync(_contexto.Trabajo.IdSociedad, AniosMarcados, ids)).ToDictionary(l => l.Id);
        foreach (var f in _todas.Where(f => ids.Contains(f.Id)).ToList())
        {
            if (nuevas.TryGetValue(f.Id, out var l)) f.Datos = l;
            else { _todas.Remove(f); _seleccion.Remove(f.Id); }
        }
        Filtrar();
        Avisar(nameof(DetalleDestinos), nameof(DetalleFechas));
        _ = CalcularAvanceAsync();
    }

    private ContextoSociedadEjercicio ContextoDe(int anio) =>
        _ejercicios.FirstOrDefault(e => e.Ejercicio == anio) is { } e
            ? new ContextoSociedadEjercicio(_contexto.Trabajo.IdSociedad, e.IdSociedadEjercicio, e.Ejercicio)
            : _contexto.Trabajo;

    private void InformarRechazos(string titulo, IReadOnlyDictionary<int, IReadOnlyList<Error>> rechazadas, int hechas, string queSiHizo)
    {
        if (rechazadas.Count == 0) return;
        var detalles = rechazadas.SelectMany(r => r.Value.Select(e => e.TextoOperador())).Distinct().Take(40).ToList();
        _dialogos.Preguntar(titulo,
            (hechas > 0 ? $"{queSiHizo}. " : "") + $"{Formatos.Plural(rechazadas.Count, "línea no se ha podido tratar", "líneas no se han podido tratar")}: cada aviso dice por qué y cómo resolverlo.",
            detalles, new OpcionMensaje("Entendido", "Cierra el aviso; las líneas que fallaron siguen como estaban", Principal: true, Cancelar: true));
    }

    public Task AsignarCuentaAsync() => Ejecutar(async () =>
    {
        var sel = Seleccion;
        if (sel.Count == 0 || _acciones.AsignarCuenta is not null) return;
        var anio = sel.GroupBy(f => f.Datos.Ejercicio).OrderByDescending(g => g.Count()).First().Key;
        var cuenta = _dialogos.ElegirCuenta(ContextoDe(anio),
            sel.Count == 1 ? $"Cuenta de destino · {sel[0].Datos.Rotulo}" : $"Cuenta de destino de {sel.Count} líneas", sel.Count == 1 ? sel[0].Datos.CuentaDestino : null);
        if (cuenta is null) return;
        await AplicarCuentaAsync(sel, cuenta.IdIdentidadContable, cuenta.Codigo);
    }, "asignar la cuenta");

    /// <summary>Pone la cuenta a las líneas (regenerando el asiento de las contabilizadas), las vuelve a leer y avisa de lo que no se ha podido.</summary>
    private async Task AplicarCuentaAsync(IReadOnlyList<FilaLineaN43VM> sel, int idIdentidad, string codigo)
    {
        var r = await _servicio.AsignarCuentaAsync(_contexto.Trabajo.IdSociedad, sel.Select(f => f.Id).ToList(), idIdentidad, _contexto.Actor);
        var texto = $"{codigo} asignada a {Formatos.Plural(r.Hechas.Count, "línea", "líneas")}";
        if (r.Recontabilizar.Count > 0)
        {
            var c = await _conciliacion.ContabilizarLineasAsync(_contexto.Trabajo.IdSociedad, r.Recontabilizar, _contexto.Actor);
            texto += $"; {Formatos.Plural(c.Hechas, "asiento regenerado", "asientos regenerados")} con la cuenta nueva";
            InformarRechazos("Algún asiento no se ha regenerado", c.Rechazadas, c.Hechas, "La cuenta sí se ha asignado");
        }
        await RefrescarLineasAsync(sel.Select(f => f.Id).ToList());
        _contexto.Avisar(texto + ".");
        InformarRechazos("Algunas líneas no han cambiado", r.Rechazadas.ToDictionary(x => x.Key, x => (IReadOnlyList<Error>)[x.Value]), r.Hechas.Count, texto);
    }

    // ---------- movimientos similares (norma43/movimientos-similares) ----------

    /// <summary>Botón «Ver todos» del avance del detalle: el diálogo completo para la línea del detalle.</summary>
    public ICommand VerTodosSimilaresCommand { get; }

    /// <summary>El detalle enseña el avance de similares: hay buscador y la línea se puede comparar.</summary>
    public bool HayAvanceVisible => _similares is not null && _actual is not null && ReglasSimilaresN43.Motivo(_actual.Datos) is null;
    public IReadOnlyList<AvanceSimilarN43VM> AvanceSimilares { get => _avance; private set { if (Fijar(ref _avance, value)) Avisar(nameof(HayAvance), nameof(SinAvance)); } }
    public bool HayAvance => _avance.Count > 0;
    public bool SinAvance => _avance.Count == 0;
    public string TextoSinAvance =>
        $"Ningún movimiento con otra cuenta se parece al menos un {ReglasSimilaresN43.MinimoAvance} %; «Ver todos» muestra desde el {ReglasSimilaresN43.MinimoLista} %.";

    /// <summary>La línea con la que se compara desde el menú: la del detalle si está seleccionada; si no, la primera seleccionada.</summary>
    private FilaLineaN43VM? BaseSimilares()
    {
        var sel = Seleccion;
        return _actual is not null && sel.Contains(_actual) ? _actual : sel.FirstOrDefault();
    }

    private string? MotivoSimilares()
    {
        if (_similares is null) return "Movimientos similares no está disponible en esta pantalla.";
        if (BaseSimilares() is not { } b) return "Selecciona una línea: se buscan los movimientos que se parecen a ella.";
        return ReglasSimilaresN43.Motivo(b.Datos);
    }

    /// <summary>Candidatos del rango de ejercicios de la línea; la primera vez se leen de la BD y después se reutilizan.</summary>
    private async Task<IReadOnlyList<CandidatoSimilarN43>> CandidatosAsync(int ejercicio)
    {
        var rango = ReglasSimilaresN43.Rango(ejercicio);
        if (_candidatos.TryGetValue(rango, out var c)) return c;
        c = await _similares!.CandidatosAsync(_contexto.Trabajo.IdSociedad, rango.Desde, rango.Hasta);
        _candidatos[rango] = c;
        return c;
    }

    /// <summary>Recalcula el avance del detalle para la línea actual (si cambia mientras se lee, gana la última).</summary>
    private async Task CalcularAvanceAsync()
    {
        var version = ++_versionAvance;
        Avisar(nameof(HayAvanceVisible));
        if (!HayAvanceVisible) { AvanceSimilares = []; return; }
        var fila = _actual!;
        try
        {
            var candidatos = await CandidatosAsync(fila.Datos.Ejercicio);
            if (version != _versionAvance) return;
            var motivo = AccionesLineasN43.Evaluar([fila.Datos]).AsignarCuenta;
            AvanceSimilares = ReglasSimilaresN43.Avance(fila.Datos, candidatos)
                .Select(s => new AvanceSimilarN43VM(s, motivo, () => _ = UsarSimilarAsync(fila, s))).ToList();
        }
        catch (Exception)
        {
            // El avance es una ayuda: si la lectura falla, no se enseña (el diálogo completo dirá el error).
            if (version == _versionAvance) AvanceSimilares = [];
        }
    }

    /// <summary>«Usar» del avance: pone la cuenta del movimiento parecido a la línea del detalle.</summary>
    private Task UsarSimilarAsync(FilaLineaN43VM fila, SimilarN43 s) => Ejecutar(async () =>
    {
        if (AccionesLineasN43.Evaluar([fila.Datos]).AsignarCuenta is not null || s.Linea.Cuenta is null) return;
        await AplicarCuentaAsync([fila], s.Linea.IdIdentidadContable, s.Linea.Cuenta);
    }, "asignar la cuenta");

    /// <summary>
    /// Movimientos similares: el diálogo con los parecidos a la línea. Desde el menú, «Asignar» pone la cuenta a todas las seleccionadas;
    /// desde «Ver todos» del detalle, solo a la línea del detalle.
    /// </summary>
    public Task MovimientosSimilaresAsync(bool desdeDetalle = false) => Ejecutar(async () =>
    {
        if (_similares is null) return;
        FilaLineaN43VM? fila;
        IReadOnlyList<FilaLineaN43VM> destino;
        if (desdeDetalle) { fila = _actual; destino = fila is null ? [] : [fila]; }
        else { fila = BaseSimilares(); destino = Seleccion; }
        if (fila is null || ReglasSimilaresN43.Motivo(fila.Datos) is not null) return;
        var resultados = ReglasSimilaresN43.Buscar(fila.Datos, await CandidatosAsync(fila.Datos.Ejercicio));
        var vm = new MovimientosSimilaresN43VM(fila.Datos, destino.Select(f => f.Datos).ToList(), resultados,
            AccionesLineasN43.Evaluar(destino.Select(f => f.Datos).ToList()).AsignarCuenta, _contexto.VerAsiento);
        switch (_dialogos.MovimientosSimilares(vm))
        {
            case { Accion: AccionSimilarN43.Asignar, Resultado: var r } when r.Linea.Cuenta is { } codigo && vm.MotivoAsignar is null:
                await AplicarCuentaAsync(destino, r.Linea.IdIdentidadContable, codigo);
                break;
            case { Accion: AccionSimilarN43.IrALinea, Resultado: var r }:
                await IrALineaAsync(r.Linea.IdLinea, r.Linea.Ejercicio);
                break;
        }
    }, "buscar los movimientos similares");

    /// <summary>
    /// Selecciona una línea en la lista y la pone en el detalle: si es de un ejercicio no marcado, lo marca y vuelve a leer; si los filtros
    /// la ocultan, los quita.
    /// </summary>
    public async Task IrALineaAsync(int id, int ejercicio)
    {
        if (_todas.All(f => f.Id != id) && Ejercicios.FirstOrDefault(e => e.Anio == ejercicio) is { Marcado: false } opcion)
        {
            opcion.FijarSinAvisar(true);
            Avisar(nameof(TextoEjercicios));
            await CargarAsync();
            _contexto.Avisar($"He marcado el ejercicio {ejercicio} para enseñarte la línea.");
        }
        var fila = _todas.FirstOrDefault(f => f.Id == id);
        if (fila is null)
        {
            _contexto.Avisar("Esa línea ya no está en la lista: puede que se haya revertido su importación. Pulsa Actualizar (F5).");
            return;
        }
        if (!_filas.Contains(fila))
        {
            QuitarFiltros();
            _contexto.Avisar("He quitado los filtros para enseñarte la línea.");
        }
        _seleccion.Clear();
        _seleccion.Add(id);
        Actual = fila;
        Filtrar();
    }

    /// <summary>
    /// Buscar por importe (norma43/busqueda-por-importe): un solo diálogo con las candidatas de todas las líneas seleccionadas; al
    /// pulsar «Enlazar» se aplica todo lo marcado y desmarcado. Si alguna línea contabilizada ya no lleva la cuenta del tercero, no se
    /// toca su asiento: el enlace es documental.
    /// </summary>
    public Task BuscarPorImporteAsync() => Ejecutar(async () =>
    {
        var sel = Seleccion;
        if (sel.Count == 0 || _busqueda is null || _acciones.BuscarPorImporte is not null) return;
        var busquedas = await _busqueda.BuscarAsync(_contexto.Trabajo.IdSociedad, sel.Select(f => f.Datos).ToList());
        var vm = new BusquedaImporteN43VM(busquedas, AbrirDocumento);
        if (!_dialogos.BuscarPorImporte(vm)) return;
        var cambios = vm.Cambios();
        if (cambios.Count == 0) return;
        var r = await _busqueda.EnlazarAsync(_contexto.Trabajo.IdSociedad, cambios, _contexto.Actor);
        await RefrescarLineasAsync(sel.Select(f => f.Id).ToList());
        var partes = new List<string>();
        if (r.Enlazadas > 0) partes.Add($"{Formatos.Plural(r.Enlazadas, "factura enlazada", "facturas enlazadas")}");
        if (r.Desenlazadas > 0) partes.Add($"{Formatos.Plural(r.Desenlazadas, "factura desenlazada", "facturas desenlazadas")}");
        if (r.ConCuenta.Count > 0) partes.Add($"{Formatos.Plural(r.ConCuenta.Count, "línea con la cuenta del tercero", "líneas con la cuenta del tercero")}");
        var texto = partes.Count == 0 ? "No ha cambiado nada" : string.Join("; ", partes);
        _contexto.Avisar(texto + ".");
        if (r.Avisos.Count > 0)
            _dialogos.Preguntar("Facturas enlazadas, con avisos", $"{texto}. Algunas líneas no han tomado la cuenta del tercero:", r.Avisos,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        InformarRechazos("Algunas líneas no se han enlazado", r.Rechazadas.ToDictionary(x => x.Key, x => (IReadOnlyList<Error>)[x.Value]), r.Lineas.Count, texto);
    }, "enlazar las facturas");

    // ---------- remesas y devoluciones (norma43/remesas) ----------

    /// <summary>
    /// Explotar remesa: el diálogo con las facturas SEPA del mes y su importe cobrado; al pulsar «Explotar» se graba el desglose (solo si
    /// cuadra) y se vuelve a leer la pantalla, que ya enseña los recibos nuevos.
    /// </summary>
    public Task ExplotarRemesaAsync() => Ejecutar(async () =>
    {
        if (Seleccion is not [var fila] || _remesas is null || _acciones.ExplotarRemesa is not null) return;
        var p = await _remesas.ProponerRemesaAsync(_contexto.Trabajo.IdSociedad, fila.Id);
        if (p is null) return;
        if (p.Bloqueo is { } b) { Informar("No se puede explotar la remesa", b); return; }
        var vm = new RemesaN43VM(p, _contexto.AbrirFacturaVenta);
        if (!_dialogos.ExplotarRemesa(vm)) return;
        var recibos = vm.Recibos();
        var r = await _remesas.ExplotarRemesaAsync(_contexto.Trabajo.IdSociedad, fila.Id, recibos, _contexto.Actor);
        await TrasRemesaAsync(r, "No se ha explotado la remesa");
        if (r.Hecho) await AjustarImportesAsync(vm, recibos, r.Recibos);
    }, "explotar la remesa");

    /// <summary>
    /// Tras explotar, las facturas cobradas por otro importe (norma43/remesas 2026-11-16 12:08): si el banco cobró más, actualizar su
    /// programada y emitir la factura de diferencia; si cobró menos, el operador elige; al final, el resumen con el recordatorio de la remesa
    /// del banco. Sin el puerto de ajustes, la diferencia se queda en la 430 y solo se recuerda.
    /// </summary>
    private async Task AjustarImportesAsync(RemesaN43VM vm, IReadOnlyList<ReciboRemesa> recibos, IReadOnlyList<int> lineas)
    {
        var distintas = vm.Filas.Where(f => f.Marcada && f.Distinto).ToList();
        if (distintas.Count == 0) return;
        var reciboDe = recibos.Select((x, i) => (x.IdFactura, Linea: lineas[i])).ToDictionary(x => x.IdFactura, x => x.Linea);
        if (_ajustes is null)
        {
            _dialogos.Preguntar("Facturas cobradas por otro importe", "La diferencia queda pendiente en la cuenta de cada cliente.",
                distintas.Select(f => $"{f.Numero}: factura {f.Importe} €, cobrado {Formatos.Importe(f.Cobrado)} €").ToList(),
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
            return;
        }
        var propuestas = await _ajustes.ProponerAsync(distintas.Select(f => (f.Datos.Id, f.Cobrado)).ToList());
        var ajustes = new AjustesRemesaN43VM(propuestas);
        if (!_dialogos.AjustarImportes(ajustes)) return;
        var r = await _ajustes.AplicarAsync(ajustes.Elegidos(id => reciboDe[id]), _contexto.Trabajo, _contexto.Actor);
        await CargarAsync();
        var detalles = r.Hechos.Concat(r.Fallos.Select(f => "No hecho: " + f)).Concat(r.Recordatorios.Select(x => "Recuerda: " + x)).ToList();
        _dialogos.Preguntar(r.Fallos.Count == 0 ? "Ajustes de importe hechos" : "Ajustes de importe, con avisos",
            r.Recordatorios.Count == 0 ? "Esto es lo que se ha hecho:" : "Esto es lo que se ha hecho. Actualiza en el banco los importes de la remesa SEPA de estas facturas para que el mes que viene cuadre:",
            detalles, new OpcionMensaje("Entendido", "Cierra el resumen", Principal: true, Cancelar: true));
    }

    /// <summary>Conciliar facturas devueltas: los recibos de la remesa anterior, con su combinación marcada; graba solo si cuadra.</summary>
    public Task ConciliarDevueltasAsync() => Ejecutar(async () =>
    {
        if (Seleccion is not [var fila] || _remesas is null || _acciones.ConciliarDevueltas is not null) return;
        var p = await _remesas.ProponerDevolucionAsync(_contexto.Trabajo.IdSociedad, fila.Id);
        if (p is null) return;
        if (p.Bloqueo is { } b && p.Remesas.Count == 0) { Informar("No se puede conciliar la devolución", b); return; }
        var vm = new DevolucionN43VM(p, id => _remesas.ProponerDevolucionAsync(_contexto.Trabajo.IdSociedad, fila.Id, id));
        if (!_dialogos.ConciliarDevolucion(vm)) return;
        var r = await _remesas.ConciliarDevolucionAsync(_contexto.Trabajo.IdSociedad, fila.Id, vm.IdsRecibos(), _contexto.Actor);
        await TrasRemesaAsync(r, "No se ha conciliado la devolución");
    }, "conciliar la devolución");

    public Task DeshacerExplosionAsync() => Ejecutar(async () =>
    {
        if (Seleccion is not [var fila] || _remesas is null || _acciones.DeshacerExplosion is not null) return;
        if (_dialogos.Preguntar("Deshacer explosión",
                $"Se quitará el desglose en recibos del {FiltroMinuscula(fila.Datos.Rotulo)} y sus facturas volverán a como estaban antes. Queda registrado con tu nombre y la hora. " +
                "No se puede si algún recibo ya tiene asiento: deshaz antes esos asientos.", null,
                new OpcionMensaje("Deshacer explosión", "Quita los recibos y libera las facturas", Principal: true),
                new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true)) != 0) return;
        var r = await _remesas.DeshacerAsync(_contexto.Trabajo.IdSociedad, fila.Id, _contexto.Actor);
        await TrasRemesaAsync(r, "No se ha deshecho la explosión");
    }, "deshacer la explosión");

    /// <summary>Botón de cabecera: explota todas las remesas pendientes de los ejercicios marcados cuyas facturas cuadran solas.</summary>
    public Task ExplotarPendientesAsync() => Ejecutar(async () =>
    {
        if (_remesas is null) return;
        var r = await _remesas.ExplotarPendientesAsync(_contexto.Trabajo.IdSociedad, AniosMarcados, _contexto.Actor);
        if (r.Explotadas.Count > 0) await CargarAsync();
        if (r.Pendientes.Count == 0) { _contexto.Avisar(r.Texto); return; }
        _dialogos.Preguntar("Remesas pendientes de explotar", r.Texto, r.Pendientes.Values.ToList(),
            new OpcionMensaje("Entendido", "Cierra el aviso; las que no cuadran siguen sin explotar", Principal: true, Cancelar: true));
    }, "explotar las remesas pendientes");

    private async Task TrasRemesaAsync(ResultadoRemesaN43 r, string tituloError)
    {
        if (!r.Hecho) { Informar(tituloError, r.Error!); return; }
        await CargarAsync();
        _contexto.Avisar(r.Texto);
    }

    private void Informar(string titulo, Error e) =>
        _dialogos.Preguntar(titulo, e.TextoOperador(), null, new OpcionMensaje("Entendido", "Cierra el aviso; no se ha cambiado nada", Principal: true, Cancelar: true));

    private static string FiltroMinuscula(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private void AbrirDocumento(CandidatoN43 c)
    {
        var abrir = c.Documento.Tipo == TipoDocumentoN43.Venta ? _contexto.AbrirFacturaVenta : _contexto.AbrirFacturaCompra;
        abrir?.Invoke(c.Documento.Id);
    }

    public Task ContabilizarAsync() => Ejecutar(async () =>
    {
        var ids = _acciones.AContabilizar;
        if (ids.Count == 0 || _acciones.Contabilizar is not null) return;
        var regenera = Seleccion.Count(f => ids.Contains(f.Id) && f.Datos.Estado == EstadoLineaN43.Contabilizada);
        if (regenera > 0 && _dialogos.Preguntar("Recontabilizar",
                $"{Formatos.Plural(regenera, "línea ya está contabilizada", "líneas ya están contabilizadas")}: se sustituirá su asiento por uno nuevo con el mismo número, con las cuentas que tienen ahora.",
                null, new OpcionMensaje(regenera == ids.Count ? "Recontabilizar" : "Contabilizar y recontabilizar", "Genera los asientos", Principal: true),
                new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true)) != 0) return;
        var r = await _conciliacion.ContabilizarLineasAsync(_contexto.Trabajo.IdSociedad, ids, _contexto.Actor);
        await RefrescarLineasAsync(ids);
        var hecho = $"{Formatos.Plural(r.Hechas, "línea contabilizada", "líneas contabilizadas")}";
        _contexto.Avisar(hecho + (r.Rechazadas.Count > 0 ? $"; {r.Rechazadas.Count} con avisos." : "."));
        InformarRechazos("Algunas líneas no se han contabilizado", r.Rechazadas, r.Hechas, hecho);
    }, "contabilizar");

    public Task DeshacerAsync() => Ejecutar(async () =>
    {
        var ids = _acciones.ADeshacer;
        if (ids.Count == 0 || _acciones.Deshacer is not null) return;
        var motivo = _dialogos.PedirMotivo("Deshacer el asiento",
            $"Se borrará {(ids.Count == 1 ? "el asiento" : $"el asiento de {ids.Count} líneas")} y las líneas volverán a estar pendientes de contabilizar, con su cuenta. " +
            "Queda registrado con tu nombre, la hora y el motivo. Si el asiento lleva una cuenta fiscal de un trimestre cerrado, no se podrá.", "Deshacer");
        if (motivo is null) return;
        var r = await _conciliacion.DeshacerAsync(_contexto.Trabajo.IdSociedad, ids, _contexto.Actor, motivo);
        await RefrescarLineasAsync(ids);
        var hecho = $"{Formatos.Plural(r.Hechas, "asiento borrado", "asientos borrados")}";
        _contexto.Avisar(hecho + ": sus líneas vuelven a estar pendientes.");
        InformarRechazos("Algunos asientos no se han borrado", r.Rechazadas, r.Hechas, hecho);
    }, "deshacer el asiento");

    public Task ActualizarTextosAsync() => Ejecutar(async () =>
    {
        var ids = _acciones.ADeshacer;
        if (ids.Count == 0 || _acciones.ActualizarTextos is not null) return;
        var r = await _conciliacion.ActualizarTextosAsync(_contexto.Trabajo.IdSociedad, ids, _contexto.Actor);
        await RefrescarLineasAsync(ids);
        var hecho = $"Textos de {Formatos.Plural(r.Hechas, "asiento actualizados", "asientos actualizados")}";
        _contexto.Avisar(hecho + ".");
        InformarRechazos("Algunos asientos no se han actualizado", r.Rechazadas, r.Hechas, hecho);
    }, "actualizar los textos");

    public Task ReasignarAsync() => Ejecutar(async () =>
    {
        if (_acciones.Reasignar is not null) return;
        var ids = IdsSeleccion();
        ResultadoAsignacionN43 r;
        if (ids.Count == 0)
        {
            r = await _asignador.AsignarAsync(_contexto.Trabajo);
            await CargarAsync();
        }
        else
        {
            r = await _asignador.ReasignarLineasAsync(_contexto.Trabajo, ids);
            await RefrescarLineasAsync(ids);
        }
        _contexto.Avisar(r.Texto);
    }, "reasignar por palabras clave");

    public Task TraspasoAsync() => Ejecutar(async () =>
    {
        var sel = Seleccion;
        if (sel.Count != 2 || _acciones.Traspaso is not null) return;
        var cargo = sel.First(f => f.Datos.EsCargo).Datos;
        var abono = sel.First(f => !f.Datos.EsCargo).Datos;
        if (_dialogos.Preguntar("Traspaso entre bancos",
                $"Las dos líneas se imputarán a la cuenta puente de traspasos ({ServicioLineasN43.CuentaPuentePredeterminada} salvo que la cuenta bancaria tenga otra configurada), " +
                "donde sus dos asientos se compensan. Después, contabilízalas (Ctrl+E).",
                [$"Sale de {cargo.NombreBanco} ({cargo.Ccc}) el {Formatos.Fecha(cargo.FechaOperacion)}: {Formatos.Importe(cargo.Importe)}",
                 $"Entra en {abono.NombreBanco} ({abono.Ccc}) el {Formatos.Fecha(abono.FechaOperacion)}: {Formatos.Importe(abono.Importe)}"],
                new OpcionMensaje("Registrar el traspaso", "Pone la cuenta puente en las dos líneas", Principal: true),
                new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true)) != 0) return;
        var r = await _servicio.TraspasoAsync(_contexto.Trabajo.IdSociedad, cargo.Id, abono.Id, _contexto.Actor);
        await RefrescarLineasAsync([cargo.Id, abono.Id]);
        if (r.Rechazadas.Count > 0)
            _dialogos.Preguntar("No se ha registrado el traspaso", r.Rechazadas.Values.First().TextoOperador(), null,
                new OpcionMensaje("Entendido", "No se ha cambiado nada", Principal: true, Cancelar: true));
        else _contexto.Avisar("Traspaso registrado: las dos líneas van a la cuenta puente. Contabilízalas con Ctrl+E.");
    }, "registrar el traspaso");

    public Task PalabrasClaveAsync() => Ejecutar(async () =>
    {
        if (Seleccion is not [var fila] || _acciones.PalabrasClave is not null) return;
        var vm = new PalabrasClaveN43VM(_servicio, _dialogos, _contexto.Trabajo.IdSociedad, fila.Datos, ContextoDe(fila.Datos.Ejercicio));
        await vm.CargarAsync();
        _dialogos.PalabrasClave(vm);
        if (!vm.HuboCambios) return;
        var opcion = fila.Datos.IdAsiento is null
            ? _dialogos.Preguntar("Aplicar las palabras clave",
                "Has cambiado palabras clave. Las líneas no cambian hasta que se reasignan: ¿las aplico ahora?", null,
                new OpcionMensaje("A esta línea", "Vuelve a buscar palabra clave para esta línea (si su cuenta la puso una palabra clave o no tiene)", Principal: true),
                new OpcionMensaje("A todas las pendientes", "Busca palabra clave para todas las líneas sin cuenta de la sociedad"),
                new OpcionMensaje("Ahora no", "Podrás hacerlo después con «Reasignar por palabras clave»", Cancelar: true))
            : 2;
        if (opcion == 0)
        {
            var r = await _asignador.ReasignarLineasAsync(_contexto.Trabajo, [fila.Id]);
            await RefrescarLineasAsync([fila.Id]);
            _contexto.Avisar(r.Texto);
        }
        else if (opcion == 1)
        {
            var r = await _asignador.AsignarAsync(_contexto.Trabajo);
            await CargarAsync();
            _contexto.Avisar(r.Texto);
        }
    }, "guardar las palabras clave");

    /// <summary>Doble clic en la línea: su asiento o, si no está contabilizada, el que generaría.</summary>
    public Task VerAsientoAsync(FilaLineaN43VM? fila = null) => Ejecutar(async () =>
    {
        fila ??= Seleccion is [var una] ? una : null;
        if (fila is null) return;
        if (fila.Datos.IdAsiento is int id)
        {
            if (_contexto.VerAsiento is { } ver) await ver(id);
            return;
        }
        var p = (await _conciliacion.ProponerAsync(_contexto.Trabajo.IdSociedad, [fila.Id])).FirstOrDefault();
        if (p is not null) _dialogos.VerPropuesta(p);
    }, "abrir el asiento");

    /// <summary>Doble clic en una cuenta: su Mayor en los ejercicios marcados.</summary>
    public void AbrirMayor(FilaLineaN43VM fila, bool origen)
    {
        var cuentas = origen
            ? (fila.Datos.CuentaOrigen is { } o ? [o] : Array.Empty<string>())
            : fila.Datos.Destinos.Select(d => d.Cuenta).OfType<string>().Distinct().ToArray();
        if (cuentas.Length == 0)
        {
            _contexto.Avisar(origen ? $"La cuenta bancaria {fila.Datos.Ccc} no tiene 572 en el ejercicio {fila.Datos.Ejercicio}: asígnasela en Bancos."
                : "Esta línea no tiene cuenta de destino: asígnale una con «Asignar cuenta…» (F2).");
            return;
        }
        var anios = AniosMarcados.Contains(fila.Datos.Ejercicio) ? AniosMarcados : [.. AniosMarcados, fila.Datos.Ejercicio];
        _contexto.AbrirMayor?.Invoke(cuentas, anios);
    }
}
