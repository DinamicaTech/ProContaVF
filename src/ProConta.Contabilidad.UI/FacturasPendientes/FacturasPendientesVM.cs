using System.Windows.Input;
using ProConta.Contabilidad.Cierre;
using ProConta.Contabilidad.FacturasPendientes;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.FacturasPendientes;

/// <summary>Contexto de la pantalla Facturas pendientes de recibir y lo que abre fuera de ella.</summary>
public sealed record ContextoFacturasPendientes(ServicioFacturasPendientes Servicio, ServicioNuevoEjercicio NuevoEjercicio, ContextoSociedadEjercicio Trabajo, string RazonSocial,
    Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Lo llama la pantalla después de confirmar, deshacer o crear el ejercicio siguiente.</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Fila de la tabla: un proveedor con saldo deudor, su casilla (entra o no) y su base editable.</summary>
public sealed class FilaProveedorPendienteVM : ModeloVista
{
    private readonly Action _alCambiar;
    private bool _incluido;
    private string _base;

    public FilaProveedorPendienteVM(FilaFacturaPendiente datos, string centro, Action alCambiar)
    {
        Datos = datos;
        Centro = centro;
        _alCambiar = alCambiar;
        _incluido = datos.Incluido;
        _base = Formatos.Numero(datos.Base);
    }

    public FilaFacturaPendiente Datos { get; private set; }
    public string Cuenta => Datos.Cuenta;
    public string Nombre => Datos.Nombre;
    public string Saldo => Formatos.Importe(Datos.Saldo);
    public string Origen => Datos.Origen == OrigenBase.UltimaFactura ? "Última factura" : "IVA supuesto";
    public string AyudaOrigen => Datos.OrigenTexto + (Datos.Origen == OrigenBase.UltimaFactura
        ? $". Base = saldo × base / total de esa factura = {Formatos.Importe(Datos.BaseCalculada)}."
        : $". Base = saldo / (1 + tipo) = {Formatos.Importe(Datos.BaseCalculada)}. El tipo se cambia en Parámetros y tablas › Ejercicios.");
    public string CuentaGasto => Datos.CuentaGasto;
    public string AyudaGasto => Datos.OrigenGasto switch
    {
        OrigenGasto.Proveedor => "Cuenta de gasto por defecto del proveedor (ficha del proveedor en la empresa).",
        OrigenGasto.UltimaFactura => "Cuenta de gasto de su última factura contabilizada (el proveedor no tiene cuenta de gasto por defecto).",
        _ => "Cuenta de gasto por defecto del ejercicio (Parámetros y tablas › Ejercicios): el proveedor no tiene cuenta de gasto ni facturas.",
    };
    public string Centro { get; }
    public bool Manual => Datos.BaseManual is not null;

    /// <summary>La casilla: entra en la provisión. Un clic la cambia.</summary>
    public bool Incluido { get => _incluido; set { if (Fijar(ref _incluido, value)) _alCambiar(); } }

    /// <summary>La base escrita (la calculada si no se cambia).</summary>
    public string Base { get => _base; set { if (Fijar(ref _base, value)) _alCambiar(); } }

    public string AyudaBase => Manual
        ? $"Base escrita a mano; la calculada es {Formatos.Importe(Datos.BaseCalculada)}. Escribe esa cifra (o pulsa «Descartar cambios») para volver a ella."
        : "Base supuesta del gasto pendiente. Puedes escribir otra (por ejemplo, la de la factura que ya sabes que llegará); se respeta al volver a proponer mientras el proveedor siga en la lista.";

    /// <summary>La base escrita, si es un importe válido.</summary>
    public decimal? BaseEscrita => Formatos.LeerImporte(_base, out var v) ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : null;

    public void Actualizar(FilaFacturaPendiente datos)
    {
        Datos = datos;
        Avisar(nameof(Manual), nameof(AyudaBase));
    }
}

/// <summary>Línea del asiento resumido (por cuenta y centro).</summary>
public sealed record FilaLineaPendienteVM(string Cuenta, string Centro, string Concepto, string Debe, string Haber);

/// <summary>
/// Pantalla Contabilidad › Procesos contables › Facturas pendientes de recibir (contabilidad/procesos/facturas-pendientes-recibir): los
/// proveedores y acreedores cuadrados en el Mayor con saldo deudor a 31/12, con su base supuesta y su cuenta de gasto; casilla para
/// excluirlos y base editable; el asiento resumido de la provisión (31/12) y de su reversión (1/1 del siguiente). Se confirma con los
/// botones del pie; las acciones generales van en la cabecera y el menú contextual de cada fila solo tiene lo que actúa sobre ella.
/// </summary>
public sealed class FacturasPendientesVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoFacturasPendientes _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private SituacionFacturasPendientes? _situacion;
    private PropuestaFacturasPendientes? _propuesta;
    private IReadOnlyDictionary<int, string> _centros = new Dictionary<int, string>();
    private IReadOnlyList<FilaProveedorPendienteVM> _filas = [];
    private string? _errorBase;
    private string _motivo = "";

    public FacturasPendientesVM(ContextoFacturasPendientes ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(Descartar, () => !_ocupado && _situacion is not null);
        VerProvisionCommand = new ComandoDelegado(() => _ = VerAsync(_situacion?.IdProvision), () => _situacion?.IdProvision is not null && _ctx.VerAsiento is not null);
        VerReversionCommand = new ComandoDelegado(() => _ = VerAsync(_situacion?.IdReversion), () => _situacion?.IdReversion is not null && _ctx.VerAsiento is not null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _situacion is { Estado: not EstadoFacturasPendientes.Pendiente });
        CrearSiguienteCommand = new ComandoDelegado(() => _ = CrearSiguienteAsync(), () => !_ocupado && _situacion is { ExisteSiguiente: false });
        MayorCommand = new ComandoDelegado(p => AbrirMayor(p), p => _ctx.AbrirMayor is not null && p is FilaProveedorPendienteVM or FilaLineaPendienteVM);
    }

    // ---------- pestaña ----------
    public string Titulo => "Facturas pendientes de recibir";
    public string Ayuda => $"Facturas pendientes de recibir de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaFacturasPendientes { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    void IPestanaTrabajo.Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerProvisionCommand { get; }
    public ICommand VerReversionCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand CrearSiguienteCommand { get; }
    public ICommand MayorCommand { get; }

    // ---------- estado ----------
    public int Ejercicio => _ctx.Trabajo.Ejercicio;
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public SituacionFacturasPendientes? Situacion
    {
        get => _situacion;
        private set
        {
            _situacion = value;
            Avisar(nameof(Situacion), nameof(EstadoTexto), nameof(ClaseEstado), nameof(AyudaEstado), nameof(TextoConfirmar), nameof(AyudaConfirmar), nameof(PideMotivo),
                nameof(AyudaMotivo), nameof(PorQueNoConfirmar), nameof(FaltaSiguiente), nameof(TextoCrearSiguiente));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string EstadoTexto => _situacion?.Estado switch
    {
        EstadoFacturasPendientes.Hecho => "Hecho",
        EstadoFacturasPendientes.Desactualizado => "Desactualizado",
        _ => "Pendiente",
    };

    public string ClaseEstado => (_situacion?.Estado ?? EstadoFacturasPendientes.Pendiente).ToString().ToUpperInvariant();

    public string AyudaEstado => _situacion switch
    {
        null => "",
        { Estado: EstadoFacturasPendientes.Pendiente } => "Sin contabilizar: revisa los proveedores, desmarca los que no tocan, corrige la base si hace falta y confirma",
        { Estado: EstadoFacturasPendientes.Hecho } s => s.Grabado == 0m ? "Hecho: no había nada que provisionar"
            : $"Provisión de {Formatos.Importe(s.Grabado)} grabada el 31/12 y revertida el 1/1/{Ejercicio + 1}",
        var s => $"Hay que repetirlo: {s.Motivo}",
    };

    public bool FaltaSiguiente => _situacion is { ExisteSiguiente: false };
    public string TextoCrearSiguiente => $"Crear el ejercicio {Ejercicio + 1}…";

    // ---------- propuesta ----------
    public PropuestaFacturasPendientes? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Avisar(nameof(Propuesta), nameof(HayPropuesta), nameof(Lineas), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber), nameof(Resumen),
                nameof(TituloPropuesta), nameof(TextoReversion), nameof(PorQueNoConfirmar), nameof(HayFilas));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayPropuesta => _propuesta is not null;
    public bool HayFilas => _filas.Count > 0;

    public string TituloPropuesta => _propuesta is null ? "" :
        $"Provisión de {_propuesta.Ejercicio} · asiento del {Formatos.Fecha(_propuesta.Fecha)} y reversión del {Formatos.Fecha(_propuesta.FechaReversion)}" +
        (_situacion is { Estado: not EstadoFacturasPendientes.Pendiente } ? " (repetirlo sustituye los dos asientos)" : "");

    public IReadOnlyList<FilaProveedorPendienteVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas), nameof(HayFilas)); } }

    /// <summary>El asiento de la provisión resumido por cuenta y centro (el de verdad lleva un par de líneas por proveedor).</summary>
    public IReadOnlyList<FilaLineaPendienteVM> Lineas => _propuesta is not { } p ? [] : p.Provision
        .GroupBy(l => (l.Cuenta, l.IdCentroCoste, Debe: l.Debe != 0m))
        .OrderBy(g => g.Key.Debe ? 0 : 1).ThenBy(g => g.Key.Cuenta, StringComparer.Ordinal)
        .Select(g => new FilaLineaPendienteVM(g.Key.Cuenta, NombreCentro(g.Key.IdCentroCoste),
            g.Count() == 2 ? g.First().Concepto : $"{Formatos.Plural(g.Count(), "proveedor", "proveedores")} pagados sin factura",
            Formatos.ImporteOVacio(g.Sum(l => l.Debe)), Formatos.ImporteOVacio(g.Sum(l => l.Haber))))
        .ToList();

    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);

    public string Resumen => _propuesta is not { } p ? ""
        : p.Provision.Count == 0 ? "Sin provisión: no hay asiento"
        : $"{Formatos.Plural(p.Incluidas.Count(), "proveedor", "proveedores")} · base supuesta {Formatos.Importe(p.Total)}";

    public string TextoReversion => _propuesta is not { Provision.Count: > 0 } p ? "" :
        $"El {Formatos.Fecha(p.FechaReversion)}, en {p.Ejercicio + 1}, el mismo asiento al revés (la {p.Configuracion.CuentaPendientes} al Debe y el gasto al Haber), para que la factura real se contabilice con normalidad.";

    public IReadOnlyList<string> Avisos => [.. _errorBase is null ? [] : new[] { _errorBase }, .. _propuesta?.Avisos ?? []];
    public bool HayAvisos => Avisos.Count > 0;

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value)) Avisar(nameof(PorQueNoConfirmar)); } }

    private bool Repetir => _situacion is { Estado: not EstadoFacturasPendientes.Pendiente };
    public bool PideMotivo => _situacion is not null && ServicioFacturasPendientes.PorQueMotivo(_situacion.EjercicioTerminado, Repetir) is not null;
    public string AyudaMotivo => _situacion is null ? "" : (ServicioFacturasPendientes.PorQueMotivo(_situacion.EjercicioTerminado, Repetir) ?? "") + " El motivo queda en los asientos y en su historial.";
    public string TextoConfirmar => Repetir ? "Repetir" : "Confirmar";
    public string AyudaConfirmar => PorQueNoConfirmar ?? (Repetir
        ? "Sustituye la provisión del 31/12 y su reversión del 1/1 por exactamente lo que se ve, y guarda las casillas y bases cambiadas"
        : "Graba la provisión del 31/12 y su reversión del 1/1 tal como se ven, y guarda las casillas y bases cambiadas");

    public string? PorQueNoConfirmar =>
        _propuesta is null ? "Espera a que se calcule la propuesta."
        : _situacion is { ExisteSiguiente: false } ? $"Falta el ejercicio {Ejercicio + 1}, que recibe la reversión: créalo con «{TextoCrearSiguiente}» en la cabecera."
        : _errorBase is not null ? _errorBase
        : _propuesta.Provision.Count == 0 && _situacion is { Estado: EstadoFacturasPendientes.Pendiente } ? "No hay nada que provisionar."
        : PideMotivo && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo." : null;

    private string NombreCentro(int? id) => id is int c ? _centros.TryGetValue(c, out var n) && n.Length > 0 ? n : $"Centro {c}" : "";

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_situacion is null) Estado = EstadoVista.Cargando;
            Ocupado = true;
            _centros = await _ctx.Servicio.NombresCentrosAsync(_ctx.Trabajo);
            Situacion = await _ctx.Servicio.SituacionAsync(_ctx.Trabajo);
            _motivo = "";
            Avisar(nameof(Motivo));
            Mostrar(Situacion!.Propuesta);
            Estado = EstadoVista.Contenido;
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer las facturas pendientes de recibir: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private void Mostrar(PropuestaFacturasPendientes p)
    {
        _errorBase = null;
        Filas = p.Filas.Select(f => new FilaProveedorPendienteVM(f, NombreCentro(f.IdCentroCoste), Recalcular)).ToList();
        Propuesta = p;
    }

    /// <summary>Al marcar una casilla o escribir una base: la propuesta con esos ajustes, sin volver a leer nada.</summary>
    private void Recalcular()
    {
        if (_propuesta is null) return;
        var mal = _filas.FirstOrDefault(f => f.Incluido && f.BaseEscrita is not > 0m);
        _errorBase = mal is null ? null : $"La base de {mal.Cuenta} {mal.Nombre} no es un importe mayor que cero: escríbela bien (por ejemplo 100,00) o desmarca el proveedor.";
        var ajustes = _filas.Select(f => new AjusteFacturaPendiente(f.Cuenta, !f.Incluido, f.Incluido ? f.BaseEscrita : f.Datos.BaseManual)).ToList();
        var p = CalculoFacturasPendientes.Aplicar(_propuesta, ajustes);
        foreach (var (fila, datos) in _filas.Zip(p.Filas)) fila.Actualizar(datos);
        Propuesta = p;
    }

    private void Descartar()
    {
        if (_situacion is null) return;
        Motivo = "";
        Mostrar(_situacion.Propuesta);
    }

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p || _situacion is null) return;
        var detalles = new List<string>
        {
            Resumen,
            $"Provisión del {Formatos.Fecha(p.Fecha)} en {p.Ejercicio}: el gasto al Debe y la {p.Configuracion.CuentaPendientes} al Haber, un par de líneas por proveedor.",
            $"Reversión del {Formatos.Fecha(p.FechaReversion)} en {p.Ejercicio + 1}: el mismo asiento al revés.",
            "Los proveedores no se tocan: su saldo se cancela cuando llegue la factura real.",
        };
        var excluidos = p.Filas.Count(f => !f.Incluido);
        if (excluidos > 0 || p.Incluidas.Any(f => f.BaseManual is not null))
            detalles.Add("Se guardan las casillas desmarcadas y las bases escritas: se respetan al volver a proponer.");
        var mensaje = p.Provision.Count > 0
            ? "Se graban los dos asientos tal como se ven en la propuesta."
            : "No hay nada que provisionar: se borran los asientos anteriores de este proceso.";
        if (_dialogos.Preguntar($"{TextoConfirmar} las facturas pendientes de recibir", mensaje, detalles,
                new OpcionMensaje(TextoConfirmar, "Graba la provisión y su reversión", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, p.Ajustes, string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se han grabado las facturas pendientes de recibir", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            _ctx.Avisar(c.IdProvision is int id
                ? $"Facturas pendientes de recibir de {p.Ejercicio}: {Formatos.Importe(p.Total)} en el asiento {id} (31/12) y su reversión en el {c.IdReversion} (1/1/{p.Ejercicio + 1})." +
                  (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : "")
                : $"Facturas pendientes de recibir de {p.Ejercicio}: nada que provisionar; no queda ningún asiento.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se han grabado las facturas pendientes de recibir", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        var motivo = _dialogos.PedirMotivo($"Deshacer las facturas pendientes de recibir de {Ejercicio}",
            $"Se borran la provisión del 31/12 y su reversión del 1/1/{Ejercicio + 1}. Las casillas y bases cambiadas se conservan para la próxima propuesta.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se han deshecho las facturas pendientes de recibir", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshechas las facturas pendientes de recibir de {Ejercicio}: {Formatos.Plural(r.Valor, "asiento borrado", "asientos borrados")}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se han deshecho las facturas pendientes de recibir", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task CrearSiguienteAsync()
    {
        var nuevo = _ctx.NuevoEjercicio;
        PrevisionNuevoEjercicio prevision;
        try { prevision = await nuevo.PrevisionAsync(_ctx.Trabajo); }
        catch (Exception ex) { _dialogos.Error($"No se ha creado el ejercicio {Ejercicio + 1}", ex.Message); return; }
        if (_dialogos.Preguntar($"Crear el ejercicio {prevision.Ejercicio}", $"Se copia de {Ejercicio}. Repetirlo no duplica nada: solo añade lo que falte.",
                AccionCrearEjercicio.Detalle(prevision, Ejercicio),
                new OpcionMensaje($"Crear {prevision.Ejercicio}", "Crea el ejercicio con lo que se lista", Principal: true), new OpcionMensaje("Cancelar", "No crea nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            var r = await nuevo.CrearSiguienteAsync(_ctx.Trabajo);
            if (!r.EsCorrecto) { _dialogos.Error($"No se ha creado el ejercicio {Ejercicio + 1}", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            var c = r.Valor;
            _ctx.Avisar($"Ejercicio {c.Ejercicio.Ejercicio} {(c.Creado ? "creado" : "completado")}: {Formatos.Plural(c.CuentasCopiadas, "cuenta copiada", "cuentas copiadas")} y " +
                $"{Formatos.Plural(c.SeriesCopiadas, "serie", "series")}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error($"No se ha creado el ejercicio {Ejercicio + 1}", ex.Message);
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task VerAsync(int? id)
    {
        if (id is int x && _ctx.VerAsiento is { } ver) await ver(x);
    }

    private void AbrirMayor(object? fila)
    {
        if (_ctx.AbrirMayor is not { } abrir) return;
        if (fila is FilaProveedorPendienteVM p) abrir([p.Cuenta]);
        else if (fila is FilaLineaPendienteVM l) abrir([l.Cuenta]);
    }
}
