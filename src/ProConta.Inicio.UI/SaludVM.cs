using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Inicio.Salud;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Inicio.UI;

/// <summary>Sección de la lista de salud.</summary>
public enum SeccionSalud { Tareas, Avisos, Hechas }

/// <summary>Una tarea, un aviso o una tarea hecha en el panel de salud.</summary>
public sealed class ElementoSaludVM
{
    public required string Clave { get; init; }
    public required SeccionSalud Seccion { get; init; }
    /// <summary>Grave, Atencion, Aviso o Hecho (para el estilo).</summary>
    public required string Tono { get; init; }
    public required string Icono { get; init; }
    public required string TextoGravedad { get; init; }
    public required string Titulo { get; init; }
    public required string Explicacion { get; init; }
    public string? Plazo { get; init; }
    public string? AyudaPlazo { get; init; }
    /// <summary>Vencida, Cerca o Lejos (para el color del plazo).</summary>
    public string TonoPlazo { get; init; } = "Lejos";
    public required string Destino { get; init; }
    public required string Ayuda { get; init; }
    public required bool PuedeAbrir { get; init; }
    public bool Hecha => Seccion == SeccionSalud.Hechas;
    public bool HayPlazo => Plazo is not null;
    public string NombreSeccion => Seccion switch { SeccionSalud.Tareas => "Tareas con plazo", SeccionSalud.Avisos => "Avisos", _ => "Hechas este trimestre" };
    public ElementoSalud? Elemento { get; init; }
    /// <summary>Variación desde el lunes («+3 desde el lunes»); null sin dato o sin cambio.</summary>
    public string? Tendencia { get; init; }
    /// <summary>Sube (más cosas pendientes: malo) o Baja (bueno).</summary>
    public string TonoTendencia { get; init; } = "";
    public bool HayTendencia => Tendencia is not null;
    public string AyudaTendencia => Tendencia is null ? "" : $"Variación del número de {(TonoTendencia == "Sube" ? "pendientes (sube: van a más)" : "pendientes (baja: van a menos)")} desde la revisión del lunes de esta semana.";
    public string Nodo { get; init; } = "";
    public DestinoSalud? DestinoModelo { get; init; }

    /// <summary>Texto que se copia con «Copiar el texto».</summary>
    public string TextoCopiar => $"{TextoGravedad}: {Titulo}{(Plazo is null ? "" : $" ({Plazo}{(AyudaPlazo is null ? "" : $", {AyudaPlazo.ToLowerInvariant()}")})")}. {Explicacion}{(Tendencia is null ? "" : $" ({Tendencia}.)")} Se resuelve en: {Destino}.";
}

/// <summary>Celda de «Estado de los periodos».</summary>
public sealed record CeldaPeriodoVM(string Texto, string Estado, string Ayuda);

/// <summary>Fila de «Estado de los periodos»: 12 celdas mensuales o 4 trimestrales; doble clic abre su pantalla.</summary>
public sealed record FilaPeriodoVM(string Nombre, IReadOnlyList<CeldaPeriodoVM> Celdas, int Columnas, string Ayuda, ICommand? Abrir);

/// <summary>«¿Por qué aparece?»: qué nodo lo detecta, la condición y cómo se resuelve.</summary>
public sealed record PorQueSaludVM(string Titulo, string Nodo, string Explicacion, string Condicion, string ComoSeResuelve, string Destino, ICommand? Abrir, string TextoCopiar);

/// <summary>
/// Panel «Salud de la contabilidad» y franja «Lo más urgente» de Inicio (pantalla-de-inicio, fase 3). La revisión se carga aparte
/// de los indicadores (no los retrasa), se va llenando según terminan las revisiones, «Actualizar» la repite y muestra la hora de
/// la última. Sin posponer ni silenciar: un elemento desaparece cuando deja de cumplirse su condición.
/// </summary>
public sealed class SaludVM : ModeloVista
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly string[] Letras = ["E", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D"];
    private readonly ServicioSalud _servicio;
    private readonly ContextoInicio _ctx;
    private readonly AccionesInicio _acciones;
    private readonly IDialogosInicio _dialogos;
    private readonly Func<IReadOnlySet<string>> _inversiones;
    private int _carga;
    private InformeSalud? _informe;
    private List<ResultadoRevision> _parciales = [];
    private IReadOnlyList<ElementoSaludVM> _seleccion = [];
    private readonly object _bloqueo = new();

    public SaludVM(ServicioSalud servicio, ContextoInicio contexto, AccionesInicio acciones, IDialogosInicio dialogos, Func<IReadOnlySet<string>> inversiones)
    {
        _servicio = servicio;
        _ctx = contexto;
        _acciones = acciones;
        _dialogos = dialogos;
        _inversiones = inversiones;
        AbrirCommand = new ComandoDelegado(p => Abrir(p as ElementoSaludVM ?? (_seleccion.Count == 1 ? _seleccion[0] : null)),
            p => (p as ElementoSaludVM ?? (_seleccion.Count == 1 ? _seleccion[0] : null)) is { PuedeAbrir: true });
        PorQueCommand = new ComandoDelegado(() => PorQue(_seleccion[0]), () => _seleccion.Count == 1 && !_seleccion[0].Hecha);
        CopiarCommand = new ComandoDelegado(() => _dialogos.Copiar(string.Join(Environment.NewLine, _seleccion.Select(s => s.TextoCopiar))), () => _seleccion.Count > 0);
        AbrirUrgenteCommand = new ComandoDelegado(() => { if (Urgente is { } u) Abrir(u); }, () => Urgente is { PuedeAbrir: true });
        ReintentarCommand = new ComandoDelegado(() => _ = RevisarAsync());
    }

    // ------------------------------------------------------------ estado

    private bool _cargando = true;
    public bool Cargando { get => _cargando; private set { if (Fijar(ref _cargando, value)) Avisar(nameof(TextoPie), nameof(HayContenido)); } }

    private string? _error;
    public string? Error { get => _error; private set { Fijar(ref _error, value); Avisar(nameof(HayContenido)); } }

    private string _textoPie = "Revisando la contabilidad…";
    public string TextoPie { get => _cargando ? _textoCarga : _textoPie; private set => Fijar(ref _textoPie, value); }
    private string _textoCarga = "Revisando la contabilidad…";

    public bool HayContenido => Error is null && (!Cargando || _parciales.Count > 0);

    // ------------------------------------------------------------ filtro

    private string _filtro = "Todo";
    public bool FiltroTodo { get => _filtro == "Todo"; set { if (value) CambiarFiltro("Todo"); } }
    public bool FiltroTareas { get => _filtro == "Tareas"; set { if (value) CambiarFiltro("Tareas"); } }
    public bool FiltroAvisos { get => _filtro == "Avisos"; set { if (value) CambiarFiltro("Avisos"); } }

    private void CambiarFiltro(string f)
    {
        if (_filtro == f) return;
        _filtro = f;
        Avisar(nameof(FiltroTodo), nameof(FiltroTareas), nameof(FiltroAvisos));
        Construir();
    }

    // ------------------------------------------------------------ contenido

    private int _graves, _atencion, _avisos;
    public string Numero => (_graves + _atencion).ToString(Es);
    public string TonoResumen => _graves > 0 ? "Grave" : _atencion > 0 ? "Atencion" : "Bien";
    public string TituloResumen => _graves > 0 ? "Hay cosas urgentes" : _atencion > 0 ? "Hay cosas pendientes" : _avisos > 0 ? "Nada urgente" : "Todo al día";
    public string RecuentoGraves => $"✕ {_graves} {(_graves == 1 ? "grave" : "graves")}";
    public string RecuentoAtencion => $"! {_atencion} atención";
    public string RecuentoAvisos => $"i {_avisos} {(_avisos == 1 ? "aviso" : "avisos")}";
    public bool TodoAlDia { get; private set; }

    public ObservableCollection<FilaPeriodoVM> Periodos { get; } = [];
    public IReadOnlyList<string> Meses => Letras;
    public bool HayPeriodos => Periodos.Count > 0;

    /// <summary>Tareas, avisos y hechas, en ese orden (la vista los agrupa por sección).</summary>
    public ObservableCollection<ElementoSaludVM> Elementos { get; } = [];
    public string TextoTareas { get; private set; } = "";
    public string TextoAvisos { get; private set; } = "";

    public ObservableCollection<string> Correctas { get; } = [];
    public string TituloCorrectas => $"Comprobaciones correctas ({Correctas.Count})";
    public bool HayCorrectas => Correctas.Count > 0;

    private ElementoSaludVM? _urgente;
    /// <summary>«Lo más urgente»: el elemento grave más prioritario; null si no hay nada grave.</summary>
    public ElementoSaludVM? Urgente { get => _urgente; private set { Fijar(ref _urgente, value); Avisar(nameof(HayUrgente), nameof(TextoBotonUrgente), nameof(AyudaBotonUrgente)); CommandManager.InvalidateRequerySuggested(); } }
    public bool HayUrgente => Urgente is not null;
    public string TextoBotonUrgente => Urgente is { PuedeAbrir: true } u ? $"Abrir {Corto(u.Destino)}" : "Sin pantalla aún";
    public string AyudaBotonUrgente => Urgente is { } u ? u.PuedeAbrir ? $"Abre {u.Destino}" : u.Ayuda : "";

    public InformeSalud? Informe => _informe;

    public ICommand AbrirCommand { get; }
    public ICommand PorQueCommand { get; }
    public ICommand CopiarCommand { get; }
    public ICommand AbrirUrgenteCommand { get; }
    public ICommand ReintentarCommand { get; }

    /// <summary>La vista avisa de la selección (simple o múltiple con Ctrl y Mayús).</summary>
    public void Seleccionar(IEnumerable<ElementoSaludVM> seleccion)
    {
        _seleccion = seleccion.ToList();
        CommandManager.InvalidateRequerySuggested();
        Avisar(nameof(TextoAbrirSeleccion));
    }

    public IReadOnlyList<ElementoSaludVM> Seleccion => _seleccion;
    public string TextoAbrirSeleccion => _seleccion.Count == 1 && _seleccion[0].PuedeAbrir ? $"Abrir {Corto(_seleccion[0].Destino)}" : "Abrir la pantalla";

    private static string Corto(string destino) => destino.Split(" › ").Last().Split(" (")[0];

    // ------------------------------------------------------------ carga

    /// <summary>Revisa la salud (al abrir Inicio y con «Actualizar»).</summary>
    public async Task RevisarAsync()
    {
        var mia = ++_carga;
        var contexto = SynchronizationContext.Current;
        _parciales = [];
        Error = null;
        Cargando = true;
        _textoCarga = "Revisando la contabilidad…";
        Avisar(nameof(TextoPie));
        try
        {
            var total = _servicio.Revisiones.Count;
            var inversiones = _inversiones();
            var terminada = false;
            void AlTerminar(ResultadoRevision r)
            {
                void Aplicar()
                {
                    lock (_bloqueo)
                    {
                        // Un aviso que llega tarde no pisa el resultado final.
                        if (mia != _carga || terminada) return;
                        _parciales.Add(r);
                        _textoCarga = $"Revisando la contabilidad… ({_parciales.Count} de {total})";
                        Construir();
                    }
                }
                contexto?.Post(_ => Aplicar(), null);
            }
            var informe = await Task.Run(() => _servicio.RevisarAsync(_ctx.IdSociedad, _ctx.Ejercicio, inversiones, AlTerminar));
            lock (_bloqueo)
            {
                if (mia != _carga) return;
                terminada = true;
                _informe = informe;
                _parciales = informe.Revisiones.ToList();
                Cargando = false;
                var hora = informe.Hora;
                TextoPie = $"Revisada {(hora.Date == DateTime.Today ? "hoy" : $"el {hora:dd/MM/yyyy}")} a las {hora:HH:mm} · {informe.Revisiones.Count} comprobaciones en {informe.Duracion.TotalSeconds.ToString("0.0", Es)} s" +
                            (_servicio.ErrorHistorico is { } eh ? $" · sin tendencia «desde el lunes»: no he podido guardar el histórico ({eh}); pulsa «Actualizar» para reintentarlo" : "");
                Construir();
            }
            Avisar(nameof(Informe));
        }
        catch (Exception ex) when (mia == _carga)
        {
            Cargando = false;
            Error = $"No he podido revisar la salud de la contabilidad: {ex.Message}. Comprueba que la base de datos está accesible y pulsa «Reintentar» (o «Actualizar»). " +
                    "Los indicadores no se ven afectados.";
        }
    }

    /// <summary>Rehace las listas con lo revisado hasta ahora.</summary>
    internal void Construir()
    {
        var resultados = _parciales.ToList();
        var elementos = resultados.SelectMany(r => r.Hallazgos.Elementos).ToList();
        (_graves, _atencion, _avisos) = OrdenSalud.Recuento(elementos);
        var hoy = _servicio.Hoy;
        _tendencias = Cargando ? new Dictionary<string, int>() : _informe?.Tendencias ?? new Dictionary<string, int>();
        var tareas = OrdenSalud.Tareas(elementos);
        var avisos = OrdenSalud.Avisos(elementos);
        var hechas = resultados.SelectMany(r => r.Hallazgos.Hechas).ToList();
        TodoAlDia = !Cargando && elementos.Count == 0;
        Elementos.Clear();
        if (_filtro != "Avisos")
            foreach (var t in tareas) Elementos.Add(Vista(t, SeccionSalud.Tareas, hoy));
        if (_filtro != "Tareas")
            foreach (var a in avisos) Elementos.Add(Vista(a, SeccionSalud.Avisos, hoy));
        if (_filtro != "Avisos")
            foreach (var h in hechas)
                Elementos.Add(new ElementoSaludVM
                {
                    Clave = h.Clave, Seccion = SeccionSalud.Hechas, Tono = "Hecho", Icono = "✓", TextoGravedad = "Hecho", Titulo = h.Titulo, Explicacion = h.Explicacion,
                    Destino = h.Destino.Nombre, PuedeAbrir = false, Nodo = h.Nodo, DestinoModelo = h.Destino,
                    Ayuda = $"Hecho. Lo detecta {h.Nodo}.",
                });
        TextoTareas = tareas.Count.ToString(Es);
        TextoAvisos = avisos.Count.ToString(Es);
        Correctas.Clear();
        foreach (var c in resultados.SelectMany(r => r.Hallazgos.Correctas)) Correctas.Add(c.Texto);
        Periodos.Clear();
        foreach (var f in resultados.SelectMany(r => r.Hallazgos.Periodos).OrderBy(f => f.Orden)) Periodos.Add(Fila(f));
        Urgente = OrdenSalud.LoMasUrgente(elementos) is { } u ? Vista(u, u.Tipo == TipoSalud.Tarea ? SeccionSalud.Tareas : SeccionSalud.Avisos, hoy) : null;
        _seleccion = [];
        Avisar(nameof(Numero), nameof(TonoResumen), nameof(TituloResumen), nameof(RecuentoGraves), nameof(RecuentoAtencion), nameof(RecuentoAvisos), nameof(TodoAlDia),
            nameof(TextoTareas), nameof(TextoAvisos), nameof(TituloCorrectas), nameof(HayCorrectas), nameof(HayPeriodos), nameof(HayContenido), nameof(TextoPie));
    }

    private IReadOnlyDictionary<string, int> _tendencias = new Dictionary<string, int>();

    private bool Abrible(DestinoSalud d) => Accion(d) is not null;

    private Action? Accion(DestinoSalud d) => d.Pantalla switch
    {
        PantallaSalud.VentasPorFacturar => _acciones.AbrirVentas,
        PantallaSalud.Ejercicios => _acciones.AbrirParametros is { } p ? () => p("ejercicios") : null,
        PantallaSalud.LineasN43 => _acciones.AbrirLineasN43,
        PantallaSalud.Bancos => _acciones.AbrirBancos,
        PantallaSalud.FacturasCompra => _acciones.AbrirFacturasCompra,
        PantallaSalud.FacturasVenta => _acciones.AbrirFacturas,
        PantallaSalud.Mayor => _acciones.AbrirMayor is { } m && d.Cuentas is { Count: > 0 } c ? () => m(c) : null,
        PantallaSalud.Diario => _acciones.AbrirDiario,
        PantallaSalud.Terceros => _acciones.AbrirTerceros,
        PantallaSalud.Nominas => _acciones.AbrirNominas,
        PantallaSalud.Programadas => _acciones.AbrirProgramadas,
        _ => null,
    };

    internal ElementoSaludVM Vista(ElementoSalud e, SeccionSalud seccion, DateTime hoy)
    {
        var dias = e.DiasHastaPlazo(hoy);
        var puede = Abrible(e.Destino);
        int? variacion = _tendencias.TryGetValue(TendenciaSalud.Clave(e.Clave), out var v) ? v : null;
        var (tono, icono, texto) = e.Gravedad switch
        {
            GravedadSalud.Grave => ("Grave", "✕", "Grave"),
            GravedadSalud.Atencion => ("Atencion", "!", "Atención"),
            _ => ("Aviso", "i", "Aviso"),
        };
        return new ElementoSaludVM
        {
            Clave = e.Clave, Seccion = seccion, Tono = tono, Icono = icono, TextoGravedad = texto, Titulo = e.Titulo, Explicacion = e.Explicacion,
            Plazo = dias is int d ? d < 0 ? "vencida" : d == 0 ? "hoy" : d == 1 ? "mañana" : $"en {d} días" : null,
            AyudaPlazo = e.Plazo is DateTime p ? $"Fecha límite: {p:dd/MM/yyyy}" : null,
            TonoPlazo = dias is int dd ? dd < 0 ? "Vencida" : dd <= 16 ? "Cerca" : "Lejos" : "Lejos",
            Destino = e.Destino.Nombre, PuedeAbrir = puede, Elemento = e, Nodo = e.Nodo, DestinoModelo = e.Destino,
            Tendencia = TendenciaSalud.Texto(variacion), TonoTendencia = variacion > 0 ? "Sube" : variacion < 0 ? "Baja" : "",
            Ayuda = $"{texto}. Lo detecta {e.Nodo}. " + (puede
                ? $"Doble clic o Intro: abre {e.Destino.Nombre}. Clic derecho: «¿Por qué aparece?» y copiar el texto."
                : $"La pantalla donde se resuelve ({e.Destino.Nombre}) aún no existe en ProContaVF: no se abre nada. Clic derecho: «¿Por qué aparece?» dice cómo resolverlo."),
        };
    }

    private FilaPeriodoVM Fila(FilaPeriodos f)
    {
        var celdas = f.Celdas.Select((c, i) => new CeldaPeriodoVM(
            (f.Trimestral ? $"{i + 1}T " : "") + c.Estado switch
            {
                EstadoPeriodo.Hecho => "✓", EstadoPeriodo.Incompleto => "!", EstadoPeriodo.ConErrores => "✕", EstadoPeriodo.EnCurso => "…", _ => "·",
            },
            c.Estado.ToString(), c.Ayuda + c.Estado switch
            {
                EstadoPeriodo.Hecho => " (hecho)", EstadoPeriodo.Incompleto => " (incompleto)", EstadoPeriodo.ConErrores => " (con errores)", EstadoPeriodo.EnCurso => " (en curso)", _ => "",
            })).ToList();
        var accion = Accion(f.Destino);
        return new FilaPeriodoVM(f.Nombre, celdas, f.Trimestral ? 4 : 12,
            accion is null ? $"{f.Nombre}. La pantalla donde se resuelve ({f.Destino.Nombre}) aún no existe en ProContaVF." : $"{f.Nombre}. Doble clic: abre {f.Destino.Nombre}.",
            accion is null ? null : new ComandoDelegado(accion));
    }

    internal void Abrir(ElementoSaludVM? e)
    {
        if (e?.DestinoModelo is { } d && Accion(d) is { } a) a();
    }

    internal PorQueSaludVM PorQueDe(ElementoSaludVM e)
    {
        var m = e.Elemento!;
        return new PorQueSaludVM(m.Titulo, m.Nodo, m.Explicacion, m.Condicion,
            m.ComoSeResuelve + " No hay que marcarlo como hecho: desaparece solo en cuanto deja de cumplirse la condición.",
            e.PuedeAbrir ? $"Se resuelve en {m.Destino.Nombre}." : $"Se resuelve en {m.Destino.Nombre}, que aún no existe en ProContaVF.",
            e.PuedeAbrir ? new ComandoDelegado(() => Abrir(e)) : null, e.TextoCopiar);
    }

    private void PorQue(ElementoSaludVM e) => _dialogos.PorQue(PorQueDe(e));
}
