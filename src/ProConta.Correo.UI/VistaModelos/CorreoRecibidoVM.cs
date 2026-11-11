using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Correo.UI.VistaModelos;

/// <summary>Fila del registro de correo recibido.</summary>
public sealed class FilaRecibidoVM(FilaRecibido datos) : ModeloVista
{
    public FilaRecibido Datos { get; } = datos;
    public int Id => Datos.IdMensaje;
    public string Fecha => Datos.FechaRecepcion.ToString("dd/MM/yyyy HH:mm");
    public DateTime FechaOrden => Datos.FechaRecepcion;
    public string Estado => ServicioRecepcionCorreo.Texto(Datos.Estado);
    public string De => Datos.Remitente ?? "";
    public string Asunto => Datos.Asunto ?? "";
    public string Adjuntos => Datos.Adjuntos == 0 ? "" : Datos.Adjuntos.ToString();
    public string Servidor => Datos.EliminadoServidor ? "Borrado" : "En el buzón";
    public bool ConIncidencia => Datos.Estado is EstadoRecibido.ConIncidencia or EstadoRecibido.NoAutorizado;
    public string AyudaEstado => Datos.Resumen ?? Estado;
}

/// <summary>
/// Registro de correo recibido (correo/pantallas, correo/recepcion): lo que ha llegado al buzón de la cuenta del ejercicio, qué
/// se ha hecho con cada adjunto (factura registrada y contabilizada, en espera de un módulo, no autorizado…), la respuesta
/// enviada y si ya se borró del servidor. Acciones en el menú contextual: volver a procesar, mirar el buzón ahora y abrir el
/// mensaje original o sus adjuntos.
/// </summary>
public sealed class CorreoRecibidoVM : ModeloVista, IPestanaTrabajo
{
    private readonly ServicioRecepcionCorreo _recepcion;
    private readonly IDialogosCorreo _dialogos;
    private readonly ContextoCorreo _contexto;
    private readonly Func<Task<IReadOnlyList<string>>> _recibirAhora;
    private readonly Action<int>? _verRespuesta;
    private readonly Action? _alCerrar;
    private List<FilaRecibidoVM> _todas = [];
    private FilaRecibidoVM? _activa;
    private DetalleRecibido? _detalle;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private string _texto = "";
    private bool _ocupado;
    private object? _vista;

    public CorreoRecibidoVM(ServicioRecepcionCorreo recepcion, IDialogosCorreo dialogos, ContextoCorreo contexto, Func<Task<IReadOnlyList<string>>> recibirAhora,
        Action<int>? verRespuesta = null, Action? alCerrar = null)
    {
        _recepcion = recepcion;
        _dialogos = dialogos;
        _contexto = contexto;
        _recibirAhora = recibirAhora;
        _verRespuesta = verRespuesta;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_activa?.Id));
        RecibirAhoraCommand = new ComandoDelegado(() => _ = RecibirAhoraAsync(), () => !_ocupado);
        ReprocesarCommand = new ComandoDelegado(() => _ = ReprocesarAsync(), () => !_ocupado && _activa?.Datos.Estado is EstadoRecibido.ConIncidencia or EstadoRecibido.NoAutorizado or EstadoRecibido.EnEspera);
        AbrirOriginalCommand = new ComandoDelegado(() => Abrir(Detalle?.RutaEmlCompleta, "el mensaje original"), () => Detalle is not null);
        VerRespuestaCommand = new ComandoDelegado(() => { if (Detalle is { IdMensajeRespuesta: not null } d) _verRespuesta?.Invoke(d.IdMensaje); }, () => _verRespuesta is not null && Detalle?.IdMensajeRespuesta is not null);
        AbrirAdjuntoCommand = new ComandoDelegado(p => { if (p is AdjuntoRecibido a) Abrir(a.RutaCompleta, $"«{a.Nombre}»"); });
    }

    // ---------- pestaña ----------

    public string Titulo => "Correo recibido";
    public string Ayuda => $"Correo recibido en la cuenta de {_contexto.RazonSocial} en {_contexto.Trabajo.Ejercicio}: qué se ha hecho con cada mensaje y sus adjuntos";
    public string Icono => "mail";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaCorreoRecibido { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- estado ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string TituloEstado => Estado == EstadoVista.Vacio ? (string.IsNullOrWhiteSpace(Texto) ? "Aún no ha llegado ningún correo" : "Ningún mensaje coincide") : "";
    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public ObservableCollection<FilaRecibidoVM> Filas { get; } = [];
    public string Resumen => _todas.Count == 0 ? "Sin mensajes" :
        $"{Formatos.Plural(_todas.Count, "mensaje", "mensajes")} · {_todas.Count(f => f.Datos.Estado == EstadoRecibido.Procesado)} procesados · " +
        $"{_todas.Count(f => f.Datos.Estado == EstadoRecibido.EnEspera)} en espera · {_todas.Count(f => f.ConIncidencia)} con incidencia";

    public FilaRecibidoVM? Activa
    {
        get => _activa;
        set
        {
            if (!Fijar(ref _activa, value)) return;
            _ = CargarDetalleAsync(value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public DetalleRecibido? Detalle { get => _detalle; private set { if (Fijar(ref _detalle, value)) Avisar(nameof(HayDetalle), nameof(DetalleEstado), nameof(DetalleDatos), nameof(DetalleAdjuntos), nameof(SinAdjuntos)); } }
    public bool HayDetalle => Detalle is not null;
    public string DetalleEstado => Detalle is null ? "" : ServicioRecepcionCorreo.Texto(Detalle.Estado);
    public string DetalleDatos => Detalle is null ? "" : string.Join("\n", new[]
    {
        $"De: {(Detalle.NombreRemitente is { } n ? $"{n} <{Detalle.Remitente}>" : Detalle.Remitente)}",
        $"Recibido el {Detalle.FechaRecepcion:dd/MM/yyyy HH:mm}" + (Detalle.FechaMensaje is { } f ? $" (enviado el {f:dd/MM/yyyy HH:mm})" : ""),
        Detalle.IdMensajeRespuesta is null ? "Sin respuesta enviada" : "Respuesta enviada (ver en Correo enviado)",
        Detalle.EliminadoServidor ? $"Borrado del servidor el {Detalle.FechaEliminacion:dd/MM/yyyy HH:mm}" : "Sigue en el buzón del servidor",
        $"UIDL: {Detalle.Uidl}" + (Detalle.MessageId is { } m ? $" · Message-ID: {m}" : ""),
    });
    public bool SinAdjuntos => Detalle is { Adjuntos.Count: 0 };
    public IReadOnlyList<AdjuntoRecibidoVM> DetalleAdjuntos => Detalle?.Adjuntos.Select(a => new AdjuntoRecibidoVM(a)).ToList() ?? [];

    public ICommand RefrescarCommand { get; }
    public ICommand RecibirAhoraCommand { get; }
    public ICommand ReprocesarCommand { get; }
    public ICommand AbrirOriginalCommand { get; }
    public ICommand VerRespuestaCommand { get; }
    public ICommand AbrirAdjuntoCommand { get; }

    public async Task CargarAsync(int? idActiva = null)
    {
        try
        {
            if (_todas.Count == 0) Estado = EstadoVista.Cargando;
            _todas = (await _recepcion.ListarAsync(_contexto.Trabajo.IdSociedadEjercicio)).Select(f => new FilaRecibidoVM(f)).ToList();
            Filtrar(idActiva ?? _activa?.Id);
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer el correo recibido: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    private void Filtrar(int? idActiva = null)
    {
        var t = Texto.Trim();
        var id = idActiva ?? _activa?.Id;
        Filas.Clear();
        foreach (var f in _todas.Where(f => t.Length == 0 || f.De.Contains(t, StringComparison.CurrentCultureIgnoreCase) || f.Asunto.Contains(t, StringComparison.CurrentCultureIgnoreCase)))
            Filas.Add(f);
        Estado = Filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        Activa = Filas.FirstOrDefault(f => f.Id == id) ?? Filas.FirstOrDefault();
        Avisar(nameof(Resumen), nameof(TituloEstado));
    }

    private async Task CargarDetalleAsync(FilaRecibidoVM? fila)
    {
        if (fila is null) { Detalle = null; return; }
        try
        {
            var d = await _recepcion.ObtenerAsync(fila.Id);
            if (_activa == fila) Detalle = d;
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No he podido leer el detalle: {ex.Message}";
        }
    }

    private async Task RecibirAhoraAsync()
    {
        _ocupado = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            _contexto.Avisar("Mirando el buzón…");
            var avisos = await _recibirAhora();
            await CargarAsync();
            if (avisos.Count == 0) _contexto.Avisar("Buzón revisado.");
            else _dialogos.Preguntar("No he podido leer el buzón", string.Join(" ", avisos), null,
                new OpcionMensaje("Entendido", "Revisa la cuenta en Parámetros y tablas › Cuenta de correo («Probar recepción») y vuelve a intentarlo", Principal: true, Cancelar: true));
        }
        catch (Exception ex)
        {
            _dialogos.Preguntar("No he podido leer el buzón", $"{ex.Message} Vuelve a intentarlo; si sigue igual, prueba la cuenta en Parámetros y tablas › Cuenta de correo.", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        finally
        {
            _ocupado = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ReprocesarAsync()
    {
        if (_activa is not { } f) return;
        _ocupado = true;
        try
        {
            var hecho = await _recepcion.ReprocesarAsync(f.Id);
            await CargarAsync(f.Id);
            _contexto.Avisar(hecho ? "Mensaje procesado de nuevo; se ha enviado una respuesta nueva al remitente." : "El mensaje ya no estaba pendiente de volver a procesar.");
        }
        catch (Exception ex)
        {
            _dialogos.Preguntar("No se ha podido procesar", $"{ex.Message} Actualiza (F5) y vuelve a intentarlo.", null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        finally
        {
            _ocupado = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void Abrir(string? ruta, string que)
    {
        if (ruta is null || !File.Exists(ruta))
        {
            _dialogos.Preguntar("No lo encuentro", $"No encuentro {que} en {ruta ?? "la carpeta de documentos de la empresa"}.", null,
                new OpcionMensaje("Entendido", "Comprueba la carpeta de documentos de la empresa (Parámetros y tablas › Empresas)", Principal: true, Cancelar: true));
            return;
        }
        _dialogos.AbrirArchivo(ruta);
    }
}

/// <summary>Adjunto recibido para el detalle.</summary>
public sealed class AdjuntoRecibidoVM(AdjuntoRecibido a)
{
    public AdjuntoRecibido Adjunto { get; } = a;
    public string Nombre => Adjunto.Nombre;
    public string Estado => Adjunto.Tipo switch
    {
        TipoAdjuntoRecibido.FacturaCompra => "Factura de compra",
        TipoAdjuntoRecibido.Norma43 => "Extracto Norma 43",
        TipoAdjuntoRecibido.Nomina => "Nóminas",
        _ => "Otro",
    } + " · " + ServicioRecepcionCorreo.TextoEstado(Adjunto.Estado);
    public string Detalle => Adjunto.Detalle ?? "";
}
