using System.Windows;
using System.Windows.Input;
using ProConta.Contabilidad.CuentasAnuales;
using ProConta.Contabilidad.Legalizacion;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Legalizacion;

/// <summary>Un fichero ya generado y guardado: su contenido y dónde ha quedado.</summary>
public sealed record FicheroGuardado(FicheroLegalizacion Fichero, string Ruta);

/// <summary>Contexto de la pantalla Legalización de libros y lo que hace fuera de ella (los PDF los genera y guarda la ventana principal).</summary>
public sealed record ContextoLegalizacion(ServicioLegalizacion Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Action<string> Avisar)
{
    /// <summary>Genera los dos libros, los guarda en la carpeta de documentos del ejercicio y devuelve dónde; null si no se ha podido (ya lo ha explicado).</summary>
    public Func<DatosLegalizacion, Task<IReadOnlyList<FicheroGuardado>?>>? Generar { get; init; }
    /// <summary>Abre un fichero o una carpeta con el programa de Windows que le corresponde.</summary>
    public Action<string>? Abrir { get; init; }
    public Action? AbrirCierre { get; init; }
    public Action? AbrirCuentasAnuales { get; init; }
}

/// <summary>Una comprobación en pantalla.</summary>
public sealed record ComprobacionLegalizacionVM(ComprobacionLegalizacion C)
{
    public string Icono => C.Gravedad switch { GravedadLegalizacion.Error => "✖", GravedadLegalizacion.Aviso => "•", _ => "✔" };
    public string Titulo => C.Titulo;
    public string Detalle => C.QueHacer is { Length: > 0 } q ? $"{C.Detalle} {q}" : C.Detalle;
}

/// <summary>Un fichero generado en pantalla: nombre oficial, libro, páginas, tamaño y huella.</summary>
public sealed record FicheroVM(FicheroGuardado G)
{
    public string Nombre => G.Fichero.Nombre;
    public string Libro => G.Fichero.Libro;
    public string Paginas => G.Fichero.Paginas.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
    public string Tamano => G.Fichero.Contenido.Length >= 1024 * 1024
        ? $"{G.Fichero.Contenido.Length / 1024d / 1024d:N1} MB" : $"{Math.Max(1, G.Fichero.Contenido.Length / 1024):N0} KB";
    public string Huella => G.Fichero.HuellaBase64;
    public string HuellaHex => G.Fichero.HuellaHex;
    public string Ruta => G.Ruta;
}

/// <summary>
/// Pantalla Contabilidad › Fin de ejercicio › Legalización de libros (contabilidad/procesos/legalizacion-libros): para el ejercicio elegido
/// (por defecto, el último cerrado dentro de plazo) comprueba que se puede legalizar, genera <c>DIARIO_001.pdf</c> e <c>INV_CUEN_001.pdf</c>
/// con sus huellas SHA-256, los guarda en la carpeta de documentos del ejercicio y explica cómo y dónde presentarlos. Solo lee la contabilidad.
/// </summary>
public sealed class LegalizacionVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoLegalizacion _ctx;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private IReadOnlyList<EjercicioCuentas> _ejercicios = [];
    private EjercicioCuentas? _ejercicio;
    private DatosLegalizacion? _datos;
    private bool _cargandoEjercicios;
    private IReadOnlyList<FicheroVM> _ficheros = [];
    private FicheroVM? _seleccionado;

    public LegalizacionVM(ContextoLegalizacion ctx, Action? alCerrar = null)
    {
        _ctx = ctx;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        GenerarCommand = new ComandoDelegado(() => _ = GenerarAsync(), () => !_ocupado && _datos is not null && _ctx.Generar is not null);
        WebCommand = new ComandoDelegado(() => _ctx.Abrir?.Invoke(LegalizacionLibros.UrlPresentacion), () => _ctx.Abrir is not null);
        CarpetaCommand = new ComandoDelegado(() => _ctx.Abrir?.Invoke(System.IO.Path.GetDirectoryName(_ficheros[0].Ruta)!), () => _ctx.Abrir is not null && _ficheros.Count > 0);
        AbrirFicheroCommand = new ComandoDelegado(() => _ctx.Abrir?.Invoke(_seleccionado!.Ruta), () => _ctx.Abrir is not null && _seleccionado is not null);
        CopiarHuellaCommand = new ComandoDelegado(() => Copiar(_seleccionado!.Huella, $"Huella de {_seleccionado.Nombre} copiada."), () => _seleccionado is not null);
        CopiarHuellaHexCommand = new ComandoDelegado(() => Copiar(_seleccionado!.HuellaHex, $"Huella hexadecimal de {_seleccionado.Nombre} copiada."), () => _seleccionado is not null);
        CierreCommand = new ComandoDelegado(() => _ctx.AbrirCierre?.Invoke(), () => _ctx.AbrirCierre is not null);
        CuentasCommand = new ComandoDelegado(() => _ctx.AbrirCuentasAnuales?.Invoke(), () => _ctx.AbrirCuentasAnuales is not null);
    }

    // ---------- pestaña ----------
    public string Titulo => "Legalización de libros";
    public string Ayuda => $"Libros Diario y de Inventarios y Cuentas Anuales para legalizar en el Registro Mercantil · {_ctx.RazonSocial}";
    public string Icono => "book";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaLegalizacion { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand GenerarCommand { get; }
    public ICommand WebCommand { get; }
    public ICommand CarpetaCommand { get; }
    public ICommand AbrirFicheroCommand { get; }
    public ICommand CopiarHuellaCommand { get; }
    public ICommand CopiarHuellaHexCommand { get; }
    public ICommand CierreCommand { get; }
    public ICommand CuentasCommand { get; }

    // ---------- estado ----------
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }
    public IReadOnlyList<EjercicioCuentas> Ejercicios { get => _ejercicios; private set => Fijar(ref _ejercicios, value); }

    /// <summary>El ejercicio de los libros; cambiarlo vuelve a reunir los datos y quita los ficheros generados de otro ejercicio.</summary>
    public EjercicioCuentas? Ejercicio
    {
        get => _ejercicio;
        set
        {
            if (!Fijar(ref _ejercicio, value) || _cargandoEjercicios || value is null) return;
            _ = ReunirAsync();
        }
    }

    public DatosLegalizacion? Datos => _datos;
    public string Url => LegalizacionLibros.UrlPresentacion;

    public string Subtitulo => _datos is null ? _ctx.RazonSocial : $"{_datos.RazonSocial} · NIF {(_datos.Nif.Length > 0 ? _datos.Nif : "sin NIF")} · Registro Mercantil del domicilio social";

    public IReadOnlyList<ComprobacionLegalizacionVM> Comprobaciones { get; private set; } = [];

    public string Resumen
    {
        get
        {
            if (_datos is null) return "";
            var plazo = $"Plazo de presentación: hasta el {_datos.Plazo:dd/MM/yyyy}.";
            return _datos.TieneErrores
                ? $"{plazo} Hay errores que resolver antes de presentar los libros (se explican aquí debajo); puedes generarlos igualmente como borrador para revisarlos."
                : $"{plazo} Todo listo para generar los libros del ejercicio {_datos.Ejercicio}.";
        }
    }

    public string ClaseResumen => _datos is null ? "" : _datos.TieneErrores ? "ERROR"
        : _datos.Comprobaciones.Any(c => c.Gravedad == GravedadLegalizacion.Aviso) ? "AVISO" : "CORRECTA";

    public string TextoGenerar => _datos?.TieneErrores == true
        ? "Genera los dos libros como borrador para revisarlos: con los errores de arriba no conviene presentarlos"
        : $"Genera {LegalizacionLibros.FicheroDiario} (libro Diario) e {LegalizacionLibros.FicheroInventarios} (Inventarios y Cuentas Anuales) en PDF, " +
          "con sus huellas SHA-256, los guarda en la carpeta de documentos del ejercicio («Legalización») y los enseña aquí debajo";

    /// <summary>Los pasos para presentar los libros, con el plazo del ejercicio elegido.</summary>
    public IReadOnlyList<string> Pasos
    {
        get
        {
            var anio = _datos?.Ejercicio ?? _ejercicio?.Ejercicio ?? _ctx.Trabajo.Ejercicio - 1;
            var plazo = LegalizacionLibros.Plazo(anio);
            return
            [
                $"1. Genera los libros con «Generar los libros» y ábrelos para revisarlos. No los cambies ni los vuelvas a guardar: el Registro comprueba cada fichero por su huella.",
                "2. Entra en la web del Colegio de Registradores (botón «Abrir la web del Registro») con el certificado electrónico de la sociedad o del administrador. Hay que estar dado de alta como usuario.",
                "3. Elige la presentación telemática de libros para legalizar (desde la sede electrónica o con el programa Legalia 2, que está en «Documentación y descargas › Manuales y programas»), en el Registro Mercantil del domicilio social.",
                $"4. Añade {LegalizacionLibros.FicheroDiario} como libro «Diario» y {LegalizacionLibros.FicheroInventarios} como «Inventarios y Cuentas Anuales», los dos del 01/01/{anio} al 31/12/{anio}. Si te pide la huella, cópiala de la tabla (clic derecho).",
                $"5. Firma y envía la solicitud antes del {plazo:dd/MM/yyyy}. Guarda el justificante y, cuando llegue, la certificación de la legalización en Datos maestros › Documentos, vinculados al ejercicio {anio}.",
            ];
        }
    }

    public IReadOnlyList<FicheroVM> Ficheros { get => _ficheros; private set { if (Fijar(ref _ficheros, value)) { Avisar(nameof(HayFicheros), nameof(TituloFicheros)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool HayFicheros => _ficheros.Count > 0;
    public string TituloFicheros => _ficheros.Count == 0
        ? "Ficheros: aún no se han generado en esta sesión (si ya los generaste, están en la carpeta de documentos del ejercicio, en «Legalización»)"
        : $"Ficheros generados en {System.IO.Path.GetDirectoryName(_ficheros[0].Ruta)}";

    public FicheroVM? Seleccionado { get => _seleccionado; set { if (Fijar(ref _seleccionado, value)) CommandManager.InvalidateRequerySuggested(); } }

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_datos is null) Estado = EstadoVista.Cargando;
            var ejercicios = await _ctx.Servicio.EjerciciosAsync(_ctx.Trabajo.IdSociedad);
            _cargandoEjercicios = true;
            Ejercicios = ejercicios;
            var anio = _ejercicio?.Ejercicio ?? ServicioLegalizacion.EjercicioPropuesto(ejercicios, _ctx.Trabajo.Ejercicio, DateTime.Today);
            Ejercicio = ejercicios.FirstOrDefault(e => e.Ejercicio == anio) ?? ejercicios.FirstOrDefault();
            _cargandoEjercicios = false;
            await ReunirAsync();
        }
        catch (Exception ex)
        {
            _cargandoEjercicios = false;
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los ejercicios de la sociedad: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    private async Task ReunirAsync()
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
            if (_ficheros.Count > 0 && _datos?.Ejercicio != e.Ejercicio) Ficheros = [];
            var datos = await Task.Run(() => _ctx.Servicio.ReunirAsync(new ContextoSociedadEjercicio(_ctx.Trabajo.IdSociedad, e.IdSociedadEjercicio, e.Ejercicio)));
            Mostrar(datos);
            Estado = EstadoVista.Contenido;
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido reunir los libros de {e.Ejercicio}: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Pone en pantalla unos datos ya reunidos (también lo usan las pruebas).</summary>
    public void Mostrar(DatosLegalizacion datos)
    {
        _datos = datos;
        Comprobaciones = datos.Comprobaciones.OrderBy(c => c.Gravedad switch { GravedadLegalizacion.Error => 0, GravedadLegalizacion.Aviso => 1, _ => 2 })
            .Select(c => new ComprobacionLegalizacionVM(c)).ToList();
        Avisar(nameof(Datos), nameof(Comprobaciones), nameof(Resumen), nameof(ClaseResumen), nameof(Subtitulo), nameof(TextoGenerar), nameof(Pasos));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Pone en pantalla los ficheros ya generados (también lo usan las pruebas).</summary>
    public void MostrarFicheros(IReadOnlyList<FicheroGuardado> ficheros)
    {
        Ficheros = ficheros.Select(f => new FicheroVM(f)).ToList();
        Seleccionado = _ficheros.FirstOrDefault();
    }

    private async Task GenerarAsync()
    {
        if (_datos is null || _ctx.Generar is null) return;
        Ocupado = true;
        try
        {
            if (await _ctx.Generar(_datos) is { } ficheros) MostrarFicheros(ficheros);
        }
        catch (Exception ex)
        {
            _ctx.Avisar($"No he podido generar los libros: {ex.Message} Vuelve a pulsar «Generar los libros».");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Copiar(string texto, string aviso)
    {
        try
        {
            Clipboard.SetText(texto);
            _ctx.Avisar(aviso);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _ctx.Avisar("El portapapeles está ocupado por otro programa: vuelve a intentarlo en un momento.");
        }
    }
}
