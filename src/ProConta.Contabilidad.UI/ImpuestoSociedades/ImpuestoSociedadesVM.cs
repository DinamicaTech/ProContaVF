using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ProConta.Contabilidad.Impuestos;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.ImpuestoSociedades;

/// <summary>Contexto de la pantalla Impuesto sobre Sociedades y lo que abre fuera de ella.</summary>
public sealed record ContextoImpuestoSociedades(ServicioImpuestoSociedades Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar o deshacer.</summary>
    public Action? Cambiada { get; init; }
    /// <summary>Pide el HTML de los datos fiscales guardado desde la Sede; null si se cancela.</summary>
    public Func<string?>? ElegirDatosFiscales { get; init; }
    /// <summary>Abre una dirección web en el navegador.</summary>
    public Action<string>? AbrirEnlace { get; init; }
}

/// <summary>Una línea de la hoja de cálculo (concepto e importe), en el orden de la de la gestoría.</summary>
public sealed record FilaCalculoIsVM(string Concepto, string Importe, string Ayuda, bool Destacada = false, bool Sangrada = false);

/// <summary>Línea de ajuste editable: clase (permanente o temporal), sentido, descripción e importe.</summary>
public sealed class FilaAjusteIsVM : ModeloVista
{
    public const string Permanente = "Permanente", Temporal = "Temporal", Aumento = "Aumento", Disminucion = "Disminución";
    private string _clase = Permanente, _sentido = Aumento, _descripcion = "", _importe = "";

    public string Clase { get => _clase; set => Fijar(ref _clase, value); }
    public string Sentido { get => _sentido; set => Fijar(ref _sentido, value); }
    public string Descripcion { get => _descripcion; set => Fijar(ref _descripcion, value); }
    public string Importe { get => _importe; set => Fijar(ref _importe, value); }
    public string? Cuenta { get; init; }

    public static FilaAjusteIsVM De(AjusteImpuestoSociedades a) => new()
    {
        _clase = a.Temporal ? Temporal : Permanente, _sentido = a.Aumento ? Aumento : Disminucion, _descripcion = a.Descripcion,
        _importe = Formatos.Numero(a.Importe), Cuenta = a.Cuenta,
    };

    /// <summary>El ajuste escrito; null si el importe no se puede leer.</summary>
    public AjusteImpuestoSociedades? Leer() =>
        Formatos.LeerImporte((_importe ?? "").Trim().Length == 0 ? "0" : _importe, out var v)
            ? new AjusteImpuestoSociedades(_clase == Temporal, _sentido == Aumento, _descripcion ?? "", Math.Round(v, 2, MidpointRounding.AwayFromZero), Cuenta)
            : null;
}

/// <summary>Subcuenta con saldo (posible ajuste).</summary>
public sealed record FilaCuentaIsVM(string Cuenta, string Nombre, string Detalle, string Saldo, SaldoCuentaImpuesto Datos);

/// <summary>Una 473 en la pantalla del impuesto: su saldo, lo que dice Hacienda y lo que se deduce (se decide en el contraste de retenciones).</summary>
public sealed record FilaRetencionIsVM(string Cuenta, string Nombre, string Pagadores, string Saldo, string SegunAeat, string Deducido);

/// <summary>Apunte de una 473 en el detalle del contraste.</summary>
public sealed record FilaApunteIsVM(int IdAsiento, string Fecha, string Cuenta, string Texto, string Importe, string Pagador);

/// <summary>Línea de los datos fiscales de Hacienda en el detalle del contraste.</summary>
public sealed record FilaAeatIsVM(string Modelo, string Nif, string Nombre, string Importe);

/// <summary>Partida del cálculo de la renta de viviendas.</summary>
public sealed record FilaPartidaIsVM(string Clase, string Centro, string Cuenta, string Nombre, string Importe, string Apuntes);

/// <summary>
/// Fila del contraste de retenciones: un pagador con lo que dice Hacienda, lo contabilizado y el valor para el IS (vacío: el propuesto) con su
/// justificación; en las filas que solo conoce Hacienda, la 473 a la que van. Se conserva mientras se escribe para no perder la fila en edición.
/// </summary>
public sealed class FilaContrasteIsVM(string clave, Action<FilaContrasteIsVM> alEscribir) : ModeloVista
{
    private string _valor = "", _justificacion = "", _cuenta = "", _pagador = "", _nif = "", _hacienda = "", _contable = "", _diferencia = "", _propuesto = "", _usado = "";
    private bool _escrito, _soloHacienda, _faltaJustificacion;
    private ContrasteRetencion? _datos;

    public string Clave { get; } = clave;
    public string Pagador { get => _pagador; private set => Fijar(ref _pagador, value); }
    public string Nif { get => _nif; private set => Fijar(ref _nif, value); }
    public string Hacienda { get => _hacienda; private set => Fijar(ref _hacienda, value); }
    public string Contable { get => _contable; private set => Fijar(ref _contable, value); }
    public string Diferencia { get => _diferencia; private set => Fijar(ref _diferencia, value); }
    public string Propuesto { get => _propuesto; private set => Fijar(ref _propuesto, value); }
    public string Usado { get => _usado; private set => Fijar(ref _usado, value); }
    public bool Escrito { get => _escrito; private set => Fijar(ref _escrito, value); }
    public bool SoloHacienda { get => _soloHacienda; private set => Fijar(ref _soloHacienda, value); }
    public bool FaltaJustificacion { get => _faltaJustificacion; private set => Fijar(ref _faltaJustificacion, value); }
    public ContrasteRetencion? Datos => _datos;

    /// <summary>Valor para el IS escrito; vacío, el propuesto.</summary>
    public string Valor { get => _valor; set { if (Fijar(ref _valor, value ?? "")) alEscribir(this); } }
    public string Justificacion { get => _justificacion; set { if (Fijar(ref _justificacion, value ?? "")) alEscribir(this); } }
    /// <summary>473 a la que va el valor (solo se elige en las filas que solo conoce Hacienda).</summary>
    public string Cuenta { get => _cuenta; set { if (Fijar(ref _cuenta, value ?? "")) alEscribir(this); } }

    public IReadOnlyList<FilaApunteIsVM> Apuntes => _datos?.Apuntes.Select(a => new FilaApunteIsVM(a.IdAsiento, Formatos.Fecha(a.Fecha), a.Cuenta, a.Texto,
        Formatos.Importe(a.Importe), a.Pagador ?? "")).ToList() ?? [];
    public IReadOnlyList<FilaAeatIsVM> LineasHacienda => _datos?.Aeat.Select(a => new FilaAeatIsVM(a.Modelo, a.Nif ?? "", a.Nombre, Formatos.Importe(a.Importe))).ToList() ?? [];

    internal void Cargar(string valor, string justificacion, string cuenta)
    {
        _valor = valor;
        _justificacion = justificacion;
        _cuenta = cuenta;
        Avisar(nameof(Valor), nameof(Justificacion), nameof(Cuenta));
    }

    internal void Actualizar(ContrasteRetencion r)
    {
        var cambianDetalles = _datos is null || !_datos.Apuntes.SequenceEqual(r.Apuntes) || !_datos.Aeat.SequenceEqual(r.Aeat);
        _datos = r;
        Pagador = r.Pagador;
        Nif = r.Nif ?? "";
        Hacienda = r.Hacienda is decimal h ? Formatos.Importe(h) : "";
        Contable = Formatos.Importe(r.Contable);
        Diferencia = r.Hacienda is null || r.Diferencia == 0m ? "" : Formatos.Importe(r.Diferencia);
        Propuesto = Formatos.Importe(r.Propuesto);
        Usado = Formatos.Importe(r.Valor);
        Escrito = r.Escrito;
        SoloHacienda = r.SoloHacienda;
        FaltaJustificacion = r.FaltaJustificacion;
        if (_cuenta.Length == 0 && r.Cuenta is { } c) { _cuenta = c; Avisar(nameof(Cuenta)); }
        if (cambianDetalles) Avisar(nameof(Apuntes), nameof(LineasHacienda));
    }
}

/// <summary>Pestaña que muestra una parte del impuesto (contraste de retenciones o renta de viviendas) con los datos de la pantalla del impuesto.</summary>
public sealed class PestanaParteIsVM(ImpuestoSociedadesVM impuesto, string titulo, string icono, Func<object> crearVista) : ModeloVista, IPestanaTrabajo
{
    private object? _vista;
    public ImpuestoSociedadesVM Impuesto { get; } = impuesto;
    public string Titulo => titulo;
    public string Ayuda => $"{titulo} · {Impuesto.Subtitulo}";
    public string Icono => icono;
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= crearVista();
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() { }
}

/// <summary>Línea del asiento propuesto.</summary>
public sealed record FilaLineaIsVM(string Cuenta, string Concepto, string Debe, string Haber);

/// <summary>Una cifra del resumen de la renta de viviendas.</summary>
public sealed record FilaResumenRentaVM(string Concepto, string Importe, string Ayuda, bool Destacada = false);

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades): el cálculo en el
/// orden de la hoja de la gestoría, los ajustes que escribe el operador (con los posibles ajustes para validar) y el asiento del 31/12.
/// El contraste de retenciones con Hacienda y el detalle de la renta de viviendas se abren en pestañas propias con los mismos datos.
/// Lee la contabilidad una vez y recalcula al escribir; los botones del pie confirman exactamente lo que se ve. El menú contextual de cada
/// tabla tiene lo que actúa sobre su fila.
/// </summary>
public sealed class ImpuestoSociedadesVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoImpuestoSociedades _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly IAnfitrionPestanas? _anfitrion;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado, _cargandoEntrada;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionImpuestoSociedades? _situacion;
    private DatosImpuestoSociedades? _datos;
    private PropuestaImpuestoSociedades? _propuesta;
    private string? _errorPropuesta;
    private string _basesNegativas = "", _bonificaciones = "", _deducciones = "", _credito = "", _cuota = "", _motivo = "", _basesAeat = "", _presentado = "", _renta = "";
    private FilaAjusteIsVM? _ajusteSeleccionado;
    private FilaCuentaIsVM? _posibleSeleccionado;
    private FilaRetencionIsVM? _retencionSeleccionada;
    private FilaContrasteIsVM? _contrasteSeleccionado;
    private FilaApunteIsVM? _apunteSeleccionado;
    private FilaPartidaIsVM? _partidaSeleccionada;
    private List<RetencionAeat> _aeat = [];
    /// <summary>Lo escrito en el contraste por clave de fila: valor, justificación y 473 (esta solo en las filas que solo conoce Hacienda).</summary>
    private readonly Dictionary<string, (string Valor, string Justificacion, string Cuenta)> _valores = new(StringComparer.Ordinal);
    private PestanaParteIsVM? _pestanaContraste, _pestanaRenta;

    public ImpuestoSociedadesVM(ContextoImpuestoSociedades ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null, IAnfitrionPestanas? anfitrion = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        _anfitrion = anfitrion;
        Ajustes.CollectionChanged += (_, e) =>
        {
            foreach (FilaAjusteIsVM f in e.NewItems ?? Array.Empty<FilaAjusteIsVM>()) f.PropertyChanged += AlCambiarAjuste;
            foreach (FilaAjusteIsVM f in e.OldItems ?? Array.Empty<FilaAjusteIsVM>()) f.PropertyChanged -= AlCambiarAjuste;
            Recalcular();
        };
        AbrirSedeCommand = new ComandoDelegado(() => _ = AbrirSedeAsync(), () => _ctx.AbrirEnlace is not null);
        ImportarDatosFiscalesCommand = new ComandoDelegado(() => _ = ImportarDatosFiscalesAsync(), () => !_ocupado && _datos is not null && _ctx.ElegirDatosFiscales is not null);
        QuitarDatosAeatCommand = new ComandoDelegado(QuitarDatosAeat, () => !_ocupado && HayDatosAeat);
        CopiarHaciendaTodasCommand = new ComandoDelegado(() => Copiar(Contraste, hacienda: true), () => !_ocupado && HayDatosAeat && Contraste.Count > 0);
        CopiarContabilidadTodasCommand = new ComandoDelegado(() => Copiar(Contraste, hacienda: false), () => !_ocupado && Contraste.Count > 0);
        CopiarHaciendaCommand = new ComandoDelegado(() => Copiar(Seleccion(), hacienda: true), () => !_ocupado && HayDatosAeat && _contrasteSeleccionado is not null);
        CopiarContabilidadCommand = new ComandoDelegado(() => Copiar(Seleccion(), hacienda: false), () => !_ocupado && _contrasteSeleccionado is not null);
        UsarPropuestoCommand = new ComandoDelegado(() => { if (_contrasteSeleccionado is { } f) f.Valor = ""; }, () => _contrasteSeleccionado is { Valor.Length: > 0 });
        MayorContrasteCommand = new ComandoDelegado(() => AbrirMayor(_contrasteSeleccionado?.Datos?.Cuenta),
            () => _contrasteSeleccionado?.Datos?.Cuenta is not null && _ctx.AbrirMayor is not null);
        VerApunteCommand = new ComandoDelegado(() => _ = VerAsientoAsync(_apunteSeleccionado?.IdAsiento), () => _apunteSeleccionado is { IdAsiento: > 0 } && _ctx.VerAsiento is not null);
        MayorApunteCommand = new ComandoDelegado(() => AbrirMayor(_apunteSeleccionado?.Cuenta), () => _apunteSeleccionado is not null && _ctx.AbrirMayor is not null);
        MayorRetencionCommand = new ComandoDelegado(() => AbrirMayor(_retencionSeleccionada?.Cuenta), () => _retencionSeleccionada is not null && _ctx.AbrirMayor is not null);
        AbrirContrasteCommand = new ComandoDelegado(AbrirContraste, () => _anfitrion is not null);
        AbrirRentaCommand = new ComandoDelegado(AbrirRenta, () => _anfitrion is not null);
        MayorPartidaCommand = new ComandoDelegado(() => AbrirMayor(_partidaSeleccionada?.Cuenta), () => _partidaSeleccionada is not null && _ctx.AbrirMayor is not null);
        UsarRentaCalculadaCommand = new ComandoDelegado(() => RentaAplicar = "", () => _renta.Trim().Length > 0);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        RecalcularCommand = new ComandoDelegado(() => _ = LeerDatosAsync(), () => !_ocupado);
        AnadirAjusteCommand = new ComandoDelegado(() => Ajustes.Add(new FilaAjusteIsVM()), () => !_ocupado);
        QuitarAjusteCommand = new ComandoDelegado(() => { if (_ajusteSeleccionado is { } f) Ajustes.Remove(f); }, () => _ajusteSeleccionado is not null && !_ocupado);
        AnadirPosibleCommand = new ComandoDelegado(AnadirPosible, () => _posibleSeleccionado is not null && !_ocupado);
        MayorPosibleCommand = new ComandoDelegado(() => AbrirMayor(_posibleSeleccionado?.Cuenta), () => _posibleSeleccionado is not null && _ctx.AbrirMayor is not null);
        UsarBonificacionCommand = new ComandoDelegado(() => Bonificaciones = "", () => _bonificaciones.Trim().Length > 0);
        UsarCuotaCommand = new ComandoDelegado(() => Cuota = "", () => _cuota.Trim().Length > 0);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(() => { CargarEntrada(_situacion?.Guardado?.Entrada ?? EntradaImpuestoSociedades.Vacia); Motivo = ""; }, () => !_ocupado && _datos is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(_situacion?.Guardado?.IdAsiento), () => _situacion?.Guardado?.IdAsiento is not null && _ctx.VerAsiento is not null);
        MayorAsientoCommand = new ComandoDelegado(() => _ctx.AbrirMayor?.Invoke([.. (_propuesta?.Lineas ?? []).Select(l => l.Cuenta).Distinct()]),
            () => _ctx.AbrirMayor is not null && _propuesta is { Lineas.Count: > 0 });
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _situacion is { Estado: not EstadoImpuestoSociedades.Pendiente });
    }

    // ---------- pestaña ----------
    public string Titulo => "Impuesto sobre Sociedades";
    public string Ayuda => $"Impuesto sobre Sociedades de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaImpuestoSociedades { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }

    public void AlCerrar()
    {
        // Las pestañas del contraste y de la renta muestran los datos de esta: se cierran con ella.
        foreach (var p in new[] { _pestanaContraste, _pestanaRenta })
            if (p is not null && _anfitrion?.Pestanas.Contains(p) == true) _anfitrion.Cerrar(p);
        _alCerrar?.Invoke();
    }

    private void AbrirContraste() => _pestanaContraste = AbrirParte(_pestanaContraste, "Contraste de retenciones", "list", () => new PantallaContrasteRetenciones { DataContext = this });
    private void AbrirRenta() => _pestanaRenta = AbrirParte(_pestanaRenta, "Renta de viviendas", "doc", () => new PantallaRentaViviendas { DataContext = this });

    private PestanaParteIsVM? AbrirParte(PestanaParteIsVM? actual, string titulo, string icono, Func<object> vista)
    {
        if (_anfitrion is null) return actual;
        if (actual is not null && _anfitrion.Pestanas.Contains(actual))
        {
            _anfitrion.Activar(actual);
            return actual;
        }
        var nueva = new PestanaParteIsVM(this, titulo, icono, vista);
        _anfitrion.Abrir(nueva);
        return nueva;
    }

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand AnadirAjusteCommand { get; }
    public ICommand QuitarAjusteCommand { get; }
    public ICommand AnadirPosibleCommand { get; }
    public ICommand MayorPosibleCommand { get; }
    public ICommand MayorRetencionCommand { get; }
    public ICommand UsarBonificacionCommand { get; }
    public ICommand UsarCuotaCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand MayorAsientoCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand AbrirSedeCommand { get; }
    public ICommand ImportarDatosFiscalesCommand { get; }
    public ICommand QuitarDatosAeatCommand { get; }
    public ICommand CopiarHaciendaTodasCommand { get; }
    public ICommand CopiarContabilidadTodasCommand { get; }
    public ICommand CopiarHaciendaCommand { get; }
    public ICommand CopiarContabilidadCommand { get; }
    public ICommand UsarPropuestoCommand { get; }
    public ICommand MayorContrasteCommand { get; }
    public ICommand VerApunteCommand { get; }
    public ICommand MayorApunteCommand { get; }
    public ICommand AbrirContrasteCommand { get; }
    public ICommand AbrirRentaCommand { get; }
    public ICommand MayorPartidaCommand { get; }
    public ICommand UsarRentaCalculadaCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public SituacionImpuestoSociedades? Situacion
    {
        get => _situacion;
        private set { _situacion = value; Avisar(nameof(Situacion), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo), nameof(PorQueNoConfirmar)); }
    }

    public string EstadoTexto => _situacion?.Estado switch
    {
        EstadoImpuestoSociedades.Contabilizado => "Contabilizado",
        EstadoImpuestoSociedades.Desactualizado => "Desactualizado",
        _ => "Pendiente",
    };

    public string ClaseEstado => (_situacion?.Estado ?? EstadoImpuestoSociedades.Pendiente).ToString().ToUpperInvariant();

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { Estado: EstadoImpuestoSociedades.Pendiente } => "Sin contabilizar: escribe los ajustes que confirme la gestoría, contrasta las retenciones con Hacienda, revisa la cuota y confirma antes de la regularización",
        { Estado: EstadoImpuestoSociedades.Contabilizado, Guardado: { } g } => $"Cuota líquida de {Formatos.Importe(g.CuotaLiquida)} contabilizada el {Formatos.Fecha(g.FechaHora)}" +
            (g.Operador is { Length: > 0 } o ? $" por {o}" : "") + (g.Resultado >= 0m ? $"; {Formatos.Importe(g.Resultado)} a ingresar" : $"; {Formatos.Importe(-g.Resultado)} a devolver"),
        var s => $"Hay que repetirlo: {s.Motivo}",
    };

    // ---------- entrada ----------
    public ObservableCollection<FilaAjusteIsVM> Ajustes { get; } = [];
    public IReadOnlyList<string> Clases { get; } = [FilaAjusteIsVM.Permanente, FilaAjusteIsVM.Temporal];
    public IReadOnlyList<string> Sentidos { get; } = [FilaAjusteIsVM.Aumento, FilaAjusteIsVM.Disminucion];
    public FilaAjusteIsVM? AjusteSeleccionado { get => _ajusteSeleccionado; set { if (Fijar(ref _ajusteSeleccionado, value)) CommandManager.InvalidateRequerySuggested(); } }
    public FilaCuentaIsVM? PosibleSeleccionado { get => _posibleSeleccionado; set { if (Fijar(ref _posibleSeleccionado, value)) CommandManager.InvalidateRequerySuggested(); } }
    public FilaRetencionIsVM? RetencionSeleccionada { get => _retencionSeleccionada; set { if (Fijar(ref _retencionSeleccionada, value)) CommandManager.InvalidateRequerySuggested(); } }
    public FilaApunteIsVM? ApunteSeleccionado { get => _apunteSeleccionado; set { if (Fijar(ref _apunteSeleccionado, value)) CommandManager.InvalidateRequerySuggested(); } }
    public FilaPartidaIsVM? PartidaSeleccionada { get => _partidaSeleccionada; set { if (Fijar(ref _partidaSeleccionada, value)) CommandManager.InvalidateRequerySuggested(); } }

    public FilaContrasteIsVM? ContrasteSeleccionado
    {
        get => _contrasteSeleccionado;
        set
        {
            if (!Fijar(ref _contrasteSeleccionado, value)) return;
            ApunteSeleccionado = null;
            Avisar(nameof(TituloDetalle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string BasesNegativas { get => _basesNegativas; set { if (Fijar(ref _basesNegativas, value)) Recalcular(); } }
    public string Bonificaciones { get => _bonificaciones; set { if (Fijar(ref _bonificaciones, value)) Recalcular(); } }
    public string Deducciones { get => _deducciones; set { if (Fijar(ref _deducciones, value)) Recalcular(); } }
    public string Credito { get => _credito; set { if (Fijar(ref _credito, value)) Recalcular(); } }
    public string Cuota { get => _cuota; set { if (Fijar(ref _cuota, value)) Recalcular(); } }
    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }
    /// <summary>Renta de viviendas que se aplica en la bonificación; vacía, la calculada.</summary>
    public string RentaAplicar { get => _renta; set { if (Fijar(ref _renta, value ?? "")) Recalcular(); } }

    public string AyudaBonificacion => _propuesta is not { } p ? "" :
        $"Bonificación por alquiler de viviendas. Vacía: la propuesta ({Formatos.Importe(p.BonificacionPropuesta)}) = cuota íntegra × renta de viviendas ({Formatos.Importe(p.RentaViviendas)}) / base × " +
        $"{CalculoImpuestoSociedades.Porcentaje(_datos?.Parametros.Bonificacion ?? 0m)}. Escribe la de la gestoría si es otra";
    public string AyudaRenta => _propuesta is not { } p ? "" :
        $"Renta de las viviendas arrendadas que se bonifica. Vacía: la calculada ({Formatos.Importe(p.RentaViviendasCalculada)}) = ingresos de viviendas − sus gastos directos − " +
        "los gastos generales en proporción a los ingresos ordinarios. Detalle…: de dónde sale cada cifra";
    public string AyudaCuota => _propuesta is not { } p ? "" :
        $"Cuota líquida que se contabiliza en la 6300. Vacía: la calculada ({Formatos.Importe(p.CuotaCalculada)}). Escribe la de la gestoría solo si no cuadra; pide motivo";
    public string AyudaCredito => _propuesta is not { } p ? "" :
        $"Crédito por pérdidas activado (saldo de la 4745: {Formatos.Importe(_datos?.SaldoCreditoPerdidas ?? 0m)}) que se aplica: 6301 al Debe y 4745 al Haber. Vacío: el propuesto ({Formatos.Importe(p.CreditoPropuesto)})";

    // ---------- propuesta ----------
    public PropuestaImpuestoSociedades? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            ActualizarContraste();
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Calculo), nameof(PosiblesAjustes), nameof(HayPosiblesAjustes), nameof(Retenciones),
                nameof(TotalRetenciones), nameof(TotalSaldos), nameof(TotalAeat), nameof(TotalContable), nameof(ResumenAeat), nameof(ResumenContraste), nameof(PorQueNoContraste),
                nameof(HayPorQueNoContraste), nameof(RentaCalculada), nameof(ResumenRenta), nameof(Partidas), nameof(HayDetalleRenta),
                nameof(Lineas), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber), nameof(Resultado), nameof(TituloPropuesta),
                nameof(AyudaBonificacion), nameof(AyudaRenta), nameof(AyudaCuota), nameof(AyudaCredito), nameof(PorQueNoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(TextoSinPropuesta), nameof(HayError), nameof(PorQueNoConfirmar)); } }
    public bool HayError => _errorPropuesta is not null && _datos is not null;
    public string TextoSinPropuesta => _errorPropuesta ?? "Calculando…";

    public string TituloPropuesta => _propuesta is null ? "" :
        $"Impuesto sobre Sociedades {_propuesta.Ejercicio} · asiento del {Formatos.Fecha(_propuesta.Fecha)}" + (_situacion?.Guardado?.IdAsiento is not null ? " (repetirlo sustituye el asiento actual)" : "");

    public IReadOnlyList<FilaCalculoIsVM> Calculo => _propuesta is not { } p ? [] :
    [
        new("Resultado contable antes del impuesto", Formatos.Importe(p.ResultadoContable),
            "Grupos 6 y 7 del ejercicio sin las cuentas del impuesto (630, 633 y 638) ni la regularización o el cierre: positivo, beneficio", true),
        new("Ajustes permanentes · aumentos", Formatos.Importe(p.AumentosPermanentes), "Suma de los ajustes permanentes que aumentan el resultado (multas, sanciones…)", Sangrada: true),
        new("Ajustes permanentes · disminuciones", Formatos.Importe(p.DisminucionesPermanentes), "Suma de los ajustes permanentes que lo disminuyen (exenciones del art. 21…)", Sangrada: true),
        new("Resultado contable ajustado", Formatos.Importe(p.ResultadoAjustado), "Resultado contable ± ajustes permanentes", true),
        new("Ajustes temporales · aumentos", Formatos.Importe(p.AumentosTemporales), "Suma de los ajustes temporales que aumentan la base", Sangrada: true),
        new("Ajustes temporales · disminuciones", Formatos.Importe(p.DisminucionesTemporales), "Suma de los ajustes temporales que la disminuyen", Sangrada: true),
        new("Base imponible previa", Formatos.Importe(p.BasePrevia), "Resultado ajustado ± ajustes temporales", true),
        new("Compensación de bases negativas", Formatos.Importe(p.BasesNegativas), "Pérdidas fiscales de años anteriores que se restan (se escriben abajo)", Sangrada: true),
        new("Base imponible", Formatos.Importe(p.BaseImponible), "Base previa − bases negativas", true),
        new("Cuota íntegra", Formatos.Importe(p.CuotaIntegra), TextoTipos(p) + $"; tipo medio {CalculoImpuestoSociedades.Porcentaje(Math.Round(p.TipoMedio, 4))}", true),
        new("Bonificaciones", Formatos.Importe(p.Bonificaciones), $"Bonificación por alquiler de viviendas sobre una renta de {Formatos.Importe(p.RentaViviendas)} (abajo; vacía, la propuesta)", Sangrada: true),
        new("Deducciones", Formatos.Importe(p.Deducciones), "Deducciones (doble imposición, inversión…), se escriben abajo", Sangrada: true),
        new("Cuota líquida calculada", Formatos.Importe(p.CuotaCalculada), "Cuota íntegra − bonificaciones − deducciones, nunca negativa"),
        new("Cuota líquida a contabilizar (6300)", Formatos.Importe(p.CuotaLiquida), p.CuotaFijada ? "Fijada a mano: no es la calculada" : "La calculada", true),
        new("Retenciones y pagos a cuenta (473)", Formatos.Importe(p.TotalRetenciones),
            "Los valores para el IS del contraste de retenciones con Hacienda (detalle a la derecha)", Sangrada: true),
        new(p.Resultado >= 0m ? "Líquido a ingresar" : "Líquido a devolver", Formatos.Importe(Math.Abs(p.Resultado)),
            p.Resultado >= 0m ? "Cuota líquida − retenciones: a la 4752" : "Retenciones − cuota líquida: a la 4709", true),
    ];

    private static string TextoTipos(PropuestaImpuestoSociedades p) => p.LimiteTramo is decimal l && p.TipoTramo is decimal t
        ? $"Hasta {Formatos.Importe(l)} al {CalculoImpuestoSociedades.Porcentaje(t)} y el resto al {CalculoImpuestoSociedades.Porcentaje(p.TipoGeneral)}"
        : $"Toda la base al {CalculoImpuestoSociedades.Porcentaje(p.TipoGeneral)}";

    public IReadOnlyList<FilaCuentaIsVM> PosiblesAjustes => _propuesta?.PosiblesAjustes
        .Select(x => new FilaCuentaIsVM(x.Cuenta, x.Nombre, Formatos.Plural(x.Apuntes, "apunte", "apuntes") + (_propuesta.Entrada.Ajustes.Any(a => a.Cuenta == x.Cuenta) ? " · ajustada" : ""),
            Formatos.Importe(x.EsGasto ? x.Saldo : -x.Saldo), x)).ToList() ?? [];
    public bool HayPosiblesAjustes => _propuesta is { PosiblesAjustes.Count: > 0 };

    // ---------- retenciones por cuenta (pantalla del impuesto) ----------
    /// <summary>Resumen por 473 de lo que decide el contraste: saldo, lo que dice Hacienda y lo que se deduce.</summary>
    public IReadOnlyList<FilaRetencionIsVM> Retenciones => _propuesta?.Retenciones.Select(r => new FilaRetencionIsVM(r.Cuenta, r.Nombre,
        string.Join(Environment.NewLine, r.Pagadores), Formatos.Importe(r.Saldo), r.SegunAeat is decimal a ? Formatos.Importe(a) : "", Formatos.Importe(r.Deducido))).ToList() ?? [];
    public string TotalRetenciones => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalRetenciones);
    public string TotalSaldos => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalSaldos473);

    // ---------- contraste de retenciones ----------
    /// <summary>Filas del contraste, que se conservan mientras se escribe (para no perder la fila en edición).</summary>
    public ObservableCollection<FilaContrasteIsVM> Contraste { get; } = [];
    public bool HayDatosAeat => _aeat.Count > 0;
    /// <summary>Las 473 del ejercicio para las filas que solo conoce Hacienda.</summary>
    public IReadOnlyList<string> Cuentas473 => [.. (_datos?.Retenciones ?? []).Select(r => r.Cuenta).Concat(_aeat.Select(a => a.Cuenta).OfType<string>())
        .Distinct().OrderBy(c => c, StringComparer.Ordinal)];
    public string BasesNegativasAeat { get => _basesAeat; set { if (Fijar(ref _basesAeat, value ?? "")) Recalcular(); } }
    public string ResultadoPresentado { get => _presentado; set { if (Fijar(ref _presentado, value ?? "")) Recalcular(); } }
    public string TotalAeat => _propuesta is null || !HayDatosAeat ? "" : Formatos.Importe(_propuesta.FilasContraste.Sum(f => f.Hacienda ?? 0m));
    public string TotalContable => _propuesta is null ? "" : Formatos.Importe(_propuesta.FilasContraste.Sum(f => f.Contable));
    public string ResumenAeat => !HayDatosAeat
        ? "Sin datos fiscales: se propone lo contabilizado. Pulsa «Abrir la Sede», guarda la página de los datos fiscales como HTML e impórtala."
        : $"{Formatos.Plural(_aeat.Count, "línea", "líneas")} de Hacienda importadas ({TotalAeat}). Se propone lo de Hacienda; si usas otro valor, justifícalo.";
    public string ResumenContraste
    {
        get
        {
            if (_propuesta is not { } p) return "";
            var f = p.FilasContraste;
            var distintas = f.Count(x => x.Hacienda is not null && x.Diferencia != 0m);
            return $"{Formatos.Plural(f.Count, "pagador", "pagadores")}" + (HayDatosAeat ? $" · {Formatos.Plural(distintas, "no cuadra", "no cuadran")} con Hacienda" : "") +
                $" · {Formatos.Plural(f.Count(x => x.Escrito), "valor escrito", "valores escritos")} · para el IS: {Formatos.Importe(p.TotalRetenciones)}";
        }
    }
    public string? PorQueNoContraste => _propuesta?.PorQueNoContraste;
    public bool HayPorQueNoContraste => PorQueNoContraste is not null;
    public string TituloDetalle => _contrasteSeleccionado is { } f ? $"Detalle de {f.Pagador}" : "Elige un pagador para ver sus apuntes y lo que dice Hacienda";

    private IEnumerable<FilaContrasteIsVM> Seleccion() => _contrasteSeleccionado is { } f ? [f] : [];

    /// <summary>Pone el valor para el IS de las filas con lo de Hacienda o con lo contabilizado (vacío cuando coincide con lo propuesto).</summary>
    private void Copiar(IEnumerable<FilaContrasteIsVM> filas, bool hacienda)
    {
        _cargandoEntrada = true;
        try
        {
            foreach (var f in filas.ToList())
            {
                if (f.Datos is not { } d) continue;
                var v = hacienda ? d.Hacienda ?? 0m : Math.Max(0m, d.Contable);
                f.Valor = v == d.Propuesto ? "" : Formatos.Numero(v);
            }
        }
        finally { _cargandoEntrada = false; }
        Recalcular();
    }

    private void AlEscribirContraste(FilaContrasteIsVM f)
    {
        var cuenta = f.SoloHacienda ? f.Cuenta.Trim() : "";
        if (f.Valor.Trim().Length == 0 && f.Justificacion.Trim().Length == 0 && cuenta.Length == 0) _valores.Remove(f.Clave);
        else _valores[f.Clave] = (f.Valor, f.Justificacion, cuenta);
        Recalcular();
    }

    /// <summary>Pone las filas del contraste al día con la propuesta: actualiza las que siguen y solo añade o quita las que cambian.</summary>
    private void ActualizarContraste()
    {
        var nuevas = _propuesta?.FilasContraste ?? [];
        if (_propuesta is null && _datos is not null) return; // un error de lectura deja las filas como están para corregir lo escrito
        var actuales = Contraste.ToDictionary(f => f.Clave, StringComparer.Ordinal);
        if (!nuevas.Select(r => r.Clave).SequenceEqual(Contraste.Select(f => f.Clave)))
        {
            var elegida = _contrasteSeleccionado?.Clave;
            Contraste.Clear();
            foreach (var r in nuevas)
            {
                var f = actuales.GetValueOrDefault(r.Clave) ?? new FilaContrasteIsVM(r.Clave, AlEscribirContraste);
                var (v, j, c) = _valores.GetValueOrDefault(r.Clave, ("", "", ""));
                f.Cargar(v, j, c);
                Contraste.Add(f);
            }
            Avisar(nameof(Cuentas473));
            ContrasteSeleccionado = elegida is null ? null : Contraste.FirstOrDefault(f => f.Clave == elegida);
        }
        foreach (var (f, r) in Contraste.Zip(nuevas)) f.Actualizar(r);
    }

    // ---------- renta de viviendas ----------
    public string RentaCalculada => _propuesta is null ? "" : Formatos.Importe(_propuesta.RentaViviendasCalculada);
    public bool HayDetalleRenta => _propuesta?.Viviendas is not null;
    public IReadOnlyList<FilaPartidaIsVM> Partidas => _propuesta?.Viviendas?.Partidas.Select(p => new FilaPartidaIsVM(p.Clase, p.Centro, p.Cuenta, p.NombreCuenta,
        Formatos.Importe(p.Importe), p.Apuntes.ToString(System.Globalization.CultureInfo.CurrentCulture))).ToList() ?? [];

    public IReadOnlyList<FilaResumenRentaVM> ResumenRenta => _propuesta?.Viviendas is not { } v ? [] :
    [
        new("Ingresos de viviendas", Formatos.Importe(v.Ingresos), "Grupo 7 de los centros cuya actividad tiene la bonificación por alquiler de viviendas"),
        new("− Gastos directos", Formatos.Importe(v.GastosDirectos), "Grupo 6 de esos mismos centros"),
        new("Gastos generales", Formatos.Importe(v.GastosIndirectos),
            "Grupo 6 sin centro, sin actividad o de la actividad general (con los gastos financieros y las amortizaciones), sin las cuentas del impuesto"),
        new("Ingresos ordinarios de viviendas", Formatos.Importe(v.IngresosOrdinariosViviendas), "Cuentas 70 a 75 de los centros de viviendas"),
        new("Ingresos ordinarios de la sociedad", Formatos.Importe(v.IngresosOrdinariosTotales), "Cuentas 70 a 75 de toda la sociedad (consulta V0805-06: el denominador es la facturación global)"),
        new("Proporción", CalculoImpuestoSociedades.Porcentaje(Math.Round(v.Proporcion, 4)), "Ingresos ordinarios de viviendas / ingresos ordinarios de la sociedad"),
        new("− Gastos generales imputados", Formatos.Importe(v.IndirectosImputados), "Gastos generales × proporción"),
        new("Renta de viviendas calculada", Formatos.Importe(v.Renta), "Ingresos − gastos directos − gastos generales imputados (art. 49.2 LIS)", true),
        new("Renta que se aplica", Formatos.Importe(_propuesta.RentaViviendas), _renta.Trim().Length > 0 ? "Escrita a mano" : "La calculada", true),
    ];

    // ---------- asiento ----------
    public IReadOnlyList<FilaLineaIsVM> Lineas => _propuesta?.Lineas.Select(l => new FilaLineaIsVM(l.Cuenta, l.Concepto, Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber))).ToList() ?? [];
    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);

    public string Resultado => _propuesta is not { } p ? ""
        : p.Resultado > 0m ? $"A ingresar: {Formatos.Importe(p.Resultado)}"
        : p.Resultado < 0m ? $"A devolver: {Formatos.Importe(-p.Resultado)}"
        : "Ni a ingresar ni a devolver";

    public IReadOnlyList<string> Avisos => _propuesta?.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;

    private bool Repetir => _situacion is { Estado: not EstadoImpuestoSociedades.Pendiente };
    private string? PorQueMotivo => _situacion is null ? null : ServicioImpuestoSociedades.PorQueMotivo(_situacion.EjercicioTerminado, Repetir, _propuesta?.CuotaFijada ?? false);
    public bool PideMotivo => PorQueMotivo is not null;
    public string AyudaMotivo => (PorQueMotivo ?? "") + " El motivo queda en el asiento y en su historial.";
    public string TextoConfirmar => Repetir ? "Repetir el impuesto" : "Contabilizar el impuesto";

    public string? PorQueNoConfirmar =>
        _datos is null ? "Espera a que se lea la contabilidad del ejercicio."
        : _errorPropuesta is not null ? _errorPropuesta
        : _propuesta is null ? "Espera a que se calcule la propuesta."
        : _propuesta.PorQueNoContraste is { } c ? c
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    // ---------- carga y cálculo ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            CargarEntrada(_situacion!.Guardado?.Entrada ?? EntradaImpuestoSociedades.Vacia, recalcular: false);
            _motivo = "";
            Avisar(nameof(Motivo));
            Estado = EstadoVista.Contenido;
            await LeerDatosAsync();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer el Impuesto sobre Sociedades del ejercicio: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    /// <summary>Vuelve a leer la contabilidad (resultado, 473 y sus apuntes, renta de viviendas, posibles ajustes y parámetros) y recalcula con lo escrito.</summary>
    public async Task LeerDatosAsync()
    {
        Ocupado = true;
        try
        {
            _datos = await _ctx.Servicio.DatosAsync(_ctx.Trabajo);
            Avisar(nameof(Cuentas473));
            Recalcular();
        }
        catch (Exception ex)
        {
            _datos = null;
            Propuesta = null;
            ErrorPropuesta = $"No he podido leer la contabilidad del ejercicio: {ex.Message}";
        }
        finally { Ocupado = false; }
    }

    private void CargarEntrada(EntradaImpuestoSociedades e, bool recalcular = true)
    {
        _cargandoEntrada = true;
        try
        {
            Ajustes.Clear();
            foreach (var a in e.Ajustes) Ajustes.Add(FilaAjusteIsVM.De(a));
            _basesNegativas = e.BasesNegativas == 0m ? "" : Formatos.Numero(e.BasesNegativas);
            _bonificaciones = e.Bonificaciones is decimal b ? Formatos.Numero(b) : "";
            _deducciones = e.Deducciones == 0m ? "" : Formatos.Numero(e.Deducciones);
            _credito = e.CreditoPerdidas is decimal c ? Formatos.Numero(c) : "";
            _cuota = e.CuotaLiquida is decimal q ? Formatos.Numero(q) : "";
            _renta = e.RentaViviendas is decimal r ? Formatos.Numero(r) : "";
            _basesAeat = e.BasesNegativasAeat is decimal ba ? Formatos.Numero(ba) : "";
            _presentado = e.ResultadoPresentado is decimal rp ? Formatos.Numero(rp) : "";
            _aeat = [.. e.RetencionesAeat ?? []];
            _valores.Clear();
            foreach (var v in e.Valores ?? [])
                _valores[v.Clave] = (v.Importe is decimal i ? Formatos.Numero(i) : "", v.Justificacion ?? "", v.Cuenta ?? "");
            foreach (var f in Contraste)
            {
                var (vv, j, cc) = _valores.GetValueOrDefault(f.Clave, ("", "", ""));
                f.Cargar(vv, j, cc);
            }
            Avisar(nameof(BasesNegativas), nameof(Bonificaciones), nameof(Deducciones), nameof(Credito), nameof(Cuota), nameof(RentaAplicar), nameof(BasesNegativasAeat),
                nameof(ResultadoPresentado), nameof(HayDatosAeat), nameof(Cuentas473));
        }
        finally { _cargandoEntrada = false; }
        if (recalcular) Recalcular();
    }

    private void AlCambiarAjuste(object? sender, PropertyChangedEventArgs e) => Recalcular();

    /// <summary>Lo escrito como entrada del cálculo, o el motivo por el que no se puede leer.</summary>
    private (EntradaImpuestoSociedades? Entrada, string? Error) LeerEntrada()
    {
        var ajustes = new List<AjusteImpuestoSociedades>();
        foreach (var f in Ajustes)
        {
            if (f.Leer() is not { } a) return (null, $"El importe del ajuste «{f.Descripcion}» no es un importe válido.");
            ajustes.Add(a);
        }
        if (!Leer(_basesNegativas, out var bases)) return (null, "La compensación de bases negativas no es un importe válido.");
        if (!Leer(_bonificaciones, out var bonif)) return (null, "Las bonificaciones no son un importe válido.");
        if (!Leer(_deducciones, out var deduc)) return (null, "Las deducciones no son un importe válido.");
        if (!Leer(_credito, out var credito)) return (null, "El crédito por pérdidas no es un importe válido.");
        if (!Leer(_cuota, out var cuota)) return (null, "La cuota líquida no es un importe válido.");
        if (!Leer(_renta, out var renta)) return (null, "La renta de viviendas a aplicar no es un importe válido.");
        if (!Leer(_basesAeat, out var basesAeat)) return (null, "Las bases negativas según Hacienda no son un importe válido.");
        if (!Leer(_presentado, out var presentado)) return (null, "El resultado presentado no es un importe válido.");
        var valores = new List<ValorRetencion>();
        foreach (var (clave, (texto, justificacion, cuenta)) in _valores.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!Leer(texto, out var v))
                return (null, $"El valor para el IS de {Contraste.FirstOrDefault(f => f.Clave == clave)?.Pagador ?? clave} no es un importe válido.");
            valores.Add(new ValorRetencion(clave, v, string.IsNullOrWhiteSpace(justificacion) ? null : justificacion.Trim(), cuenta.Trim().Length == 0 ? null : cuenta.Trim()));
        }
        return (new EntradaImpuestoSociedades(ajustes, bases ?? 0m, bonif, deduc ?? 0m, credito, cuota, _aeat.Count == 0 ? null : _aeat, basesAeat, presentado,
            valores.Count == 0 ? null : valores, renta), null);
    }

    private static bool Leer(string? texto, out decimal? valor)
    {
        valor = null;
        var t = (texto ?? "").Replace("€", "").Trim();
        if (t.Length == 0) return true;
        if (!Formatos.LeerImporte(t, out var v)) return false;
        valor = Math.Round(v, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Recalcula la propuesta con los datos leídos y lo escrito, sin volver a la BD.</summary>
    private void Recalcular()
    {
        if (_cargandoEntrada || _datos is null) return;
        var (entrada, error) = LeerEntrada();
        error ??= entrada is null ? null : CalculoImpuestoSociedades.Validar(entrada);
        if (error is not null || entrada is null)
        {
            ErrorPropuesta = error;
            Propuesta = null;
            return;
        }
        ErrorPropuesta = null;
        Propuesta = _ctx.Servicio.Calcular(_ctx.Trabajo, _datos, entrada);
    }

    private void AnadirPosible()
    {
        if (_posibleSeleccionado is not { } f) return;
        if (Ajustes.Any(a => a.Cuenta == f.Cuenta))
        {
            _ctx.Avisar($"La {f.Cuenta} ya está en los ajustes.");
            return;
        }
        var saldo = f.Datos.Saldo;
        // Saldo deudor (un gasto que no es deducible) aumenta el resultado; saldo acreedor (un ingreso exento) lo disminuye.
        var aumenta = saldo > 0m;
        Ajustes.Add(new FilaAjusteIsVM
        {
            Clase = FilaAjusteIsVM.Permanente, Sentido = aumenta ? FilaAjusteIsVM.Aumento : FilaAjusteIsVM.Disminucion,
            Descripcion = $"{f.Cuenta} {f.Nombre}".Trim(), Importe = Formatos.Numero(Math.Abs(saldo)), Cuenta = f.Cuenta,
        });
    }

    private void AbrirMayor(string? cuenta)
    {
        if (cuenta is not null) _ctx.AbrirMayor?.Invoke([cuenta]);
    }

    private async Task AbrirSedeAsync()
    {
        try { _ctx.AbrirEnlace?.Invoke(await _ctx.Servicio.UrlDatosFiscalesAsync()); }
        catch (Exception ex) { _dialogos.Error("No he podido abrir la Sede de la AEAT", ex.Message); }
    }

    private void QuitarDatosAeat()
    {
        if (_dialogos.Preguntar("Quitar los datos fiscales", "Se quitan las líneas de Hacienda, las bases negativas pendientes y el resultado presentado. " +
                "Lo escrito en el contraste se conserva; sin datos fiscales se propone lo contabilizado.", [],
                new OpcionMensaje("Quitar los datos", "Quita lo importado de Hacienda", Principal: true), new OpcionMensaje("Cancelar", "No quita nada", Cancelar: true)) != 0)
            return;
        _aeat = [];
        _basesAeat = "";
        _presentado = "";
        Avisar(nameof(HayDatosAeat), nameof(BasesNegativasAeat), nameof(ResultadoPresentado), nameof(Cuentas473));
        Recalcular();
    }

    /// <summary>Lee el HTML de los datos fiscales y sustituye las líneas de Hacienda, las bases negativas pendientes y el resultado presentado.</summary>
    private async Task ImportarDatosFiscalesAsync()
    {
        if (_datos is not { } datos || _ctx.ElegirDatosFiscales?.Invoke() is not { Length: > 0 } ruta) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ImportarDatosFiscalesAsync(_ctx.Trabajo, await System.IO.File.ReadAllBytesAsync(ruta), datos);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se han importado los datos fiscales", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var d = r.Valor;
            _aeat = [.. d.Retenciones];
            _basesAeat = d.BasesNegativas is decimal b ? Formatos.Numero(b) : "";
            _presentado = d.ResultadoPresentado is decimal p ? Formatos.Numero(p) : "";
            Avisar(nameof(HayDatosAeat), nameof(BasesNegativasAeat), nameof(ResultadoPresentado), nameof(Cuentas473));
            Recalcular();
            var noCuadran = _propuesta?.FilasContraste.Count(f => f.Hacienda is not null && f.Diferencia != 0m) ?? 0;
            _ctx.Avisar($"Datos fiscales {d.Ejercicio} importados: {Formatos.Plural(d.Retenciones.Count, "línea", "líneas")} de retenciones y pagos a cuenta ({Formatos.Importe(d.Retenciones.Sum(x => x.Importe))})" +
                (noCuadran > 0 ? $"; {Formatos.Plural(noCuadran, "pagador no cuadra", "pagadores no cuadran")} con la contabilidad: revísalos en el contraste." : ".") +
                (d.ArrendamientoViviendas == false && (_datos?.Parametros.Bonificacion ?? 0m) > 0m ? " Hacienda no le reconoce el carácter de arrendamiento de viviendas: revisa la bonificación." : "") +
                (d.Avisos.Count > 0 ? " " + string.Join(" ", d.Avisos) : ""));
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se han importado los datos fiscales", ex.Message);
        }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p) return;
        var detalles = new List<string> { Resultado, $"Cuota líquida: {Formatos.Importe(p.CuotaLiquida)}; retenciones y pagos a cuenta: {Formatos.Importe(p.TotalRetenciones)}." };
        if (p.Avisos.Count > 0) detalles.Add($"Hay {Formatos.Plural(p.Avisos.Count, "aviso", "avisos")} en la propuesta: revísalos antes.");
        var mensaje = p.Lineas.Count > 0
            ? $"Se graba el asiento del {Formatos.Fecha(p.Fecha)} con {Formatos.Plural(p.Lineas.Count, "línea", "líneas")}, tal como se ve en la propuesta, y se guarda el cálculo con sus ajustes y el contraste de retenciones."
            : "No hay cuota ni retenciones: solo se guarda el cálculo" + (_situacion?.Guardado?.IdAsiento is not null ? " y se borra el asiento anterior." : ".");
        if (_dialogos.Preguntar(TextoConfirmar, mensaje, detalles,
                new OpcionMensaje(TextoConfirmar, "Graba el asiento del impuesto y guarda el cálculo", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, p.Entrada, string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha contabilizado el impuesto", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            _ctx.Avisar($"Impuesto sobre Sociedades {p.Ejercicio}: cuota líquida de {Formatos.Importe(p.CuotaLiquida)}" +
                (c.IdAsiento is int id ? $" en el asiento {id}." : " (sin asiento).") + $" {Resultado}." +
                (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : ""));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha contabilizado el impuesto", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        var motivo = _dialogos.PedirMotivo($"Deshacer el Impuesto sobre Sociedades de {_ctx.Trabajo.Ejercicio}",
            "Se borra su asiento y el cálculo guardado con sus ajustes. Las 473 vuelven a quedar con saldo.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha deshecho el impuesto", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshecho el Impuesto sobre Sociedades de {_ctx.Trabajo.Ejercicio}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha deshecho el impuesto", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsientoAsync(int? id)
    {
        if (id is int i && i > 0 && _ctx.VerAsiento is { } ver) await ver(i);
    }
}
