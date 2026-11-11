using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Nucleo.Auditoria;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Modelos;

namespace ProConta.Correo.UI.VistaModelos;

/// <summary>Diálogos y ficheros del correo, en la vista, para que los modelos se prueben sin ventanas.</summary>
public interface IDialogosCorreo
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    /// <summary>Elige ficheros para adjuntar; vacío si se cancela.</summary>
    IReadOnlyList<string> ElegirFicheros();

    /// <summary>Abre un fichero con el programa predeterminado de Windows; si no puede, lo explica.</summary>
    void AbrirArchivo(string ruta);

    /// <summary>Pide un texto obligatorio (el motivo de una resolución); null si se cancela.</summary>
    string? PedirTexto(string titulo, string explicacion, string etiqueta, string textoBoton);
}

/// <summary>Adjunto del compositor: el contenido exacto que saldrá.</summary>
public sealed class AdjuntoCompositorVM(AdjuntoNuevo adjunto) : ModeloVista
{
    public AdjuntoNuevo Adjunto { get; } = adjunto;
    public string Nombre => Adjunto.Nombre;
    public string Tamano => TextoTamano(Adjunto.Contenido.LongLength);

    public static string TextoTamano(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{Math.Round(bytes / 1024m):0} KB",
        _ => (bytes / 1024m / 1024m).ToString("0.0", CultureInfo.GetCultureInfo("es-ES")) + " MB",
    };
}

/// <summary>Fase del compositor: redactando, enviando o con el resultado del envío.</summary>
public enum FaseCompositor { Redactando, Enviando, Enviado, Rechazado, Incierto, EnCola }

/// <summary>
/// Compositor individual (correo/pantallas, spec § 22 RF-COR-01/02): remitente fijo de la cuenta del ejercicio de origen,
/// Para, CC y CCO con validación junto a cada campo, asunto, texto y adjuntos. Enviar guarda el mensaje en la cola y lo
/// transmite al momento; mientras se envía, la edición se bloquea. Cancelar no envía nada.
/// </summary>
public sealed class CompositorCorreoVM : ModeloVista
{
    private readonly ServicioCorreo _servicio;
    private readonly IDialogosCorreo _dialogos;
    private readonly Actor _actor;
    private readonly MensajeNuevo _origen;
    private string _para, _cc, _cco, _asunto, _cuerpo;
    private bool _mostrarCc, _mostrarCco, _cambios;
    private FaseCompositor _fase = FaseCompositor.Redactando;
    private string? _resultado, _errorPara, _errorCc, _errorCco, _errorAsunto, _resumenErrores;
    private IReadOnlyList<string> _sinRellenar = [];

    public CompositorCorreoVM(ServicioCorreo servicio, IDialogosCorreo dialogos, Actor actor, MensajeNuevo borrador, string remitente, string? contexto, string? aviso, string? desvio)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _actor = actor;
        _origen = borrador;
        Remitente = remitente;
        Contexto = contexto;
        AvisoDestinatarios = aviso;
        Desvio = desvio;
        _para = Unir(borrador, TipoDestinatario.Para);
        _cc = Unir(borrador, TipoDestinatario.Cc);
        _cco = Unir(borrador, TipoDestinatario.Cco);
        _mostrarCc = _cc.Length > 0;
        _mostrarCco = _cco.Length > 0;
        _asunto = borrador.Asunto;
        _cuerpo = borrador.CuerpoTexto ?? (borrador.CuerpoHtml is null ? "" : TextoPlano.DeHtml(borrador.CuerpoHtml));
        foreach (var a in borrador.Adjuntos) Adjuntos.Add(new AdjuntoCompositorVM(a));
        RevisarPlantilla();

        EnviarCommand = new ComandoDelegado(() => _ = EnviarAsync(), () => Editable);
        MostrarCcCommand = new ComandoDelegado(() => MostrarCc = true, () => Editable && !MostrarCc);
        MostrarCcoCommand = new ComandoDelegado(() => MostrarCco = true, () => Editable && !MostrarCco);
        AnadirAdjuntoCommand = new ComandoDelegado(AnadirAdjuntos, () => Editable);
        QuitarAdjuntoCommand = new ComandoDelegado(p => { if (p is AdjuntoCompositorVM a && Editable) { Adjuntos.Remove(a); Marcar(); Avisar(nameof(TotalAdjuntos)); } });
        VerAdjuntoCommand = new ComandoDelegado(p => { if (p is AdjuntoCompositorVM a) VerAdjunto(a); });
    }

    // ---------- contenido ----------

    public string Remitente { get; }
    public string? Contexto { get; }
    public string? AvisoDestinatarios { get; }
    public string? Desvio { get; }
    public string Titulo => string.IsNullOrWhiteSpace(_origen.ReferenciaOrigen) ? "Nuevo correo" : $"Correo · {_origen.ReferenciaOrigen}";

    public string Para { get => _para; set { if (Fijar(ref _para, value ?? "")) { Marcar(); ErrorPara = null; } } }
    public string Cc { get => _cc; set { if (Fijar(ref _cc, value ?? "")) { Marcar(); ErrorCc = null; } } }
    public string Cco { get => _cco; set { if (Fijar(ref _cco, value ?? "")) { Marcar(); ErrorCco = null; } } }
    public bool MostrarCc { get => _mostrarCc; set => Fijar(ref _mostrarCc, value); }
    public bool MostrarCco { get => _mostrarCco; set => Fijar(ref _mostrarCco, value); }
    public string Asunto { get => _asunto; set { if (Fijar(ref _asunto, value ?? "")) { Marcar(); ErrorAsunto = null; RevisarPlantilla(); } } }
    public string Cuerpo { get => _cuerpo; set { if (Fijar(ref _cuerpo, value ?? "")) { Marcar(); RevisarPlantilla(); } } }
    public ObservableCollection<AdjuntoCompositorVM> Adjuntos { get; } = [];
    public string TotalAdjuntos => Adjuntos.Count == 0 ? "Sin adjuntos" : $"{Adjuntos.Count} {(Adjuntos.Count == 1 ? "adjunto" : "adjuntos")} · {AdjuntoCompositorVM.TextoTamano(Adjuntos.Sum(a => (long)a.Adjunto.Contenido.Length))}";

    public string? ErrorPara { get => _errorPara; private set => Fijar(ref _errorPara, value); }
    public string? ErrorCc { get => _errorCc; private set => Fijar(ref _errorCc, value); }
    public string? ErrorCco { get => _errorCco; private set => Fijar(ref _errorCco, value); }
    public string? ErrorAsunto { get => _errorAsunto; private set => Fijar(ref _errorAsunto, value); }
    public string? ResumenErrores { get => _resumenErrores; private set => Fijar(ref _resumenErrores, value); }

    /// <summary>Variables de la plantilla que siguen entre llaves: hay que completarlas antes de enviar.</summary>
    public string? AvisoPlantilla => _sinRellenar.Count == 0 ? null : $"Completa lo que sigue entre llaves antes de enviar: {string.Join(", ", _sinRellenar)}.";

    public bool TieneCambios => _cambios;

    // ---------- estado del envío ----------

    public FaseCompositor Fase
    {
        get => _fase;
        private set
        {
            if (!Fijar(ref _fase, value)) return;
            Avisar(nameof(Editable), nameof(Enviando), nameof(Terminado), nameof(TextoCerrar), nameof(TipoResultado));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool Editable => Fase is FaseCompositor.Redactando or FaseCompositor.Rechazado;
    public bool Enviando => Fase == FaseCompositor.Enviando;
    public bool Terminado => Fase is FaseCompositor.Enviado or FaseCompositor.Incierto or FaseCompositor.EnCola;
    public string TextoCerrar => Terminado ? "Cerrar" : "Cancelar";
    public string? Resultado { get => _resultado; private set => Fijar(ref _resultado, value); }
    public TipoNota TipoResultado => Fase switch
    {
        FaseCompositor.Enviado => TipoNota.Ok,
        FaseCompositor.Rechazado => TipoNota.Error,
        FaseCompositor.Incierto or FaseCompositor.EnCola => TipoNota.Aviso,
        _ => TipoNota.Info,
    };

    /// <summary>Id del mensaje en la cola, una vez enviado.</summary>
    public int? IdMensaje { get; private set; }

    public ICommand EnviarCommand { get; }
    public ICommand MostrarCcCommand { get; }
    public ICommand MostrarCcoCommand { get; }
    public ICommand AnadirAdjuntoCommand { get; }
    public ICommand QuitarAdjuntoCommand { get; }
    public ICommand VerAdjuntoCommand { get; }

    /// <summary>Se puede cerrar sin preguntar: no hay nada escrito sin enviar, o ya se ha enviado.</summary>
    public bool PuedeCerrarSinPreguntar => Terminado || !_cambios || Enviando;

    // ---------- acciones ----------

    /// <summary>Valida, guarda en la cola y envía ya. Con errores, los muestra junto a su campo y no envía.</summary>
    public async Task EnviarAsync()
    {
        if (!Editable) return;
        var mensaje = Construir();
        if (!Validar(mensaje)) return;
        Fase = FaseCompositor.Enviando;
        Resultado = "Enviando…";
        try
        {
            var encolado = await _servicio.EncolarAsync(mensaje, _actor);
            if (!encolado.EsCorrecto)
            {
                Fase = FaseCompositor.Rechazado;
                Resultado = string.Join(" ", encolado.Errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")));
                return;
            }
            IdMensaje = encolado.Valor;
            var r = await _servicio.EnviarAhoraAsync(encolado.Valor);
            (Fase, Resultado) = r.Estado switch
            {
                EstadoMensaje.Aceptado => (FaseCompositor.Enviado, r.Explicacion + " Eso no confirma que haya llegado al buzón del destinatario; cerrar esta ventana no cambia nada."),
                EstadoMensaje.AceptadoEnParte => (FaseCompositor.Enviado, r.Explicacion + " Mira en el registro de correo a quién no le ha llegado."),
                EstadoMensaje.Pendiente => (FaseCompositor.EnCola, r.Explicacion + " Puedes cerrar: queda en la cola y lo verás en el registro de correo."),
                EstadoMensaje.Incierto => (FaseCompositor.Incierto, r.Explicacion),
                // Fallo confirmado: no ha salido nada y lo escrito se conserva para corregir y volver a enviar sin duplicar.
                _ => (FaseCompositor.Rechazado, r.Explicacion + " No se ha enviado nada: corrige lo necesario y vuelve a enviar."),
            };
            if (Fase != FaseCompositor.Rechazado) _cambios = false;
        }
        catch (Exception ex)
        {
            Fase = IdMensaje is null ? FaseCompositor.Rechazado : FaseCompositor.EnCola;
            Resultado = IdMensaje is null
                ? $"No he podido guardar el mensaje: {ex.Message} No se ha enviado nada; vuelve a intentarlo."
                : $"El mensaje está guardado en la cola, pero no he podido enviarlo ahora: {ex.Message} Se enviará en la siguiente pasada de la cola.";
        }
    }

    internal MensajeNuevo Construir()
    {
        var destinatarios = new List<Destinatario>();
        destinatarios.AddRange(ReglasMensaje.Separar(Para).Select(d => new Destinatario(TipoDestinatario.Para, d, NombreDe(d))));
        if (MostrarCc) destinatarios.AddRange(ReglasMensaje.Separar(Cc).Select(d => new Destinatario(TipoDestinatario.Cc, d, NombreDe(d))));
        if (MostrarCco) destinatarios.AddRange(ReglasMensaje.Separar(Cco).Select(d => new Destinatario(TipoDestinatario.Cco, d, NombreDe(d))));
        return _origen with
        {
            Destinatarios = destinatarios,
            Asunto = Asunto.Trim(),
            CuerpoTexto = Cuerpo,
            CuerpoHtml = ServicioPlantillasCorreo.HtmlDeTexto(Cuerpo),
            Adjuntos = Adjuntos.Select(a => a.Adjunto).ToList(),
        };
    }

    /// <summary>Errores junto a cada campo y un resumen al pie; true si se puede enviar.</summary>
    internal bool Validar(MensajeNuevo m)
    {
        ErrorPara = ErrorCampo(Para, TipoDestinatario.Para, obligatorio: true);
        ErrorCc = MostrarCc ? ErrorCampo(Cc, TipoDestinatario.Cc, false) : null;
        ErrorCco = MostrarCco ? ErrorCampo(Cco, TipoDestinatario.Cco, false) : null;
        var errores = ReglasMensaje.Validar(m);
        ErrorAsunto = errores.FirstOrDefault(e => e.Codigo is "Correo.SinAsunto" or "Correo.AsuntoLargo")?.Mensaje;
        var repetidas = errores.Where(e => e.Codigo == "Correo.DireccionRepetida").Select(e => e.Mensaje + " " + e.AccionCorrectora).ToList();
        var lista = new[] { ErrorPara, ErrorCc, ErrorCco, ErrorAsunto }.Where(e => e is not null).Select(e => e!).Concat(repetidas).Concat(errores.Where(e => e.Codigo == "Correo.AdjuntoVacio").Select(e => e.Mensaje)).ToList();
        if (_sinRellenar.Count > 0) lista.Add(AvisoPlantilla!);
        ResumenErrores = lista.Count == 0 ? null : (lista.Count == 1 ? lista[0] : "Antes de enviar, revisa: " + string.Join(" · ", lista));
        return lista.Count == 0;
    }

    private static string? ErrorCampo(string texto, TipoDestinatario tipo, bool obligatorio)
    {
        var direcciones = ReglasMensaje.Separar(texto);
        if (direcciones.Count == 0) return obligatorio ? "Escribe al menos una dirección." : null;
        var malas = direcciones.Select(d => (d, ReglasMensaje.Normalizar(d).Motivo)).Where(x => x.Motivo is not null).ToList();
        return malas.Count == 0 ? null : string.Join(" ", malas.Select(x => $"«{x.d}»: {x.Motivo}"));
    }

    private string? NombreDe(string direccion) =>
        _origen.Destinatarios.FirstOrDefault(d => string.Equals(d.Direccion, direccion, StringComparison.OrdinalIgnoreCase))?.Nombre;

    private void AnadirAdjuntos()
    {
        foreach (var ruta in _dialogos.ElegirFicheros())
        {
            try
            {
                Adjuntos.Add(new AdjuntoCompositorVM(AdjuntoNuevo.DesdeFichero(ruta)));
                Marcar();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _dialogos.Preguntar("No he podido adjuntar el fichero", $"No he podido leer {ruta}: {ex.Message}", null,
                    new OpcionMensaje("Entendido", "Comprueba que el fichero no esté abierto en otro programa que lo bloquee y vuelve a adjuntarlo", Principal: true, Cancelar: true));
            }
        }
        Avisar(nameof(TotalAdjuntos));
    }

    private void VerAdjunto(AdjuntoCompositorVM a)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF", "Adjuntos", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, a.Nombre);
        File.WriteAllBytes(ruta, a.Adjunto.Contenido);
        _dialogos.AbrirArchivo(ruta);
    }

    private void RevisarPlantilla()
    {
        _sinRellenar = [.. ServicioPlantillasCorreo.SinRellenar(Asunto), .. ServicioPlantillasCorreo.SinRellenar(Cuerpo)];
        Avisar(nameof(AvisoPlantilla));
    }

    private void Marcar()
    {
        _cambios = true;
        if (Fase == FaseCompositor.Rechazado) Resultado = null;
    }

    private static string Unir(MensajeNuevo m, TipoDestinatario t) => string.Join("; ", m.Destinatarios.Where(d => d.Tipo == t).Select(d => d.Direccion));
}
