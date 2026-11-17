using System.Windows.Input;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Prorrata;

/// <summary>Contexto de la pantalla Prorrata de IVA y lo que abre fuera de ella.</summary>
public sealed record ContextoRegularizacionProrrata(ServicioRegularizacionProrrata Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Abre la liquidación del IVA en el 4T.</summary>
    public Action? AbrirLiquidacion4T { get; init; }
    /// <summary>Genera el informe para la gestoría (con la hoja «Prorrata» del desglose).</summary>
    public Action? GenerarInforme { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar o deshacer.</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Fila de la tabla de trimestres (y la del año).</summary>
public sealed record FilaTrimestreProrrataVM(string Trimestre, string CuotaSujeta, string Deducido, string Deducible, string Regularizacion, bool EsTotal = false);

/// <summary>Línea del asiento propuesto.</summary>
public sealed record FilaLineaProrrataVM(string Cuenta, string Centro, string Concepto, string Debe, string Haber);

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Prorrata de IVA (contabilidad/procesos/prorrata): porcentajes (provisional, legal y
/// definitiva), la regularización por trimestre con la definitiva y el asiento del 31/12. La definitiva propone la legal; se cambia por
/// la de la gestoría y se confirma con los botones del pie, que guardan la definitiva, la copian como provisional del ejercicio
/// siguiente y graban el asiento como lote regenerable. El menú contextual del asiento tiene lo que actúa sobre él.
/// </summary>
public sealed class RegularizacionProrrataVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoRegularizacionProrrata _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionRegularizacionProrrata? _situacion;
    private PropuestaRegularizacionProrrata? _propuesta;
    private string? _errorPropuesta;
    private string _definitiva = "";
    private string _motivo = "";

    public RegularizacionProrrataVM(ContextoRegularizacionProrrata ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        RecalcularCommand = new ComandoDelegado(() => _ = ProponerAsync(), () => !_ocupado);
        UsarLegalCommand = new ComandoDelegado(() => { Definitiva = _propuesta is null ? "" : Porcentaje(_propuesta.Legal); _ = ProponerAsync(); },
            () => !_ocupado && _propuesta is { DefinitivaEsLaLegal: false });
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(() => { _definitiva = ""; Motivo = ""; Avisar(nameof(Definitiva)); _ = ProponerAsync(); }, () => !_ocupado && _propuesta is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => _situacion?.IdAsiento is not null && _ctx.VerAsiento is not null);
        MayorCommand = new ComandoDelegado(AbrirMayor, () => _ctx.AbrirMayor is not null && _propuesta is not null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _situacion is { Estado: not EstadoRegularizacionProrrata.Pendiente });
        Liquidacion4TCommand = new ComandoDelegado(() => _ctx.AbrirLiquidacion4T?.Invoke(), () => _ctx.AbrirLiquidacion4T is not null);
        InformeCommand = new ComandoDelegado(() => _ctx.GenerarInforme?.Invoke(), () => _ctx.GenerarInforme is not null);
    }

    // ---------- pestaña ----------
    public string Titulo => "Prorrata de IVA";
    public string Ayuda => $"Prorrata de IVA y su regularización anual de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaRegularizacionProrrata { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand UsarLegalCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand Liquidacion4TCommand { get; }
    public ICommand InformeCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public SituacionRegularizacionProrrata? Situacion
    {
        get => _situacion;
        private set { _situacion = value; Avisar(nameof(Situacion), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo), nameof(PorQueNoConfirmar)); }
    }

    public string EstadoTexto => _situacion?.Estado switch
    {
        EstadoRegularizacionProrrata.Regularizada => "Regularizada",
        EstadoRegularizacionProrrata.Desactualizada => "Desactualizada",
        _ => "Pendiente",
    };

    public string ClaseEstado => (_situacion?.Estado ?? EstadoRegularizacionProrrata.Pendiente).ToString().ToUpperInvariant();

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { Estado: EstadoRegularizacionProrrata.Pendiente } => "Sin prorrata definitiva: revisa la propuesta (por defecto, la legal), cámbiala por la de la gestoría si es otra y confirma",
        { Estado: EstadoRegularizacionProrrata.Regularizada } s => $"Definitiva del {Porcentaje(s.Definitiva ?? 0m)}" +
            (s.IdAsiento is not null ? $": regularización de {Formatos.Importe(s.Grabada)} grabada el 31/12" : ": no hay nada que regularizar") +
            ". La liquidación del 4T salda la 4729",
        var s => $"Hay que repetirla: {s.Motivo}",
    };

    // ---------- propuesta ----------
    public PropuestaRegularizacionProrrata? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Cifras), nameof(Trimestres), nameof(Lineas), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe),
                nameof(TotalHaber), nameof(Resultado), nameof(TituloPropuesta), nameof(AyudaDefinitiva), nameof(PorQueNoConfirmar), nameof(PideMotivo), nameof(AyudaMotivo));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(TextoSinPropuesta)); } }
    public string TextoSinPropuesta => _errorPropuesta ?? "Calculando…";

    public string TituloPropuesta => _propuesta is null ? "" :
        $"Regularización de {_propuesta.Ejercicio} · asiento del {Formatos.Fecha(_propuesta.Fecha)}" + (_situacion?.IdAsiento is not null ? " (repetirla sustituye el asiento actual)" : "");

    public IReadOnlyList<CifraLiquidacionVM> Cifras => _propuesta is not { } p ? [] :
    [
        new("Provisional", p.ProvisionalInformada ? Porcentaje(p.Provisional) : "sin informar",
            $"La definitiva de {p.Ejercicio - 1}, con la que se liquidaron los trimestres (Parámetros y tablas › Ejercicios, «Prorrata del año anterior»)"),
        new("Legal (real del año)", Porcentaje(p.Legal),
            $"Ventas con derecho a deducción {Formatos.Importe(p.Numerador)} entre el total {Formatos.Importe(p.Denominador)}, redondeado al entero superior (art. 104 LIVA)"),
        new("Definitiva", Porcentaje(p.Definitiva), "La que se aplica en la regularización: la escrita abajo, la guardada o, por defecto, la legal", true),
        new("Cuota sujeta del año", Formatos.Importe(p.CuotaSujeta), "IVA soportado de los gastos en prorrata (gastos comunes) en el año"),
        new("Regularización", Formatos.Importe(p.Regularizacion), "Deducible con la definitiva − deducido con la provisional: a favor (+) o en contra (−). Va en el 303 del 4T", true),
    ];

    public IReadOnlyList<FilaTrimestreProrrataVM> Trimestres => _propuesta is not { } p ? [] :
    [
        .. p.Trimestres.Select(t => new FilaTrimestreProrrataVM(CalculoLiquidacionIva.Nombre(t.Trimestre), Formatos.Importe(t.CuotaSujeta), Formatos.Importe(t.Deducido),
            Formatos.Importe(t.Deducible), Formatos.Importe(t.Regularizacion))),
        new("Año", Formatos.Importe(p.CuotaSujeta), Formatos.Importe(p.Deducido), Formatos.Importe(p.Deducible), Formatos.Importe(p.Regularizacion), EsTotal: true),
    ];

    public IReadOnlyList<FilaLineaProrrataVM> Lineas => _propuesta?.Lineas.Select(l => new FilaLineaProrrataVM(l.Cuenta, l.NombreCentro ?? (l.IdCentroCoste is null ? "" : $"Centro {l.IdCentroCoste}"), l.Concepto, Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber))).ToList() ?? [];
    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);

    public string Resultado => _propuesta is not { } p ? ""
        : p.Regularizacion > 0m ? $"A favor: {Formatos.Importe(p.Regularizacion)} más de IVA deducible en el 4T"
        : p.Regularizacion < 0m ? $"En contra: {Formatos.Importe(-p.Regularizacion)} menos de IVA deducible en el 4T"
        : "Sin regularización: no hay asiento";

    public IReadOnlyList<string> Avisos => _propuesta?.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;

    /// <summary>Prorrata definitiva escrita, en porcentaje (20 = 20 %).</summary>
    public string Definitiva { get => _definitiva; set { if (Fijar(ref _definitiva, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    public string AyudaDefinitiva => _propuesta is null ? "" :
        $"Prorrata definitiva de {_propuesta.Ejercicio} en porcentaje. Propone la legal ({Porcentaje(_propuesta.Legal)}); escribe la que dé la gestoría si es otra y pulsa Intro o «Volver a calcular»";

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    private bool Repetir => _situacion is { Estado: not EstadoRegularizacionProrrata.Pendiente };
    public bool PideMotivo => _situacion is not null && ServicioRegularizacionProrrata.PorQueMotivo(_situacion.EjercicioTerminado, Repetir) is not null;
    public string AyudaMotivo => _situacion is null ? "" : (ServicioRegularizacionProrrata.PorQueMotivo(_situacion.EjercicioTerminado, Repetir) ?? "") + " El motivo queda en el asiento y en su historial.";
    public string TextoConfirmar => Repetir ? "Repetir la regularización" : "Confirmar la regularización";

    public string? PorQueNoConfirmar =>
        _propuesta is null ? "Espera a que se calcule la propuesta."
        : DefinitivaEscrita() is { Valido: false } ? "La prorrata definitiva no es un porcentaje válido (de 0 a 100)."
        : DefinitivaEscrita() is { Valido: true, Valor: decimal v } && v != _propuesta.Definitiva ? "Has cambiado la definitiva: pulsa Intro o «Volver a calcular» para ver la regularización con ella."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    private (bool Valido, decimal? Valor) DefinitivaEscrita()
    {
        var texto = (_definitiva ?? "").Replace("%", "").Trim();
        if (texto.Length == 0) return (true, null);
        return Formatos.LeerImporte(texto, out var v) && v is >= 0m and <= 100m ? (true, Math.Round(v, 4, MidpointRounding.AwayFromZero) / 100m) : (false, null);
    }

    private static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100m).ToString("0.##", Formatos.Cultura) + " %";

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            Estado = EstadoVista.Contenido;
            _definitiva = "";
            _motivo = "";
            Avisar(nameof(Definitiva), nameof(Motivo));
            await ProponerAsync();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer la prorrata del ejercicio: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    public async Task ProponerAsync()
    {
        Propuesta = null;
        ErrorPropuesta = null;
        var (valido, valor) = DefinitivaEscrita();
        if (!valido) { ErrorPropuesta = "La prorrata definitiva no es un porcentaje válido: escríbela de 0 a 100, por ejemplo 20."; return; }
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ProponerAsync(_ctx.Trabajo, valor);
            if (!r.EsCorrecto) { ErrorPropuesta = r.Error!.TextoOperador(); return; }
            Propuesta = r.Valor;
            _definitiva = Porcentaje(r.Valor.Definitiva).Replace(" %", "");
            Avisar(nameof(Definitiva), nameof(PorQueNoConfirmar));
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
        var detalles = new List<string> { Resultado, $"Se guarda la prorrata definitiva del {Porcentaje(p.Definitiva)} en el ejercicio {p.Ejercicio} y se copia como provisional de {p.Ejercicio + 1} si ese ejercicio existe." };
        if (p.Lineas.Count > 0) detalles.Add("Después hay que liquidar (o repetir) el 4T: su 303 deduce la regularización y salda la 4729.");
        var mensaje = p.Lineas.Count > 0
            ? $"Se graba el asiento del {Formatos.Fecha(p.Fecha)} con {Formatos.Plural(p.Lineas.Count, "línea", "líneas")}, tal como se ve en la propuesta."
            : "No hay nada que regularizar: solo se guarda la prorrata definitiva" + (_situacion?.IdAsiento is not null ? " y se borra el asiento de la regularización anterior." : ".");
        if (_dialogos.Preguntar(TextoConfirmar, mensaje, detalles,
                new OpcionMensaje(TextoConfirmar, "Guarda la definitiva y graba la regularización", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, p.Definitiva, string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha grabado la regularización", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            _ctx.Avisar($"Prorrata definitiva de {p.Ejercicio}: {Porcentaje(p.Definitiva)}." +
                (c.IdAsiento is int id ? $" Regularización de {Formatos.Importe(p.Regularizacion)} en el asiento {id}." : " Sin asiento: no había nada que regularizar.") +
                (c.EjercicioSiguiente is int s ? $" Copiada como provisional de {s}." : "") +
                (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : "") +
                (c.IdAsiento is not null ? " Ahora liquida (o repite) el IVA del 4T." : ""));
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha grabado la regularización", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        var motivo = _dialogos.PedirMotivo($"Deshacer la regularización de la prorrata de {_ctx.Trabajo.Ejercicio}",
            "Se borra su asiento y se quita la prorrata definitiva del ejercicio (la provisional del ejercicio siguiente no cambia). Si el 4T ya está liquidado, hay que repetir su liquidación.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha deshecho la regularización", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshecha la regularización de la prorrata de {_ctx.Trabajo.Ejercicio}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha deshecho la regularización", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsientoAsync()
    {
        if (_situacion?.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void AbrirMayor()
    {
        if (_propuesta is { } p) _ctx.AbrirMayor?.Invoke([.. p.Lineas.Select(l => l.Cuenta).Where(CalculoLiquidacionIva.EsRegularizacionProrrata).DefaultIfEmpty(CuentasRegularizacionProrrata.RegularizacionDefecto).Distinct()]);
    }
}
