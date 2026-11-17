using System.Windows.Input;
using ProConta.Contabilidad.Cierre;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Cierre;

/// <summary>Contexto de la pantalla Cierre del ejercicio y lo que abre fuera de ella.</summary>
public sealed record ContextoCierreEjercicio(ServicioCierreEjercicio Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Abre la pantalla que resuelve una comprobación previa (null si no hay ninguna para ese destino).</summary>
    public Func<DestinoComprobacion, Action?>? Destino { get; init; }
    /// <summary>Lo llama la pantalla después de cerrar, reabrir o crear el ejercicio siguiente.</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Fila de la comprobación previa, con el enlace a la pantalla que la resuelve.</summary>
public sealed record FilaComprobacionCierreVM(string Titulo, string Detalle, string Clase, string Marca, string TextoAccion, ICommand? Accion)
{
    public bool TieneAccion => Accion is not null;
}

/// <summary>Línea de uno de los asientos propuestos.</summary>
public sealed record FilaLineaCierreVM(string Cuenta, string Nombre, string Debe, string Haber);

/// <summary>Cifra del resumen de la propuesta.</summary>
public sealed record CifraCierreVM(string Etiqueta, string Valor, string Ayuda, bool Destacada = false);

/// <summary>Qué asiento de la propuesta se ve.</summary>
public enum AsientoVisible { Regularizacion, Cierre, Apertura }

/// <summary>
/// Pantalla Contabilidad › Cierre del ejercicio (contabilidad/procesos/cierre-ejercicio 2026-11-17): situación del ejercicio de trabajo
/// (cerrado o no, ejercicio siguiente, cuenta de resultados), la comprobación previa (validación V5, avisa y no impide) con enlaces a donde
/// se resuelve, y la propuesta de los tres asientos (regularización, cierre y apertura del siguiente) que se graba tal cual con el botón
/// del pie. En la cabecera, las acciones generales: crear el ejercicio siguiente, ver los asientos en el Diario y reabrir.
/// </summary>
public sealed class CierreEjercicioVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoCierreEjercicio _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionCierre? _situacion;
    private IReadOnlyList<ComprobacionCierre> _comprobaciones = [];
    private PropuestaCierre? _propuesta;
    private string? _errorPropuesta;
    private string _motivo = "";
    private AsientoVisible _visible = AsientoVisible.Regularizacion;

    public CierreEjercicioVM(ContextoCierreEjercicio ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        CrearSiguienteCommand = new ComandoDelegado(() => _ = CrearSiguienteAsync(), () => !_ocupado && _situacion is { Siguiente: null });
        VerEnDiarioCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => IdVisible is not null && _ctx.VerAsiento is not null);
        ReabrirCommand = new ComandoDelegado(() => _ = ReabrirAsync(), () => !_ocupado && _situacion is { Cerrado: true, SiguienteCerrado: false });
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoCerrar is null);
        MayorCommand = new ComandoDelegado(p => AbrirMayor(p as FilaLineaCierreVM), _ => _ctx.AbrirMayor is not null && LineasVisibles.Count > 0);
        VerCommand = new ComandoDelegado(p => { if (p is string s && Enum.TryParse<AsientoVisible>(s, out var v)) Visible = v; });
    }

    // ---------- pestaña ----------
    public string Titulo => "Cierre del ejercicio";
    public string Ayuda => $"Cierre del ejercicio {_ctx.Trabajo.Ejercicio} de {_ctx.RazonSocial}: regularización, cierre y apertura de {_ctx.Trabajo.Ejercicio + 1}";
    public string Icono => "lock";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaCierreEjercicio { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand CrearSiguienteCommand { get; }
    public ICommand VerEnDiarioCommand { get; }
    public ICommand ReabrirCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand VerCommand { get; }

    // ---------- estado ----------
    private int Ejercicio => _ctx.Trabajo.Ejercicio;
    public string Subtitulo => $"{_ctx.RazonSocial} · {Ejercicio}";
    public string TextoCrearSiguiente => $"Crear el ejercicio {Ejercicio + 1}…";
    public string TextoConfirmar => $"Cerrar el ejercicio {Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public SituacionCierre? Situacion
    {
        get => _situacion;
        private set
        {
            _situacion = value;
            Avisar(nameof(Situacion), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoSiguiente), nameof(TextoCerrado), nameof(EstaCerrado),
                nameof(PorQueNoCerrar), nameof(PideMotivo), nameof(AyudaMotivo), nameof(TituloVisible));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool EstaCerrado => _situacion?.Cerrado == true;
    public string EstadoTexto => _situacion switch { { Cerrado: true } => "Cerrado", { Siguiente: null } => $"Sin {Ejercicio + 1}", _ => "Abierto" };
    public string ClaseEstado => _situacion switch { { Cerrado: true } => "CERRADO", { Siguiente: null } => "SINSIGUIENTE", _ => "ABIERTO" };

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { Cerrado: true, SiguienteCerrado: true } => $"Cerrado. {Ejercicio + 1} también está cerrado: para reabrir {Ejercicio}, reabre antes {Ejercicio + 1}",
        { Cerrado: true, TieneAsientos: false } => "Cerrado fuera de este proceso (por ejemplo, importado de ContaSol): no tiene la regularización, el cierre ni la apertura de ProContaVF",
        { Cerrado: true } => $"Cerrado: no admite cambios. «Reabrir el ejercicio…» borra sus tres asientos y lo deja abierto",
        { Siguiente: null } => $"Falta el ejercicio {Ejercicio + 1}, que recibe la apertura: créalo con «{TextoCrearSiguiente}»",
        _ => "Abierto: revisa la comprobación previa y la propuesta y ciérralo con el botón del pie",
    };

    public string TextoSiguiente => _situacion switch
    {
        null => "",
        { Siguiente: null } => "No existe",
        { SiguienteCerrado: true } => "Creado y cerrado",
        _ => "Creado",
    };

    public string TextoCerrado => _situacion is { Cerrado: true } s
        ? (s.FechaHoraCierre is DateTime f ? $"{f:dd/MM/yyyy HH:mm}" : "Sí") + (string.IsNullOrWhiteSpace(s.Operador) ? "" : $" · {s.Operador}")
        : "No";

    // ---------- comprobación previa ----------
    public IReadOnlyList<ComprobacionCierre> Comprobaciones
    {
        get => _comprobaciones;
        private set { _comprobaciones = value; Avisar(nameof(Comprobaciones), nameof(FilasComprobacion), nameof(PideMotivo), nameof(AyudaMotivo), nameof(PorQueNoCerrar)); }
    }

    public IReadOnlyList<FilaComprobacionCierreVM> FilasComprobacion => _comprobaciones.Select(Fila).ToList();

    private FilaComprobacionCierreVM Fila(ComprobacionCierre c)
    {
        var (clase, marca) = c.Resultado switch
        {
            ResultadoComprobacion.Correcta => ("OK", "✓"),
            ResultadoComprobacion.Aviso => ("AVISO", "!"),
            ResultadoComprobacion.Bloqueo => ("BLOQUEO", "✕"),
            _ => ("NA", "–"),
        };
        ICommand? accion = null;
        var texto = "";
        if (c.Destino == DestinoComprobacion.CrearEjercicio)
        {
            accion = CrearSiguienteCommand;
            texto = "Crear";
        }
        else if (c.Destino != DestinoComprobacion.Ninguno && _ctx.Destino?.Invoke(c.Destino) is { } abrir)
        {
            accion = new ComandoDelegado(abrir);
            texto = c.Resultado == ResultadoComprobacion.Aviso ? c.Destino switch
            {
                DestinoComprobacion.LiquidacionIva => "Liquidar",
                DestinoComprobacion.Diario => "Ver",
                _ => "Resolver",
            } : "Abrir";
        }
        return new FilaComprobacionCierreVM(c.Titulo, c.Detalle, clase, marca, texto, accion);
    }

    // ---------- propuesta ----------
    public PropuestaCierre? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Cifras), nameof(Bloqueos), nameof(HayBloqueos), nameof(PorQueNoCerrar), nameof(TextoResultado),
                nameof(TituloVisible), nameof(LineasVisibles), nameof(TotalDebe), nameof(TotalHaber), nameof(TextoRegularizacion), nameof(TextoCierre), nameof(TextoApertura));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(TextoSinPropuesta)); } }
    public string TextoSinPropuesta => _errorPropuesta ?? "Calculando…";

    public string TextoResultado => _propuesta is not { } p ? ""
        : p.Resultado > 0m ? $"Beneficio de {Formatos.Importe(p.Resultado)} al Haber de la {p.CuentaResultado}"
        : p.Resultado < 0m ? $"Pérdida de {Formatos.Importe(-p.Resultado)} al Debe de la {p.CuentaResultado}"
        : "Resultado cero";

    public IReadOnlyList<CifraCierreVM> Cifras => _propuesta is not { } p ? [] :
    [
        new("Ingresos (grupo 7)", Formatos.Importe(p.Ingresos), "Saldo acreedor de las subcuentas del grupo 7 en el ejercicio, sin los asientos del cierre"),
        new("Gastos (grupo 6)", Formatos.Importe(p.Gastos), "Saldo deudor de las subcuentas del grupo 6 en el ejercicio, sin los asientos del cierre"),
        new($"Resultado a la {p.CuentaResultado}", Formatos.Importe(p.Resultado),
            "Ingresos − gastos: la regularización lo lleva a la cuenta de resultados (Parámetros y tablas › Ejercicios, «Cierre · cuenta de resultados»; vacía, 1290000)", true),
        new("Cierre y apertura", Formatos.Importe(p.Cierre?.TotalDebe ?? 0m), "Sumas del asiento de cierre (las de la apertura son las mismas, cambiadas de lado)"),
        new("Cuadre", p.Cuadra ? "✓ Cuadran" : "✕ No cuadran", "Cada asiento cuadra y la apertura es exactamente el cierre al revés", true),
    ];

    public IReadOnlyList<string> Bloqueos => _propuesta?.Bloqueos ?? [];
    public bool HayBloqueos => Bloqueos.Count > 0;

    public string TextoRegularizacion => Pestana("Regularización", _propuesta?.Regularizacion);
    public string TextoCierre => Pestana("Cierre", _propuesta?.Cierre);
    public string TextoApertura => Pestana("Apertura", _propuesta?.Apertura);
    private static string Pestana(string nombre, AsientoCierre? a) => a is null ? nombre : $"{nombre} · {Formatos.Fecha(a.Fecha)}";

    public AsientoVisible Visible
    {
        get => _visible;
        set
        {
            if (Fijar(ref _visible, value))
                Avisar(nameof(VerRegularizacion), nameof(VerCierre), nameof(VerApertura), nameof(TituloVisible), nameof(LineasVisibles), nameof(TotalDebe), nameof(TotalHaber));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool VerRegularizacion => _visible == AsientoVisible.Regularizacion;
    public bool VerCierre => _visible == AsientoVisible.Cierre;
    public bool VerApertura => _visible == AsientoVisible.Apertura;

    private AsientoCierre? AsientoVisto => _visible switch
    {
        AsientoVisible.Regularizacion => _propuesta?.Regularizacion,
        AsientoVisible.Cierre => _propuesta?.Cierre,
        _ => _propuesta?.Apertura,
    };

    private int? IdVisible => _visible switch
    {
        AsientoVisible.Regularizacion => _situacion?.IdRegularizacion,
        AsientoVisible.Cierre => _situacion?.IdCierre,
        _ => _situacion?.IdApertura,
    };

    public string TituloVisible => AsientoVisto is { } a
        ? $"{a.Descripcion} · {Formatos.Fecha(a.Fecha)} · {Formatos.Plural(a.Lineas.Count, "línea", "líneas")}" + (IdVisible is int id ? $" (grabado: asiento {id})" : "")
        : _visible switch
        {
            AsientoVisible.Regularizacion => "Sin regularización: ninguna cuenta de los grupos 6 y 7 tiene saldo",
            _ => "Sin cierre ni apertura: ninguna cuenta de los grupos 1 a 5 tiene saldo",
        };

    public IReadOnlyList<FilaLineaCierreVM> LineasVisibles => AsientoVisto?.Lineas
        .Select(l => new FilaLineaCierreVM(l.Cuenta, l.Nombre, Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber))).ToList() ?? [];
    public string TotalDebe => AsientoVisto is { } a ? Formatos.Importe(a.TotalDebe) : "";
    public string TotalHaber => AsientoVisto is { } a ? Formatos.Importe(a.TotalHaber) : "";

    // ---------- motivo y confirmar ----------
    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoCerrar)); } }

    private string? PorQueMotivo => ServicioCierreEjercicio.PorQueMotivo(_comprobaciones, _ctx.Servicio.EjercicioTerminado(_ctx.Trabajo));
    public bool PideMotivo => !EstaCerrado && PorQueMotivo is not null;
    public string AyudaMotivo => PorQueMotivo is { } p ? $"{p} Escribe el motivo para cerrar igualmente: queda en los asientos y en su historial." : "";

    public string? PorQueNoCerrar =>
        _situacion is { Cerrado: true } ? $"El ejercicio {Ejercicio} ya está cerrado."
        : _propuesta is null ? "Espera a que se calcule la propuesta."
        : _propuesta.Bloqueos.Count > 0 ? _propuesta.Bloqueos[0]
        : !_propuesta.Cuadra ? "La regularización, el cierre y la apertura no cuadran entre sí."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Ocupado = true;
            Situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            Comprobaciones = await _ctx.Servicio.ComprobarAsync(_ctx.Trabajo);
            Estado = EstadoVista.Contenido;
            _motivo = "";
            Avisar(nameof(Motivo));
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer la situación del ejercicio: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
            return;
        }
        finally { Ocupado = false; }
        await ProponerAsync();
    }

    public async Task ProponerAsync()
    {
        Propuesta = null;
        ErrorPropuesta = null;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ProponerAsync(_ctx.Trabajo);
            if (!r.EsCorrecto) { ErrorPropuesta = r.Error!.TextoOperador(); return; }
            Propuesta = r.Valor;
        }
        catch (Exception ex)
        {
            ErrorPropuesta = $"No he podido calcular la propuesta: {ex.Message}";
        }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p) return;
        var asientos = new[] { p.Regularizacion, p.Cierre, p.Apertura }.Where(a => a is not null).Select(a => $"{a!.Descripcion}, {Formatos.Fecha(a.Fecha)} ({Formatos.Plural(a.Lineas.Count, "línea", "líneas")})").ToList();
        var detalles = new List<string> { TextoResultado };
        detalles.AddRange(asientos);
        detalles.Add($"El ejercicio {Ejercicio} queda cerrado: no admitirá cambios hasta que lo reabras.");
        if (_dialogos.Preguntar(TextoConfirmar, $"Se graban {Formatos.Plural(asientos.Count, "asiento", "asientos")} tal como se ven en la propuesta y se marca el ejercicio como cerrado, todo de una vez.",
                detalles, new OpcionMensaje(TextoConfirmar, "Graba la regularización, el cierre y la apertura y cierra el ejercicio", Principal: true),
                new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha cerrado el ejercicio", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            var ids = new[] { c.IdRegularizacion, c.IdCierre, c.IdApertura }.Where(x => x is not null).Select(x => x!.Value.ToString(Formatos.Cultura)).ToList();
            _ctx.Avisar($"Ejercicio {Ejercicio} cerrado. {TextoResultado}." + (ids.Count > 0 ? $" Asientos {string.Join(", ", ids)}." : "") +
                (c.CuentasCreadas.Count > 0 ? $" Cuenta creada: {string.Join(", ", c.CuentasCreadas)}." : "") +
                (c.CuentasCopiadas > 0 ? $" {Formatos.Plural(c.CuentasCopiadas, "cuenta copiada", "cuentas copiadas")} a {Ejercicio + 1}." : ""));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha cerrado el ejercicio", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task ReabrirAsync()
    {
        if (_situacion is not { } s) return;
        var borrar = new[] { ("Regularización", s.IdRegularizacion), ("Cierre", s.IdCierre), ("Apertura", s.IdApertura) }
            .Where(x => x.Item2 is not null).Select(x => $"{x.Item1} (asiento {x.Item2})").ToList();
        var explicacion = (borrar.Count > 0 ? $"Se borran {string.Join(", ", borrar)}; el borrado queda en el historial de los apuntes. " : "") +
            $"{Ejercicio} vuelve a admitir cambios. Mientras esté abierto, {Ejercicio + 1} no tendrá apertura y sus saldos iniciales saldrán a cero hasta que vuelvas a cerrar.";
        var motivo = _dialogos.PedirMotivo($"Reabrir el ejercicio {Ejercicio}", explicacion);
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ReabrirAsync(_ctx.Trabajo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha reabierto el ejercicio", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Ejercicio {Ejercicio} reabierto" + (r.Valor > 0 ? $": {Formatos.Plural(r.Valor, "asiento borrado", "asientos borrados")}." : "."));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha reabierto el ejercicio", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task CrearSiguienteAsync()
    {
        var nuevo = _ctx.Servicio.NuevoEjercicio;
        PrevisionNuevoEjercicio prevision;
        try { prevision = await nuevo.PrevisionAsync(_ctx.Trabajo); }
        catch (Exception ex) { _dialogos.Error($"No se ha creado el ejercicio {Ejercicio + 1}", ex.Message); return; }
        var detalles = new List<string>
        {
            "Parámetros del ejercicio: cuentas, formas de pago, tipos, retenciones, cuenta de resultados e Impuesto sobre Sociedades.",
            $"Plan de cuentas: {Formatos.Plural(prevision.Cuentas, "cuenta", "cuentas")}, con las mismas identidades y sin saldos.",
            prevision.Series == 0 ? "Series de facturación: no hay ninguna."
                : $"Series de facturación: {Formatos.Plural(prevision.Series, "serie", "series")} ({string.Join(", ", prevision.NombresSeries)}), con la numeración empezando en 1.",
            prevision.Prorrata is decimal pr
                ? $"Prorrata provisional de {prevision.Ejercicio}: {(pr * 100m).ToString("0.##", Formatos.Cultura)} % ({(prevision.DefinitivaFijada ? "la definitiva" : "la provisional, porque la definitiva aún no está fijada")} de {Ejercicio})."
                : "Prorrata: sin informar.",
            "Trimestre activo: 1.º. La configuración de correo, si el ejercicio nuevo no tiene.",
        };
        if (_dialogos.Preguntar($"Crear el ejercicio {prevision.Ejercicio}", $"Se copia de {Ejercicio}. Repetirlo no duplica nada: solo añade lo que falte.", detalles,
                new OpcionMensaje($"Crear {prevision.Ejercicio}", "Crea el ejercicio con lo que se lista", Principal: true), new OpcionMensaje("Cancelar", "No crea nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await nuevo.CrearSiguienteAsync(_ctx.Trabajo);
            if (!r.EsCorrecto) { _dialogos.Error($"No se ha creado el ejercicio {Ejercicio + 1}", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            var c = r.Valor;
            _ctx.Avisar($"Ejercicio {c.Ejercicio.Ejercicio} {(c.Creado ? "creado" : "completado")}: {Formatos.Plural(c.CuentasCopiadas, "cuenta copiada", "cuentas copiadas")}, " +
                $"{Formatos.Plural(c.SeriesCopiadas, "serie", "series")}" + (c.OtrasTablas.Count > 0 ? $" y {string.Join(", ", c.OtrasTablas)}." : "."));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error($"No se ha creado el ejercicio {Ejercicio + 1}", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsientoAsync()
    {
        if (IdVisible is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void AbrirMayor(FilaLineaCierreVM? fila)
    {
        if (_ctx.AbrirMayor is not { } abrir) return;
        if (fila is not null) abrir([fila.Cuenta]);
        else if (_propuesta is { } p) abrir([p.CuentaResultado]);
    }
}
