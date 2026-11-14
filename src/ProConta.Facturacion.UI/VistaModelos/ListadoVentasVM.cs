using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Facturacion.Ventas;
using ProConta.Seguridad.Permisos;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Facturacion.UI.VistaModelos;

/// <summary>Opción de una lista de la ficha (origen, piso, concepto, tratamiento del IVA); Id null es «sin elegir».</summary>
public sealed record OpcionVenta(object? Id, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>Fila del listado de ventas por facturar.</summary>
public sealed class FilaVentaVM(VentaPorFacturar datos, string origen, string cliente, string piso) : ModeloVista
{
    public VentaPorFacturar Datos { get; } = datos;
    public int Id => Datos.IdVentaPorFacturar;
    public DateTime Fecha => Datos.FechaReferencia;
    public string TextoFecha => Formatos.Fecha(Datos.FechaReferencia);
    public string Salida => Datos.FechaFin is DateTime f ? Formatos.Fecha(f) : "";
    public string Origen { get; } = origen;
    public string Cliente { get; } = cliente;
    public string Piso { get; } = piso;
    public string Referencia => Datos.Referencia ?? "";
    public decimal Cantidad => Datos.Cantidad;
    public string TextoCantidad => Texto.Cantidad(Datos.Cantidad);
    public decimal Importe => Datos.Importe;
    public string TextoImporte => Formatos.Importe(Datos.Importe);
    public string TextoComision => Datos.Comision is decimal c ? Formatos.Importe(c) : "";
    public string Estado => VentaPorFacturar.TextoEstado(Datos.Estado);
    public string Factura => Datos.FacturadaFuera ? "ERP" : Datos.RotuloFactura ?? "";
    public string Fuente => Datos.Fuente switch { FuenteVenta.Avaibook => "Avaibook", FuenteVenta.Erp => "ERP", _ => "Manual" };
    public bool TieneIncidencia => !string.IsNullOrWhiteSpace(Datos.Incidencia);

    public bool Contiene(string texto) =>
        string.IsNullOrWhiteSpace(texto) || new[] { Origen, Cliente, Piso, Datos.Referencia, Datos.Descripcion, Datos.Titular, Datos.Notas, Factura, Id.ToString(Formatos.Cultura) }
            .Any(c => c?.Contains(texto.Trim(), StringComparison.CurrentCultureIgnoreCase) == true);
}

/// <summary>
/// Pantalla «Ventas por facturar» (facturacion/pantallas, derivado de facturacion/reservas 2026-11-14 11:45): listado
/// filtrable de las ventas de la sociedad y, a la derecha, la ficha de la activa, editable mientras está pendiente, con
/// Guardar/Cancelar al pie. Selección múltiple y acciones en el menú contextual; solo «Nueva venta» en la cabecera.
/// </summary>
public sealed class ListadoVentasVM : ModeloVista, IPestanaTrabajo
{
    private readonly ModuloFacturacion _modulo;
    private readonly ServiciosFacturacion _s;
    private readonly ServicioVentas _ventas;
    private List<FilaVentaVM> _todas = [];
    private object? _vista;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private FilaVentaVM? _activa;
    private IReadOnlyList<FilaVentaVM> _seleccion = [];
    private string _texto = "";
    private OpcionFiltro _filtroEstado;
    private OpcionVenta _filtroOrigen = new(null, "Todos los orígenes");
    private OpcionVenta _filtroPiso = new(null, "Todos los pisos");
    private bool _puedeEditar;
    private int _cargas;
    private IReadOnlyDictionary<int, string> _clientes = new Dictionary<int, string>();
    private IReadOnlyList<OrigenVenta> _origenes = [];

    internal ListadoVentasVM(ModuloFacturacion modulo, ServicioVentas ventas)
    {
        _modulo = modulo;
        _s = modulo.Servicios;
        _ventas = ventas;
        OpcionesEstado = [new("Todos los estados", null), new("Pendientes", nameof(EstadoVenta.Pendiente)), new("En borrador", nameof(EstadoVenta.EnBorrador)),
            new("Facturadas", nameof(EstadoVenta.Facturada)), new("Anuladas", nameof(EstadoVenta.Anulada)), new("Con incidencia", "Incidencia")];
        _filtroEstado = OpcionesEstado[1];

        NuevaCommand = new ComandoDelegado(Nueva, () => _puedeEditar);
        GuardarCommand = new ComandoDelegado(() => _ = GuardarFichaAsync(), () => _puedeEditar && FichaEditable && FichaCambiada);
        CancelarCommand = new ComandoDelegado(CancelarFicha, () => FichaCambiada);
        ElegirClienteCommand = new ComandoDelegado(ElegirCliente, () => FichaEditable);
        QuitarClienteCommand = new ComandoDelegado(() => { _fIdCliente = null; FichaCliente = "Del origen"; Cambio(); }, () => FichaEditable && _fIdCliente is not null);
        AnularCommand = new ComandoDelegado(() => _ = AnularAsync(true), () => _puedeEditar && AAnular.Count > 0);
        ReactivarCommand = new ComandoDelegado(() => _ = AnularAsync(false), () => _puedeEditar && AReactivar.Count > 0);
        EliminarCommand = new ComandoDelegado(() => _ = EliminarAsync(), () => _puedeEditar && AEliminar.Count > 0);
        FacturarSeleccionadasCommand = new ComandoDelegado(() => _ = FacturarAsync(AFacturar.Select(f => f.Id).ToHashSet()), () => _puedeEditar && AFacturar.Count > 0);
        FacturarCommand = new ComandoDelegado(() => _ = FacturarAsync(null), () => _puedeEditar);
        ImportarCommand = new ComandoDelegado(() => _ = ImportarAsync(), () => _puedeEditar && _modulo.Avaibook is not null);
        VerFacturaCommand = new ComandoDelegado(() => { if (Ambito is [{ Datos.IdFactura: int id }]) _modulo.AbrirFactura(id); }, () => Ambito is [{ Datos.IdFactura: not null }]);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        LimpiarFiltrosCommand = new ComandoDelegado(LimpiarFiltros, () => HayFiltros);
        VerTodasCommand = new ComandoDelegado(() => FiltroEstado = OpcionesEstado[0], () => _filtroEstado != OpcionesEstado[0]);
    }

    // ---------- pestaña ----------

    public string Titulo => "Ventas por facturar";
    public string Ayuda => $"Ventas por facturar de {_s.RazonSocial}";
    public string Icono => "doc";
    public bool TieneCambios => FichaCambiada;
    public bool GuardadoFallido { get; private set; }
    public object Vista => _vista ??= new Vistas.VistaListadoVentas { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => FichaCambiada ? [$"{FichaTitulo}: cambios sin guardar"] : [];
    public Task<bool> GuardarAsync() => GuardarFichaAsync();
    public void Descartar() => CancelarFicha();
    public void AlCerrar() => _modulo.VentasCerrado();

    // ---------- estado ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string TituloEstado => Estado switch
    {
        EstadoVista.Vacio => _todas.Count == 0 ? "Aún no hay ventas por facturar en esta sociedad" : HayFiltros ? "Ninguna venta cumple los filtros" : "No hay ventas pendientes de facturar",
        EstadoVista.Error => "No he podido leer las ventas por facturar",
        EstadoVista.Cargando => "Leyendo ventas por facturar…",
        _ => ""
    };
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string? AccionEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 => _modulo.Avaibook is not null ? "Importar de Avaibook…" : "Nueva venta",
        EstadoVista.Vacio when !HayFiltros => "Ver todas",
        EstadoVista.Vacio => "Quitar filtros",
        EstadoVista.Error => "Reintentar",
        _ => null
    };
    public ICommand? ComandoEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 => _modulo.Avaibook is not null ? ImportarCommand : NuevaCommand,
        EstadoVista.Vacio when !HayFiltros => VerTodasCommand,
        EstadoVista.Vacio => LimpiarFiltrosCommand,
        EstadoVista.Error => RefrescarCommand,
        _ => null
    };

    // ---------- filtros ----------

    public IReadOnlyList<OpcionFiltro> OpcionesEstado { get; }
    public ObservableCollection<OpcionVenta> OpcionesOrigen { get; } = [];
    public ObservableCollection<OpcionVenta> OpcionesPiso { get; } = [];
    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public OpcionFiltro FiltroEstado { get => _filtroEstado; set { if (value is not null && Fijar(ref _filtroEstado, value)) Filtrar(); } }
    public OpcionVenta FiltroOrigen { get => _filtroOrigen; set { if (value is not null && Fijar(ref _filtroOrigen, value)) Filtrar(); } }
    public OpcionVenta FiltroPiso { get => _filtroPiso; set { if (value is not null && Fijar(ref _filtroPiso, value)) Filtrar(); } }

    public bool HayFiltros => _texto.Length > 0 || _filtroEstado != OpcionesEstado[1] || _filtroOrigen.Id is not null || _filtroPiso.Id is not null;

    private void LimpiarFiltros()
    {
        _texto = "";
        _filtroEstado = OpcionesEstado[1];
        _filtroOrigen = OpcionesOrigen.FirstOrDefault() ?? _filtroOrigen;
        _filtroPiso = OpcionesPiso.FirstOrDefault() ?? _filtroPiso;
        Avisar(nameof(Texto), nameof(FiltroEstado), nameof(FiltroOrigen), nameof(FiltroPiso));
        Filtrar();
    }

    private bool Cumple(FilaVentaVM f) =>
        f.Contiene(_texto)
        && (_filtroEstado.Valor switch { null => true, "Incidencia" => f.TieneIncidencia, var e => f.Datos.Estado.ToString() == e })
        && (_filtroOrigen.Id is not int o || f.Datos.IdOrigenVenta == o)
        && (_filtroPiso.Id is not int p || f.Datos.IdActivo == p);

    private void Filtrar()
    {
        var id = _activa?.Id;
        Filas.Clear();
        foreach (var f in _todas.Where(Cumple)) Filas.Add(f);
        if (!FichaCambiada) Activa = Filas.FirstOrDefault(f => f.Id == id) ?? Filas.FirstOrDefault();
        Estado = Filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = _todas.Count == 0
            ? "Importa las reservas de Avaibook o da de alta una venta con «Nueva venta»."
            : HayFiltros ? "Cambia o quita los filtros para ver más ventas." : "Las demás ya están facturadas, en borrador o anuladas.";
        Avisar(nameof(HayFiltros), nameof(Pie), nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- filas, activa y selección ----------

    public ObservableCollection<FilaVentaVM> Filas { get; } = [];

    public FilaVentaVM? Activa
    {
        get => _activa;
        set
        {
            if (ReferenceEquals(_activa, value)) return;
            if (FichaCambiada && !ConfirmarDescartar()) { Avisar(); return; }
            _activa = value;
            Avisar();
            CargarFicha(value?.Datos);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public void CambiarSeleccion(IReadOnlyList<FilaVentaVM> seleccion)
    {
        _seleccion = seleccion;
        Avisar(nameof(Pie), nameof(TextoAnular), nameof(TextoReactivar), nameof(TextoEliminar), nameof(TextoFacturarSeleccionadas));
        CommandManager.InvalidateRequerySuggested();
    }

    private IReadOnlyList<FilaVentaVM> Ambito => _seleccion.Count > 0 ? _seleccion : _activa is null ? [] : [_activa];
    private List<FilaVentaVM> AAnular => Ambito.Where(f => f.Datos.Estado == EstadoVenta.Pendiente).ToList();
    private List<FilaVentaVM> AReactivar => Ambito.Where(f => f.Datos.Estado == EstadoVenta.Anulada).ToList();
    private List<FilaVentaVM> AEliminar => Ambito.Where(f => f.Datos.Fuente == FuenteVenta.Manual && f.Datos.Estado is EstadoVenta.Pendiente or EstadoVenta.Anulada).ToList();
    private List<FilaVentaVM> AFacturar => Ambito.Where(f => f.Datos.Estado == EstadoVenta.Pendiente).ToList();

    private static string ConCuenta(string texto, int n) => n > 1 ? $"{texto} ({n})" : texto;
    public string TextoAnular => ConCuenta("Anular", AAnular.Count);
    public string TextoReactivar => ConCuenta("Reactivar", AReactivar.Count);
    public string TextoEliminar => ConCuenta("Eliminar", AEliminar.Count);
    public string TextoFacturarSeleccionadas => ConCuenta("Facturar las seleccionadas…", AFacturar.Count);

    public string Pie
    {
        get
        {
            var texto = Filas.Count == _todas.Count ? Formatos.Plural(Filas.Count, "venta", "ventas") : $"{Formatos.Plural(Filas.Count, "venta", "ventas")} de {Formatos.Recuento(_todas.Count)}";
            texto += $" · {Formatos.Importe(Filas.Sum(f => f.Importe))}";
            if (_seleccion.Count > 1) texto += $" · {_seleccion.Count} seleccionadas · {Formatos.Importe(_seleccion.Sum(f => f.Importe))}";
            var pendientes = _todas.Where(f => f.Datos.Estado == EstadoVenta.Pendiente).ToList();
            return pendientes.Count > 0 ? $"{texto} · {Formatos.Plural(pendientes.Count, "pendiente", "pendientes")} de facturar ({Formatos.Importe(pendientes.Sum(f => f.Importe))})" : texto;
        }
    }

    // ---------- ficha ----------

    private VentaPorFacturar? _ficha;
    private bool _fichaCambiada;
    private OpcionVenta? _fOrigen, _fPiso, _fConcepto, _fCausa;
    private int? _fIdCliente;
    private string _fCliente = "", _fFecha = "", _fSalida = "", _fCantidad = "", _fImporte = "", _fComision = "", _fReferencia = "", _fTitular = "", _fDescripcion = "", _fNotas = "";
    private string? _fError;
    private bool _cargandoFicha;

    public ObservableCollection<OpcionVenta> FichaOrigenes { get; } = [];
    public ObservableCollection<OpcionVenta> FichaPisos { get; } = [];
    public ObservableCollection<OpcionVenta> FichaConceptos { get; } = [];
    public IReadOnlyList<OpcionVenta> FichaCausas { get; } =
        [new(null, "Sujeta y no exenta (IVA del concepto)"), new("E1", "E1 · Exenta art. 20 (alquiler sin servicios de hostelería)"), new("E2", "E2 · Exenta art. 21"),
         new("E3", "E3 · Exenta art. 22"), new("E4", "E4 · Exenta arts. 23 y 24"), new("E5", "E5 · Exenta art. 25"), new("E6", "E6 · Exenta por otros")];

    public bool HayFicha => _ficha is not null;
    public bool FichaEditable => _ficha is not null && (_ficha.EsNueva || _ficha.EsEditable) && _puedeEditar;
    public bool FichaSoloLectura => !FichaEditable;
    public bool FichaCambiada { get => _fichaCambiada; private set { if (Fijar(ref _fichaCambiada, value)) { Avisar(nameof(TieneCambios)); CommandManager.InvalidateRequerySuggested(); } } }
    public string FichaTitulo => _ficha is null ? "" : _ficha.EsNueva ? "Venta nueva" : _ficha.Rotulo;
    public string FichaEstado => _ficha is null ? "" : _ficha.EsNueva ? "Nueva" : VentaPorFacturar.TextoEstado(_ficha.Estado);
    public string? FichaAviso => _ficha is null ? null
        : !string.IsNullOrWhiteSpace(_ficha.Incidencia) ? _ficha.Incidencia
        : _ficha.Estado == EstadoVenta.EnBorrador ? $"Está en el borrador {_ficha.RotuloFactura}. Para cambiarla, borra el borrador (la venta vuelve a pendiente) o corrige la línea en el borrador."
        : _ficha.Estado == EstadoVenta.Facturada ? (_ficha.FacturadaFuera ? "Facturada en el ERP: no se vuelve a facturar." : $"Facturada en {_ficha.RotuloFactura}. Para corregirla, rectifica la factura.")
        : _ficha.Estado == EstadoVenta.Anulada ? $"Anulada: {_ficha.MotivoAnulacion}. «Reactivar» la devuelve a pendiente."
        : _ficha.Fuente == FuenteVenta.Avaibook && !_ficha.EsNueva ? "Viene de Avaibook: si cambias algo aquí, la próxima importación lo vuelve a poner como en Avaibook mientras siga pendiente."
        : null;
    public string? FichaError { get => _fError; private set => Fijar(ref _fError, value); }
    public string FichaFuente => _ficha is null ? "" : _ficha.Fuente switch
    {
        FuenteVenta.Avaibook => $"Avaibook ({_ficha.IdExterno})",
        FuenteVenta.Erp => $"ERP (reserva {_ficha.IdExterno})",
        _ => _ficha.EsNueva ? "Alta manual" : $"Alta manual de {_ficha.OperadorAlta} el {Formatos.Fecha(_ficha.FechaAlta)}"
    };

    public OpcionVenta? FichaOrigen { get => _fOrigen; set { if (Fijar(ref _fOrigen, value)) { Avisar(nameof(FichaClienteOrigen)); Cambio(); } } }
    public string FichaCliente { get => _fCliente; private set => Fijar(ref _fCliente, value); }
    public string FichaClienteOrigen => _fOrigen?.Id is int o && _origenes.FirstOrDefault(x => x.IdOrigenVenta == o) is { } origen
        ? origen.IdCliente is int c ? $"Sin cliente propio se factura a {_clientes.GetValueOrDefault(c, $"cliente {c}")}, el del origen." : "Su origen no tiene cliente: elige uno."
        : "";
    public OpcionVenta? FichaPiso { get => _fPiso; set { if (Fijar(ref _fPiso, value)) Cambio(); } }
    public OpcionVenta? FichaConcepto { get => _fConcepto; set { if (Fijar(ref _fConcepto, value)) Cambio(); } }
    public OpcionVenta? FichaCausa { get => _fCausa; set { if (Fijar(ref _fCausa, value)) Cambio(); } }
    public string FichaFecha { get => _fFecha; set { if (Fijar(ref _fFecha, value ?? "")) Cambio(); } }
    public string FichaSalida { get => _fSalida; set { if (Fijar(ref _fSalida, value ?? "")) Cambio(); } }
    public string FichaCantidad { get => _fCantidad; set { if (Fijar(ref _fCantidad, value ?? "")) Cambio(); } }
    public string FichaImporte { get => _fImporte; set { if (Fijar(ref _fImporte, value ?? "")) Cambio(); } }
    public string FichaComision { get => _fComision; set { if (Fijar(ref _fComision, value ?? "")) Cambio(); } }
    public string FichaReferencia { get => _fReferencia; set { if (Fijar(ref _fReferencia, value ?? "")) Cambio(); } }
    public string FichaTitular { get => _fTitular; set { if (Fijar(ref _fTitular, value ?? "")) Cambio(); } }
    public string FichaDescripcion { get => _fDescripcion; set { if (Fijar(ref _fDescripcion, value ?? "")) Cambio(); } }
    public string FichaNotas { get => _fNotas; set { if (Fijar(ref _fNotas, value ?? "")) Cambio(); } }

    private void Cambio()
    {
        if (_cargandoFicha) return;
        FichaCambiada = true;
        FichaError = null;
    }

    private static OpcionVenta? Buscar(IEnumerable<OpcionVenta> opciones, object? id) => opciones.FirstOrDefault(o => Equals(o.Id, id)) ?? opciones.FirstOrDefault();

    private void CargarFicha(VentaPorFacturar? v)
    {
        _cargandoFicha = true;
        _ficha = v?.Clonar();
        if (v is not null)
        {
            _fOrigen = Buscar(FichaOrigenes, v.IdOrigenVenta == 0 ? null : v.IdOrigenVenta);
            _fPiso = Buscar(FichaPisos, v.IdActivo);
            _fConcepto = Buscar(FichaConceptos, v.IdConcepto);
            _fCausa = Buscar(FichaCausas, string.IsNullOrWhiteSpace(v.CausaExencion) ? null : v.CausaExencion.Trim());
            _fIdCliente = v.IdCliente;
            _fCliente = v.IdCliente is int c ? _clientes.GetValueOrDefault(c, $"Cliente {c}") : "Del origen";
            _fFecha = Formatos.Fecha(v.FechaReferencia);
            _fSalida = v.FechaFin is DateTime f ? Formatos.Fecha(f) : "";
            _fCantidad = VistaModelos.Texto.Cantidad(v.Cantidad);
            _fImporte = v.Importe == 0m && v.EsNueva ? "" : VistaModelos.Texto.Precio(v.Importe);
            _fComision = v.Comision is decimal com ? VistaModelos.Texto.Precio(com) : "";
            _fReferencia = v.Referencia ?? "";
            _fTitular = v.Titular ?? "";
            _fDescripcion = v.Descripcion ?? "";
            _fNotas = v.Notas ?? "";
        }
        _fError = null;
        _cargandoFicha = false;
        FichaCambiada = v is not null && v.EsNueva;
        Avisar(nameof(HayFicha), nameof(FichaEditable), nameof(FichaSoloLectura), nameof(FichaTitulo), nameof(FichaEstado), nameof(FichaAviso), nameof(FichaError), nameof(FichaFuente),
            nameof(FichaOrigen), nameof(FichaCliente), nameof(FichaClienteOrigen), nameof(FichaPiso), nameof(FichaConcepto), nameof(FichaCausa), nameof(FichaFecha), nameof(FichaSalida),
            nameof(FichaCantidad), nameof(FichaImporte), nameof(FichaComision), nameof(FichaReferencia), nameof(FichaTitular), nameof(FichaDescripcion), nameof(FichaNotas));
    }

    private bool ConfirmarDescartar() =>
        _modulo.Dialogos.Preguntar("Cambios sin guardar", $"{FichaTitulo} tiene cambios sin guardar.", null,
            new OpcionMensaje("Seguir editando", "Vuelve a la ficha", Cancelar: true), new OpcionMensaje("Descartar", "Pierde los cambios", Peligro: true)) == 1;

    private void Nueva()
    {
        if (FichaCambiada && !ConfirmarDescartar()) return;
        var origen = _filtroOrigen.Id is int o ? _origenes.FirstOrDefault(x => x.IdOrigenVenta == o) : _origenes.FirstOrDefault(x => x.Activo && x.EsDirecta) ?? _origenes.FirstOrDefault(x => x.Activo);
        _activa = null;
        Avisar(nameof(Activa));
        FichaCambiada = false;
        CargarFicha(_ventas.Nueva(_s.Trabajo.IdSociedad, origen));
        CommandManager.InvalidateRequerySuggested();
    }

    private void CancelarFicha()
    {
        if (_ficha is { EsNueva: true }) { FichaCambiada = false; CargarFicha(null); Activa = Filas.FirstOrDefault(); return; }
        FichaCambiada = false;
        CargarFicha(_activa?.Datos);
    }

    private void ElegirCliente()
    {
        if (_modulo.Dialogos.ElegirCliente() is not { } c) return;
        _fIdCliente = c.IdCliente;
        FichaCliente = c.Nombre;
        Cambio();
    }

    /// <summary>Lee la ficha a la venta; si algún número o fecha no se entiende, lo dice y devuelve null.</summary>
    private VentaPorFacturar? LeerFicha()
    {
        if (_ficha is null) return null;
        var v = _ficha.Clonar();
        if (!VistaModelos.Texto.LeerFecha(_fFecha, out var fecha) || fecha is null) { FichaError = "Escribe la fecha de entrada como dd/mm/aaaa."; return null; }
        if (!VistaModelos.Texto.LeerFecha(_fSalida, out var salida)) { FichaError = "Escribe la fecha de salida como dd/mm/aaaa (o déjala vacía)."; return null; }
        if (!VistaModelos.Texto.LeerNumero(_fCantidad, out var cantidad)) { FichaError = "Escribe la cantidad (noches) como un número."; return null; }
        if (!VistaModelos.Texto.LeerNumero(_fImporte, out var importe)) { FichaError = "Escribe el importe que se factura (lo que paga el huésped)."; return null; }
        decimal? comision = null;
        if (!string.IsNullOrWhiteSpace(_fComision))
        {
            if (!VistaModelos.Texto.LeerNumero(_fComision, out var c)) { FichaError = "Escribe la comisión como un importe (o déjala vacía)."; return null; }
            comision = c;
        }
        v.IdOrigenVenta = _fOrigen?.Id as int? ?? 0;
        v.IdCliente = _fIdCliente;
        v.IdActivo = _fPiso?.Id as int?;
        v.IdConcepto = _fConcepto?.Id as int?;
        v.CausaExencion = _fCausa?.Id as string;
        v.FechaReferencia = fecha.Value;
        v.FechaFin = salida;
        v.Cantidad = cantidad;
        v.Importe = Math.Round(importe, 2, MidpointRounding.AwayFromZero);
        v.Comision = comision is decimal x ? Math.Round(x, 2, MidpointRounding.AwayFromZero) : null;
        v.Referencia = _fReferencia.Trim();
        v.Titular = _fTitular.Trim();
        v.Descripcion = _fDescripcion.Trim();
        v.Notas = _fNotas;
        return v;
    }

    private async Task<bool> GuardarFichaAsync()
    {
        if (!FichaCambiada) return true;
        if (!FichaEditable) { GuardadoFallido = true; return false; }
        var v = LeerFicha();
        if (v is null) { GuardadoFallido = true; return false; }
        var r = await _ventas.GuardarAsync(v, _s.Actor);
        if (!r.Guardada)
        {
            FichaError = string.Join(" ", r.Incidencias.Select(i => i.Mensaje));
            GuardadoFallido = true;
            return false;
        }
        GuardadoFallido = false;
        FichaCambiada = false;
        _modulo.Avisar($"{r.Venta!.Rotulo} guardada.");
        await CargarAsync(r.Venta.IdVentaPorFacturar);
        return true;
    }

    // ---------- carga ----------

    public async Task CargarAsync(int? idActiva = null)
    {
        var carga = ++_cargas;
        if (_todas.Count == 0) Estado = EstadoVista.Cargando;
        try
        {
            _puedeEditar = await _s.Facturas.PuedeAsync(Acciones.FacturasEditar);
            var sociedad = _s.Trabajo.IdSociedad;
            _origenes = await _ventas.OrigenesAsync(sociedad);
            var activos = await _ventas.ActivosAsync(sociedad);
            var conceptos = await _s.Conceptos.ListarAsync(_s.Trabajo);
            _clientes = await _ventas.NombresClientesAsync();
            var ventas = await _ventas.ListarAsync(sociedad);
            if (carga != _cargas) return;

            var nombresOrigen = _origenes.ToDictionary(o => o.IdOrigenVenta, o => o.Nombre);
            var nombresActivo = activos.ToDictionary(a => a.IdActivo, a => a.Nombre);
            _todas = ventas.Select(v => new FilaVentaVM(v, nombresOrigen.GetValueOrDefault(v.IdOrigenVenta, $"Origen {v.IdOrigenVenta}"),
                    AgrupadorVentas.ClienteEfectivo(v, _origenes.ToDictionary(o => o.IdOrigenVenta)) is int c ? _clientes.GetValueOrDefault(c, $"Cliente {c}") : "Sin cliente",
                    v.IdActivo is int a ? nombresActivo.GetValueOrDefault(a, $"Activo {a}") : ""))
                .ToList();

            Rellenar(OpcionesOrigen, [new OpcionVenta(null, "Todos los orígenes"), .. _origenes.Select(o => new OpcionVenta(o.IdOrigenVenta, o.Nombre))]);
            Rellenar(OpcionesPiso, [new OpcionVenta(null, "Todos los pisos"), .. activos.Where(a => ventas.Any(v => v.IdActivo == a.IdActivo)).Select(a => new OpcionVenta(a.IdActivo, a.Nombre))]);
            _filtroOrigen = OpcionesOrigen.FirstOrDefault(o => Equals(o.Id, _filtroOrigen.Id)) ?? OpcionesOrigen[0];
            _filtroPiso = OpcionesPiso.FirstOrDefault(o => Equals(o.Id, _filtroPiso.Id)) ?? OpcionesPiso[0];
            Avisar(nameof(FiltroOrigen), nameof(FiltroPiso));
            Rellenar(FichaOrigenes, [new OpcionVenta(null, "Elige el origen"), .. _origenes.Where(o => o.Activo).Select(o => new OpcionVenta(o.IdOrigenVenta, o.Nombre))]);
            Rellenar(FichaPisos, [new OpcionVenta(null, "Sin piso"), .. activos.Select(a => new OpcionVenta(a.IdActivo, a.Nombre))]);
            Rellenar(FichaConceptos, [new OpcionVenta(null, "Elige el concepto"), .. conceptos.Where(x => x.Activo).Select(x => new OpcionVenta(x.IdConcepto, x.CuentaVenta is null ? x.Nombre : $"{x.Nombre} ({x.CuentaVenta})"))]);

            var activa = idActiva ?? _activa?.Id;
            var conservada = _todas.FirstOrDefault(f => f.Id == activa);
            if (idActiva is not null && conservada is not null && !Cumple(conservada))
            {
                _filtroEstado = OpcionesEstado[0];
                Avisar(nameof(FiltroEstado));
            }
            FichaCambiada = false;
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

    private static void Rellenar(ObservableCollection<OpcionVenta> destino, IEnumerable<OpcionVenta> opciones)
    {
        destino.Clear();
        foreach (var o in opciones) destino.Add(o);
    }

    // ---------- acciones ----------

    public ICommand NuevaCommand { get; }
    public ICommand GuardarCommand { get; }
    public ICommand CancelarCommand { get; }
    public ICommand ElegirClienteCommand { get; }
    public ICommand QuitarClienteCommand { get; }
    public ICommand AnularCommand { get; }
    public ICommand ReactivarCommand { get; }
    public ICommand EliminarCommand { get; }
    public ICommand FacturarCommand { get; }
    public ICommand FacturarSeleccionadasCommand { get; }
    public ICommand ImportarCommand { get; }
    public ICommand VerFacturaCommand { get; }
    public ICommand VerTodasCommand { get; }
    public ICommand RefrescarCommand { get; }
    public ICommand LimpiarFiltrosCommand { get; }

    private static string Errores(ProConta.Nucleo.Resultados.Resultado r) =>
        string.Join(" ", r.Errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")));

    private async Task AnularAsync(bool anular)
    {
        var filas = anular ? AAnular : AReactivar;
        if (filas.Count == 0) return;
        if (anular && _modulo.Dialogos.Preguntar(filas.Count == 1 ? $"Anular {filas[0].Datos.Rotulo}" : $"Anular {filas.Count} ventas",
                "Las ventas anuladas no se facturan. Se pueden reactivar después.", filas.Count == 1 ? null : filas.Select(f => $"{f.Datos.Rotulo} · {f.TextoFecha} · {f.TextoImporte}").ToList(),
                new OpcionMensaje("Cancelar", "No anula nada", Cancelar: true), new OpcionMensaje("Anular", "Las deja fuera de la facturación", Peligro: true)) != 1) return;
        var hechas = new List<string>();
        var problemas = new List<string>();
        foreach (var f in filas)
        {
            var r = anular ? await _ventas.AnularAsync(f.Id, f.Datos.Version, null, _s.Actor) : await _ventas.ReactivarAsync(f.Id, f.Datos.Version, _s.Actor);
            if (r.EsCorrecto) hechas.Add(f.Datos.Rotulo);
            else problemas.Add($"{f.Datos.Rotulo}: {Errores(r)}");
        }
        await CargarAsync();
        var verbo = anular ? (hechas.Count == 1 ? "anulada" : "anuladas") : (hechas.Count == 1 ? "reactivada" : "reactivadas");
        Informar(hechas.Count == 1 ? $"{hechas[0]} {verbo}." : $"{hechas.Count} ventas {verbo}.", anular ? "No se ha anulado" : "No se ha reactivado", hechas, problemas);
    }

    private async Task EliminarAsync()
    {
        var filas = AEliminar;
        if (filas.Count == 0) return;
        if (_modulo.Dialogos.Preguntar(filas.Count == 1 ? $"Eliminar {filas[0].Datos.Rotulo}" : $"Eliminar {filas.Count} ventas",
                "Se borran las ventas manuales seleccionadas que no están facturadas. Las importadas no se borran (se anulan).", null,
                new OpcionMensaje("Cancelar", "No borra nada", Cancelar: true), new OpcionMensaje("Eliminar", "Borra las ventas", Peligro: true)) != 1) return;
        var hechas = new List<string>();
        var problemas = new List<string>();
        foreach (var f in filas)
        {
            var r = await _ventas.EliminarAsync(f.Id, f.Datos.Version);
            if (r.EsCorrecto) hechas.Add(f.Datos.Rotulo);
            else problemas.Add($"{f.Datos.Rotulo}: {Errores(r)}");
        }
        await CargarAsync();
        Informar(hechas.Count == 1 ? $"{hechas[0]} eliminada." : $"{hechas.Count} ventas eliminadas.", "No se ha eliminado", hechas, problemas);
    }

    /// <summary>«Facturar ventas…»: elige el mes y si se validan, crea las facturas y resume lo creado y lo que no se ha podido facturar.</summary>
    private async Task FacturarAsync(IReadOnlySet<int>? soloVentas)
    {
        if (FichaCambiada && !await GuardarFichaAsync()) return;
        try
        {
            var hasta = soloVentas is null ? _ventas.HastaPropuesto : _todas.Where(f => soloVentas.Contains(f.Id)).Max(f => f.Fecha);
            var eleccion = _modulo.Dialogos.ElegirFacturacionVentas(hasta, soloVentas is not null, h => _ventas.PrevisualizarAsync(_s.Trabajo.IdSociedad, h, soloVentas));
            if (eleccion is not { } e) return;
            var r = await _ventas.FacturarAsync(_s.Trabajo.IdSociedad, e.Hasta, e.Validar, _s.Actor, soloVentas);
            if (!r.EsCorrecto) { Aviso("No se han creado las facturas", Errores(r), null); return; }
            await CargarAsync();
            _modulo.FacturaCambiada();
            var res = r.Valor;
            var titulo = res.Facturas.Count == 0 ? "No se ha creado ninguna factura" : Formatos.Plural(res.Facturas.Count, "factura creada", "facturas creadas");
            var lineas = res.Facturas.Select(f => $"{f.Rotulo} · {f.Cliente} · {f.Piso} · {f.Mes:MM/yyyy} · {Formatos.Plural(f.Ventas, "venta", "ventas")} · {Formatos.Importe(f.Total)}").ToList();
            if (res.SinIncidencias)
            {
                _modulo.Avisar($"{titulo}. Las ves en Facturas{(e.Validar ? "" : "; los borradores se validan con «Validar…»")}.");
                return;
            }
            Aviso(titulo, res.Facturas.Count == 0 ? "No se ha podido facturar ninguna venta. Revisa lo siguiente:" : "Lo creado ya está guardado; lo demás necesita revisión:",
                [.. lineas, .. res.Incidencias.Select(i => "⚠ " + i)]);
        }
        catch (Exception ex)
        {
            Aviso("No se ha podido facturar", $"Ha fallado la facturación de ventas: {ex.Message} Lo que ya se hubiera creado está guardado; actualiza la lista y vuelve a intentarlo.", [ex.ToString()]);
        }
    }

    /// <summary>«Importar de Avaibook…»: elige las fechas de entrada e importa; resume lo importado y las incidencias.</summary>
    private async Task ImportarAsync()
    {
        if (_modulo.Avaibook is not { } importador) return;
        var conexion = await importador.ConexionAsync(_s.Trabajo.IdSociedad);
        if (conexion is null || !conexion.Activa || string.IsNullOrWhiteSpace(conexion.Token))
        {
            Aviso("Falta la conexión con Avaibook", "Para importar, guarda antes el token de Avaibook en Parámetros y tablas › Conexión con Avaibook (la clave «owner» de app.avaibook.com › API).", null);
            return;
        }
        var hoy = DateTime.Today;
        var desde = conexion.UltimaImportacion is DateTime u ? new DateTime(u.Year, u.Month, 1).AddMonths(-2) : conexion.ImportarDesde ?? new DateTime(hoy.Year, 1, 1);
        if (conexion.ImportarDesde is DateTime minimo && desde < minimo) desde = minimo;
        var fechas = _modulo.Dialogos.ElegirFechasImportacion(desde, hoy.AddMonths(12));
        if (fechas is not { } f) return;
        try
        {
            var r = await importador.ImportarAsync(_s.Trabajo.IdSociedad, _s.Actor, f.Desde, f.Hasta);
            if (!r.EsCorrecto) { Aviso("No se ha importado", Errores(r), null); return; }
            await CargarAsync();
            var res = r.Valor;
            if (res.Incidencias.Count == 0) { _modulo.Avisar($"Avaibook: {res.Resumen}"); return; }
            Aviso("Importación de Avaibook", res.Resumen, res.Incidencias);
        }
        catch (Exception ex)
        {
            Aviso("No se ha podido importar", $"Ha fallado la importación de Avaibook: {ex.Message} Vuelve a intentarlo; lo importado ya está guardado y no se duplica.", [ex.ToString()]);
        }
    }

    private void Informar(string aviso, string tituloFallo, IReadOnlyList<string> hechas, IReadOnlyList<string> problemas)
    {
        if (problemas.Count == 0) { _modulo.Avisar(aviso); return; }
        Aviso(hechas.Count == 0 ? tituloFallo : aviso, hechas.Count == 0 ? "Revisa lo siguiente:" : $"Hechas: {string.Join(", ", hechas)}. Las demás necesitan revisión:", problemas);
    }

    private void Aviso(string titulo, string mensaje, IReadOnlyList<string>? detalles) =>
        _modulo.Dialogos.Preguntar(titulo, mensaje, detalles, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
}
