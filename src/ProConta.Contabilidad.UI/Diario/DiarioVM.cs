using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Diario;
using ProConta.Contabilidad.Mayor;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;
using ProConta.UI.Base.Tablas;

namespace ProConta.Contabilidad.UI.Diario;

/// <summary>Lo que necesita la pantalla Diario: de dónde lee, el motor de asientos con que escribe, los centros de coste y el ejercicio de trabajo.</summary>
public sealed record ContextoDiario(IFuenteDiario Fuente, IMotorAsientos Motor, ICatalogoCentrosCoste Centros, ContextoSociedadEjercicio Trabajo, string RazonSocial,
    Actor Actor, Action<string> Avisar);

/// <summary>Diálogos del Diario (los implementa <see cref="DialogosDiario"/>; las pruebas los sustituyen por respuestas fijas).</summary>
public interface IDialogosDiario
{
    void Error(string titulo, string mensaje);

    /// <summary>Pide un texto (número de factura o concepto) y, si <paramref name="pideMotivo"/>, el motivo. Null si se cancela.</summary>
    (string Valor, string Motivo)? PedirTexto(string titulo, string explicacion, string etiqueta, string? inicial, int longitud, bool pideMotivo, bool valorObligatorio);

    /// <summary>Pide el motivo de un cambio; null si se cancela.</summary>
    string? PedirMotivo(string titulo, string explicacion);

    /// <summary>Buscador común de subcuentas del ejercicio indicado; null si se cancela.</summary>
    (string Codigo, string Nombre)? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto);

    /// <summary>Elige un centro de coste (o «Sin centro») y el motivo; null si se cancela.</summary>
    (CentroDiario Centro, string Motivo)? ElegirCentro(IReadOnlyList<CentroDiario> centros, string explicacion);

    /// <summary>Confirma el borrado de unos asientos manuales y pide su motivo (obligatorio); null si se cancela.</summary>
    string? ConfirmarBorrado(IReadOnlyList<AsientoDiario> asientos);

    /// <summary>Consulta de los asientos borrados, con sus apuntes.</summary>
    void VerBorrados(IReadOnlyList<AsientoBorrado> borrados, Func<AsientoBorrado, ApunteBorrado, string> nombreCuenta, string ejercicios);

    /// <summary>PDF o Excel; null si se cancela.</summary>
    FormatoExportacionDiario? ElegirExportacion(string explicacion, bool hayPdf);

    /// <summary>Pregunta dónde guardar un fichero; null si se cancela.</summary>
    string? PedirRuta(string titulo, string nombrePropuesto, string filtro);

    /// <summary>Lista las cuentas cuya apertura no coincide con el cierre del ejercicio anterior.</summary>
    void VerDiferenciasApertura(IReadOnlyList<DiferenciaApertura> diferencias, int anterior, int trabajo);

    /// <summary>Pone un texto en el portapapeles.</summary>
    void Copiar(string texto);
}

/// <summary>
/// Pantalla Contabilidad › Diario (contabilidad/diario): todos los apuntes del ejercicio de trabajo y del anterior, por asiento o en lista,
/// con filtros, selección múltiple, sumas, ficha del asiento con edición rápida y menú contextual con las operaciones sobre la selección.
/// Lee una vez los dos ejercicios y filtra en memoria; todo lo que cambia apuntes pasa por el motor de asientos.
/// </summary>
public sealed class DiarioVM : ModeloVista
{
    public const string PeriodoEjercicio = "ej", PeriodoAnterior = "ant";
    private const string MotivoEjercicioCerrado = "El ejercicio de estos apuntes está cerrado";

    private readonly ContextoDiario _ctx;
    private readonly IDialogosDiario _dialogos;
    private DatosDiario _datos = DatosDiario.Vacio;
    private VistaDiario _vista = new([]);
    private EjercicioDiario? _trabajo;
    private EjercicioDiario? _anterior;
    private IReadOnlyList<DiferenciaApertura> _diferencias = [];
    private IReadOnlyList<FilaDiarioVM> _filas = [];
    private readonly HashSet<int> _plegados = [];
    private IReadOnlyList<ApunteDiario> _sel = [];
    private HashSet<string> _clavesSeleccion = [];
    private bool _iniciada;
    private bool _cargando;
    private string? _error;

    // filtros
    private string _periodo = PeriodoEjercicio;
    private DateTime? _desde;
    private DateTime? _hasta;
    private string _desdeTexto = "";
    private string _hastaTexto = "";
    private string? _errorFechas;
    private string _texto = "";
    private string _cuenta = "";
    private OpcionDiario<string?> _origen;
    private OpcionDiario<bool?> _punteo;
    private OpcionDiario<int?> _centro;
    private bool _porAsiento = true;
    private bool _totalesMes;

    // ficha
    private IReadOnlyList<CentroDiario> _centros = [new CentroDiario(null)];
    private ApunteDiario? _editado;
    private string _edConcepto = "";
    private string _edDocumento = "";
    private string _edVencimiento = "";
    private CentroDiario? _edCentro;
    private bool _edPunteado;
    private IReadOnlyList<CambioFichaDiario> _historial = [];
    private int _historialDe;

    public DiarioVM(ContextoDiario contexto, IDialogosDiario dialogos)
    {
        _ctx = contexto;
        _dialogos = dialogos;
        Origenes = [new(null, "Todos", "Asientos de cualquier origen"), .. OrigenesDiario.Todos.Select(o => new OpcionDiario<string?>(o, o, o == OrigenesDiario.Manual ? "Asientos hechos a mano" : $"Asientos automáticos de {o}"))];
        _origen = Origenes[0];
        Punteos = [new(null, "Todos", "Punteados o no"), new(false, "Sin puntear", "Apuntes que faltan por revisar"), new(true, "Punteados", "Apuntes ya revisados")];
        _punteo = Punteos[0];
        CentrosFiltro = [new(null, "Todos", "Con cualquier centro de coste o sin él")];
        _centro = CentrosFiltro[0];

        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        QuitarFiltrosCommand = new ComandoDelegado(QuitarFiltros);
        PeriodoCommand = new ComandoDelegado(p => PonerPeriodo(p as string ?? PeriodoEjercicio));
        VistaCommand = new ComandoDelegado(p => PorAsiento = (p as string) != "lista");
        DesplegarTodoCommand = new ComandoDelegado(() => { _plegados.Clear(); Reconstruir(); }, () => PorAsiento);
        PlegarTodoCommand = new ComandoDelegado(() => { foreach (var a in _vista.Asientos) _plegados.Add(a.Asiento.IdAsiento); Reconstruir(); }, () => PorAsiento);
        AlternarPlegadoCommand = new ComandoDelegado(p => { if (p is FilaDiarioVM { EsCabecera: true } f) AlternarPlegado(f); });
        BuscarCuentaCommand = new ComandoDelegado(BuscarCuentaFiltro);
        NuevoAsientoCommand = new ComandoDelegado(() => NuevoAsiento?.Invoke(), () => NuevoAsiento is not null);
        BorradosCommand = new ComandoDelegado(() => _ = VerBorradosAsync());
        ExportarCommand = new ComandoDelegado(() => _ = ExportarAsync(), () => _vista.NumeroApuntes > 0);
        VerDiferenciasCommand = new ComandoDelegado(() => { if (_anterior is not null && _trabajo is not null) _dialogos.VerDiferenciasApertura(_diferencias, _anterior.Ejercicio, _trabajo.Ejercicio); });
        SeleccionarApunteCommand = new ComandoDelegado(p => { if (p is LineaFichaDiario l) SeleccionarApunte(l.Apunte); });
        GuardarFichaCommand = new ComandoDelegado(() => _ = GuardarFichaAsync(), () => PuedeGuardarFicha);
        CancelarFichaCommand = new ComandoDelegado(CargarEdicion, () => FichaCambiada);

        AccionAbrir = new("Abrir el asiento", "Abre el asiento en la pantalla de asiento, en una pestaña (doble clic)", () => Abrir(modificar: false));
        AccionModificar = new("Modificar el asiento…", "Abre el asiento para cambiarlo: pide el motivo y solo guarda si cuadra", () => Abrir(modificar: true));
        AccionCorreccion = new("Asistente de corrección…", "Abre el asistente con las correcciones que encajan con este apunte (anular y rehacer, cambiar la subcuenta…)", Correccion);
        AccionBase = new("Usar como base de un asiento nuevo", "Abre un asiento nuevo con las mismas cuentas e importes, sin guardar hasta que lo confirmes", () => { if (UnAsiento is { } a) UsarComoBase?.Invoke(a); });
        AccionMayor = new("Ver el Mayor", "Abre el Mayor de las cuentas seleccionadas en su ejercicio, en una pestaña", VerMayor);
        AccionContrapartida = new("Ir a la contrapartida", "Selecciona el apunte de la contrapartida, en el mismo asiento", IrAContrapartida);
        AccionOrigen = new("Abrir el origen", "Abre el documento que generó este asiento en su pantalla", () => { if (UnAsiento is { } a) AbrirOrigen?.Invoke(a); });
        AccionPuntear = new("Puntear", "Marca o desmarca los apuntes como revisados; también en automáticos y periodos cerrados", () => _ = PuntearAsync());
        AccionDocumento = new("Asignar número de factura…", "Pone el número de factura o documento en los apuntes seleccionados; también en automáticos y periodos cerrados", () => _ = AsignarDocumentoAsync());
        AccionConcepto = new("Cambiar el concepto…", "Cambia el concepto de los apuntes seleccionados, también en automáticos; pide el motivo", () => _ = CambiarConceptoAsync());
        AccionCuenta = new("Cambiar la subcuenta…", "Pasa los apuntes seleccionados a otra subcuenta por el motor de asientos; pide el motivo", () => _ = CambiarCuentaAsync());
        AccionCentro = new("Asignar centro de coste…", "Pone o quita el centro de coste de los apuntes seleccionados; pide el motivo", () => _ = AsignarCentroAsync());
        AccionFiltrarCuenta = new("Filtrar por esta cuenta", "Deja solo los apuntes de la subcuenta de este apunte", () => { if (UnApunte is { } l) Cuenta = l.Cuenta; });
        AccionFiltrarDocumento = new("Filtrar por este documento", "Busca el documento de este apunte en todo lo cargado", () => { if (UnApunte is { Documento: { } d }) TextoFiltro = d; });
        AccionBuscarImporte = new("Buscar este importe", "Busca los apuntes con el mismo importe", () => { if (UnApunte is { } l) TextoFiltro = l.Importe.ToString("0.00", Formatos.Cultura); });
        AccionCopiar = new("Copiar las filas", "Copia los apuntes seleccionados al portapapeles, separados por tabuladores, para pegarlos en Excel", Copiar);
        AccionEliminar = new("Eliminar el asiento…", "Borra el asiento manual completo; pide el motivo y deja una copia en «Asientos borrados…»", () => _ = EliminarAsync());
        ActualizarAcciones();
    }

    // ---------- lo que abre fuera de la pantalla (lo conecta el módulo) ----------

    public Action? NuevoAsiento { get; set; }
    /// <summary>Abre un asiento en la pantalla de asiento; true para modificarlo (un automático, con sus bloqueos).</summary>
    public Action<AsientoDiario, bool>? AbrirAsiento { get; set; }
    public Action<ApunteDiario>? AsistenteCorreccion { get; set; }
    public Action<AsientoDiario>? UsarComoBase { get; set; }
    /// <summary>Abre el Mayor de unas cuentas en unos ejercicios (años).</summary>
    public Action<IReadOnlyList<string>, IReadOnlyList<int>>? AbrirMayor { get; set; }
    /// <summary>Por qué no se puede abrir el origen de un asiento automático (null: se puede con <see cref="AbrirOrigen"/>).</summary>
    public Func<AsientoDiario, string?>? PorQueNoAbrirOrigen { get; set; }
    public Action<AsientoDiario>? AbrirOrigen { get; set; }
    /// <summary>Convierte el HTML del Diario en PDF (el motor de informes); sin él, solo se exporta a Excel.</summary>
    public Func<string, byte[]>? HtmlAPdf { get; set; }
    public Action<string>? AbrirFichero { get; set; }
    /// <summary>Columnas visibles de la rejilla, en su orden (las da la vista); sin vista, las de fábrica.</summary>
    public Func<IReadOnlyList<string>>? ColumnasVisibles { get; set; }
    /// <summary>Apuntes de la lista en el orden en que los enseña la rejilla (si el operador la ha ordenado).</summary>
    public Func<IReadOnlyList<ApunteDiario>>? OrdenLista { get; set; }

    // ---------- cabecera y estado ----------

    public string Titulo => "Diario";

    /// <summary>Aviso de la rejilla (columna movida, restablecida…) para la barra de estado.</summary>
    public void AvisarDesdeLaVista(string texto) => _ctx.Avisar(texto);
    public string Subtitulo => _trabajo is null ? "" : _anterior is null
        ? $"{_ctx.RazonSocial} · ejercicio {_trabajo.Ejercicio}"
        : $"{_ctx.RazonSocial} · ejercicio {_trabajo.Ejercicio} y, cambiando el periodo, {_anterior.Ejercicio}" + (_anterior.Cerrado ? " (cerrado: solo consulta)" : "");
    public int EjercicioTrabajo => _ctx.Trabajo.Ejercicio;
    public bool HayAnterior => _anterior is not null;
    public string TextoAnterior => _anterior?.Ejercicio.ToString(CultureInfo.InvariantCulture) ?? "";

    public EstadoVista Estado => _cargando && _filas.Count == 0 ? EstadoVista.Cargando : _error is not null ? EstadoVista.Error : _filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
    public string TituloEstado => Estado switch
    {
        EstadoVista.Cargando => "Leyendo el diario…",
        EstadoVista.Error => "No he podido leer el diario",
        _ => _datos.Asientos.Count == 0 ? "No hay asientos" : "Ningún apunte cumple el filtro",
    };
    public string MensajeEstado => Estado switch
    {
        EstadoVista.Error => _error + " Pulsa F5 para volver a leerlo.",
        EstadoVista.Vacio when _datos.Asientos.Count == 0 => $"El ejercicio {EjercicioTrabajo} aún no tiene asientos contabilizados. Crea el primero con «+ Nuevo asiento».",
        EstadoVista.Vacio => $"No hay apuntes {TextoPeriodo()} con los filtros puestos. Quita alguno o amplía el periodo.",
        _ => "",
    };
    public string? TextoAccionEstado => Estado == EstadoVista.Vacio && _datos.Asientos.Count > 0 ? "Quitar los filtros" : null;

    public bool HayAvisoApertura => _diferencias.Count > 0;
    public string AvisoApertura => _diferencias.Count == 0 || _anterior is null || _trabajo is null ? ""
        : $"La apertura de {_trabajo.Ejercicio} no coincide con el saldo con que acabó {_anterior.Ejercicio} en {Formatos.Plural(_diferencias.Count, "cuenta", "cuentas")}: " +
          "se ha quedado desactualizada. Mira qué cuentas son y corrígela (cuando exista el proceso de apertura se podrá regenerar desde aquí).";

    public ICommand RefrescarCommand { get; }
    public ICommand QuitarFiltrosCommand { get; }
    public ICommand PeriodoCommand { get; }
    public ICommand VistaCommand { get; }
    public ICommand DesplegarTodoCommand { get; }
    public ICommand PlegarTodoCommand { get; }
    public ICommand AlternarPlegadoCommand { get; }
    public ICommand BuscarCuentaCommand { get; }
    public ICommand NuevoAsientoCommand { get; }
    public ICommand BorradosCommand { get; }
    public ICommand ExportarCommand { get; }
    public ICommand VerDiferenciasCommand { get; }
    public ICommand SeleccionarApunteCommand { get; }
    public ICommand GuardarFichaCommand { get; }
    public ICommand CancelarFichaCommand { get; }

    // ---------- carga ----------

    public async Task IniciarAsync()
    {
        if (_iniciada) return;
        _iniciada = true;
        await CargarAsync();
    }

    /// <summary>Vuelve a leer los dos ejercicios y conserva filtros, plegados y selección.</summary>
    public async Task CargarAsync()
    {
        _cargando = true;
        _error = null;
        AvisarEstado();
        try
        {
            var ejercicios = await _ctx.Fuente.EjerciciosAsync(_ctx.Trabajo.IdSociedad);
            _trabajo = ejercicios.FirstOrDefault(e => e.IdSociedadEjercicio == _ctx.Trabajo.IdSociedadEjercicio)
                       ?? new EjercicioDiario(_ctx.Trabajo.IdSociedadEjercicio, _ctx.Trabajo.Ejercicio, false, null);
            _anterior = ejercicios.FirstOrDefault(e => e.Ejercicio == _trabajo.Ejercicio - 1);
            var centros = (await _ctx.Centros.ListarAsync(_ctx.Trabajo.IdSociedad)).OrderBy(c => c.RutaJerarquica, StringComparer.Ordinal).ToList();
            _centros = [new CentroDiario(null), .. centros.Where(c => c.EstaActivo && c.EsImputable).Select(c => new CentroDiario(c))];
            var idCentro = _centro.Valor;
            CentrosFiltro = [new(null, "Todos", "Con cualquier centro de coste o sin él"), .. centros.Select(c => new OpcionDiario<int?>(c.IdCentroCoste, $"{c.Codigo} · {c.Nombre}", c.RutaJerarquica))];
            _centro = CentrosFiltro.FirstOrDefault(c => c.Valor == idCentro) ?? CentrosFiltro[0];
            _datos = await _ctx.Fuente.CargarAsync(_ctx.Trabajo.IdSociedad, _anterior is null ? [_trabajo] : [_anterior, _trabajo]);
            _diferencias = _anterior is null ? [] : CalculadoraDiario.DiferenciasApertura(_datos, _anterior, _trabajo);
            if (_desde is null && _hasta is null && _periodo.Length > 0) PonerFechasDelPeriodo(_periodo);
        }
        catch (Exception ex)
        {
            _error = $"La base de datos ha respondido: {ex.Message}";
            _datos = DatosDiario.Vacio;
        }
        finally { _cargando = false; }
        Avisar(nameof(Subtitulo), nameof(HayAnterior), nameof(TextoAnterior), nameof(CentrosFiltro), nameof(CentroFiltro), nameof(CentrosFicha), nameof(HayAvisoApertura), nameof(AvisoApertura),
            nameof(AyudaTrimestres));
        Filtrar();
    }

    // ---------- filtros ----------

    public string Periodo => _periodo;
    public bool EnEjercicio => _periodo == PeriodoEjercicio;
    public bool En1T => _periodo == "1";
    public bool En2T => _periodo == "2";
    public bool En3T => _periodo == "3";
    public bool En4T => _periodo == "4";
    public bool EnAnterior => _periodo == PeriodoAnterior;
    public string AyudaTrimestres => $"Trimestre del ejercicio {EjercicioTrabajo}" + (_trabajo?.TrimestreActivo is int t ? $" (el activo es el {t}T; los anteriores están cerrados para las cuentas de IVA y retenciones)" : "");

    private void PonerPeriodo(string periodo)
    {
        _periodo = periodo;
        PonerFechasDelPeriodo(periodo);
        _errorFechas = null;
        Avisar(nameof(ErrorFechas));
        Filtrar();
    }

    private void PonerFechasDelPeriodo(string periodo)
    {
        var anio = periodo == PeriodoAnterior && _anterior is not null ? _anterior.Ejercicio : EjercicioTrabajo;
        if (int.TryParse(periodo, out var t) && t is >= 1 and <= 4)
        {
            _desde = TrimestreContable.Inicio(anio, t);
            _hasta = _desde.Value.AddMonths(3).AddDays(-1);
        }
        else
        {
            _desde = new DateTime(anio, 1, 1);
            _hasta = new DateTime(anio, 12, 31);
        }
        _desdeTexto = Formatos.Fecha(_desde.Value);
        _hastaTexto = Formatos.Fecha(_hasta.Value);
        Avisar(nameof(Periodo), nameof(EnEjercicio), nameof(En1T), nameof(En2T), nameof(En3T), nameof(En4T), nameof(EnAnterior), nameof(DesdeTexto), nameof(HastaTexto));
    }

    public string DesdeTexto { get => _desdeTexto; set { if (Fijar(ref _desdeTexto, value ?? "")) FechasEscritas(); } }
    public string HastaTexto { get => _hastaTexto; set { if (Fijar(ref _hastaTexto, value ?? "")) FechasEscritas(); } }
    public string? ErrorFechas => _errorFechas;

    private void FechasEscritas()
    {
        var bienDesde = LeerFecha(_desdeTexto, out var d);
        var bienHasta = LeerFecha(_hastaTexto, out var h);
        _errorFechas = !bienDesde || !bienHasta ? "Fecha no válida: escribe dd/mm/aaaa" : d is not null && h is not null && d > h ? "«Desde» es posterior a «Hasta»" : null;
        Avisar(nameof(ErrorFechas));
        if (_errorFechas is not null) return;
        _desde = d;
        _hasta = h;
        _periodo = "";
        Avisar(nameof(Periodo), nameof(EnEjercicio), nameof(En1T), nameof(En2T), nameof(En3T), nameof(En4T), nameof(EnAnterior));
        Filtrar();
    }

    /// <summary>Lee una fecha escrita (dd/mm/aaaa, también con - o . y con el año en dos cifras); vacía vale y es null.</summary>
    public static bool LeerFecha(string? texto, out DateTime? fecha)
    {
        fecha = null;
        var t = (texto ?? "").Trim().Replace('-', '/').Replace('.', '/');
        if (t.Length == 0) return true;
        if (!DateTime.TryParseExact(t, ["d/M/yyyy", "d/M/yy"], Formatos.Cultura, DateTimeStyles.None, out var f)) return false;
        fecha = f.Date;
        return true;
    }

    public string TextoFiltro { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public string Cuenta { get => _cuenta; set { if (Fijar(ref _cuenta, (value ?? "").Trim())) Filtrar(); } }
    public IReadOnlyList<OpcionDiario<string?>> Origenes { get; }
    public OpcionDiario<string?> OrigenFiltro { get => _origen; set { if (value is not null && Fijar(ref _origen, value)) Filtrar(); } }
    public IReadOnlyList<OpcionDiario<bool?>> Punteos { get; }
    public OpcionDiario<bool?> PunteoFiltro { get => _punteo; set { if (value is not null && Fijar(ref _punteo, value)) Filtrar(); } }
    public IReadOnlyList<OpcionDiario<int?>> CentrosFiltro { get; private set; }
    public OpcionDiario<int?> CentroFiltro { get => _centro; set { if (value is not null && Fijar(ref _centro, value)) Filtrar(); } }

    public FiltroDiario Filtro => new()
    {
        Desde = _desde, Hasta = _hasta, Texto = _texto.Trim().Length == 0 ? null : _texto.Trim(), Cuenta = _cuenta.Length == 0 ? null : CuentaExpandida(),
        Origen = _origen.Valor, Punteado = _punteo.Valor, IdCentroCoste = _centro.Valor,
    };

    /// <summary>«629.1» vale por 6290001, como en el resto de pantallas.</summary>
    private string CuentaExpandida() => _cuenta.Contains('.') && Cuentas.BuscadorCuentas.ExpandirPunto(_cuenta) is { } c ? c : _cuenta;

    public IReadOnlyList<FichaFiltroDiario> Fichas
    {
        get
        {
            var lista = new List<FichaFiltroDiario>();
            void Ficha(string texto, Action quitar) => lista.Add(new(texto, "Quitar este filtro", new ComandoDelegado(quitar)));
            if (_texto.Trim().Length > 0) Ficha($"Buscar: {_texto.Trim()}", () => TextoFiltro = "");
            if (_cuenta.Length > 0)
                Ficha($"Cuenta: {_cuenta}" + (_datos.Asientos.SelectMany(a => a.Apuntes).FirstOrDefault(l => l.Cuenta == CuentaExpandida()) is { } l ? $" {l.NombreCuenta}" : "…"), () => Cuenta = "");
            if (_origen.Valor is not null) Ficha($"Origen: {_origen.Texto}", () => OrigenFiltro = Origenes[0]);
            if (_punteo.Valor is not null) Ficha(_punteo.Texto, () => PunteoFiltro = Punteos[0]);
            if (_centro.Valor is not null) Ficha($"Centro: {_centro.Texto}", () => CentroFiltro = CentrosFiltro[0]);
            return lista;
        }
    }
    public bool HayFichas => _texto.Trim().Length > 0 || _cuenta.Length > 0 || _origen.Valor is not null || _punteo.Valor is not null || _centro.Valor is not null;

    private void QuitarFiltros()
    {
        _texto = "";
        _cuenta = "";
        _origen = Origenes[0];
        _punteo = Punteos[0];
        _centro = CentrosFiltro[0];
        _periodo = PeriodoEjercicio;
        _errorFechas = null;
        PonerFechasDelPeriodo(_periodo);
        Avisar(nameof(TextoFiltro), nameof(Cuenta), nameof(OrigenFiltro), nameof(PunteoFiltro), nameof(CentroFiltro), nameof(ErrorFechas));
        Filtrar();
    }

    private void BuscarCuentaFiltro()
    {
        if (_trabajo is null) return;
        var ejercicio = _periodo == PeriodoAnterior && _anterior is not null ? _anterior : _trabajo;
        if (_dialogos.ElegirCuenta(Contexto(ejercicio), "Buscar una subcuenta", _cuenta.Length > 0 ? _cuenta : null) is { } c) Cuenta = c.Codigo;
    }

    private ContextoSociedadEjercicio Contexto(EjercicioDiario e) => new(_ctx.Trabajo.IdSociedad, e.IdSociedadEjercicio, e.Ejercicio);

    private string TextoPeriodo() => _desde is { } d && _hasta is { } h ? $"entre el {Formatos.Fecha(d)} y el {Formatos.Fecha(h)}"
        : _desde is { } d2 ? $"desde el {Formatos.Fecha(d2)}" : _hasta is { } h2 ? $"hasta el {Formatos.Fecha(h2)}" : "en los ejercicios cargados";

    // ---------- vista y filas ----------

    public bool PorAsiento
    {
        get => _porAsiento;
        set { if (Fijar(ref _porAsiento, value)) { Avisar(nameof(EnLista)); Reconstruir(); } }
    }
    public bool EnLista => !_porAsiento;
    public bool TotalesPorMes { get => _totalesMes; set { if (Fijar(ref _totalesMes, value)) Reconstruir(); } }

    public IReadOnlyList<FilaDiarioVM> Filas => _filas;
    public VistaDiario Vista => _vista;

    private void Filtrar()
    {
        _vista = CalculadoraDiario.Filtrar(_datos, Filtro);
        Avisar(nameof(Fichas), nameof(HayFichas));
        Reconstruir();
    }

    /// <summary>Rehace las filas de la rejilla con la vista y los plegados actuales; la selección se conserva por clave.</summary>
    private void Reconstruir()
    {
        var filas = new List<FilaDiarioVM>();
        if (_porAsiento)
        {
            var filtraApuntes = Filtro.FiltraApuntes;
            var totales = _totalesMes ? _vista.TotalesPorMes().ToDictionary(t => (t.Ejercicio, t.Mes)) : null;
            (int, int)? mes = null;
            foreach (var (asiento, coinciden) in _vista.Asientos)
            {
                var actual = (asiento.Fecha.Year, asiento.Fecha.Month);
                if (totales is not null && mes is { } m && m != actual) filas.Add(new FilaDiarioVM(totales[m]));
                mes = actual;
                var plegado = _plegados.Contains(asiento.IdAsiento);
                var notas = (asiento.BloqueoPeriodo is not null ? (asiento.Ejercicio.Cerrado ? "  ·  🔒 ejercicio cerrado" : "  ·  🔒 trimestre cerrado") : "")
                            + (asiento.CorregidoAMano ? "  ·  ✎ modificado a mano" : "")
                            + (filtraApuntes && coinciden.Count < asiento.Apuntes.Count ? $"  ·  {coinciden.Count} de {asiento.Apuntes.Count} cumplen el filtro" : "");
                filas.Add(new FilaDiarioVM(asiento, plegado, notas));
                if (plegado) continue;
                var aciertos = filtraApuntes ? coinciden.ToHashSet() : null;
                foreach (var l in asiento.Apuntes)
                    filas.Add(new FilaDiarioVM(l, agrupada: true, coincide: aciertos?.Contains(l) == true, apagada: aciertos is not null && !aciertos.Contains(l)));
            }
            if (totales is not null && mes is { } ultimo) filas.Add(new FilaDiarioVM(totales[ultimo]));
        }
        else
            foreach (var l in _vista.Apuntes) filas.Add(new FilaDiarioVM(l, agrupada: false, coincide: false, apagada: false));
        _filas = filas;
        // La selección que ya no se ve se pierde; la que sigue, la restaura la vista por clave.
        var visibles = filas.Select(f => f.Clave).ToHashSet(StringComparer.Ordinal);
        _clavesSeleccion.IntersectWith(visibles);
        _sel = ApuntesDe(filas.Where(f => _clavesSeleccion.Contains(f.Clave)));
        Avisar(nameof(Filas), nameof(Resumen), nameof(AyudaResumen), nameof(Cuadre), nameof(HayCuadre));
        AvisarEstado();
        AlCambiarSeleccion();
    }

    private void AvisarEstado() => Avisar(nameof(Estado), nameof(TituloEstado), nameof(MensajeEstado), nameof(TextoAccionEstado));

    private void AlternarPlegado(FilaDiarioVM fila)
    {
        var id = fila.Asiento!.IdAsiento;
        if (!_plegados.Remove(id)) _plegados.Add(id);
        Reconstruir();
    }

    public string Resumen => _porAsiento
        ? $"{Formatos.Plural(_vista.NumeroAsientos, "asiento", "asientos")} · {Formatos.Plural(_vista.ApuntesAsientos, "apunte", "apuntes")} · Debe {Formatos.Importe(_vista.DebeAsientos)} · Haber {Formatos.Importe(_vista.HaberAsientos)}"
        : $"{Formatos.Plural(_vista.NumeroAsientos, "asiento", "asientos")} · {Formatos.Plural(_vista.NumeroApuntes, "apunte", "apuntes")} · Debe {Formatos.Importe(_vista.DebeApuntes)} · Haber {Formatos.Importe(_vista.HaberApuntes)} · Saldo {Formatos.Importe(_vista.DebeApuntes - _vista.HaberApuntes)}";
    public string AyudaResumen => _porAsiento
        ? "Los asientos que cumplen el filtro, completos: por eso el Debe y el Haber coinciden"
        : "Solo los apuntes que cumplen el filtro; el saldo es su Debe menos su Haber";
    public bool HayCuadre => _porAsiento && _vista.NumeroAsientos > 0;
    public string Cuadre => _vista.DebeAsientos == _vista.HaberAsientos ? "Cuadrado" : $"Descuadre de {Formatos.Importe(_vista.DebeAsientos - _vista.HaberAsientos)}";

    // ---------- selección ----------

    /// <summary>Claves de las filas seleccionadas (apuntes «p…» y cabeceras «a…»), para que la vista las restaure al reconstruir.</summary>
    public IReadOnlySet<string> ClavesSeleccion => _clavesSeleccion;
    /// <summary>La vista está restaurando la selección: sus cambios intermedios no cuentan.</summary>
    public bool Restaurando { get; set; }
    /// <summary>La vista debe seleccionar esta fila y llevarla a la vista.</summary>
    public event Action<FilaDiarioVM>? SeleccionPedida;

    /// <summary>Apuntes seleccionados: los de las filas de apunte y todos los de los asientos cuya cabecera está seleccionada.</summary>
    public IReadOnlyList<ApunteDiario> Seleccion => _sel;

    private static IReadOnlyList<ApunteDiario> ApuntesDe(IEnumerable<FilaDiarioVM> filas) =>
        filas.SelectMany(f => f.Tipo switch
        {
            TipoFilaDiario.Apunte => [f.Apunte!],
            TipoFilaDiario.Asiento => f.Asiento!.Apuntes,
            _ => (IEnumerable<ApunteDiario>)[],
        }).Distinct().ToList();

    /// <summary>La vista avisa de las filas seleccionadas.</summary>
    public void CambiarSeleccion(IReadOnlyList<FilaDiarioVM> filas)
    {
        if (Restaurando) return;
        _clavesSeleccion = filas.Select(f => f.Clave).ToHashSet(StringComparer.Ordinal);
        _sel = ApuntesDe(filas);
        AlCambiarSeleccion();
    }

    private void AlCambiarSeleccion()
    {
        Avisar(nameof(ResumenSeleccion), nameof(HaySeleccion));
        ActualizarAcciones();
        ActualizarFicha();
    }

    public bool HaySeleccion => _sel.Count > 0;
    public string ResumenSeleccion
    {
        get
        {
            if (_sel.Count == 0) return "";
            var debe = _sel.Sum(l => l.Debe);
            var haber = _sel.Sum(l => l.Haber);
            return $"Selección: {Formatos.Plural(_sel.Count, "apunte", "apuntes")} de {Formatos.Plural(AsientosSel.Count, "asiento", "asientos")} · Debe {Formatos.Importe(debe)} · Haber {Formatos.Importe(haber)} · Diferencia {Formatos.Importe(debe - haber)}";
        }
    }

    private IReadOnlyList<AsientoDiario> AsientosSel => _sel.Select(l => l.Asiento).Distinct().ToList();
    private AsientoDiario? UnAsiento => AsientosSel is { Count: 1 } a ? a[0] : null;
    private ApunteDiario? UnApunte => _sel.Count == 1 ? _sel[0] : null;

    private void SeleccionarApunte(ApunteDiario apunte)
    {
        if (_plegados.Remove(apunte.Asiento.IdAsiento)) Reconstruir();
        if (_filas.FirstOrDefault(f => f.Apunte == apunte) is { } fila) SeleccionPedida?.Invoke(fila);
    }

    // ---------- acciones del menú contextual ----------

    public AccionDiarioVM AccionAbrir { get; }
    public AccionDiarioVM AccionModificar { get; }
    public AccionDiarioVM AccionCorreccion { get; }
    public AccionDiarioVM AccionBase { get; }
    public AccionDiarioVM AccionMayor { get; }
    public AccionDiarioVM AccionContrapartida { get; }
    public AccionDiarioVM AccionOrigen { get; }
    public AccionDiarioVM AccionPuntear { get; }
    public AccionDiarioVM AccionDocumento { get; }
    public AccionDiarioVM AccionConcepto { get; }
    public AccionDiarioVM AccionCuenta { get; }
    public AccionDiarioVM AccionCentro { get; }
    public AccionDiarioVM AccionFiltrarCuenta { get; }
    public AccionDiarioVM AccionFiltrarDocumento { get; }
    public AccionDiarioVM AccionBuscarImporte { get; }
    public AccionDiarioVM AccionCopiar { get; }
    public AccionDiarioVM AccionEliminar { get; }

    /// <summary>Cabecera del menú: sobre qué actúa.</summary>
    public string TituloMenu => _sel.Count == 0 ? "Nada seleccionado"
        : UnApunte is { } l ? $"Apunte {l.Orden} del asiento {l.Asiento.IdAsiento} · {l.Cuenta}"
        : $"{Formatos.Plural(_sel.Count, "apunte", "apuntes")} de {Formatos.Plural(AsientosSel.Count, "asiento", "asientos")}";

    private void ActualizarAcciones()
    {
        var asientos = AsientosSel;
        var a1 = UnAsiento;
        var l1 = UnApunte;
        var nada = _sel.Count == 0 ? "Selecciona algún apunte" : null;
        var unAsiento = nada ?? (a1 is null ? "Solo con apuntes de un asiento" : null);
        var unApunte = nada ?? (l1 is null ? "Solo con un apunte" : null);
        var cerrado = _sel.Any(l => l.Asiento.Ejercicio.Cerrado) ? MotivoEjercicioCerrado : null;
        var cuentas = _sel.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal).ToList();

        AccionAbrir.Poner(a1 is null ? "Abrir el asiento" : $"Abrir el asiento {a1.IdAsiento}", unAsiento ?? (AbrirAsiento is null ? "No disponible aquí" : null));
        AccionModificar.Poner("Modificar el asiento…", unAsiento ?? (AbrirAsiento is null ? "No disponible aquí" : null)
            ?? (a1!.Ejercicio.Cerrado ? $"El ejercicio {a1.Ejercicio.Ejercicio} está cerrado" : null));
        AccionCorreccion.Poner("Asistente de corrección…", unApunte ?? (AsistenteCorreccion is null ? "No disponible aquí" : null)
            ?? (l1!.Asiento.Ejercicio.IdSociedadEjercicio != _ctx.Trabajo.IdSociedadEjercicio ? $"El apunte es de {l1.Asiento.Ejercicio.Ejercicio}: el asistente crea asientos en el ejercicio de trabajo" : null));
        AccionBase.Poner("Usar como base de un asiento nuevo", unAsiento ?? (UsarComoBase is null ? "No disponible aquí" : null));
        AccionMayor.Poner(cuentas.Count == 1 ? $"Ver el Mayor de {cuentas[0]}" : cuentas.Count > 1 ? $"Ver el Mayor de las {cuentas.Count} cuentas" : "Ver el Mayor", nada ?? (AbrirMayor is null ? "No disponible aquí" : null));
        var contra = l1?.CuentasContrapartida ?? [];
        AccionContrapartida.Poner(contra.Count == 1 ? $"Ir a la contrapartida ({contra[0]})" : "Ir a la contrapartida",
            unApunte ?? (contra.Count == 0 ? "Este apunte no tiene contrapartida" : contra.Count > 1 ? $"Tiene varias ({string.Join(", ", contra)}): míralas en la ficha" : null));
        AccionOrigen.Poner(a1 is { EsAutomatico: true } ? $"Abrir el origen: {a1.Referencia}" : "Abrir el origen",
            unAsiento ?? (!a1!.EsAutomatico ? "Asiento manual, sin origen" : AbrirOrigen is null ? "No disponible aquí" : PorQueNoAbrirOrigen?.Invoke(a1)));
        AccionPuntear.Poner(_sel.Count > 0 && _sel.All(l => l.Punteado) ? "Quitar el punteo" : "Puntear", nada);
        AccionDocumento.Poner("Asignar número de factura…", nada);
        AccionConcepto.Poner("Cambiar el concepto…", nada ?? cerrado);
        AccionCuenta.Poner("Cambiar la subcuenta…", nada ?? cerrado
            ?? (cuentas.Count > 1 ? "Los apuntes son de cuentas distintas" : null)
            ?? (asientos.Select(a => a.Ejercicio.IdSociedadEjercicio).Distinct().Count() > 1 ? "Los apuntes son de ejercicios distintos" : null)
            ?? _sel.Select(l => l.BloqueoCuenta).FirstOrDefault(b => b is not null));
        AccionCentro.Poner("Asignar centro de coste…", nada ?? cerrado);
        AccionFiltrarCuenta.Poner("Filtrar por esta cuenta", unApunte);
        AccionFiltrarDocumento.Poner("Filtrar por este documento", unApunte ?? (l1!.Documento is null ? "El apunte no tiene documento" : null));
        AccionBuscarImporte.Poner("Buscar este importe", unApunte);
        AccionCopiar.Poner("Copiar las filas", nada);
        AccionEliminar.Poner(asientos.Count == 1 ? $"Eliminar el asiento {asientos[0].IdAsiento}…" : asientos.Count > 1 ? $"Eliminar los {asientos.Count} asientos…" : "Eliminar el asiento…",
            nada ?? (asientos.Any(a => a.EsAutomatico) ? "Los asientos automáticos se borran desde su origen" : null) ?? asientos.Select(a => a.BloqueoPeriodo).FirstOrDefault(b => b is not null));
        Avisar(nameof(TituloMenu));
    }

    /// <summary>Doble clic o «Abrir»: la pantalla de asiento; con <paramref name="modificar"/>, un automático se abre para corregirlo.</summary>
    public void Abrir(bool modificar)
    {
        if (UnAsiento is { } a && (modificar ? AccionModificar : AccionAbrir).Habilitada) AbrirAsiento?.Invoke(a, modificar);
    }

    /// <summary>Doble clic en una fila: abre su asiento.</summary>
    public void AbrirFila(FilaDiarioVM fila)
    {
        if (fila.Asiento is { } a) AbrirAsiento?.Invoke(a, false);
    }

    private void Correccion()
    {
        if (UnApunte is { } l) AsistenteCorreccion?.Invoke(l);
    }

    private void VerMayor()
    {
        if (_sel.Count == 0) return;
        AbrirMayor?.Invoke(_sel.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal).ToList(), _sel.Select(l => l.Asiento.Ejercicio.Ejercicio).Distinct().Order().ToList());
    }

    private void IrAContrapartida()
    {
        if (UnApunte is not { CuentasContrapartida: { Count: 1 } c } l) return;
        if (l.Asiento.Apuntes.FirstOrDefault(x => x != l && x.Cuenta == c[0]) is { } otro) SeleccionarApunte(otro);
    }

    private void Copiar()
    {
        if (_sel.Count == 0) return;
        var columnas = ColumnasExportacion();
        var lineas = new List<string> { string.Join('\t', columnas.Select(c => c.Titulo)) };
        lineas.AddRange(_sel.Select(l => string.Join('\t', columnas.Select(c => c.Clave == ColumnasDiario.Bloqueos ? "" : c.Texto(l).Replace('\t', ' ')))));
        _dialogos.Copiar(string.Join(Environment.NewLine, lineas));
        _ctx.Avisar($"{Formatos.Plural(_sel.Count, "fila copiada", "filas copiadas")}: se pegan en Excel con sus columnas.");
    }

    /// <summary>F4: puntea la selección o, si ya está toda punteada, le quita el punteo. No vuelve a leer la BD.</summary>
    public async Task PuntearAsync()
    {
        if (_sel.Count == 0) return;
        var apuntes = _sel.ToList();
        var puntear = !apuntes.All(l => l.Punteado);
        if (!await Escribir("No he podido cambiar el punteo", () => _ctx.Motor.PuntearApuntesAsync(apuntes.Select(l => l.IdApunte).ToList(), puntear, _ctx.Actor, puntear ? "Punteo en el Diario" : "Despunteo en el Diario"), recargar: false))
            return;
        foreach (var l in apuntes) l.Punteado = puntear;
        var cambiados = apuntes.ToHashSet();
        if (_punteo.Valor is not null) Filtrar();
        else foreach (var f in _filas.Where(f => f.Apunte is { } a && cambiados.Contains(a))) f.AvisarPunteo();
        ActualizarAcciones();
        ActualizarFicha();
        _ctx.Avisar(puntear ? $"{Formatos.Plural(apuntes.Count, "apunte punteado", "apuntes punteados")}" : $"Punteo quitado de {Formatos.Plural(apuntes.Count, "apunte", "apuntes")}");
    }

    /// <summary>
    /// Ctrl+N: número de factura de la selección, con la misma regla que el Mayor (contabilidad/mayor 2026-11-12 10:00): si solo un apunte
    /// tiene número, pasa a los demás sin preguntar; si no, se pide, proponiendo el primero que no empieza por 0000.
    /// </summary>
    public async Task AsignarDocumentoAsync()
    {
        if (_sel.Count == 0) return;
        var apuntes = _sel.ToList();
        var conNumero = apuntes.Where(l => l.Documento is not null).ToList();
        string numero, motivo;
        if (apuntes.Count > 1 && conNumero.Count == 1)
        {
            numero = conNumero[0].Documento!;
            motivo = $"Mismo número que el apunte {conNumero[0].Orden} del asiento {conNumero[0].Asiento.IdAsiento}";
        }
        else
        {
            var propuesto = conNumero.Select(l => l.Documento!).FirstOrDefault(d => !d.StartsWith("0000", StringComparison.Ordinal)) ?? conNumero.FirstOrDefault()?.Documento;
            if (_dialogos.PedirTexto("Número de factura", $"Se pone en {Formatos.Plural(apuntes.Count, "apunte seleccionado", "apuntes seleccionados")}. También vale en asientos automáticos y en periodos cerrados; vacío lo quita.",
                    "Número de factura o documento", propuesto, 100, pideMotivo: true, valorObligatorio: false) is not { } r) return;
            (numero, motivo) = r;
        }
        if (await Escribir("No he podido poner el número de factura", () => _ctx.Motor.AsignarNumeroDocumentoAsync(apuntes.Select(l => l.IdApunte).ToList(), numero, _ctx.Actor, motivo)))
            _ctx.Avisar(numero.Length == 0 ? "Número de factura quitado" : $"Número de factura {numero} en {Formatos.Plural(apuntes.Count, "apunte", "apuntes")}");
    }

    private async Task CambiarConceptoAsync()
    {
        if (_sel.Count == 0) return;
        var apuntes = _sel.ToList();
        if (_dialogos.PedirTexto("Cambiar el concepto", $"Se cambia en {Formatos.Plural(apuntes.Count, "apunte seleccionado", "apuntes seleccionados")}, también de asientos automáticos (si después se regenera su origen, vuelve el concepto original). Vacío deja la descripción del asiento.",
                "Concepto", UnApunte?.ConceptoPropio ?? apuntes[0].Concepto, 255, pideMotivo: true, valorObligatorio: false) is not { } r) return;
        if (await Escribir("No he podido cambiar el concepto", () => _ctx.Motor.CambiarConceptoApuntesAsync(apuntes.Select(l => l.IdApunte).ToList(), r.Valor, _ctx.Actor, r.Motivo)))
            _ctx.Avisar($"Concepto cambiado en {Formatos.Plural(apuntes.Count, "apunte", "apuntes")}");
    }

    private async Task CambiarCuentaAsync()
    {
        if (_sel.Count == 0 || !AccionCuenta.Habilitada) return;
        var apuntes = _sel.ToList();
        var ejercicio = Contexto(apuntes[0].Asiento.Ejercicio);
        var actual = apuntes[0].Cuenta;
        if (_dialogos.ElegirCuenta(ejercicio, $"Cambiar la subcuenta {actual} de {Formatos.Plural(apuntes.Count, "apunte", "apuntes")}", null) is not { } nueva) return;
        if (nueva.Codigo == actual) { _ctx.Avisar("Has elegido la misma subcuenta: no cambia nada."); return; }
        var automaticos = apuntes.Any(l => l.Asiento.EsAutomatico);
        if (_dialogos.PedirMotivo("Motivo del cambio de subcuenta",
                $"{Formatos.Plural(apuntes.Count, "apunte pasa", "apuntes pasan")} de {actual} a {nueva.Codigo} {nueva.Nombre}." +
                (automaticos ? " Hay apuntes de asientos automáticos: quedarán marcados como modificados a mano y, si se regenera su origen, el cambio se pierde." : "")) is not { } motivo) return;
        if (await Escribir("No he podido cambiar la subcuenta", () => _ctx.Motor.CambiarCuentaApuntesAsync(ejercicio, apuntes.Select(l => l.IdApunte).ToList(), nueva.Codigo, _ctx.Actor, motivo)))
            _ctx.Avisar($"{Formatos.Plural(apuntes.Count, "apunte pasado", "apuntes pasados")} a {nueva.Codigo} {nueva.Nombre}");
    }

    private async Task AsignarCentroAsync()
    {
        if (_sel.Count == 0) return;
        var apuntes = _sel.ToList();
        if (_dialogos.ElegirCentro(_centros, $"Se asigna a {Formatos.Plural(apuntes.Count, "apunte seleccionado", "apuntes seleccionados")}, también de asientos automáticos y de trimestres cerrados (el centro solo cuenta para la prorrata, que se calcula a final de año).") is not { } r) return;
        if (await Escribir("No he podido asignar el centro de coste",
                () => _ctx.Motor.CambiarDatosApuntesAsync(apuntes.Select(l => l.IdApunte).ToList(), new CambioDatosApunte { CambiaCentro = true, IdCentroCoste = r.Centro.Id }, _ctx.Actor, r.Motivo)))
            _ctx.Avisar($"Centro de coste «{r.Centro.Texto}» en {Formatos.Plural(apuntes.Count, "apunte", "apuntes")}");
    }

    private async Task EliminarAsync()
    {
        var asientos = AsientosSel;
        if (asientos.Count == 0 || !AccionEliminar.Habilitada) return;
        if (_dialogos.ConfirmarBorrado(asientos) is not { } motivo) return;
        var borrados = 0;
        var errores = new List<string>();
        foreach (var a in asientos)
        {
            try
            {
                var r = await _ctx.Motor.EliminarManualAsync(a.IdAsiento, a.Version, _ctx.Actor, motivo);
                if (r.EsCorrecto) borrados++;
                else errores.Add($"Asiento {a.IdAsiento}: " + string.Join(" ", r.Errores.Select(e => e.TextoOperador())));
            }
            catch (Exception ex)
            {
                errores.Add($"Asiento {a.IdAsiento}: " + (ex is Datos.Persistencia.ExcepcionDatos d ? d.Error.TextoOperador() : $"la base de datos ha respondido: {ex.Message}"));
            }
        }
        await CargarAsync();
        if (errores.Count > 0)
            _dialogos.Error(borrados == 0 ? "No he eliminado ningún asiento" : $"He eliminado {Formatos.Plural(borrados, "asiento", "asientos")}, pero no todos",
                string.Join(Environment.NewLine + Environment.NewLine, errores));
        if (borrados > 0) _ctx.Avisar($"{Formatos.Plural(borrados, "asiento eliminado", "asientos eliminados")}: su copia está en «Asientos borrados…»");
    }

    private Task<bool> Escribir(string titulo, Func<Task<Resultado<Guid>>> cambio, bool recargar = true) => EscribirAsync(titulo, async () => (Resultado)await cambio(), recargar);

    private async Task<bool> EscribirAsync(string titulo, Func<Task<Resultado>> cambio, bool recargar)
    {
        try
        {
            var r = await cambio();
            if (!r.EsCorrecto)
            {
                _dialogos.Error(titulo, string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return false;
            }
        }
        catch (Datos.Persistencia.ExcepcionDatos ex)
        {
            _dialogos.Error(titulo, ex.Error.TextoOperador() + " No se ha guardado nada.");
            if (recargar) await CargarAsync();
            return false;
        }
        catch (Exception ex)
        {
            _dialogos.Error(titulo, $"La base de datos ha respondido: {ex.Message} No se ha guardado nada. Vuelve a leer el diario (F5) y repite el cambio.");
            return false;
        }
        if (recargar) await CargarAsync();
        return true;
    }

    // ---------- ficha ----------

    public bool FichaVacia => _sel.Count == 0;
    public bool FichaVarios => AsientosSel.Count > 1;
    public bool FichaAsiento => UnAsiento is not null;
    public string TituloFicha => UnAsiento is { } a ? $"Asiento {a.IdAsiento}" : FichaVarios ? $"{Formatos.Plural(_sel.Count, "apunte", "apuntes")} de {AsientosSel.Count} asientos" : "Ficha del asiento";
    public string OrigenFicha => UnAsiento?.Origen ?? "";
    public IReadOnlyList<DatoFichaDiario> DatosFicha { get; private set; } = [];
    public string? InfoFicha { get; private set; }
    public bool HayInfoFicha => InfoFicha is not null;
    public string TotalesFicha { get; private set; } = "";
    public IReadOnlyList<LineaFichaDiario> ApuntesFicha { get; private set; } = [];
    public IReadOnlyList<SumaCuentaDiario> SumasFicha { get; private set; } = [];
    public IReadOnlyList<CambioFichaDiario> Historial => _historial;
    public bool HayHistorial => _historial.Count > 0;

    private void ActualizarFicha()
    {
        var a = UnAsiento;
        if (a is null)
        {
            DatosFicha = [];
            InfoFicha = null;
            ApuntesFicha = [];
            TotalesFicha = "";
            SumasFicha = _sel.GroupBy(l => l.Cuenta, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new SumaCuentaDiario(g.Key, g.First().NombreCuenta, g.Sum(l => l.Debe), g.Sum(l => l.Haber), g.Count())).ToList();
            _historial = [];
            _historialDe = 0;
        }
        else
        {
            var datos = new List<DatoFichaDiario> { new("Fecha", Formatos.Fecha(a.Fecha)), new("Descripción", a.Descripcion) };
            if (a.Referencia is { } r) datos.Add(new("Origen", r));
            if (a.Plantilla is { } p) datos.Add(new("Plantilla", p));
            if (a.Notas is { } n) datos.Add(new("Notas", n));
            if (a.FechaDocumento is { } fd) datos.Add(new("Fecha del documento", Formatos.Fecha(fd)));
            if (a.GrabadoEl is { } g) datos.Add(new("Grabado el", $"{Formatos.Fecha(g)} · {a.OperadorCreacion}"));
            if (a.ModificadoEl is { } m) datos.Add(new("Modificado el", $"{Formatos.Fecha(m)} · {a.OperadorModificacion}"));
            if (a.BloqueoPeriodo is { } b) datos.Add(new("Bloqueo", "🔒 " + b));
            DatosFicha = datos;
            InfoFicha = !a.EsAutomatico ? null
                : $"Asiento automático de {a.Origen}. Concepto, documento, vencimiento, centro y punteo se cambian aquí. Cuentas e importes, con «Modificar el asiento…», salvo los apuntes de la 572."
                  + (a.ConLibroIva ? " Las líneas de IVA y los importes no: van con el libro de IVA y se corrigen en la factura." : "")
                  + (a.CorregidoAMano ? " Ya se ha modificado a mano: si regeneras su origen, el cambio se pierde." : "");
            var marcados = _sel.ToHashSet();
            ApuntesFicha = a.Apuntes.Select(l => new LineaFichaDiario(l, marcados.Contains(l))).ToList();
            TotalesFicha = a.TotalDebe == a.TotalHaber
                ? $"Apuntes · Debe {Formatos.Numero(a.TotalDebe)} = Haber {Formatos.Numero(a.TotalHaber)}"
                : $"Apuntes · Debe {Formatos.Numero(a.TotalDebe)} ≠ Haber {Formatos.Numero(a.TotalHaber)}";
            SumasFicha = [];
            if (_historialDe != a.IdAsiento) { _historial = []; _ = CargarHistorialAsync(a.IdAsiento); }
        }
        if (_editado != UnApunte || !FichaCambiada) CargarEdicion(UnApunte);
        Avisar(nameof(FichaVacia), nameof(FichaVarios), nameof(FichaAsiento), nameof(TituloFicha), nameof(OrigenFicha), nameof(DatosFicha), nameof(InfoFicha), nameof(HayInfoFicha),
            nameof(TotalesFicha), nameof(ApuntesFicha), nameof(SumasFicha), nameof(Historial), nameof(HayHistorial));
    }

    private async Task CargarHistorialAsync(int idAsiento)
    {
        _historialDe = idAsiento;
        try
        {
            var cambios = await _ctx.Motor.RegistroCambiosAsync(idAsiento);
            if (_historialDe != idAsiento) return;
            _historial = cambios.GroupBy(c => c.IdLote).Select(g =>
            {
                var primero = g.First();
                var altas = g.Count(c => c.Operacion == "ALTA");
                var texto = altas == g.Count() ? "Alta del asiento" : primero.Motivo;
                var detalle = string.Join("\n", g.Take(25).Select(c => c.Operacion switch
                {
                    "ALTA" => $"Alta del apunte {c.Orden}: {c.ValorNuevo}",
                    "BAJA" => (c.Campo == "Asiento" ? "Baja del asiento" : $"Baja del apunte {c.Orden}") + $": {c.ValorAntiguo}",
                    _ => $"Apunte {c.Orden} · {NombreCampo(c.Campo)}: «{c.ValorAntiguo}» → «{c.ValorNuevo}»",
                })) + (g.Count() > 25 ? $"\n… y {g.Count() - 25} más" : "");
                return new CambioFichaDiario(primero.FechaHora, primero.Operador, texto, $"{primero.Motivo}\n{detalle}");
            }).OrderByDescending(c => c.FechaHora).Take(40).ToList();
        }
        catch (Exception ex)
        {
            _historial = [new CambioFichaDiario(DateTime.Now, "", "No he podido leer el historial", ex.Message)];
        }
        Avisar(nameof(Historial), nameof(HayHistorial));
    }

    private static string NombreCampo(string campo) => campo switch
    {
        "CuentaContable" => "subcuenta", "ImporteDebe" => "Debe", "ImporteHaber" => "Haber", "Descripcion" => "concepto", "NumeroDocumento" => "documento",
        "FechaVencimiento" => "vencimiento", "IdCentroCoste" => "centro de coste", "Punteado" => "punteado", "IdCliente" => "cliente", "IdProveedor" => "proveedor", _ => campo,
    };

    // ---------- edición rápida del apunte (un apunte seleccionado) ----------

    public bool HayEdicion => _editado is not null;
    public string TituloEdicion => _editado is { } l ? $"Apunte {l.Orden} · {l.Cuenta} {l.NombreCuenta}" : "";
    /// <summary>En un ejercicio cerrado solo se cambian el documento y el punteo.</summary>
    public bool TextosEditables => _editado is { Asiento.Ejercicio.Cerrado: false };
    public string AyudaTextos => _editado is null ? "" : TextosEditables ? "" : $" (no se cambia: el ejercicio {_editado.Asiento.Ejercicio.Ejercicio} está cerrado)";
    public IReadOnlyList<CentroDiario> CentrosFicha => _editado is { IdCentroCoste: int id } && _centros.All(c => c.Id != id)
        ? [.. _centros, new CentroDiario(new CentroCoste(id, _ctx.Trabajo.IdSociedad, null, "", _editado.CentroCoste, false, false, 0, ""))]
        : _centros;
    public string EdConcepto { get => _edConcepto; set { if (Fijar(ref _edConcepto, value ?? "")) AvisarEdicion(); } }
    public string EdDocumento { get => _edDocumento; set { if (Fijar(ref _edDocumento, value ?? "")) AvisarEdicion(); } }
    public string EdVencimiento { get => _edVencimiento; set { if (Fijar(ref _edVencimiento, value ?? "")) AvisarEdicion(); } }
    public CentroDiario? EdCentro { get => _edCentro; set { if (Fijar(ref _edCentro, value)) AvisarEdicion(); } }
    public bool EdPunteado { get => _edPunteado; set { if (Fijar(ref _edPunteado, value)) AvisarEdicion(); } }

    private void CargarEdicion() => CargarEdicion(_editado);

    private void CargarEdicion(ApunteDiario? apunte)
    {
        _editado = apunte;
        _edConcepto = apunte?.Concepto ?? "";
        _edDocumento = apunte?.Documento ?? "";
        _edVencimiento = apunte?.Vencimiento is { } v ? Formatos.Fecha(v) : "";
        Avisar(nameof(CentrosFicha));
        _edCentro = CentrosFicha.FirstOrDefault(c => c.Id == apunte?.IdCentroCoste) ?? CentrosFicha[0];
        _edPunteado = apunte?.Punteado ?? false;
        Avisar(nameof(HayEdicion), nameof(TituloEdicion), nameof(TextosEditables), nameof(AyudaTextos), nameof(EdConcepto), nameof(EdDocumento), nameof(EdVencimiento), nameof(EdCentro), nameof(EdPunteado));
        AvisarEdicion();
    }

    private void AvisarEdicion() => Avisar(nameof(FichaCambiada), nameof(PuedeGuardarFicha), nameof(AyudaGuardarFicha), nameof(TieneCambios));

    /// <summary>Lo que cambia la ficha respecto al apunte grabado, o null si la fecha de vencimiento no se entiende.</summary>
    private CambioDatosApunte? CambioFicha()
    {
        if (_editado is not { } l) return new CambioDatosApunte();
        if (!LeerFecha(_edVencimiento, out var vencimiento)) return null;
        var concepto = _edConcepto.Trim();
        var documento = _edDocumento.Trim();
        return new CambioDatosApunte
        {
            CambiaConcepto = concepto != l.Concepto, Concepto = concepto == l.Asiento.Descripcion ? null : concepto,
            CambiaDocumento = documento != (l.Documento ?? ""), Documento = documento,
            CambiaVencimiento = vencimiento != l.Vencimiento, Vencimiento = vencimiento,
            CambiaCentro = _edCentro?.Id != l.IdCentroCoste, IdCentroCoste = _edCentro?.Id,
            Punteado = _edPunteado != l.Punteado ? _edPunteado : null,
        };
    }

    public bool FichaCambiada => _editado is not null && CambioFicha() is not { Vacio: true };
    public bool PuedeGuardarFicha => FichaCambiada && CambioFicha() is not null;
    public string AyudaGuardarFicha => !FichaCambiada ? "No hay cambios que guardar"
        : CambioFicha() is null ? "La fecha de vencimiento no se entiende: escribe dd/mm/aaaa, o déjala vacía"
        : "Guarda los cambios del apunte por el motor de asientos, que los anota en el historial (Ctrl+G); pide el motivo salvo que solo cambie el punteo";

    /// <summary>La ficha tiene cambios sin guardar (la pestaña lo enseña y pregunta al cerrar).</summary>
    public bool TieneCambios => FichaCambiada;
    public IReadOnlyList<string> DescribirCambios() => FichaCambiada && _editado is { } l ? [$"Cambios sin guardar en el apunte {l.Orden} del asiento {l.Asiento.IdAsiento}"] : [];
    public void DescartarFicha() => CargarEdicion();

    public async Task<bool> GuardarFichaAsync()
    {
        if (_editado is not { } l || CambioFicha() is not { Vacio: false } cambio) return !FichaCambiada;
        string? motivo = null;
        if (!cambio.SoloPunteo)
        {
            var que = string.Join(", ", new[] { cambio.CambiaConcepto ? "concepto" : null, cambio.CambiaDocumento ? "documento" : null, cambio.CambiaVencimiento ? "vencimiento" : null,
                cambio.CambiaCentro ? "centro de coste" : null, cambio.Punteado is not null ? "punteo" : null }.Where(t => t is not null));
            motivo = _dialogos.PedirMotivo($"Motivo del cambio del apunte {l.Orden} del asiento {l.Asiento.IdAsiento}", $"Cambia: {que}. El motivo queda en el historial del asiento.");
            if (motivo is null) return false;
        }
        // Tras guardar se relee: la ficha vuelve a cargarse con lo grabado.
        _editado = null;
        var hecho = await Escribir("No he podido guardar el apunte", () => _ctx.Motor.CambiarDatosApuntesAsync([l.IdApunte], cambio, _ctx.Actor, motivo));
        if (hecho) _ctx.Avisar($"Apunte {l.Orden} del asiento {l.Asiento.IdAsiento} guardado");
        else { _editado = l; AvisarEdicion(); }
        return hecho;
    }

    // ---------- cabecera: borrados y exportar ----------

    private async Task VerBorradosAsync()
    {
        if (_trabajo is null) return;
        try
        {
            var ids = new[] { _trabajo, _anterior }.Where(e => e is not null).Select(e => e!.IdSociedadEjercicio).ToList();
            var borrados = await _ctx.Motor.AsientosBorradosAsync(ids);
            var nombres = _datos.Asientos.SelectMany(a => a.Apuntes).GroupBy(l => (l.Asiento.Ejercicio.IdSociedadEjercicio, l.Cuenta)).ToDictionary(g => g.Key, g => g.First().NombreCuenta);
            _dialogos.VerBorrados(borrados, (b, p) => nombres.GetValueOrDefault((b.IdSociedadEjercicio, p.Cuenta), ""),
                _anterior is null ? _trabajo.Ejercicio.ToString(CultureInfo.InvariantCulture) : $"{_anterior.Ejercicio} y {_trabajo.Ejercicio}");
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido leer los asientos borrados", $"La base de datos ha respondido: {ex.Message} Vuelve a intentarlo.");
        }
    }

    private IReadOnlyList<ColumnaDiario> ColumnasExportacion()
    {
        var claves = ColumnasVisibles?.Invoke() ?? ColumnasDiario.Todas.Where(c => !c.OcultaDeFabrica).Select(c => c.Clave).ToList();
        var columnas = claves.Select(k => ColumnasDiario.Todas.FirstOrDefault(c => c.Clave == k)).Where(c => c is not null).Select(c => c!).ToList();
        return columnas.Count > 0 ? columnas : ColumnasDiario.Todas.Where(c => !c.OcultaDeFabrica).ToList();
    }

    public CabeceraExportacionDiario CabeceraExportacion(DateTime ahora)
    {
        var anios = _vista.Asientos.Select(a => a.Asiento.Ejercicio.Ejercicio).Distinct().Order().ToList();
        return new(_ctx.RazonSocial, anios.Count == 0 ? EjercicioTrabajo.ToString(CultureInfo.InvariantCulture) : string.Join(" y ", anios),
            _desde is { } d && _hasta is { } h ? $"del {Formatos.Fecha(d)} al {Formatos.Fecha(h)}" : TextoPeriodo(), string.Join(" · ", Fichas.Select(f => f.Texto)), _porAsiento, _totalesMes, ahora);
    }

    /// <summary>«Exportar…»: todas las filas que cumplen el filtro, con las columnas visibles y sus totales, a PDF o a Excel.</summary>
    public async Task ExportarAsync()
    {
        if (_vista.NumeroApuntes == 0) return;
        var cabecera = CabeceraExportacion(DateTime.Now);
        var n = _porAsiento ? _vista.ApuntesAsientos : _vista.NumeroApuntes;
        if (_dialogos.ElegirExportacion($"Todas las filas que cumplen el filtro ({cabecera.Periodo}; {Formatos.Plural(n, "apunte", "apuntes")}), con las columnas que se ven y sus totales, de una vez.", HtmlAPdf is not null) is not { } formato) return;
        var nombre = $"Diario {_ctx.RazonSocial} {cabecera.Ejercicios} {cabecera.Generado:yyyyMMdd-HHmm}";
        nombre = string.Concat(nombre.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var pdf = formato == FormatoExportacionDiario.Pdf;
        if (_dialogos.PedirRuta(pdf ? "Exportar el Diario a PDF" : "Exportar el Diario a Excel", nombre + (pdf ? ".pdf" : ".xlsx"), pdf ? "Documento PDF (*.pdf)|*.pdf" : "Libro de Excel (*.xlsx)|*.xlsx") is not { } ruta) return;
        var columnas = ColumnasExportacion();
        var vista = _vista;
        var lista = !_porAsiento ? OrdenLista?.Invoke() : null;
        _ctx.Avisar($"Generando el Diario en {(pdf ? "PDF" : "Excel")}…");
        try
        {
            if (pdf)
            {
                var html = ExportacionDiario.Html(cabecera, columnas, vista, lista);
                var bytes = await Task.Run(() => HtmlAPdf!(html));
                await System.IO.File.WriteAllBytesAsync(ruta, bytes);
            }
            else
            {
                var apuntes = lista ?? ExportacionDiario.Apuntes(vista, cabecera.PorAsiento).ToList();
                var exportables = columnas.Select(c => new ColumnaExportable(c.Titulo, o => c.Clave == ColumnasDiario.Punteado ? (((ApunteDiario)o).Punteado ? "Sí" : "") : c.Valor((ApunteDiario)o))).ToList();
                var totales = columnas.Select((c, i) => (object?)(c.Clave switch
                {
                    ColumnasDiario.Debe => apuntes.Sum(a => a.Debe),
                    ColumnasDiario.Haber => apuntes.Sum(a => a.Haber),
                    _ => i == 0 ? "Totales" : null,
                })).ToList();
                await Task.Run(() => ExportadorExcel.Exportar(ruta, exportables, apuntes, "Diario", ExportacionDiario.TituloHoja(cabecera), totales));
            }
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido generar el fichero",
                ex is System.IO.IOException or UnauthorizedAccessException
                    ? $"No he podido escribir {ruta}: {ex.Message} Si el fichero está abierto en otro programa, ciérralo y vuelve a exportar, o elige otra carpeta."
                    : ex.Message);
            return;
        }
        _ctx.Avisar($"Diario exportado a {System.IO.Path.GetFileName(ruta)} ({Formatos.Plural(n, "apunte", "apuntes")})");
        AbrirFichero?.Invoke(ruta);
    }
}
