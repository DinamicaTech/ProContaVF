using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Facturacion.Facturas;
using ProConta.Seguridad.Permisos;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Facturacion.UI.VistaModelos;

/// <summary>Fila del listado: la factura y su marca de selección (independiente de la fila activa).</summary>
public sealed class FilaFacturaVM(FilaFactura datos, Action marcaCambiada, Func<int, Task<FacturaVenta?>> cargar) : ModeloVista
{
    private bool _marcada;
    private bool _desplegada;
    private IReadOnlyList<LineaDetalle> _lineas = [];

    /// <summary>Líneas desplegadas bajo la fila (solo lectura); se leen al desplegar.</summary>
    public bool Desplegada
    {
        get => _desplegada;
        set
        {
            if (!Fijar(ref _desplegada, value) || !value || _lineas.Count > 0) return;
            _ = LeerLineasAsync();
        }
    }

    public IReadOnlyList<LineaDetalle> Lineas { get => _lineas; private set => Fijar(ref _lineas, value); }

    private async Task LeerLineasAsync()
    {
        if (await cargar(Datos.IdFactura) is { } f) Lineas = f.Lineas.Select(l => new LineaDetalle(l)).ToList();
    }

    public FilaFactura Datos { get; } = datos;

    public bool Marcada
    {
        get => _marcada;
        set { if (Fijar(ref _marcada, value)) marcaCambiada(); }
    }

    public string Numero => Datos.Rotulo;
    public string Fecha => Datos.FechaFactura is { } f ? Formatos.Fecha(f) : "";
    public DateTime? FechaOrden => Datos.FechaFactura;
    public string Cliente => Datos.Cliente;
    public string? Nif => Datos.Nif;
    public string Descripcion => Datos.Descripcion;
    public string Serie => Datos.PrefijoSerie ?? "";
    public decimal Base => Datos.BaseImponible;
    public decimal Total => Datos.ImporteTotal;
    public string TextoBase => Formatos.Importe(Datos.BaseImponible);
    public string TextoTotal => Formatos.Importe(Datos.ImporteTotal);
    public string Estado => Datos.TextoEstado;
    public string VeriFactu => Datos.TextoVeriFactu;
    public string Origen => Datos.EsRectificativa ? "Rectificativa" : Datos.EsProgramada ? "Programada" : "Manual";
    public string Asiento => Datos.EsBorrador ? "" : Datos.IdAsientoContable is null ? "Sin asiento" : "Contabilizada";
    public bool SinAsiento => !Datos.EsBorrador && Datos.IdAsientoContable is null;
    public string? Relacion => Datos.NumeroRectificada is { } o ? $"Rectifica a {o}" + (Datos.MotivoRectificacion is { } m ? $" · Motivo: {m}" : "")
        : Datos.NumeroRectificativa is { } r ? $"Rectificada por {r}" : null;
}

/// <summary>Opción de un filtro desplegable.</summary>
public sealed record OpcionFiltro(string Texto, string? Valor)
{
    public override string ToString() => Texto;
}

/// <summary>
/// Pantalla Facturas emitidas (facturacion/pantallas): listado filtrable con fila activa (la del detalle), marcas de
/// selección y ámbito de las acciones colectivas (las marcadas o, si no hay, la activa). Validar y contabilizar actúan
/// sobre el ámbito; editar, duplicar, rectificar y eliminar, sobre la fila activa.
/// </summary>
public sealed class ListadoFacturasVM : ModeloVista, IPestanaTrabajo
{
    private readonly ModuloFacturacion _modulo;
    private readonly ServiciosFacturacion _s;
    private List<FilaFacturaVM> _todas = [];
    private object? _vista;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private FilaFacturaVM? _activa;
    private FacturaVenta? _detalle;
    private string _texto = "";
    private OpcionFiltro _filtroEstado, _filtroVeriFactu, _filtroSerie, _filtroOrigen;
    private string _desde = "", _hasta = "";
    private bool _puedeEditar, _puedeValidar, _puedeRectificar;
    private int _cargas;

    internal ListadoFacturasVM(ModuloFacturacion modulo)
    {
        _modulo = modulo;
        _s = modulo.Servicios;
        OpcionesEstado =
        [
            new("Todos los estados", null), new("Borrador", nameof(EstadoFactura.Borrador)), new("Validada", nameof(EstadoFactura.Validada)),
            new("Rectificación pendiente", nameof(EstadoFactura.RectificacionPendiente)), new("Rectificada", nameof(EstadoFactura.Rectificada))
        ];
        OpcionesVeriFactu = [new("VeriFactu: todas", null), new("No aplica", "No aplica"), new("Sin VeriFactu", "Sin VeriFactu"), new("Pendiente", "Pendiente")];
        OpcionesOrigen = [new("Todos los orígenes", null), new("Manual", "Manual"), new("Programada", "Programada"), new("Rectificativa", "Rectificativa")];
        OpcionesSerie.Add(new("Todas las series", null));
        _filtroEstado = OpcionesEstado[0];
        _filtroVeriFactu = OpcionesVeriFactu[0];
        _filtroOrigen = OpcionesOrigen[0];
        _filtroSerie = OpcionesSerie[0];

        NuevaCommand = new ComandoDelegado(_modulo.NuevaFactura, () => _puedeEditar);
        AbrirCommand = new ComandoDelegado(() => { if (_activa is not null) _modulo.AbrirFactura(_activa.Datos.IdFactura); }, () => _activa is not null);
        ValidarCommand = new ComandoDelegado(() => _ = ValidarAsync(), () => _puedeValidar && Ambito.Any(f => f.Datos.EsBorrador));
        ContabilizarCommand = new ComandoDelegado(() => _ = ContabilizarAsync(), () => _puedeValidar && Ambito.Any(f => f.SinAsiento));
        DuplicarCommand = new ComandoDelegado(() => _ = DuplicarAsync(), () => _puedeEditar && _activa is not null);
        RectificarCommand = new ComandoDelegado(() => _ = RectificarAsync(), () => _puedeRectificar && _activa is { Datos: { Estado: EstadoFactura.Validada, EsRectificativa: false } });
        EliminarCommand = new ComandoDelegado(() => _ = EliminarAsync(), () => _puedeEditar && _activa is { Datos.EsBorrador: true });
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_activa?.Datos.IdFactura));
        LimpiarFiltrosCommand = new ComandoDelegado(LimpiarFiltros, () => HayFiltros);
        MarcarTodasCommand = new ComandoDelegado(() => MarcarTodas(true), () => Filas.Count > 0);
        DesmarcarCommand = new ComandoDelegado(() => MarcarTodas(false), () => Marcadas.Count > 0);
    }

    // ---------- pestaña ----------

    public string Titulo => "Facturas emitidas";
    public string Ayuda => $"Facturas y borradores de {_s.RazonSocial} en {_s.Trabajo.Ejercicio}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaListadoFacturas { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _modulo.ListadoCerrado();

    // ---------- estado ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string TituloEstado => Estado switch
    {
        EstadoVista.Vacio => _todas.Count == 0 ? "Aún no hay facturas en este ejercicio" : "Ninguna factura cumple los filtros",
        EstadoVista.Error => "No he podido leer las facturas",
        EstadoVista.Cargando => "Leyendo facturas…",
        _ => ""
    };
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string? AccionEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 => _puedeEditar ? "Nueva factura" : null,
        EstadoVista.Vacio => "Quitar filtros",
        EstadoVista.Error => "Reintentar",
        _ => null
    };
    public ICommand? ComandoEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 => NuevaCommand,
        EstadoVista.Vacio => LimpiarFiltrosCommand,
        EstadoVista.Error => RefrescarCommand,
        _ => null
    };

    // ---------- filtros ----------

    public IReadOnlyList<OpcionFiltro> OpcionesEstado { get; }
    public IReadOnlyList<OpcionFiltro> OpcionesVeriFactu { get; }
    public IReadOnlyList<OpcionFiltro> OpcionesOrigen { get; }
    public ObservableCollection<OpcionFiltro> OpcionesSerie { get; } = [];

    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public OpcionFiltro FiltroEstado { get => _filtroEstado; set { if (value is not null && Fijar(ref _filtroEstado, value)) Filtrar(); } }
    public OpcionFiltro FiltroVeriFactu { get => _filtroVeriFactu; set { if (value is not null && Fijar(ref _filtroVeriFactu, value)) Filtrar(); } }
    public OpcionFiltro FiltroSerie { get => _filtroSerie; set { if (value is not null && Fijar(ref _filtroSerie, value)) Filtrar(); } }
    public OpcionFiltro FiltroOrigen { get => _filtroOrigen; set { if (value is not null && Fijar(ref _filtroOrigen, value)) Filtrar(); } }

    public string Desde
    {
        get => _desde;
        set { _desde = value ?? ""; Avisar(); Avisar(nameof(ErrorFechas)); Filtrar(); }
    }

    public string Hasta
    {
        get => _hasta;
        set { _hasta = value ?? ""; Avisar(); Avisar(nameof(ErrorFechas)); Filtrar(); }
    }

    public string? ErrorFechas =>
        !VistaModelos.Texto.LeerFecha(_desde, out _) || !VistaModelos.Texto.LeerFecha(_hasta, out _) ? "Escribe las fechas como dd/mm/aaaa." : null;

    public bool HayFiltros => _texto.Length > 0 || _filtroEstado.Valor is not null || _filtroVeriFactu.Valor is not null || _filtroSerie.Valor is not null
                              || _filtroOrigen.Valor is not null || _desde.Length > 0 || _hasta.Length > 0;

    private void LimpiarFiltros()
    {
        _texto = ""; _desde = ""; _hasta = "";
        _filtroEstado = OpcionesEstado[0];
        _filtroVeriFactu = OpcionesVeriFactu[0];
        _filtroSerie = OpcionesSerie[0];
        _filtroOrigen = OpcionesOrigen[0];
        Avisar(nameof(Texto), nameof(Desde), nameof(Hasta), nameof(FiltroEstado), nameof(FiltroVeriFactu), nameof(FiltroSerie), nameof(FiltroOrigen), nameof(ErrorFechas));
        Filtrar();
    }

    private bool Cumple(FilaFacturaVM f)
    {
        var d = f.Datos;
        if (!d.Contiene(_texto)) return false;
        if (_filtroEstado.Valor is { } e && d.Estado.ToString() != e) return false;
        if (_filtroVeriFactu.Valor is { } v && d.TextoVeriFactu != v) return false;
        if (_filtroSerie.Valor is { } s && f.Serie != s) return false;
        if (_filtroOrigen.Valor is { } o && f.Origen != o) return false;
        if (VistaModelos.Texto.LeerFecha(_desde, out var desde) && desde is { } dd && (d.FechaFactura is null || d.FechaFactura.Value.Date < dd)) return false;
        if (VistaModelos.Texto.LeerFecha(_hasta, out var hasta) && hasta is { } hh && (d.FechaFactura is null || d.FechaFactura.Value.Date > hh)) return false;
        return true;
    }

    private void Filtrar()
    {
        var id = _activa?.Datos.IdFactura;
        Filas.Clear();
        foreach (var f in _todas.Where(Cumple)) Filas.Add(f);
        // Lo que queda fuera del filtro deja de estar marcado: el ámbito nunca incluye filas que no se ven.
        foreach (var f in _todas.Where(f => f.Marcada && !Filas.Contains(f))) f.Marcada = false;
        Activa = Filas.FirstOrDefault(f => f.Datos.IdFactura == id) ?? Filas.FirstOrDefault();
        Estado = _todas.Count == 0 || Filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = _todas.Count == 0 ? "Crea la primera con «Nueva factura»." : "Cambia o quita los filtros para ver más facturas.";
        Avisar(nameof(HayFiltros), nameof(Pie), nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado));
        MarcasCambiadas();
    }

    // ---------- filas, activa, marcas y ámbito ----------

    public ObservableCollection<FilaFacturaVM> Filas { get; } = [];

    /// <summary>Fila activa: la que se ve en el detalle (barra naranja en la maqueta).</summary>
    public FilaFacturaVM? Activa
    {
        get => _activa;
        set
        {
            if (!Fijar(ref _activa, value)) return;
            Avisar(nameof(HayActiva), nameof(TextoAmbito));
            _ = CargarDetalleAsync(value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayActiva => _activa is not null;

    public IReadOnlyList<FilaFacturaVM> Marcadas => Filas.Where(f => f.Marcada).ToList();

    /// <summary>Ámbito de las acciones colectivas: las marcadas o, si no hay ninguna, la activa.</summary>
    public IReadOnlyList<FilaFacturaVM> Ambito => Marcadas is { Count: > 0 } m ? m : _activa is null ? [] : [_activa];

    public string TextoAmbito => Marcadas.Count switch
    {
        0 when _activa is null => "Sin factura activa",
        0 => $"Acciones sobre la factura activa: {_activa!.Numero}",
        var n => $"Acciones sobre {Formatos.Plural(n, "factura marcada", "facturas marcadas")} · {Formatos.Importe(Marcadas.Sum(f => f.Total))}"
    };

    public string Pie
    {
        get
        {
            var total = Filas.Sum(f => f.Total);
            var texto = $"{Formatos.Plural(Filas.Count, "factura", "facturas")} · total {Formatos.Importe(total)}";
            return Filas.Count == _todas.Count ? texto : $"{texto} (de {Formatos.Recuento(_todas.Count)})";
        }
    }

    private void MarcasCambiadas()
    {
        Avisar(nameof(TextoAmbito), nameof(Marcadas), nameof(Ambito));
        CommandManager.InvalidateRequerySuggested();
    }

    private void MarcarTodas(bool marcar)
    {
        foreach (var f in Filas) f.Marcada = marcar;
    }

    // ---------- detalle ----------

    public FacturaVenta? Detalle { get => _detalle; private set { if (Fijar(ref _detalle, value)) AvisarDetalle(); } }
    public bool HayDetalle => _detalle is not null;
    public string DetalleTitulo => _detalle?.Rotulo ?? "";
    public string DetalleEstado => _detalle is null ? "" : TextosEstado.Texto(_detalle.Estado);
    public string DetalleCliente => _detalle is null ? "" : _detalle.Cliente.EstaVacia ? _activa?.Cliente ?? "" : _detalle.Cliente.NombreVisible;
    public string DetalleNif => _detalle?.Cliente.Nif ?? _activa?.Nif ?? "";
    public string DetalleDomicilio => _detalle?.Cliente.DomicilioEnUnaLinea ?? "";
    public string DetalleFechas => _detalle is null ? "" :
        string.Join(" · ", new[]
        {
            _detalle.FechaFactura is { } f ? $"Fecha {Formatos.Fecha(f)}" : "Sin fecha",
            _detalle.FechaInicioValidez is { } d && _detalle.FechaFinValidez is { } h ? $"Periodo {Formatos.Fecha(d)} – {Formatos.Fecha(h)}" : null,
            _detalle.TipoFactura
        }.Where(x => x is not null));
    public string DetalleDescripcion => _detalle?.Descripcion ?? "";
    public string? DetalleNotas => string.IsNullOrWhiteSpace(_detalle?.NotasInternas) ? null : _detalle!.NotasInternas;
    public string? DetalleRelacion => _activa?.Relacion;
    public IReadOnlyList<LineaDetalle> DetalleLineas => _detalle?.Lineas.Select(l => new LineaDetalle(l)).ToList() ?? [];
    public string DetalleBase => _detalle is null ? "" : Formatos.Importe(_detalle.Totales.BaseImponible.Valor);
    public string DetalleIva => _detalle is null ? "" : Formatos.Importe(_detalle.Totales.Iva.Valor);
    public string DetalleRetencion => _detalle is null ? "" : Formatos.Importe(-_detalle.Totales.Retencion.Valor);
    public string DetalleTotal => _detalle is null ? "" : Formatos.Importe(_detalle.Totales.ImporteTotal.Valor);
    public string DetalleAsiento => _detalle is null ? "" : _detalle.EsBorrador ? "Se generará al validar" : _detalle.IdAsientoContable is int a ? $"Asiento generado (id {a})" : "Sin asiento: usa «Contabilizar»";
    public IReadOnlyList<string> DetalleHistorial => _detalle?.LineasHistorial.Reverse().Take(8).ToList() ?? [];

    private void AvisarDetalle() =>
        Avisar(nameof(HayDetalle), nameof(DetalleTitulo), nameof(DetalleEstado), nameof(DetalleCliente), nameof(DetalleNif), nameof(DetalleDomicilio), nameof(DetalleFechas),
            nameof(DetalleDescripcion), nameof(DetalleNotas), nameof(DetalleRelacion), nameof(DetalleLineas), nameof(DetalleBase), nameof(DetalleIva), nameof(DetalleRetencion),
            nameof(DetalleTotal), nameof(DetalleAsiento), nameof(DetalleHistorial));

    private async Task CargarDetalleAsync(FilaFacturaVM? fila)
    {
        if (fila is null) { Detalle = null; return; }
        var f = await _s.Facturas.ObtenerAsync(fila.Datos.IdFactura);
        if (ReferenceEquals(_activa, fila)) Detalle = f;
    }

    // ---------- carga ----------

    public async Task CargarAsync(int? idActiva = null)
    {
        var carga = ++_cargas;
        if (_todas.Count == 0) Estado = EstadoVista.Cargando;
        try
        {
            _puedeEditar = await _s.Facturas.PuedeAsync(Acciones.FacturasEditar);
            _puedeValidar = await _s.Facturas.PuedeAsync(Acciones.FacturasValidar);
            _puedeRectificar = await _s.Facturas.PuedeAsync(Acciones.FacturasRectificar);
            var filas = await _s.Consulta.ListarAsync(_s.Trabajo.IdSociedadEjercicio);
            if (carga != _cargas) return;
            var marcadas = _todas.Where(f => f.Marcada).Select(f => f.Datos.IdFactura).ToHashSet();
            var activa = idActiva ?? _activa?.Datos.IdFactura;
            _todas = filas.Select(f => new FilaFacturaVM(f, MarcasCambiadas, _s.Facturas.ObtenerAsync)).ToList();
            foreach (var f in _todas.Where(f => marcadas.Contains(f.Datos.IdFactura))) f.Marcada = true;
            var seriePrevia = _filtroSerie.Valor;
            OpcionesSerie.Clear();
            OpcionesSerie.Add(new("Todas las series", null));
            foreach (var p in _todas.Select(f => f.Serie).Where(p => p.Length > 0).Distinct().Order(StringComparer.Ordinal)) OpcionesSerie.Add(new(p, p));
            _filtroSerie = OpcionesSerie.FirstOrDefault(o => o.Valor == seriePrevia) ?? OpcionesSerie[0];
            Avisar(nameof(FiltroSerie));
            _activa = null;
            Filtrar();
            if (activa is int id && Filas.FirstOrDefault(f => f.Datos.IdFactura == id) is { } fila) Activa = fila;
            else _ = CargarDetalleAsync(_activa);
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = ex.Message;
            Avisar(nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado));
        }
    }

    // ---------- acciones ----------

    public ICommand NuevaCommand { get; }
    public ICommand AbrirCommand { get; }
    public ICommand ValidarCommand { get; }
    public ICommand ContabilizarCommand { get; }
    public ICommand DuplicarCommand { get; }
    public ICommand RectificarCommand { get; }
    public ICommand EliminarCommand { get; }
    public ICommand RefrescarCommand { get; }
    public ICommand LimpiarFiltrosCommand { get; }
    public ICommand MarcarTodasCommand { get; }
    public ICommand DesmarcarCommand { get; }

    public string MotivoVeriFactu => "Llega con el desarrollo de VeriFactu: hoy las facturas se validan «sin VeriFactu» (solo BD de pruebas).";
    public string MotivoPdf => "Llega con el desarrollo del PDF.";
    public string MotivoEnviar => "Llega con el envío por correo.";
    public string MotivoProgramadas => "Las facturas programadas llegan con su propio desarrollo (facturacion/programadas).";

    /// <summary>Valida los borradores del ámbito, uno a uno; los que tienen total cero se confirman aparte.</summary>
    private async Task ValidarAsync()
    {
        var borradores = Ambito.Where(f => f.Datos.EsBorrador).ToList();
        if (borradores.Count == 0) return;
        var opcion = _modulo.Dialogos.Preguntar(borradores.Count == 1 ? $"Validar {borradores[0].Numero}" : $"Validar {borradores.Count} borradores",
            $"Cada borrador recibe el siguiente número de su serie, se copian los datos del cliente y se genera su asiento. Después no se podrán cambiar.{Environment.NewLine}{Environment.NewLine}" +
            $"Validación provisional «{_s.Facturas.NombrePasoFiscal}»: no se envían a la AEAT y solo valen en la BD de pruebas.",
            borradores.Select(b => $"{b.Numero} · {b.Cliente} · {b.TextoTotal}").ToList(),
            new OpcionMensaje("Cancelar", "No valida nada", Cancelar: true),
            new OpcionMensaje("Validar", "Numera y contabiliza", Principal: true));
        if (opcion != 1) return;

        var correctas = new List<string>();
        var problemas = new List<string>();
        var ceros = new List<FilaFacturaVM>();
        foreach (var b in borradores)
        {
            var r = await _s.Facturas.ValidarAsync(b.Datos.IdFactura, b.Datos.Version, _s.Actor);
            if (r.SoloAvisos && r.Incidencias.All(i => i.Codigo == Incidencia.ImporteCero)) { ceros.Add(b); continue; }
            Anotar(b, r, correctas, problemas);
        }
        if (ceros.Count > 0 && _modulo.Dialogos.Preguntar("Facturas con total cero",
                $"{Formatos.Plural(ceros.Count, "borrador tiene", "borradores tienen")} total 0,00 €. Comprueba que no falte ningún precio o línea; si es correcto, valídalos igualmente.",
                ceros.Select(c => $"{c.Numero} · {c.Cliente}").ToList(),
                new OpcionMensaje("No validarlos", "Los deja como borradores", Cancelar: true),
                new OpcionMensaje("Validar con total cero", "Los numera igualmente", Principal: true)) == 1)
            foreach (var b in ceros)
                Anotar(b, await _s.Facturas.ValidarAsync(b.Datos.IdFactura, b.Datos.Version, _s.Actor, aceptarAvisos: true), correctas, problemas);
        else
            problemas.AddRange(ceros.Select(c => $"{c.Numero}: no validado (total cero)."));

        await CargarAsync(_activa?.Datos.IdFactura);
        Informar(correctas.Count == 1 ? $"Validada {correctas[0]}" : $"{Formatos.Plural(correctas.Count, "factura validada", "facturas validadas")}", correctas, problemas);
    }

    private static void Anotar(FilaFacturaVM b, ResultadoValidacion r, List<string> correctas, List<string> problemas)
    {
        if (!r.Validada) problemas.Add($"{b.Numero}: {string.Join(" ", r.Incidencias.Select(i => i.Mensaje))}");
        else if (!r.Contabilizada) problemas.Add($"{r.Factura!.NumeroFactura}: validada pero sin asiento. {string.Join(" ", (r.ErrorContabilizacion ?? []).Select(e => e.Mensaje))}");
        else correctas.Add(r.Factura!.NumeroFactura!);
    }

    private async Task ContabilizarAsync()
    {
        var pendientes = Ambito.Where(f => f.SinAsiento).ToList();
        var correctas = new List<string>();
        var problemas = new List<string>();
        foreach (var f in pendientes)
        {
            var r = await _s.Facturas.ContabilizarAsync(f.Datos.IdFactura, _s.Actor);
            if (r.EsCorrecto) correctas.Add(f.Numero);
            else problemas.Add($"{f.Numero}: {string.Join(" ", r.Errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")))}");
        }
        await CargarAsync(_activa?.Datos.IdFactura);
        Informar($"{Formatos.Plural(correctas.Count, "factura contabilizada", "facturas contabilizadas")}", correctas, problemas);
    }

    private void Informar(string titulo, List<string> correctas, List<string> problemas)
    {
        if (problemas.Count == 0) { _modulo.Avisar(titulo + (correctas.Count > 1 ? $": {string.Join(", ", correctas)}" : ".")); return; }
        _modulo.Dialogos.Preguntar(correctas.Count == 0 ? "No se ha podido completar" : titulo,
            correctas.Count == 0 ? "Ninguna factura se ha completado. Revisa lo siguiente:" : $"Hechas: {string.Join(", ", correctas)}. Las demás necesitan revisión:",
            problemas, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
    }

    private async Task DuplicarAsync()
    {
        if (_activa is null) return;
        var r = await _s.Facturas.DuplicarAsync(_activa.Datos.IdFactura, _s.Trabajo, _s.Actor);
        if (!r.EsCorrecto) { Error("No se ha duplicado", r.Errores); return; }
        await CargarAsync(r.Valor.IdFactura);
        _modulo.AbrirFactura(r.Valor.IdFactura);
    }

    private async Task RectificarAsync()
    {
        if (_activa is null) return;
        var fila = _activa;
        var tipo = ReglasFactura.TipoRectificativaDe(fila.Datos.TipoFactura);
        var motivo = _modulo.Dialogos.PedirMotivoRectificacion(fila.Numero,
                $"Se crea un borrador rectificativo {tipo} en la serie de rectificativas con las mismas líneas y las cantidades en negativo. Mientras sea borrador, {fila.Numero} queda en «Rectificación pendiente»; al validarlo pasa a «Rectificada». Si eliminas el borrador, vuelve a «Validada».");
        if (motivo is null) return;
        var r = await _s.Facturas.CrearRectificativaAsync(fila.Datos.IdFactura, motivo, _s.Trabajo, _s.Actor);
        if (!r.EsCorrecto) { Error("No se ha creado la rectificativa", r.Errores); return; }
        await CargarAsync(r.Valor.IdFactura);
        _modulo.AbrirFactura(r.Valor.IdFactura);
    }

    private async Task EliminarAsync()
    {
        if (_activa is not { Datos.EsBorrador: true } fila) return;
        var mensaje = fila.Datos.EsRectificativa
            ? $"Se borra este borrador y la factura {fila.Datos.NumeroRectificada} vuelve a «Validada», todo a la vez."
            : "Se borra el borrador y sus líneas. No consume número.";
        if (_modulo.Dialogos.Preguntar($"Eliminar {fila.Numero}", mensaje, null, new OpcionMensaje("Cancelar", "No borra nada", Cancelar: true),
                new OpcionMensaje("Eliminar", "Borra el borrador", Peligro: true)) != 1) return;
        var r = await _s.Facturas.EliminarBorradorAsync(fila.Datos.IdFactura, fila.Datos.Version, _s.Actor);
        if (!r.EsCorrecto) { Error("No se ha eliminado", r.Errores); return; }
        _modulo.Avisar($"{fila.Numero} eliminado.");
        await CargarAsync();
    }

    private void Error(string titulo, IReadOnlyList<ProConta.Nucleo.Resultados.Error> errores) =>
        _modulo.Dialogos.Preguntar(titulo, errores.Count == 1 ? errores[0].Mensaje + (errores[0].AccionCorrectora is { } a ? " " + a : "") : "Revisa lo siguiente:",
            errores.Count == 1 ? null : errores.Select(e => e.Mensaje).ToList(), new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
}

/// <summary>Línea del desplegable de detalle (solo lectura).</summary>
public sealed record LineaDetalle(LineaFacturaVenta Linea)
{
    public string Nombre => Linea.Nombre;
    public string Descripcion => Linea.Descripcion;
    public string Cantidad => Texto.Cantidad(Linea.Cantidad);
    public string Precio => Texto.Precio(Linea.PrecioUnitario);
    public string Base => Formatos.Numero(Linea.BaseImponible.Valor);
    public string Iva => Linea.EsSuplido ? "Suplido" : $"{Texto.Porcentaje(Linea.TipoIva)} %";
    public string Total => Formatos.Numero(Linea.ImporteTotal.Valor);
    public string Cuenta => Linea.CuentaContable ?? "";
}
