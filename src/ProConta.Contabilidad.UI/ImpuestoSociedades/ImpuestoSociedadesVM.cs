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

/// <summary>Subcuenta con saldo (473 o posible ajuste).</summary>
public sealed record FilaCuentaIsVM(string Cuenta, string Nombre, string Detalle, string Saldo, SaldoCuentaImpuesto Datos);

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
    private string _basesNegativas = "", _bonificaciones = "", _deducciones = "", _credito = "", _cuota = "", _motivo = "";
    private FilaAjusteIsVM? _ajusteSeleccionado;
    private FilaCuentaIsVM? _posibleSeleccionado, _retencionSeleccionada;

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
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        RecalcularCommand = new ComandoDelegado(() => _ = LeerDatosAsync(), () => !_ocupado);
        AnadirAjusteCommand = new ComandoDelegado(() => Ajustes.Add(new FilaAjusteIsVM()), () => !_ocupado);
        QuitarAjusteCommand = new ComandoDelegado(() => { if (_ajusteSeleccionado is { } f) Ajustes.Remove(f); }, () => _ajusteSeleccionado is not null && !_ocupado);
        AnadirPosibleCommand = new ComandoDelegado(AnadirPosible, () => _posibleSeleccionado is not null && !_ocupado);
        MayorPosibleCommand = new ComandoDelegado(() => AbrirMayor(_posibleSeleccionado), () => _posibleSeleccionado is not null && _ctx.AbrirMayor is not null);
        MayorRetencionCommand = new ComandoDelegado(() => AbrirMayor(_retencionSeleccionada), () => _retencionSeleccionada is not null && _ctx.AbrirMayor is not null);
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
    public FilaCuentaIsVM? RetencionSeleccionada { get => _retencionSeleccionada; set { if (Fijar(ref _retencionSeleccionada, value)) CommandManager.InvalidateRequerySuggested(); } }

    public string BasesNegativas { get => _basesNegativas; set { if (Fijar(ref _basesNegativas, value)) Recalcular(); } }
    public string Bonificaciones { get => _bonificaciones; set { if (Fijar(ref _bonificaciones, value)) Recalcular(); } }
    public string Deducciones { get => _deducciones; set { if (Fijar(ref _deducciones, value)) Recalcular(); } }
    public string Credito { get => _credito; set { if (Fijar(ref _credito, value)) Recalcular(); } }
    public string Cuota { get => _cuota; set { if (Fijar(ref _cuota, value)) Recalcular(); } }
    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

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
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Calculo), nameof(PosiblesAjustes), nameof(HayPosiblesAjustes), nameof(Retenciones), nameof(TotalRetenciones),
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
        new("Retenciones y pagos a cuenta (473)", Formatos.Importe(p.TotalRetenciones), "Saldo deudor de las 473 a 31/12 (detalle a la derecha)", Sangrada: true),
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

    public IReadOnlyList<FilaCuentaIsVM> Retenciones => _propuesta?.Retenciones
        .Select(x => new FilaCuentaIsVM(x.Cuenta, x.Nombre, x.Pagadores is { Count: > 0 } pg ? string.Join(", ", pg) : "", Formatos.Importe(x.Saldo), x)).ToList() ?? [];
    public string TotalRetenciones => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalRetenciones);

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
            Avisar(nameof(BasesNegativas), nameof(Bonificaciones), nameof(Deducciones), nameof(Credito), nameof(Cuota));
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
        return (new EntradaImpuestoSociedades(ajustes, bases ?? 0m, bonif, deduc ?? 0m, credito, cuota), null);
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

    private void AbrirMayor(FilaCuentaIsVM? f)
    {
        if (f is not null) _ctx.AbrirMayor?.Invoke([f.Cuenta]);
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
