using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.ModelosTributarios;

/// <summary>Contexto de la pantalla Modelos tributarios y lo que abre fuera de ella.</summary>
public sealed record ContextoModelosTributarios(ServicioModelosTributarios Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Si el operador puede presentar (acción MODELOS.PRESENTAR).</summary>
    public Func<Task<bool>> PuedePresentar { get; init; } = () => Task.FromResult(true);
    /// <summary>Abre un fichero (PDF, fichero del modelo) con el programa del equipo.</summary>
    public Action<string>? AbrirFichero { get; init; }
    /// <summary>Abre el proceso relacionado con un modelo, en el periodo si lo admite (la liquidación del IVA en su trimestre).</summary>
    public Action<ProcesoModelo, string, string>? AbrirProceso { get; init; }
    /// <summary>Copia un texto al portapapeles.</summary>
    public Action<string>? Copiar { get; init; }
    /// <summary>Carpeta de la empresa (documentos) donde se guardan los ficheros de los modelos; null si no se sabe.</summary>
    public Func<Task<string?>>? CarpetaEmpresa { get; init; }
}

/// <summary>Diálogos de la pantalla (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosModelosTributarios
{
    /// <summary>Ventana de conformidad: el operador ve el PDF y los datos y marca «Conforme». True si confirma la presentación.</summary>
    bool Conformidad(ConformidadPresentacion datos, Action verPdf);
    /// <summary>Pide el NRC del pago (forma I). Null si se cancela.</summary>
    string? PedirNrc(string titulo, string explicacion);
    /// <summary>«Modelos de la sociedad…»: los modelos con su marca. Devuelve los marcados, o null si se cancela.</summary>
    IReadOnlyCollection<string>? ElegirModelos(IReadOnlyList<(DefinicionModelo Modelo, bool Marcado)> modelos);
    /// <summary>Dónde guardar un fichero (propone carpeta y nombre). Null si se cancela.</summary>
    string? GuardarComo(string carpeta, string nombre);
    void Informar(string titulo, string mensaje, IReadOnlyList<string>? detalles = null);
    void Error(string titulo, string mensaje);
}

/// <summary>Ficha de un periodo en la lista de modelos o en la franja de plazos: va al periodo.</summary>
public sealed record FichaPeriodoVM(string Texto, string Detalle, string Clase, string Ayuda, ICommand Ir);

/// <summary>Un modelo en la lista «Modelos de la sociedad».</summary>
public sealed class FilaModeloVM(DefinicionModelo def, IReadOnlyList<FichaPeriodoVM> fichas, string proximo, string? soloConsulta) : ModeloVista
{
    public DefinicionModelo Definicion { get; } = def;
    public string Id => Definicion.Id;
    public string Nombre => Definicion.Nombre;
    public string Periodicidad => Definicion.Periodicidad;
    public string Corto => Definicion.Corto;
    public IReadOnlyList<FichaPeriodoVM> Fichas { get; } = fichas;
    public string Proximo { get; } = proximo;
    public string? SoloConsulta { get; } = soloConsulta;
    public bool EsSoloConsulta => SoloConsulta is not null;
}

/// <summary>Un periodo en la tabla del detalle o en la vista «Por plazo».</summary>
public sealed class FilaPeriodoVM(PeriodoModelo datos, DateTime hoy, Action alMarcar) : ModeloVista
{
    private bool _marcado;

    public PeriodoModelo Datos { get; } = datos;
    public DefinicionModelo Modelo { get; } = DefinicionModelo.De(datos.Modelo);
    public string Clave => Datos.Clave;
    public string Id => Datos.Modelo;
    public string NombreModelo => Modelo.Nombre;
    public string Periodo => Datos.Nombre;
    public string Plazo => $"{Datos.PlazoDesde:dd/MM} – {Datos.PlazoHasta:dd/MM/yyyy}";
    public string PlazoHasta => Formatos.Fecha(Datos.PlazoHasta);
    public EstadoPeriodoModelo EstadoPeriodo => Datos.Estado(hoy);
    public string Estado => Datos.TextoEstado(hoy);
    public string ClaseEstado => EstadoPeriodo.ToString().ToUpperInvariant();
    public string AyudaEstado => Datos.AyudaEstado(hoy) + (Datos.Bloqueos.Count > 0 && !Datos.Presentado && hoy.Date >= Datos.PlazoDesde ? $" Antes hay que resolver: {Datos.Bloqueos[0]}" : "");
    public string Origen => Datos.Ultima is null ? "" : Datos.Ultima.EsExterna ? "gestoría" : "programa";
    public string AyudaOrigen => Datos.Ultima is null ? "" : Datos.Ultima.EsExterna ? "Presentado fuera del programa; lo devuelve la consulta de la AEAT" : "Presentado desde el programa";
    public bool Provisional => hoy.Date < Datos.PlazoDesde;
    public string Calculado => Datos.SoloConsulta is not null ? "—" : Datos.Calculado is decimal c ? Formatos.Importe(c) : Datos.TextoCalculado ?? "";
    public string AyudaCalculado => Datos.SoloConsulta ?? (Provisional ? "Provisional: el periodo no ha terminado o su plazo no ha empezado" : Datos.TextoCalculado ?? "Lo que calcula el programa ahora");
    public string Presentado => Datos.Ultima is null ? "" : Datos.Ultima.EsExterna ? "—" : Formatos.Importe(Datos.Ultima.Resultado);
    public string AyudaPresentado => Datos.Ultima?.EsExterna == true ? "La consulta de la AEAT no da el importe de lo presentado fuera del programa" : "";
    public string Fecha => Datos.Ultima is { } u ? Formatos.Fecha(u.FechaHora) : "";
    public string Csv => Datos.Ultima?.Csv ?? "";

    public bool Marcado
    {
        get => _marcado;
        set { if (Fijar(ref _marcado, value)) alMarcar(); }
    }

    /// <summary>Marca o desmarca sin avisar (para las marcas en bloque).</summary>
    internal void FijarMarca(bool valor) { if (Fijar(ref _marcado, valor, nameof(Marcado))) { } }
}

/// <summary>Una opción del menú contextual de los periodos.</summary>
public sealed record OpcionMenuVM(string Texto, string? Detalle, bool Habilitada, ICommand? Comando, bool Separador = false);

/// <summary>
/// Pantalla Contabilidad › Modelos tributarios (contabilidad/modelos-tributarios; maqueta validada sources/maquetas/ModelosTributarios,
/// decisiones del propietario del 2026-11-18 05:34): los modelos de la sociedad con sus periodos del ejercicio coloreados por estado,
/// el detalle del modelo elegido con la tabla de sus periodos, la vista «Por plazo» con todos los periodos ordenados por fin de plazo y
/// la franja de vencidos, desactualizados y próximos plazos. Cabecera: Por modelo/Por plazo, «Consultar a la AEAT», «Modelos de la
/// sociedad…» y Actualizar, con la marca PRUEBAS. Las acciones de los periodos marcados van en el menú contextual, con el botón «⋯» de
/// cada fila como atajo. Al abrirla, consulta sola a la AEAT una vez al día.
/// </summary>
public sealed class ModelosTributariosVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoModelosTributarios _ctx;
    private readonly IDialogosModelosTributarios _dialogos;
    private readonly Action<DetalleModelo, string>? _abrirDetalle;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _porPlazo;
    private string _filtro = "Todos";
    private FilaModeloVM? _seleccion;
    private string _textoConsulta = "";
    private bool _puedePresentar = true;
    private IReadOnlyDictionary<string, IReadOnlyList<PeriodoModelo>> _periodos = new Dictionary<string, IReadOnlyList<PeriodoModelo>>();
    private IReadOnlySet<string> _visibles = new HashSet<string>();
    private bool _elegidos;
    private readonly Dictionary<string, FilaPeriodoVM> _filas = [];

    public ModelosTributariosVM(ContextoModelosTributarios ctx, IDialogosModelosTributarios dialogos, Action<DetalleModelo, string>? abrirDetalle = null, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _abrirDetalle = abrirDetalle;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        ConsultarCommand = new ComandoDelegado(() => _ = ConsultarAsync(automatica: false), () => !_ocupado);
        ModelosSociedadCommand = new ComandoDelegado(() => _ = ElegirModelosAsync(), () => !_ocupado);
        AbrirProcesoCommand = new ComandoDelegado(() => { if (_seleccion is { } m) AbrirProceso(m.Definicion, ""); }, () => _seleccion?.Definicion.Proceso is not (null or ProcesoModelo.Ninguno));
        AbrirCasillasDetalleCommand = new ComandoDelegado(() => { if ((Periodos.FirstOrDefault(p => p.Marcado) ?? Periodos.LastOrDefault(p => !p.Provisional)) is { } f) _ = AccionAsync(AccionModelo.AbrirCasillas, [f]); },
            () => _seleccion is { EsSoloConsulta: false });
    }

    // ---------- pestaña ----------
    public string Titulo => "Modelos tributarios";
    public string Ayuda => $"Modelos tributarios de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaModelosTributarios { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand ConsultarCommand { get; }
    public ICommand ModelosSociedadCommand { get; }
    public ICommand AbrirProcesoCommand { get; }
    public ICommand AbrirCasillasDetalleCommand { get; }

    // ---------- estado ----------
    public DateTime Hoy => _ctx.Servicio.Hoy;
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public bool EsPruebas { get; private set; }
    public string AyudaEntorno => EsPruebas
        ? "La BD es una copia de pruebas: todo se envía al entorno de pruebas de la AEAT (sin efectos, días laborables de 8:00 a 15:00) y nunca al real"
        : "Esta BD es la de producción: los modelos se presentan de verdad en la AEAT";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string TextoConsulta { get => _textoConsulta; private set => Fijar(ref _textoConsulta, value); }

    public bool PorPlazo
    {
        get => _porPlazo;
        set { if (Fijar(ref _porPlazo, value)) { Avisar(nameof(PorModelo)); RellenarPlazos(); } }
    }

    public bool PorModelo { get => !_porPlazo; set => PorPlazo = !value; }

    public string Filtro
    {
        get => _filtro;
        set { if (Fijar(ref _filtro, value)) { Avisar(nameof(FiltroTodos), nameof(FiltroPendientes), nameof(FiltroPresentados)); RellenarPlazos(); } }
    }

    public bool FiltroTodos { get => _filtro == "Todos"; set { if (value) Filtro = "Todos"; } }
    public bool FiltroPendientes { get => _filtro == "Pendientes"; set { if (value) Filtro = "Pendientes"; } }
    public bool FiltroPresentados { get => _filtro == "Presentados"; set { if (value) Filtro = "Presentados"; } }

    public ObservableCollection<FilaModeloVM> Modelos { get; } = [];
    public string TextoModelos => $"{Modelos.Count} de {DefinicionModelo.Todos.Count}";
    public ObservableCollection<FilaPeriodoVM> Periodos { get; } = [];
    public ObservableCollection<FilaPeriodoVM> PeriodosPlazo { get; } = [];
    public string TextoPlazos => $"{PeriodosPlazo.Count} periodo{(PeriodosPlazo.Count == 1 ? "" : "s")}";
    public string TituloCalendario => $"Calendario de {_ctx.Trabajo.Ejercicio}";
    public ObservableCollection<FichaPeriodoVM> Vencidos { get; } = [];
    public ObservableCollection<FichaPeriodoVM> Desactualizados { get; } = [];
    public ObservableCollection<FichaPeriodoVM> Proximos { get; } = [];
    public bool HayVencidos => Vencidos.Count > 0;
    public bool HayDesactualizados => Desactualizados.Count > 0;
    public bool HayProximos => Proximos.Count > 0;
    public string TextoMasProximos { get; private set; } = "";
    public string TextoMasVencidos { get; private set; } = "";
    public bool SinModelos => Modelos.Count == 0;

    public FilaModeloVM? Seleccion
    {
        get => _seleccion;
        set
        {
            if (!Fijar(ref _seleccion, value)) return;
            RellenarDetalle();
            Avisar(nameof(HaySeleccion), nameof(TituloDetalle), nameof(DescripcionDetalle), nameof(PeriodicidadDetalle), nameof(PlazoDetalle), nameof(CanalDetalle),
                nameof(PagoDetalle), nameof(OrigenDetalle), nameof(ProcesoDetalle), nameof(HayProceso), nameof(SoloConsultaDetalle), nameof(EsSoloConsultaDetalle), nameof(TextoCasillasDetalle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HaySeleccion => _seleccion is not null;
    public string TituloDetalle => _seleccion is null ? "" : $"{_seleccion.Id} · {_seleccion.Nombre}";
    public string DescripcionDetalle => _seleccion?.Definicion.Descripcion ?? "";
    public string PeriodicidadDetalle => _seleccion?.Periodicidad ?? "";
    public string PlazoDetalle => _seleccion is null ? "" : _seleccion.Definicion.Plazo + " Si acaba en sábado, domingo o festivo nacional, pasa al siguiente día hábil.";
    public string CanalDetalle => _seleccion?.Definicion.TextoCanal ?? "";
    public string PagoDetalle => _seleccion?.Definicion.Pago ?? "";
    public string OrigenDetalle => _seleccion?.Definicion.Origen ?? "";
    public string ProcesoDetalle => _seleccion?.Definicion.TextoProceso ?? "";
    public bool HayProceso => _seleccion?.Definicion.Proceso is not (null or ProcesoModelo.Ninguno);
    public string? SoloConsultaDetalle => _seleccion?.SoloConsulta is { } s ? $"Solo consulta: {s} Puedes ver su calendario, lo que consta presentado en la AEAT y sus justificantes." : null;
    public bool EsSoloConsultaDetalle => _seleccion?.EsSoloConsulta == true;
    public string TextoCasillasDetalle => _seleccion?.Definicion.Informativa == true ? "Registros del periodo marcado" : "Casillas del periodo marcado";

    // ---------- carga ----------

    /// <summary>Carga la pantalla; con <paramref name="modelo"/> y <paramref name="periodo"/>, deja ese periodo elegido y marcado (enlaces desde otras pantallas).</summary>
    public async Task CargarAsync(string? modelo = null, string? periodo = null, bool consultaDelDia = false)
    {
        try
        {
            if (Modelos.Count == 0) Estado = EstadoVista.Cargando;
            Ocupado = true;
            var entorno = await _ctx.Servicio.Presentacion.EntornoAsync();
            EsPruebas = entorno == EntornoAeat.Pruebas;
            Avisar(nameof(EsPruebas), nameof(AyudaEntorno));
            _puedePresentar = await _ctx.PuedePresentar();
            var periodos = new Dictionary<string, IReadOnlyList<PeriodoModelo>>();
            var errores = new List<string>();
            foreach (var m in _ctx.Servicio.Modelos)
            {
                try { periodos[m.Definicion.Id] = await m.PeriodosAsync(_ctx.Trabajo); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errores.Add($"{m.Definicion.Id}: {ex.Message}");
                    periodos[m.Definicion.Id] = [.. PlazosModelos.Periodos(m.Definicion.Id).Select(p =>
                    {
                        var (desde, hasta) = PlazosModelos.Plazo(m.Definicion.Id, _ctx.Trabajo.Ejercicio, p);
                        return new PeriodoModelo(m.Definicion.Id, _ctx.Trabajo.Ejercicio, p, desde, hasta, Estado303.SinPresentar, null, null, null, [], [$"No he podido calcular el {m.Definicion.Id}: {ex.Message}"], [],
                            $"El programa no ha podido calcularlo ({ex.Message}).");
                    })];
                }
            }
            _periodos = periodos;
            var elegidos = await _ctx.Servicio.ElegidosAsync(_ctx.Trabajo.IdSociedad);
            _elegidos = elegidos is not null;
            _visibles = elegidos ?? ServicioModelosTributarios.PorDefecto(periodos);
            Pintar(modelo ?? _seleccion?.Id, periodo);
            Estado = EstadoVista.Contenido;
            if (errores.Count > 0) _ctx.Avisar("No he podido calcular algún modelo: " + string.Join(" · ", errores));
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido calcular los modelos: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
        if (consultaDelDia && Estado == EstadoVista.Contenido && await _ctx.Servicio.TocaConsultaDelDiaAsync(_ctx.Trabajo.IdSociedad))
            await ConsultarAsync(automatica: true);
        else await TextoUltimaConsultaAsync();
    }

    private async Task TextoUltimaConsultaAsync()
    {
        var u = await _ctx.Servicio.UltimaConsultaAsync(_ctx.Trabajo.IdSociedad);
        TextoConsulta = u is DateTime d ? $"Última consulta a la AEAT: {d:dd/MM/yyyy HH:mm}" : "Aún no se ha consultado a la AEAT lo presentado fuera del programa";
    }

    private void Pintar(string? modelo, string? periodo)
    {
        var hoy = Hoy;
        var marcados = _filas.Values.Where(f => f.Marcado).Select(f => f.Clave).ToHashSet();
        _filas.Clear();
        foreach (var (id, lista) in _periodos)
            foreach (var p in lista) _filas[p.Clave] = new FilaPeriodoVM(p, hoy, AlMarcar) ;
        foreach (var k in marcados) if (_filas.TryGetValue(k, out var f)) f.FijarMarca(true);

        Modelos.Clear();
        foreach (var def in DefinicionModelo.Todos.Where(d => _visibles.Contains(d.Id) && _periodos.ContainsKey(d.Id)))
        {
            var lista = _periodos[def.Id];
            var fichas = lista.Select(p => new FichaPeriodoVM(p.Nombre, "", p.Estado(hoy).ToString().ToUpperInvariant(), $"{p.Nombre}: {p.TextoEstado(hoy)}. {p.AyudaEstado(hoy)}",
                new ComandoDelegado(() => IrA(p.Modelo, p.Periodo)))).ToList();
            var siguiente = lista.FirstOrDefault(p => p.Estado(hoy) is EstadoPeriodoModelo.Vencido or EstadoPeriodoModelo.EnPlazo or EstadoPeriodoModelo.AunNoAbre);
            var proximo = siguiente is null ? "todo presentado" : (siguiente.Estado(hoy) == EstadoPeriodoModelo.Vencido ? "venció el " : "hasta el ") + Formatos.Fecha(siguiente.PlazoHasta);
            Modelos.Add(new FilaModeloVM(def, fichas, proximo, lista.Select(p => p.SoloConsulta).FirstOrDefault(s => s is not null)));
        }
        Avisar(nameof(TextoModelos), nameof(SinModelos));
        RellenarAgenda();
        var elegido = Modelos.FirstOrDefault(m => m.Id == modelo) ?? Modelos.FirstOrDefault(m => m.Id == "303") ?? Modelos.FirstOrDefault();
        _seleccion = null;
        Seleccion = elegido;
        RellenarPlazos();
        if (modelo is not null && periodo is not null) IrA(modelo, periodo);
    }

    private IEnumerable<FilaPeriodoVM> Visibles => _filas.Values.Where(f => _visibles.Contains(f.Id));

    private void RellenarAgenda()
    {
        var hoy = Hoy;
        Vencidos.Clear(); Desactualizados.Clear(); Proximos.Clear();
        var vencidos = Visibles.Where(f => f.EstadoPeriodo == EstadoPeriodoModelo.Vencido).OrderBy(f => f.Datos.PlazoHasta).ThenBy(f => f.Id).ToList();
        foreach (var f in vencidos.Take(5))
            Vencidos.Add(Ficha(f, $"venció el {f.PlazoHasta}", "VENCIDO"));
        TextoMasVencidos = vencidos.Count > 5 ? $"y {vencidos.Count - 5} más (vista «Por plazo», filtro Pendientes)" : "";
        foreach (var f in Visibles.Where(f => f.EstadoPeriodo == EstadoPeriodoModelo.Desactualizado))
            Desactualizados.Add(Ficha(f, $"presentado {f.Presentado}", "DESACTUALIZADO"));
        var pendientes = Visibles.Where(f => f.EstadoPeriodo is EstadoPeriodoModelo.EnPlazo or EstadoPeriodoModelo.AunNoAbre).OrderBy(f => f.Datos.PlazoHasta).ThenBy(f => f.Id).ToList();
        foreach (var f in pendientes.Take(4))
            Proximos.Add(Ficha(f, f.EstadoPeriodo == EstadoPeriodoModelo.EnPlazo ? $"hasta el {f.PlazoHasta}" : $"del {f.Datos.PlazoDesde:dd/MM} al {f.PlazoHasta}", f.ClaseEstado));
        TextoMasProximos = pendientes.Count > 4 ? $"y {pendientes.Count - 4} más" : pendientes.Count == 0 ? "ninguno pendiente en este ejercicio" : "";
        Avisar(nameof(HayVencidos), nameof(HayDesactualizados), nameof(HayProximos), nameof(TextoMasProximos), nameof(TextoMasVencidos));
    }

    private FichaPeriodoVM Ficha(FilaPeriodoVM f, string detalle, string clase) =>
        new($"{f.Id} {f.Periodo}", detalle, clase, $"{f.Estado}. {f.AyudaEstado} Pulsa para ir a este periodo.", new ComandoDelegado(() => IrA(f.Id, f.Datos.Periodo)));

    private void RellenarDetalle()
    {
        Periodos.Clear();
        if (_seleccion is null) return;
        foreach (var f in _filas.Values.Where(f => f.Id == _seleccion.Id).OrderBy(f => f.Datos.PlazoDesde)) Periodos.Add(f);
    }

    private void RellenarPlazos()
    {
        PeriodosPlazo.Clear();
        var lista = Visibles.OrderBy(f => f.Datos.PlazoHasta).ThenBy(f => f.Id, StringComparer.Ordinal).AsEnumerable();
        if (_filtro == "Pendientes") lista = lista.Where(f => f.EstadoPeriodo is not (EstadoPeriodoModelo.Presentado or EstadoPeriodoModelo.SinObligacion));
        if (_filtro == "Presentados") lista = lista.Where(f => f.EstadoPeriodo is EstadoPeriodoModelo.Presentado or EstadoPeriodoModelo.Desactualizado);
        foreach (var f in lista) PeriodosPlazo.Add(f);
        Avisar(nameof(TextoPlazos));
    }

    /// <summary>Elige el modelo y deja marcado solo ese periodo (fichas, franja y enlaces de otras pantallas).</summary>
    public void IrA(string modelo, string periodo)
    {
        foreach (var f in _filas.Values) f.FijarMarca(false);
        if (_porPlazo && _filtro != "Todos") Filtro = "Todos";
        if (Modelos.FirstOrDefault(m => m.Id == modelo) is { } m && !ReferenceEquals(m, _seleccion)) Seleccion = m;
        if (_filas.Values.FirstOrDefault(f => f.Id == modelo && f.Datos.Periodo == periodo) is { } fila)
        {
            fila.FijarMarca(true);
            Marcada = fila;
        }
        AlMarcar();
    }

    /// <summary>La fila a la que hay que llevar la vista (la última que se ha marcado desde un enlace).</summary>
    public FilaPeriodoVM? Marcada { get; private set; }

    private void AlMarcar() => Avisar(nameof(TextoMarcados), nameof(HayMarcados), nameof(TodosMarcadosDetalle), nameof(TodosMarcadosPlazo));

    public IReadOnlyList<FilaPeriodoVM> Marcados => [.. _filas.Values.Where(f => f.Marcado).OrderBy(f => f.Datos.PlazoHasta).ThenBy(f => f.Id)];
    public bool HayMarcados => _filas.Values.Any(f => f.Marcado);
    public string TextoMarcados => Marcados.Count switch
    {
        0 => "Marca uno o varios periodos y usa el clic derecho (o «⋯») para ver el borrador, generar el fichero, presentar o abrir el justificante.",
        1 => $"Marcado: {Marcados[0].Id} {Marcados[0].Periodo}. Clic derecho o «⋯» para sus acciones.",
        var n => $"{n} periodos marcados. Clic derecho para sus acciones en bloque.",
    };

    public bool? TodosMarcadosDetalle
    {
        get => Periodos.Count == 0 ? false : Periodos.All(p => p.Marcado) ? true : Periodos.Any(p => p.Marcado) ? null : false;
        set { var v = value == true; foreach (var p in Periodos) p.FijarMarca(v); AlMarcar(); }
    }

    public bool? TodosMarcadosPlazo
    {
        get => PeriodosPlazo.Count == 0 ? false : PeriodosPlazo.All(p => p.Marcado) ? true : PeriodosPlazo.Any(p => p.Marcado) ? null : false;
        set { var v = value == true; foreach (var p in PeriodosPlazo) p.FijarMarca(v); AlMarcar(); }
    }

    /// <summary>
    /// Antes de abrir el menú sobre una fila: si no estaba marcada, queda marcada solo ella (como en la maqueta); si lo estaba, el menú
    /// actúa sobre todas las marcadas.
    /// </summary>
    public void PrepararMenu(FilaPeriodoVM? fila)
    {
        if (fila is not null && !fila.Marcado)
        {
            foreach (var f in _filas.Values) f.FijarMarca(false);
            fila.FijarMarca(true);
            AlMarcar();
        }
    }

    /// <summary>Opciones del menú contextual para los periodos marcados (<see cref="AccionesPeriodos"/>).</summary>
    public IReadOnlyList<OpcionMenuVM> OpcionesMenu()
    {
        var marcados = Marcados;
        var r = new List<OpcionMenuVM>();
        foreach (var a in AccionesPeriodos.De([.. marcados.Select(m => m.Datos)], Hoy, _puedePresentar))
        {
            if (a.Accion is AccionModelo.AbrirJustificante or AccionModelo.AbrirCasillas && marcados.Count == 1 && r.Count > 0) r.Add(new OpcionMenuVM("", null, false, null, true));
            var accion = a.Accion;
            r.Add(new OpcionMenuVM(a.Texto, a.NoSePuede ?? a.Ayuda, a.Disponible && !_ocupado, new ComandoDelegado(() => _ = AccionAsync(accion, marcados))));
        }
        return r;
    }

    // ---------- acciones ----------

    public async Task AccionAsync(AccionModelo accion, IReadOnlyList<FilaPeriodoVM> filas)
    {
        if (filas.Count == 0) return;
        if (filas.Count > 1)
        {
            switch (accion)
            {
                case AccionModelo.VerBorrador: foreach (var f in filas.Where(f => f.Datos.Generable && Hoy >= f.Datos.PlazoDesde)) await VerBorradorAsync(f); break;
                case AccionModelo.GenerarFichero: await GenerarFicherosAsync([.. filas.Where(f => f.Datos.Generable)]); break;
                case AccionModelo.AbrirJustificante: foreach (var f in filas.Where(f => f.Datos.Presentado)) await AbrirJustificanteAsync(f); break;
            }
            return;
        }
        var fila = filas[0];
        switch (accion)
        {
            case AccionModelo.VerBorrador: await VerBorradorAsync(fila); break;
            case AccionModelo.GenerarFichero: await GenerarFicheroAsync(fila); break;
            case AccionModelo.Presentar: await PresentarAsync(fila, false); break;
            case AccionModelo.PresentarComplementaria: await PresentarAsync(fila, true); break;
            case AccionModelo.AbrirJustificante: await AbrirJustificanteAsync(fila); break;
            case AccionModelo.DatosPresentacion: DatosPresentacion(fila); break;
            case AccionModelo.CopiarCsv:
                if (fila.Datos.Ultima is { } u) { _ctx.Copiar?.Invoke(u.Csv); _ctx.Avisar($"CSV copiado: {u.Csv}"); }
                break;
            case AccionModelo.AbrirCasillas: await AbrirCasillasAsync(fila); break;
            case AccionModelo.AbrirProceso: AbrirProceso(fila.Modelo, fila.Datos.Periodo); break;
        }
    }

    private void AbrirProceso(DefinicionModelo def, string periodo) => _ctx.AbrirProceso?.Invoke(def.Proceso, def.Id, periodo);

    private async Task AbrirCasillasAsync(FilaPeriodoVM fila)
    {
        Ocupado = true;
        try
        {
            var d = await _ctx.Servicio.Modelo(fila.Id).DetalleAsync(_ctx.Trabajo, fila.Datos.Periodo);
            _abrirDetalle?.Invoke(d, $"{fila.Id} {fila.Periodo}");
        }
        catch (Exception ex) { _dialogos.Error($"No he podido calcular el {fila.Id}", ex.Message); }
        finally { Ocupado = false; }
    }

    private async Task<DeclaracionGenerada?> GenerarAsync(FilaPeriodoVM fila, bool complementaria, string accion)
    {
        var r = await _ctx.Servicio.Modelo(fila.Id).GenerarAsync(_ctx.Trabajo, fila.Datos.Periodo, complementaria);
        if (r.EsCorrecto) return r.Valor;
        _dialogos.Error($"No se puede {accion} el {fila.Id} del {fila.Periodo}", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
        return null;
    }

    private async Task VerBorradorAsync(FilaPeriodoVM fila)
    {
        Ocupado = true;
        try
        {
            if (await GenerarAsync(fila, false, "generar") is not { } g) return;
            if (g.Directa is { } d)
            {
                var v = await _ctx.Servicio.Presentacion.ValidarAsync(d);
                if (!v.Correcta)
                {
                    _dialogos.Error($"La AEAT no valida el {fila.Id} del {fila.Periodo}", "La AEAT ha devuelto estos errores (no se ha presentado nada):" + Environment.NewLine + Environment.NewLine +
                        string.Join(Environment.NewLine, v.Errores) + Environment.NewLine + Environment.NewLine + "Corrige lo que indica y vuelve a pedir el borrador.");
                    return;
                }
                AbrirPdf(v.Pdf!, $"{fila.Id} {_ctx.Trabajo.Ejercicio} {fila.Datos.Periodo} borrador");
                if (v.Avisos.Count > 0) _ctx.Avisar("Avisos de la AEAT: " + string.Join(" · ", v.Avisos));
            }
            else
            {
                var b = await _ctx.Servicio.Tgvi.PrepararAsync(_ctx.Trabajo, g.Informativa!);
                if (b.VistaPrevia is { } pdf) AbrirPdf(pdf, $"{fila.Id} {_ctx.Trabajo.Ejercicio} {fila.Datos.Periodo} borrador");
                var notas = b.Bloqueos.Concat(b.Errores.Take(10).Select(e => $"Registro erróneo: {e}")).Concat(b.AvisosAeat.Take(10).Select(a => $"Aviso de la AEAT: {a}")).ToList();
                if (b.VistaPrevia is null || notas.Count > 0)
                    _dialogos.Informar($"Validación del {fila.Id} del {fila.Periodo} en TGVI Online",
                        $"La AEAT ha validado el envío: {b.Correctos} registros correctos y {b.Erroneos} erróneos. No se ha presentado nada.", notas);
            }
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido obtener el borrador", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}El borrador lo genera la AEAT al validar el fichero: comprueba la conexión a internet " +
                (EsPruebas ? "(el entorno de pruebas solo funciona los días laborables de 8:00 a 15:00) " : "") + "y vuelve a intentarlo.");
        }
        finally { Ocupado = false; }
    }

    private async Task<string> CarpetaModeloAsync(string modelo)
    {
        var raiz = _ctx.CarpetaEmpresa is { } c ? await c() : null;
        return Path.Combine(raiz ?? Path.GetTempPath(), _ctx.Trabajo.Ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture), "Modelos AEAT", modelo);
    }

    private async Task GenerarFicheroAsync(FilaPeriodoVM fila)
    {
        Ocupado = true;
        try
        {
            if (await GenerarAsync(fila, false, "generar") is not { } g) return;
            var carpeta = await CarpetaModeloAsync(fila.Id);
            if (_dialogos.GuardarComo(carpeta, g.NombreFichero) is not { } ruta) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllTextAsync(ruta, g.Fichero, Encoding.Latin1);
            _ctx.Avisar($"Fichero del {fila.Id} del {fila.Periodo} guardado en {ruta}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _dialogos.Error("No he podido guardar el fichero", ex.Message); }
        finally { Ocupado = false; }
    }

    private async Task GenerarFicherosAsync(IReadOnlyList<FilaPeriodoVM> filas)
    {
        Ocupado = true;
        var hechos = new List<string>();
        var fallos = new List<string>();
        try
        {
            foreach (var f in filas)
            {
                var r = await _ctx.Servicio.Modelo(f.Id).GenerarAsync(_ctx.Trabajo, f.Datos.Periodo, false);
                if (!r.EsCorrecto) { fallos.Add($"{f.Id} {f.Periodo}: {r.Error!.Mensaje}"); continue; }
                var ruta = Path.Combine(await CarpetaModeloAsync(f.Id), r.Valor.NombreFichero);
                Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
                await File.WriteAllTextAsync(ruta, r.Valor.Fichero, Encoding.Latin1);
                hechos.Add(ruta);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { fallos.Add(ex.Message); }
        finally { Ocupado = false; }
        _dialogos.Informar("Ficheros de los modelos", $"He guardado {hechos.Count} fichero{(hechos.Count == 1 ? "" : "s")}" + (fallos.Count > 0 ? $" y {fallos.Count} no se han podido generar." : "."),
            [.. hechos, .. fallos]);
    }

    private void AbrirPdf(byte[] pdf, string nombre)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF", "Modelos");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, $"{nombre} {DateTime.Now:HHmmss}.pdf");
        File.WriteAllBytes(ruta, pdf);
        _ctx.AbrirFichero?.Invoke(ruta);
    }

    private async Task PresentarAsync(FilaPeriodoVM fila, bool complementaria)
    {
        if (!await _ctx.PuedePresentar()) { _dialogos.Error("No puedes presentar modelos", "Tu grupo de operadores no tiene el permiso «Presentar modelos» (MODELOS.PRESENTAR). Pídeselo a quien administra la seguridad."); return; }
        Ocupado = true;
        var titulo = $"{fila.Id} del {fila.Periodo} {_ctx.Trabajo.Ejercicio}";
        try
        {
            if (await GenerarAsync(fila, complementaria, "presentar") is not { } g) return;
            if (g.BloqueosPropios.Count > 0) { _dialogos.Error($"No se puede presentar el {titulo}", string.Join(Environment.NewLine + Environment.NewLine, g.BloqueosPropios)); return; }
            Nucleo.Resultados.Resultado<PresentacionHecha> p;
            EntornoAeat entorno;
            if (g.Directa is { } aeat)
            {
                if (aeat.FormaPago == 'I')
                {
                    var nrc = _dialogos.PedirNrc($"NRC del pago del {titulo}",
                        $"El {fila.Id} sale a ingresar {Formatos.Importe(aeat.ImporteAIngresar)} y ya no se puede domiciliar (el plazo de domiciliación ha terminado o aún no ha empezado). " +
                        $"Paga ese importe exacto en tu banco (o en la Sede de la AEAT, «Pagar, aplazar y consultar») con el NIF de la sociedad, el modelo {fila.Id}, el ejercicio y el periodo, " +
                        "y escribe el NRC de 22 caracteres que te den.");
                    if (nrc is null) return;
                    aeat = aeat with { Nrc = nrc.Trim().ToUpperInvariant() };
                }
                var b = await _ctx.Servicio.Presentacion.PrepararAsync(_ctx.Trabajo, aeat);
                entorno = b.Entorno;
                if (!_dialogos.Conformidad(ConformidadPresentacion.De(b), () => { if (b.Validacion.Pdf is { } pdf) AbrirPdf(pdf, $"{fila.Id} {_ctx.Trabajo.Ejercicio} {fila.Datos.Periodo} a presentar"); }))
                    return;
                p = await _ctx.Servicio.Presentacion.PresentarAsync(_ctx.Trabajo, b, conforme: true, _ctx.Actor);
            }
            else
            {
                var b = await _ctx.Servicio.Tgvi.PrepararAsync(_ctx.Trabajo, g.Informativa!);
                entorno = b.Entorno;
                if (!_dialogos.Conformidad(ConformidadPresentacion.De(b), () => { if (b.VistaPrevia is { } pdf) AbrirPdf(pdf, $"{fila.Id} {_ctx.Trabajo.Ejercicio} {fila.Datos.Periodo} a presentar"); }))
                    return;
                p = await _ctx.Servicio.Tgvi.PresentarAsync(_ctx.Trabajo, b, conforme: true, _ctx.Actor);
            }
            if (!p.EsCorrecto)
            {
                _dialogos.Error($"No se ha presentado el {titulo}", string.Join(Environment.NewLine + Environment.NewLine, p.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var gu = p.Valor.Guardada;
            var detalles = new List<string>
            {
                $"CSV: {gu.Csv}", $"Justificante: {gu.Justificante}", $"Expediente: {gu.Expediente}", $"Fecha y hora: {gu.FechaHora:dd/MM/yyyy HH:mm}",
                gu.RutaPdf is { } ruta ? $"Justificante archivado en {ruta}" : "El PDF justificante no se ha podido archivar todavía: «Abrir el justificante» lo vuelve a intentar.",
            };
            if (fila.Id == "303") detalles.Add("La liquidación de IVA de este trimestre pasa a usar el importe presentado: revísala en Contabilidad › Liquidación de IVA.");
            detalles.AddRange(p.Valor.Avisos);
            _dialogos.Informar(entorno == EntornoAeat.Pruebas ? $"{fila.Id} presentado en el entorno de PRUEBAS de la AEAT" : $"{fila.Id} presentado en la AEAT",
                $"La AEAT ha aceptado el {titulo}{(complementaria ? " (complementaria)" : "")}.", detalles);
            _ctx.Avisar($"{fila.Id} {fila.Periodo} presentado: CSV {gu.Csv}.");
        }
        catch (Exception ex)
        {
            _dialogos.Error($"No se ha presentado el {titulo}", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Antes de repetirlo pulsa «Consultar a la AEAT»: si la AEAT llegó a recibirlo, saldrá como presentado.");
        }
        finally { Ocupado = false; }
        await CargarAsync(fila.Id, fila.Datos.Periodo);
    }

    private async Task AbrirJustificanteAsync(FilaPeriodoVM fila)
    {
        if (fila.Datos.Ultima is not { } u) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.Presentacion.ArchivarJustificanteAsync(_ctx.Trabajo, u);
            if (!r.EsCorrecto) { _dialogos.Error($"No se puede abrir el justificante del {fila.Id} del {fila.Periodo}", r.Error!.TextoOperador()); return; }
            _ctx.AbrirFichero?.Invoke(r.Valor);
        }
        finally { Ocupado = false; }
    }

    private void DatosPresentacion(FilaPeriodoVM fila)
    {
        if (fila.Datos.Ultima is not { } u) return;
        var externa = u.EsExterna;
        var lineas = new List<string>
        {
            $"CSV: {u.Csv}", $"Justificante: {u.Justificante}", $"Expediente: {u.Expediente ?? "—"}", $"Fecha y hora: {u.FechaHora:dd/MM/yyyy HH:mm}",
            externa ? "Importe: la consulta de la AEAT no lo da" : $"{(fila.Modelo.Informativa ? "Importe declarado" : "Resultado")}: {Formatos.Importe(u.Resultado)}",
            externa ? "Forma de pago: la consulta de la AEAT no la da" : $"Forma de pago: {u.FormaPago ?? (fila.Modelo.Informativa ? "sin pago (informativa)" : "—")}",
            externa ? "Firmado por: la consulta de la AEAT no dice quién lo presentó" : $"Firmado por: {u.NifPresentador} {u.Presentador}",
            $"Operador: {(externa ? "fuera del programa" : u.Operador)}",
            $"Entorno: {(u.EsPruebas ? "PRUEBAS" : "producción")}",
            externa ? "Fichero enviado: no lo hay, no se presentó desde el programa" : "Fichero enviado y respuesta completa de la AEAT: guardados en la base de datos (PresentacionesAeat)",
        };
        if (u.ComplementariaDe is { } ant) lineas.Add($"Rectifica o sustituye la declaración {ant}");
        if (u.RutaPdf is { } ruta) lineas.Add($"Justificante archivado en {ruta}");
        if (!string.IsNullOrWhiteSpace(u.Avisos)) lineas.AddRange(u.Avisos.Split('\n').Select(a => a.TrimEnd('\r')).Where(a => a.Length > 0));
        if (fila.Datos.Presentaciones.Count > 1) lineas.Add($"Este periodo tiene {fila.Datos.Presentaciones.Count} presentaciones; esta es la última.");
        _dialogos.Informar($"Presentación del {fila.Id} · {fila.Periodo} de {_ctx.Trabajo.Ejercicio}",
            externa ? "Presentado fuera del programa. Estos datos los devuelve la consulta de la AEAT." : "Presentado desde el programa. Se guarda todo lo que respondió la AEAT.", lineas);
    }

    private async Task ConsultarAsync(bool automatica)
    {
        Ocupado = true;
        try
        {
            var modelos = _visibles.Count > 0 ? _visibles : DefinicionModelo.Todos.Select(d => d.Id).ToHashSet();
            var r = await _ctx.Servicio.ConsultarAeatAsync(_ctx.Trabajo, modelos);
            var texto = r.Nuevas.Count > 0
                ? $"La AEAT devuelve {r.Nuevas.Count} presentación{(r.Nuevas.Count == 1 ? "" : "es")} que no estaba{(r.Nuevas.Count == 1 ? "" : "n")} en el programa ({string.Join(", ", r.Nuevas.Select(n => $"{n.Modelo} {PlazosModelos.Nombre(n.Periodo)}"))}). Añadida{(r.Nuevas.Count == 1 ? "" : "s")}."
                : r.Errores.Count == 0 ? "La AEAT no devuelve nada nuevo: todo lo presentado ya estaba aquí." : "No he podido consultar a la AEAT.";
            if (r.Errores.Count > 0 && !automatica)
                _dialogos.Error("Consulta a la AEAT", texto + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, r.Errores));
            else if (!automatica || r.Nuevas.Count > 0) _ctx.Avisar(texto + (r.SinConsulta.Count > 0 && !automatica ? $" Las informativas ({string.Join(", ", r.SinConsulta)}) no tienen consulta en la AEAT: sale lo presentado desde el programa." : ""));
            if (r.Nuevas.Count > 0) { Ocupado = false; await CargarAsync(); }
        }
        catch (Exception ex) when (!automatica) { _dialogos.Error("No he podido consultar a la AEAT", ex.Message); }
        catch (Exception ex) { _ctx.Avisar($"No he podido hacer la consulta del día a la AEAT: {ex.Message}"); }
        finally { Ocupado = false; }
        await TextoUltimaConsultaAsync();
    }

    private async Task ElegirModelosAsync()
    {
        var lista = DefinicionModelo.Todos.Select(d => (d, _visibles.Contains(d.Id))).ToList();
        if (_dialogos.ElegirModelos(lista) is not { } elegidos) return;
        await _ctx.Servicio.GuardarElegidosAsync(_ctx.Trabajo.IdSociedad, elegidos);
        _visibles = elegidos.ToHashSet();
        _elegidos = true;
        Pintar(_seleccion?.Id, null);
        _ctx.Avisar($"Modelos de la sociedad: {(elegidos.Count == 0 ? "ninguno" : string.Join(", ", DefinicionModelo.Todos.Where(d => elegidos.Contains(d.Id)).Select(d => d.Id)))}.");
    }

    /// <summary>Si la sociedad ha elegido sus modelos (si no, salen los que tienen datos o presentaciones).</summary>
    public bool ModelosElegidos => _elegidos;
}
