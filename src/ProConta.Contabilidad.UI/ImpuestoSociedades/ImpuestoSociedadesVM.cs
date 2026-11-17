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

/// <summary>Una 473: su saldo, lo que conoce la AEAT, lo propuesto y el importe a deducir que escribe el operador (vacío: el propuesto).</summary>
public sealed class FilaRetencionIsVM(string cuenta, Action<string, string> alEscribir) : ModeloVista
{
    private string _deducir = "", _nombre = "", _pagadores = "", _saldo = "", _segunAeat = "", _propuesto = "", _deducido = "";
    private bool _escrito;

    public string Cuenta { get; } = cuenta;
    public string Nombre { get => _nombre; private set => Fijar(ref _nombre, value); }
    public string Pagadores { get => _pagadores; private set => Fijar(ref _pagadores, value); }
    public string Saldo { get => _saldo; private set => Fijar(ref _saldo, value); }
    public string SegunAeat { get => _segunAeat; private set => Fijar(ref _segunAeat, value); }
    public string Propuesto { get => _propuesto; private set => Fijar(ref _propuesto, value); }
    public string Deducido { get => _deducido; private set => Fijar(ref _deducido, value); }
    public bool Escrito { get => _escrito; private set => Fijar(ref _escrito, value); }

    /// <summary>Lo que escribe el operador; vacío, lo propuesto.</summary>
    public string Deducir { get => _deducir; set { if (Fijar(ref _deducir, value ?? "")) alEscribir(Cuenta, _deducir); } }

    internal void Cargar(string deducir) { _deducir = deducir; Avisar(nameof(Deducir)); }

    internal void Actualizar(RetencionPropuesta r)
    {
        Nombre = r.Nombre;
        Pagadores = string.Join(", ", r.Pagadores);
        Saldo = Formatos.Importe(r.Saldo);
        SegunAeat = r.SegunAeat is decimal a ? Formatos.Importe(a) : "";
        Propuesto = Formatos.Importe(r.Propuesto);
        Deducido = Formatos.Importe(r.Deducido);
        Escrito = r.Escrito;
    }
}

/// <summary>Línea de los datos fiscales de la AEAT (importada o escrita): modelo, pagador, importe y la 473 a la que corresponde.</summary>
public sealed class FilaAeatIsVM : ModeloVista
{
    private string _nombre = "", _importe = "", _cuenta = "";

    public string Modelo { get; init; } = "";
    public string? Nif { get; init; }
    public string Nombre { get => _nombre; set => Fijar(ref _nombre, value ?? ""); }
    public string Importe { get => _importe; set => Fijar(ref _importe, value ?? ""); }
    /// <summary>473 a la que corresponde; vacía: sin asignar (no se deduce).</summary>
    public string Cuenta { get => _cuenta; set => Fijar(ref _cuenta, value ?? ""); }
    public string Pagador => Nif is { Length: > 0 } n ? $"{Nombre} · {n}" : Nombre;

    public static FilaAeatIsVM De(RetencionAeat r) => new() { Modelo = r.Modelo, Nif = r.Nif, _nombre = r.Nombre, _importe = Formatos.Numero(r.Importe), _cuenta = r.Cuenta ?? "" };

    /// <summary>La línea escrita; null si el importe no se puede leer.</summary>
    public RetencionAeat? Leer() =>
        Formatos.LeerImporte(_importe.Replace("€", "").Trim().Length == 0 ? "0" : _importe.Replace("€", "").Trim(), out var v)
            ? new RetencionAeat(Modelo, Nif, _nombre.Trim(), Math.Round(v, 2, MidpointRounding.AwayFromZero), _cuenta.Trim().Length == 0 ? null : _cuenta.Trim())
            : null;
}

/// <summary>Línea del asiento propuesto.</summary>
public sealed record FilaLineaIsVM(string Cuenta, string Concepto, string Debe, string Haber);

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades): el cálculo en el
/// orden de la hoja de la gestoría, los ajustes que escribe el operador (con los posibles ajustes para validar y las 473 con sus
/// pagadores) y el asiento del 31/12. Lee la contabilidad una vez y recalcula al escribir; los botones del pie confirman exactamente lo
/// que se ve. El menú contextual de cada tabla tiene lo que actúa sobre su fila.
/// </summary>
public sealed class ImpuestoSociedadesVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoImpuestoSociedades _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado, _cargandoEntrada;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionImpuestoSociedades? _situacion;
    private DatosImpuestoSociedades? _datos;
    private PropuestaImpuestoSociedades? _propuesta;
    private string? _errorPropuesta;
    private string _basesNegativas = "", _bonificaciones = "", _deducciones = "", _credito = "", _cuota = "", _motivo = "", _basesAeat = "", _presentado = "";
    private FilaAjusteIsVM? _ajusteSeleccionado;
    private FilaCuentaIsVM? _posibleSeleccionado;
    private FilaRetencionIsVM? _retencionSeleccionada;
    private FilaAeatIsVM? _aeatSeleccionada;
    private readonly Dictionary<string, string> _deducir = new(StringComparer.Ordinal);

    public ImpuestoSociedadesVM(ContextoImpuestoSociedades ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        Ajustes.CollectionChanged += (_, e) =>
        {
            foreach (FilaAjusteIsVM f in e.NewItems ?? Array.Empty<FilaAjusteIsVM>()) f.PropertyChanged += AlCambiarAjuste;
            foreach (FilaAjusteIsVM f in e.OldItems ?? Array.Empty<FilaAjusteIsVM>()) f.PropertyChanged -= AlCambiarAjuste;
            Recalcular();
        };
        DatosAeat.CollectionChanged += (_, e) =>
        {
            foreach (FilaAeatIsVM f in e.NewItems ?? Array.Empty<FilaAeatIsVM>()) f.PropertyChanged += AlCambiarAjuste;
            foreach (FilaAeatIsVM f in e.OldItems ?? Array.Empty<FilaAeatIsVM>()) f.PropertyChanged -= AlCambiarAjuste;
            Avisar(nameof(HayDatosAeat));
            Recalcular();
        };
        AbrirSedeCommand = new ComandoDelegado(() => _ = AbrirSedeAsync(), () => _ctx.AbrirEnlace is not null);
        ImportarDatosFiscalesCommand = new ComandoDelegado(() => _ = ImportarDatosFiscalesAsync(), () => !_ocupado && _datos is not null && _ctx.ElegirDatosFiscales is not null);
        AnadirDatoAeatCommand = new ComandoDelegado(() => DatosAeat.Add(new FilaAeatIsVM { Modelo = "Manual", Nombre = "Retención" }), () => !_ocupado);
        QuitarDatoAeatCommand = new ComandoDelegado(() => { if (_aeatSeleccionada is { } f) DatosAeat.Remove(f); }, () => _aeatSeleccionada is not null && !_ocupado);
        QuitarDatosAeatCommand = new ComandoDelegado(() => { DatosAeat.Clear(); BasesNegativasAeat = ""; ResultadoPresentado = ""; }, () => !_ocupado && (HayDatosAeat || _basesAeat.Length > 0 || _presentado.Length > 0));
        UsarPropuestoCommand = new ComandoDelegado(() => { if (_retencionSeleccionada is { } f) f.Deducir = ""; }, () => _retencionSeleccionada is { Escrito: true });
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        RecalcularCommand = new ComandoDelegado(() => _ = LeerDatosAsync(), () => !_ocupado);
        AnadirAjusteCommand = new ComandoDelegado(() => Ajustes.Add(new FilaAjusteIsVM()), () => !_ocupado);
        QuitarAjusteCommand = new ComandoDelegado(() => { if (_ajusteSeleccionado is { } f) Ajustes.Remove(f); }, () => _ajusteSeleccionado is not null && !_ocupado);
        AnadirPosibleCommand = new ComandoDelegado(AnadirPosible, () => _posibleSeleccionado is not null && !_ocupado);
        MayorPosibleCommand = new ComandoDelegado(() => AbrirMayor(_posibleSeleccionado?.Cuenta), () => _posibleSeleccionado is not null && _ctx.AbrirMayor is not null);
        MayorRetencionCommand = new ComandoDelegado(() => AbrirMayor(_retencionSeleccionada?.Cuenta), () => _retencionSeleccionada is not null && _ctx.AbrirMayor is not null);
        UsarBonificacionCommand = new ComandoDelegado(() => Bonificaciones = "", () => _bonificaciones.Trim().Length > 0);
        UsarCuotaCommand = new ComandoDelegado(() => Cuota = "", () => _cuota.Trim().Length > 0);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(() => { CargarEntrada(_situacion?.Guardado?.Entrada ?? EntradaImpuestoSociedades.Vacia); Motivo = ""; }, () => !_ocupado && _datos is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => _situacion?.Guardado?.IdAsiento is not null && _ctx.VerAsiento is not null);
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
    public void AlCerrar() => _alCerrar?.Invoke();

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
    public ICommand AnadirDatoAeatCommand { get; }
    public ICommand QuitarDatoAeatCommand { get; }
    public ICommand QuitarDatosAeatCommand { get; }
    public ICommand UsarPropuestoCommand { get; }

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
        { Estado: EstadoImpuestoSociedades.Pendiente } => "Sin contabilizar: escribe los ajustes que confirme la gestoría, revisa la cuota y confirma antes de la regularización",
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
    public FilaAeatIsVM? AeatSeleccionada { get => _aeatSeleccionada; set { if (Fijar(ref _aeatSeleccionada, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>Retenciones y pagos a cuenta de los datos fiscales de la AEAT.</summary>
    public ObservableCollection<FilaAeatIsVM> DatosAeat { get; } = [];
    public bool HayDatosAeat => DatosAeat.Count > 0;
    /// <summary>Las 473 del ejercicio para asignar las líneas de la AEAT (vacía: sin asignar).</summary>
    public IReadOnlyList<string> Cuentas473 => ["", .. (_datos?.Retenciones ?? []).Select(r => r.Cuenta).Distinct().OrderBy(c => c, StringComparer.Ordinal)];
    public string BasesNegativasAeat { get => _basesAeat; set { if (Fijar(ref _basesAeat, value ?? "")) Recalcular(); } }
    public string ResultadoPresentado { get => _presentado; set { if (Fijar(ref _presentado, value ?? "")) Recalcular(); } }
    public string TotalAeat => _propuesta?.TotalAeat is decimal t ? Formatos.Importe(t) : "";
    public string ResumenAeat => !HayDatosAeat ? "Sin datos fiscales: se propone el saldo de cada 473. Ábrelos en la Sede, guárdalos como HTML e impórtalos."
        : $"{Formatos.Plural(DatosAeat.Count, "línea", "líneas")} · {TotalAeat} según la AEAT · {Formatos.Plural(DatosAeat.Count(d => d.Cuenta.Trim().Length == 0), "sin cuenta", "sin cuenta")}";

    public string BasesNegativas { get => _basesNegativas; set { if (Fijar(ref _basesNegativas, value)) Recalcular(); } }
    public string Bonificaciones { get => _bonificaciones; set { if (Fijar(ref _bonificaciones, value)) Recalcular(); } }
    public string Deducciones { get => _deducciones; set { if (Fijar(ref _deducciones, value)) Recalcular(); } }
    public string Credito { get => _credito; set { if (Fijar(ref _credito, value)) Recalcular(); } }
    public string Cuota { get => _cuota; set { if (Fijar(ref _cuota, value)) Recalcular(); } }
    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    /// <summary>Las 473 de la propuesta, que se conservan mientras se escribe (para no perder la fila en edición).</summary>
    public ObservableCollection<FilaRetencionIsVM> Retenciones { get; } = [];

    public string AyudaBonificacion => _propuesta is not { } p ? "" :
        $"Bonificación por alquiler de viviendas. Vacía: la propuesta ({Formatos.Importe(p.BonificacionPropuesta)}) = cuota íntegra × renta de viviendas ({Formatos.Importe(p.RentaViviendas)}) / base × " +
        $"{CalculoImpuestoSociedades.Porcentaje(_datos?.Parametros.Bonificacion ?? 0m)}. Escribe la de la gestoría si es otra";
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
            ActualizarRetenciones();
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Calculo), nameof(PosiblesAjustes), nameof(HayPosiblesAjustes), nameof(TotalRetenciones), nameof(TotalSaldos),
                nameof(TotalAeat), nameof(ResumenAeat),
                nameof(Lineas), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber), nameof(Resultado), nameof(TituloPropuesta),
                nameof(AyudaBonificacion), nameof(AyudaCuota), nameof(AyudaCredito), nameof(PorQueNoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo));
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
        new("Bonificaciones", Formatos.Importe(p.Bonificaciones), "Bonificación por alquiler de viviendas (se escribe abajo; vacía, la propuesta)", Sangrada: true),
        new("Deducciones", Formatos.Importe(p.Deducciones), "Deducciones (doble imposición, inversión…), se escriben abajo", Sangrada: true),
        new("Cuota líquida calculada", Formatos.Importe(p.CuotaCalculada), "Cuota íntegra − bonificaciones − deducciones, nunca negativa"),
        new("Cuota líquida a contabilizar (6300)", Formatos.Importe(p.CuotaLiquida), p.CuotaFijada ? "Fijada a mano: no es la calculada" : "La calculada", true),
        new("Retenciones y pagos a cuenta (473)", Formatos.Importe(p.TotalRetenciones),
            (p.TotalAeat is null ? "Saldo deudor de las 473 a 31/12" : "Lo que conoce la AEAT de cada 473") + " salvo lo escrito a mano (detalle a la derecha)", Sangrada: true),
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

    public string TotalRetenciones => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalRetenciones);
    public string TotalSaldos => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalSaldos473);

    /// <summary>Pone las filas de las 473 al día con la propuesta: actualiza las que siguen y solo añade o quita las que cambian.</summary>
    private void ActualizarRetenciones()
    {
        var nuevas = _propuesta?.Retenciones ?? [];
        var actuales = Retenciones.ToDictionary(f => f.Cuenta, StringComparer.Ordinal);
        if (!nuevas.Select(r => r.Cuenta).SequenceEqual(Retenciones.Select(f => f.Cuenta)))
        {
            Retenciones.Clear();
            foreach (var r in nuevas)
            {
                var f = actuales.GetValueOrDefault(r.Cuenta) ?? new FilaRetencionIsVM(r.Cuenta, AlEscribirDeducir);
                f.Cargar(_deducir.GetValueOrDefault(r.Cuenta, ""));
                Retenciones.Add(f);
            }
            Avisar(nameof(Cuentas473));
        }
        foreach (var (f, r) in Retenciones.Zip(nuevas)) f.Actualizar(r);
    }

    private void AlEscribirDeducir(string cuenta, string texto)
    {
        if (texto.Trim().Length == 0) _deducir.Remove(cuenta); else _deducir[cuenta] = texto;
        Recalcular();
    }

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

    /// <summary>Vuelve a leer la contabilidad (resultado, 473, posibles ajustes y parámetros) y recalcula con lo escrito.</summary>
    public async Task LeerDatosAsync()
    {
        Ocupado = true;
        try
        {
            _datos = await _ctx.Servicio.DatosAsync(_ctx.Trabajo);
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
            _basesAeat = e.BasesNegativasAeat is decimal ba ? Formatos.Numero(ba) : "";
            _presentado = e.ResultadoPresentado is decimal rp ? Formatos.Numero(rp) : "";
            DatosAeat.Clear();
            foreach (var r in e.RetencionesAeat ?? []) DatosAeat.Add(FilaAeatIsVM.De(r));
            _deducir.Clear();
            foreach (var d in e.Deducir ?? []) _deducir[d.Cuenta] = Formatos.Numero(d.Importe);
            foreach (var f in Retenciones) f.Cargar(_deducir.GetValueOrDefault(f.Cuenta, ""));
            Avisar(nameof(BasesNegativas), nameof(Bonificaciones), nameof(Deducciones), nameof(Credito), nameof(Cuota), nameof(BasesNegativasAeat), nameof(ResultadoPresentado));
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
        if (!Leer(_basesAeat, out var basesAeat)) return (null, "Las bases negativas según la AEAT no son un importe válido.");
        if (!Leer(_presentado, out var presentado)) return (null, "El resultado presentado no es un importe válido.");
        var aeat = new List<RetencionAeat>();
        foreach (var f in DatosAeat)
        {
            if (f.Leer() is not { } a) return (null, $"El importe de la AEAT de «{f.Nombre}» no es un importe válido.");
            aeat.Add(a);
        }
        var deducir = new List<RetencionDeducida>();
        foreach (var (cuenta, texto) in _deducir.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!Leer(texto, out var v)) return (null, $"El importe a deducir de la {cuenta} no es un importe válido.");
            if (v is decimal d) deducir.Add(new(cuenta, d));
        }
        return (new EntradaImpuestoSociedades(ajustes, bases ?? 0m, bonif, deduc ?? 0m, credito, cuota, aeat.Count == 0 ? null : aeat, basesAeat, presentado,
            deducir.Count == 0 ? null : deducir), null);
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

    /// <summary>Lee el HTML de los datos fiscales y sustituye las líneas de la AEAT, las bases negativas pendientes y el resultado presentado.</summary>
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
            _cargandoEntrada = true;
            try
            {
                DatosAeat.Clear();
                foreach (var x in d.Retenciones) DatosAeat.Add(FilaAeatIsVM.De(x));
                _basesAeat = d.BasesNegativas is decimal b ? Formatos.Numero(b) : "";
                _presentado = d.ResultadoPresentado is decimal p ? Formatos.Numero(p) : "";
                Avisar(nameof(BasesNegativasAeat), nameof(ResultadoPresentado));
            }
            finally { _cargandoEntrada = false; }
            Recalcular();
            var sinCuenta = d.Retenciones.Count(x => x.Cuenta is null);
            _ctx.Avisar($"Datos fiscales {d.Ejercicio} importados: {Formatos.Plural(d.Retenciones.Count, "línea", "líneas")} de retenciones y pagos a cuenta ({Formatos.Importe(d.Retenciones.Sum(x => x.Importe))})" +
                (sinCuenta > 0 ? $", {Formatos.Plural(sinCuenta, "sin cuenta 473", "sin cuenta 473")}: elígela en la tabla." : ".") +
                (d.ArrendamientoViviendas == false && (_datos?.Parametros.Bonificacion ?? 0m) > 0m ? " La AEAT no le reconoce el carácter de arrendamiento de viviendas: revisa la bonificación." : "") +
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
            ? $"Se graba el asiento del {Formatos.Fecha(p.Fecha)} con {Formatos.Plural(p.Lineas.Count, "línea", "líneas")}, tal como se ve en la propuesta, y se guarda el cálculo con sus ajustes."
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

    private async Task VerAsientoAsync()
    {
        if (_situacion?.Guardado?.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }
}
