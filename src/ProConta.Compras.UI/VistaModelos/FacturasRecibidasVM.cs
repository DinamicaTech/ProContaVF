using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Compras.Facturas;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>Una fila del filtro de la lista (botón «Filtro»): los tipos de operación se suman entre sí; el resto se exige a la vez.</summary>
public sealed class CriterioFiltroVM(string texto, string clave, bool esOperacion, Action cambio) : ModeloVista
{
    private bool _marcado;
    public string Texto { get; } = texto;
    public string Clave { get; } = clave;
    public bool EsOperacion { get; } = esOperacion;
    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value)) cambio(); } }
    internal void Quitar() { _marcado = false; Avisar(nameof(Marcado)); }
}

/// <summary>
/// Facturas recibidas (compras/pantallas, prototipo versión 26): lista con pestañas Pendientes y Contabilizadas, filtros
/// rápidos, búsqueda (con «f+» para el fichero) y filtro por tipo de operación, retención, suplidos, inversión del sujeto
/// pasivo y rectificativas; ficha central editable y visor del PDF. Todas las acciones están en el menú contextual. Al
/// cambiar de factura se guardan sus cambios sin preguntar.
/// En modo «Probar» (pestaña Reconocimiento de facturas) la misma pantalla vuelve a reconocer el PDF y compara cada campo
/// con lo grabado, sin escribir nada.
/// </summary>
public sealed class FacturasRecibidasVM : ModeloVista, IPestanaTrabajo
{
    private readonly ModuloCompras _modulo;
    private readonly ServiciosCompras _s;
    private List<FilaCompraVM> _todas = [];
    private IReadOnlyList<FilaFacturaCompra> _datos = [];
    private IReadOnlyList<ProveedorFicha> _proveedores = [];
    private IReadOnlyList<OpcionFicha<string>> _cuentasGasto = [];
    private IReadOnlyList<CentroCoste> _centros = [];
    private IReadOnlyList<decimal> _tiposIva = [];
    private IReadOnlyList<(byte Id, string Descripcion)> _formasPago = [];
    private IReadOnlyList<FilaCompraVM> _seleccion = [];
    private object? _vista;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "", _texto = "", _ocupado = "";
    private string _pestanaLista;
    private OpcionCompra _rapido;
    private FilaCompraVM? _activa;
    private FichaCompraVM? _ficha;
    private IReadOnlyList<ComparacionCampo> _comparacion = [];
    private int? _relacionadasDe;
    private bool _puedeEditar;
    private string? _errorFicha;
    private int _cargaFicha;
    private bool _prueba;

    internal FacturasRecibidasVM(ModuloCompras modulo, bool prueba)
    {
        _modulo = modulo;
        _s = modulo.Servicios;
        _prueba = prueba;
        _pestanaLista = prueba ? "todas" : "pendientes";
        Filtros =
        [
            new("Nacionales", "nac", true, Filtrar), new("Intracomunitarias", "int", true, Filtrar), new("Extranjeras (importación)", "imp", true, Filtrar),
            new("Con retención", "ret", false, Filtrar), new("Con suplidos", "sup", false, Filtrar), new("Con inversión del sujeto pasivo", "isp", false, Filtrar),
            new("Rectificativas y abonos", "rect", false, Filtrar)
        ];
        _rapido = OpcionesRapidas[0];

        BuscarPdfNuevosCommand = new ComandoDelegado(() => _ = BuscarPdfNuevosAsync(), () => !Prueba && _puedeEditar && !Ocupado);
        ReconocerCommand = new ComandoDelegado(() => _ = ReconocerDeNuevoAsync(), () => _activa is { PdfExiste: true } && (Prueba || _ficha is { Editable: true }) && !Ocupado);
        CompararTodasCommand = new ComandoDelegado(() => _ = CompararTodasAsync(), () => Prueba && Filas.Count > 0 && !Ocupado);
        // Guardar sin cambios también vale: confirma lo leído y enseña las plantillas del proveedor (compras/reconocimiento 2026-11-05 18:42).
        GuardarCommand = new ComandoDelegado(() => _ = GuardarOEnsenarAsync(), () => (_ficha is { Editable: true } || PuedeEnsenar) && !Ocupado);
        DescartarCommand = new ComandoDelegado(() => { Descartar(); }, () => _ficha is { TieneCambios: true });
        AbrirPdfCommand = new ComandoDelegado(() => { if (_activa is { PdfExiste: true } a) _modulo.Dialogos.Abrir(a.Datos.RutaPdf); }, () => _activa is { PdfExiste: true });
        CopiarRutaCommand = new ComandoDelegado(CopiarRuta, () => _activa is not null && _activa.Datos.RutaPdf.Length > 0);
        SepararCommand = new ComandoDelegado(() => _ = SepararAsync(), () => !Prueba && _puedeEditar && _activa is { PdfExiste: true } && !_activa.Datos.Contabilizada && !Ocupado);
        AbrirCarpetaCommand = new ComandoDelegado(() => { if (_activa is { } a && Path.GetDirectoryName(a.Datos.RutaPdf) is { } d) _modulo.Dialogos.Abrir(d); }, () => _activa is not null);
        VerRelacionadasCommand = new ComandoDelegado(VerRelacionadas, () => _activa is not null && (_activa.Datos.IdRegistroFacturaRectificada is not null || _datos.Any(d => d.IdRegistroFacturaRectificada == _activa.IdRegistro)));
        QuitarRelacionadasCommand = new ComandoDelegado(() => { _relacionadasDe = null; Avisar(nameof(TextoRelacionadas), nameof(HayRelacionadas)); Filtrar(); }, () => _relacionadasDe is not null);
        AbrirProveedorCommand = new ComandoDelegado(() => { if (_ficha?.IdTerceroProveedor is int t) _modulo.AbrirFichaTercero(t); }, () => _ficha?.IdTerceroProveedor is not null);
        AnadirTipoCommand = new ComandoDelegado(() => _ficha?.AnadirTipo(), () => _ficha is { Editable: true });
        QuitarTipoCommand = new ComandoDelegado(p => { if (p is DesgloseVM d) _ficha?.QuitarTipo(d); }, _ => _ficha is { Editable: true, Desgloses.Count: > 1 });
        RefrescarCommand = new ComandoDelegado(() => _ = RefrescarAsync());
        LimpiarFiltrosCommand = new ComandoDelegado(LimpiarFiltros, () => HayFiltros);
        VerPendientesCommand = new ComandoDelegado(() => PestanaLista = "pendientes");
        VerContabilizadasCommand = new ComandoDelegado(() => PestanaLista = "contabilizadas");
        VerTodasCommand = new ComandoDelegado(() => PestanaLista = "todas");
    }

    /// <summary>
    /// Modo prueba (casilla del menú contextual): al elegir una factura se reconoce de nuevo su PDF y se compara con lo
    /// grabado, sin guardar nada. Al entrar se guardan antes los cambios de la ficha.
    /// </summary>
    public bool Prueba
    {
        get => _prueba;
        set { if (value != _prueba) _ = CambiarModoAsync(value); }
    }

    private async Task CambiarModoAsync(bool prueba)
    {
        if (TieneCambios && !await GuardarFichaAsync())
        {
            Avisar(nameof(Prueba));
            return;
        }
        _prueba = prueba;
        _pestanaLista = prueba ? "todas" : "pendientes";
        _rapido = OpcionesRapidas[0];
        Avisar(nameof(Prueba), nameof(Ayuda), nameof(OpcionesRapidas), nameof(FiltroRapido), nameof(EnPendientes), nameof(EnContabilizadas), nameof(EnTodas));
        Filtrar();
        if (_activa is not null) await AbrirFichaAsync(_activa, guardarAntes: false);
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- pestaña del contenedor ----------

    public string Titulo => "Facturas recibidas";
    public string Ayuda => Prueba
        ? $"Modo prueba: vuelve a reconocer los PDF de compras de {_s.RazonSocial} y compara con lo grabado, sin guardar nada"
        : $"Facturas de compra de {_s.RazonSocial} en {_s.Trabajo.Ejercicio}: revisión de lo reconocido en los PDF";
    public string Icono => "scan";
    public bool TieneCambios => _ficha?.TieneCambios == true;
    public bool GuardadoFallido => _errorFicha is not null;
    public object Vista => _vista ??= new Vistas.VistaFacturasRecibidas { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => TieneCambios ? [$"Datos de {_ficha!.Factura.Rotulo}"] : [];
    public async Task<bool> GuardarAsync() => !TieneCambios || await GuardarFichaAsync();

    public void Descartar()
    {
        if (_activa is not null) _ = AbrirFichaAsync(_activa, guardarAntes: false);
    }

    public void AlCerrar() => _modulo.Cerrada(this);

    // ---------- estado ----------

    public EstadoVista Estado { get => _estado; private set { if (Fijar(ref _estado, value)) Avisar(nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado)); } }
    public string TituloEstado => Estado switch
    {
        EstadoVista.Vacio => _todas.Count == 0 ? "Aún no hay facturas de compra en este ejercicio" : "Ninguna factura cumple los filtros",
        EstadoVista.Error => "No he podido leer las facturas de compra",
        EstadoVista.Cargando => "Leyendo facturas de compra…",
        _ => ""
    };
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string? AccionEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 && !Prueba => "Buscar PDF nuevos",
        EstadoVista.Vacio => "Quitar filtros",
        EstadoVista.Error => "Reintentar",
        _ => null
    };
    public ICommand? ComandoEstado => Estado switch
    {
        EstadoVista.Vacio when _todas.Count == 0 && !Prueba => BuscarPdfNuevosCommand,
        EstadoVista.Vacio => LimpiarFiltrosCommand,
        EstadoVista.Error => RefrescarCommand,
        _ => null
    };

    /// <summary>Trabajo en curso (buscar PDF, reconocer, comparar): se ve en la cabecera y bloquea las demás acciones largas.</summary>
    public string TextoOcupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) { Avisar(nameof(Ocupado)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool Ocupado => _ocupado.Length > 0;

    public string? ErrorFicha { get => _errorFicha; private set => Fijar(ref _errorFicha, value); }

    // ---------- carga ----------

    /// <summary>Al abrir: lee las facturas, los proveedores y los catálogos y, fuera del modo prueba, busca los PDF nuevos.</summary>
    internal async Task IniciarAsync()
    {
        _puedeEditar = await _s.PuedeEditar();
        try
        {
            _proveedores = await _s.Facturas.ProveedoresAsync(_s.Trabajo);
            _cuentasGasto = (await _s.Cuentas.ListarAsync(_s.Trabajo, "6"))
                .Where(c => c.EstaActiva && !c.EstaBloqueada && c.Codigo.Length >= 7)
                .OrderBy(c => c.Codigo, StringComparer.Ordinal)
                .Select(c => new OpcionFicha<string>($"{c.Codigo} · {c.Nombre}", c.Codigo)).ToList();
            _centros = await _s.CentrosCoste.ListarAsync(_s.Trabajo.IdSociedad);
            if (_s.TiposIva is { } tipos) _tiposIva = await tipos();
            if (_s.FormasPago is { } formas) _formasPago = await formas();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _modulo.Avisar($"No he podido leer los proveedores o las cuentas: {ex.Message}");
        }
        await CargarAsync();
        if (!Prueba && _puedeEditar) await BuscarPdfNuevosAsync(silencioso: true);
    }

    /// <summary>F5: vuelve a leer los proveedores (su forma de pago por defecto puede haber cambiado) y la lista.</summary>
    private async Task RefrescarAsync()
    {
        try { _proveedores = await _s.Facturas.ProveedoresAsync(_s.Trabajo); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _modulo.Avisar($"No he podido leer los proveedores: {ex.Message}"); }
        await CargarAsync();
    }

    /// <summary>Vuelve a leer la lista, conservando la factura activa (sus cambios se guardan antes).</summary>
    internal async Task CargarAsync(int? idActiva = null)
    {
        if (TieneCambios && !await GuardarFichaAsync()) return;
        var id = idActiva ?? _activa?.IdRegistro;
        Estado = EstadoVista.Cargando;
        try
        {
            _datos = await _s.Facturas.ListarAsync(_s.Trabajo);
            _todas = await Task.Run(() => _datos.Select(d => new FilaCompraVM(d)).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MensajeEstado = ex.Message;
            Estado = EstadoVista.Error;
            return;
        }
        Avisar(nameof(TextoPendientes), nameof(TextoContabilizadas), nameof(TextoTodas));
        Filtrar(id);
    }

    // ---------- pestañas de la lista, filtros y búsqueda ----------

    /// <summary>pendientes, contabilizadas o todas (esta solo en modo prueba).</summary>
    public string PestanaLista
    {
        get => _pestanaLista;
        set
        {
            if (!Fijar(ref _pestanaLista, value)) return;
            _rapido = OpcionesRapidas[0];
            Avisar(nameof(OpcionesRapidas), nameof(FiltroRapido), nameof(EnPendientes), nameof(EnContabilizadas), nameof(EnTodas));
            Filtrar();
        }
    }

    public bool EnPendientes => _pestanaLista == "pendientes";
    public bool EnContabilizadas => _pestanaLista == "contabilizadas";
    public bool EnTodas => _pestanaLista == "todas";
    public string TextoPendientes => $"Pendientes ({Formatos.Recuento(_todas.Count(f => !f.Datos.Contabilizada))})";
    public string TextoContabilizadas => $"Contabilizadas ({Formatos.Recuento(_todas.Count(f => f.Datos.Contabilizada))})";
    public string TextoTodas => $"Todas ({Formatos.Recuento(_todas.Count)})";

    public IReadOnlyList<OpcionCompra> OpcionesRapidas => _pestanaLista switch
    {
        "contabilizadas" => [new("Todas", "", "Todas las contabilizadas del ejercicio"), new("Sin centro de coste", "cc", "Sin cuenta de gasto o sin centro de coste")],
        "todas" => [new("Todas", "", "Todos los PDF registrados"), new("Pendientes", "pend", "Sin contabilizar"), new("Contabilizadas", "cont", "Contabilizadas: sirven para comparar el reconocimiento con lo grabado")],
        _ =>
        [
            new("Todas", "", "Todas las facturas pendientes"), new("Sin rellenar", "vac", "Sin reconocer ni rellenar, o por revisar"),
            new("Listas", "com", "Con proveedor, número, fecha e importes que cuadran"), new("Sin centro de coste", "cc", "Sin cuenta de gasto o sin centro de coste"),
            new("PDF no encontrado", "fal", "El fichero ya no está en la carpeta")
        ]
    };

    public OpcionCompra FiltroRapido { get => _rapido; set { if (value is not null && Fijar(ref _rapido, value)) Filtrar(); } }
    public string Busqueda { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public IReadOnlyList<CriterioFiltroVM> Filtros { get; }
    public string TextoBotonFiltro => Filtros.Count(f => f.Marcado) is var n and > 0 ? $"Filtro ({n})" : "Filtro";

    public bool HayFiltros => _texto.Length > 0 || _rapido.Clave.Length > 0 || Filtros.Any(f => f.Marcado) || _relacionadasDe is not null;

    private void LimpiarFiltros()
    {
        _texto = "";
        _rapido = OpcionesRapidas[0];
        _relacionadasDe = null;
        foreach (var f in Filtros) f.Quitar();
        Avisar(nameof(Busqueda), nameof(FiltroRapido), nameof(TextoRelacionadas), nameof(HayRelacionadas));
        Filtrar();
    }

    private bool Cumple(FilaCompraVM f)
    {
        var d = f.Datos;
        if (_relacionadasDe is int rel)
        {
            var origen = _datos.FirstOrDefault(x => x.IdRegistro == rel);
            return d.IdRegistro == rel || d.IdRegistroFacturaRectificada == rel || origen?.IdRegistroFacturaRectificada == d.IdRegistro;
        }
        if (_pestanaLista == "pendientes" && d.Contabilizada) return false;
        if (_pestanaLista == "contabilizadas" && !d.Contabilizada) return false;
        if (!f.Contiene(_texto)) return false;
        var ok = _rapido.Clave switch
        {
            "vac" => f.Estado is "Sin rellenar" or "Por revisar",
            "com" => f.Estado == "Lista",
            "cc" => string.IsNullOrEmpty(d.CuentaGasto) || d.CuentaGasto == "6000000" || (_centros.Count > 0 && d.IdCentroCoste is null),
            "fal" => !f.PdfExiste,
            "pend" => !d.Contabilizada,
            "cont" => d.Contabilizada,
            _ => true
        };
        if (!ok) return false;
        var ops = Filtros.Where(x => x.EsOperacion && x.Marcado).Select(x => x.Clave).ToList();
        if (ops.Count > 0 && !ops.Contains(d.Operacion switch { OperacionCompra.Intracomunitaria => "int", OperacionCompra.Importacion => "imp", _ => "nac" })) return false;
        foreach (var x in Filtros.Where(x => !x.EsOperacion && x.Marcado))
        {
            var cumple = x.Clave switch
            {
                "ret" => d.TieneRetencion,
                "sup" => (d.Suplido ?? 0m) != 0m,
                "isp" => d.InversionSujetoPasivo,
                "rect" => d.EsRectificativa || _datos.Any(o => o.IdRegistroFacturaRectificada == d.IdRegistro),
                _ => true
            };
            if (!cumple) return false;
        }
        return true;
    }

    private void Filtrar() => Filtrar(null);

    private void Filtrar(int? id)
    {
        id ??= _activa?.IdRegistro;
        Filas.Clear();
        foreach (var f in _todas.Where(Cumple).OrderBy(f => f.Carpeta, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Fichero, StringComparer.CurrentCultureIgnoreCase))
            Filas.Add(f);
        var nueva = Filas.FirstOrDefault(f => f.IdRegistro == id) ?? Filas.FirstOrDefault();
        if (!ReferenceEquals(nueva, _activa)) Activa = nueva;
        Estado = Filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = _todas.Count == 0
            ? Prueba ? "Sal del modo prueba (clic derecho) y registra antes alguna factura." : "Pon los PDF en la carpeta de compras del ejercicio y pulsa «Buscar PDF nuevos» (clic derecho)."
            : "Cambia o quita los filtros para ver más facturas.";
        Avisar(nameof(HayFiltros), nameof(Pie), nameof(TituloEstado), nameof(AccionEstado), nameof(ComandoEstado), nameof(TextoBotonFiltro));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- relacionadas ----------

    public bool HayRelacionadas => _relacionadasDe is not null;
    public string? TextoRelacionadas => _relacionadasDe is int id && _datos.FirstOrDefault(d => d.IdRegistro == id) is { } d
        ? $"Solo la factura {(d.NumeroFactura.Length > 0 ? d.NumeroFactura : d.NombreFichero)} y sus relacionadas (la original y sus rectificativas)." : null;

    private void VerRelacionadas()
    {
        if (_activa is null) return;
        _relacionadasDe = _activa.IdRegistro;
        Avisar(nameof(TextoRelacionadas), nameof(HayRelacionadas));
        Filtrar(_activa.IdRegistro);
    }

    // ---------- filas, activa y selección ----------

    public ObservableCollection<FilaCompraVM> Filas { get; } = [];

    public FilaCompraVM? Activa
    {
        get => _activa;
        set
        {
            if (ReferenceEquals(_activa, value)) return;
            var anterior = _activa;
            _activa = value;
            Avisar(nameof(Activa), nameof(RutaPdf), nameof(HayPdf), nameof(TituloVisor));
            _ = CambiarActivaAsync(anterior, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Al cambiar de factura, la anterior se guarda sin preguntar (si no se puede, se queda en ella y se explica).</summary>
    private async Task CambiarActivaAsync(FilaCompraVM? anterior, FilaCompraVM? nueva)
    {
        if (_ficha is { TieneCambios: true } && anterior is not null)
        {
            var titulo = _ficha.Factura.Rotulo;
            if (await GuardarFichaAsync()) _modulo.Avisar($"Cambios de {titulo} guardados.");
        }
        if (nueva is null)
        {
            Ficha = null;
            return;
        }
        await AbrirFichaAsync(nueva, guardarAntes: false);
    }

    private async Task AbrirFichaAsync(FilaCompraVM fila, bool guardarAntes)
    {
        if (guardarAntes && TieneCambios && !await GuardarFichaAsync()) return;
        var carga = ++_cargaFicha;
        ErrorFicha = null;
        Comparacion = [];
        AsientoFicha = null;
        try
        {
            var factura = await _s.Facturas.CargarAsync(fila.IdRegistro);
            if (carga != _cargaFicha) return;
            if (factura is null)
            {
                Ficha = null;
                ErrorFicha = "La factura ya no existe: otro operador la ha borrado.";
                return;
            }
            if (factura.Contabilizada) _ = CargarAsientoAsync(factura, carga);
            if (!Prueba)
            {
                Ficha = new FichaCompraVM(factura, _proveedores, _cuentasGasto, _centros, _datos, soloLectura: !_puedeEditar, tiposIva: _tiposIva, formasPago: _formasPago);
                return;
            }
            // Modo prueba: se reconoce de nuevo el PDF y se compara con lo grabado; nada se guarda.
            Ficha = new FichaCompraVM(factura, _proveedores, _cuentasGasto, _centros, _datos, soloLectura: true, tiposIva: _tiposIva, formasPago: _formasPago);
            if (fila.PdfExiste) await ProbarAsync(factura, carga);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (carga == _cargaFicha) ErrorFicha = ex.Message;
        }
    }

    public FichaCompraVM? Ficha
    {
        get => _ficha;
        private set
        {
            if (_ficha is not null) _ficha.PropertyChanged -= AlCambiarFicha;
            if (!Fijar(ref _ficha, value)) return;
            if (value is not null) value.PropertyChanged += AlCambiarFicha;
            Avisar(nameof(HayFicha), nameof(TieneCambios));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayFicha => _ficha is not null;

    // ---------- asiento de la factura ----------

    private AsientoFacturaVM? _asiento;

    /// <summary>Asiento de la factura contabilizada, en la pastilla de solo lectura al pie de la ficha.</summary>
    public AsientoFacturaVM? AsientoFicha { get => _asiento; private set { if (Fijar(ref _asiento, value)) Avisar(nameof(HayAsiento)); } }
    public bool HayAsiento => _asiento is not null;

    private async Task CargarAsientoAsync(FacturaCompra factura, int carga)
    {
        try
        {
            var asiento = await _s.Facturas.AsientoAsync(_s.Trabajo, factura);
            if (carga != _cargaFicha) return;
            AsientoFicha = asiento is null
                ? new AsientoFacturaVM("Asiento", "No he encontrado el asiento de esta factura en la contabilidad.", [])
                : AsientoFacturaVM.De(asiento);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (carga == _cargaFicha) AsientoFicha = new AsientoFacturaVM("Asiento", $"No he podido leer el asiento: {ex.Message}", []);
        }
    }

    private void AlCambiarFicha(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FichaCompraVM.TieneCambios))
        {
            Avisar(nameof(TieneCambios));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Selección de la tabla (la mantiene la vista): el ámbito de las acciones del menú contextual.</summary>
    public void CambiarSeleccion(IReadOnlyList<FilaCompraVM> seleccion)
    {
        _seleccion = seleccion;
        Avisar(nameof(Pie));
        CommandManager.InvalidateRequerySuggested();
    }

    public string Pie
    {
        get
        {
            var total = Filas.Sum(f => f.TotalOrden);
            var texto = $"{Formatos.Plural(Filas.Count, "factura", "facturas")} · total {Formatos.Importe(total)}";
            if (Filas.Count != _todas.Count) texto += $" (de {Formatos.Recuento(_todas.Count)})";
            return _seleccion.Count > 1 ? $"{texto} · {_seleccion.Count} seleccionadas · {Formatos.Importe(_seleccion.Sum(f => f.TotalOrden))}" : texto;
        }
    }

    // ---------- visor ----------

    public string? RutaPdf => _activa is { PdfExiste: true } a ? a.Datos.RutaPdf : null;
    public bool HayPdf => RutaPdf is not null;
    public string TituloVisor => _activa is null ? "Visor del PDF" : _activa.PdfExiste ? _activa.Fichero : $"{_activa.Fichero} (no encontrado)";

    // ---------- guardar ----------

    /// <summary>Una contabilizada no se modifica desde aquí (hasta la entrega 3), pero «Guardar» sí enseña la plantilla con sus valores.</summary>
    private bool PuedeEnsenar => !Prueba && _puedeEditar && _ficha is { Factura.Contabilizada: true } && _activa is { PdfExiste: true };

    /// <summary>Tras un clic en un valor resaltado del visor.</summary>
    internal void AvisarElegido(string campo)
    {
        var nombre = campo switch { "nif" => "el NIF", "numero" => "el número", "fecha" => "la fecha", "total" => "el total", _ => campo };
        _modulo.Avisar($"Al guardar, la plantilla aprenderá {nombre} de ese sitio del PDF.");
    }

    private async Task GuardarOEnsenarAsync()
    {
        if (_ficha is { Editable: true })
        {
            await GuardarFichaAsync(confirmar: true);
            return;
        }
        if (!PuedeEnsenar) return;
        TextoOcupado = "Aprendiendo de la factura…";
        try
        {
            await _s.Facturas.EnsenarAsync(_ficha!.Factura, _ficha.Elegidos);
            _modulo.Avisar("He aprendido dónde están los campos de esta factura. Sus datos no cambian porque está contabilizada.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorFicha = $"No he podido aprender de la factura: {ex.Message}";
        }
        finally
        {
            TextoOcupado = "";
        }
    }

    // ---------- copiar ruta y separar facturas ----------

    private void CopiarRuta()
    {
        if (_activa is null) return;
        _modulo.Avisar(_modulo.Dialogos.Copiar(_activa.Datos.RutaPdf) ? $"Ruta copiada: {_activa.Datos.RutaPdf}" : "Windows no ha dejado copiar la ruta; vuelve a intentarlo.");
    }

    /// <summary>«Separar facturas»: el PDF trae dos facturas (pasa con algunas de Amazon).</summary>
    private async Task SepararAsync()
    {
        if (_activa is not { PdfExiste: true } fila) return;
        if (TieneCambios && !await GuardarFichaAsync()) return;
        var propuesto = Path.GetFileNameWithoutExtension(fila.Datos.RutaPdf) + "_2.pdf";
        if (_modulo.Dialogos.PedirSeparacion(fila.Fichero, propuesto) is not { } eleccion) return;
        var factura = await _s.Facturas.CargarAsync(fila.IdRegistro);
        if (factura is null) return;
        TextoOcupado = "Separando las facturas…";
        try
        {
            var r = await _s.Facturas.SepararAsync(_s.Trabajo, factura, eleccion.Pagina, eleccion.Nombre);
            if (!r.EsCorrecto)
            {
                var e = r.Error!;
                _modulo.Dialogos.Preguntar("No he podido separar las facturas", e.Mensaje, e.AccionCorrectora is { } a ? [a] : null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
                return;
            }
            var segunda = r.Valor;
            _modulo.Avisar(segunda.Registrada
                ? $"Facturas separadas: {fila.Fichero} y {Path.GetFileName(segunda.Ruta)}. El original queda como .BAK."
                : $"Facturas separadas, pero {Path.GetFileName(segunda.Ruta)} no se ha podido registrar: {segunda.Error?.Mensaje}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _modulo.Dialogos.Preguntar("No he podido separar las facturas", ex.Message, null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
        }
        finally
        {
            TextoOcupado = "";
        }
        await CargarAsync(fila.IdRegistro);
        if (_activa is not null) await AbrirFichaAsync(_activa, guardarAntes: false);
    }

    private async Task<bool> GuardarFichaAsync(bool confirmar = false)
    {
        if (_ficha is not { Editable: true } ficha || !ficha.TieneCambios && !confirmar) return true;
        var f = ficha.Construir();
        Resultado r;
        try
        {
            r = await _s.Facturas.GuardarAsync(f, ficha.Elegidos);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            r = Resultado.Fallo(new Error("Compras.Guardar", $"No he podido guardar la factura: {ex.Message}", f.Rotulo));
        }
        if (!r.EsCorrecto)
        {
            ErrorFicha = string.Join(" ", r.Errores.Select(e => e.AccionCorrectora is { } a ? $"{e.Mensaje} {a}" : e.Mensaje));
            Avisar(nameof(GuardadoFallido));
            return false;
        }
        ErrorFicha = null;
        ficha.Guardada(f);
        // La forma de pago guardada pasa a ser la del proveedor: se propone ya en sus otras facturas.
        if (f.IdProveedor is int prov && f.IdFormaPago is byte fp)
            _proveedores = _proveedores.Select(p => p.IdProveedor == prov ? p with { IdFormaPagoDefecto = fp } : p).ToList();
        ActualizarFila(f);
        _modulo.FacturaCambiada(this);
        Avisar(nameof(TieneCambios), nameof(GuardadoFallido));
        return true;
    }

    /// <summary>Refleja en la lista lo guardado sin volver a leer todo.</summary>
    private void ActualizarFila(FacturaCompra f)
    {
        var fila = _todas.FirstOrDefault(x => x.IdRegistro == f.IdRegistro);
        if (fila is null) return;
        var o = fila.Datos;
        var nueva = new FilaFacturaCompra
        {
            IdRegistro = o.IdRegistro, IdProveedor = f.IdProveedor,
            Proveedor = _proveedores.FirstOrDefault(p => p.IdProveedor == f.IdProveedor)?.Nombre ?? o.Proveedor,
            Nif = f.NifProveedor, NumeroFactura = f.NumeroFactura, Fecha = f.Fecha, BaseImponible = f.BaseImponible, CuotaIva = f.CuotaIva,
            ImporteTotal = f.ImporteTotal, ImporteRetencion = f.ImporteRetencion, TieneRetencion = (f.ImporteRetencion ?? 0m) != 0m, Suplido = f.Suplido,
            InversionSujetoPasivo = f.InversionSujetoPasivo, Operacion = f.Operacion, Divisa = f.Divisa, IdRegistroFacturaRectificada = f.IdRegistroFacturaRectificada,
            Contabilizada = o.Contabilizada, Duplicada = o.Duplicada, RutaPdf = o.RutaPdf, Carpeta = o.Carpeta, CuentaGasto = f.CuentaGasto,
            IdCentroCoste = f.IdCentroCoste, Confianza = o.Confianza, RequiereRevision = f.RequiereRevision, FechaHoraRegistro = o.FechaHoraRegistro, Asiento = o.Asiento,
            ConfianzaMinima = o.ConfianzaMinima, Version = f.Version, RutasGuardadas = o.RutasGuardadas
        };
        _datos = _datos.Select(d => d.IdRegistro == nueva.IdRegistro ? nueva : d).ToList();
        fila.Datos = nueva;
        Avisar(nameof(Pie));
    }

    // ---------- buscar PDF nuevos ----------

    /// <summary>Busca en la carpeta de compras los PDF cuyo MD5 no está registrado, los reconoce y los registra uno a uno.</summary>
    private async Task BuscarPdfNuevosAsync(bool silencioso = false)
    {
        if (Ocupado) return;
        if (TieneCambios && !await GuardarFichaAsync()) return;
        TextoOcupado = "Buscando PDF nuevos…";
        try
        {
            var n = 0;
            var errores = new List<string>();
            var progreso = new Progress<ImportacionPdf>(r =>
            {
                if (r.Registrada) n++;
                else if (r.Error is { } e) errores.Add($"{Path.GetFileName(r.Ruta)}: {e.Mensaje}");
                TextoOcupado = $"Registrando PDF nuevos… {n} registrados";
            });
            var resultado = await _s.Facturas.ImportarNuevosAsync(_s.Trabajo, progreso);
            if (!resultado.EsCorrecto)
            {
                var e = resultado.Error!;
                if (!silencioso || e.Codigo != "Compras.SinCarpetaDocumentos")
                    _modulo.Dialogos.Preguntar("No he podido buscar los PDF nuevos", e.Mensaje, e.AccionCorrectora is { } a ? [a] : null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
                return;
            }
            var registrados = resultado.Valor.Count(r => r.Registrada);
            var fallidos = resultado.Valor.Where(r => !r.Registrada && r.Error is not null).ToList();
            if (registrados > 0 || !silencioso)
                _modulo.Avisar(registrados == 0 && fallidos.Count == 0 ? "La carpeta no tiene PDF nuevos."
                    : $"{Formatos.Plural(registrados, "PDF nuevo registrado", "PDF nuevos registrados")}{(fallidos.Count > 0 ? $"; {fallidos.Count} no se han podido leer" : "")}.");
            if (fallidos.Count > 0 && !silencioso)
                _modulo.Dialogos.Preguntar("Algunos PDF no se han podido registrar", "Estos PDF siguen en la carpeta y se volverán a intentar la próxima vez.",
                    fallidos.Select(f => $"{Path.GetFileName(f.Ruta)}: {f.Error!.Mensaje}").ToList(), new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
            if (registrados > 0) await CargarAsync();
            else foreach (var f in _todas) f.ComprobarPdf();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _modulo.Dialogos.Preguntar("No he podido buscar los PDF nuevos", ex.Message, null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
        }
        finally
        {
            TextoOcupado = "";
        }
    }

    // ---------- reconocer y probar ----------

    /// <summary>
    /// Vuelve a leer el PDF de la factura activa con las reglas actuales. Fuera del modo prueba vuelca lo leído en la ficha
    /// como cambios sin guardar; en modo prueba solo compara con lo grabado.
    /// </summary>
    private async Task ReconocerDeNuevoAsync()
    {
        if (_activa is null || _ficha is null) return;
        if (Prueba)
        {
            await ProbarAsync(_ficha.Factura, _cargaFicha);
            return;
        }
        TextoOcupado = "Reconociendo el PDF…";
        try
        {
            var (_, leida) = await _s.Facturas.ProbarAsync(_s.Trabajo, _activa.Datos.RutaPdf);
            _ficha.AplicarLectura(leida);
            _modulo.Avisar("Reconocido de nuevo: revisa los campos y guarda (se guarda solo al cambiar de factura).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorFicha = $"No he podido leer el PDF: {ex.Message}";
        }
        finally
        {
            TextoOcupado = "";
        }
    }

    /// <summary>Comparación del modo prueba de la factura activa: lo grabado frente a lo leído ahora.</summary>
    public IReadOnlyList<ComparacionCampo> Comparacion { get => _comparacion; private set { if (Fijar(ref _comparacion, value)) Avisar(nameof(HayComparacion), nameof(ResumenComparacion)); } }
    public bool HayComparacion => _comparacion.Count > 0;
    public string ResumenComparacion => _comparacion.Count == 0 ? ""
        : $"{_comparacion.Count(c => c.Igual)} de {_comparacion.Count} campos iguales a lo grabado";

    private async Task ProbarAsync(FacturaCompra grabada, int carga)
    {
        TextoOcupado = "Reconociendo el PDF (no se guarda nada)…";
        try
        {
            var (reconocida, leida) = await _s.Facturas.ProbarAsync(_s.Trabajo, grabada.RutaPdf);
            if (carga != _cargaFicha) return;
            Ficha = new FichaCompraVM(leida, _proveedores, _cuentasGasto, _centros, _datos, soloLectura: true, ConfianzaCampos.DesdeLog(reconocida.Log), _tiposIva, _formasPago);
            Comparacion = ServicioFacturasCompra.Comparar(grabada, leida);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (carga == _cargaFicha) ErrorFicha = $"No he podido leer el PDF: {ex.Message}";
        }
        finally
        {
            TextoOcupado = "";
        }
    }

    /// <summary>
    /// Modo prueba: reconoce de nuevo los PDF de las facturas de la lista y cuenta, campo a campo, cuántos coinciden con lo
    /// grabado. No guarda nada.
    /// </summary>
    private async Task CompararTodasAsync()
    {
        var filas = Filas.Where(f => f.PdfExiste).ToList();
        if (filas.Count == 0) return;
        var iguales = new Dictionary<string, (int Iguales, int Total)>();
        var errores = 0;
        for (var i = 0; i < filas.Count; i++)
        {
            TextoOcupado = $"Comparando {i + 1} de {filas.Count} (no se guarda nada)…";
            try
            {
                var grabada = await _s.Facturas.CargarAsync(filas[i].IdRegistro);
                if (grabada is null) continue;
                var (_, leida) = await _s.Facturas.ProbarAsync(_s.Trabajo, grabada.RutaPdf);
                foreach (var c in ServicioFacturasCompra.Comparar(grabada, leida).Where(c => !c.SinGrabar))
                {
                    var (ig, tot) = iguales.GetValueOrDefault(c.Campo);
                    iguales[c.Campo] = (ig + (c.Igual ? 1 : 0), tot + 1);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errores++;
            }
        }
        TextoOcupado = "";
        var detalles = iguales.Select(kv => $"{kv.Key}: {kv.Value.Iguales} de {kv.Value.Total} iguales ({(kv.Value.Total == 0 ? 0 : Math.Round(100m * kv.Value.Iguales / kv.Value.Total, 1))} %)").ToList();
        if (errores > 0) detalles.Add($"{errores} PDF no se han podido leer.");
        _modulo.Dialogos.Preguntar("Comparación con lo grabado",
            $"He reconocido de nuevo {Formatos.Plural(filas.Count, "PDF", "PDF")} de la lista y comparado cada campo con lo grabado. No se ha guardado nada.",
            detalles, new OpcionMensaje("Entendido", "Cierra el resumen", Principal: true));
    }

    // ---------- comandos ----------

    public ICommand BuscarPdfNuevosCommand { get; }
    public ICommand ReconocerCommand { get; }
    public ICommand CompararTodasCommand { get; }
    public ICommand GuardarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand AbrirPdfCommand { get; }
    public ICommand AbrirCarpetaCommand { get; }
    public ICommand CopiarRutaCommand { get; }
    public ICommand SepararCommand { get; }
    public ICommand VerRelacionadasCommand { get; }
    public ICommand QuitarRelacionadasCommand { get; }
    public ICommand AbrirProveedorCommand { get; }
    public ICommand AnadirTipoCommand { get; }
    public ICommand QuitarTipoCommand { get; }
    public ICommand RefrescarCommand { get; }
    public ICommand LimpiarFiltrosCommand { get; }
    public ICommand VerPendientesCommand { get; }
    public ICommand VerContabilizadasCommand { get; }
    public ICommand VerTodasCommand { get; }
}
