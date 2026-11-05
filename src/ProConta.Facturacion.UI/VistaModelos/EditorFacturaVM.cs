using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Programadas;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.FormasPago;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;
using ProConta.Terceros.Roles;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Facturacion.UI.VistaModelos;

/// <summary>Incidencia del resumen de errores, con su acción correctora si la hay.</summary>
public sealed record IncidenciaVM(string Mensaje, bool EsAviso, string? TextoAccion, ICommand? Accion)
{
    public bool TieneAccion => Accion is not null;
}

/// <summary>Apunte de la vista previa del asiento.</summary>
public sealed record ApunteVistaPrevia(string Cuenta, string Concepto, string Debe, string Haber, bool FaltaCuenta);

/// <summary>Base y cuota de un tipo de IVA.</summary>
public sealed record DesgloseVM(string Tipo, string Base, string Cuota);

/// <summary>
/// Editor de una factura de venta (facturacion/pantallas, prototipo sources/maquetas/FacturaBorrador): un borrador se
/// edita y se guarda sin número; validarlo asigna el número, congela el cliente y genera el asiento. Una factura validada
/// se ve en solo lectura y solo se cambian sus notas internas. Con la casilla «Periódica» (facturacion/programadas) el
/// mismo editor edita una factura programada: la factura es su plantilla y la pestaña «Programación», su cadencia.
/// </summary>
public sealed class EditorFacturaVM : ModeloVista, IPestanaTrabajo, INotifyDataErrorInfo
{
    private readonly ModuloFacturacion _modulo;
    private readonly ServiciosFacturacion _s;
    private readonly Dictionary<string, string> _errores = new(StringComparer.Ordinal);
    private FacturaVenta _f = new();
    private string _firmaGuardada = "";
    private object? _vista;
    private bool _puedeEditar;
    private bool _puedeValidar;
    private bool _puedeRectificar;
    private bool _cargando = true;
    private bool _guardadoFallido;
    private string? _errorCarga;
    private ClienteElegible? _cliente;
    private CuentaRol? _cuentaCliente;
    private CuentaRol? _cuentaRetencion;
    private ConfiguracionEjercicio? _config;
    private IReadOnlyList<decimal> _tiposIva = [21m, 10m, 4m, 0m];
    private IReadOnlyList<OpcionCentroCoste> _centros = [SinCentro];
    private static readonly OpcionCentroCoste SinCentro = new(null, "Sin centro");
    private string _fechaFactura = "", _fechaOperacion = "", _periodoDesde = "", _periodoHasta = "", _diaPago = "", _retencion = "";
    private FacturaProgramada? _p;
    private string _inicio = "", _diaEmision = "", _fechaFin = "", _repeticiones = "";
    private int _tipoFinal;

    internal EditorFacturaVM(ModuloFacturacion modulo)
    {
        _modulo = modulo;
        _s = modulo.Servicios;
        GuardarCommand = new ComandoDelegado(() => _ = GuardarAsync(), () => PuedeGuardar);
        DescartarCommand = new ComandoDelegado(Descartar, () => TieneCambios);
        ValidarCommand = new ComandoDelegado(() => _ = ValidarAsync(), () => EsBorrador && !EsProgramada && _puedeValidar && !_cargando);
        DuplicarCommand = new ComandoDelegado(() => _ = DuplicarAsync(), () => Guardada && _puedeEditar && !_cargando);
        EliminarCommand = new ComandoDelegado(() => _ = EliminarAsync(), () => EsBorrador && _puedeEditar && !_cargando);
        RectificarCommand = new ComandoDelegado(() => _ = RectificarAsync(), () => PuedeRectificar);
        BuscarClienteCommand = new ComandoDelegado(() => _ = BuscarClienteAsync(), () => EditableCliente);
        AsignarCuentaClienteCommand = new ComandoDelegado(() => _ = AsignarCuentaAsync(ClaseCuentaRol.Cliente), () => _cliente is not null && _puedeEditar && EsBorrador);
        AsignarCuentaRetencionCommand = new ComandoDelegado(() => _ = AsignarCuentaAsync(ClaseCuentaRol.Retencion), () => _cliente is not null && _puedeEditar && EsBorrador);
        AbrirFichaCommand = new ComandoDelegado(() => { if (_cliente is not null) _modulo.AbrirFichaTercero(_cliente.IdTercero); }, () => _cliente is not null);
        AnadirLineaCommand = new ComandoDelegado(() => _ = AnadirLineaAsync(), () => EditableLineas);
        QuitarLineaCommand = new ComandoDelegado(p => { if (p is LineaFacturaVM l) QuitarLinea(l); }, _ => EditableLineas);
        SubirLineaCommand = new ComandoDelegado(p => { if (p is LineaFacturaVM l) MoverLinea(l, -1); }, p => EditableLineas && p is LineaFacturaVM l && Lineas.IndexOf(l) > 0);
        BajarLineaCommand = new ComandoDelegado(p => { if (p is LineaFacturaVM l) MoverLinea(l, 1); }, p => EditableLineas && p is LineaFacturaVM l && Lineas.IndexOf(l) is var i && i >= 0 && i < Lineas.Count - 1);
        VolverCommand = new ComandoDelegado(() => { if (_p is null) _modulo.VolverAlListado(); else _modulo.VolverAProgramadas(); });
    }

    public int IdFactura => _p is null ? _f.IdFactura : 0;
    public int IdProgramada => _p?.IdFacturaProgramada ?? 0;

    /// <summary>Ya está guardada (la factura o la programada).</summary>
    private bool Guardada => _p is null ? !_f.EsNueva : !_p.EsNueva;

    // ---------- pestaña ----------

    public string Titulo => _cargando && _f.EsNueva && _errorCarga is null ? "Factura…" : _p is not null ? (_p.EsNueva ? "Programada nueva" : $"Programada {_p.Rotulo}") : _f.Rotulo;
    public string Ayuda => (_p is not null ? $"Factura programada: plantilla y programación ({_p.TextoEstado.ToLowerInvariant()})"
                              : EsBorrador ? "Borrador de factura de venta" : $"Factura {_f.Rotulo} ({TextosEstado.Texto(_f.Estado).ToLowerInvariant()})") +
                           (TieneCambios ? ". Cambios sin guardar" : "");
    public string Icono => "doc";
    public bool TieneCambios => !_cargando && Firma() != _firmaGuardada;
    public bool GuardadoFallido { get => _guardadoFallido; private set => Fijar(ref _guardadoFallido, value); }
    public object Vista => _vista ??= new Vistas.VistaEditorFactura { DataContext = this };

    public IReadOnlyList<string> DescribirCambios() => !TieneCambios ? []
        : _p is not null ? [$"{Titulo}: cambios en la programada"]
        : [$"{_f.Rotulo}: {(EsBorrador ? "cambios en el borrador" : "notas internas")}"];

    Task<bool> IPestanaTrabajo.GuardarAsync() => GuardarAsync();

    public void AlCerrar() => _modulo.EditorCerrado(this);

    // ---------- estado ----------

    public bool Cargando { get => _cargando; private set { if (Fijar(ref _cargando, value)) TodoCambiado(); } }
    public string? ErrorCarga { get => _errorCarga; private set => Fijar(ref _errorCarga, value); }
    public bool EsBorrador => _f.EsBorrador;
    public bool EsRectificativa => _f.EsRectificativa;
    public bool EsValidada => !_f.EsBorrador;
    public bool Editable => EsBorrador && _puedeEditar;
    public bool SoloLectura => !Editable;
    public bool EditableCliente => Editable && !_f.EsRectificativa;
    public bool EditableLineas => Editable && !_f.EsRectificativa;
    public bool EditableNotas => _puedeEditar;
    public bool NotasSoloLectura => !EditableNotas;
    public bool PuedeGuardar => TieneCambios && _puedeEditar;
    public bool PuedeRectificar => _puedeRectificar && _f.Estado == EstadoFactura.Validada && !_f.EsRectificativa && !_cargando;
    public string TextoGuardar => EsBorrador ? "Guardar" : "Guardar notas";
    public string AyudaGuardar => _p is not null ? "Guarda la programada; los cambios solo afectan a las facturas que se generen después (Ctrl+G)"
        : EsBorrador ? "Guarda el borrador sin numerarlo (Ctrl+G)" : "Guarda las notas internas (Ctrl+G)";
    public string TextoEliminar => _p is not null ? "Eliminar programada" : EsRectificativa ? "Eliminar rectificativa" : "Eliminar borrador";
    public string AyudaEliminar => _p is not null ? "Borra la programada si aún no ha generado ninguna factura; si ya tiene, páusala" : "Borra el borrador; no consume número";
    public string AyudaDuplicar => _p is not null ? "Crea otra programada igual, pausada y sin facturas generadas" : "Crea otro borrador con el mismo cliente y líneas, sin número ni fechas";

    public string TextoVolver => _p is null ? "Volver a Facturas" : "Volver a Facturas programadas";

    public string TextoEstado => _p?.TextoEstado ?? TextosEstado.Texto(_f.Estado);
    public string Subtitulo => $"{(_cliente?.Nombre ?? NombreClienteFactura ?? "Sin cliente")} · " +
        (_p is not null ? (_p.ProximaCalculada is { } pr ? $"próxima emisión {Formatos.Fecha(pr)}" : "sin emisiones pendientes")
            : _f.FechaFactura is { } d ? Formatos.Fecha(d) : "Fecha al validar");
    private string? NombreClienteFactura => _f.Cliente.EstaVacia ? null : _f.Cliente.NombreVisible;
    public string? TextoRectifica => _f.EsRectificativa && _f.NumeroFacturaRectificada is { } n ? $"Rectifica a {n}" : null;
    public bool SinVeriFactu => _f.SinVeriFactu && !_f.EsBorrador;

    /// <summary>Banda de solo lectura: factura validada o perfil de consulta.</summary>
    public string? MotivoSoloLectura =>
        _cargando ? null
        : !EsBorrador ? "Una factura validada no se modifica: su número, cliente, importes y líneas quedan fijos. Solo puedes cambiar sus notas internas. Para corregirla, crea una rectificativa."
        : !_puedeEditar ? "Tu perfil es de consulta: puedes ver el borrador pero no cambiarlo."
        : null;

    public string? AvisoRectificativa => _f.EsRectificativa && EsBorrador
        ? "Las cantidades van en negativo: la rectificativa anula por completo a la original. El cliente y las líneas son los de la original y no se cambian; puedes ajustar la descripción, las fechas y las notas."
        : null;

    public string TextoCabeceraCliente => _p is not null ? "cada factura generada copia sus datos al validarse" : EsBorrador ? "los datos se copian a la factura al validarla" : "datos copiados al validar; los cambios posteriores en Terceros no la alteran";

    // ---------- cliente ----------

    public bool HayCliente => _cliente is not null || !_f.Cliente.EstaVacia;
    public string NombreCliente => EsBorrador ? _cliente?.Nombre ?? "" : NombreClienteFactura ?? _cliente?.Nombre ?? "";
    public string NifCliente => EsBorrador ? _cliente?.Nif ?? "Sin NIF" : _f.Cliente.Nif ?? "";
    public string DomicilioCliente => EsBorrador ? _cliente?.Domicilio ?? "" : _f.Cliente.DomicilioEnUnaLinea;
    public string TextoBuscarCliente => _cliente is null ? "Buscar cliente… (F3)" : "Cambiar cliente…";
    public string CuentaCliente => _cuentaCliente is null ? $"Sin cuenta en {_s.Trabajo.IdSociedad}" : Rotulo(_cuentaCliente);
    public bool FaltaCuentaCliente => _cliente is not null && _cuentaCliente is not { ExisteEnEjercicio: true };
    public string CuentaRetencion => _cuentaRetencion is null ? (_f.TipoRetencion > 0 ? "Sin cuenta (hace falta: hay retención)" : "Sin cuenta") : Rotulo(_cuentaRetencion);
    public bool FaltaCuentaRetencion => _cliente is not null && _f.TipoRetencion > 0 && _cuentaRetencion is not { ExisteEnEjercicio: true };
    /// <summary>Id del cliente y su cuenta contable en la sociedad, en corto para la cabecera compacta del cliente.</summary>
    public string CodigoCliente => _f.IdCliente is int id ? $"Cliente {id}" : "";
    public string CuentaClienteCorta => _cuentaCliente is { ExisteEnEjercicio: true } c ? c.EnEjercicio!.Codigo : "Sin cuenta";
    public string? NumeroAsiento => _p is not null ? "vista previa: cada factura generada tendrá el suyo al validarse"
        : _f.IdAsientoContable is int a ? $"generado al validar · id {a}" : EsBorrador ? "se generará al validar" : "sin asiento: contabilízala desde Facturas";

    private string Rotulo(CuentaRol r) => r.ExisteEnEjercicio
        ? $"{r.EnEjercicio!.Codigo} · {r.EnEjercicio.Nombre}"
        : $"{r.Codigo} (aún no existe en {_s.Trabajo.Ejercicio})";

    // ---------- cabecera ----------

    public ObservableCollection<Serie> Series { get; } = [];
    public ObservableCollection<TipoFacturaAeat> TiposFactura { get; } = [];
    public ObservableCollection<FormaPago> FormasPago { get; } = [];

    public Serie? Serie
    {
        get => Series.FirstOrDefault(s => s.IdSerie == _f.IdSerie);
        set { if (value is not null && value.IdSerie != _f.IdSerie) { _f.IdSerie = value.IdSerie; Cambio(); } }
    }

    public bool PuedeCambiarSerie => Editable && !_f.EsRectificativa;

    public TipoFacturaAeat? TipoFactura
    {
        get => TiposFactura.FirstOrDefault(t => t.Codigo == _f.TipoFactura);
        set { if (value is not null && value.Codigo != _f.TipoFactura) { _f.TipoFactura = value.Codigo; Cambio(); } }
    }

    public string Numero => _p is not null ? "Uno por factura" : _f.NumeroFactura ?? "Se asigna al validar";

    public FormaPago? FormaPago
    {
        get => FormasPago.FirstOrDefault(f => f.Id == _f.FormaDePago);
        set { if (value?.Id != _f.FormaDePago) { _f.FormaDePago = value?.Id; Cambio(); } }
    }

    public string FechaFactura { get => _fechaFactura; set => FijarFecha(ref _fechaFactura, value, nameof(FechaFactura), d => _f.FechaFactura = d); }
    public string FechaOperacion { get => _fechaOperacion; set => FijarFecha(ref _fechaOperacion, value, nameof(FechaOperacion), d => _f.FechaOperacion = d); }
    public string PeriodoDesde { get => _periodoDesde; set => FijarFecha(ref _periodoDesde, value, nameof(PeriodoDesde), d => _f.FechaInicioValidez = d); }
    public string PeriodoHasta { get => _periodoHasta; set => FijarFecha(ref _periodoHasta, value, nameof(PeriodoHasta), d => _f.FechaFinValidez = d); }

    public string DiaPago
    {
        get => _diaPago;
        set
        {
            _diaPago = value ?? "";
            if (_diaPago.Trim().Length == 0) { _f.DiaDePago = null; QuitarError(nameof(DiaPago)); }
            else if (byte.TryParse(_diaPago.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d <= 31) { _f.DiaDePago = d; QuitarError(nameof(DiaPago)); }
            else PonerError(nameof(DiaPago), "El día de pago es un número del 1 al 31 (0 o vacío si no hay día fijo).");
            Avisar();
            Cambio();
        }
    }

    public string Retencion
    {
        get => _retencion;
        set
        {
            _retencion = value ?? "";
            if (_retencion.Trim().Length == 0) { _f.TipoRetencion = 0m; QuitarError(nameof(Retencion)); }
            else if (Texto.LeerNumero(_retencion, out var r) && r is >= 0m and <= 100m) { _f.TipoRetencion = r; QuitarError(nameof(Retencion)); }
            else PonerError(nameof(Retencion), "La retención es un porcentaje entre 0 y 100.");
            Avisar();
            Cambio();
            // La retención cambia el total a cobrar y el asiento, no solo su rótulo.
            Avisar(nameof(CuentaRetencion), nameof(FaltaCuentaRetencion));
            AvisarTotales();
        }
    }

    public string Descripcion
    {
        get => _f.Descripcion;
        set { _f.Descripcion = value ?? ""; Avisar(); Cambio(); }
    }

    /// <summary>Motivo de la rectificación (solo en rectificativas): se pide al crearla y se puede ajustar mientras es borrador.</summary>
    public string Motivo
    {
        get => _f.MotivoRectificacion ?? "";
        set { _f.MotivoRectificacion = string.IsNullOrWhiteSpace(value) ? null : value; QuitarError(nameof(Motivo)); Avisar(); Cambio(); }
    }

    public string Notas
    {
        get => _f.NotasInternas ?? "";
        set { _f.NotasInternas = string.IsNullOrEmpty(value) ? null : value; Avisar(); Cambio(); }
    }

    private void FijarFecha(ref string campo, string? valor, string propiedad, Action<DateTime?> fijar)
    {
        campo = valor ?? "";
        if (Texto.LeerFecha(campo, out var d))
        {
            fijar(d);
            QuitarError(propiedad);
            if (d is not null) campo = Texto.Fecha(d);
        }
        else PonerError(propiedad, $"«{valor}» no es una fecha. Escríbela como dd/mm/aaaa.");
        Avisar(propiedad);
        Avisar(nameof(Subtitulo));
        Cambio();
    }

    // ---------- programada ----------

    /// <summary>El editor tiene una programada (casilla «Periódica» marcada).</summary>
    public bool EsProgramada => _p is not null;
    public bool EsFactura => _p is null;
    public bool MostrarValidar => EsBorrador && _p is null;

    /// <summary>La casilla se ve en una factura nueva y en las programadas (no en una factura ya guardada ni en una rectificativa).</summary>
    public bool MostrarPeriodica => !_cargando && !_f.EsRectificativa && (_p is not null || _f.EsNueva);

    /// <summary>Solo se cambia antes de guardar: una programada guardada no vuelve a ser factura, ni al revés.</summary>
    public bool PuedeCambiarPeriodica => Editable && (_p is null ? _f.EsNueva : _p.EsNueva);

    public string AyudaPeriodica => PuedeCambiarPeriodica
        ? "Convierte esta factura en una factura programada: una plantilla que genera una factura en cada vencimiento (pestaña «Programación»)"
        : "Es una factura programada: genera una factura en cada vencimiento. Para programar una factura ya guardada, usa «Programar…» en Facturas.";

    public bool Periodica
    {
        get => _p is not null;
        set
        {
            if (value == (_p is not null) || !PuedeCambiarPeriodica) { Avisar(); return; }
            if (value)
            {
                _p = FacturaProgramada.DesdePlantilla(_f, DateTime.Today);
                _periodoDesde = Texto.Fecha(_f.FechaInicioValidez);
                _periodoHasta = Texto.Fecha(_f.FechaFinValidez);
                CargarCamposProgramacion();
            }
            else _p = null;
            foreach (var e in new[] { nameof(FechaInicioProgramacion), nameof(DiaEmision), nameof(FechaFinProgramacion), nameof(Repeticiones) }) QuitarError(e);
            TodoCambiado();
            Cambio();
        }
    }

    public string EtiquetaPeriodoDesde => _p is not null ? "Primer periodo desde" : "Periodo desde";
    public string AyudaPeriodoDesde => _p is not null ? "Primer día del periodo de la primera factura; en cada factura avanza según la periodicidad" : "Primer día del periodo facturado";
    public string AyudaPeriodoHasta => _p is not null ? "Último día del periodo de la primera factura (avanza igual; un 31 sigue siendo el último día del mes)" : "Último día del periodo facturado; no puede ser anterior al primero";

    public IReadOnlyList<Periodicidad> Periodicidades => Periodicidad.Todas;

    public Periodicidad? PeriodicidadElegida
    {
        get => _p?.Periodicidad;
        set
        {
            if (_p is null || value is null || value == _p.Periodicidad) return;
            _p.Periodicidad = value;
            AvisarProgramacion();
            Cambio();
        }
    }

    public string FechaInicioProgramacion
    {
        get => _inicio;
        set => FijarFecha(ref _inicio, value, nameof(FechaInicioProgramacion), d => { if (_p is not null) _p.FechaInicio = d; AvisarProgramacion(); });
    }

    public string DiaEmision
    {
        get => _diaEmision;
        set
        {
            _diaEmision = value ?? "";
            if (_p is null) return;
            if (_diaEmision.Trim().Length == 0) { _p.DiaEmision = null; QuitarError(nameof(DiaEmision)); }
            else if (byte.TryParse(_diaEmision.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d is >= 1 and <= 31) { _p.DiaEmision = d; QuitarError(nameof(DiaEmision)); }
            else PonerError(nameof(DiaEmision), "El día de emisión es un número del 1 al 31; del 29 al 31 cuentan desde el final del mes (31 = último día).");
            Avisar();
            AvisarProgramacion();
            Cambio();
        }
    }

    public IReadOnlyList<string> OpcionesFinal { get; } = ["Sin final", "En una fecha", "Tras un número de facturas"];

    /// <summary>0 sin final, 1 en una fecha (FechaFin), 2 tras un número de facturas (NumeroRepeticiones).</summary>
    public int TipoFinal
    {
        get => _tipoFinal;
        set
        {
            if (_p is null || value == _tipoFinal || value is < 0 or > 2) return;
            _tipoFinal = value;
            if (value != 1) { _p.FechaFin = null; _fechaFin = ""; QuitarError(nameof(FechaFinProgramacion)); }
            if (value != 2) { _p.NumeroRepeticiones = null; _repeticiones = ""; QuitarError(nameof(Repeticiones)); }
            Avisar(nameof(TipoFinal), nameof(FinPorFecha), nameof(FinPorRepeticiones), nameof(FechaFinProgramacion), nameof(Repeticiones));
            AvisarProgramacion();
            Cambio();
        }
    }

    public bool FinPorFecha => _tipoFinal == 1;
    public bool FinPorRepeticiones => _tipoFinal == 2;

    public string FechaFinProgramacion
    {
        get => _fechaFin;
        set => FijarFecha(ref _fechaFin, value, nameof(FechaFinProgramacion), d => { if (_p is not null) _p.FechaFin = d; AvisarProgramacion(); });
    }

    public string Repeticiones
    {
        get => _repeticiones;
        set
        {
            _repeticiones = value ?? "";
            if (_p is null) return;
            if (_repeticiones.Trim().Length == 0) { _p.NumeroRepeticiones = null; QuitarError(nameof(Repeticiones)); }
            else if (int.TryParse(_repeticiones.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1) { _p.NumeroRepeticiones = n; QuitarError(nameof(Repeticiones)); }
            else PonerError(nameof(Repeticiones), "Escribe cuántas facturas genera en total (1 o más).");
            Avisar();
            AvisarProgramacion();
            Cambio();
        }
    }

    public bool AlGenerarBorrador
    {
        get => _p?.AlGenerar != AlGenerar.Validar;
        set { if (_p is null || !value || _p.AlGenerar == AlGenerar.Borrador) return; _p.AlGenerar = AlGenerar.Borrador; Avisar(nameof(AlGenerarBorrador), nameof(AlGenerarValidar)); Cambio(); }
    }

    public bool AlGenerarValidar
    {
        get => _p?.AlGenerar == AlGenerar.Validar;
        set { if (_p is null || !value || _p.AlGenerar == AlGenerar.Validar) return; _p.AlGenerar = AlGenerar.Validar; Avisar(nameof(AlGenerarBorrador), nameof(AlGenerarValidar)); Cambio(); }
    }

    public bool EnviarPorEmail
    {
        get => _p?.EnviarPorEmail == true;
        set { if (_p is null || value == _p.EnviarPorEmail) return; _p.EnviarPorEmail = value; Avisar(); Cambio(); }
    }

    public bool Activa
    {
        get => _p?.Activa == true;
        set { if (_p is null || value == _p.Activa) return; _p.Activa = value; Avisar(); AvisarProgramacion(); Cambio(); }
    }

    public string TextoProximas
    {
        get
        {
            if (_p is null) return "";
            if (_p.FechaInicio is null) return "Escribe la fecha de inicio para ver las próximas emisiones.";
            var proximas = _p.ProximasEmisiones(4);
            if (proximas.Count == 0) return "No le quedan facturas por emitir: está finalizada.";
            return (proximas.Count == 1 ? "Próxima emisión: " : "Próximas emisiones: ") + string.Join(" · ", proximas.Select(d => Formatos.Fecha(d)));
        }
    }

    public string TextoPeriodoProximo
    {
        get
        {
            if (_p is null || _p.ProximaCalculada is null) return "";
            var (d, h) = _p.Periodo(_p.FacturasGeneradas);
            return d is null && h is null ? "Las facturas no llevarán periodo: escribe el de la primera en «Datos de la factura»."
                : $"La próxima factura facturará el periodo {(d is { } x ? Formatos.Fecha(x) : "…")} – {(h is { } y ? Formatos.Fecha(y) : "…")}.";
        }
    }

    public string TextoGeneradas => _p is null ? ""
        : _p.FacturasGeneradas == 0 ? "Aún no ha generado ninguna factura."
        : $"Ha generado {Formatos.Plural(_p.FacturasGeneradas, "factura", "facturas")}" + (_p.FechaUltimaGeneracion is { } u ? $"; la última, el {Formatos.Fecha(u)}." : ".");

    public string MotivoEnvio => "El envío por correo llegará con su propio desarrollo: de momento se guarda la preferencia y no se envía nada.";

    private void CargarCamposProgramacion()
    {
        if (_p is null) return;
        _inicio = Texto.Fecha(_p.FechaInicio);
        _diaEmision = _p.DiaEmision?.ToString(CultureInfo.InvariantCulture) ?? "";
        _fechaFin = Texto.Fecha(_p.FechaFin);
        _repeticiones = _p.NumeroRepeticiones?.ToString(CultureInfo.InvariantCulture) ?? "";
        _tipoFinal = _p.NumeroRepeticiones is not null ? 2 : _p.FechaFin is not null ? 1 : 0;
    }

    private void AvisarProgramacion() =>
        Avisar(nameof(TextoProximas), nameof(TextoPeriodoProximo), nameof(TextoEstado), nameof(Subtitulo), nameof(TextoGeneradas), nameof(Titulo), nameof(Ayuda));

    // ---------- líneas, totales y asiento ----------

    public ObservableCollection<LineaFacturaVM> Lineas { get; } = [];
    public string TextoLineas => Formatos.Plural(Lineas.Count, "línea", "líneas") + (Editable && !EsRectificativa ? " · los importes se recalculan al escribir" : "");
    public bool SinLineas => Lineas.Count == 0;

    private TotalesFactura T => _f.Totales;
    public string TotalBase => Formatos.Importe(T.BaseImponible.Valor);
    public string TotalIva => Formatos.Importe(T.Iva.Valor);
    public string TotalSuplidos => Formatos.Importe(T.Suplidos.Valor);
    public string TotalBruto => Formatos.Importe(T.ImporteBruto.Valor);
    public string TextoRetencion => $"Retención {Texto.Porcentaje(_f.TipoRetencion)} %";
    public string TotalRetencion => Formatos.Importe(-T.Retencion.Valor);
    public string TotalACobrar => Formatos.Importe(T.ImporteTotal.Valor);
    public bool HaySuplidos => !T.Suplidos.EsCero;
    public bool HayRetencion => !T.Retencion.EsCero || _f.TipoRetencion != 0m;
    public bool HayVariosIva => CalculoFactura.Desglose(_f.Lineas).Count > 1;
    public IReadOnlyList<DesgloseVM> Desglose =>
        CalculoFactura.Desglose(_f.Lineas).Select(d => new DesgloseVM($"{Texto.Porcentaje(d.TipoIva)} %", Formatos.Numero(d.BaseImponible.Valor), Formatos.Numero(d.CuotaIva.Valor))).ToList();

    /// <summary>
    /// Vista previa del asiento con el mismo constructor que la contabilización: las cuentas que faltan se ven como «¿430?»,
    /// «¿473?» o «¿IVA?» en rojo.
    /// </summary>
    public IReadOnlyList<ApunteVistaPrevia> Asiento
    {
        get
        {
            if (!HayCliente || _f.Lineas.Count == 0) return [];
            var iva = new Dictionary<decimal, string>();
            foreach (var d in CalculoFactura.Desglose(_f.Lineas)) iva[d.TipoIva] = _config?.CuentaIva(d.TipoIva) ?? $"¿IVA {Texto.Porcentaje(d.TipoIva)} %?";
            var cuentas = new CuentasVenta(
                _cuentaCliente?.EnEjercicio?.Codigo ?? "¿430?",
                _cuentaRetencion?.EnEjercicio?.Codigo ?? "¿473?",
                iva,
                string.IsNullOrWhiteSpace(_config?.CuentaSuplido) ? "¿suplidos?" : _config!.CuentaSuplido);
            var copia = _f.Clonar();
            foreach (var l in copia.Lineas.Where(l => !l.EsSuplido && string.IsNullOrWhiteSpace(l.CuentaContable))) l.CuentaContable = "¿ingreso?";
            var asiento = AsientoVenta.Construir(copia, _s.Trabajo, cuentas, DateTime.Today);
            return asiento.Apuntes.Select(a => new ApunteVistaPrevia(a.CodigoCuenta, a.Descripcion ?? "",
                a.Debe.EsCero ? "" : Formatos.Numero(a.Debe.Valor), a.Haber.EsCero ? "" : Formatos.Numero(a.Haber.Valor), a.CodigoCuenta.StartsWith('¿'))).ToList();
        }
    }

    public string TextoAsientoVacio => "Elige el cliente y añade líneas para ver el asiento.";
    public IReadOnlyList<string> Historial => (_p?.LineasHistorial ?? _f.LineasHistorial).Reverse().ToList();

    // ---------- resumen de errores ----------

    public ObservableCollection<IncidenciaVM> Incidencias { get; } = [];
    public bool HayIncidencias => Incidencias.Count > 0;
    public string TituloIncidencias
    {
        get
        {
            var errores = Incidencias.Count(i => !i.EsAviso);
            var accion = _p is null ? "validar" : _p.Activa ? "guardar la programada activa (o desmarca «Activa» para guardarla pausada)" : "guardar la programada";
            return errores == 0 ? "Aviso" : errores == 1 ? $"No puedo {accion}: hay un dato que corregir" : $"No puedo {accion}: hay {errores} datos que corregir";
        }
    }
    public bool IncidenciasSonAvisos => Incidencias.Count > 0 && Incidencias.All(i => i.EsAviso);

    // ---------- comandos ----------

    public ICommand GuardarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand ValidarCommand { get; }
    public ICommand DuplicarCommand { get; }
    public ICommand EliminarCommand { get; }
    public ICommand RectificarCommand { get; }
    public ICommand BuscarClienteCommand { get; }
    public ICommand AsignarCuentaClienteCommand { get; }
    public ICommand AsignarCuentaRetencionCommand { get; }
    public ICommand AbrirFichaCommand { get; }
    public ICommand AnadirLineaCommand { get; }
    public ICommand QuitarLineaCommand { get; }
    /// <summary>Sube la línea un puesto (Alt+↑); el orden de las líneas tiene significado y se guarda.</summary>
    public ICommand SubirLineaCommand { get; }
    /// <summary>Baja la línea un puesto (Alt+↓).</summary>
    public ICommand BajarLineaCommand { get; }
    public ICommand VolverCommand { get; }

    public string MotivoPdf => "Llega con el desarrollo del PDF.";
    public string MotivoVeriFactu => "Llega con el desarrollo de VeriFactu: hoy las facturas se validan sin enviarse a la AEAT (solo BD de pruebas).";

    // ---------- carga ----------

    public async Task NuevaAsync()
    {
        _p = null;
        await PrepararAsync();
        var r = await _s.Facturas.NuevoBorradorAsync(_s.Trabajo);
        if (!r.EsCorrecto) { FalloCarga(r.Errores); return; }
        await MostrarAsync(r.Valor);
    }

    /// <summary>Programada nueva sin guardar; con <paramref name="desdeFactura"/>, con el contenido de esa factura («Programar…»).</summary>
    public async Task NuevaProgramadaAsync(int? desdeFactura = null)
    {
        await PrepararAsync();
        var r = desdeFactura is int id ? await _s.Programadas.DesdeFacturaAsync(id, _s.Trabajo) : await _s.Programadas.NuevaAsync(_s.Trabajo);
        if (!r.EsCorrecto) { FalloCarga(r.Errores); return; }
        await MostrarProgramadaAsync(r.Valor);
    }

    public async Task CargarProgramadaAsync(int idProgramada)
    {
        await PrepararAsync();
        var p = await _s.Programadas.ObtenerAsync(idProgramada);
        if (p is null) { ErrorCarga = "La programada ya no existe: otro operador la ha borrado. Cierra esta pestaña y actualiza el listado."; Cargando = false; return; }
        await MostrarProgramadaAsync(p);
    }

    private async Task MostrarProgramadaAsync(FacturaProgramada p)
    {
        _p = p.Clonar();
        CargarCamposProgramacion();
        await MostrarAsync(_p.Plantilla);
    }

    public async Task CargarAsync(int idFactura)
    {
        _p = null;
        await PrepararAsync();
        var f = await _s.Facturas.ObtenerAsync(idFactura);
        if (f is null) { ErrorCarga = "La factura ya no existe: otro operador la ha borrado. Cierra esta pestaña y actualiza el listado."; Cargando = false; return; }
        await MostrarAsync(f);
    }

    private async Task PrepararAsync()
    {
        try
        {
            _puedeEditar = await _s.Facturas.PuedeAsync(Acciones.FacturasEditar);
            _puedeValidar = await _s.Facturas.PuedeAsync(Acciones.FacturasValidar);
            _puedeRectificar = await _s.Facturas.PuedeAsync(Acciones.FacturasRectificar);
            _config = await _s.Configuracion.ObtenerAsync(_s.Trabajo.IdSociedadEjercicio);
            _tiposIva = await _s.Apoyo.TiposIvaAsync();
            var centros = await _s.CentrosCoste.ListarAsync(_s.Trabajo.IdSociedad);
            _centros = [SinCentro, .. centros.Where(c => c.EstaActivo && c.EsImputable).Select(c => new OpcionCentroCoste(c.IdCentroCoste, $"{c.Codigo.Trim()} · {c.Nombre.Trim()}"))];
            Rellenar(Series, await _s.Facturas.SeriesAsync(_s.Trabajo.IdSociedadEjercicio));
            Rellenar(TiposFactura, await _s.Apoyo.TiposFacturaAsync());
            Rellenar(FormasPago, await _s.FormasPago.ListarAsync());
        }
        catch (Exception ex)
        {
            ErrorCarga = $"No he podido leer los datos de facturación: {ex.Message}";
        }
    }

    private static void Rellenar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var x in origen) destino.Add(x);
    }

    private void FalloCarga(IReadOnlyList<Error> errores)
    {
        ErrorCarga = string.Join(" ", errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")));
        Cargando = false;
    }

    private async Task MostrarAsync(FacturaVenta f)
    {
        _f = f.Clonar();
        _p?.CambiarPlantilla(_f);
        _cliente = f.IdCliente is int c ? await _s.Apoyo.ClienteAsync(c) : null;
        await CargarCuentasAsync();
        _fechaFactura = Texto.Fecha(f.FechaFactura);
        _fechaOperacion = Texto.Fecha(f.FechaOperacion);
        _periodoDesde = Texto.Fecha(f.FechaInicioValidez);
        _periodoHasta = Texto.Fecha(f.FechaFinValidez);
        _diaPago = f.DiaDePago?.ToString(CultureInfo.InvariantCulture) ?? "";
        _retencion = Texto.Porcentaje(f.TipoRetencion);
        _errores.Clear();
        Incidencias.Clear();
        ReconstruirLineas();
        _firmaGuardada = Firma();
        GuardadoFallido = false;
        ErrorCarga = null;
        _cargando = false;
        TodoCambiado();
    }

    private async Task CargarCuentasAsync()
    {
        (_cuentaCliente, _cuentaRetencion) = _f.IdCliente is int c ? await _s.Apoyo.CuentasClienteAsync(c, _s.Trabajo) : (null, null);
    }

    private void ReconstruirLineas()
    {
        Lineas.Clear();
        foreach (var l in _f.Lineas) Lineas.Add(new LineaFacturaVM(l, EditableLineas, _tiposIva, _centros, LineaCambiada));
    }

    private void LineaCambiada()
    {
        AvisarTotales();
        Cambio();
    }

    private void AvisarTotales() =>
        Avisar(nameof(TotalBase), nameof(TotalIva), nameof(TotalSuplidos), nameof(TotalBruto), nameof(TotalRetencion), nameof(TotalACobrar),
            nameof(Desglose), nameof(Asiento), nameof(TextoLineas), nameof(SinLineas), nameof(TextoRetencion),
            nameof(HaySuplidos), nameof(HayRetencion), nameof(HayVariosIva));

    private void TodoCambiado()
    {
        Avisar("");
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Un campo ha cambiado: se avisa a la pestaña (punto de cambios) y se refrescan totales y asiento.</summary>
    private void Cambio()
    {
        if (_cargando) return;
        GuardadoFallido = false;
        Avisar(nameof(TieneCambios), nameof(Ayuda), nameof(PuedeGuardar), nameof(Asiento), nameof(Subtitulo));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- cliente ----------

    private async Task BuscarClienteAsync()
    {
        var elegido = _modulo.Dialogos.ElegirCliente();
        if (elegido is null) return;
        _cliente = elegido;
        _f.IdCliente = elegido.IdCliente;
        await CargarCuentasAsync();
        if (await _s.Facturas.PropuestaClienteAsync(elegido.IdCliente, _s.Trabajo.IdSociedad) is { } p)
        {
            if (p.TipoRetencion is decimal r && _f.TipoRetencion == 0m) { _f.TipoRetencion = r; _retencion = Texto.Porcentaje(r); }
            if (p.FormaDePago is byte fp && _f.FormaDePago is null) _f.FormaDePago = fp;
            if (p.DiaDePago is byte dp && _f.DiaDePago is null) { _f.DiaDePago = dp; _diaPago = dp.ToString(CultureInfo.InvariantCulture); }
        }
        QuitarError("Cliente");
        Avisar(nameof(HayCliente), nameof(NombreCliente), nameof(NifCliente), nameof(DomicilioCliente), nameof(TextoBuscarCliente), nameof(CuentaCliente),
            nameof(FaltaCuentaCliente), nameof(CuentaRetencion), nameof(FaltaCuentaRetencion), nameof(Retencion), nameof(DiaPago), nameof(FormaPago), nameof(TextoRetencion),
            nameof(CodigoCliente), nameof(CuentaClienteCorta));
        AvisarTotales();
        Cambio();
    }

    private async Task AsignarCuentaAsync(ClaseCuentaRol clase)
    {
        if (_cliente is null) return;
        var actual = clase == ClaseCuentaRol.Retencion ? _cuentaRetencion : _cuentaCliente;
        var propuesta = actual is { ExisteEnEjercicio: false } ? actual.Codigo : await _s.Facturas.ProponerCodigoCuentaAsync(clase, _s.Trabajo);
        var codigo = _modulo.Dialogos.PedirCuenta(clase, _cliente.Nombre, propuesta, _s.RazonSocial);
        if (codigo is null) return;
        var r = await _s.Facturas.AsignarCuentaClienteAsync(_cliente.IdCliente, clase, _s.Trabajo, codigo);
        if (!r.EsCorrecto)
        {
            MostrarErrores("No se ha asignado la cuenta", r.Errores);
            return;
        }
        await CargarCuentasAsync();
        _modulo.Avisar($"Cuenta {r.Valor.Codigo} asignada a {_cliente.Nombre} en {_s.Trabajo.IdSociedad}.");
        Incidencias.Clear();
        Avisar(nameof(CuentaCliente), nameof(CuentaClienteCorta), nameof(FaltaCuentaCliente), nameof(CuentaRetencion), nameof(FaltaCuentaRetencion), nameof(Asiento), nameof(HayIncidencias));
    }

    // ---------- líneas ----------

    private async Task AnadirLineaAsync()
    {
        var conceptos = await _s.Conceptos.ListarAsync(_s.Trabajo, soloActivos: true);
        var c = _modulo.Dialogos.ElegirConcepto(conceptos);
        var linea = new LineaFacturaVenta();
        if (c is not null)
        {
            linea.IdConcepto = c.IdConcepto;
            linea.Nombre = c.Nombre;
            linea.Descripcion = c.Descripcion ?? "";
            linea.PrecioUnitario = c.PrecioUnitario;
            linea.TipoIva = c.TipoIva ?? _tiposIva.FirstOrDefault();
            linea.CuentaContable = c.CuentaVenta;
            linea.IdDepartamento = c.IdDepartamento;
            linea.IdCentroCoste = c.IdCentroCoste;
        }
        else if (conceptos.Count > 0) return; // canceló el diálogo
        else linea.TipoIva = _tiposIva.FirstOrDefault();
        linea.Recalcular();
        _f.AnadirLinea(linea);
        Lineas.Add(new LineaFacturaVM(linea, EditableLineas, _tiposIva, _centros, LineaCambiada));
        QuitarError("Lineas");
        AvisarTotales();
        Cambio();
    }

    internal void MoverLinea(LineaFacturaVM l, int desplazamiento)
    {
        var i = Lineas.IndexOf(l);
        if (!_f.MoverLinea(l.Datos, desplazamiento)) return;
        Lineas.Move(i, i + desplazamiento);
        foreach (var x in Lineas) x.AvisarNumero();
        AvisarTotales();
        Cambio();
        CommandManager.InvalidateRequerySuggested();
    }

    private void QuitarLinea(LineaFacturaVM l)
    {
        _f.QuitarLinea(l.Datos);
        ReconstruirLineas();
        AvisarTotales();
        Cambio();
    }

    // ---------- acciones ----------

    public async Task<bool> GuardarAsync()
    {
        if (!_puedeEditar) return false;
        if (HasErrors)
        {
            MostrarIncidencias(_errores.Values.Select(m => new Incidencia("Campo", m)).ToList());
            GuardadoFallido = true;
            return false;
        }
        if (_p is not null)
        {
            var nueva = _p.EsNueva;
            var rp = await _s.Programadas.GuardarAsync(_p, _s.Actor);
            if (!rp.Guardada) { MostrarIncidencias(rp.Incidencias); GuardadoFallido = true; return false; }
            await MostrarProgramadaAsync(rp.Programada!);
            _modulo.Avisar($"Programada {rp.Programada!.Rotulo} {(nueva ? "creada" : "guardada")}" +
                           (rp.Programada.Activa ? rp.Programada.ProximaCalculada is { } pr ? $": próxima emisión el {Formatos.Fecha(pr)}." : "." : ", pausada: no genera facturas hasta que la reanudes."));
            _modulo.ProgramadaCambiada(rp.Programada.IdFacturaProgramada);
            return true;
        }
        if (!EsBorrador)
        {
            var notas = await _s.Facturas.GuardarNotasAsync(_f.IdFactura, _f.NotasInternas, _f.Version, _s.Actor);
            if (!notas.EsCorrecto) { MostrarErrores("No se han guardado las notas", notas.Errores); GuardadoFallido = true; return false; }
            await RecargarAsync(_f.IdFactura);
            _modulo.Avisar("Notas guardadas.");
            _modulo.FacturaCambiada(_f.IdFactura);
            return true;
        }
        var r = await _s.Facturas.GuardarBorradorAsync(_f, _s.Actor);
        if (!r.EsCorrecto) { MostrarErrores("No se ha guardado el borrador", r.Errores); GuardadoFallido = true; return false; }
        await MostrarAsync(r.Valor);
        _modulo.Avisar($"{r.Valor.Rotulo} guardado. Sigue sin número.");
        _modulo.FacturaCambiada(r.Valor.IdFactura);
        return true;
    }

    public void Descartar()
    {
        if (_p is not null)
        {
            if (_p.EsNueva) _ = NuevaProgramadaAsync();
            else _ = CargarProgramadaAsync(_p.IdFacturaProgramada);
            return;
        }
        if (_f.EsNueva) { _ = NuevaAsync(); return; }
        _ = RecargarAsync(_f.IdFactura);
    }

    private async Task RecargarAsync(int id)
    {
        if (await _s.Facturas.ObtenerAsync(id) is { } f) await MostrarAsync(f);
    }

    private async Task ValidarAsync()
    {
        if (TieneCambios && !await GuardarAsync()) return;
        // Primero se comprueba todo, para mostrar todos los problemas a la vez sin numerar nada.
        var previas = await _s.Facturas.ComprobarAsync(_f);
        if (previas.Any(i => !i.EsAviso)) { MostrarIncidencias(previas); return; }
        var serie = Serie?.Prefijo.Trim() ?? "";
        var fecha = _f.FechaFactura is { } d ? Formatos.Fecha(d) : Formatos.Fecha(DateTime.Today);
        var avisos = previas.Where(i => i.EsAviso).Select(i => i.Mensaje).ToList();
        var mensaje = $"Se asignará el siguiente número de la serie {serie} con fecha {fecha}, se copiarán los datos del cliente y se generará el asiento. Después no se podrá cambiar." +
                      $"{Environment.NewLine}{Environment.NewLine}Esta validación es provisional, «{_s.Facturas.NombrePasoFiscal}»: la factura no se envía a la AEAT y solo vale en la BD de pruebas.";
        var opcion = _modulo.Dialogos.Preguntar("Validar la factura", mensaje, avisos.Count > 0 ? avisos : null,
            new OpcionMensaje("Cancelar", "Vuelve al borrador sin validar", Cancelar: true),
            new OpcionMensaje(avisos.Count > 0 ? "Validar con total cero" : "Validar", "Numera la factura y genera el asiento", Principal: true));
        if (opcion != 1) return;
        var r = await _s.Facturas.ValidarAsync(_f.IdFactura, _f.Version, _s.Actor, aceptarAvisos: true);
        if (!r.Validada) { MostrarIncidencias(r.Incidencias); return; }
        await MostrarAsync(r.Factura!);
        _modulo.FacturaCambiada(r.Factura!.IdFactura);
        if (r.Contabilizada) _modulo.Avisar($"Factura {r.Factura!.NumeroFactura} validada y contabilizada.");
        else
            MostrarErrores($"Factura {r.Factura!.NumeroFactura} validada sin asiento", r.ErrorContabilizacion ?? [],
                "La factura ya tiene su número y no cambia. El asiento no se ha podido generar; corrige lo que se indica y contabilízala desde Facturas.");
    }

    private async Task DuplicarAsync()
    {
        if (TieneCambios && EsBorrador && !await GuardarAsync()) return;
        if (_p is not null)
        {
            var d = await _s.Programadas.DuplicarAsync(_p.IdFacturaProgramada, _s.Actor);
            if (!d.Guardada) { MostrarErrores("No se ha duplicado", d.Incidencias.Select(i => new Error(i.Codigo, i.Mensaje)).ToList()); return; }
            _modulo.ProgramadaCambiada(d.Programada!.IdFacturaProgramada);
            _modulo.AbrirProgramada(d.Programada.IdFacturaProgramada);
            _modulo.Avisar($"Creada {d.Programada.Rotulo} a partir de {_p.Rotulo}, pausada y sin facturas generadas: revísala y reanúdala.");
            return;
        }
        var r = await _s.Facturas.DuplicarAsync(_f.IdFactura, _s.Trabajo, _s.Actor);
        if (!r.EsCorrecto) { MostrarErrores("No se ha duplicado", r.Errores); return; }
        _modulo.FacturaCambiada(r.Valor.IdFactura);
        _modulo.AbrirFactura(r.Valor.IdFactura);
        _modulo.Avisar($"Creado {r.Valor.Rotulo} a partir de {_f.Rotulo}, sin número ni fecha.");
    }

    private async Task EliminarAsync()
    {
        if (_p is not null)
        {
            if (_p.EsNueva) { _cargando = true; _modulo.CerrarEditor(this); return; }
            if (_modulo.Dialogos.Preguntar($"Eliminar {_p.Rotulo}", "Se borra la programada y sus líneas. Solo se puede si aún no ha generado ninguna factura; si ya tiene, páusala.", null,
                    new OpcionMensaje("Cancelar", "No borra nada", Cancelar: true),
                    new OpcionMensaje("Eliminar", "Borra la programada", Peligro: true)) != 1) return;
            var e = await _s.Programadas.EliminarAsync(_p.IdFacturaProgramada, _p.Version);
            if (!e.EsCorrecto) { MostrarErrores("No se ha eliminado", e.Errores); return; }
            _modulo.Avisar($"{_p.Rotulo} eliminada.");
            _modulo.ProgramadaCambiada();
            _cargando = true;
            _modulo.CerrarEditor(this);
            return;
        }
        if (_f.EsNueva) { _modulo.CerrarEditor(this); return; }
        var mensaje = EsRectificativa
            ? $"Se borra este borrador y la factura {_f.NumeroFacturaRectificada} vuelve a «Validada», todo a la vez."
            : "Se borra el borrador y sus líneas. No consume número.";
        var opcion = _modulo.Dialogos.Preguntar(TextoEliminar, mensaje, null,
            new OpcionMensaje("Cancelar", "No borra nada", Cancelar: true),
            new OpcionMensaje("Eliminar", "Borra el borrador", Peligro: true));
        if (opcion != 1) return;
        var r = await _s.Facturas.EliminarBorradorAsync(_f.IdFactura, _f.Version, _s.Actor);
        if (!r.EsCorrecto) { MostrarErrores("No se ha eliminado", r.Errores); return; }
        _modulo.Avisar($"{_f.Rotulo} eliminado.");
        _modulo.FacturaCambiada();
        _cargando = true; // sin cambios pendientes al cerrar
        _modulo.CerrarEditor(this);
    }

    private async Task RectificarAsync()
    {
        if (TieneCambios && !await GuardarAsync()) return;
        var tipo = ReglasFactura.TipoRectificativaDe(_f.TipoFactura);
        var motivo = _modulo.Dialogos.PedirMotivoRectificacion(_f.NumeroFactura ?? _f.Rotulo,
            $"Se crea un borrador rectificativo {tipo} en la serie de rectificativas con las mismas líneas y las cantidades en negativo. Mientras sea borrador, {_f.NumeroFactura} queda en «Rectificación pendiente»; al validarlo pasa a «Rectificada». Si eliminas el borrador, vuelve a «Validada».");
        if (motivo is null) return;
        var r = await _s.Facturas.CrearRectificativaAsync(_f.IdFactura, motivo, _s.Trabajo, _s.Actor);
        if (!r.EsCorrecto) { MostrarErrores("No se ha creado la rectificativa", r.Errores); return; }
        await RecargarAsync(_f.IdFactura);
        _modulo.FacturaCambiada(r.Valor.IdFactura);
        _modulo.AbrirFactura(r.Valor.IdFactura);
    }

    // ---------- errores ----------

    private void MostrarErrores(string titulo, IReadOnlyList<Error> errores, string? explicacion = null)
    {
        var detalles = errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")).ToList();
        _modulo.Dialogos.Preguntar(titulo, explicacion ?? (detalles.Count == 1 ? detalles[0] : "Revisa lo siguiente:"), detalles.Count == 1 && explicacion is null ? null : detalles,
            new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
    }

    /// <summary>Pinta las incidencias en sus campos y en el resumen, con la acción que las corrige si la hay.</summary>
    private void MostrarIncidencias(IReadOnlyList<Incidencia> incidencias)
    {
        foreach (var p in _errores.Keys.ToList()) QuitarError(p);
        foreach (var l in Lineas) l.LimpiarErrores();
        Incidencias.Clear();
        foreach (var i in incidencias)
        {
            if (i.Campo is { } campo) MarcarCampo(campo, i.Mensaje);
            var (texto, accion) = i.Accion switch
            {
                "AsignarCuentaCliente" => ("Asignar cuenta…", AsignarCuentaClienteCommand),
                "AsignarCuentaRetencion" => ("Asignar cuenta…", AsignarCuentaRetencionCommand),
                "AbrirFichaCliente" => ("Abrir ficha", AbrirFichaCommand),
                _ => ((string?)null, (ICommand?)null)
            };
            Incidencias.Add(new IncidenciaVM(i.Mensaje, i.EsAviso, texto, accion));
        }
        Avisar(nameof(HayIncidencias), nameof(TituloIncidencias), nameof(IncidenciasSonAvisos));
    }

    private void MarcarCampo(string campo, string mensaje)
    {
        if (campo.StartsWith("Linea", StringComparison.Ordinal) && campo != "Lineas")
        {
            var partes = campo[5..].Split('.', 2);
            if (int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && Lineas.FirstOrDefault(l => l.Numero == n) is { } linea)
                linea.PonerError(partes.Length > 1 ? LineaFacturaVM.Propiedad(partes[1]) ?? nameof(LineaFacturaVM.Nombre) : nameof(LineaFacturaVM.Nombre), mensaje);
            return;
        }
        var propiedad = campo switch
        {
            "FechaFinValidez" => nameof(PeriodoHasta),
            "FechaInicioValidez" => nameof(PeriodoDesde),
            "TipoRetencion" => nameof(Retencion),
            "IdSerie" => nameof(Serie),
            "MotivoRectificacion" => nameof(Motivo),
            "FechaInicio" => nameof(FechaInicioProgramacion),
            "FechaFin" => nameof(FechaFinProgramacion),
            "NumeroRepeticiones" => nameof(Repeticiones),
            _ => campo
        };
        PonerError(propiedad, mensaje);
    }

    public bool HasErrors => _errores.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errores.TryGetValue(propertyName, out var e) ? new[] { e } : Array.Empty<string>();

    /// <summary>Error del campo (para los que no son de texto: cliente, líneas).</summary>
    public string? ErrorCliente => _errores.GetValueOrDefault("Cliente");
    public string? ErrorLineas => _errores.GetValueOrDefault("Lineas");

    private void PonerError(string propiedad, string mensaje)
    {
        _errores[propiedad] = mensaje;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
        Avisar(nameof(ErrorCliente), nameof(ErrorLineas));
    }

    private void QuitarError(string propiedad)
    {
        if (!_errores.Remove(propiedad)) return;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
        Avisar(nameof(ErrorCliente), nameof(ErrorLineas));
    }

    // ---------- cambios ----------

    /// <summary>Huella de lo editable, para saber si hay cambios sin guardar.</summary>
    private string Firma()
    {
        var f = _f;
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"{f.IdSerie}|{f.IdCliente}|{f.TipoFactura}|{f.FechaFactura:d}|{f.FechaOperacion:d}|{f.FechaInicioValidez:d}|{f.FechaFinValidez:d}|");
        b.Append(CultureInfo.InvariantCulture, $"{f.FormaDePago}|{f.DiaDePago}|{f.TipoRetencion}|{f.Descripcion}|{f.NotasInternas}|{f.MotivoRectificacion}");
        if (_p is { } p)
            b.Append(CultureInfo.InvariantCulture, $"|P{p.IdFacturaProgramada}|{p.Periodicidad.Codigo}|{p.FechaInicio:d}|{p.DiaEmision}|{p.FechaFin:d}|{p.NumeroRepeticiones}|{p.AlGenerar}|{p.EnviarPorEmail}|{p.Activa}");
        foreach (var l in f.Lineas)
            b.Append(CultureInfo.InvariantCulture,
                $"#{l.IdConcepto}|{l.Nombre}|{l.Descripcion}|{l.Cantidad}|{l.PrecioUnitario}|{l.DescuentoPorcentaje}|{l.DescuentoImporte}|{l.TipoIva}|{l.EsSuplido}|{l.CuentaContable}|{l.ImporteTotal}|{l.Fecha:d}|{l.IdCentroCoste}");
        return b.ToString();
    }
}
