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
    /// <summary>Abre la pantalla del activo de un piso (por el nombre de su centro de coste) con su cuenta de explotación; null mientras no exista.</summary>
    public Action<string>? AbrirActivoDeCentro { get; init; }
}

/// <summary>Fila que hace algo con el doble clic (abrir el Mayor de su cuenta, su asiento o su activo).</summary>
public interface IFilaConDobleClic
{
    ICommand? DobleClicCommand { get; }
}

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
public sealed record FilaCuentaIsVM(string Cuenta, string Nombre, string Detalle, string Saldo, SaldoCuentaImpuesto Datos, ICommand? DobleClicCommand = null) : IFilaConDobleClic;

/// <summary>Apunte de una 473 en el detalle de un pagador; el doble clic abre su asiento.</summary>
public sealed record FilaApunteIsVM(int IdAsiento, string Fecha, string Cuenta, string Texto, string Importe, ICommand AbrirCommand) : IFilaConDobleClic
{
    public ICommand? DobleClicCommand => AbrirCommand;
}

/// <summary>Línea de los datos fiscales de Hacienda en el detalle de un pagador.</summary>
public sealed record FilaAeatIsVM(string Modelo, string Nif, string Nombre, string Importe);

/// <summary>Un paso de la pantalla (resultado, ajustes, viviendas, retenciones, contabilizar) con su estado.</summary>
public sealed class PasoIsVM(int numero, string titulo, ICommand irCommand) : ModeloVista
{
    private string _detalle = "", _clase = "";
    private bool _seleccionado;
    public int Numero { get; } = numero;
    public string Titulo { get; } = titulo;
    public ICommand IrCommand { get; } = irCommand;
    public string Detalle { get => _detalle; set => Fijar(ref _detalle, value); }
    /// <summary>OK (revisado), AVISO (hay algo que mirar) o vacío.</summary>
    public string Clase { get => _clase; set { if (Fijar(ref _clase, value)) Avisar(nameof(Marca)); } }
    public string Marca => _clase == "OK" ? "✓" : (Numero + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }
}

/// <summary>Línea de «La cuenta»: el cálculo resumido; al pulsarla se va al paso del que sale.</summary>
/// <param name="Clase">N (normal), SUB (detalle), TOT (subtotal) o FIN (resultado).</param>
public sealed record LineaCuentaIsVM(string Concepto, string Importe, string Clase, string Ayuda, ICommand IrCommand);

/// <summary>Comprobación antes de contabilizar: correcta o por revisar, con el paso donde se arregla.</summary>
public sealed record ChequeoIsVM(bool Ok, string Texto, ICommand? IrCommand = null, string TextoIr = "")
{
    public bool TieneIr => IrCommand is not null;
}

/// <summary>Cuenta de un piso en la cuenta de resultados de las viviendas; el doble clic abre su Mayor.</summary>
public sealed record FilaCuentaPisoIsVM(string Cuenta, string Nombre, string Ingresos, string Gastos, string Apuntes, ICommand AbrirMayorCommand) : IFilaConDobleClic
{
    public ICommand? DobleClicCommand => AbrirMayorCommand;
}

/// <summary>Un piso (centro de una actividad con bonificación): ingresos, gastos directos, su parte de gastos generales y su renta.</summary>
public sealed class FilaPisoIsVM(string centro, string ingresos, string directos, string generales, string renta, IReadOnlyList<FilaCuentaPisoIsVM> cuentas,
    ICommand abrirActivoCommand) : ModeloVista, IFilaConDobleClic
{
    public ICommand? DobleClicCommand => AbrirActivoCommand;
    private bool _expandido;
    public string Centro { get; } = centro;
    public string Ingresos { get; } = ingresos;
    public string Directos { get; } = directos;
    public string Generales { get; } = generales;
    public string Renta { get; } = renta;
    public IReadOnlyList<FilaCuentaPisoIsVM> Cuentas { get; } = cuentas;
    public ICommand AbrirActivoCommand { get; } = abrirActivoCommand;
    public bool Expandido { get => _expandido; set => Fijar(ref _expandido, value); }
}

/// <summary>Cuenta de gastos generales que se reparte a las viviendas; el doble clic abre su Mayor.</summary>
public sealed record FilaGeneralIsVM(string Cuenta, string Nombre, string Importe, string Imputado, ICommand AbrirMayorCommand) : IFilaConDobleClic
{
    public ICommand? DobleClicCommand => AbrirMayorCommand;
}

/// <summary>
/// Un pagador del contraste de retenciones: lo que dice Hacienda, lo contabilizado y el valor que se usa en el IS, que se elige entre el de
/// Hacienda, el de la contabilidad u otro escrito a mano (con justificación si no es el de Hacienda); en los que solo conoce Hacienda, la 473
/// a la que van. Se conserva mientras se escribe para no perder el foco ni lo desplegado.
/// </summary>
public sealed class FilaContrasteIsVM : ModeloVista
{
    private enum Modo { Propuesto, Hacienda, Contabilidad, Otro }

    private readonly Action<FilaContrasteIsVM> _alEscribir;
    private readonly Action<int> _verAsiento;
    private string _valor = "", _justificacion = "", _cuenta = "";
    private Modo _modo = Modo.Propuesto;
    private bool _expandido;
    private ContrasteRetencion? _datos;
    private IReadOnlyList<FilaApunteIsVM> _apuntes = [];
    private IReadOnlyList<FilaAeatIsVM> _lineas = [];

    public FilaContrasteIsVM(string clave, Action<FilaContrasteIsVM> alEscribir, Action<int> verAsiento, Action<string> abrirMayor)
    {
        Clave = clave;
        _alEscribir = alEscribir;
        _verAsiento = verAsiento;
        UsarHaciendaCommand = new ComandoDelegado(UsarHacienda, () => _datos?.Hacienda is not null);
        UsarContabilidadCommand = new ComandoDelegado(UsarContabilidad);
        UsarOtroCommand = new ComandoDelegado(UsarOtro);
        MayorCommand = new ComandoDelegado(() => { if (_datos?.Cuenta is { } c) abrirMayor(c); }, () => _datos?.Cuenta is not null);
    }

    public string Clave { get; }
    public ContrasteRetencion? Datos => _datos;
    public ICommand UsarHaciendaCommand { get; }
    public ICommand UsarContabilidadCommand { get; }
    public ICommand UsarOtroCommand { get; }
    public ICommand MayorCommand { get; }

    public string Pagador => _datos?.Pagador ?? "";
    public string Detalle => _datos is not { } d ? "" : string.Join(" · ", new[] { d.Nif, d.Cuenta is null ? null : $"{d.Cuenta} {d.NombreCuenta}".Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public bool HayHacienda => _datos?.Hacienda is not null;
    public string Hacienda => _datos?.Hacienda is decimal h ? Formatos.Importe(h) : "sin datos";
    public string Contable => _datos is null ? "" : Formatos.Importe(_datos.Contable);
    public string Usado => _datos is null ? "" : Formatos.Importe(_datos.Valor);
    public bool SoloHacienda => _datos?.SoloHacienda ?? false;
    public bool FaltaJustificacion => _datos?.FaltaJustificacion ?? false;
    /// <summary>Si el valor que se usa no es el de Hacienda: se muestra la justificación.</summary>
    public bool PideJustificacion => _datos is { Hacienda: not null } d && d.Valor != d.Hacienda;
    public bool Cuadra => _datos is { Hacienda: decimal h } d && h == d.Contable;

    public string Estado => _datos is not { } d ? "" : d.Hacienda is null ? "Sin datos de Hacienda" : d.FaltaJustificacion ? "Falta justificar"
        : d.Valor != 0m && d.Cuenta is null ? "Falta la cuenta" : Cuadra ? "Cuadra" : "No cuadra";
    public string ClaseEstado => _datos is not { } d ? "" : d.Hacienda is null ? "INFO" : Cuadra && !d.FaltaJustificacion ? "OK" : "AVISO";

    public string Explicacion
    {
        get
        {
            if (_datos is not { } d) return "";
            if (d.Hacienda is not decimal h) return "No hay datos fiscales importados: se usa lo contabilizado.";
            if (d.SoloHacienda) return "Solo lo conoce Hacienda: no hay apuntes en la contabilidad. Elige la 473 a la que va, o usa la contabilidad (0,00 €) si no te corresponde.";
            if (h == 0m && d.Contable > 0m)
                return "Hacienda no sabe nada de estas retenciones: puede que el pagador no las haya declarado. Si las soportaste, usa la contabilidad y justifícalo (certificado del pagador, cobros en el banco).";
            if (h > d.Contable) return $"Hacienda tiene {Formatos.Importe(h - d.Contable)} más que la contabilidad. Mira si falta contabilizar alguna factura o si el pagador declaró algo de otro año.";
            if (h < d.Contable) return $"La contabilidad tiene {Formatos.Importe(d.Contable - h)} más que Hacienda. Si usas la contabilidad, justifícalo.";
            return "";
        }
    }
    public bool HayExplicacion => Explicacion.Length > 0;

    public bool EsHacienda => Efectivo == Modo.Hacienda;
    public bool EsContabilidad => Efectivo == Modo.Contabilidad;
    public bool EsOtro => Efectivo == Modo.Otro;
    private Modo Efectivo => _modo != Modo.Propuesto ? _modo : _datos is null ? Modo.Propuesto : _datos.Hacienda is null ? Modo.Contabilidad : Modo.Hacienda;

    public bool Expandido { get => _expandido; set => Fijar(ref _expandido, value); }

    /// <summary>Valor para el IS escrito; vacío, el propuesto (lo de Hacienda con datos fiscales; si no, lo contabilizado).</summary>
    public string Valor { get => _valor; set { if (Fijar(ref _valor, value ?? "")) _alEscribir(this); } }
    public string Justificacion { get => _justificacion; set { if (Fijar(ref _justificacion, value ?? "")) _alEscribir(this); } }
    /// <summary>473 a la que va el valor (solo se elige en las filas que solo conoce Hacienda).</summary>
    public string Cuenta { get => _cuenta; set { if (Fijar(ref _cuenta, value ?? "")) _alEscribir(this); } }

    public IReadOnlyList<FilaApunteIsVM> Apuntes => _apuntes;
    public IReadOnlyList<FilaAeatIsVM> LineasHacienda => _lineas;
    public bool HayApuntes => _apuntes.Count > 0;
    public bool HayLineasHacienda => _lineas.Count > 0;

    public void UsarHacienda()
    {
        if (_datos is not { Hacienda: decimal h } d) return;
        _modo = Modo.Hacienda;
        Valor = h == d.Propuesto ? "" : Formatos.Numero(h);
        Avisar(nameof(EsHacienda), nameof(EsContabilidad), nameof(EsOtro));
    }

    public void UsarContabilidad()
    {
        if (_datos is not { } d) return;
        _modo = Modo.Contabilidad;
        var v = Math.Max(0m, d.Contable);
        Valor = v == d.Propuesto ? "" : Formatos.Numero(v);
        Avisar(nameof(EsHacienda), nameof(EsContabilidad), nameof(EsOtro));
    }

    private void UsarOtro()
    {
        if (_datos is not { } d) return;
        _modo = Modo.Otro;
        if (_valor.Trim().Length == 0) Valor = Formatos.Numero(d.Valor);
        Avisar(nameof(EsHacienda), nameof(EsContabilidad), nameof(EsOtro));
    }

    internal void Cargar(string valor, string justificacion, string cuenta)
    {
        _valor = valor;
        _justificacion = justificacion;
        _cuenta = cuenta;
        _modo = Modo.Propuesto;
        _datos = null;
        Avisar(nameof(Valor), nameof(Justificacion), nameof(Cuenta));
    }

    internal void Actualizar(ContrasteRetencion r)
    {
        var primera = _datos is null;
        var cambianDetalles = primera || !_datos!.Apuntes.SequenceEqual(r.Apuntes) || !_datos.Aeat.SequenceEqual(r.Aeat);
        _datos = r;
        // Al cargar lo guardado, lo escrito dice qué se eligió: lo contabilizado, lo de Hacienda u otro valor.
        if (primera && r.Escrito)
            _modo = r.Valor == Math.Max(0m, r.Contable) ? Modo.Contabilidad : r.Hacienda is decimal h && r.Valor == h ? Modo.Hacienda : Modo.Otro;
        if (_cuenta.Length == 0 && r.Cuenta is { } c) { _cuenta = c; Avisar(nameof(Cuenta)); }
        if (cambianDetalles)
        {
            _apuntes = r.Apuntes.Select(a => new FilaApunteIsVM(a.IdAsiento, Formatos.Fecha(a.Fecha), a.Cuenta, a.Texto, Formatos.Importe(a.Importe),
                new ComandoDelegado(() => _verAsiento(a.IdAsiento), () => a.IdAsiento > 0))).ToList();
            _lineas = r.Aeat.Select(a => new FilaAeatIsVM(a.Modelo, a.Nif ?? "", a.Nombre, Formatos.Importe(a.Importe))).ToList();
            Avisar(nameof(Apuntes), nameof(LineasHacienda), nameof(HayApuntes), nameof(HayLineasHacienda));
        }
        Avisar(nameof(Pagador), nameof(Detalle), nameof(HayHacienda), nameof(Hacienda), nameof(Contable), nameof(Usado), nameof(SoloHacienda), nameof(FaltaJustificacion),
            nameof(PideJustificacion), nameof(Cuadra), nameof(Estado), nameof(ClaseEstado), nameof(Explicacion), nameof(HayExplicacion), nameof(EsHacienda),
            nameof(EsContabilidad), nameof(EsOtro));
    }
}

/// <summary>Línea del asiento propuesto; el doble clic abre el Mayor de su cuenta.</summary>
public sealed record FilaLineaIsVM(string Cuenta, string Concepto, string Debe, string Haber, ICommand? DobleClicCommand = null) : IFilaConDobleClic;

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades), por pasos
/// (propietario 2026-11-17 15:09): a la izquierda los cinco pasos con su estado, en el centro un paso cada vez (resultado del año, ajustes
/// fiscales, alquiler de viviendas, retenciones y pagos a cuenta, contabilizar), a la derecha «La cuenta» con el cálculo resumido y en el pie
/// lo que falta. Lee la contabilidad una vez y recalcula al escribir; el botón del pie confirma exactamente lo que se ve.
/// </summary>
public sealed class ImpuestoSociedadesVM : ModeloVista, IPestanaTrabajo
{
    private const int PasoResultado = 0, PasoAjustes = 1, PasoViviendas = 2, PasoRetenciones = 3, PasoContabilizar = 4;

    private readonly ContextoImpuestoSociedades _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado, _cargandoEntrada, _generalesExpandido;
    private int _paso;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionImpuestoSociedades? _situacion;
    private DatosImpuestoSociedades? _datos;
    private PropuestaImpuestoSociedades? _propuesta;
    private string? _errorPropuesta, _rutaFichero;
    private string _basesNegativas = "", _bonificaciones = "", _deducciones = "", _credito = "", _cuota = "", _motivo = "", _basesAeat = "", _presentado = "", _renta = "";
    private FilaAjusteIsVM? _ajusteSeleccionado;
    private FilaCuentaIsVM? _posibleSeleccionado;
    private List<RetencionAeat> _aeat = [];
    private IReadOnlyList<FilaPisoIsVM> _pisos = [];
    private IReadOnlyList<FilaGeneralIsVM> _generales = [];
    /// <summary>Lo escrito en las retenciones por clave de fila: valor, justificación y 473 (esta solo en las filas que solo conoce Hacienda).</summary>
    private readonly Dictionary<string, (string Valor, string Justificacion, string Cuenta)> _valores = new(StringComparer.Ordinal);

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
        IrAPasoCommand = new ComandoDelegado(p => IrAPaso(p));
        Pasos =
        [
            new(PasoResultado, "Resultado del año", new ComandoDelegado(() => Paso = PasoResultado)),
            new(PasoAjustes, "Ajustes fiscales", new ComandoDelegado(() => Paso = PasoAjustes)),
            new(PasoViviendas, "Alquiler de viviendas", new ComandoDelegado(() => Paso = PasoViviendas)),
            new(PasoRetenciones, "Retenciones y pagos a cuenta", new ComandoDelegado(() => Paso = PasoRetenciones)),
            new(PasoContabilizar, "Contabilizar", new ComandoDelegado(() => Paso = PasoContabilizar)),
        ];
        Pasos[0].Seleccionado = true;
        AnteriorCommand = new ComandoDelegado(() => Paso--, () => _paso > 0);
        SiguienteCommand = new ComandoDelegado(() => Paso++, () => _paso < PasoContabilizar);
        AbrirSedeCommand = new ComandoDelegado(() => _ = AbrirSedeAsync(), () => _ctx.AbrirEnlace is not null);
        ImportarDatosFiscalesCommand = new ComandoDelegado(() => _ = ImportarDatosFiscalesAsync(), () => !_ocupado && _datos is not null && _ctx.ElegirDatosFiscales is not null);
        AbrirFicheroCommand = new ComandoDelegado(() => { if (_rutaFichero is { } r) _ctx.AbrirEnlace?.Invoke(r); }, () => _rutaFichero is not null && _ctx.AbrirEnlace is not null);
        CopiarHaciendaTodasCommand = new ComandoDelegado(() => Copiar(hacienda: true), () => !_ocupado && HayDatosAeat && Contraste.Count > 0);
        CopiarContabilidadTodasCommand = new ComandoDelegado(() => Copiar(hacienda: false), () => !_ocupado && Contraste.Count > 0);
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
        DescartarCommand = new ComandoDelegado(() => _ = DescartarAsync(), () => !_ocupado && _datos is not null);
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
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand IrAPasoCommand { get; }
    public ICommand AnteriorCommand { get; }
    public ICommand SiguienteCommand { get; }
    public ICommand AnadirAjusteCommand { get; }
    public ICommand QuitarAjusteCommand { get; }
    public ICommand AnadirPosibleCommand { get; }
    public ICommand MayorPosibleCommand { get; }
    public ICommand UsarBonificacionCommand { get; }
    public ICommand UsarCuotaCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand MayorAsientoCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand AbrirSedeCommand { get; }
    public ICommand ImportarDatosFiscalesCommand { get; }
    public ICommand AbrirFicheroCommand { get; }
    public ICommand CopiarHaciendaTodasCommand { get; }
    public ICommand CopiarContabilidadTodasCommand { get; }
    public ICommand UsarRentaCalculadaCommand { get; }

    // ---------- pasos ----------
    public IReadOnlyList<PasoIsVM> Pasos { get; }

    /// <summary>Paso que se ve en el centro (0 a 4).</summary>
    public int Paso
    {
        get => _paso;
        set
        {
            var v = Math.Clamp(value, 0, PasoContabilizar);
            if (!Fijar(ref _paso, v)) return;
            foreach (var p in Pasos) p.Seleccionado = p.Numero == v;
            Avisar(nameof(EsPaso0), nameof(EsPaso1), nameof(EsPaso2), nameof(EsPaso3), nameof(EsPaso4), nameof(TextoSiguiente));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool EsPaso0 => _paso == PasoResultado;
    public bool EsPaso1 => _paso == PasoAjustes;
    public bool EsPaso2 => _paso == PasoViviendas;
    public bool EsPaso3 => _paso == PasoRetenciones;
    public bool EsPaso4 => _paso == PasoContabilizar;
    public string TextoSiguiente => _paso < PasoContabilizar ? $"Siguiente: {Pasos[_paso + 1].Titulo}" : "Siguiente";

    private void IrAPaso(object? parametro)
    {
        if (parametro is int i) Paso = i;
        else if (int.TryParse(parametro?.ToString(), out var n)) Paso = n;
    }

    private ICommand Ir(int paso) => Pasos[paso].IrCommand;

    private void ActualizarPasos()
    {
        void Poner(int i, string detalle, string clase) { Pasos[i].Detalle = detalle; Pasos[i].Clase = clase; }
        if (_datos is not { } d || _propuesta is not { } p)
        {
            foreach (var paso in Pasos) { paso.Detalle = ""; paso.Clase = ""; }
            return;
        }
        Poner(PasoResultado, (p.ResultadoContable >= 0m ? "Beneficio de " : "Pérdida de ") + Formatos.Importe(Math.Abs(p.ResultadoContable)), "OK");
        var sinRevisar = CuentasSinRevisar;
        Poner(PasoAjustes, sinRevisar > 0 ? Formatos.Plural(sinRevisar, "cuenta por revisar", "cuentas por revisar")
            : p.Entrada.Ajustes.Count > 0 ? Formatos.Plural(p.Entrada.Ajustes.Count, "ajuste", "ajustes") : "Sin ajustes", sinRevisar > 0 ? "AVISO" : "OK");
        var faltaMarca = !d.HayActividadBonificada && d.Parametros.Bonificacion > 0m;
        Poner(PasoViviendas, faltaMarca ? "Falta marcar la actividad" : d.HayActividadBonificada ? $"Bonificación de {Formatos.Importe(p.Bonificaciones)}" : "Sin bonificación",
            faltaMarca ? "AVISO" : "OK");
        var noCuadran = p.FilasContraste.Count(f => f.Hacienda is not null && f.Diferencia != 0m);
        var avisoRet = p.PorQueNoContraste is not null || (!HayDatosAeat && p.FilasContraste.Count > 0);
        Poner(PasoRetenciones, p.PorQueNoContraste is not null ? "Falta justificar" : !HayDatosAeat ? (p.FilasContraste.Count > 0 ? "Sin datos de Hacienda" : "Sin retenciones")
            : noCuadran > 0 ? Formatos.Plural(noCuadran, "no cuadra con Hacienda", "no cuadran con Hacienda") : "Todo cuadra con Hacienda", avisoRet ? "AVISO" : "OK");
        Poner(PasoContabilizar, EstadoTexto, _situacion?.Estado switch
        {
            EstadoImpuestoSociedades.Contabilizado => "OK",
            EstadoImpuestoSociedades.Desactualizado => "AVISO",
            _ => "",
        });
    }

    /// <summary>Cuentas que conviene revisar con la gestoría y todavía no tienen ajuste.</summary>
    private int CuentasSinRevisar => _propuesta is not { } p ? 0 : p.PosiblesAjustes.Count(x => !p.Entrada.Ajustes.Any(a => a.Cuenta == x.Cuenta));

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public SituacionImpuestoSociedades? Situacion
    {
        get => _situacion;
        private set
        {
            _situacion = value;
            ActualizarPasos();
            Avisar(nameof(Situacion), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo), nameof(PorQueNoConfirmar),
                nameof(MensajePie), nameof(TituloAsiento));
        }
    }

    public string EstadoTexto => _situacion?.Estado switch
    {
        EstadoImpuestoSociedades.Contabilizado => "Contabilizado",
        EstadoImpuestoSociedades.Desactualizado => "Hay que repetirlo",
        _ => "Pendiente",
    };

    public string ClaseEstado => (_situacion?.Estado ?? EstadoImpuestoSociedades.Pendiente).ToString().ToUpperInvariant();

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { Estado: EstadoImpuestoSociedades.Pendiente } => "Sin contabilizar: sigue los pasos y contabilízalo antes de la regularización",
        { Estado: EstadoImpuestoSociedades.Contabilizado, Guardado: { } g } => $"Impuesto de {Formatos.Importe(g.CuotaLiquida)} contabilizado el {Formatos.Fecha(g.FechaHora)}" +
            (g.Operador is { Length: > 0 } o ? $" por {o}" : "") + (g.Resultado >= 0m ? $"; {Formatos.Importe(g.Resultado)} a pagar" : $"; {Formatos.Importe(-g.Resultado)} a devolver"),
        var s => $"Hay que repetirlo: {s.Motivo}",
    };

    // ---------- veredicto y «La cuenta» ----------
    public string Veredicto => _propuesta is not { } p ? ""
        : p.Resultado < 0m ? $"Hacienda te devuelve {Formatos.Importe(-p.Resultado)}"
        : p.Resultado > 0m ? $"Tienes que pagar {Formatos.Importe(p.Resultado)}"
        : "Ni pagas ni te devuelven";
    public string VeredictoDetalle => _propuesta is not { } p ? "" : $"Impuesto del año {Formatos.Importe(p.CuotaLiquida)} · ya adelantado {Formatos.Importe(p.TotalRetenciones)}";
    /// <summary>DEVUELVE, PAGA o vacío.</summary>
    public string ClaseVeredicto => _propuesta is not { } p ? "" : p.Resultado < 0m ? "DEVUELVE" : p.Resultado > 0m ? "PAGA" : "";

    public IReadOnlyList<LineaCuentaIsVM> LaCuenta
    {
        get
        {
            if (_propuesta is not { } p) return [];
            var ajustes = p.BasePrevia - p.ResultadoContable;
            var lineas = new List<LineaCuentaIsVM>
            {
                new(p.ResultadoContable >= 0m ? "Beneficio del año" : "Pérdida del año", Formatos.Importe(p.ResultadoContable), "N",
                    "Lo que dice la contabilidad: ingresos menos gastos, sin el propio impuesto", Ir(PasoResultado)),
                new("Ajustes fiscales", Signo(ajustes), "SUB", "Gastos que Hacienda no deja restar (suman) e ingresos que no tributan (restan)", Ir(PasoAjustes)),
            };
            if (p.BasesNegativas != 0m)
                lineas.Add(new("Pérdidas de años anteriores", Signo(-p.BasesNegativas), "SUB", "Pérdidas fiscales de otros años que se restan", Ir(PasoAjustes)));
            lineas.Add(new("Base del impuesto", Formatos.Importe(p.BaseImponible), "TOT", "Sobre esta cifra se calcula el impuesto", Ir(PasoAjustes)));
            lineas.Add(new($"Impuesto ({CalculoImpuestoSociedades.Porcentaje(Math.Round(p.TipoMedio, 4))})", Formatos.Importe(p.CuotaIntegra), "N", TextoTipos(p), Ir(PasoAjustes)));
            if (p.Bonificaciones != 0m || (_datos?.HayActividadBonificada ?? false))
                lineas.Add(new("Bonificación por viviendas", Signo(-p.Bonificaciones), "SUB",
                    $"Descuento por alquilar viviendas, sobre una renta de {Formatos.Importe(p.RentaViviendas)}", Ir(PasoViviendas)));
            if (p.Deducciones != 0m) lineas.Add(new("Deducciones", Signo(-p.Deducciones), "SUB", "Deducciones que indica la gestoría", Ir(PasoContabilizar)));
            lineas.Add(new("Impuesto del año", Formatos.Importe(p.CuotaLiquida), "TOT",
                p.CuotaFijada ? "Fijado a mano: no es el calculado" : "Lo que cuesta el impuesto este año (va a la cuenta 6300)", Ir(PasoContabilizar)));
            lineas.Add(new("Ya adelantado", Signo(-p.TotalRetenciones), "SUB", "Retenciones que te practicaron y pagos a cuenta que hiciste durante el año", Ir(PasoRetenciones)));
            lineas.Add(new(p.Resultado < 0m ? "Te devuelven" : "A pagar", Formatos.Importe(Math.Abs(p.Resultado)), "FIN",
                p.Resultado < 0m ? "Adelantaste más de lo que cuesta el impuesto: Hacienda te devuelve la diferencia" : "Impuesto del año menos lo ya adelantado", Ir(PasoContabilizar)));
            return lineas;
        }
    }

    private static string Signo(decimal v) => v > 0m ? "+ " + Formatos.Importe(v) : v < 0m ? "− " + Formatos.Importe(-v) : Formatos.Importe(0m);

    // ---------- paso 1: resultado del año ----------
    public string TituloResultado => _propuesta is { ResultadoContable: < 0m } ? "Pérdida del año" : "Beneficio del año";
    public string ResultadoTexto => _propuesta is not { } p ? "" : Formatos.Importe(Math.Abs(p.ResultadoContable));
    public string IngresosTexto => _datos?.Ingresos is decimal i ? Formatos.Importe(i) : "";
    public string GastosTexto => _datos?.Gastos is decimal g ? Formatos.Importe(g) : "";

    // ---------- paso 2: ajustes fiscales ----------
    public ObservableCollection<FilaAjusteIsVM> Ajustes { get; } = [];
    public IReadOnlyList<string> Clases { get; } = [FilaAjusteIsVM.Permanente, FilaAjusteIsVM.Temporal];
    public IReadOnlyList<string> Sentidos { get; } = [FilaAjusteIsVM.Aumento, FilaAjusteIsVM.Disminucion];
    public FilaAjusteIsVM? AjusteSeleccionado { get => _ajusteSeleccionado; set { if (Fijar(ref _ajusteSeleccionado, value)) CommandManager.InvalidateRequerySuggested(); } }
    public FilaCuentaIsVM? PosibleSeleccionado { get => _posibleSeleccionado; set { if (Fijar(ref _posibleSeleccionado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public string BasesNegativas { get => _basesNegativas; set { if (Fijar(ref _basesNegativas, value)) Recalcular(); } }
    public string Bonificaciones { get => _bonificaciones; set { if (Fijar(ref _bonificaciones, value)) Recalcular(); } }
    public string Deducciones { get => _deducciones; set { if (Fijar(ref _deducciones, value)) Recalcular(); } }
    public string Credito { get => _credito; set { if (Fijar(ref _credito, value)) Recalcular(); } }
    public string Cuota { get => _cuota; set { if (Fijar(ref _cuota, value)) Recalcular(); } }
    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar), nameof(MensajePie)); } }
    /// <summary>Renta de viviendas que se aplica en la bonificación; vacía, la calculada.</summary>
    public string RentaAplicar { get => _renta; set { if (Fijar(ref _renta, value ?? "")) Recalcular(); } }

    public IReadOnlyList<FilaCuentaIsVM> PosiblesAjustes => _propuesta?.PosiblesAjustes
        .Select(x => new FilaCuentaIsVM(x.Cuenta, x.Nombre, Formatos.Plural(x.Apuntes, "apunte", "apuntes") + (_propuesta.Entrada.Ajustes.Any(a => a.Cuenta == x.Cuenta) ? " · ajustada" : ""),
            Formatos.Importe(x.EsGasto ? x.Saldo : -x.Saldo), x, ComandoMayor(x.Cuenta))).ToList() ?? [];
    public bool HayPosiblesAjustes => _propuesta is { PosiblesAjustes.Count: > 0 };
    public string AyudaBasesAeat => _propuesta?.Entrada.BasesNegativasAeat is decimal b ? $"Según Hacienda te quedan {Formatos.Importe(b)} de pérdidas de años anteriores por compensar." : "";
    public bool HayAyudaBasesAeat => AyudaBasesAeat.Length > 0;
    public string AyudaCredito => _propuesta is not { } p ? "" :
        $"Crédito por pérdidas activado (saldo de la 4745: {Formatos.Importe(_datos?.SaldoCreditoPerdidas ?? 0m)}) que se aplica: 6301 al Debe y 4745 al Haber. Vacío: el propuesto ({Formatos.Importe(p.CreditoPropuesto)})";
    public bool HayCredito => (_datos?.SaldoCreditoPerdidas ?? 0m) != 0m || _credito.Trim().Length > 0;

    // ---------- paso 3: alquiler de viviendas ----------
    public bool HayDetalleRenta => _propuesta?.Viviendas is not null;
    /// <summary>Por qué la renta sale vacía: ninguna actividad tiene la marca de bonificación (null si alguna la tiene).</summary>
    public string? SinActividadBonificada => _datos is { HayActividadBonificada: false }
        ? "Ninguna actividad tiene marcada la bonificación: marca «Bonificación IS por alquiler de viviendas» en la de viviendas (Catálogos › Actividades (IVA)) y pulsa F9."
        : null;
    public bool HaySinActividadBonificada => SinActividadBonificada is not null;
    public string EcIngresos => _propuesta?.Viviendas is { } v ? Formatos.Importe(v.Ingresos) : "";
    public string EcDirectos => _propuesta?.Viviendas is { } v ? Formatos.Importe(v.GastosDirectos) : "";
    public string EcGenerales => _propuesta?.Viviendas is { } v ? Formatos.Importe(v.IndirectosImputados) : "";
    public string EcRenta => _propuesta?.Viviendas is { } v ? Formatos.Importe(v.Renta) : "";
    /// <summary>Peso de las viviendas en los ingresos de la sociedad, de 0 a 100 (para la barra).</summary>
    public double ProporcionBarra => _propuesta?.Viviendas is { } v ? (double)Math.Clamp(Math.Round(v.Proporcion * 100m, 1), 0m, 100m) : 0d;
    public string ProporcionTexto => _propuesta?.Viviendas is not { } v ? "" :
        $"Las viviendas son el {CalculoImpuestoSociedades.Porcentaje(Math.Round(v.Proporcion, 4))} de los ingresos de la sociedad ({Formatos.Importe(v.IngresosOrdinariosViviendas)} de " +
        $"{Formatos.Importe(v.IngresosOrdinariosTotales)}), así que cargan con esa misma parte de los gastos generales ({Formatos.Importe(v.IndirectosImputados)} de {Formatos.Importe(v.GastosIndirectos)}).";
    public IReadOnlyList<FilaPisoIsVM> Pisos => _pisos;
    public bool HayPisos => _pisos.Count > 0;
    public IReadOnlyList<FilaGeneralIsVM> Generales => _generales;
    public bool GeneralesExpandido { get => _generalesExpandido; set => Fijar(ref _generalesExpandido, value); }
    public string TituloGenerales => _propuesta?.Viviendas is { } v
        ? $"Gastos generales de la sociedad: {Formatos.Importe(v.GastosIndirectos)} ({Formatos.Plural(_generales.Count, "cuenta", "cuentas")})" : "";
    public string BonificacionTexto => _propuesta is not { } p ? "" : Formatos.Importe(p.Bonificaciones);
    public string RentaAplicadaTexto => _propuesta is not { } p ? "" : Formatos.Importe(p.RentaViviendas);
    public string AyudaBonificacion => _propuesta is not { } p ? "" :
        $"El impuesto que corresponde a la renta de las viviendas ({Formatos.Importe(p.RentaViviendas)} de una base de {Formatos.Importe(p.BaseImponible)}) tiene un descuento del " +
        $"{CalculoImpuestoSociedades.Porcentaje(_datos?.Parametros.Bonificacion ?? 0m)}: {Formatos.Importe(p.BonificacionPropuesta)}." +
        (_bonificaciones.Trim().Length > 0 ? " Se aplica la que has escrito." : "");
    public string AyudaRenta => _propuesta is not { } p ? "" :
        $"Vacía: la calculada ({Formatos.Importe(p.RentaViviendasCalculada)}). Escribe la de la gestoría si usa otra.";

    /// <summary>Monta la cuenta de resultados por piso y los gastos generales con los datos leídos (no cambian al escribir).</summary>
    private void MontarViviendas()
    {
        var plegados = _pisos.Where(p => p.Expandido).Select(p => p.Centro).ToHashSet(StringComparer.Ordinal);
        if (_datos?.Viviendas is not { } v)
        {
            _pisos = [];
            _generales = [];
        }
        else
        {
            _pisos = [.. v.PorCentro().Select(c => new FilaPisoIsVM(c.Centro, Formatos.Importe(c.Ingresos), Formatos.Importe(c.GastosDirectos), Formatos.Importe(c.GastosGenerales),
                Formatos.Importe(c.Renta),
                [.. c.Partidas.Select(p =>
                {
                    var ingreso = p.Clase == PartidaRentaViviendas.IngresoViviendas;
                    return new FilaCuentaPisoIsVM(p.Cuenta, p.NombreCuenta, ingreso ? Formatos.Importe(p.Importe) : "", ingreso ? "" : Formatos.Importe(p.Importe),
                        Formatos.Plural(p.Apuntes, "apunte", "apuntes"), ComandoMayor(p.Cuenta));
                })],
                new ComandoDelegado(() => AbrirActivo(c.Centro))) { Expandido = plegados.Contains(c.Centro) })];
            _generales = [.. v.Partidas.Where(p => p.Clase == PartidaRentaViviendas.GastoIndirecto).GroupBy(p => (p.Cuenta, p.NombreCuenta))
                .Select(g => (g.Key.Cuenta, g.Key.NombreCuenta, Importe: g.Sum(p => p.Importe))).OrderByDescending(g => g.Importe)
                .Select(g => new FilaGeneralIsVM(g.Cuenta, g.NombreCuenta, Formatos.Importe(g.Importe),
                    Formatos.Importe(Math.Round(g.Importe * v.Proporcion, 2, MidpointRounding.AwayFromZero)), ComandoMayor(g.Cuenta)))];
        }
        Avisar(nameof(Pisos), nameof(HayPisos), nameof(Generales), nameof(TituloGenerales));
    }

    private void AbrirActivo(string centro)
    {
        if (_ctx.AbrirActivoDeCentro is { } abrir) abrir(centro);
        else _ctx.Avisar($"La pantalla del activo de «{centro}» con su cuenta de explotación todavía no está disponible. De momento, el doble clic en una de sus cuentas abre su Mayor.");
    }

    // ---------- paso 4: retenciones y pagos a cuenta ----------
    /// <summary>Un pagador por fila; se conservan mientras se escribe (para no perder el foco ni lo desplegado).</summary>
    public ObservableCollection<FilaContrasteIsVM> Contraste { get; } = [];
    public bool HayContraste => Contraste.Count > 0;
    public bool HayDatosAeat => _aeat.Count > 0;
    /// <summary>Las 473 del ejercicio para las filas que solo conoce Hacienda.</summary>
    public IReadOnlyList<string> Cuentas473 => [.. (_datos?.Retenciones ?? []).Select(r => r.Cuenta).Concat(_aeat.Select(a => a.Cuenta).OfType<string>())
        .Distinct().OrderBy(c => c, StringComparer.Ordinal)];
    public string BasesNegativasAeat { get => _basesAeat; set { if (Fijar(ref _basesAeat, value ?? "")) Recalcular(); } }
    public string ResultadoPresentado { get => _presentado; set { if (Fijar(ref _presentado, value ?? "")) Recalcular(); } }
    public string TotalAeat => _propuesta is null || !HayDatosAeat ? "sin datos" : Formatos.Importe(_propuesta.FilasContraste.Sum(f => f.Hacienda ?? 0m));
    public string TotalContable => _propuesta is null ? "" : Formatos.Importe(_propuesta.FilasContraste.Sum(f => f.Contable));
    public string TotalRetenciones => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalRetenciones);
    public string ResumenAeat => !HayDatosAeat
        ? "Todavía no hay datos de Hacienda: se usa lo contabilizado. Pulsa «Abrir la Sede», guarda la página de los datos fiscales (Ctrl+S) e impórtala; solo hay que hacerlo una vez."
        : "Se propone lo que dice Hacienda. Si en algún pagador usas otro valor, tienes que explicar por qué.";
    public string? PorQueNoContraste => _propuesta?.PorQueNoContraste;
    public bool HayPorQueNoContraste => PorQueNoContraste is not null;
    public bool HayFichero => _rutaFichero is not null;
    public string TextoFichero => _rutaFichero is { } r ? $"Datos de Hacienda guardados en {r}. Se leen solos al abrir esta pantalla." : "";

    /// <summary>Pone en todos los pagadores lo de Hacienda o lo contabilizado.</summary>
    private void Copiar(bool hacienda)
    {
        _cargandoEntrada = true;
        try
        {
            foreach (var f in Contraste.ToList())
            {
                if (hacienda) f.UsarHacienda();
                else f.UsarContabilidad();
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

    /// <summary>Pone las filas de pagadores al día con la propuesta: actualiza las que siguen y solo añade o quita las que cambian.</summary>
    private void ActualizarContraste()
    {
        var nuevas = _propuesta?.FilasContraste ?? [];
        if (_propuesta is null && _datos is not null) return; // un error de lectura deja las filas como están para corregir lo escrito
        var actuales = Contraste.ToDictionary(f => f.Clave, StringComparer.Ordinal);
        if (!nuevas.Select(r => r.Clave).SequenceEqual(Contraste.Select(f => f.Clave)))
        {
            Contraste.Clear();
            foreach (var r in nuevas)
            {
                if (!actuales.TryGetValue(r.Clave, out var f))
                {
                    f = new FilaContrasteIsVM(r.Clave, AlEscribirContraste, id => _ = VerAsientoAsync(id), c => AbrirMayor(c));
                    var (v, j, c) = _valores.GetValueOrDefault(r.Clave, ("", "", ""));
                    f.Cargar(v, j, c);
                }
                Contraste.Add(f);
            }
            Avisar(nameof(Cuentas473), nameof(HayContraste));
        }
        foreach (var (f, r) in Contraste.Zip(nuevas)) f.Actualizar(r);
    }

    // ---------- paso 5: contabilizar ----------
    public IReadOnlyList<ChequeoIsVM> Chequeos
    {
        get
        {
            if (_datos is not { } d || _propuesta is not { } p) return [];
            var lista = new List<ChequeoIsVM> { new(true, $"Contabilidad del {p.Ejercicio} leída: {TituloResultado.ToLowerInvariant()} de {ResultadoTexto}.") };
            var sinRevisar = CuentasSinRevisar;
            lista.Add(sinRevisar > 0
                ? new(false, $"Hay {Formatos.Plural(sinRevisar, "cuenta que conviene", "cuentas que conviene")} revisar con la gestoría (multas, donativos…) sin ajuste.", Ir(PasoAjustes), "Revisar")
                : new(true, p.Entrada.Ajustes.Count > 0 ? $"{Formatos.Plural(p.Entrada.Ajustes.Count, "ajuste fiscal aplicado", "ajustes fiscales aplicados")}." : "No hay ajustes fiscales pendientes de revisar."));
            if (d.Parametros.Bonificacion > 0m || d.HayActividadBonificada)
                lista.Add(d.HayActividadBonificada
                    ? new(true, $"Bonificación por alquiler de viviendas: {Formatos.Importe(p.Bonificaciones)}.")
                    : new(false, "Ninguna actividad tiene marcada la bonificación por alquiler de viviendas: no se aplica ninguna.", Ir(PasoViviendas), "Ver"));
            if (p.FilasContraste.Count > 0 || HayDatosAeat)
            {
                lista.Add(HayDatosAeat ? new(true, "Datos de Hacienda importados.")
                    : new(false, "No has importado los datos de Hacienda: las retenciones salen solo de la contabilidad.", Ir(PasoRetenciones), "Importar"));
                lista.Add(p.PorQueNoContraste is { } porQue ? new(false, porQue, Ir(PasoRetenciones), "Arreglar")
                    : new(true, $"Retenciones y pagos a cuenta revisados: {Formatos.Importe(p.TotalRetenciones)}."));
            }
            lista.AddRange(p.Avisos.Select(a => new ChequeoIsVM(false, a)));
            return lista;
        }
    }

    public string AyudaCuota => _propuesta is not { } p ? "" :
        $"Vacío: el calculado ({Formatos.Importe(p.CuotaCalculada)}). Escribe el de la gestoría solo si no cuadra; pide motivo.";
    public string TituloAsiento => _propuesta is null ? "" :
        $"Asiento del {Formatos.Fecha(_propuesta.Fecha)}" + (_situacion?.Guardado?.IdAsiento is not null ? " (repetirlo sustituye el asiento actual)" : "");
    public IReadOnlyList<FilaLineaIsVM> Lineas => _propuesta?.Lineas
        .Select(l => new FilaLineaIsVM(l.Cuenta, l.Concepto, Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber), ComandoMayor(l.Cuenta))).ToList() ?? [];
    public bool HayLineas => _propuesta is { Lineas.Count: > 0 };
    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);

    public string Resultado => _propuesta is not { } p ? ""
        : p.Resultado > 0m ? $"A ingresar: {Formatos.Importe(p.Resultado)}"
        : p.Resultado < 0m ? $"A devolver: {Formatos.Importe(-p.Resultado)}"
        : "Ni a ingresar ni a devolver";

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

    /// <summary>Lo que falta, en el pie: lo que impide contabilizar o, si nada lo impide, lo que conviene mirar antes.</summary>
    public string MensajePie
    {
        get
        {
            if (PorQueNoConfirmar is { } falta) return "Falta: " + falta;
            var avisos = Pasos.Where(p => p.Numero != PasoContabilizar && p.Clase == "AVISO").Select(p => $"{p.Titulo.ToLowerInvariant()} ({p.Detalle.ToLowerInvariant()})").ToList();
            if (avisos.Count > 0) return "Puedes contabilizar, pero conviene mirar antes: " + string.Join("; ", avisos) + ".";
            return _situacion?.Estado == EstadoImpuestoSociedades.Contabilizado ? AyudaEstado + "." : "Todo revisado: ya puedes contabilizar el impuesto.";
        }
    }

    // ---------- propuesta ----------
    public PropuestaImpuestoSociedades? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            ActualizarContraste();
            ActualizarPasos();
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Veredicto), nameof(VeredictoDetalle), nameof(ClaseVeredicto), nameof(LaCuenta), nameof(TituloResultado),
                nameof(ResultadoTexto), nameof(IngresosTexto), nameof(GastosTexto), nameof(PosiblesAjustes), nameof(HayPosiblesAjustes), nameof(AyudaBasesAeat), nameof(HayAyudaBasesAeat),
                nameof(AyudaCredito), nameof(HayCredito), nameof(HayDetalleRenta), nameof(SinActividadBonificada), nameof(HaySinActividadBonificada), nameof(EcIngresos), nameof(EcDirectos),
                nameof(EcGenerales), nameof(EcRenta), nameof(ProporcionBarra), nameof(ProporcionTexto), nameof(TituloGenerales), nameof(BonificacionTexto), nameof(RentaAplicadaTexto),
                nameof(AyudaBonificacion), nameof(AyudaRenta), nameof(TotalAeat), nameof(TotalContable), nameof(TotalRetenciones), nameof(ResumenAeat), nameof(PorQueNoContraste),
                nameof(HayPorQueNoContraste), nameof(Chequeos), nameof(AyudaCuota), nameof(TituloAsiento), nameof(Lineas), nameof(HayLineas), nameof(TotalDebe), nameof(TotalHaber),
                nameof(Resultado), nameof(PorQueNoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo), nameof(MensajePie));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(TextoSinPropuesta), nameof(HayError), nameof(PorQueNoConfirmar), nameof(MensajePie)); } }
    public bool HayError => _errorPropuesta is not null && _datos is not null;
    public string TextoSinPropuesta => _errorPropuesta ?? "Calculando…";

    private static string TextoTipos(PropuestaImpuestoSociedades p) => p.LimiteTramo is decimal l && p.TipoTramo is decimal t
        ? $"Los primeros {Formatos.Importe(l)} al {CalculoImpuestoSociedades.Porcentaje(t)} y el resto al {CalculoImpuestoSociedades.Porcentaje(p.TipoGeneral)}"
        : $"Toda la base al {CalculoImpuestoSociedades.Porcentaje(p.TipoGeneral)}";

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

    /// <summary>
    /// Vuelve a leer la contabilidad (resultado, 473 y sus apuntes, renta de viviendas, posibles ajustes y parámetros) y recalcula con lo escrito.
    /// Si no hay datos de Hacienda, lee los del fichero guardado en la carpeta de documentos (propietario 2026-11-17 15:23).
    /// </summary>
    public async Task LeerDatosAsync()
    {
        Ocupado = true;
        try
        {
            _datos = await _ctx.Servicio.DatosAsync(_ctx.Trabajo);
            MontarViviendas();
            await LeerFicheroGuardadoAsync(_datos);
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

    /// <summary>Anota dónde está el fichero de Hacienda guardado y, si todavía no hay datos de Hacienda, los lee de él. Un fallo aquí no impide trabajar.</summary>
    private async Task LeerFicheroGuardadoAsync(DatosImpuestoSociedades datos)
    {
        try
        {
            var ruta = await _ctx.Servicio.RutaDatosFiscalesAsync(_ctx.Trabajo);
            _rutaFichero = ruta is not null && System.IO.File.Exists(ruta) ? ruta : null;
            if (_rutaFichero is not null && _aeat.Count == 0 && await _ctx.Servicio.LeerDatosFiscalesGuardadosAsync(_ctx.Trabajo, datos) is { EsCorrecto: true } r)
                PonerDatosFiscales(r.Valor);
        }
        catch (Exception ex)
        {
            _rutaFichero = null;
            _ctx.Avisar($"No he podido leer el fichero guardado de los datos de Hacienda: {ex.Message}");
        }
        Avisar(nameof(HayFichero), nameof(TextoFichero));
        CommandManager.InvalidateRequerySuggested();
    }

    private void PonerDatosFiscales(DatosFiscalesAeat d)
    {
        _aeat = [.. d.Retenciones];
        _basesAeat = d.BasesNegativas is decimal b ? Formatos.Numero(b) : "";
        _presentado = d.ResultadoPresentado is decimal p ? Formatos.Numero(p) : "";
        Avisar(nameof(HayDatosAeat), nameof(BasesNegativasAeat), nameof(ResultadoPresentado), nameof(Cuentas473));
    }

    private async Task DescartarAsync()
    {
        CargarEntrada(_situacion?.Guardado?.Entrada ?? EntradaImpuestoSociedades.Vacia, recalcular: false);
        Motivo = "";
        await LeerDatosAsync();
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
        if (!Leer(_basesNegativas, out var bases)) return (null, "Las pérdidas de años anteriores no son un importe válido.");
        if (!Leer(_bonificaciones, out var bonif)) return (null, "La bonificación no es un importe válido.");
        if (!Leer(_deducciones, out var deduc)) return (null, "Las deducciones no son un importe válido.");
        if (!Leer(_credito, out var credito)) return (null, "El crédito por pérdidas no es un importe válido.");
        if (!Leer(_cuota, out var cuota)) return (null, "El impuesto del año escrito no es un importe válido.");
        if (!Leer(_renta, out var renta)) return (null, "La renta de viviendas a aplicar no es un importe válido.");
        if (!Leer(_basesAeat, out var basesAeat)) return (null, "Las pérdidas pendientes según Hacienda no son un importe válido.");
        if (!Leer(_presentado, out var presentado)) return (null, "El resultado presentado no es un importe válido.");
        var valores = new List<ValorRetencion>();
        foreach (var (clave, (texto, justificacion, cuenta)) in _valores.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!Leer(texto, out var v))
                return (null, $"El valor de {Contraste.FirstOrDefault(f => f.Clave == clave)?.Pagador ?? clave} no es un importe válido.");
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

    private ICommand ComandoMayor(string cuenta) => new ComandoDelegado(() => AbrirMayor(cuenta), () => _ctx.AbrirMayor is not null);

    private async Task AbrirSedeAsync()
    {
        try { _ctx.AbrirEnlace?.Invoke(await _ctx.Servicio.UrlDatosFiscalesAsync()); }
        catch (Exception ex) { _dialogos.Error("No he podido abrir la Sede de la AEAT", ex.Message); }
    }

    /// <summary>
    /// Lee el HTML de los datos fiscales, sustituye las líneas de Hacienda, las pérdidas pendientes y el resultado presentado, y guarda una copia
    /// en la carpeta de documentos para no tener que volver a elegirlo.
    /// </summary>
    private async Task ImportarDatosFiscalesAsync()
    {
        if (_datos is not { } datos || _ctx.ElegirDatosFiscales?.Invoke() is not { Length: > 0 } ruta) return;
        Ocupado = true;
        try
        {
            var contenido = await System.IO.File.ReadAllBytesAsync(ruta);
            var r = await _ctx.Servicio.ImportarDatosFiscalesAsync(_ctx.Trabajo, contenido, datos);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se han importado los datos de Hacienda", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var d = r.Valor;
            PonerDatosFiscales(d);
            string guardado;
            try
            {
                var copia = await _ctx.Servicio.GuardarDatosFiscalesAsync(_ctx.Trabajo, contenido);
                _rutaFichero = copia ?? _rutaFichero;
                guardado = copia is null ? " No se ha guardado copia del fichero: la empresa no tiene carpeta de documentos (Parámetros y tablas › Empresas)." : $" Copia guardada en {copia}.";
            }
            catch (Exception ex) { guardado = $" No he podido guardar la copia del fichero: {ex.Message}"; }
            Avisar(nameof(HayFichero), nameof(TextoFichero));
            Recalcular();
            var noCuadran = _propuesta?.FilasContraste.Count(f => f.Hacienda is not null && f.Diferencia != 0m) ?? 0;
            _ctx.Avisar($"Datos de Hacienda {d.Ejercicio} importados: {Formatos.Plural(d.Retenciones.Count, "línea", "líneas")} de retenciones y pagos a cuenta ({Formatos.Importe(d.Retenciones.Sum(x => x.Importe))})" +
                (noCuadran > 0 ? $"; {Formatos.Plural(noCuadran, "pagador no cuadra", "pagadores no cuadran")} con la contabilidad." : ".") +
                (d.ArrendamientoViviendas == false && (_datos?.Parametros.Bonificacion ?? 0m) > 0m ? " Hacienda no le reconoce el carácter de arrendamiento de viviendas: revisa la bonificación." : "") +
                (d.Avisos.Count > 0 ? " " + string.Join(" ", d.Avisos) : "") + guardado);
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se han importado los datos de Hacienda", ex.Message);
        }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p) return;
        var detalles = new List<string> { Resultado, $"Impuesto del año: {Formatos.Importe(p.CuotaLiquida)}; retenciones y pagos a cuenta: {Formatos.Importe(p.TotalRetenciones)}." };
        if (p.Avisos.Count > 0) detalles.Add($"Hay {Formatos.Plural(p.Avisos.Count, "aviso", "avisos")} en la propuesta: revísalos antes.");
        var mensaje = p.Lineas.Count > 0
            ? $"Se graba el asiento del {Formatos.Fecha(p.Fecha)} con {Formatos.Plural(p.Lineas.Count, "línea", "líneas")}, tal como se ve en el último paso, y se guarda el cálculo con sus ajustes y las retenciones."
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
            _ctx.Avisar($"Impuesto sobre Sociedades {p.Ejercicio}: {Formatos.Importe(p.CuotaLiquida)}" +
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
