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
public sealed record ContextoCuentasAnuales(ServicioCuentasAnuales Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, string Operador, Action<string> Avisar)
{
    /// <summary>Genera el PDF de las cuentas con su memoria (y el IdSociedadEjercicio de su ejercicio, para vincularlo), lo guarda en la carpeta de documentos y lo abre.</summary>
    public Func<CuentasAnualesPymes, IReadOnlyList<ApartadoMemoria>, int, Task>? GenerarPdf { get; init; }
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

/// <summary>Un apartado de la memoria en pantalla: el texto se edita y se guarda; las tablas se ven como texto.</summary>
public sealed class ApartadoMemoriaVM(ApartadoMemoria apartado) : ModeloVista
{
    private string _texto = apartado.Texto;

    public ApartadoMemoria Apartado { get; private set; } = apartado;
    public string Encabezado => Apartado.Encabezado + (TieneCambios ? " *" : Apartado.Guardado ? "" : " (propuesto)");
    public string Texto { get => _texto; set { if (Fijar(ref _texto, value)) Avisar(nameof(TieneCambios), nameof(Encabezado)); } }
    public bool TieneCambios => _texto != Apartado.Texto;
    public string Tablas => string.Join(Environment.NewLine + Environment.NewLine, Apartado.Tablas.Select(TextoTabla));
    public bool HayTablas => Apartado.Tablas.Count > 0;

    public void Guardado(ApartadoMemoria nuevo)
    {
        Apartado = nuevo;
        _texto = nuevo.Texto;
        Avisar(nameof(Apartado), nameof(Texto), nameof(TieneCambios), nameof(Encabezado), nameof(Tablas));
    }

    /// <summary>La tabla en columnas de ancho fijo, para la vista previa.</summary>
    public static string TextoTabla(TablaMemoria t)
    {
        var ancho = Math.Max(12, t.Filas.Select(f => f.Concepto.Length).DefaultIfEmpty(0).Max());
        var col = Math.Max(16, t.Columnas.Select(c => c.Length).DefaultIfEmpty(0).Max() + 2);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(t.Titulo);
        sb.Append(new string(' ', ancho)).AppendLine(string.Concat(t.Columnas.Select(c => c.PadLeft(col))));
        foreach (var f in t.Filas)
            sb.Append(f.Concepto.PadRight(ancho)).AppendLine(string.Concat(f.Importes.Select(i => (i is decimal v ? Formatos.Numero(v) : "").PadLeft(col))));
        return sb.ToString().TrimEnd();
    }
}

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
    private IReadOnlyList<ApartadoMemoriaVM> _memoria = [];
    private ApartadoMemoriaVM? _apartado;

    public CuentasAnualesVM(ContextoCuentasAnuales ctx, Action? alCerrar = null)
    {
        _ctx = ctx;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        PdfCommand = new ComandoDelegado(() => _ = PdfAsync(), () => !_ocupado && _cuentas is not null && _ctx.GenerarPdf is not null);
        MayorCommand = new ComandoDelegado(AbrirMayor, () => _ctx.AbrirMayor is not null && _seleccionada is { EsTotal: false } s && SubcuentasDe(s).Count > 0);
        CierreCommand = new ComandoDelegado(() => _ctx.AbrirCierre?.Invoke(), () => _ctx.AbrirCierre is not null);
        GuardarApartadoCommand = new ComandoDelegado(() => _ = GuardarApartadoAsync(), () => !_ocupado && _apartado is { TieneCambios: true });
        PropuestoCommand = new ComandoDelegado(() => _ = VolverAlPropuestoAsync(), () => !_ocupado && _apartado is not null && (_apartado.Apartado.Guardado || _apartado.TieneCambios));
    }

    // ---------- pestaña ----------
    public string Titulo => "Cuentas anuales";
    public string Ayuda => $"Cuentas anuales (modelo pymes del Registro Mercantil) de {_ctx.RazonSocial}";
    public string Icono => "book";
    public bool TieneCambios => _memoria.Any(a => a.TieneCambios);
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaCuentasAnuales { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => _memoria.Where(a => a.TieneCambios).Select(a => $"Memoria: texto del apartado {a.Apartado.Encabezado} sin guardar").ToList();

    public async Task<bool> GuardarAsync()
    {
        foreach (var a in _memoria.Where(a => a.TieneCambios).ToList())
            if (!await GuardarAsync(a, a.Texto)) return false;
        return true;
    }

    public void Descartar()
    {
        foreach (var a in _memoria) a.Texto = a.Apartado.Texto;
    }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand PdfCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand CierreCommand { get; }
    public ICommand GuardarApartadoCommand { get; }
    public ICommand PropuestoCommand { get; }

    private int _pestana;

    /// <summary>Pestaña elegida (0 activo, 1 patrimonio neto y pasivo, 2 pérdidas y ganancias, 3 memoria); las subcuentas solo salen en los estados.</summary>
    public int Pestana { get => _pestana; set { if (Fijar(ref _pestana, value)) Avisar(nameof(VerSubcuentas)); } }

    public bool VerSubcuentas => _pestana != 3;

    // ---------- memoria ----------
    public IReadOnlyList<ApartadoMemoriaVM> Memoria { get => _memoria; private set => Fijar(ref _memoria, value); }

    /// <summary>El apartado de la memoria que se está editando.</summary>
    public ApartadoMemoriaVM? Apartado
    {
        get => _apartado;
        set { if (Fijar(ref _apartado, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    private async Task GuardarApartadoAsync()
    {
        if (_apartado is { } a) await GuardarAsync(a, a.Texto);
    }

    private async Task VolverAlPropuestoAsync()
    {
        if (_apartado is { } a) await GuardarAsync(a, null);
    }

    /// <summary>Guarda el texto de un apartado (null o igual al propuesto: vuelve al propuesto) y lo refresca.</summary>
    private async Task<bool> GuardarAsync(ApartadoMemoriaVM a, string? texto)
    {
        if (_ejercicio is not { } e) return false;
        if (texto is not null && texto.Trim() == a.Apartado.TextoPropuesto.Trim()) texto = null;
        Ocupado = true;
        try
        {
            await _ctx.Servicio.GuardarTextoMemoriaAsync(e.IdSociedadEjercicio, a.Apartado.Numero, texto, _ctx.Operador);
            a.Guardado(a.Apartado with { Texto = texto ?? a.Apartado.TextoPropuesto, Guardado = texto is not null });
            Avisar(nameof(TieneCambios));
            _ctx.Avisar(texto is null ? $"Memoria {e.Ejercicio}: el apartado {a.Apartado.Encabezado} vuelve al texto propuesto." : $"Memoria {e.Ejercicio}: guardado el apartado {a.Apartado.Encabezado}.");
            return true;
        }
        catch (Exception ex)
        {
            _ctx.Avisar($"No he podido guardar el apartado {a.Apartado.Encabezado} de la memoria: {ex.Message} El texto sigue en pantalla: vuelve a pulsar «Guardar el apartado».");
            return false;
        }
        finally
        {
            Ocupado = false;
        }
    }

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
            var memoria = await Task.Run(() => _ctx.Servicio.MemoriaAsync(_ctx.Trabajo.IdSociedad, e.IdSociedadEjercicio, cuentas));
            Mostrar(cuentas, memoria);
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
    public void Mostrar(CuentasAnualesPymes cuentas, IReadOnlyList<ApartadoMemoria>? memoria = null)
    {
        _cuentas = cuentas;
        var numero = _apartado?.Apartado.Numero;
        Memoria = (memoria ?? []).Select(a => new ApartadoMemoriaVM(a)).ToList();
        foreach (var a in _memoria)
            a.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName != nameof(ApartadoMemoriaVM.TieneCambios)) return;
                Avisar(nameof(TieneCambios));
                CommandManager.InvalidateRequerySuggested();
            };
        Apartado = _memoria.FirstOrDefault(a => a.Apartado.Numero == numero) ?? _memoria.FirstOrDefault();
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
        : "Genera en PDF las páginas del modelo oficial (balance BP1 y BP2 y pérdidas y ganancias PP) con las dos columnas y la memoria (guarda antes los textos sin guardar), lo guarda en la carpeta de documentos del ejercicio y lo abre";

    private async Task PdfAsync()
    {
        if (_cuentas is null || _ejercicio is null || _ctx.GenerarPdf is null) return;
        if (TieneCambios && !await GuardarAsync()) return;
        Ocupado = true;
        try
        {
            await _ctx.GenerarPdf(_cuentas, _memoria.Select(a => a.Apartado).ToList(), _ejercicio.IdSociedadEjercicio);
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
