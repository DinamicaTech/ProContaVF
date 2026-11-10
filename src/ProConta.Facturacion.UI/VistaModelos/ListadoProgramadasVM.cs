using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Programadas;
using ProConta.Seguridad.Permisos;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Facturacion.UI.VistaModelos;

/// <summary>Fila del listado de programadas: la programada, su cliente y cuántas facturas ha generado en el ejercicio.</summary>
public sealed class FilaProgramadaVM(FacturaProgramada datos, string cliente, int generadasEjercicio) : ModeloVista
{
    public FacturaProgramada Datos { get; } = datos;
    public int Id => Datos.IdFacturaProgramada;
    public string Codigo => Datos.Rotulo;
    public string Cliente { get; } = cliente;
    public string Descripcion => Datos.Plantilla.Descripcion;
    public string Periodicidad => Datos.Periodicidad.EsPuntual ? "Una vez" : Datos.Periodicidad.Codigo[..1] + Datos.Periodicidad.Codigo[1..].ToLowerInvariant();
    public DateTime? ProximaOrden => Datos.ProximaCalculada;
    public string Proxima => Datos.ProximaCalculada is { } d ? Formatos.Fecha(d) : "—";
    public string Final => ReglasProgramada.TextoFinal(Datos);
    public int Generadas { get; } = generadasEjercicio;
    public string TextoGeneradas => Generadas.ToString(Formatos.Cultura);
    public decimal Total => Datos.Plantilla.Totales.ImporteTotal.Valor;
    public string TextoTotal => Formatos.Importe(Total);
    public string AlGenerar => Datos.AlGenerar == Programadas.AlGenerar.Validar ? "Validada" : "Borrador";
    public string Estado => Datos.TextoEstado;

    /// <summary>Orden por defecto: activas, pausadas y finalizadas; dentro de cada grupo, por próxima emisión.</summary>
    public int Grupo => Datos.Finalizada ? 2 : Datos.Activa ? 0 : 1;

    public bool Contiene(string texto) =>
        string.IsNullOrWhiteSpace(texto) || new[] { Codigo, Cliente, Descripcion, Datos.Plantilla.NotasInternas }
            .Any(c => c?.Contains(texto.Trim(), StringComparison.CurrentCultureIgnoreCase) == true);
}

/// <summary>
/// Pantalla Facturas programadas (facturacion/pantallas 2026-11-04): listado filtrable de las programadas de la sociedad
/// y, a la derecha, el detalle de la activa con su programación, líneas, totales, notas e historial. Admite selección
/// múltiple y todas las acciones están en el menú contextual: unas son de una sola programada (Abrir, Ver sus facturas) y
/// las demás actúan sobre todas las seleccionadas.
/// </summary>
public sealed class ListadoProgramadasVM : ModeloVista, IPestanaTrabajo
{
    /// <summary>Valor del filtro «Activas vencidas»: activas con la próxima emisión hoy o antes, las que toca facturar.</summary>
    private const string Vencidas = "Vencida";

    private readonly ModuloFacturacion _modulo;
    private readonly ServiciosFacturacion _s;
    private List<FilaProgramadaVM> _todas = [];
    private object? _vista;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private FilaProgramadaVM? _activa;
    private IReadOnlyList<FilaProgramadaVM> _seleccion = [];
    private ClienteElegible? _cliente;
    private string _texto = "";
    private OpcionFiltro _filtroEstado;
    private bool _puedeEditar;
    private int _cargas;

    internal ListadoProgramadasVM(ModuloFacturacion modulo)
    {
        _modulo = modulo;
        _s = modulo.Servicios;
        OpcionesEstado = [new("Todos los estados", null), new("Activas", "Activa"), new("Activas vencidas", Vencidas), new("Pausadas", "Pausada"), new("Finalizadas", "Finalizada")];
        _filtroEstado = OpcionesEstado[1];

        NuevaCommand = new ComandoDelegado(_modulo.NuevaProgramada, () => _puedeEditar);
        AbrirCommand = new ComandoDelegado(() => { if (Ambito.Count == 1) _modulo.AbrirProgramada(Ambito[0].Id); }, () => Ambito.Count == 1);
        VerFacturasCommand = new ComandoDelegado(() => { if (Ambito.Count == 1) _modulo.VerFacturasDeProgramada(Ambito[0].Id); }, () => Ambito.Count == 1);
        PausarCommand = new ComandoDelegado(() => _ = CambiarActivasAsync(false), () => _puedeEditar && APausar.Count > 0);
        ReanudarCommand = new ComandoDelegado(() => _ = CambiarActivasAsync(true), () => _puedeEditar && AReanudar.Count > 0);
        DuplicarCommand = new ComandoDelegado(() => _ = DuplicarAsync(), () => _puedeEditar && Ambito.Count > 0);
        EliminarCommand = new ComandoDelegado(() => _ = EliminarAsync(), () => _puedeEditar && Ambito.Count > 0);
        GenerarSeleccionadasCommand = new ComandoDelegado(() => _ = GenerarAsync(AGenerar.Select(f => f.Id).ToHashSet()), () => _puedeEditar && AGenerar.Count > 0);
        GenerarCommand = new ComandoDelegado(() => _ = GenerarAsync(null), () => _puedeEditar);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        LimpiarFiltrosCommand = new ComandoDelegado(LimpiarFiltros, () => HayFiltros);
        VerTodasCommand = new ComandoDelegado(() => FiltroEstado = OpcionesEstado[0], () => _filtroEstado != OpcionesEstado[0]);
    }

    // ---------- pestaña ----------

    public string Titulo => "Facturas programadas";
    public string Ayuda => $"Facturas programadas de {_s.RazonSocial}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaListadoProgramadas { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _modulo.ProgramadasCerrado();

    // ---------- estado ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string TituloEstado => Estado switch
    {
        EstadoVista.Vacio => _todas.Count == 0 ? "Aún no hay facturas programadas en esta sociedad" : _filtroEstado.Valor == Vencidas && _texto.Length == 0 ? "No hay programadas vencidas: hoy no toca facturar ninguna" : HayFiltros ? "Ninguna programada cumple los filtros" : "No hay programadas activas",
        EstadoVista.Error => "No he podido leer las facturas programadas",
        EstadoVista.Cargando => "Leyendo facturas programadas…",
        _ => ""
    };
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string? AccionEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 => _puedeEditar ? "Nueva programada" : null,
        EstadoVista.Vacio when !HayFiltros => "Ver todas",
        EstadoVista.Vacio => "Quitar filtros",
        EstadoVista.Error => "Reintentar",
        _ => null
    };
    public ICommand? ComandoEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 => NuevaCommand,
        EstadoVista.Vacio when !HayFiltros => VerTodasCommand,
        EstadoVista.Vacio => LimpiarFiltrosCommand,
        EstadoVista.Error => RefrescarCommand,
        _ => null
    };

    // ---------- filtros ----------

    public IReadOnlyList<OpcionFiltro> OpcionesEstado { get; }
    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public OpcionFiltro FiltroEstado { get => _filtroEstado; set { if (value is not null && Fijar(ref _filtroEstado, value)) Filtrar(); } }

    /// <summary>Hay filtros si la vista no es la de partida: sin texto y solo las activas.</summary>
    public bool HayFiltros => _texto.Length > 0 || _filtroEstado != OpcionesEstado[1];

    private void LimpiarFiltros()
    {
        _texto = "";
        _filtroEstado = OpcionesEstado[1];
        Avisar(nameof(Texto), nameof(FiltroEstado));
        Filtrar();
    }

    private bool CumpleEstado(FilaProgramadaVM p) => _filtroEstado.Valor switch
    {
        null => true,
        Vencidas => p.Grupo == 0 && p.Datos.ProximaCalculada is { } d && d <= DateTime.Today,
        var e => p.Estado == e
    };

    private void Filtrar()
    {
        var id = _activa?.Id;
        Filas.Clear();
        foreach (var p in _todas.Where(p => p.Contiene(_texto) && CumpleEstado(p))) Filas.Add(p);
        Activa = Filas.FirstOrDefault(p => p.Id == id) ?? Filas.FirstOrDefault();
        Estado = Filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = _todas.Count == 0 ? "Crea la primera con «Nueva programada», o con «Programar…» desde una factura." : HayFiltros ? "Cambia o quita los filtros para ver más programadas." : "Las demás están pausadas o finalizadas.";
        Avisar(nameof(HayFiltros), nameof(Pie), nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- filas y activa ----------

    public ObservableCollection<FilaProgramadaVM> Filas { get; } = [];

    /// <summary>Programada activa: la del detalle y la de las acciones.</summary>
    public FilaProgramadaVM? Activa
    {
        get => _activa;
        set
        {
            if (!Fijar(ref _activa, value)) return;
            _ = CargarClienteAsync(value);
            AvisarDetalle();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Selección de la tabla (la mantiene la vista): el ámbito de las acciones del menú contextual.</summary>
    public void CambiarSeleccion(IReadOnlyList<FilaProgramadaVM> seleccion)
    {
        _seleccion = seleccion;
        Avisar(nameof(Pie), nameof(AvisoSeleccion), nameof(TextoPausar), nameof(TextoReanudar), nameof(TextoDuplicar), nameof(TextoEliminar), nameof(TextoGenerarSeleccionadas));
        CommandManager.InvalidateRequerySuggested();
    }

    private IReadOnlyList<FilaProgramadaVM> Ambito => _seleccion.Count > 0 ? _seleccion : _activa is null ? [] : [_activa];
    private List<FilaProgramadaVM> APausar => Ambito.Where(f => f.Datos.Activa && !f.Datos.Finalizada).ToList();
    private List<FilaProgramadaVM> AReanudar => Ambito.Where(f => !f.Datos.Activa && !f.Datos.Finalizada).ToList();
    private List<FilaProgramadaVM> AGenerar => Ambito.Where(f => f.Datos.Activa && !f.Datos.Finalizada).ToList();

    private static string ConCuenta(string texto, int n) => n > 1 ? $"{texto} ({n})" : texto;
    public string TextoPausar => ConCuenta("Pausar", APausar.Count);
    public string TextoReanudar => ConCuenta("Reanudar", AReanudar.Count);
    public string TextoDuplicar => ConCuenta("Duplicar", Ambito.Count);
    public string TextoEliminar => ConCuenta("Eliminar", Ambito.Count);
    public string TextoGenerarSeleccionadas => ConCuenta("Generar las seleccionadas…", AGenerar.Count);

    public string Pie
    {
        get
        {
            var activas = _todas.Count(p => p.Grupo == 0);
            var pendientes = _todas.Count(p => p.Grupo == 0 && p.Datos.ProximaCalculada is { } d && d <= DateTime.Today);
            var texto = Filas.Count == _todas.Count ? Formatos.Plural(Filas.Count, "programada", "programadas") : $"{Formatos.Plural(Filas.Count, "programada", "programadas")} de {Formatos.Recuento(_todas.Count)}";
            if (_seleccion.Count > 1) texto += $" · {_seleccion.Count} seleccionadas · {Formatos.Importe(_seleccion.Sum(f => f.Total))}";
            texto += $" · {Formatos.Plural(activas, "activa", "activas")}";
            return pendientes > 0 ? $"{texto} · {Formatos.Plural(pendientes, "pendiente de generar", "pendientes de generar")} hoy" : texto;
        }
    }

    /// <summary>Con varias seleccionadas, deja claro de cuál es el detalle.</summary>
    public string? AvisoSeleccion => _seleccion.Count > 1 && _activa is not null ? $"{_seleccion.Count} programadas seleccionadas. El detalle es de {_activa.Codigo}." : null;

    // ---------- detalle ----------

    private FacturaProgramada? D => _activa?.Datos;
    public bool HayDetalle => _activa is not null;
    public string DetalleTitulo => _activa?.Codigo ?? "";
    public string DetalleEstado => _activa?.Estado ?? "";
    public string DetalleCliente => _activa?.Cliente ?? "";
    public string DetalleNif => _cliente?.Nif ?? "";
    public string DetalleDomicilio => _cliente?.Domicilio ?? "";
    public string DetalleDescripcion => D?.Plantilla.Descripcion ?? "";
    public string DetallePeriodicidad => D?.Periodicidad.Texto ?? "";
    public string DetalleInicio => D?.FechaInicio is { } d ? Formatos.Fecha(d) : "";
    public string DetalleDia => D is null ? "" : D.DiaEmision is byte b ? b switch
    {
        31 => "31 (último día del mes)",
        30 => "30 (penúltimo día del mes)",
        29 => "29 (antepenúltimo día del mes)",
        _ => b.ToString(Formatos.Cultura)
    } : "El de la fecha de inicio";
    public string DetalleFinal => D is null ? "" : ReglasProgramada.TextoFinal(D);
    public string DetalleGenera => D is null ? "" : D.AlGenerar == AlGenerar.Validar ? "Una factura validada" : "Un borrador";
    public string DetalleEnvio => D is null ? "" : D.EnviarPorEmail ? "Por correo, al generarla validada (con su PDF)" : "No se envía";

    public string DetalleProximas
    {
        get
        {
            if (D is null) return "";
            var proximas = D.ProximasEmisiones(4);
            return proximas.Count == 0 ? "No le quedan facturas por emitir." : string.Join(" · ", proximas.Select(x => Formatos.Fecha(x)));
        }
    }

    public string DetallePeriodo
    {
        get
        {
            if (D?.ProximaCalculada is null) return "";
            var (d, h) = D.Periodo(D.FacturasGeneradas);
            return d is null && h is null ? "Las facturas no llevarán periodo."
                : $"La próxima factura facturará el periodo {(d is { } x ? Formatos.Fecha(x) : "…")} – {(h is { } y ? Formatos.Fecha(y) : "…")}.";
        }
    }

    public string DetalleGeneradas => _activa is null ? "" : _activa.Generadas == 0
        ? $"Sin facturas generadas en {_s.Trabajo.Ejercicio}."
        : $"{Formatos.Plural(_activa.Generadas, "factura generada", "facturas generadas")} en {_s.Trabajo.Ejercicio}.";

    public IReadOnlyList<LineaDetalle> DetalleLineas => D?.Plantilla.Lineas.Select(l => new LineaDetalle(l)).ToList() ?? [];
    public string DetalleBase => D is null ? "" : Formatos.Importe(D.Plantilla.Totales.BaseImponible.Valor);

    public IReadOnlyList<FilaResumen> DetalleIvas
    {
        get
        {
            if (D is null) return [];
            var filas = CalculoFactura.Desglose(D.Plantilla.Lineas)
                .Select(x => new FilaResumen($"IVA {VistaModelos.Texto.Porcentaje(x.TipoIva)} %", Formatos.Importe((x.CuotaIva + x.CuotaRecargo).Valor))).ToList();
            return filas.Count > 0 ? filas : [new FilaResumen("IVA", Formatos.Importe(D.Plantilla.Totales.Iva.Valor))];
        }
    }

    public string DetalleTextoRetencion => D?.Plantilla is { TipoRetencion: > 0m } f ? $"Retención {VistaModelos.Texto.Porcentaje(f.TipoRetencion)} %" : "Retención";
    public string DetalleRetencion => D is null ? "" : Formatos.Importe(-D.Plantilla.Totales.Retencion.Valor);
    public string DetalleTotal => D is null ? "" : Formatos.Importe(D.Plantilla.Totales.ImporteTotal.Valor);
    public string? DetalleNotas => string.IsNullOrWhiteSpace(D?.Plantilla.NotasInternas) ? null : D!.Plantilla.NotasInternas;
    public IReadOnlyList<string> DetalleHistorial => D?.LineasHistorial.Reverse().Take(8).ToList() ?? [];

    private void AvisarDetalle() =>
        Avisar(nameof(HayDetalle), nameof(DetalleTitulo), nameof(DetalleEstado), nameof(DetalleCliente), nameof(DetalleNif), nameof(DetalleDomicilio), nameof(DetalleDescripcion),
            nameof(DetallePeriodicidad), nameof(DetalleInicio), nameof(DetalleDia), nameof(DetalleFinal), nameof(DetalleGenera), nameof(DetalleEnvio), nameof(DetalleProximas),
            nameof(DetallePeriodo), nameof(DetalleGeneradas), nameof(DetalleLineas), nameof(DetalleBase), nameof(DetalleIvas), nameof(DetalleTextoRetencion), nameof(DetalleRetencion),
            nameof(DetalleTotal), nameof(DetalleNotas), nameof(DetalleHistorial), nameof(AvisoSeleccion));

    private async Task CargarClienteAsync(FilaProgramadaVM? fila)
    {
        _cliente = null;
        Avisar(nameof(DetalleNif), nameof(DetalleDomicilio));
        if (fila?.Datos.Plantilla.IdCliente is not int id) return;
        try
        {
            var c = await _s.Apoyo.ClienteAsync(id);
            if (!ReferenceEquals(_activa, fila)) return;
            _cliente = c;
            Avisar(nameof(DetalleNif), nameof(DetalleDomicilio));
        }
        catch (Exception ex) when (ex is ProConta.Datos.Persistencia.ExcepcionDatos or InvalidOperationException)
        {
            // El NIF y el domicilio son informativos: si fallan, el detalle sigue con el resto.
        }
    }

    // ---------- carga ----------

    public async Task CargarAsync(int? idActiva = null)
    {
        var carga = ++_cargas;
        if (_todas.Count == 0) Estado = EstadoVista.Cargando;
        try
        {
            _puedeEditar = await _s.Facturas.PuedeAsync(Acciones.FacturasEditar);
            var programadas = await _s.Programadas.ListarAsync(_s.Trabajo.IdSociedad);
            var generadas = await _s.Programadas.GeneradasEnEjercicioAsync(_s.Trabajo.IdSociedadEjercicio);
            var clientes = await _s.Programadas.NombresClientesAsync();
            if (carga != _cargas) return;
            var activa = idActiva ?? _activa?.Id;
            _todas = programadas
                .Select(p => new FilaProgramadaVM(p, p.Plantilla.IdCliente is int c && clientes.TryGetValue(c, out var n) ? n : "Sin cliente", generadas.GetValueOrDefault(p.IdFacturaProgramada)))
                .OrderBy(p => p.Grupo).ThenBy(p => p.ProximaOrden ?? DateTime.MaxValue).ThenBy(p => p.Id)
                .ToList();
            var conservada = _todas.FirstOrDefault(p => p.Id == activa);
            // Si se pide ver una programada que el filtro de estado oculta (una recién duplicada está pausada), se muestran todas.
            if (idActiva is not null && conservada is not null && !CumpleEstado(conservada))
            {
                _filtroEstado = OpcionesEstado[0];
                Avisar(nameof(FiltroEstado));
            }
            _activa = null;
            Filas.Clear();
            Activa = conservada;
            Filtrar();
        }
        catch (Exception ex) when (ex is ProConta.Datos.Persistencia.ExcepcionDatos or InvalidOperationException)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = ex is ProConta.Datos.Persistencia.ExcepcionDatos d ? d.Error.TextoOperador() : ex.Message;
            Avisar(nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado));
        }
    }

    // ---------- acciones ----------

    public ICommand NuevaCommand { get; }
    public ICommand AbrirCommand { get; }
    public ICommand PausarCommand { get; }
    public ICommand ReanudarCommand { get; }
    public ICommand DuplicarCommand { get; }
    public ICommand EliminarCommand { get; }
    public ICommand VerFacturasCommand { get; }
    public ICommand GenerarCommand { get; }
    public ICommand GenerarSeleccionadasCommand { get; }
    public ICommand VerTodasCommand { get; }
    public ICommand RefrescarCommand { get; }
    public ICommand LimpiarFiltrosCommand { get; }

    private static string Motivos(ResultadoProgramada r) => string.Join(" ", r.Incidencias.Select(i => i.Mensaje));

    /// <summary>Pausa o reanuda las seleccionadas a las que aplique; las que no se pueden, se explican.</summary>
    private async Task CambiarActivasAsync(bool activa)
    {
        var filas = activa ? AReanudar : APausar;
        var hechas = new List<string>();
        var problemas = new List<string>();
        foreach (var f in filas)
        {
            var r = await _s.Programadas.CambiarActivaAsync(f.Id, activa, f.Datos.Version, _s.Actor);
            if (r.Guardada) hechas.Add(f.Codigo);
            else problemas.Add($"{f.Codigo}: {Motivos(r)}");
        }
        await CargarAsync();
        var verbo = activa ? (hechas.Count == 1 ? "reanudada" : "reanudadas") : (hechas.Count == 1 ? "pausada" : "pausadas");
        Informar(hechas.Count == 1 ? $"{hechas[0]} {verbo}." : $"{hechas.Count} programadas {verbo}.", activa ? "No se ha reanudado" : "No se ha pausado", hechas, problemas);
    }

    private async Task DuplicarAsync()
    {
        var filas = Ambito.ToList();
        var creadas = new List<FacturaProgramada>();
        var problemas = new List<string>();
        foreach (var f in filas)
        {
            var r = await _s.Programadas.DuplicarAsync(f.Id, _s.Actor);
            if (r.Guardada) creadas.Add(r.Programada!);
            else problemas.Add($"{f.Codigo}: {Motivos(r)}");
        }
        if (creadas.Count > 0) await CargarAsync(creadas[^1].IdFacturaProgramada);
        if (creadas.Count == 1 && problemas.Count == 0) { _modulo.AbrirProgramada(creadas[0].IdFacturaProgramada); return; }
        Informar($"{creadas.Count} programadas duplicadas, pausadas y sin facturas generadas.", "No se ha duplicado", creadas.Select(p => p.Rotulo).ToList(), problemas);
    }

    private async Task EliminarAsync()
    {
        var filas = Ambito.ToList();
        if (filas.Count == 0) return;
        var titulo = filas.Count == 1 ? $"Eliminar {filas[0].Codigo}" : $"Eliminar {filas.Count} programadas";
        if (_modulo.Dialogos.Preguntar(titulo, "Se borran las programadas y sus líneas. Solo se puede si aún no han generado ninguna factura; las que ya tengan se quedan y te lo indico para que las pauses.",
                filas.Count == 1 ? null : filas.Select(f => $"{f.Codigo} · {f.Cliente}").ToList(),
                new OpcionMensaje("Cancelar", "No borra nada", Cancelar: true), new OpcionMensaje("Eliminar", "Borra las programadas sin facturas", Peligro: true)) != 1) return;
        var hechas = new List<string>();
        var problemas = new List<string>();
        foreach (var f in filas)
        {
            var r = await _s.Programadas.EliminarAsync(f.Id, f.Datos.Version);
            if (r.EsCorrecto) hechas.Add(f.Codigo);
            else problemas.Add($"{f.Codigo}: {string.Join(" ", r.Errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")))}");
        }
        await CargarAsync();
        Informar(hechas.Count == 1 ? $"{hechas[0]} eliminada." : $"{hechas.Count} programadas eliminadas.", "No se ha eliminado", hechas, problemas);
    }

    /// <summary>Sin problemas, un aviso; con alguno, un resumen con lo hecho y lo que necesita revisión.</summary>
    private void Informar(string aviso, string tituloFallo, IReadOnlyList<string> hechas, IReadOnlyList<string> problemas)
    {
        if (problemas.Count == 0) { _modulo.Avisar(aviso); return; }
        _modulo.Dialogos.Preguntar(hechas.Count == 0 ? tituloFallo : aviso,
            hechas.Count == 0 ? "Revisa lo siguiente:" : $"Hechas: {string.Join(", ", hechas)}. Las demás necesitan revisión:", problemas,
            new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
    }

    /// <summary>
    /// «Generar…»: elige la fecha y las programadas, genera el lote y resume lo creado y las incidencias. Con
    /// <paramref name="soloMarcadas"/> abre marcadas únicamente esas; sin él, todas las que tienen vencimientos.
    /// </summary>
    private async Task GenerarAsync(IReadOnlySet<int>? soloMarcadas)
    {
        try
        {
            var eleccion = _modulo.Dialogos.ElegirProgramadasAGenerar(DateTime.Today, h => _s.Programadas.PendientesAsync(_s.Trabajo.IdSociedad, h), soloMarcadas);
            if (eleccion is not { } e) return;
            var r = await _s.Programadas.GenerarAsync(_s.Trabajo, e.Hasta, e.Ids, _s.Actor);
            if (!r.EsCorrecto) { Error("No se ha generado el lote", r.Errores.Select(x => x.Mensaje + (x.AccionCorrectora is { } a ? " " + a : "")).ToList()); return; }
            var lote = r.Valor;
            await CargarAsync();
            _modulo.FacturaCambiada();
            // Las validadas de programadas con «Enviar por email» salen en un lote de correo, con su resumen al final.
            var aEnviar = lote.Facturas.Where(f => f.Validada && f.EnviarPorEmail).Select(f => (f.IdFactura, f.Rotulo)).ToList();
            if (aEnviar.Count > 0 && _modulo.Correo is not null) await _modulo.EnviarPorCorreoAsync(aEnviar, $"Facturas programadas · lote {lote.IdLote}");
            var creadas = lote.Facturas.Select(f => $"{f.Programada} · {Formatos.Fecha(f.Vencimiento)} → {(f.Validada ? f.Rotulo : f.Rotulo + " (borrador)")}").ToList();
            var titulo = lote.Facturas.Count == 0 ? "No se ha creado ninguna factura" : $"Lote {lote.IdLote}: {Formatos.Plural(lote.Facturas.Count, "factura creada", "facturas creadas")}";
            if (lote.SinIncidencias)
            {
                _modulo.Avisar($"{titulo}. Las ves en Facturas; los borradores se validan con «Validar…».");
                return;
            }
            _modulo.Dialogos.Preguntar(titulo,
                lote.Facturas.Count == 0 ? "Ninguna programada ha generado su factura. Revisa lo siguiente:" : "Las demás programadas necesitan revisión; lo creado ya está guardado:",
                [.. creadas, .. lote.Incidencias.Select(i => "⚠ " + i)],
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        catch (Exception ex)
        {
            // Un fallo inesperado no se pierde en silencio: se explica y se puede repetir.
            _modulo.Dialogos.Preguntar("No se ha podido generar", $"Ha fallado la generación de programadas: {ex.Message} Lo que ya se hubiera creado está guardado; actualiza el listado y vuelve a intentarlo.", [ex.ToString()],
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
    }

    private void Error(string titulo, IReadOnlyList<string> detalles) =>
        _modulo.Dialogos.Preguntar(titulo, detalles.Count == 1 ? detalles[0] : "Revisa lo siguiente:", detalles.Count == 1 ? null : detalles,
            new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
}
