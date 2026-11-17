using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Amortizacion;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Amortizacion;

/// <summary>Contexto de la pantalla Amortización de activos y lo que abre fuera de ella.</summary>
public sealed record ContextoAmortizacion(ServicioAmortizacion Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado (el primero del lote del periodo).</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Abre Parámetros y tablas en una tabla (la periodicidad está en «ejercicios»).</summary>
    public Action<string>? AbrirParametros { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar o deshacer.</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Fila de la lista de periodos del ejercicio.</summary>
public sealed class FilaPeriodoAmortizacionVM(SituacionPeriodoAmortizacion datos) : ModeloVista
{
    public SituacionPeriodoAmortizacion Datos { get; } = datos;
    public string Nombre => Datos.Periodo.Nombre;
    public string Fecha => Formatos.Fecha(Datos.Periodo.Hasta);
    public string Estado => Datos.Estado switch
    {
        EstadoAmortizacion.Hecho => "Hecho",
        EstadoAmortizacion.Desactualizado => "Desactualizado",
        _ when !Datos.Terminado => "En curso",
        _ => "Pendiente",
    };
    public string ClaseEstado => Datos.Estado == EstadoAmortizacion.Pendiente && !Datos.Terminado ? "CURSO" : Datos.Estado.ToString().ToUpperInvariant();
    public string Asiento => Datos.IdAsiento?.ToString(CultureInfo.InvariantCulture) ?? "";
    public string Grabado => Formatos.ImporteOVacio(Datos.Grabado);
    public string Propuesto => Formatos.ImporteOVacio(Datos.Propuesta.Total);
    public string Ayuda => Datos.Estado switch
    {
        EstadoAmortizacion.Hecho when Datos.IdAsiento is null => "Hecho: no había nada que amortizar en este periodo.",
        EstadoAmortizacion.Hecho => $"Asiento {Datos.IdAsiento} del {Fecha} por {Formatos.Importe(Datos.Grabado)}.",
        EstadoAmortizacion.Desactualizado => $"Hay que repetirlo: {Datos.Motivo}",
        _ when !Datos.Terminado => $"El periodo aún no ha terminado; se propone {Formatos.Importe(Datos.Propuesta.Total)} hasta el {Fecha}.",
        _ => $"Sin contabilizar: se propone {Formatos.Importe(Datos.Propuesta.Total)}.",
    };
}

/// <summary>Fila de la propuesta: un activo con su cálculo y su casilla.</summary>
public sealed class FilaActivoAmortizacionVM : ModeloVista
{
    private readonly Action _alCambiar;
    private bool _incluido;

    public FilaActivoAmortizacionVM(FilaAmortizacion datos, string centro, Action alCambiar)
    {
        Datos = datos;
        Centro = centro;
        _alCambiar = alCambiar;
        _incluido = datos.Incluido;
    }

    public FilaAmortizacion Datos { get; private set; }
    private CuotaAmortizacion? C => Datos.Cuota;
    public bool ConProblema => Datos.ConProblema;
    public bool Editable => !Datos.ConProblema;
    public string Codigo => Datos.Activo.Codigo;
    public string Nombre => Datos.Activo.Nombre;
    public string Centro { get; }
    public string CuentaGasto => Datos.Activo.CuentaGasto ?? "";
    public string CuentaAcumulada => Datos.Activo.CuentaAcumulada ?? "";
    public string Base => Datos.Activo.Plan is { } p && p.Base > 0m ? Formatos.Importe(p.Base) : "";
    public string Porcentaje => Datos.Activo.Plan is { PorcentajeAnual: > 0m } p ? (p.PorcentajeAnual * 100m).ToString("0.##", Formatos.Cultura) + " %" : "";
    public string Dias => C is { } c ? $"{c.Dias}/{c.DiasAno}" : "";
    public string HastaFin => C is { } c ? Formatos.Importe(c.HastaFinPeriodo) : "";
    public string Ya => C is { } c ? Formatos.ImporteOVacio(c.YaContabilizado) : "";
    public string Pendiente => C is { } c ? Formatos.Importe(c.Pendiente) : "";
    public string Importe => C is { } c ? Formatos.Importe(Incluido ? c.Importe : 0m) : "";
    public string Nota => Datos.Nota ?? "";
    public string AyudaFila => ConProblema ? $"No entra: {Datos.Nota}."
        : C is not { } c ? ""
        : $"Plan desde el {Formatos.Fecha(Datos.Activo.Plan!.FechaInicio)}: {Base} × {Porcentaje} = {Formatos.Importe(CalculoAmortizacion.Redondear(Datos.Activo.Plan.CuotaAnual))} al año. " +
          $"Hasta el fin del periodo tocan {c.Dias} de {c.DiasAno} días = {Formatos.Importe(c.HastaFinPeriodo)}; ya contabilizado en el año en la {CuentaGasto}: {Formatos.Importe(c.YaContabilizado)}. " +
          $"Acumulada en la {CuentaAcumulada}: {Formatos.Importe(c.Acumulada)}; queda por amortizar {Formatos.Importe(c.Pendiente)}.";

    /// <summary>La casilla: se le hace su asiento. Un clic la cambia.</summary>
    public bool Incluido
    {
        get => _incluido;
        set { if (Editable && Fijar(ref _incluido, value)) { Avisar(nameof(Importe)); _alCambiar(); } }
    }

    public void Actualizar(FilaAmortizacion datos) { Datos = datos; Avisar(nameof(Importe)); }
}

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Fin de ejercicio › Amortización de activos (contabilidad/procesos/amortizacion-de-activos):
/// los periodos del ejercicio con su estado y, para el elegido, la cuota de cada activo con su casilla, el total de los asientos, el motivo
/// cuando hace falta y la confirmación al pie. Las acciones generales van en la cabecera; el menú contextual de un activo abre el Mayor
/// de sus subcuentas.
/// </summary>
public sealed class AmortizacionVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoAmortizacion _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionAmortizacion? _situacion;
    private FilaPeriodoAmortizacionVM? _seleccion;
    private PropuestaAmortizacion? _propuesta;
    private IReadOnlyDictionary<int, string> _centros = new Dictionary<int, string>();
    private IReadOnlyList<FilaActivoAmortizacionVM> _filas = [];
    private string _motivo = "";

    public AmortizacionVM(ContextoAmortizacion ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(Descartar, () => !_ocupado && _seleccion is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsync(), () => _seleccion?.Datos.IdAsiento is not null && _ctx.VerAsiento is not null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _seleccion?.Datos.IdAsiento is not null && _situacion is { EjercicioCerrado: false });
        PeriodicidadCommand = new ComandoDelegado(() => _ctx.AbrirParametros?.Invoke("ejercicios"), () => _ctx.AbrirParametros is not null);
        MayorGastoCommand = new ComandoDelegado(p => Mayor(p, gasto: true), p => _ctx.AbrirMayor is not null && p is FilaActivoAmortizacionVM { CuentaGasto.Length: > 0 });
        MayorAcumuladaCommand = new ComandoDelegado(p => Mayor(p, gasto: false), p => _ctx.AbrirMayor is not null && p is FilaActivoAmortizacionVM { CuentaAcumulada.Length: > 0 });
    }

    // ---------- pestaña ----------
    public string Titulo => "Amortización de activos";
    public string Ayuda => $"Amortización de activos de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaAmortizacion { DataContext = this };
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
    public ICommand PeriodicidadCommand { get; }
    public ICommand MayorGastoCommand { get; }
    public ICommand MayorAcumuladaCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public string TextoPeriodicidad => _situacion is null ? "" : _situacion.Periodicidad switch
    {
        PeriodicidadAmortizacion.Mensual => "Amortización mensual",
        PeriodicidadAmortizacion.Trimestral => "Amortización trimestral",
        _ => "Amortización anual",
    };

    public string AyudaPeriodicidad => "Cada cuánto se graban los asientos (uno por activo). Se cambia en Parámetros y tablas › Ejercicios › «Periodicidad de la amortización»; cambiarla a mitad de año no duplica nada.";

    public IReadOnlyList<FilaPeriodoAmortizacionVM> Periodos { get; private set; } = [];

    public FilaPeriodoAmortizacionVM? Seleccion
    {
        get => _seleccion;
        set
        {
            if (!Fijar(ref _seleccion, value)) return;
            _motivo = "";
            Avisar(nameof(Motivo), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(AyudaConfirmar), nameof(PideMotivo), nameof(AyudaMotivo));
            if (value is not null) Mostrar(value.Datos.Propuesta);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string EstadoTexto => _seleccion?.Estado ?? "";
    public string ClaseEstado => _seleccion?.ClaseEstado ?? "PENDIENTE";
    public string AyudaEstado => _situacion is { EjercicioCerrado: true } ? $"El ejercicio {_ctx.Trabajo.Ejercicio} está cerrado: solo se consulta (para cambiar algo, reábrelo en Cierre del ejercicio)."
        : _seleccion?.Ayuda ?? "";

    // ---------- propuesta ----------
    public PropuestaAmortizacion? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber), nameof(Resumen), nameof(TituloPropuesta),
                nameof(PorQueNoConfirmar));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public IReadOnlyList<FilaActivoAmortizacionVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }

    public string TituloPropuesta => _propuesta is null ? "" :
        $"{ProcesoAmortizacion.Descripcion(_propuesta.Periodo)} · un asiento por activo a {Formatos.Fecha(_propuesta.Periodo.Hasta)}" +
        (_seleccion?.Datos.IdAsiento is not null ? " (repetirlo sustituye los asientos)" : "");

    public string Resumen => _propuesta is not { } p ? ""
        : p.Lineas.Count == 0 ? "Nada que amortizar: no hay asientos"
        : $"{Formatos.Plural(p.Incluidas.Count(), "activo", "activos")} · {Formatos.Importe(p.Total)}";

    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);
    public IReadOnlyList<string> Avisos => _propuesta?.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    private bool Repetir => _seleccion?.Datos.IdAsiento is not null;
    private bool Terminado => _seleccion?.Datos.Terminado ?? true;
    public bool PideMotivo => _seleccion is not null && ServicioAmortizacion.PorQueMotivo(Terminado, Repetir) is not null;
    public string AyudaMotivo => _seleccion is null ? "" : (ServicioAmortizacion.PorQueMotivo(Terminado, Repetir) ?? "") + " El motivo queda en los asientos y en su historial.";
    public string TextoConfirmar => Repetir ? "Repetir" : "Confirmar";
    public string AyudaConfirmar => PorQueNoConfirmar ?? (Repetir
        ? "Sustituye los asientos del periodo por exactamente lo que se ve y guarda las casillas desmarcadas"
        : "Graba un asiento por activo tal como se ve y guarda las casillas desmarcadas");

    public string? PorQueNoConfirmar =>
        _propuesta is null || _seleccion is null ? "Elige un periodo."
        : _situacion is { EjercicioCerrado: true } ? $"El ejercicio {_ctx.Trabajo.Ejercicio} está cerrado: reábrelo en Cierre del ejercicio para cambiar la amortización."
        : _propuesta.Lineas.Count == 0 && !Repetir ? "No hay nada que amortizar en este periodo."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    private string NombreCentro(int? id) => id is int c ? _centros.TryGetValue(c, out var n) && n.Length > 0 ? n : $"Centro {c}" : "";

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Ocupado = true;
            var clave = _seleccion?.Datos.Periodo.Clave;
            _centros = await _ctx.Servicio.NombresCentrosAsync(_ctx.Trabajo);
            _situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            Periodos = _situacion.Periodos.Select(p => new FilaPeriodoAmortizacionVM(p)).ToList();
            Avisar(nameof(Periodos), nameof(TextoPeriodicidad));
            _seleccion = null;
            Seleccion = Periodos.FirstOrDefault(p => p.Datos.Periodo.Clave == clave)
                ?? Periodos.FirstOrDefault(p => p.Datos.Terminado && p.Datos.FaltaAlgo)
                ?? Periodos.LastOrDefault(p => p.Datos.Terminado)
                ?? Periodos.FirstOrDefault();
            Estado = _situacion.Activos == 0 && _situacion.Periodos.All(p => p.Propuesta.Filas.Count == 0) ? EstadoVista.Vacio : EstadoVista.Contenido;
            if (Estado == EstadoVista.Vacio)
                MensajeEstado = "No hay activos amortizables con plan de amortización en esta empresa (los activos ajenos y los de baja no se amortizan).";
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer la amortización de los activos: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private void Mostrar(PropuestaAmortizacion p)
    {
        Filas = p.Filas.Select(f => new FilaActivoAmortizacionVM(f, NombreCentro(f.Activo.IdCentroCoste), Recalcular)).ToList();
        Propuesta = p;
    }

    /// <summary>Al marcar o desmarcar una casilla: los asientos con esas casillas, sin volver a leer nada.</summary>
    private void Recalcular()
    {
        if (_propuesta is null) return;
        var p = ProcesoAmortizacion.Aplicar(_propuesta, Excluidos());
        foreach (var (fila, datos) in _filas.Zip(p.Filas)) fila.Actualizar(datos);
        Propuesta = p;
    }

    private HashSet<int> Excluidos() => _filas.Where(f => f.Editable && !f.Incluido).Select(f => f.Datos.Activo.IdActivo).ToHashSet();

    private void Descartar()
    {
        if (_seleccion is null) return;
        Motivo = "";
        Mostrar(_seleccion.Datos.Propuesta);
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p || _seleccion is null) return;
        var detalles = new List<string>
        {
            Resumen,
            $"Asiento del {Formatos.Fecha(p.Periodo.Hasta)}: la 68 de cada activo al Debe, con su centro de coste, y su 28 al Haber.",
        };
        if (p.Excluidos.Count > 0) detalles.Add($"Se guardan las casillas desmarcadas ({Formatos.Plural(p.Excluidos.Count, "activo", "activos")}): se respetan al volver a proponer.");
        var mensaje = p.Lineas.Count > 0 ? "Se graba un asiento por activo tal como se ve en la propuesta." : "No hay nada que amortizar: se borran los asientos anteriores de este periodo.";
        if (_dialogos.Preguntar($"{TextoConfirmar} la amortización de {p.Periodo.Nombre}", mensaje, detalles,
                new OpcionMensaje(TextoConfirmar, "Graba los asientos de amortización", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, p.Periodo, Excluidos(), string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha grabado la amortización", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            _ctx.Avisar(r.Valor is int id
                ? $"Amortización de {p.Periodo.Nombre}: {Formatos.Importe(p.Total)} en {Formatos.Plural(p.Lineas.Count / 2, "asiento", "asientos")}, uno por activo (el primero es el {id})."
                : $"Amortización de {p.Periodo.Nombre}: nada que amortizar; no queda ningún asiento.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha grabado la amortización", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        if (_seleccion is null) return;
        var periodo = _seleccion.Datos.Periodo;
        var motivo = _dialogos.PedirMotivo($"Deshacer la amortización de {periodo.Nombre}",
            $"Se borran los asientos de amortización del {Formatos.Fecha(periodo.Hasta)}. Las casillas desmarcadas se conservan para la próxima propuesta.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, periodo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha deshecho la amortización", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshecha la amortización de {periodo.Nombre}: {Formatos.Plural(r.Valor, "asiento borrado", "asientos borrados")}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha deshecho la amortización", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsync()
    {
        if (_seleccion?.Datos.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void Mayor(object? fila, bool gasto)
    {
        if (_ctx.AbrirMayor is not { } abrir || fila is not FilaActivoAmortizacionVM f) return;
        abrir([gasto ? f.CuentaGasto : f.CuentaAcumulada]);
    }
}
