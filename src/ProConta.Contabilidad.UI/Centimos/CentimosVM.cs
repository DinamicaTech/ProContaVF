using System.Windows.Input;
using ProConta.Contabilidad.Centimos;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Centimos;

/// <summary>Contexto de la pantalla Céntimos residuales y lo que abre fuera de ella.</summary>
public sealed record ContextoCentimos(ServicioCentimos Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado (el primero del lote).</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Abre Parámetros y tablas en una tabla (el umbral y las cuentas de redondeo están en «ejercicios»).</summary>
    public Action<string>? AbrirParametros { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar o deshacer.</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Fila de la propuesta: una subcuenta con céntimos residuales y su casilla.</summary>
public sealed class FilaCentimosVM : ModeloVista
{
    private readonly Action _alCambiar;
    private bool _incluido;

    public FilaCentimosVM(FilaCentimos datos, Action alCambiar)
    {
        Datos = datos;
        _alCambiar = alCambiar;
        _incluido = datos.Incluido;
    }

    public FilaCentimos Datos { get; }
    public string Cuenta => Datos.Cuenta;
    public string Nombre => Datos.Nombre;
    public string Saldo => Formatos.Importe(Datos.Saldo);
    public string Naturaleza => Datos.Deudora ? "Deudor" : "Acreedor";
    public string Contrapartida => Datos.Contrapartida;
    public string Importe => Formatos.Importe(Incluido ? Datos.Importe : 0m);
    public string AyudaFila => Datos.Deudora
        ? $"Saldo deudor de {Formatos.Importe(Datos.Saldo)} a 31/12: se abona la {Cuenta} contra la {Contrapartida} (diferencias de redondeo, gasto)."
        : $"Saldo acreedor de {Formatos.Importe(Math.Abs(Datos.Saldo))} a 31/12: se carga la {Cuenta} contra la {Contrapartida} (diferencias de redondeo, ingreso).";

    /// <summary>La casilla: se le hace su asiento. Un clic la cambia.</summary>
    public bool Incluido
    {
        get => _incluido;
        set { if (Fijar(ref _incluido, value)) { Avisar(nameof(Importe)); _alCambiar(); } }
    }
}

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Fin de ejercicio › Céntimos residuales (contabilidad/procesos/centimos-residuales): el
/// estado del proceso en el ejercicio, las subcuentas con céntimos residuales a 31/12 con su casilla, los totales contra las cuentas de
/// redondeo, el motivo cuando hace falta y la confirmación al pie. Las acciones generales van en la cabecera; el menú contextual de una
/// subcuenta abre su Mayor.
/// </summary>
public sealed class CentimosVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoCentimos _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionCentimos? _situacion;
    private PropuestaCentimos? _propuesta;
    private IReadOnlyList<FilaCentimosVM> _filas = [];
    private string _motivo = "";

    public CentimosVM(ContextoCentimos ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(Descartar, () => !_ocupado && _situacion is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsync(), () => _situacion?.IdAsiento is not null && _ctx.VerAsiento is not null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _situacion is { IdAsiento: not null, EjercicioCerrado: false });
        ParametrosCommand = new ComandoDelegado(() => _ctx.AbrirParametros?.Invoke("ejercicios"), () => _ctx.AbrirParametros is not null);
        MayorCommand = new ComandoDelegado(p => Mayor(p), p => _ctx.AbrirMayor is not null && p is FilaCentimosVM);
    }

    // ---------- pestaña ----------
    public string Titulo => "Céntimos residuales";
    public string Ayuda => $"Céntimos residuales de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaCentimos { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    void IPestanaTrabajo.Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand ParametrosCommand { get; }
    public ICommand MayorCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public string EstadoTexto => _situacion?.Estado switch
    {
        EstadoCentimos.Hecho => "Hecho",
        EstadoCentimos.Desactualizado => "Desactualizado",
        EstadoCentimos.Pendiente when !_situacion.EjercicioTerminado => "En curso",
        EstadoCentimos.Pendiente => "Pendiente",
        _ => "",
    };

    public string ClaseEstado => _situacion is { Estado: EstadoCentimos.Pendiente, EjercicioTerminado: false } ? "CURSO" : (_situacion?.Estado.ToString() ?? "PENDIENTE").ToUpperInvariant();

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { EjercicioCerrado: true } => $"El ejercicio {_ctx.Trabajo.Ejercicio} está cerrado: solo se consulta (para cambiar algo, reábrelo en Cierre del ejercicio).",
        { Estado: EstadoCentimos.Hecho, IdAsiento: null } => "Hecho: no hay céntimos residuales que saldar.",
        { Estado: EstadoCentimos.Hecho } s => $"{Formatos.Plural(s.Asientos, "asiento", "asientos")} del 31/12 por {Formatos.Importe(s.Grabado)}, uno por subcuenta.",
        { Estado: EstadoCentimos.Desactualizado } s => $"Hay que repetirlo: {s.Motivo}",
        { EjercicioTerminado: false } s => $"El ejercicio aún no ha terminado; con los saldos de hoy se saldarían {Formatos.Plural(s.Propuesta.Asientos.Count, "subcuenta", "subcuentas")}.",
        var s => $"Sin saldar: {Formatos.Plural(s.Propuesta.Asientos.Count, "subcuenta", "subcuentas")} por {Formatos.Importe(s.Propuesta.Total)}.",
    };

    public string TextoUmbral => _propuesta is null ? "" : $"Umbral: menos de {Formatos.Importe(_propuesta.Configuracion.Umbral)}";
    public string AyudaUmbral => "Entran las subcuentas de los grupos 1 a 5 (salvo 47 y 57) con un saldo a 31/12 distinto de cero y menor que el umbral en valor absoluto. " +
        "El umbral y las cuentas de redondeo se cambian en Parámetros y tablas › Ejercicios.";

    // ---------- propuesta ----------
    public PropuestaCentimos? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Avisos), nameof(HayAvisos), nameof(TotalGastos), nameof(TotalIngresos), nameof(Resumen),
                nameof(TituloPropuesta), nameof(TextoUmbral), nameof(EtiquetaGastos), nameof(EtiquetaIngresos), nameof(PorQueNoConfirmar));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public IReadOnlyList<FilaCentimosVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }

    public string TituloPropuesta => _propuesta is null ? "" :
        $"{CalculoCentimos.Descripcion(_propuesta.Ejercicio)} · un asiento por subcuenta a {Formatos.Fecha(_propuesta.Fecha)}" +
        (Repetir ? " (repetirlo sustituye los asientos)" : "");

    public string Resumen => _propuesta is not { } p ? ""
        : p.Filas.Count == 0 ? "Ninguna subcuenta con céntimos residuales"
        : p.Asientos.Count == 0 ? "Todas excluidas: no hay asientos"
        : $"{Formatos.Plural(p.Asientos.Count, "subcuenta", "subcuentas")} · {Formatos.Importe(p.Total)}";

    public string EtiquetaGastos => _propuesta is null ? "" : $"A la {_propuesta.Configuracion.CuentaGastos} (gasto)";
    public string EtiquetaIngresos => _propuesta is null ? "" : $"A la {_propuesta.Configuracion.CuentaIngresos} (ingreso)";
    public string TotalGastos => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalGastos);
    public string TotalIngresos => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalIngresos);
    public IReadOnlyList<string> Avisos => _propuesta?.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    private bool Repetir => _situacion?.IdAsiento is not null;
    private bool Terminado => _situacion?.EjercicioTerminado ?? true;
    public bool PideMotivo => _situacion is not null && ServicioCentimos.PorQueMotivo(Terminado, Repetir) is not null;
    public string AyudaMotivo => _situacion is null ? "" : (ServicioCentimos.PorQueMotivo(Terminado, Repetir) ?? "") + " El motivo queda en los asientos y en su historial.";
    public string TextoConfirmar => Repetir ? "Repetir" : "Confirmar";
    public string AyudaConfirmar => PorQueNoConfirmar ?? (Repetir
        ? "Sustituye los asientos de céntimos residuales del 31/12 por exactamente lo que se ve y guarda las casillas desmarcadas"
        : "Graba un asiento por subcuenta a 31/12 tal como se ve y guarda las casillas desmarcadas");

    public string? PorQueNoConfirmar =>
        _propuesta is null || _situacion is null ? "Cargando…"
        : _situacion.EjercicioCerrado ? $"El ejercicio {_ctx.Trabajo.Ejercicio} está cerrado: reábrelo en Cierre del ejercicio para cambiar los céntimos residuales."
        : _propuesta.Asientos.Count == 0 && !Repetir ? "No hay céntimos residuales que saldar."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Ocupado = true;
            _situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            _motivo = "";
            Avisar(nameof(Motivo), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(AyudaConfirmar), nameof(PideMotivo), nameof(AyudaMotivo));
            Mostrar(_situacion.Propuesta);
            Estado = _situacion.Propuesta.Filas.Count == 0 && _situacion.IdAsiento is null ? EstadoVista.Vacio : EstadoVista.Contenido;
            if (Estado == EstadoVista.Vacio)
                MensajeEstado = $"Ninguna subcuenta de los grupos 1 a 5 (salvo 47 y 57) tiene a 31/12 un saldo distinto de cero y menor de {Formatos.Importe(_situacion.Propuesta.Configuracion.Umbral)}: no hay céntimos residuales que saldar.";
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los céntimos residuales: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private void Mostrar(PropuestaCentimos p)
    {
        Filas = p.Filas.Select(f => new FilaCentimosVM(f, Recalcular)).ToList();
        Propuesta = p;
    }

    /// <summary>Al marcar o desmarcar una casilla: los asientos con esas casillas, sin volver a leer nada.</summary>
    private void Recalcular()
    {
        if (_propuesta is null) return;
        Propuesta = CalculoCentimos.Aplicar(_propuesta, Excluidas());
    }

    private HashSet<string> Excluidas() => _filas.Where(f => !f.Incluido).Select(f => f.Cuenta).ToHashSet(StringComparer.Ordinal);

    private void Descartar()
    {
        if (_situacion is null) return;
        Motivo = "";
        Mostrar(_situacion.Propuesta);
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p) return;
        var detalles = new List<string>
        {
            Resumen,
            $"Un asiento por subcuenta a {Formatos.Fecha(p.Fecha)}: las deudoras contra la {p.Configuracion.CuentaGastos} ({Formatos.Importe(p.TotalGastos)}) y las acreedoras contra la {p.Configuracion.CuentaIngresos} ({Formatos.Importe(p.TotalIngresos)}).",
        };
        if (p.Excluidas.Count > 0) detalles.Add($"Se guardan las casillas desmarcadas ({Formatos.Plural(p.Excluidas.Count, "subcuenta", "subcuentas")}): se respetan al volver a proponer.");
        var mensaje = p.Asientos.Count > 0 ? "Se graba un asiento por subcuenta tal como se ve en la propuesta." : "No hay nada que saldar: se borran los asientos anteriores de este proceso.";
        if (_dialogos.Preguntar($"{TextoConfirmar} los céntimos residuales de {p.Ejercicio}", mensaje, detalles,
                new OpcionMensaje(TextoConfirmar, "Graba los asientos de céntimos residuales", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, Excluidas(), string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se han saldado los céntimos residuales", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            _ctx.Avisar((c.IdAsiento is int id
                    ? $"Céntimos residuales de {p.Ejercicio}: {Formatos.Importe(p.Total)} en {Formatos.Plural(c.Asientos, "asiento", "asientos")}, uno por subcuenta (el primero es el {id})."
                    : $"Céntimos residuales de {p.Ejercicio}: nada que saldar; no queda ningún asiento.")
                + (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : ""));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se han saldado los céntimos residuales", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        var motivo = _dialogos.PedirMotivo($"Deshacer los céntimos residuales de {_ctx.Trabajo.Ejercicio}",
            "Se borran los asientos de céntimos residuales del 31/12. Las casillas desmarcadas se conservan para la próxima propuesta.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se han deshecho los céntimos residuales", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshechos los céntimos residuales de {_ctx.Trabajo.Ejercicio}: {Formatos.Plural(r.Valor, "asiento borrado", "asientos borrados")}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se han deshecho los céntimos residuales", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsync()
    {
        if (_situacion?.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void Mayor(object? fila)
    {
        if (_ctx.AbrirMayor is not { } abrir || fila is not FilaCentimosVM f) return;
        abrir([f.Cuenta]);
    }
}
