using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Ratios;
using ProConta.Inicio.Salud;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Graficos;
using ProConta.UI.Base.Modelos;
using ProConta.UI.Base.Preferencias;

namespace ProConta.Inicio.UI;

/// <summary>Sociedad y ejercicio de trabajo de la pantalla, con el aviso breve de la barra de estado.</summary>
public sealed record ContextoInicio(string IdSociedad, int Ejercicio, string RazonSocial, Action<string> Avisar);

/// <summary>
/// Pantallas que se abren al pulsar un indicador. Solo se conectan las que ya existen en el contenedor; un indicador sin
/// pantalla no es pulsable.
/// </summary>
public sealed record AccionesInicio(
    Action? AbrirFacturas = null, Action? AbrirPerdidasYGanancias = null, Action? AbrirBancos = null, Action<IReadOnlyList<string>>? AbrirMayor = null,
    Action? AbrirBalanceSituacion = null)
{
    // Pantallas donde se resuelve la salud de la contabilidad (fase 3).
    public Action? AbrirVentas { get; init; }
    public Action? AbrirLineasN43 { get; init; }
    public Action? AbrirFacturasCompra { get; init; }
    /// <summary>Compras › Facturas recurrentes pendientes (compras/facturas-recurrentes-pendientes).</summary>
    public Action? AbrirFacturasRecurrentes { get; init; }
    public Action? AbrirDiario { get; init; }
    /// <summary>Contabilidad › Procesos contables › Liquidación de IVA (contabilidad/procesos/liquidacion-iva).</summary>
    public Action? AbrirLiquidacionIva { get; init; }
    /// <summary>Contabilidad › Procesos contables › Fin de ejercicio › Amortización de activos (contabilidad/procesos/amortizacion-de-activos).</summary>
    public Action? AbrirAmortizacion { get; init; }
    /// <summary>Contabilidad › Procesos contables › Fin de ejercicio › Céntimos residuales (contabilidad/procesos/centimos-residuales).</summary>
    public Action? AbrirCentimosResiduales { get; init; }
    public Action? AbrirTerceros { get; init; }
    public Action? AbrirNominas { get; init; }
    /// <summary>Configuración › Parámetros y tablas en una tabla concreta (por ejemplo «ejercicios»).</summary>
    public Action<string>? AbrirParametros { get; init; }
    /// <summary>Ventas › Facturas programadas (agenda, fase 4).</summary>
    public Action? AbrirProgramadas { get; init; }
    /// <summary>Contabilidad › Modelos tributarios con un periodo marcado (modelo y periodo), desde los plazos de la agenda y de la salud.</summary>
    public Action<string?, string?>? AbrirModelosTributarios { get; init; }
    /// <summary>
    /// Botón del aviso «Comisiones sin compensar» (facturacion/reservas/compensacion-comision): compensa las pendientes tras confirmarlo;
    /// true si ha hecho algo y hay que volver a revisar la salud.
    /// </summary>
    public Func<Task<bool>>? CompensarComisiones { get; init; }
}

/// <summary>Bloque de la pantalla que se puede ocultar con «Personalizar…».</summary>
public sealed record BloqueInicio(string Clave, string Nombre, string Ayuda);

/// <summary>Cuenta bancaria en «Personalizar…», con su marca de inversión (no es tesorería operativa).</summary>
public sealed record CuentaBancoInicio(string Nombre, string Cuenta, bool Inversion);

/// <summary>Lo elegido en «Personalizar…»: bloques ocultos y cuentas contables de los bancos marcados como inversión.</summary>
public sealed record EleccionInicio(IReadOnlySet<string> Ocultos, IReadOnlySet<string> Inversiones);

/// <summary>Diálogos de la pantalla de inicio (para poder probar el modelo de vista sin ventanas).</summary>
public interface IDialogosInicio
{
    /// <summary>Elige qué bloques se ven y qué cuentas bancarias son de inversión; null si se cancela.</summary>
    EleccionInicio? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos, IReadOnlyList<CuentaBancoInicio> bancos);

    /// <summary>Muestra la ficha de un ratio.</summary>
    void VerRatio(FichaRatioVM ficha);

    /// <summary>«¿Por qué aparece?» de un elemento de salud.</summary>
    void PorQue(PorQueSaludVM porQue) { }

    /// <summary>Copia un texto al portapapeles.</summary>
    void Copiar(string texto) { }
}

/// <summary>Opción del desplegable «Comparar con».</summary>
public sealed record OpcionComparar(string Texto, int? Ejercicio);

/// <summary>Cómo se ve el comparativo: verde si es bueno para la sociedad, rojo si es malo, gris si no se juzga.</summary>
public enum Tono { Neutro, Bueno, Malo }

/// <summary>Uno de los seis indicadores de la cabecera.</summary>
public sealed class IndicadorVM(string titulo, string valor, string? variacion, Tono tono, string? comparado, string? proyeccion, IReadOnlyList<decimal?> evolucion,
    string ayuda, ICommand? comando)
{
    public string Titulo { get; } = titulo;
    public string Valor { get; } = valor;
    public string? Variacion { get; } = variacion;
    public Tono Tono { get; } = tono;
    public string? Comparado { get; } = comparado;
    public string? Proyeccion { get; } = proyeccion;
    public IReadOnlyList<decimal?> Evolucion { get; } = evolucion;
    public string Ayuda { get; } = ayuda;
    public ICommand? Comando { get; } = comando;
    public bool Pulsable => Comando is not null;
    public bool HayProyeccion => Proyeccion is not null;
    public bool HayComparado => Variacion is not null || Comparado is not null;
}

/// <summary>Muestra de la leyenda de un gráfico: color y si es sólida, rayada (proyección) o a trazos.</summary>
public sealed record ElementoLeyenda(string Texto, string Color, bool Rayado = false, bool Discontinuo = false)
{
    public bool Solido => !Rayado && !Discontinuo;
}

/// <summary>Fila horizontal con barra y marca de referencia (actividades y gastos).</summary>
public sealed record FilaBarraVM(string Nombre, string Color, string Valor, string? Variacion, Tono Tono, decimal Actual, decimal? Referencia, decimal Maximo, string Ayuda,
    IReadOnlyList<string>? Cuentas = null);

/// <summary>Fila de la vista Tabla de facturación.</summary>
public sealed record FilaFacturacionVM(string Mes, string Facturado, string Proyectado, string Comparado, string Variacion, string Acumulado, string AcumuladoComparado,
    decimal? ImporteFacturado, decimal? ImporteProyectado, decimal? ImporteComparado, decimal ImporteAcumulado, decimal? ImporteAcumuladoComparado);

/// <summary>Cliente de la tabla de pendiente de cobro.</summary>
public sealed record ClienteVM(string Cuenta, string Nombre, string Saldo, string MasDe30, decimal ImporteSaldo, decimal ImporteMasDe30);

/// <summary>Banco con su saldo y la fecha de su último extracto.</summary>
public sealed record BancoVM(string Nombre, string? Cuenta, string Saldo, string Extracto, string? Aviso, string Ayuda);

/// <summary>Tramo de antigüedad en la leyenda.</summary>
public sealed record LeyendaTramoVM(string Texto, string Importe, string Color);

/// <summary>
/// Pantalla de inicio (pantalla-de-inicio, fase 1): indicadores con comparativo y proyección, facturación por mes y por
/// actividad, ingresos y gastos, gastos por naturaleza, pendiente de cobro por antigüedad y tesorería. Solo lee.
/// </summary>
public sealed class InicioVM : ModeloVista
{
    public const string ClaveOcultos = "inicio.bloquesOcultos";

    /// <summary>Preferencia con las cuentas contables de los bancos marcados como inversión, por sociedad.</summary>
    public static string ClaveInversiones(string idSociedad) => "inicio.cuentasInversion." + idSociedad;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly string[] Meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];
    private static readonly string[] MesesLargos = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    public static IReadOnlyList<BloqueInicio> Bloques { get; } =
    [
        new("indicadores", "Indicadores principales", "Facturación, resultado, tesorería, pendiente de cobro y de pago e IVA estimado del trimestre"),
        new("facturacion", "Facturación", "Facturación por mes frente al ejercicio comparado, con la proyección de cierre y el reparto por actividad"),
        new("ratios", "Ratios", "Los 12 ratios de liquidez, solvencia, rentabilidad y gestión con su rango de referencia, a fin del último mes completo"),
        new("tesoreria", "Tesorería", "Tesorería operativa a fin de cada uno de los últimos 12 meses, saldo por banco y cuentas de inversión aparte"),
        new("ingresos", "Ingresos y gastos", "Ingresos y gastos de cada mes del ejercicio"),
        new("cobros", "Pendiente de cobro", "Lo que deben los clientes, por antigüedad, y los 5 clientes con más saldo"),
        new("gastos", "Gastos por naturaleza", "Gasto acumulado por cuenta de 3 dígitos, con el ejercicio comparado"),
        new("agenda", "Agenda", "Próximos vencimientos en orden de fecha (90 días): plazos del 303 y del 390, cambios de tipo de IVA, crear el ejercicio y facturas programadas"),
        new("ocupacion", "Ocupación turística", "Noches ocupadas entre noches disponibles por mes y por alojamiento, frente al ejercicio comparado. Solo aparece si la sociedad tiene reservas"),
        new("salud", "Salud de la contabilidad", "Tareas con plazo, avisos de cosas a corregir, estado de los periodos y comprobaciones, con «Lo más urgente» arriba"),
    ];

    private readonly ServicioInicio _servicio;
    private readonly ContextoInicio _ctx;
    private readonly IDialogosInicio _dialogos;
    private readonly AccionesInicio _acciones;
    private readonly IPreferenciasOperador _prefs;
    private HashSet<string> _ocultos;
    private HashSet<string> _inversiones;
    private int _carga;
    private bool _iniciado;
    private PanelInicio? _panel;

    public InicioVM(ServicioInicio servicio, ContextoInicio contexto, IDialogosInicio dialogos, AccionesInicio? acciones = null, IPreferenciasOperador? preferencias = null,
        ServicioSalud? salud = null, ProConta.Inicio.Bloques.ServicioBloques? bloques = null)
    {
        _servicio = servicio;
        _ctx = contexto;
        _dialogos = dialogos;
        _acciones = acciones ?? new AccionesInicio();
        _prefs = preferencias ?? PreferenciasOperador.Actual;
        _ocultos = (_prefs.Leer<List<string>>(ClaveOcultos) ?? []).ToHashSet(StringComparer.Ordinal);
        _inversiones = (_prefs.Leer<List<string>>(ClaveInversiones(contexto.IdSociedad)) ?? []).ToHashSet(StringComparer.Ordinal);
        ActualizarCommand = new ComandoDelegado(() => _ = ActualizarAsync(), () => !Cargando);
        PersonalizarCommand = new ComandoDelegado(() => _ = PersonalizarAsync());
        ReintentarCommand = new ComandoDelegado(() => _ = ActualizarAsync());
        AbrirClienteCommand = new ComandoDelegado(p => { if (p is ClienteVM c) _acciones.AbrirMayor?.Invoke([c.Cuenta]); }, p => p is ClienteVM && _acciones.AbrirMayor is not null);
        AbrirBancoCommand = new ComandoDelegado(p => { if (p is BancoVM { Cuenta: { } c }) _acciones.AbrirMayor?.Invoke([c]); }, p => p is BancoVM { Cuenta: not null } && _acciones.AbrirMayor is not null);
        AbrirGastoCommand = new ComandoDelegado(p => { if (p is FilaBarraVM { Cuentas: { Count: > 0 } c }) _acciones.AbrirMayor?.Invoke(c); }, p => p is FilaBarraVM { Cuentas.Count: > 0 } && _acciones.AbrirMayor is not null);
        Subtitulo = $"{contexto.RazonSocial} · ejercicio {contexto.Ejercicio}";
        if (salud is not null)
        {
            Salud = new SaludVM(salud, contexto, _acciones, dialogos, () => _inversiones.ToHashSet(StringComparer.Ordinal));
            Salud.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SaludVM.HayUrgente)) Avisar(nameof(VerUrgente)); };
        }
        if (bloques is not null)
        {
            Fase4 = new BloquesFase4VM(bloques, contexto, _acciones);
            Fase4.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BloquesFase4VM.HayOcupacion)) Avisar(nameof(VerOcupacion), nameof(TodoOculto)); };
        }
    }

    /// <summary>Ocupación turística y agenda (fase 4); null si no se han conectado.</summary>
    public BloquesFase4VM? Fase4 { get; }

    /// <summary>La franja «Lo más urgente»: solo con la salud visible y algo grave.</summary>
    public bool VerUrgente => VerSalud && Salud!.HayUrgente;

    /// <summary>Salud de la contabilidad (fase 3); null si no se ha conectado.</summary>
    public SaludVM? Salud { get; }

    // ------------------------------------------------------------ cabecera y estado

    public string Titulo => "Inicio";
    private string _subtitulo = "";
    public string Subtitulo { get => _subtitulo; private set => Fijar(ref _subtitulo, value); }

    public ObservableCollection<OpcionComparar> Comparables { get; } = [];

    private OpcionComparar? _comparar;
    public OpcionComparar? Comparar
    {
        get => _comparar;
        set
        {
            if (!Fijar(ref _comparar, value) || !_iniciado || value is null) return;
            _ = CargarAsync();
            if (Fase4 is not null) _ = Fase4.CompararAsync(value.Ejercicio);
        }
    }

    public string AyudaComparar => _panel?.FechaComparada is { } f
        ? $"Ejercicio con el que se comparan los indicadores y los gráficos. Se compara siempre a la misma fecha (hasta el {f:dd/MM})"
        : "Ejercicio con el que se comparan los indicadores y los gráficos, siempre a la misma fecha. «Sin comparar» quita el comparativo y la proyección";

    private EstadoVista _estado = EstadoVista.Cargando;
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }

    private string _tituloEstado = "Calculando los indicadores…";
    public string TituloEstado { get => _tituloEstado; private set => Fijar(ref _tituloEstado, value); }

    private string _mensajeEstado = "Leo los apuntes y las facturas del ejercicio y del comparado.";
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }

    private bool _cargando;
    public bool Cargando { get => _cargando; private set { if (Fijar(ref _cargando, value)) CommandManager.InvalidateRequerySuggested(); } }

    private string? _aviso;
    /// <summary>Aviso destacado bajo la cabecera (ejercicio sin movimientos).</summary>
    public string? Aviso { get => _aviso; private set { Fijar(ref _aviso, value); Avisar(nameof(HayAviso)); } }
    public bool HayAviso => Aviso is not null;

    public ICommand ActualizarCommand { get; }
    public ICommand PersonalizarCommand { get; }
    public ICommand ReintentarCommand { get; }
    public ICommand AbrirClienteCommand { get; }
    public ICommand AbrirBancoCommand { get; }
    public ICommand AbrirGastoCommand { get; }

    public bool VerIndicadores => !_ocultos.Contains("indicadores");
    public bool VerFacturacion => !_ocultos.Contains("facturacion");
    public bool VerRatios => !_ocultos.Contains("ratios");
    public bool VerTesoreria => !_ocultos.Contains("tesoreria");
    public bool VerIngresos => !_ocultos.Contains("ingresos");
    public bool VerCobros => !_ocultos.Contains("cobros");
    public bool VerGastos => !_ocultos.Contains("gastos");
    public bool VerSalud => Salud is not null && !_ocultos.Contains("salud");
    public bool VerAgenda => Fase4 is not null && !_ocultos.Contains("agenda");
    public bool VerOcupacion => Fase4 is { HayOcupacion: true } && !_ocultos.Contains("ocupacion");
    public bool TodoOculto => Bloques.All(b => _ocultos.Contains(b.Clave) || (b.Clave == "salud" && Salud is null) || (b.Clave == "agenda" && Fase4 is null)
                                               || (b.Clave == "ocupacion" && Fase4 is not { HayOcupacion: true }));

    // ------------------------------------------------------------ bloques

    public ObservableCollection<IndicadorVM> Indicadores { get; } = [];

    private VistaFacturacion _vista = VistaFacturacion.Mensual;
    public bool VistaMensual { get => _vista == VistaFacturacion.Mensual; set { if (value) CambiarVista(VistaFacturacion.Mensual); } }
    public bool VistaAcumulada { get => _vista == VistaFacturacion.Acumulada; set { if (value) CambiarVista(VistaFacturacion.Acumulada); } }
    public bool VistaTabla { get => _vista == VistaFacturacion.Tabla; set { if (value) CambiarVista(VistaFacturacion.Tabla); } }
    public bool VerGraficoFacturacion => _vista != VistaFacturacion.Tabla;

    private DatosGrafico? _datosFacturacion;
    public DatosGrafico? DatosFacturacion { get => _datosFacturacion; private set => Fijar(ref _datosFacturacion, value); }
    public bool FacturacionEnBarras => _vista == VistaFacturacion.Mensual;
    public bool FacturacionEnLineas => _vista == VistaFacturacion.Acumulada;
    public ObservableCollection<ElementoLeyenda> LeyendaFacturacion { get; } = [];
    public ObservableCollection<FilaFacturacionVM> FilasFacturacion { get; } = [];
    private string? _textoProyeccion;
    public string? TextoProyeccion { get => _textoProyeccion; private set => Fijar(ref _textoProyeccion, value); }
    private string? _avisoFuente;
    /// <summary>Nota cuando la facturación de algún ejercicio sale de la contabilidad (grupo 70) porque no tiene facturas.</summary>
    public string? AvisoFuente { get => _avisoFuente; private set => Fijar(ref _avisoFuente, value); }
    private string? _sinFacturas;
    public string? SinFacturas { get => _sinFacturas; private set => Fijar(ref _sinFacturas, value); }

    public ObservableCollection<FilaBarraVM> Actividades { get; } = [];
    private string _textoActividades = "";
    public string TextoActividades { get => _textoActividades; private set => Fijar(ref _textoActividades, value); }

    private DatosGrafico? _datosIngresos;
    public DatosGrafico? DatosIngresos { get => _datosIngresos; private set => Fijar(ref _datosIngresos, value); }
    private string? _sinIngresos;
    public string? SinIngresos { get => _sinIngresos; private set => Fijar(ref _sinIngresos, value); }

    public ObservableCollection<FilaBarraVM> Gastos { get; } = [];
    private string? _notaGastos;
    public string? NotaGastos { get => _notaGastos; private set => Fijar(ref _notaGastos, value); }

    private IReadOnlyList<TramoApilado> _tramos = [];
    public IReadOnlyList<TramoApilado> Tramos { get => _tramos; private set => Fijar(ref _tramos, value); }
    public ObservableCollection<LeyendaTramoVM> LeyendaTramos { get; } = [];
    public ObservableCollection<ClienteVM> Clientes { get; } = [];
    private string _textoCobros = "";
    public string TextoCobros { get => _textoCobros; private set => Fijar(ref _textoCobros, value); }
    private string? _sinCobros;
    public string? SinCobros { get => _sinCobros; private set => Fijar(ref _sinCobros, value); }

    private DatosGrafico? _datosTesoreria;
    public DatosGrafico? DatosTesoreria { get => _datosTesoreria; private set => Fijar(ref _datosTesoreria, value); }
    public ObservableCollection<BancoVM> Bancos { get; } = [];
    private string _textoBancos = "";
    public string TextoBancos { get => _textoBancos; private set => Fijar(ref _textoBancos, value); }

    public ObservableCollection<BancoVM> Inversiones { get; } = [];
    private string _textoInversiones = "";
    public string TextoInversiones { get => _textoInversiones; private set => Fijar(ref _textoInversiones, value); }
    public bool HayInversiones => Inversiones.Count > 0;

    public ObservableCollection<FamiliaRatioVM> FamiliasRatios { get; } = [];
    private string _textoRatios = "";
    public string TextoRatios { get => _textoRatios; private set => Fijar(ref _textoRatios, value); }
    private string? _notaRatios;
    public string? NotaRatios { get => _notaRatios; private set => Fijar(ref _notaRatios, value); }
    private string? _sinRatios;
    /// <summary>Estado vacío del bloque de ratios (sin balance o sin modelo de balance de situación).</summary>
    public string? SinRatios { get => _sinRatios; private set => Fijar(ref _sinRatios, value); }
    private int _ratiosBien, _ratiosVigilar, _ratiosMal;
    public string RecuentoBien => $"✓ {_ratiosBien} bien";
    public string RecuentoVigilar => $"! {_ratiosVigilar} a vigilar";
    public string RecuentoMal => $"✕ {_ratiosMal} mal";
    public bool HayBien => _ratiosBien > 0;
    public bool HayVigilar => _ratiosVigilar > 0;
    public bool HayMal => _ratiosMal > 0;

    /// <summary>Cuentas contables de los bancos marcados como inversión.</summary>
    public IReadOnlySet<string> CuentasInversion => _inversiones;

    public PanelInicio? Panel => _panel;

    // ------------------------------------------------------------ carga

    /// <summary>Primera carga: los ejercicios comparables (por defecto, el anterior) y los indicadores.</summary>
    public async Task IniciarAsync()
    {
        // La salud y los bloques de la fase 4 se cargan aparte: no retrasan los indicadores.
        var salud = Salud?.RevisarAsync() ?? Task.CompletedTask;
        await IniciarIndicadoresAsync();
        var fase4 = Fase4?.CargarAsync(Comparar?.Ejercicio) ?? Task.CompletedTask;
        await salud;
        await fase4;
    }

    private async Task IniciarIndicadoresAsync()
    {
        if (_iniciado)
        {
            await CargarAsync();
            return;
        }
        try
        {
            Cargando = true;
            var comparables = await Task.Run(() => _servicio.ComparablesAsync(_ctx.IdSociedad, _ctx.Ejercicio));
            Comparables.Clear();
            foreach (var e in comparables) Comparables.Add(new OpcionComparar(e.ToString(Es), e));
            Comparables.Add(new OpcionComparar("Sin comparar", null));
            _comparar = Comparables.FirstOrDefault(o => o.Ejercicio == _ctx.Ejercicio - 1) ?? Comparables[0];
            Avisar(nameof(Comparar));
            _iniciado = true;
        }
        catch (Exception ex)
        {
            MostrarError(ex);
            Cargando = false;
            return;
        }
        await CargarAsync();
    }

    /// <summary>«Actualizar»: vuelve a leer todo de la BD.</summary>
    public async Task ActualizarAsync()
    {
        _servicio.Olvidar();
        var salud = Salud?.RevisarAsync() ?? Task.CompletedTask;
        var fase4 = Fase4?.CargarAsync(Comparar?.Ejercicio, releer: true) ?? Task.CompletedTask;
        if (!_iniciado) await IniciarIndicadoresAsync();
        else
        {
            await CargarAsync();
            if (Estado == EstadoVista.Contenido) _ctx.Avisar($"Inicio actualizado a las {DateTime.Now:HH:mm}");
        }
        await salud;
        await fase4;
    }

    private async Task CargarAsync()
    {
        var mia = ++_carga;
        Cargando = true;
        if (_panel is null) Estado = EstadoVista.Cargando;
        try
        {
            var comparado = Comparar?.Ejercicio;
            var inversiones = _inversiones.ToHashSet(StringComparer.Ordinal);
            var panel = await Task.Run(() => _servicio.CalcularAsync(_ctx.IdSociedad, _ctx.Ejercicio, comparado, inversiones));
            if (mia != _carga) return;
            Aplicar(panel);
            Estado = EstadoVista.Contenido;
        }
        catch (Exception ex) when (mia == _carga)
        {
            MostrarError(ex);
        }
        finally
        {
            if (mia == _carga) Cargando = false;
        }
    }

    private void MostrarError(Exception ex)
    {
        TituloEstado = "No he podido calcular los indicadores";
        MensajeEstado = $"La lectura de la base de datos ha fallado: {ex.Message}. Comprueba que la base de datos está accesible (la ruta está en la barra de estado) " +
                        "y pulsa «Reintentar». Si sigue fallando, copia este texto y envíaselo a soporte.";
        Estado = EstadoVista.Error;
    }

    private void CambiarVista(VistaFacturacion v)
    {
        if (_vista == v) return;
        _vista = v;
        Avisar(nameof(VistaMensual), nameof(VistaAcumulada), nameof(VistaTabla), nameof(VerGraficoFacturacion), nameof(FacturacionEnBarras), nameof(FacturacionEnLineas));
        if (_panel is not null) AplicarFacturacion(_panel);
    }

    /// <summary>«Personalizar…»: bloques visibles y cuentas bancarias de inversión; si cambian estas, vuelve a calcular (sin releer la BD).</summary>
    internal async Task PersonalizarAsync()
    {
        IReadOnlyList<BancoN43> bancos;
        try { bancos = await _servicio.BancosAsync(_ctx.IdSociedad, _ctx.Ejercicio); }
        catch (Exception ex)
        {
            _ctx.Avisar($"No he podido leer las cuentas bancarias para marcar las de inversión: {ex.Message}. Pulsa «Actualizar» y vuelve a intentarlo.");
            bancos = [];
        }
        var cuentas = bancos.Where(b => b.CuentaContable is { Length: > 0 }).GroupBy(b => b.CuentaContable!, StringComparer.Ordinal)
            .Select(g => new CuentaBancoInicio(g.First().Nombre, g.Key, _inversiones.Contains(g.Key))).OrderBy(c => c.Cuenta, StringComparer.Ordinal).ToList();
        var r = _dialogos.Personalizar(Bloques, _ocultos, cuentas);
        if (r is null) return;
        _ocultos = r.Ocultos.ToHashSet(StringComparer.Ordinal);
        if (_ocultos.Count == 0) _prefs.Borrar(ClaveOcultos);
        else _prefs.Guardar(ClaveOcultos, _ocultos.OrderBy(x => x, StringComparer.Ordinal).ToList());
        Avisar(nameof(VerIndicadores), nameof(VerFacturacion), nameof(VerRatios), nameof(VerTesoreria), nameof(VerIngresos), nameof(VerCobros), nameof(VerGastos), nameof(VerSalud),
            nameof(VerAgenda), nameof(VerOcupacion), nameof(VerUrgente), nameof(TodoOculto));
        var inversiones = r.Inversiones.ToHashSet(StringComparer.Ordinal);
        if (inversiones.SetEquals(_inversiones)) return;
        _inversiones = inversiones;
        var clave = ClaveInversiones(_ctx.IdSociedad);
        if (_inversiones.Count == 0) _prefs.Borrar(clave);
        else _prefs.Guardar(clave, _inversiones.OrderBy(x => x, StringComparer.Ordinal).ToList());
        if (Salud is not null) _ = Salud.RevisarAsync();
        if (_iniciado) await CargarAsync();
    }

    // ------------------------------------------------------------ de los cálculos a la pantalla

    internal void Aplicar(PanelInicio p)
    {
        _panel = p;
        var hoy = p.FechaCorte == _servicio.Hoy;
        Subtitulo = p.Terminado && !hoy
            ? $"{_ctx.RazonSocial} · ejercicio {p.Ejercicio} · datos contabilizados hasta el {p.FechaCorte:dd/MM/yyyy} (ejercicio terminado)"
            : $"{_ctx.RazonSocial} · ejercicio {p.Ejercicio} · datos contabilizados hasta hoy, {p.FechaCorte:dd/MM/yyyy}";
        Aviso = p.HayMovimientos ? null
            : $"Aún no hay movimientos contabilizados ni facturas validadas en {p.Ejercicio}. Los indicadores y los gráficos se llenan con el primer asiento contabilizado " +
              $"o la primera factura validada del ejercicio. Mientras, puedes ver un ejercicio anterior cambiando el ejercicio de trabajo en la cabecera.";
        Avisar(nameof(AyudaComparar), nameof(Panel));
        AplicarIndicadores(p);
        AplicarFacturacion(p);
        AplicarRatios(p);
        AplicarActividades(p);
        AplicarIngresos(p);
        AplicarGastos(p);
        AplicarCobros(p);
        AplicarTesoreria(p);
    }

    private static string Imp(decimal v) => Formatos.Importe(Math.Round(v, 2, MidpointRounding.AwayFromZero));
    private static string Pct(decimal v) => FormatoGrafico.TextoVariacion(Math.Round(v, 1, MidpointRounding.AwayFromZero));
    private string HastaCorte(PanelInicio p) => p.FechaCorte == _servicio.Hoy ? "hasta hoy" : $"hasta el {p.FechaCorte:dd/MM/yyyy}";

    private static Tono TonoDe(decimal? variacion, bool subirEsBueno) =>
        variacion is not decimal v ? Tono.Neutro : (v >= 0) == subirEsBueno ? Tono.Bueno : Tono.Malo;

    private void AplicarIndicadores(PanelInicio p)
    {
        var c = p.Comparado;
        string? Vs(Indicador i) => c is null ? null : i.Comparado is decimal v ? $"en {c}: {Imp(v)}" : null;
        string? Var(Indicador i) => c is null ? null : i.Variacion is decimal v ? Pct(v) : i.Comparado is not null ? "sin variación calculable" : null;
        string? Proy(Indicador i) => i.Proyeccion is decimal pr ? $"Proyección de cierre: {Imp(pr)}" : null;
        string AyudaProy(Indicador i) => i.Proyeccion is decimal pr && i.TotalComparado is decimal t
            ? $" Proyección de cierre (estimación, no dato real): total de {c} ({Imp(t)}) × (acumulado actual / acumulado de {c} a la misma fecha) = {Imp(pr)}."
            : "";
        ICommand? Cmd(Action? a) => a is null ? null : new ComandoDelegado(a);
        ICommand? Mayor(params string[] cuentas) => _acciones.AbrirMayor is { } m ? new ComandoDelegado(() => m(cuentas)) : null;
        string Pulsa(ICommand? cmd, string destino) => cmd is null ? "" : $" Pulsa para abrir {destino}.";
        var comparadoTexto = c is null ? "" : $" Comparado con {c} hasta el {p.FechaComparada:dd/MM/yyyy}.";

        Indicadores.Clear();
        var f = p.Facturacion;
        var cmdF = Cmd(_acciones.AbrirFacturas);
        Indicadores.Add(new IndicadorVM("Facturación del ejercicio", Imp(f.Valor), Var(f), TonoDe(f.Variacion, true), Vs(f), Proy(f), f.Evolucion,
            $"Base imponible de las facturas de venta validadas desde el 1 de enero {HastaCorte(p)}, por fecha de factura" +
            (p.FacturacionDeContabilidad ? " (este ejercicio no tiene facturas: sale del grupo 70 de la contabilidad)" : "") + "." + comparadoTexto + AyudaProy(f) +
            Pulsa(cmdF, "Ventas › Facturas emitidas"), cmdF));

        var r = p.Resultado;
        var cmdR = Cmd(_acciones.AbrirPerdidasYGanancias);
        var eb = p.Ebitda;
        var proyEbitda = eb.Proyeccion is decimal pe ? $"EBITDA proyectado al cierre: {Imp(pe)}" : null;
        var ayudaEbitda = $" EBITDA acumulado {HastaCorte(p)}: {Imp(eb.Actual)} (grupos 7 y 6 sin el impuesto 630, las amortizaciones 68, los deterioros y provisiones 69 y 79 ni los financieros 66 y 76)." +
            (eb.Proyeccion is decimal pe2 && eb.TotalComparado is decimal etc && eb.Comparado is decimal ec
                ? $" EBITDA proyectado al cierre (estimación, no dato real): EBITDA de {c} completo ({Imp(etc)}) × (EBITDA acumulado actual / EBITDA de {c} a la misma fecha, {Imp(ec)}) = {Imp(pe2)}." +
                  " Se proyecta el EBITDA y no el resultado porque no cuenta las amortizaciones, de los principales ajustes de fin de ejercicio."
                : "");
        Indicadores.Add(new IndicadorVM("Resultado antes de impuestos", Imp(r.Valor), Var(r), TonoDe(r.Variacion, true), Vs(r), proyEbitda, r.Evolucion,
            $"Grupo 7 menos grupo 6 (sin el impuesto sobre beneficios, 630) {HastaCorte(p)}: solo asientos contabilizados, sin la regularización ni el cierre." + comparadoTexto + ayudaEbitda +
            Pulsa(cmdR, "la cuenta de pérdidas y ganancias del ejercicio en Excel"), cmdR));

        var t = p.Tesoreria;
        var cmdT = Cmd(_acciones.AbrirBancos);
        var notaInversion = p.Inversiones.Count == 0
            ? " Si alguna cuenta es de inversión (un bróker), márcala en «Personalizar…» para que no cuente aquí."
            : $" Sin las cuentas de inversión ({string.Join(", ", p.Inversiones.Select(b => b.Cuenta))}, {Imp(p.TotalInversiones)}), que se marcan en «Personalizar…».";
        Indicadores.Add(new IndicadorVM("Tesorería operativa", Imp(t.Valor), Var(t), TonoDe(t.Variacion, true), Vs(t), null, t.Evolucion,
            $"Saldo contable de las cuentas bancarias (572 y las cuentas de los bancos de Norma 43) {HastaCorte(p)}." + notaInversion + comparadoTexto + " El minigráfico es el saldo a fin de cada uno de los últimos 12 meses." +
            Pulsa(cmdT, "Bancos › Cuentas bancarias"), cmdT));

        var cb = p.PendienteCobro;
        var cmdC = Mayor("430", "431", "432", "433", "434", "435", "436");
        Indicadores.Add(new IndicadorVM("Pendiente de cobro", Imp(cb.Valor), Var(cb), TonoDe(cb.Variacion, false), Vs(cb), null, cb.Evolucion,
            $"Suma de los saldos deudores de los clientes (430 a 436) {HastaCorte(p)}; los clientes con saldo acreedor no restan." + comparadoTexto +
            Pulsa(cmdC, "el Mayor de las cuentas 430 a 436"), cmdC));

        var pg = p.PendientePago;
        var cmdP = Mayor("400", "401", "402", "403", "404", "405", "406", "407", "408", "409", "410");
        Indicadores.Add(new IndicadorVM("Pendiente de pago", Imp(pg.Valor), Var(pg), TonoDe(pg.Variacion, false), Vs(pg), null, pg.Evolucion,
            $"Suma de los saldos acreedores de proveedores y acreedores (400 a 410) {HastaCorte(p)}." + comparadoTexto +
            Pulsa(cmdP, "el Mayor de las cuentas 400 a 410"), cmdP));

        var iva = p.Iva;
        var cmdI = Mayor("472", "477");
        var ivaVar = c is null ? null : iva.Comparado is decimal ic && ic != 0 ? Pct((iva.Neto - ic) / Math.Abs(ic) * 100m) : null;
        var neto = iva.Neto;
        Indicadores.Add(new IndicadorVM($"IVA del {iva.Trimestre}T (estimado)", Imp(Math.Abs(neto)), ivaVar, Tono.Neutro,
            (neto >= 0 ? "a ingresar" : "a compensar o devolver") + $" · repercutido {Imp(iva.Repercutido)}",
            c is not null && iva.Comparado is decimal ivc ? $"{iva.Trimestre}T de {c}: {Imp(ivc)}" : null, p.EvolucionIva,
            $"IVA repercutido (477) menos soportado deducible (472, según la actividad de cada serie) del {iva.Trimestre}T, del {iva.Desde:dd/MM} al {iva.Hasta:dd/MM/yyyy}. " +
            $"Repercutido {Imp(iva.Repercutido)}, soportado deducible {Imp(iva.Soportado)}. No es la liquidación: es lo que saldría si se liquidara ahora. " +
            "Sin los asientos de liquidación (los que mueven la 4700 o la 4750). El minigráfico es el estimado de cada trimestre." + Pulsa(cmdI, "el Mayor de las cuentas 472 y 477"), cmdI));
    }

    private void AplicarFacturacion(PanelInicio p)
    {
        var anio = p.Ejercicio.ToString(Es);
        var c = p.Comparado;
        var comp = p.FacturacionComparadaMensual;
        var hayProy = p.ProyeccionMensual.Any(x => x is > 0);
        var corteMes = p.FechaCorte.Month - 1;
        AvisoFuente = (p.FacturacionDeContabilidad, p.ComparadoDeContabilidad) switch
        {
            (true, true) => $"{anio} y {c} no tienen facturas de venta validadas: su facturación sale del grupo 70 de la contabilidad.",
            (true, false) => $"{anio} no tiene facturas de venta validadas: su facturación sale del grupo 70 de la contabilidad.",
            (false, true) => $"{c} no tiene facturas de venta validadas: su facturación sale del grupo 70 de la contabilidad.",
            _ => null,
        };
        SinFacturas = p.FacturacionMensual.All(v => v is null or 0) && (comp is null || comp.All(v => v == 0))
            ? $"Aún no hay facturas en {anio}. El gráfico se llena con la primera factura validada del ejercicio."
            : null;
        var proyTotal = p.Facturacion.Proyeccion;
        TextoProyeccion = proyTotal is decimal pt && p.Facturacion.TotalComparado is decimal tc
            ? $"proyección de cierre {Imp(pt)}" + (tc != 0 ? $" ({Pct((pt - tc) / Math.Abs(tc) * 100m)} sobre {c} completo, {Imp(tc)})" : "")
            : null;

        LeyendaFacturacion.Clear();
        LeyendaFacturacion.Add(new ElementoLeyenda($"{anio} facturado", PaletaGraficos.Serie1));
        if (hayProy) LeyendaFacturacion.Add(new ElementoLeyenda($"{anio} proyectado", PaletaGraficos.Serie1, Rayado: _vista == VistaFacturacion.Mensual, Discontinuo: _vista == VistaFacturacion.Acumulada));
        if (c is not null) LeyendaFacturacion.Add(new ElementoLeyenda(c.Value.ToString(Es), PaletaGraficos.Serie2));

        var compN = comp?.Select(v => (decimal?)v).ToList();
        string Mes(int i) => MesesLargos[i] + (i == corteMes && !p.Terminado ? $" (hasta el {p.FechaCorte.Day})" : "");
        if (_vista == VistaFacturacion.Mensual)
        {
            var series = new List<SerieGrafico>
            {
                new($"{anio} facturado", PaletaGraficos.Serie1, p.FacturacionMensual, p.ProyeccionMensual.Select(x => x is > 0 ? x : null).ToList()),
            };
            if (compN is not null) series.Add(new SerieGrafico(c!.Value.ToString(Es), PaletaGraficos.Serie2, compN));
            DatosFacturacion = new DatosGrafico(Meses, series, i =>
            {
                var filas = new List<FilaTooltip> { new($"{anio} facturado", p.FacturacionMensual[i] is decimal v ? Imp(v) : "—", PaletaGraficos.Serie1) };
                var pr = p.ProyeccionMensual[i];
                if (pr is > 0) filas.Add(new FilaTooltip("Proyectado", Imp(pr.Value)));
                if (comp is not null)
                {
                    filas.Add(new FilaTooltip(c!.Value.ToString(Es), Imp(comp[i]), PaletaGraficos.Serie2));
                    var total = (p.FacturacionMensual[i] ?? 0) + (pr ?? 0);
                    if ((p.FacturacionMensual[i] is not null || pr is not null) && comp[i] != 0)
                        filas.Add(new FilaTooltip(pr is not null ? "Variación proyectada" : "Variación", Pct((total - comp[i]) / Math.Abs(comp[i]) * 100m), Destacada: true));
                }
                return new ContenidoTooltip(Mes(i), filas);
            }, Descripcion: $"Facturación mensual de {anio}" + (c is null ? "" : $" frente a {c}"));
        }
        else if (_vista == VistaFacturacion.Acumulada)
        {
            var real = CalculoInicio.Acumulado(p.FacturacionMensual);
            var series = new List<SerieGrafico>();
            if (compN is not null) series.Add(new SerieGrafico(c!.Value.ToString(Es), PaletaGraficos.Serie2, CalculoInicio.Acumulado(compN)));
            decimal?[]? proy = null;
            if (hayProy && real[corteMes] is decimal baseProy)
            {
                proy = AcumuladoProyectado(baseProy, p.ProyeccionMensual, corteMes);
                series.Add(new SerieGrafico($"{anio} proyectado", PaletaGraficos.Serie1, proy, Discontinua: true,
                    Rotulo: proyTotal is decimal ptt ? $"proyección {anio}: {FormatoGrafico.Compacto((double)ptt)}" : null));
            }
            series.Add(new SerieGrafico($"{anio} facturado", PaletaGraficos.Serie1, real));
            var compAcu = compN is null ? null : CalculoInicio.Acumulado(compN);
            DatosFacturacion = new DatosGrafico(Meses, series, i =>
            {
                var filas = new List<FilaTooltip> { new($"{anio} facturado", real[i] is decimal v ? Imp(v) : "—", PaletaGraficos.Serie1) };
                if (proy?[i] is decimal pv && i > corteMes) filas.Add(new FilaTooltip(i == 11 ? "Proyección de cierre" : "Proyectado", Imp(pv), PaletaGraficos.Serie1));
                if (compAcu is not null) filas.Add(new FilaTooltip(c!.Value.ToString(Es), Imp(compAcu[i] ?? 0), PaletaGraficos.Serie2));
                var actualI = real[i] ?? (i > corteMes ? proy?[i] : null);
                if (compAcu?[i] is decimal ca && ca != 0 && actualI is decimal ai)
                    filas.Add(new FilaTooltip(real[i] is null ? "Variación proyectada" : "Variación", Pct((ai - ca) / Math.Abs(ca) * 100m), Destacada: true));
                return new ContenidoTooltip(MesesLargos[i] + " · acumulado" + (i == corteMes && !p.Terminado ? $" (hasta el {p.FechaCorte.Day})" : ""), filas);
            }, Descripcion: $"Facturación acumulada de {anio}" + (c is null ? "" : $" frente a {c}"));
        }
        // Tabla (también se rellena en las otras vistas, por si se cambia).
        FilasFacturacion.Clear();
        decimal acum = 0, acumC = 0;
        for (var i = 0; i < 12; i++)
        {
            var a = p.FacturacionMensual[i];
            var pr = p.ProyeccionMensual[i] is > 0 ? p.ProyeccionMensual[i] : null;
            var cv = comp?[i];
            acum += (a ?? 0) + (pr ?? 0);
            if (cv is decimal x) acumC += x;
            var total = (a ?? 0) + (pr ?? 0);
            var variacion = cv is decimal cc && cc != 0 && (a is not null || pr is not null) ? Pct((total - cc) / Math.Abs(cc) * 100m) : "";
            FilasFacturacion.Add(new FilaFacturacionVM(MesesLargos[i], a is decimal av ? Imp(av) : "", pr is decimal pv ? Imp(pv) : "", cv is decimal cvv ? Imp(cvv) : "",
                variacion, a is null && pr is null ? "" : Imp(acum), cv is null ? "" : Imp(acumC), a, pr, cv, acum, cv is null ? null : acumC));
        }
    }

    /// <summary>
    /// Línea discontinua de la vista Acumulada: arranca en el acumulado real del mes de corte y suma después lo proyectado (la
    /// parte que falta del mes de corte se suma en el mes siguiente). Si el corte es en diciembre, el único punto es el cierre.
    /// </summary>
    internal static decimal?[] AcumuladoProyectado(decimal acumuladoReal, IReadOnlyList<decimal?> proyeccionMensual, int mesCorte)
    {
        var r = new decimal?[12];
        if (mesCorte == 11)
        {
            r[11] = acumuladoReal + (proyeccionMensual[11] ?? 0);
            return r;
        }
        r[mesCorte] = acumuladoReal;
        for (var i = mesCorte + 1; i < 12; i++)
            r[i] = r[i - 1] + (i == mesCorte + 1 ? proyeccionMensual[mesCorte] ?? 0 : 0) + (proyeccionMensual[i] ?? 0);
        return r;
    }

    private void AplicarActividades(PanelInicio p)
    {
        Actividades.Clear();
        var c = p.Comparado;
        var lista = p.Actividades;
        var max = lista.Count == 0 ? 0 : lista.Max(a => Math.Max(a.Actual, a.Comparado ?? 0));
        var total = lista.Sum(a => a.Actual);
        for (var i = 0; i < lista.Count; i++)
        {
            var a = lista[i];
            var variacion = c is not null && a.Comparado is decimal ac && ac != 0 ? (a.Actual - ac) / Math.Abs(ac) * 100m : (decimal?)null;
            var peso = total != 0 ? $" · {(a.Actual / total * 100m).ToString("0.0", Es)} % del total" : "";
            Actividades.Add(new FilaBarraVM(a.Actividad, PaletaGraficos.Color(i), Imp(a.Actual), variacion is decimal v ? Pct(v) : null, TonoDe(variacion, true), a.Actual, a.Comparado, max,
                $"{a.Actividad}: {Imp(a.Actual)} {HastaCorte(p)}{peso}" + (a.Comparado is decimal cc ? $". {c} a la misma fecha: {Imp(cc)}" : "") +
                (a.Actividad == CalculoInicio.SinActividad ? ". Líneas sin centro de coste o con un centro sin actividad de IVA" : "")));
        }
        TextoActividades = lista.Count == 0 ? "Sin facturación en el ejercicio."
            : $"Total {Imp(total)}" + (total != 0 && lista[0].Actual > 0 ? $" · {(lista[0].Actual / total * 100m).ToString("0", Es)} % {lista[0].Actividad.ToLower(Es)}" : "") +
              (c is not null ? $". La raya es {c} a la misma fecha." : "");
    }

    private void AplicarIngresos(PanelInicio p)
    {
        SinIngresos = p.IngresosMensuales.All(v => v is null or 0) && p.GastosMensuales.All(v => v is null or 0) ? $"Sin ingresos ni gastos contabilizados en {p.Ejercicio}." : null;
        var corteMes = p.FechaCorte.Month - 1;
        DatosIngresos = new DatosGrafico(Meses,
        [
            new SerieGrafico("Ingresos", PaletaGraficos.Serie1, p.IngresosMensuales),
            new SerieGrafico("Gastos", PaletaGraficos.Serie2, p.GastosMensuales),
        ], i =>
        {
            var ing = p.IngresosMensuales[i];
            var gas = p.GastosMensuales[i];
            var filas = new List<FilaTooltip>
            {
                new("Ingresos", ing is decimal a ? Imp(a) : "—", PaletaGraficos.Serie1),
                new("Gastos", gas is decimal g ? Imp(g) : "—", PaletaGraficos.Serie2),
            };
            if (ing is decimal x && gas is decimal y) filas.Add(new FilaTooltip("Resultado", Imp(x - y), Destacada: true));
            return new ContenidoTooltip(MesesLargos[i] + (i == corteMes && !p.Terminado ? $" (hasta el {p.FechaCorte.Day})" : ""), filas);
        }, Descripcion: "Ingresos y gastos por mes");
    }

    private void AplicarGastos(PanelInicio p)
    {
        Gastos.Clear();
        var c = p.Comparado;
        var max = p.Gastos.Count == 0 ? 0 : p.Gastos.Max(g => Math.Max(g.Actual, g.Comparado ?? 0));
        var codigosMostrados = p.Gastos.Where(g => g.Codigo.Length > 0).Select(g => g.Codigo).ToList();
        foreach (var g in p.Gastos)
        {
            var variacion = c is not null && g.Comparado is decimal gc && gc != 0 ? (g.Actual - gc) / Math.Abs(gc) * 100m : (decimal?)null;
            IReadOnlyList<string>? cuentas = g.Codigo.Length > 0 ? [g.Codigo] : null;
            Gastos.Add(new FilaBarraVM(g.Texto, PaletaGraficos.Serie1, Imp(g.Actual), variacion is decimal v ? Pct(v) : null, TonoDe(variacion, false), g.Actual, g.Comparado, max,
                $"{g.Texto}: {Imp(g.Actual)} {HastaCorte(p)}" + (g.Comparado is decimal cc ? $". {c} a la misma fecha: {Imp(cc)}" : "") +
                (g.Codigo.Length == 0 ? $". Resto de cuentas del grupo 6 (sin la 630) que no están entre las 8 de más gasto" : "") +
                (cuentas is not null && _acciones.AbrirMayor is not null ? ". Doble clic: abre el Mayor de la cuenta" : ""), cuentas));
        }
        NotaGastos = p.Gastos.Count == 0 ? $"Sin gastos contabilizados en {p.Ejercicio}."
            : c is not null ? $"La raya es {c} a la misma fecha. En gastos, ▲ en rojo es que se gasta más." : null;
    }

    private void AplicarCobros(PanelInicio p)
    {
        var colores = new[] { PaletaGraficos.Serie1, PaletaGraficos.Serie4, PaletaGraficos.Serie2, PaletaGraficos.Serie5, PaletaGraficos.Serie3 };
        var total = p.PendienteCobro.Valor;
        TextoCobros = (p.HayVencimientos ? "por antigüedad del vencimiento" : "por antigüedad de la fecha del apunte (los apuntes no tienen vencimiento)") + " · " + Imp(total);
        SinCobros = total <= 0 ? $"Nada pendiente de cobro a {p.FechaCorte:dd/MM/yyyy}." : null;
        Tramos = p.Antiguedad.Select((t, i) => new TramoApilado(t.Etiqueta, t.Importe, colores[i])).ToList();
        LeyendaTramos.Clear();
        for (var i = 0; i < p.Antiguedad.Count; i++)
            if (p.Antiguedad[i].Importe != 0 || i > 0) LeyendaTramos.Add(new LeyendaTramoVM(p.Antiguedad[i].Etiqueta, Imp(p.Antiguedad[i].Importe), colores[i]));
        Clientes.Clear();
        foreach (var c in p.Clientes)
            Clientes.Add(new ClienteVM(c.Cuenta, string.IsNullOrWhiteSpace(c.Nombre) ? "(sin nombre)" : c.Nombre, Imp(c.Saldo), c.MasDe30Dias > 0 ? Imp(c.MasDe30Dias) : "—", c.Saldo, c.MasDe30Dias));
    }

    private void AplicarTesoreria(PanelInicio p)
    {
        var puntos = p.TesoreriaMensual;
        var marca = puntos.Count - 1;
        DatosTesoreria = new DatosGrafico(puntos.Select(x => x.Etiqueta).ToList(),
            [new SerieGrafico("Tesorería operativa", PaletaGraficos.Serie1, puntos.Select(x => x.Saldo).ToList(), Area: true)],
            i =>
            {
                var x = puntos[i];
                var titulo = x.EsCorte ? (x.Fecha == _servicio.Hoy ? $"{x.Fecha:dd/MM/yyyy} (hoy)" : $"{x.Fecha:dd/MM/yyyy}") : $"Fin de {MesesLargos[x.Fecha.Month - 1]} de {x.Fecha.Year}";
                return new ContenidoTooltip(titulo, [new FilaTooltip("Tesorería operativa", x.Saldo is decimal s ? Imp(s) : "sin datos de ese ejercicio", PaletaGraficos.Serie1)]);
            }, Marca: marca, TextoMarca: p.FechaCorte == _servicio.Hoy ? "hoy" : null, Descripcion: "Tesorería operativa (sin las cuentas de inversión) a fin de cada uno de los últimos 12 meses");
        Bancos.Clear();
        foreach (var b in p.Bancos)
        {
            var dias = b.UltimoExtracto is DateTime u ? (p.FechaCorte - u.Date).Days : (int?)null;
            // Mismo umbral que el aviso de extracto antiguo de la salud (decisión 2026-11-15 15:18 de pantalla-de-inicio).
            var aviso = dias > ProConta.Inicio.Salud.RevisionExtractoAntiguo.Dias ? $"hace {dias} días" : null;
            Bancos.Add(new BancoVM(b.Nombre, b.Cuenta, Imp(b.Saldo), b.UltimoExtracto is DateTime ue ? $"extracto hasta el {ue:dd/MM/yyyy}" : "sin extractos N43", aviso,
                $"{b.Nombre} ({b.Cuenta}): saldo contable {Imp(b.Saldo)} {HastaCorte(p)}. " +
                (b.UltimoExtracto is DateTime ux ? $"El último extracto Norma 43 importado llega hasta el {ux:dd/MM/yyyy}." : "No tiene extractos Norma 43 importados.") +
                (_acciones.AbrirMayor is not null ? " Doble clic: abre el Mayor de la cuenta" : "")));
        }
        if (p.OtrasCuentasTesoreria != 0)
            Bancos.Add(new BancoVM("Otras cuentas 572", null, Imp(p.OtrasCuentasTesoreria), "sin cuenta bancaria de Norma 43", null,
                "Saldo de las subcuentas 572 que no están asociadas a ninguna cuenta bancaria de Norma 43 (tarjetas, traspasos en curso…)"));
        TextoBancos = $"Tesorería operativa por banco · {Imp(p.Tesoreria.Valor)}";
        Inversiones.Clear();
        foreach (var b in p.Inversiones)
            Inversiones.Add(new BancoVM(b.Nombre, b.Cuenta, Imp(b.Saldo), b.UltimoExtracto is DateTime ue ? $"extracto hasta el {ue:dd/MM/yyyy}" : "sin extractos N43", null,
                $"{b.Nombre} ({b.Cuenta}): cuenta de inversión, saldo contable {Imp(b.Saldo)} {HastaCorte(p)}. No suma en la tesorería operativa; la marca se cambia en «Personalizar…»." +
                (_acciones.AbrirMayor is not null ? " Doble clic: abre el Mayor de la cuenta" : "")));
        TextoInversiones = $"Inversiones · {Imp(p.TotalInversiones)}";
        Avisar(nameof(HayInversiones));
    }

    // ------------------------------------------------------------ ratios

    private void AplicarRatios(PanelInicio p)
    {
        FamiliasRatios.Clear();
        var r = p.Ratios;
        var c = p.Comparado;
        var comparado = c is int ce ? p.SerieRatios.FirstOrDefault(x => x.Ejercicio == ce)?.Ratios : null;
        _ratiosBien = r?.Bien ?? 0;
        _ratiosVigilar = r?.Vigilar ?? 0;
        _ratiosMal = r?.Mal ?? 0;
        Avisar(nameof(RecuentoBien), nameof(RecuentoVigilar), nameof(RecuentoMal), nameof(HayBien), nameof(HayVigilar), nameof(HayMal));
        if (r is null)
        {
            TextoRatios = "";
            NotaRatios = null;
            SinRatios = "La base de datos no tiene el modelo del balance de situación (contabilidad/balances): sin él no se pueden clasificar las cuentas. " +
                        "Actualiza el esquema con el ActualizadorEsquema y pulsa «Actualizar».";
            return;
        }
        TextoRatios = $"balance a {r.FechaBalance:dd/MM/yyyy} · resultado de los últimos 12 meses";
        if (!r.HayBalance)
        {
            NotaRatios = null;
            SinRatios = $"Aún no hay balance a {r.FechaBalance:dd/MM/yyyy}: los ratios se calculan con el balance a fin del último mes completo y salen con el primer mes contabilizado.";
            return;
        }
        SinRatios = null;
        foreach (var familia in r.Ratios.GroupBy(x => x.Definicion.Familia))
        {
            var filas = familia.Select(v =>
            {
                var d = v.Definicion;
                var pv = comparado?.Ratio(d.Clave)?.Valor;
                var ayuda = $"{d.Formula}. Referencia: {d.Referencia}." +
                            (v.Motivo is { } m ? $" Sin datos: {m}" : $" Valor: {FormatoRatios.Valor(d, v.Valor)} ({FormatoRatios.Estado(v.Estado)[2..].ToLowerInvariant()}).") +
                            (c is not null ? pv is decimal x ? $" En {c} (a cierre): {FormatoRatios.Valor(d, x)}." : $" En {c}: sin datos." : "") +
                            " Pulsa para ver de qué cifras sale y su evolución.";
                return new FilaRatioVM(d.Nombre, FormatoRatios.Valor(d, v.Valor), v.Estado, FormatoRatios.Estado(v.Estado), v.Valor, pv, d.Minimo, d.Maximo, d.MenosEsMejor, ayuda,
                    new ComandoDelegado(() => VerRatio(d.Clave)));
            }).ToList();
            FamiliasRatios.Add(new FamiliaRatioVM(FormatoRatios.Familia(familia.Key), filas));
        }
        var primero = p.SerieRatios.FirstOrDefault()?.Ejercicio;
        NotaRatios = "Franja verde: rango de referencia. Marca oscura: valor actual." + (c is not null ? $" Raya gris: {c} a cierre." : "") +
                     " Pulsa un ratio para ver de qué cifras sale" + (primero is int pr && pr < p.Ejercicio ? $" y su evolución desde {pr}." : ".") +
                     (r.SinClasificar.Count > 0 ? $" Hay {r.SinClasificar.Count} cuentas con saldo que el modelo del balance no clasifica ({string.Join(", ", r.SinClasificar.Take(5))}{(r.SinClasificar.Count > 5 ? "…" : "")}): no cuentan en los ratios." : "");
    }

    /// <summary>La ficha de un ratio: fórmula, partidas (cada importe abre el Mayor de sus cuentas), resultado y evolución.</summary>
    internal FichaRatioVM? FichaRatio(string clave)
    {
        if (_panel?.Ratios?.Ratio(clave) is not { } v) return null;
        var d = v.Definicion;
        FichaRatioVM? ficha = null;
        var partidas = v.Partidas.Select(pa =>
        {
            var cuentas = pa.CuentasMayor;
            ICommand? abrir = _acciones.AbrirMayor is { } m && cuentas.Count > 0 ? new ComandoDelegado(() => { ficha?.Cerrar?.Invoke(); m(cuentas); }) : null;
            return new PartidaRatioVM(pa.Nombre, pa.Cuentas, FormatoRatios.Importe(pa.Importe),
                abrir is null ? $"{pa.Nombre}: {FormatoRatios.Importe(pa.Importe)}" : $"Abre el Mayor con las cuentas de «{pa.Nombre}» ({pa.Detalle.Count} subcuentas)", abrir);
        }).ToList();
        var evolucion = _panel.SerieRatios.Select(s =>
        {
            var x = s.Ratios.Ratio(clave);
            return new EvolucionRatioVM(s.EsActual ? $"{s.Ejercicio}*" : s.Ejercicio.ToString(CultureInfo.InvariantCulture), FormatoRatios.Valor(d, x?.Valor), x?.Estado ?? EstadoRatio.SinDatos,
                x?.Motivo ?? (s.EsActual ? $"A {s.Ratios.FechaBalance:dd/MM/yyyy}, con el resultado de los últimos 12 meses" : $"A 31/12/{s.Ejercicio}, con el resultado del ejercicio"));
        }).ToList();
        var r = _panel.Ratios!;
        ficha = new FichaRatioVM
        {
            Titulo = d.Nombre,
            Subtitulo = $"{FormatoRatios.Familia(d.Familia)} · balance a {r.FechaBalance:dd/MM/yyyy}, resultado de los últimos 12 meses (del {r.DesdeResultado:dd/MM/yyyy} al {r.FechaBalance:dd/MM/yyyy})",
            Formula = d.Formula,
            Referencia = $"Referencia: {d.Referencia}",
            Partidas = partidas,
            Resultado = FormatoRatios.Valor(d, v.Valor),
            Estado = v.Estado,
            TextoEstado = FormatoRatios.Estado(v.Estado),
            Motivo = v.Motivo,
            Evolucion = evolucion,
            NotaEvolucion = $"* {_panel.Ejercicio} a {r.FechaBalance:dd/MM/yyyy}, con el resultado de los últimos 12 meses. Los demás, a 31/12 con el resultado del ejercicio.",
            AbrirBalanceCommand = _acciones.AbrirBalanceSituacion is { } ab ? new ComandoDelegado(() => { ficha?.Cerrar?.Invoke(); ab(); }) : null,
        };
        return ficha;
    }

    private void VerRatio(string clave)
    {
        if (FichaRatio(clave) is { } f) _dialogos.VerRatio(f);
    }
}

/// <summary>Vista del bloque de facturación.</summary>
public enum VistaFacturacion { Mensual, Acumulada, Tabla }
