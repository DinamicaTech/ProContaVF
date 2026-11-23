using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Contabilidad.CuentasAnuales;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.CuentasAnuales;

/// <summary>Contexto de la pantalla Cuentas anuales y lo que hace fuera de ella (el PDF lo genera la ventana principal con el motor de informes).</summary>
public sealed record ContextoCuentasAnuales(ServicioCuentasAnuales Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Action<string> Avisar)
{
    /// <summary>Genera el PDF de las cuentas (con el IdSociedadEjercicio de su ejercicio, para vincularlo), lo guarda en la carpeta de documentos y lo abre.</summary>
    public Func<CuentasAnualesPymes, int, Task>? GenerarPdf { get; init; }
    /// <summary>Abre el Mayor de unas subcuentas en un ejercicio.</summary>
    public Action<IReadOnlyList<string>, int>? AbrirMayor { get; init; }
    /// <summary>Abre el Cierre del ejercicio.</summary>
    public Action? AbrirCierre { get; init; }
}

/// <summary>Una fila de un estado en pantalla: la partida con su sangría y los importes de los dos ejercicios.</summary>
public sealed record FilaPartidaVM(FilaCuentasAnuales Fila)
{
    public string Clave => Fila.Partida.Clave;
    public string Partida => new string(' ', Math.Max(0, Fila.Partida.Nivel - 1) * 4) + Fila.Partida.Texto;
    public string Actual => Formatos.ImporteOVacio(Fila.Actual);
    public string Anterior => Formatos.ImporteOVacio(Fila.Anterior);
    public bool EsTotal => Fila.Partida.EsTotal;
    public bool Negativo => Fila.Actual < 0m;
    public string Ayuda => Fila.Partida.EsTotal
        ? $"Total: suma de las claves {string.Join(" + ", Fila.Partida.Sumandos)}"
        : Fila.CuentasActual.Count == 0 && Fila.CuentasAnterior.Count == 0 ? "Ninguna subcuenta va a esta partida" : "Selecciónala para ver abajo sus subcuentas";
}

/// <summary>Una subcuenta de la partida elegida, con lo que aporta en cada ejercicio.</summary>
public sealed record FilaSubcuentaVM(string Cuenta, string Nombre, string Actual, string Anterior);

/// <summary>Una comprobación en pantalla.</summary>
public sealed record ComprobacionVM(ComprobacionCuentas C)
{
    public string Icono => C.Gravedad switch { GravedadComprobacion.Error => "✖", GravedadComprobacion.Aviso => "▲", _ => "✔" };
    public string Gravedad => C.Gravedad.ToString().ToUpperInvariant();
    public string Titulo => C.Titulo;
    public string Detalle => C.QueHacer is { Length: > 0 } q ? $"{C.Detalle} {q}" : C.Detalle;
}

/// <summary>
/// Pantalla Contabilidad › Fin de ejercicio › Cuentas anuales (contabilidad/procesos/cuentas-anuales 2026-11-23): el balance y la cuenta de
/// pérdidas y ganancias del modelo pymes del Registro Mercantil del ejercicio elegido, con el anterior al lado y la clave oficial de cada
/// partida; las comprobaciones del Registro (activo = patrimonio neto y pasivo, resultado igual en los dos estados, subcuentas sin partida) y,
/// al elegir una partida, las subcuentas que la forman. «Generar PDF…» saca las páginas del modelo. Solo lee: no graba asientos.
/// </summary>
public sealed class CuentasAnualesVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoCuentasAnuales _ctx;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private IReadOnlyList<EjercicioCuentas> _ejercicios = [];
    private EjercicioCuentas? _ejercicio;
    private CuentasAnualesPymes? _cuentas;
    private FilaPartidaVM? _seleccionada;
    private bool _cargandoEjercicios;

    public CuentasAnualesVM(ContextoCuentasAnuales ctx, Action? alCerrar = null)
    {
        _ctx = ctx;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        PdfCommand = new ComandoDelegado(() => _ = PdfAsync(), () => !_ocupado && _cuentas is not null && _ctx.GenerarPdf is not null);
        MayorCommand = new ComandoDelegado(AbrirMayor, () => _ctx.AbrirMayor is not null && _seleccionada is { EsTotal: false } s && SubcuentasDe(s).Count > 0);
        CierreCommand = new ComandoDelegado(() => _ctx.AbrirCierre?.Invoke(), () => _ctx.AbrirCierre is not null);
    }

    // ---------- pestaña ----------
    public string Titulo => "Cuentas anuales";
    public string Ayuda => $"Cuentas anuales (modelo pymes del Registro Mercantil) de {_ctx.RazonSocial}";
    public string Icono => "book";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaCuentasAnuales { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand PdfCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand CierreCommand { get; }

    // ---------- estado ----------
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public IReadOnlyList<EjercicioCuentas> Ejercicios { get => _ejercicios; private set => Fijar(ref _ejercicios, value); }

    /// <summary>El ejercicio de las cuentas; cambiarlo las vuelve a calcular.</summary>
    public EjercicioCuentas? Ejercicio
    {
        get => _ejercicio;
        set
        {
            if (!Fijar(ref _ejercicio, value) || _cargandoEjercicios || value is null) return;
            _ = CalcularAsync();
        }
    }

    public string Subtitulo => _cuentas is null ? _ctx.RazonSocial
        : $"{_ctx.RazonSocial} · NIF {(_cuentas.Nif.Length > 0 ? _cuentas.Nif : "sin NIF")} · modelo pymes del Registro Mercantil";

    public string ColumnaActual => _cuentas is null ? "Ejercicio" : $"Ejercicio {_cuentas.Ejercicio}";
    public string ColumnaAnterior => _cuentas is null ? "Ejercicio anterior" : $"Ejercicio {_cuentas.Ejercicio - 1}";

    public IReadOnlyList<FilaPartidaVM> Activo { get; private set; } = [];
    public IReadOnlyList<FilaPartidaVM> PatrimonioNetoYPasivo { get; private set; } = [];
    public IReadOnlyList<FilaPartidaVM> PerdidasYGanancias { get; private set; } = [];
    public IReadOnlyList<ComprobacionVM> Comprobaciones { get; private set; } = [];

    /// <summary>Errores y avisos, primero los errores; las correctas, en su resumen.</summary>
    public IReadOnlyList<ComprobacionVM> Incidencias => Comprobaciones.Where(c => c.C.Gravedad != GravedadComprobacion.Correcta)
        .OrderBy(c => c.C.Gravedad == GravedadComprobacion.Error ? 0 : 1).ToList();

    public bool HayIncidencias => Incidencias.Count > 0;

    public string ResumenComprobaciones
    {
        get
        {
            if (_cuentas is null) return "";
            var errores = Comprobaciones.Count(c => c.C.Gravedad == GravedadComprobacion.Error);
            var avisos = Comprobaciones.Count(c => c.C.Gravedad == GravedadComprobacion.Aviso);
            var cifras = $"Activo {Formatos.Importe(_cuentas.Actual.TotalActivo)} · patrimonio neto y pasivo {Formatos.Importe(_cuentas.Actual.TotalPatrimonioNetoYPasivo)} · resultado {Formatos.Importe(_cuentas.Actual.Resultado)}";
            return errores > 0 ? $"{cifras}. {errores} {(errores == 1 ? "error impide" : "errores impiden")} presentarlas: se explican aquí debajo."
                : avisos > 0 ? $"{cifras}. Cuadran; {avisos} {(avisos == 1 ? "aviso" : "avisos")} que revisar."
                : $"{cifras}. Cuadran y pasan las comprobaciones del Registro.";
        }
    }

    public string ClaseResumen => _cuentas is null ? "" : _cuentas.TieneErrores ? "ERROR" : Comprobaciones.Any(c => c.C.Gravedad == GravedadComprobacion.Aviso) ? "AVISO" : "CORRECTA";

    /// <summary>La partida elegida en cualquiera de las tablas; abajo salen sus subcuentas.</summary>
    public FilaPartidaVM? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (!Fijar(ref _seleccionada, value)) return;
            Avisar(nameof(Subcuentas), nameof(TituloSubcuentas), nameof(HaySubcuentas));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string TituloSubcuentas => _seleccionada is null ? "Elige una partida para ver sus subcuentas"
        : _seleccionada.EsTotal ? $"{_seleccionada.Fila.Partida.Texto}: es un total ({_seleccionada.Ayuda.ToLowerInvariant()})"
        : $"Subcuentas de «{_seleccionada.Fila.Partida.Texto}» (clave {_seleccionada.Clave})";

    public bool HaySubcuentas => Subcuentas.Count > 0;

    public IReadOnlyList<FilaSubcuentaVM> Subcuentas => _seleccionada is { EsTotal: false } s ? SubcuentasDe(s) : [];

    private static IReadOnlyList<FilaSubcuentaVM> SubcuentasDe(FilaPartidaVM s)
    {
        var actual = s.Fila.CuentasActual.ToDictionary(c => c.Cuenta, StringComparer.Ordinal);
        var anterior = s.Fila.CuentasAnterior.ToDictionary(c => c.Cuenta, StringComparer.Ordinal);
        return actual.Keys.Union(anterior.Keys).Order(StringComparer.Ordinal).Select(k => new FilaSubcuentaVM(k,
            actual.GetValueOrDefault(k)?.Nombre ?? anterior.GetValueOrDefault(k)?.Nombre ?? "",
            Formatos.ImporteOVacio(actual.GetValueOrDefault(k)?.Importe ?? 0m), Formatos.ImporteOVacio(anterior.GetValueOrDefault(k)?.Importe ?? 0m))).ToList();
    }

    public CuentasAnualesPymes? Cuentas => _cuentas;

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_cuentas is null) Estado = EstadoVista.Cargando;
            var ejercicios = await _ctx.Servicio.EjerciciosAsync(_ctx.Trabajo.IdSociedad);
            _cargandoEjercicios = true;
            Ejercicios = ejercicios;
            var anio = _ejercicio?.Ejercicio ?? ServicioCuentasAnuales.EjercicioPropuesto(ejercicios, _ctx.Trabajo.Ejercicio);
            Ejercicio = ejercicios.FirstOrDefault(e => e.Ejercicio == anio) ?? ejercicios.FirstOrDefault();
            _cargandoEjercicios = false;
            await CalcularAsync();
        }
        catch (Exception ex)
        {
            _cargandoEjercicios = false;
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los ejercicios de la sociedad: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    private async Task CalcularAsync()
    {
        if (_ejercicio is not { } e)
        {
            Estado = EstadoVista.Vacio;
            MensajeEstado = "La sociedad no tiene ejercicios: créalos en Configuración › Parámetros y tablas › Ejercicios.";
            return;
        }
        Ocupado = true;
        try
        {
            var clave = _seleccionada?.Clave;
            var cuentas = await Task.Run(() => _ctx.Servicio.GenerarAsync(new ContextoSociedadEjercicio(_ctx.Trabajo.IdSociedad, e.IdSociedadEjercicio, e.Ejercicio)));
            Mostrar(cuentas);
            Seleccionada = clave is null ? null : Activo.Concat(PatrimonioNetoYPasivo).Concat(PerdidasYGanancias).FirstOrDefault(f => f.Clave == clave);
            Estado = EstadoVista.Contenido;
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido calcular las cuentas anuales de {e.Ejercicio}: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Pone en pantalla unas cuentas ya calculadas (también lo usan las pruebas).</summary>
    public void Mostrar(CuentasAnualesPymes cuentas)
    {
        _cuentas = cuentas;
        Activo = cuentas.Activo.Select(f => new FilaPartidaVM(f)).ToList();
        PatrimonioNetoYPasivo = cuentas.PatrimonioNetoYPasivo.Select(f => new FilaPartidaVM(f)).ToList();
        PerdidasYGanancias = cuentas.PerdidasYGanancias.Select(f => new FilaPartidaVM(f)).ToList();
        Comprobaciones = cuentas.Comprobaciones.Select(c => new ComprobacionVM(c)).ToList();
        Avisar(nameof(Cuentas), nameof(Activo), nameof(PatrimonioNetoYPasivo), nameof(PerdidasYGanancias), nameof(Comprobaciones), nameof(Incidencias), nameof(HayIncidencias),
            nameof(ResumenComprobaciones), nameof(ClaseResumen), nameof(Subtitulo), nameof(ColumnaActual), nameof(ColumnaAnterior), nameof(TextoPdf));
        CommandManager.InvalidateRequerySuggested();
    }

    public string TextoPdf => _cuentas?.TieneErrores == true
        ? "Genera el PDF igualmente, como borrador para revisar: con errores, el Registro no admitiría estas cuentas"
        : "Genera en PDF las páginas del modelo oficial (balance BP1 y BP2 y pérdidas y ganancias PP) con las dos columnas, lo guarda en la carpeta de documentos del ejercicio y lo abre";

    private async Task PdfAsync()
    {
        if (_cuentas is null || _ejercicio is null || _ctx.GenerarPdf is null) return;
        Ocupado = true;
        try
        {
            await _ctx.GenerarPdf(_cuentas, _ejercicio.IdSociedadEjercicio);
        }
        catch (Exception ex)
        {
            _ctx.Avisar($"No he podido generar el PDF de las cuentas anuales: {ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void AbrirMayor()
    {
        if (_seleccionada is not { EsTotal: false } s || _cuentas is null) return;
        var cuentas = SubcuentasDe(s).Select(c => c.Cuenta).ToList();
        if (cuentas.Count > 0) _ctx.AbrirMayor?.Invoke(cuentas, _cuentas.Ejercicio);
    }
}
