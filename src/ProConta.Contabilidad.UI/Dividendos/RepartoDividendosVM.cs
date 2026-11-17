using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Dividendos;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Dividendos;

/// <summary>Contexto de la pantalla Reparto de dividendos y lo que abre fuera de ella.</summary>
public sealed record ContextoRepartoDividendos(ServicioDividendos Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Lee los datos del acta de un reparto y dónde se guarda (null: sin acta).</summary>
    public ServicioActaDividendos? Actas { get; init; }
    /// <summary>Convierte el HTML del acta a PDF (el motor de informes, con WebView2).</summary>
    public Func<string, byte[]>? HtmlAPdf { get; init; }
    /// <summary>Abre un fichero con el programa de Windows que le corresponde.</summary>
    public Action<string>? AbrirFichero { get; init; }
}

/// <summary>Diálogos de la pantalla (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosRepartoDividendos
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);
    string? PedirMotivo(string titulo, string explicacion);
    void Error(string titulo, string mensaje);

    /// <summary>Pide la hora de la junta para el acta (null: cancelar). Por omisión, la propuesta.</summary>
    string? PedirHora(string titulo, string explicacion, string propuesta) => propuesta;
}

/// <summary>Fila de la lista: un reparto confirmado.</summary>
public sealed class FilaRepartoDividendosVM(RepartoGuardado datos) : ModeloVista
{
    public RepartoGuardado Datos { get; } = datos;
    public int IdReparto => Datos.IdReparto;
    public string Fecha => Formatos.Fecha(Datos.Fecha);
    public DateTime FechaOrden => Datos.Fecha;
    public string Socios => string.Join(", ", Datos.Socios.Select(s => s.Nombre));
    public string Neto => Formatos.Importe(Datos.Neto);
    public string Retencion => Formatos.Importe(Datos.Retencion);
    public string Bruto => Formatos.Importe(Datos.Bruto);
    public string PorcentajeRetencion => Datos.PorcentajeRetencion.ToString("0.##", Formatos.Cultura) + " %";
    public string Asiento => Datos.IdAsiento?.ToString(CultureInfo.InvariantCulture) ?? "";
    public string Situacion => $"Confirmado el {Datos.FechaHora:dd/MM/yyyy HH:mm}" + (Datos.Operador is { } o ? $" por {o}" : "") + (Datos.Motivo is { } m ? $". Última corrección: {m}" : "");
}

/// <summary>Un socio en la ficha del reparto: casilla, %, lo pagado, el neto editable y lo calculado.</summary>
public sealed class SocioRepartoVM : ModeloVista
{
    private readonly RepartoDividendosVM _padre;
    private bool _incluido;
    private string _netoTexto;
    private ResultadoSocioDividendos? _resultado;

    internal SocioRepartoVM(RepartoDividendosVM padre, SocioParaReparto datos)
    {
        _padre = padre;
        Datos = datos;
        _incluido = datos.Incluido;
        _netoTexto = Formatos.Numero(datos.NetoPropuesto);
    }

    public SocioParaReparto Datos { get; }
    public int IdSocio => Datos.IdSocio;
    public string Nombre => Datos.Nombre;
    public string Participacion => Datos.Participacion.ToString("0.####", Formatos.Cultura) + " %";
    public string Pagado => Formatos.ImporteOVacio(Datos.Pagado);
    public string Cuentas => Datos.Error is null ? $"{Datos.CuentaDividendos} · {Datos.CuentaRetencion}" : "";
    public bool Elegible => Datos.Error is null;

    public string Ayuda => Datos.Error ?? $"{Nombre}: {Participacion} del capital; dividendos a pagar en la {Datos.CuentaDividendos} y retención en la {Datos.CuentaRetencion}. " +
        (Datos.Pagado > 0m ? $"Ya tiene pagados {Formatos.Importe(Datos.Pagado)} a cuenta (saldo deudor de su 526 a la fecha)." : "Su 526 no tiene pagos a cuenta a la fecha.");

    /// <summary>Si entra en el reparto. Un socio sin subcuentas no puede entrar.</summary>
    public bool Incluido
    {
        get => _incluido;
        set { if (Elegible && Fijar(ref _incluido, value)) _padre.SociosCambiados(); }
    }

    /// <summary>Lo que percibe el socio (neto). Por defecto, lo que ya tiene pagado.</summary>
    public string NetoTexto
    {
        get => _netoTexto;
        set { if (Fijar(ref _netoTexto, value ?? "")) { Avisar(nameof(NetoValido)); _padre.SociosCambiados(); } }
    }

    public bool NetoValido => string.IsNullOrWhiteSpace(_netoTexto) || (Formatos.LeerImporte(_netoTexto, out var v) && v >= 0m);
    public decimal Neto => Formatos.LeerImporte(_netoTexto, out var v) ? v : 0m;

    internal void FijarNeto(decimal neto) { _netoTexto = Formatos.Numero(neto); Avisar(nameof(NetoTexto), nameof(NetoValido)); }

    internal void FijarResultado(ResultadoSocioDividendos? r) { _resultado = r; Avisar(nameof(Bruto), nameof(Retencion)); }

    public string Bruto => _resultado is { } r ? Formatos.Importe(r.Bruto) : "";
    public string Retencion => _resultado is { } r ? Formatos.Importe(r.Retencion) : "";
}

/// <summary>Línea del asiento propuesto.</summary>
public sealed record FilaLineaDividendosVM(string Cuenta, string Concepto, string Debe, string Haber);

/// <summary>
/// Pantalla Contabilidad › Reparto de dividendos (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): los repartos del ejercicio de
/// trabajo; «Nuevo reparto…» en la cabecera abre la ficha de uno nuevo y elegir uno de la lista abre la suya para corregirlo. La ficha tiene la
/// fecha, el neto total (que se reparte por participación), la tabla de socios con casilla y neto editable, el asiento propuesto y, al pie,
/// Descartar y Confirmar. El menú contextual del reparto elegido tiene Ver el asiento, Mayor de sus cuentas y Deshacer el reparto….
/// </summary>
public sealed class RepartoDividendosVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoRepartoDividendos _ctx;
    private readonly IDialogosRepartoDividendos _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private FilaRepartoDividendosVM? _seleccion;
    private bool _fichaAbierta;
    private int? _idReparto;
    private DatosReparto? _datos;
    private string _fechaTexto = "";
    private string _netoTotalTexto = "";
    private string _motivo = "";
    private PropuestaDividendos? _propuesta;
    private string? _errorPropuesta;
    private int _version;
    private bool _cargandoFicha;

    public RepartoDividendosVM(ContextoRepartoDividendos ctx, IDialogosRepartoDividendos dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_seleccion?.IdReparto));
        NuevoCommand = new ComandoDelegado(() => _ = NuevoAsync(), () => !_ocupado);
        RepartirCommand = new ComandoDelegado(Repartir, () => !_ocupado && _fichaAbierta && Socios.Any(s => s.Incluido));
        RecalcularCommand = new ComandoDelegado(() => _ = ProponerAsync(), () => !_ocupado && _fichaAbierta);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync(), () => !_ocupado && PorQueNoConfirmar is null);
        DescartarCommand = new ComandoDelegado(() => _ = DescartarAsync(), () => !_ocupado && _fichaAbierta);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => _seleccion?.Datos.IdAsiento is not null && _ctx.VerAsiento is not null);
        MayorCommand = new ComandoDelegado(AbrirMayor, () => _ctx.AbrirMayor is not null && _seleccion is not null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && _seleccion is not null);
        GenerarActaCommand = new ComandoDelegado(() => { if (_seleccion is { } f) _ = GenerarActaAsync(f.IdReparto); },
            () => !_ocupado && _seleccion is not null && _ctx.Actas is not null && _ctx.HtmlAPdf is not null);
    }

    // ---------- pestaña ----------
    public string Titulo => "Reparto de dividendos";
    public string Ayuda => $"Repartos de dividendos de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaRepartoDividendos { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand NuevoCommand { get; }
    public ICommand RepartirCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand GenerarActaCommand { get; }

    // ---------- lista ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}" + (_datos is { } d
        ? $" · retención {d.Configuracion.PorcentajeRetencion.ToString("0.##", Formatos.Cultura)} % · reservas {d.Configuracion.CuentaReservas}" : "");
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public ObservableCollection<FilaRepartoDividendosVM> Repartos { get; } = [];
    public bool HayRepartos => Repartos.Count > 0;
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public FilaRepartoDividendosVM? Seleccion
    {
        get => _seleccion;
        set
        {
            if (!Fijar(ref _seleccion, value)) return;
            CommandManager.InvalidateRequerySuggested();
            if (value is not null) _ = AbrirFichaAsync(value.IdReparto);
        }
    }

    // ---------- ficha ----------
    public bool FichaAbierta { get => _fichaAbierta; private set { if (Fijar(ref _fichaAbierta, value)) Avisar(nameof(TextoSinFicha)); } }
    public bool EsCorreccion => _idReparto is not null;
    public string TextoSinFicha => HayRepartos
        ? "Elige un reparto de la lista para ver y corregir su ficha, o pulsa «Nuevo reparto…» para repartir dividendos."
        : "Aún no hay repartos de dividendos en este ejercicio. Pulsa «Nuevo reparto…»: propone como neto de cada socio lo que ya tiene pagado a cuenta en su 526.";
    public string TituloFicha => _idReparto is int id ? $"Reparto {id} · corregir (regenera su asiento sin duplicarlo)" : "Nuevo reparto de dividendos";
    public ObservableCollection<SocioRepartoVM> Socios { get; } = [];
    public bool SinSocios => _fichaAbierta && Socios.Count == 0;

    public string FechaTexto
    {
        get => _fechaTexto;
        set { if (Fijar(ref _fechaTexto, value ?? "")) Avisar(nameof(PorQueNoConfirmar)); }
    }

    public string AyudaFecha => $"Fecha del reparto y de su asiento, dentro de {_ctx.Trabajo.Ejercicio} (dd/mm/aaaa). Al cambiarla, pulsa Intro: el neto propuesto de cada socio es lo que tiene pagado en su 526 hasta ese día";

    public string NetoTotalTexto { get => _netoTotalTexto; set => Fijar(ref _netoTotalTexto, value ?? ""); }

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value ?? "")) Avisar(nameof(PorQueNoConfirmar)); } }

    public PropuestaDividendos? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            foreach (var s in Socios) s.FijarResultado(value?.Socios.FirstOrDefault(r => r.IdSocio == s.IdSocio));
            Avisar(nameof(Propuesta), nameof(Lineas), nameof(Avisos), nameof(HayAvisos), nameof(TotalDebe), nameof(TotalHaber), nameof(Resumen), nameof(PorQueNoConfirmar));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? ErrorPropuesta { get => _errorPropuesta; private set { Fijar(ref _errorPropuesta, value); Avisar(nameof(HayErrorPropuesta), nameof(PorQueNoConfirmar)); } }
    public bool HayErrorPropuesta => _errorPropuesta is not null;

    public IReadOnlyList<FilaLineaDividendosVM> Lineas => _propuesta?.Lineas.Select(l => new FilaLineaDividendosVM(l.Cuenta, l.Concepto,
        Formatos.ImporteOVacio(l.Debe), Formatos.ImporteOVacio(l.Haber))).ToList() ?? [];
    public IReadOnlyList<string> Avisos => _propuesta?.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;
    public string TotalDebe => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalDebe);
    public string TotalHaber => _propuesta is null ? "" : Formatos.Importe(_propuesta.TotalHaber);
    public string Resumen => _propuesta is not { } p || p.Socios.Count == 0 ? "" :
        $"Neto {Formatos.Importe(p.Neto)} · retención {p.PorcentajeRetencion.ToString("0.##", Formatos.Cultura)} % {Formatos.Importe(p.Retencion)} · bruto {Formatos.Importe(p.Bruto)} de la {p.CuentaReservas}";

    public string TextoConfirmar => EsCorreccion ? "Corregir el reparto" : "Confirmar el reparto";

    /// <summary>Por qué no se puede confirmar ahora (null: se puede).</summary>
    public string? PorQueNoConfirmar =>
        !_fichaAbierta ? "Pulsa «Nuevo reparto…» o elige un reparto de la lista."
        : LeerFecha() is null ? $"Escribe la fecha del reparto (dd/mm/aaaa, dentro de {_ctx.Trabajo.Ejercicio})."
        : Socios.Any(s => s.Incluido && !s.NetoValido) ? "Hay un neto que no es un importe válido: escríbelo como 1.234,56."
        : _errorPropuesta is { } e ? e
        : _propuesta is null ? "Calculando la propuesta…"
        : _propuesta.Socios.Count == 0 ? "Marca algún socio y escribe su neto."
        : _propuesta.Fecha != LeerFecha() ? "Has cambiado la fecha: pulsa Intro o «Volver a calcular»."
        : EsCorreccion && string.IsNullOrWhiteSpace(_motivo) ? "Escribe el motivo de la corrección." : null;

    private DateTime? LeerFecha()
    {
        var t = (_fechaTexto ?? "").Trim();
        string[] formatos = ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d-M-yyyy", "ddMMyyyy"];
        return DateTime.TryParseExact(t, formatos, Formatos.Cultura, DateTimeStyles.None, out var f) && f.Year == _ctx.Trabajo.Ejercicio ? f.Date : null;
    }

    // ---------- carga ----------
    public async Task CargarAsync(int? elegir = null)
    {
        try
        {
            if (Repartos.Count == 0) Estado = EstadoVista.Cargando;
            var lista = await _ctx.Servicio.RepartosAsync(_ctx.Trabajo);
            Repartos.Clear();
            foreach (var r in lista) Repartos.Add(new FilaRepartoDividendosVM(r));
            Avisar(nameof(HayRepartos), nameof(TextoSinFicha));
            Estado = EstadoVista.Contenido;
            _seleccion = null;
            Avisar(nameof(Seleccion));
            if (elegir is int id && Repartos.FirstOrDefault(r => r.IdReparto == id) is { } fila) Seleccion = fila;
            else if (!_fichaAbierta || _idReparto is not null) CerrarFicha();
            if (_datos is null) { _datos = await _ctx.Servicio.DatosAsync(_ctx.Trabajo, _ctx.Servicio.FechaPropuesta(_ctx.Trabajo)); Avisar(nameof(Subtitulo)); }
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los repartos de dividendos: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    /// <summary>«Nuevo reparto…»: ficha vacía con la fecha de hoy y, como neto de cada socio, lo que tiene pagado en su 526.</summary>
    public async Task NuevoAsync()
    {
        _seleccion = null;
        Avisar(nameof(Seleccion));
        CommandManager.InvalidateRequerySuggested();
        await AbrirFichaAsync(null);
    }

    private void CerrarFicha()
    {
        _idReparto = null;
        _datos = _datos is null ? null : _datos with { Socios = [] };
        Socios.Clear();
        FichaAbierta = false;
        Propuesta = null;
        ErrorPropuesta = null;
        _motivo = "";
        Avisar(nameof(Motivo), nameof(SinSocios), nameof(EsCorreccion), nameof(TituloFicha), nameof(TextoConfirmar), nameof(PorQueNoConfirmar));
    }

    private async Task AbrirFichaAsync(int? idReparto, DateTime? fecha = null)
    {
        Ocupado = true;
        _cargandoFicha = true;
        try
        {
            var guardado = idReparto is int id ? Repartos.FirstOrDefault(r => r.IdReparto == id)?.Datos : null;
            var dia = fecha ?? guardado?.Fecha ?? _ctx.Servicio.FechaPropuesta(_ctx.Trabajo);
            _datos = await _ctx.Servicio.DatosAsync(_ctx.Trabajo, dia, idReparto);
            _idReparto = idReparto;
            _fechaTexto = Formatos.Fecha(dia);
            if (fecha is null) _motivo = "";
            Socios.Clear();
            foreach (var s in _datos.Socios) Socios.Add(new SocioRepartoVM(this, s));
            _netoTotalTexto = Formatos.Numero(Socios.Where(s => s.Incluido).Sum(s => s.Neto));
            FichaAbierta = true;
            Avisar(nameof(FechaTexto), nameof(NetoTotalTexto), nameof(Motivo), nameof(SinSocios), nameof(EsCorreccion), nameof(TituloFicha), nameof(TextoConfirmar), nameof(Subtitulo));
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido abrir el reparto", ex.Message);
            return;
        }
        finally
        {
            _cargandoFicha = false;
            Ocupado = false;
        }
        await ProponerAsync();
    }

    internal void SociosCambiados()
    {
        if (_cargandoFicha) return;
        _netoTotalTexto = Formatos.Numero(Socios.Where(s => s.Incluido).Sum(s => s.Neto));
        Avisar(nameof(NetoTotalTexto), nameof(PorQueNoConfirmar));
        CommandManager.InvalidateRequerySuggested();
        _ = ProponerAsync();
    }

    /// <summary>Reparte el neto total entre los socios marcados según su participación (resto mayor al céntimo).</summary>
    private void Repartir()
    {
        if (!Formatos.LeerImporte(_netoTotalTexto, out var total) || total < 0m)
        {
            _dialogos.Error("Neto total no válido", "Escribe el neto total que perciben los socios entre todos, como 100.000,00, y vuelve a pulsar «Repartir».");
            return;
        }
        var marcados = Socios.Where(s => s.Incluido).ToList();
        var partes = CalculoDividendos.Repartir(total, marcados.Select(s => s.Datos.Participacion).ToList());
        _cargandoFicha = true;
        for (var i = 0; i < marcados.Count; i++) marcados[i].FijarNeto(partes[i]);
        _cargandoFicha = false;
        SociosCambiados();
    }

    /// <summary>Calcula la propuesta con los netos de los socios marcados. Si la fecha ha cambiado, vuelve a leer lo pagado de cada socio.</summary>
    public async Task ProponerAsync()
    {
        if (!_fichaAbierta) return;
        var version = ++_version;
        ErrorPropuesta = null;
        if (LeerFecha() is not DateTime fecha) { Propuesta = null; ErrorPropuesta = $"La fecha no es válida o no es de {_ctx.Trabajo.Ejercicio}: escríbela como dd/mm/aaaa."; return; }
        if (_datos is not null && _datos.Fecha != fecha && !_cargandoFicha)
        {
            await AbrirFichaAsync(_idReparto, fecha);
            return;
        }
        if (Socios.Any(s => s.Incluido && !s.NetoValido)) { Propuesta = null; return; }
        var netos = Socios.Where(s => s.Incluido && s.Neto != 0m).Select(s => new NetoSocio(s.IdSocio, s.Neto)).ToList();
        if (netos.Count == 0) { Propuesta = null; ErrorPropuesta = Socios.Count == 0 ? SinSociosTexto : "Marca algún socio y escribe su neto (o un neto total y «Repartir»)."; return; }
        try
        {
            var r = await _ctx.Servicio.ProponerAsync(_ctx.Trabajo, fecha, netos, _idReparto);
            if (version != _version) return;
            if (!r.EsCorrecto) { Propuesta = null; ErrorPropuesta = r.Error!.TextoOperador(); return; }
            Propuesta = r.Valor;
        }
        catch (Exception ex)
        {
            if (version == _version) ErrorPropuesta = $"No he podido calcular la propuesta: {ex.Message}";
        }
    }

    public string SinSociosTexto => "La sociedad no tiene socios activos. Dales el rol Socio en su ficha de Terceros (pestaña Roles), con su participación, y vuelve a pulsar «Nuevo reparto…».";

    private async Task ConfirmarAsync()
    {
        if (_propuesta is not { } p || LeerFecha() is not DateTime fecha) return;
        var detalles = new List<string> { Resumen };
        detalles.AddRange(p.Avisos);
        if (_dialogos.Preguntar(TextoConfirmar, $"Se graba el asiento del {Formatos.Fecha(p.Fecha)} con {Formatos.Plural(p.Lineas.Count, "línea", "líneas")}, tal como se ve en la propuesta." +
                (EsCorreccion ? " Sustituye el asiento actual del reparto." : ""), detalles,
                new OpcionMensaje(TextoConfirmar, "Graba el asiento del reparto", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        int? elegir = _idReparto;
        try
        {
            var netos = p.Socios.Select(s => new NetoSocio(s.IdSocio, s.Neto)).ToList();
            var r = await _ctx.Servicio.ConfirmarAsync(_ctx.Trabajo, fecha, netos, _idReparto, string.IsNullOrWhiteSpace(_motivo) ? null : _motivo.Trim(), _ctx.Actor, p);
            if (!r.EsCorrecto)
            {
                _dialogos.Error("No se ha grabado el reparto", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var c = r.Valor;
            elegir = c.IdReparto;
            _ctx.Avisar($"Reparto de dividendos {c.IdReparto} grabado: asiento {c.IdAsiento}, bruto {Formatos.Importe(p.Bruto)}." +
                (c.CuentasCreadas.Count > 0 ? $" Cuentas creadas: {string.Join(", ", c.CuentasCreadas)}." : ""));
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha grabado el reparto", ex.Message);
            return;
        }
        finally { Ocupado = false; }
        await CargarAsync(elegir);
        if (elegir is int nuevo && _ctx.Actas is not null && _ctx.HtmlAPdf is not null
            && _dialogos.Preguntar("Acta del reparto", "¿Genero ahora el acta de la junta que aprueba este reparto?",
                ["Es un PDF con los socios, el administrador, el acuerdo con su importe en letras y las firmas. También puedes generarla después con clic derecho sobre el reparto › «Generar el acta…»."],
                new OpcionMensaje("Generar el acta", "Pide la hora de la junta y abre el PDF", Principal: true), new OpcionMensaje("Ahora no", "No genera nada", Cancelar: true)) == 0)
            await GenerarActaAsync(nuevo);
    }

    /// <summary>
    /// Acta de la junta que aprueba un reparto (contabilidad/procesos/aplicacion-resultado 2026-11-17): pide la hora, avisa de lo que falta en las
    /// fichas, genera el PDF en la carpeta de documentos de la empresa (&lt;ejercicio&gt;\Actas) y lo abre.
    /// </summary>
    public async Task GenerarActaAsync(int idReparto)
    {
        if (_ctx.Actas is not { } actas || _ctx.HtmlAPdf is not { } aPdf) return;
        var hora = _dialogos.PedirHora("Acta del reparto de dividendos", "Hora a la que se reúne la junta, como sale en el acta (hh:mm).", "11:00");
        if (hora is null) return;
        if (!ActaDividendos.HoraValida(hora)) { _dialogos.Error("No se ha generado el acta", $"La hora «{hora}» no es válida: escríbela como hh:mm, por ejemplo 11:00."); return; }
        Ocupado = true;
        try
        {
            var r = await actas.DatosAsync(_ctx.Trabajo, idReparto, hora);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha generado el acta", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            var datos = r.Valor;
            if (datos.Avisos.Count > 0 && _dialogos.Preguntar("Acta del reparto de dividendos", "Faltan datos en las fichas para que el acta salga completa:", datos.Avisos,
                    new OpcionMensaje("Generar igualmente", "Genera el acta con lo que hay", Principal: true), new OpcionMensaje("Cancelar", "Completa antes las fichas", Cancelar: true)) != 0)
                return;
            var ruta = await actas.RutaAsync(_ctx.Trabajo, datos);
            var html = ActaDividendos.Html(datos);
            var pdf = await Task.Run(() => aPdf(html));
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllBytesAsync(ruta, pdf);
            _ctx.Avisar($"Acta del reparto guardada en {ruta}.");
            _ctx.AbrirFichero?.Invoke(ruta);
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha generado el acta", ex.Message + " Si el PDF está abierto, ciérralo y vuelve a generarla.");
        }
        finally { Ocupado = false; }
    }

    private async Task DescartarAsync()
    {
        if (_idReparto is int id) await AbrirFichaAsync(id);
        else CerrarFicha();
    }

    private async Task DeshacerAsync()
    {
        if (_seleccion is not { } fila) return;
        var motivo = _dialogos.PedirMotivo($"Deshacer el reparto del {fila.Fecha}",
            $"Se borra su asiento (bruto {fila.Bruto} de reservas y retención {fila.Retencion}) y el reparto. Los pagos a los socios no se tocan: vuelven a quedar como pagos a cuenta en su 526.");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeshacerAsync(_ctx.Trabajo, fila.IdReparto, motivo, _ctx.Actor);
            if (!r.EsCorrecto) { _dialogos.Error("No se ha deshecho el reparto", string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _ctx.Avisar($"Deshecho el reparto de dividendos del {fila.Fecha}.");
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha deshecho el reparto", ex.Message);
        }
        finally { Ocupado = false; }
        CerrarFicha();
        await CargarAsync();
    }

    private async Task VerAsientoAsync()
    {
        if (_seleccion?.Datos.IdAsiento is int id && _ctx.VerAsiento is { } ver) await ver(id);
    }

    private void AbrirMayor()
    {
        if (_seleccion is not { } f) return;
        var cuentas = new[] { f.Datos.CuentaReservas }.Concat(f.Datos.Socios.SelectMany(s => new[] { s.CuentaDividendos, s.CuentaRetencion })).Distinct().ToList();
        _ctx.AbrirMayor?.Invoke(cuentas);
    }
}
