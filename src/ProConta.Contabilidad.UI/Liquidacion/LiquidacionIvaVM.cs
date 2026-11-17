using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Contabilidad.Iva;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Liquidacion;

/// <summary>Contexto de la pantalla Liquidación de IVA y lo que abre fuera de ella.</summary>
public sealed record ContextoLiquidacionIva(ServicioLiquidacionIva Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar o deshacer (la barra de estado vuelve a leer el trimestre activo).</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Diálogos de la pantalla (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosLiquidacionIva
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);
    string? PedirMotivo(string titulo, string explicacion);
    void Error(string titulo, string mensaje);
}

/// <summary>Fila de la lista: un trimestre del ejercicio.</summary>
public sealed class FilaTrimestreIvaVM(TrimestreLiquidacionIva datos) : ModeloVista
{
    public TrimestreLiquidacionIva Datos { get; } = datos;
    public int Trimestre => Datos.Trimestre;
    public string Nombre => Datos.Nombre;
    public string Fin => Formatos.Fecha(Datos.Fin);
    public string Estado => Datos.Estado switch
    {
        EstadoLiquidacionIva.EnCurso => "En curso",
        EstadoLiquidacionIva.Pendiente => "Pendiente",
        EstadoLiquidacionIva.Liquidado => "Liquidado",
        _ => "Desactualizado",
    };
    public string ClaseEstado => Datos.Estado.ToString().ToUpperInvariant();
    public string AyudaEstado => Datos.Estado switch
    {
        EstadoLiquidacionIva.EnCurso => $"El trimestre termina el {Fin}: se liquida a partir del día siguiente",
        EstadoLiquidacionIva.Pendiente => "Terminado y sin liquidar: elígelo, revisa la propuesta y pulsa «Confirmar la liquidación»",
        EstadoLiquidacionIva.Liquidado => $"Liquidado el {Datos.Guardada!.FechaHora:dd/MM/yyyy HH:mm}" + (Datos.Guardada.Operador is { } o ? $" por {o}" : "") +
            (Datos.Guardada.Motivo is { } m ? $". Motivo: {m}" : "") + ". Sus 472 y 477 están a cero",
        _ => $"Hay que repetirla: {Datos.Motivo}",
    };
    public string Cerrado => Datos.Cerrado ? "Cerrado" : "";
    public string AyudaCerrado => Datos.Cerrado ? "Trimestre anterior al activo: sus asientos con cuentas fiscales no se tocan, salvo la propia liquidación (con motivo)" : "";
    public string Calculado => Datos.Estado == EstadoLiquidacionIva.EnCurso ? "" : Formatos.Importe(Datos.ResultadoCalculado);
    public decimal CalculadoOrden => Datos.ResultadoCalculado;
    public string Liquidado => Datos.Guardada is { } g ? Formatos.Importe(g.ImporteLiquidado) : "";
    public string Compensado => Datos.Guardada is { } g ? Formatos.ImporteOVacio(g.Compensado) : "";
    public string Ajuste => Datos.Guardada is { } g ? Formatos.ImporteOVacio(g.Ajuste) : "";
    public bool TieneAsiento => Datos.Guardada?.IdAsiento is not null;
}

/// <summary>Línea del asiento propuesto.</summary>
public sealed record FilaLineaLiquidacionVM(string Cuenta, string Concepto, string Debe, string Haber, bool EsAjuste);

/// <summary>Una cifra del resumen de la propuesta.</summary>
public sealed record CifraLiquidacionVM(string Etiqueta, string Valor, string Ayuda, bool Destacada = false);

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Liquidación de IVA (contabilidad/procesos/liquidacion-iva): los cuatro trimestres del
/// ejercicio de trabajo con su estado; al elegir uno terminado se calcula su propuesta, que se revisa (importe editable para ajustarlo a
/// lo presentado, propietario 2026-11-16 08:37) y se confirma con los botones del pie. El menú contextual tiene lo que actúa sobre el
/// trimestre elegido (volver a calcular, ver el asiento, Mayor de sus cuentas, deshacer).
/// </summary>
public sealed class LiquidacionIvaVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoLiquidacionIva _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private FilaTrimestreIvaVM? _seleccion;
    private PropuestaLiquidacionIva? _propuesta;
    private string? _errorPropuesta;
    private string _importe = "";
    private string _motivo = "";
    private int? _activo;

    public LiquidacionIvaVM(ContextoLiquidacionIva ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_seleccion?.Trimestre));
        RecalcularCommand = new ComandoDelegado(() => _ = ProponerAsync(), () => !_ocupado && PuedeProponer);
        UsarCalculadoCommand = new ComandoDelegado(() => { Importe = _propuesta is null ? "" : Formatos.Numero(_propuesta.ResultadoCalculado); _ = ProponerAsync(); },
            () => !_ocupado && _propuesta is { ImporteModificado: true });
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(() => { _importe = ""; Motivo = ""; _ = ProponerAsync(); }, () => !_ocupado && _propuesta is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => _seleccion?.Datos.Guardada?.IdAsiento is not null && _ctx.VerAsiento is not null);
        MayorCommand = new ComandoDelegado(AbrirMayor, () => _ctx.AbrirMayor is not null && _propuesta is { Lineas.Count: > 0 });
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _seleccion?.Datos.Guardada is not null);
    }

    // ---------- pestaña ----------
    public string Titulo => "Liquidación de IVA";
    public string Ayuda => $"Liquidación trimestral del IVA de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaLiquidacionIva { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand UsarCalculadoCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand DeshacerCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}" + (_activo is int a ? $" · trimestre activo {CalculoLiquidacionIva.Nombre(a)}" : " · sin trimestre activo");
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public ObservableCollection<FilaTrimestreIvaVM> Trimestres { get; } = [];
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public FilaTrimestreIvaVM? Seleccion
    {
        get => _seleccion;
        set
        {
            if (!Fijar(ref _seleccion, value)) return;
            _importe = "";
            _motivo = "";
            Avisar(nameof(Importe), nameof(Motivo));
            _ = ProponerAsync();
        }
    }

    private bool PuedeProponer => _seleccion is { Datos.Estado: not EstadoLiquidacionIva.EnCurso };

    // ---------- propuesta ----------
    public PropuestaLiquidacionIva? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Lineas), nameof(Cifras), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber),
                nameof(TituloPropuesta), nameof(Resultado), nameof(PideMotivo), nameof(AyudaMotivo), nameof(PorQueNoConfirmar), nameof(TextoConfirmar), nameof(AyudaImporte));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(HayErrorPropuesta), nameof(TextoSinPropuesta)); } }
    public bool HayErrorPropuesta => _errorPropuesta is not null;
    public string TextoSinPropuesta => _errorPropuesta ?? (_seleccion is null ? "Elige un trimestre para ver su propuesta de liquidación."
        : _seleccion.Datos.Estado == EstadoLiquidacionIva.EnCurso ? $"El {_seleccion.Nombre} aún no ha terminado: se liquida a partir del {_seleccion.Datos.Fin.AddDays(1):dd/MM/yyyy}." : "Calculando…");

    public string TituloPropuesta => _propuesta is null ? "" :
        $"Propuesta del {_propuesta.NombreTrimestre} {_propuesta.Ejercicio} · asiento del {Formatos.Fecha(_propuesta.Fecha)}" +
        (_seleccion?.Datos.Guardada is not null ? " (repetirla sustituye el asiento actual)" : "");

    public IReadOnlyList<FilaLineaLiquidacionVM> Lineas => _propuesta?.Lineas.Select(l => new FilaLineaLiquidacionVM(l.Cuenta, l.Concepto,
        Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber), l.Concepto.StartsWith("Ajuste", StringComparison.Ordinal))).ToList() ?? [];

    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);

    public string Resultado => _propuesta is not { } p ? ""
        : p.AIngresar > 0m ? $"A ingresar: {Formatos.Importe(p.AIngresar)}" + (p.Compensado > 0m ? $" (después de compensar {Formatos.Importe(p.Compensado)})" : "")
        : p.ACompensar > 0m ? $"A compensar: {Formatos.Importe(p.ACompensar)}"
        : p.Compensado > 0m ? $"Sale a cero después de compensar {Formatos.Importe(p.Compensado)}" : "Resultado cero";

    public IReadOnlyList<CifraLiquidacionVM> Cifras => _propuesta is not { } p ? [] :
    [
        new("IVA devengado (303)", Formatos.Importe(p.Devengado303), "Repercutido y autorrepercutido del trimestre según los libros de IVA"),
        new("IVA deducible (303)", Formatos.Importe(p.Deducible303), $"Lo que se deduce del soportado, con la prorrata provisional del {p.Prorrata * 100m:0.##} %"),
        new("Resultado calculado", Formatos.Importe(p.ResultadoCalculado), "Devengado − deducible: el 303 del informe para la gestoría", true),
        new("No deducible a la 6341", Formatos.ImporteOVacio(p.NoDeducibleProrrata + p.NoDeducible472), "IVA soportado que no se deduce (por la prorrata o por ser de actividades no deducibles): va a gasto"),
        new("Crédito de la 4700", Formatos.ImporteOVacio(p.CreditoAnterior), "IVA a compensar de trimestres anteriores pendiente el último día del trimestre"),
        new("Ajuste", Formatos.ImporteOVacio(p.Ajuste), "Lo que no cuadra: importe fijado distinto del calculado o cuentas que no coinciden con los libros. A la 6391 si sale a pagar menos, a la 6341 si sale a pagar más"),
    ];

    public IReadOnlyList<string> Avisos => _propuesta?.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;

    /// <summary>Importe a liquidar (antes de compensar): el calculado, o el que se escriba para ajustarlo a lo presentado.</summary>
    public string Importe
    {
        get => _importe;
        set { if (Fijar(ref _importe, value)) Avisar(nameof(PorQueNoConfirmar)); }
    }

    public string AyudaImporte => _propuesta is null ? "" :
        $"Resultado del trimestre antes de compensar (positivo a ingresar, negativo a compensar). Calculado: {Formatos.Importe(_propuesta.ResultadoCalculado)}. " +
        "Escribe el que presentó la gestoría si es otro y pulsa Intro o «Volver a calcular»: la diferencia va a la línea de ajuste";

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }
    public bool PideMotivo => _propuesta is not null && _seleccion is not null && ServicioLiquidacionIva.PorQueMotivo(_propuesta, _seleccion.Datos.Cerrado, _seleccion.Datos.Guardada is not null) is not null;
    public string AyudaMotivo => _propuesta is null || _seleccion is null ? "" :
        (ServicioLiquidacionIva.PorQueMotivo(_propuesta, _seleccion.Datos.Cerrado, _seleccion.Datos.Guardada is not null) ?? "") + " El motivo queda en el asiento y en su historial.";

    /// <summary>Por qué no se puede confirmar ahora (null: se puede).</summary>
    public string? PorQueNoConfirmar =>
        _propuesta is null ? "Elige un trimestre terminado."
        : _propuesta.Lineas.Count == 0 ? "No hay nada que liquidar."
        : ImporteEscrito() is { Valido: false } ? "El importe a liquidar no es un número válido."
        : ImporteEscrito() is { Valido: true, Valor: var v } && v != _propuesta.ImporteLiquidado ? "Has cambiado el importe: pulsa Intro o «Volver a calcular» para ver el asiento con ese importe."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    public string TextoConfirmar => _seleccion?.Datos.Guardada is not null ? "Repetir la liquidación" : "Confirmar la liquidación";

    private (bool Valido, decimal? Valor) ImporteEscrito()
    {
        if (string.IsNullOrWhiteSpace(_importe)) return (true, null);
        return Formatos.LeerImporte(_importe, out var v) ? (true, Math.Round(v, 2, MidpointRounding.AwayFromZero)) : (false, null);
    }

    // ---------- carga ----------
    public async Task CargarAsync(int? trimestre = null)
    {
        try
        {
            if (Trimestres.Count == 0) Estado = EstadoVista.Cargando;
            var lista = await _ctx.Servicio.TrimestresAsync(_ctx.Trabajo);
            _activo = await _ctx.Servicio.TrimestreActivoAsync(_ctx.Trabajo);
            Avisar(nameof(Subtitulo));
            Trimestres.Clear();
            foreach (var t in lista) Trimestres.Add(new FilaTrimestreIvaVM(t));
            Estado = EstadoVista.Contenido;
            // Por defecto, el primer trimestre pendiente o desactualizado; si no hay, el último terminado.
            var elegido = trimestre is int n ? Trimestres.FirstOrDefault(f => f.Trimestre == n)
                : Trimestres.FirstOrDefault(f => f.Datos.Estado is EstadoLiquidacionIva.Pendiente or EstadoLiquidacionIva.Desactualizado)
                  ?? Trimestres.LastOrDefault(f => f.Datos.Estado != EstadoLiquidacionIva.EnCurso);
            _seleccion = null;
            Seleccion = elegido;
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer las liquidaciones del IVA: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    public async Task ProponerAsync()
    {
        var fila = _seleccion;
        Propuesta = null;
        ErrorPropuesta = null;
        Avisar(nameof(TextoSinPropuesta));
        if (fila is null || fila.Datos.Estado == EstadoLiquidacionIva.EnCurso) return;
        var (valido, valor) = ImporteEscrito();
        if (!valido) { ErrorPropuesta = "El importe a liquidar no es un número válido: escríbelo como 1.234,56 (negativo si sale a compensar)."; return; }
        // Al abrir un trimestre ya liquidado, la propuesta parte del importe con que se liquidó.
        var fijado = valor ?? (fila.Datos.Guardada is { } g && g.ImporteLiquidado != g.ResultadoCalculado ? g.ImporteLiquidado : null);
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ProponerAsync(_ctx.Trabajo, fila.Trimestre, fijado);
            if (!ReferenceEquals(fila, _seleccion)) return;
            if (!r.EsCorrecto) { ErrorPropuesta = Texto(r.Error!); return; }
            Propuesta = r.Valor;
            _importe = Formatos.Numero(r.Valor.ImporteLiquidado);
            Avisar(nameof(Importe), nameof(PorQueNoConfirmar));
        }
        catch (Exception ex)
        {
            ErrorPropuesta = $"No he podido calcular la propuesta: {ex.Message}";
        }
        finally { Ocupado = false; }
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p || _seleccion is not { } fila) return;
        var detalles = new List<string> { Resultado };
        if (p.HayAjuste) detalles.Add($"Ajuste de {Formatos.Importe(Math.Abs(p.Ajuste))} a la {(p.Ajuste > 0m ? "6391" : "6341")}.");
        if (fila.Datos.Cerrado) detalles.Add("El trimestre está cerrado: solo se graba el asiento de su liquidación.");
        if (_activo == fila.Trimestre && fila.Trimestre < 4) detalles.Add($"Se activa el trimestre siguiente ({CalculoLiquidacionIva.Nombre(fila.Trimestre + 1)}).");
        if (fila.Trimestre == 4) detalles.Add("El cuarto trimestre no se cierra aquí: lo cierra el cierre del ejercicio.");
        if (_dialogos.Preguntar(TextoConfirmar, $"Se graba el asiento del {Formatos.Fecha(p.Fecha)} con {Formatos.Plural(p.Lineas.Count, "línea", "líneas")}, tal como se ve en la propuesta.", detalles,
                new OpcionMensaje(TextoConfirmar, "Graba el asiento de la liquidación", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, fila.Trimestre, p.ImporteModificado ? p.ImporteLiquidado : null,
                string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha grabado la liquidación", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(Texto)));
                return;
            }
            var c = r.Valor;
            _ctx.Avisar($"Liquidado el IVA del {p.NombreTrimestre}: asiento {c.IdAsiento}." +
                (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : "") +
                (c.TrimestreActivo is int a && a != _activo ? $" Trimestre activo: {CalculoLiquidacionIva.Nombre(a)}." : ""));
            _ctx.Cambiada?.Invoke();
            // Después de confirmar, el siguiente pendiente (o el mismo, si no queda ninguno).
            Ocupado = false;
            await CargarAsync();
            return;
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha grabado la liquidación", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync(fila.Trimestre);
    }

    private async Task DeshacerAsync()
    {
        if (_seleccion is not { Datos.Guardada: not null } fila) return;
        var motivo = _dialogos.PedirMotivo($"Deshacer la liquidación del {fila.Nombre}",
            "Se borra su asiento y el trimestre queda pendiente. El trimestre activo no cambia: si hay que volver a abrirlo, hazlo desde la barra de estado (solo se puede el anterior y en los días que lo permite).");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, fila.Trimestre, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha deshecho la liquidación", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(Texto))); return; }
            _ctx.Avisar($"Deshecha la liquidación del IVA del {fila.Nombre}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha deshecho la liquidación", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync(fila.Trimestre);
    }

    private async Task VerAsientoAsync()
    {
        if (_seleccion?.Datos.Guardada?.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void AbrirMayor()
    {
        if (_propuesta is { } p) _ctx.AbrirMayor?.Invoke(p.Lineas.Select(l => l.Cuenta).Where(c => CalculoLiquidacionIva.SeLiquida(c, p.Trimestre)).Distinct().ToList());
    }

    private static string Texto(Nucleo.Resultados.Error e) => e.TextoOperador();
}
