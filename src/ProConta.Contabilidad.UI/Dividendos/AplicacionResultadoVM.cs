using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Dividendos;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Dividendos;

/// <summary>Contexto de la pantalla Aplicación del resultado y lo que abre fuera de ella.</summary>
public sealed record ContextoAplicacionResultado(ServicioAplicacionResultado Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Abre la pantalla Reparto de dividendos (para el acta del reparto con cargo al resultado).</summary>
    public Action? AbrirRepartos { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar o deshacer.</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Línea de los asientos propuestos (la aplicación y, si hay dividendos, su reparto).</summary>
public sealed record FilaLineaAplicacionVM(string Asiento, string Cuenta, string Concepto, string Debe, string Haber);

/// <summary>
/// Pantalla Contabilidad › Fin de ejercicio › Aplicación del resultado (contabilidad/procesos/aplicacion-resultado 2026-11-17): en el ejercicio de
/// trabajo, el resultado del anterior (saldo de su cuenta de resultados tras la apertura), la propuesta (compensación de pérdidas, reserva legal
/// mínima, dividendos y voluntarias), editable, y los asientos. Se confirma con los botones del pie; el menú contextual del asiento tiene lo que
/// actúa sobre él.
/// </summary>
public sealed class AplicacionResultadoVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoAplicacionResultado _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionAplicacion? _situacion;
    private PropuestaAplicacionCompleta? _propuesta;
    private string? _errorPropuesta;
    private string _fecha = "";
    private string _compensacion = "";
    private string _legal = "";
    private string _dividendos = "";
    private string _motivo = "";

    public AplicacionResultadoVM(ContextoAplicacionResultado ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        RecalcularCommand = new ComandoDelegado(() => _ = ProponerAsync(), () => !_ocupado);
        UsarPropuestaCommand = new ComandoDelegado(() => { _compensacion = ""; _legal = ""; Avisar(nameof(Compensacion), nameof(ReservaLegal)); _ = ProponerAsync(); }, () => !_ocupado);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => _situacion?.Guardada?.IdAsiento is not null && _ctx.VerAsiento is not null);
        MayorCommand = new ComandoDelegado(AbrirMayor, () => _ctx.AbrirMayor is not null && _propuesta is not null);
        RepartosCommand = new ComandoDelegado(() => _ctx.AbrirRepartos?.Invoke(), () => _ctx.AbrirRepartos is not null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _situacion?.Guardada is not null);
    }

    // ---------- pestaña ----------
    public string Titulo => "Aplicación del resultado";
    public string Ayuda => $"Aplicación del resultado de {_ctx.Trabajo.Ejercicio - 1} de {_ctx.RazonSocial}, en {_ctx.Trabajo.Ejercicio}";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaAplicacionResultado { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand UsarPropuestaCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand RepartosCommand { get; }
    public ICommand DeshacerCommand { get; }

    // ---------- estado ----------
    public int EjercicioResultado => _ctx.Trabajo.Ejercicio - 1;
    public string Subtitulo => $"{_ctx.RazonSocial} · resultado de {EjercicioResultado}, aplicado en {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public SituacionAplicacion? Situacion
    {
        get => _situacion;
        private set { _situacion = value; Avisar(nameof(Situacion), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(PideMotivo), nameof(PorQueNoConfirmar)); }
    }

    public string EstadoTexto => _situacion?.Estado switch
    {
        EstadoAplicacion.Aplicada => "Aplicado",
        EstadoAplicacion.Desactualizada => "Desactualizado",
        EstadoAplicacion.SinResultado => "Sin resultado",
        _ => "Pendiente",
    };

    public string ClaseEstado => (_situacion?.Estado ?? EstadoAplicacion.Pendiente).ToString().ToUpperInvariant();

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { Estado: EstadoAplicacion.SinResultado } => $"La cuenta de resultados no tiene saldo en {_ctx.Trabajo.Ejercicio}: cierra antes {EjercicioResultado} (Fin de ejercicio › Cierre del ejercicio), que deja el resultado en la apertura",
        { Estado: EstadoAplicacion.Pendiente } s => $"Resultado de {EjercicioResultado} sin aplicar: {Formatos.Importe(s.Resultado)}. Revisa la propuesta y confírmala con la fecha de la junta",
        { Estado: EstadoAplicacion.Aplicada, Guardada: { } g } => $"Aplicado el {Formatos.Fecha(g.Fecha)}" + (g.IdAsiento is int a ? $" (asiento {a})" : "") +
            (g.IdReparto is int r ? $", con el reparto de dividendos {r}" : "") + (g.Operador is { } o ? $" por {o}" : ""),
        var s => $"Hay que repetirla: {s.Motivo}",
    };

    // ---------- propuesta ----------
    public PropuestaAplicacionCompleta? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Cifras), nameof(Lineas), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber),
                nameof(TituloPropuesta), nameof(PorQueNoConfirmar), nameof(EsBeneficio), nameof(Resumen));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public bool EsBeneficio => _propuesta?.Aplicacion is { Resultado: > 0m };
    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(TextoSinPropuesta)); } }
    public string TextoSinPropuesta => _errorPropuesta ?? "Calculando…";

    public string TituloPropuesta => _propuesta is null ? "" :
        $"Aplicación del resultado de {EjercicioResultado} · asiento del {Formatos.Fecha(_propuesta.Aplicacion.Fecha)}" + (_situacion?.Guardada is not null ? " (repetirla sustituye los asientos actuales)" : "");

    public IReadOnlyList<CifraLiquidacionVM> Cifras => _propuesta?.Aplicacion is not { } p ? [] :
    [
        new("Resultado de " + EjercicioResultado, Formatos.Importe(p.Resultado), $"Saldo acreedor de la {p.Config.CuentaResultado} en {_ctx.Trabajo.Ejercicio} (la apertura deja ahí el resultado): positivo, beneficio; negativo, pérdida", true),
        new("Pérdidas anteriores", Formatos.Importe(p.Saldos.PerdidasPendientes), $"Saldo deudor de la {p.Config.CuentaResultadosNegativos}: resultados negativos de ejercicios anteriores aún sin compensar"),
        new("Reserva legal actual", Formatos.Importe(p.Saldos.ReservaLegal), $"Saldo de la {p.Config.CuentaReservaLegal} antes de esta aplicación"),
        new("Capital", Formatos.Importe(p.Saldos.Capital), "Saldo de las cuentas 100: la reserva legal tiene que llegar al 20 % del capital"),
        new("Mínimo legal", Formatos.Importe(p.MinimoLegal), "10 % del beneficio hasta que la reserva legal llegue al 20 % del capital (art. 274 de la Ley de Sociedades de Capital)"),
        new("Dividendos (bruto)", Formatos.Importe(p.DividendosBruto), _propuesta.Dividendos is { } d
            ? $"Neto {Formatos.Importe(d.Neto)} + retención {Formatos.Importe(d.Retencion)} al {d.PorcentajeRetencion.ToString("0.##", Formatos.Cultura)} %, repartido entre los socios por su participación"
            : "Sin dividendos con cargo al resultado"),
        new("A voluntarias", Formatos.Importe(p.Voluntarias), $"El resto del beneficio, a la {p.Config.CuentaVoluntarias}", true),
    ];

    public string Resumen => _propuesta?.Aplicacion is not { } p ? ""
        : p.EsPerdida ? $"Pérdida: {Formatos.Importe(-p.Resultado)} a la {p.Config.CuentaResultadosNegativos}"
        : $"Beneficio de {Formatos.Importe(p.Resultado)}: {Formatos.Importe(p.Compensacion)} compensa pérdidas, {Formatos.Importe(p.ReservaLegal)} a reserva legal, " +
          $"{Formatos.Importe(p.DividendosBruto)} a dividendos y {Formatos.Importe(p.Voluntarias)} a voluntarias";

    public IReadOnlyList<FilaLineaAplicacionVM> Lineas
    {
        get
        {
            if (_propuesta is not { } p) return [];
            var r = p.Aplicacion.Lineas.Select(l => new FilaLineaAplicacionVM("Aplicación", l.Cuenta, l.Concepto, Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber))).ToList();
            if (p.Dividendos is { } d)
                r.AddRange(d.Lineas.Select(l => new FilaLineaAplicacionVM("Dividendos", l.Cuenta, l.Concepto, Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber))));
            return r;
        }
    }

    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.Aplicacion.TotalDebe + (_propuesta.Dividendos?.TotalDebe ?? 0m));
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.Aplicacion.TotalHaber + (_propuesta.Dividendos?.TotalHaber ?? 0m));

    public IReadOnlyList<string> Avisos => _propuesta is not { } p ? [] : [.. p.Aplicacion.Errores, .. p.Aplicacion.Avisos, .. p.Dividendos?.Avisos ?? []];
    public bool HayAvisos => Avisos.Count > 0;

    // ---------- campos ----------
    public string Fecha { get => _fecha; set { if (Fijar(ref _fecha, value)) Avisar(nameof(PorQueNoConfirmar)); } }
    public string Compensacion { get => _compensacion; set { if (Fijar(ref _compensacion, value)) Avisar(nameof(PorQueNoConfirmar)); } }
    public string ReservaLegal { get => _legal; set { if (Fijar(ref _legal, value)) Avisar(nameof(PorQueNoConfirmar)); } }
    public string Dividendos { get => _dividendos; set { if (Fijar(ref _dividendos, value)) Avisar(nameof(PorQueNoConfirmar)); } }
    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    public string AyudaFecha => $"Fecha de la junta que aprueba la aplicación y de su asiento, dentro de {_ctx.Trabajo.Ejercicio} (dd/mm/aaaa). Si hay dividendos, su retención (4751) tiene que caer en el trimestre activo";
    public string AyudaCompensacion => "Parte del beneficio que compensa pérdidas de ejercicios anteriores (121). Propone el menor del beneficio y las pérdidas pendientes";
    public string AyudaLegal => "Parte del beneficio para la reserva legal (112). Propone el mínimo de la ley: el 10 % del beneficio hasta que la reserva legal llegue al 20 % del capital";
    public string AyudaDividendos => "Neto total que perciben los socios con cargo al resultado (vacío o 0: sin dividendos). Se reparte por la participación de cada socio y se graba como un reparto de dividendos con su retención";

    public bool PideMotivo => _situacion?.Guardada is not null;
    public string AyudaMotivo => "Repetir una aplicación confirmada pide motivo; queda en el asiento y en su historial.";
    public string TextoConfirmar => _situacion?.Guardada is not null ? "Repetir la aplicación" : "Confirmar la aplicación";

    public string? PorQueNoConfirmar =>
        _propuesta is null ? "Espera a que se calcule la propuesta."
        : LeerFecha() is null ? $"Escribe la fecha de la junta (dd/mm/aaaa, dentro de {_ctx.Trabajo.Ejercicio})."
        : !Importe(_compensacion).Valido || !Importe(_legal).Valido || !Importe(_dividendos).Valido ? "Algún importe no es válido: escríbelo en euros, por ejemplo 1.234,56."
        : Cambiado() ? "Has cambiado algún dato: pulsa Intro o «Volver a calcular» para ver la propuesta con él."
        : _propuesta.Aplicacion.Errores.Count > 0 ? _propuesta.Aplicacion.Errores[0]
        : !_propuesta.SePuedeConfirmar ? "No hay resultado que aplicar."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    private bool Cambiado()
    {
        if (_propuesta is not { } p) return false;
        var a = p.Aplicacion;
        return LeerFecha() != a.Fecha
            || (Importe(_compensacion).Valor is decimal c && c != a.Compensacion)
            || (Importe(_legal).Valor is decimal l && l != a.ReservaLegal)
            || (Importe(_dividendos).Valor ?? 0m) != (p.Dividendos?.Neto ?? 0m);
    }

    private DateTime? LeerFecha()
    {
        var t = (_fecha ?? "").Trim();
        string[] formatos = ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d-M-yyyy", "ddMMyyyy"];
        return DateTime.TryParseExact(t, formatos, Formatos.Cultura, DateTimeStyles.None, out var f) && f.Year == _ctx.Trabajo.Ejercicio ? f.Date : null;
    }

    private static (bool Valido, decimal? Valor) Importe(string? texto)
    {
        var t = (texto ?? "").Replace("€", "").Trim();
        if (t.Length == 0) return (true, null);
        return Formatos.LeerImporte(t, out var v) && v >= 0m ? (true, Math.Round(v, 2, MidpointRounding.AwayFromZero)) : (false, null);
    }

    private ImportesAplicacion Importes() => new(Importe(_compensacion).Valor, Importe(_legal).Valor, Importe(_dividendos).Valor ?? 0m);

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            Estado = EstadoVista.Contenido;
            var g = _situacion?.Guardada;
            _fecha = Formatos.Fecha(_ctx.Servicio.FechaPropuesta(_ctx.Trabajo, _situacion));
            _compensacion = g is null ? "" : Formatos.Numero(g.Compensacion);
            _legal = g is null ? "" : Formatos.Numero(g.ReservaLegal);
            _dividendos = (_situacion?.DividendosNeto ?? 0m) == 0m ? "" : Formatos.Numero(_situacion!.DividendosNeto);
            _motivo = "";
            Avisar(nameof(Fecha), nameof(Compensacion), nameof(ReservaLegal), nameof(Dividendos), nameof(Motivo));
            await ProponerAsync();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer la aplicación del resultado: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    public async Task ProponerAsync()
    {
        Propuesta = null;
        ErrorPropuesta = null;
        if (LeerFecha() is not DateTime fecha) { ErrorPropuesta = $"Escribe la fecha de la junta (dd/mm/aaaa, dentro de {_ctx.Trabajo.Ejercicio})."; return; }
        if (!Importe(_compensacion).Valido || !Importe(_legal).Valido || !Importe(_dividendos).Valido)
        {
            ErrorPropuesta = "Algún importe no es válido: escríbelo en euros y positivo, por ejemplo 1.234,56 (vacío: el propuesto).";
            return;
        }
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ProponerAsync(_ctx.Trabajo, fecha, Importes());
            if (!r.EsCorrecto) { ErrorPropuesta = r.Error!.TextoOperador(); return; }
            Propuesta = r.Valor;
            var a = r.Valor.Aplicacion;
            if (a.Resultado > 0m)
            {
                _compensacion = Formatos.Numero(a.Compensacion);
                _legal = Formatos.Numero(a.ReservaLegal);
                Avisar(nameof(Compensacion), nameof(ReservaLegal), nameof(PorQueNoConfirmar));
            }
        }
        catch (Exception ex)
        {
            ErrorPropuesta = $"No he podido calcular la propuesta: {ex.Message}";
        }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p || LeerFecha() is not DateTime fecha) return;
        var detalles = new List<string> { Resumen };
        if (p.Dividendos is { } d)
            detalles.Add($"Se graba además un reparto de dividendos de {Formatos.Importe(d.Bruto)} brutos ({Formatos.Importe(d.Neto)} netos) con cargo a la {p.Aplicacion.Config.CuentaResultado}; su acta se genera desde Reparto de dividendos.");
        else if (_situacion?.Guardada?.IdReparto is not null)
            detalles.Add("Se deshace el reparto de dividendos que tenía la aplicación anterior.");
        if (_dialogos.Preguntar(TextoConfirmar, $"Se graba la aplicación del resultado de {EjercicioResultado} con fecha {Formatos.Fecha(fecha)}, tal como se ve en la propuesta.", detalles,
                new OpcionMensaje(TextoConfirmar, "Graba los asientos de la propuesta", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, fecha, Importes(), string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha grabado la aplicación del resultado", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            _ctx.Avisar($"Resultado de {EjercicioResultado} aplicado" + (c.IdAsiento is int a ? $" en el asiento {a}" : "") + (c.IdReparto is int rep ? $", con el reparto de dividendos {rep}" : "") + "." +
                (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : ""));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha grabado la aplicación del resultado", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        var motivo = _dialogos.PedirMotivo($"Deshacer la aplicación del resultado de {EjercicioResultado}",
            "Se borra su asiento y, si tenía dividendos con cargo al resultado, también su reparto. El resultado vuelve a quedar en la cuenta de resultados.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha deshecho la aplicación", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshecha la aplicación del resultado de {EjercicioResultado}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha deshecho la aplicación", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsientoAsync()
    {
        if (_situacion?.Guardada?.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void AbrirMayor()
    {
        if (_propuesta is { } p)
            _ctx.AbrirMayor?.Invoke([.. p.Aplicacion.Lineas.Select(l => l.Cuenta).Append(p.Aplicacion.Config.CuentaResultado).Distinct()]);
    }
}
