using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Correo.UI.VistaModelos;

/// <summary>Fila del registro de correo.</summary>
public sealed class FilaRegistroVM(FilaMensaje datos) : ModeloVista
{
    public FilaMensaje Datos { get; } = datos;
    public int Id => Datos.IdMensaje;
    public string Fecha => Datos.FechaCreacion.ToString("dd/MM/yyyy HH:mm");
    public DateTime FechaOrden => Datos.FechaCreacion;
    public string Estado => ServicioCorreo.Texto(Datos.Estado);
    public string Para => Datos.TotalDestinatarios > ReglasMensaje.Separar(Datos.Para).Count ? $"{Datos.Para} (+{Datos.TotalDestinatarios - ReglasMensaje.Separar(Datos.Para).Count})" : Datos.Para;
    public string Asunto => Datos.Asunto;
    public string Origen => Datos.ReferenciaOrigen ?? "";
    public int Intentos => Datos.Intentos;
    public string Adjuntos => Datos.Adjuntos == 0 ? "" : Datos.Adjuntos.ToString();
    public bool ConIncidencia => Datos.Estado is EstadoMensaje.Fallido or EstadoMensaje.Incierto or EstadoMensaje.AceptadoEnParte;
    public string AyudaEstado => Datos.Estado switch
    {
        EstadoMensaje.Pendiente when Datos.ProximoIntento is { } p && p > DateTime.Now => $"En la cola: próximo intento a las {p:HH:mm} del {p:dd/MM}. {Datos.UltimoError}",
        EstadoMensaje.Pendiente => "En la cola: sale en la siguiente pasada (cada minuto mientras la aplicación está abierta).",
        EstadoMensaje.Aceptado => $"Nuestro servidor de correo lo aceptó el {Datos.FechaAceptacion:dd/MM/yyyy HH:mm}. No confirma que haya llegado al buzón del destinatario.",
        _ => Datos.UltimoError ?? Estado,
    };
}

/// <summary>Opción de un filtro desplegable.</summary>
public sealed record OpcionFiltroCorreo(string Texto, Func<FilaMensaje, bool>? Cumple)
{
    public override string ToString() => Texto;
}

/// <summary>
/// Registro de correo enviado (correo/pantallas 2026-11-10 19:40): tabla filtrable con selección múltiple y detalle de solo
/// lectura del mensaje activo, tal como se preparó (no regenerado con la plantilla de hoy), con sus adjuntos y sus intentos.
/// Las acciones van en el menú contextual y dicen a cuántos mensajes afectan. Se usa en una pestaña desde el menú y como
/// resumen modal de un lote (filtrado por él). Cerrar el resumen no cancela ningún envío.
/// </summary>
public sealed class RegistroCorreoVM : ModeloVista, IPestanaTrabajo
{
    private readonly ServicioCorreo _servicio;
    private readonly IDialogosCorreo _dialogos;
    private readonly ContextoCorreo _contexto;
    private readonly Func<Task> _pasada;
    private readonly Action<string, int>? _abrirOrigen;
    private readonly Action? _alCerrar;
    private List<FilaRegistroVM> _todas = [];
    private IReadOnlyList<FilaRegistroVM> _seleccion = [];
    private FilaRegistroVM? _activa;
    private DetalleMensaje? _detalle;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private string _texto = "";
    private OpcionFiltroCorreo _filtroEstado;
    private object? _vista;
    private string? _desvio;

    public RegistroCorreoVM(ServicioCorreo servicio, IDialogosCorreo dialogos, ContextoCorreo contexto, Func<Task> pasada, FiltroRegistro filtro,
        string? tituloFiltro = null, IReadOnlyList<string>? noPreparados = null, Action<string, int>? abrirOrigen = null, Action? alCerrar = null)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _contexto = contexto;
        _pasada = pasada;
        _abrirOrigen = abrirOrigen;
        _alCerrar = alCerrar;
        Filtro = filtro;
        TituloFiltro = tituloFiltro;
        NoPreparados = noPreparados ?? [];
        OpcionesEstado =
        [
            new("Todos los estados", null),
            new("Con incidencia", f => f.Estado is EstadoMensaje.Fallido or EstadoMensaje.Incierto or EstadoMensaje.AceptadoEnParte),
            new("En la cola", f => f.Estado is EstadoMensaje.Pendiente or EstadoMensaje.EnProceso),
            new("Aceptados", f => f.Estado is EstadoMensaje.Aceptado or EstadoMensaje.AceptadoEnParte),
            new("Fallidos", f => f.Estado == EstadoMensaje.Fallido),
            new("Inciertos", f => f.Estado == EstadoMensaje.Incierto),
            new("Cancelados", f => f.Estado == EstadoMensaje.Cancelado),
        ];
        _filtroEstado = OpcionesEstado[0];

        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_activa?.Id));
        ReintentarCommand = new ComandoDelegado(() => _ = ReintentarAsync(), () => Ambito.Any(f => f.Datos.Estado == EstadoMensaje.Fallido));
        ReenviarCommand = new ComandoDelegado(() => _ = ReenviarAsync(), () => Ambito.Any(f => f.Datos.Estado is EstadoMensaje.Aceptado or EstadoMensaje.AceptadoEnParte));
        CancelarEnvioCommand = new ComandoDelegado(() => _ = CancelarAsync(), () => Ambito.Any(f => f.Datos.Estado == EstadoMensaje.Pendiente));
        DarPorEnviadoCommand = new ComandoDelegado(() => _ = ResolverAsync(true), () => Ambito is [{ Datos.Estado: EstadoMensaje.Incierto }]);
        VolverAEnviarCommand = new ComandoDelegado(() => _ = ResolverAsync(false), () => Ambito is [{ Datos.Estado: EstadoMensaje.Incierto }]);
        AbrirOrigenCommand = new ComandoDelegado(AbrirOrigen, () => _abrirOrigen is not null && Ambito is [{ Datos: { TipoOrigen: not null, IdOrigen: not null } }]);
        AbrirAdjuntoCommand = new ComandoDelegado(p => { if (p is AdjuntoRegistrado a) AbrirAdjunto(a); });
        LimpiarFiltrosCommand = new ComandoDelegado(() => { Texto = ""; FiltroEstado = OpcionesEstado[0]; }, () => HayFiltros);
    }

    public FiltroRegistro Filtro { get; }
    public string? TituloFiltro { get; }
    public IReadOnlyList<string> NoPreparados { get; }
    public bool HayNoPreparados => NoPreparados.Count > 0;

    // ---------- pestaña ----------

    public string Titulo => Filtro.IdLote is null ? "Correo enviado" : "Resumen del lote";
    public string Ayuda => $"Correo enviado de {_contexto.RazonSocial} en {_contexto.Trabajo.Ejercicio}: estado de cada mensaje, su contenido, adjuntos e intentos";
    public string Icono => "mail";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaRegistroCorreo { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- estado y filtros ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string TituloEstado => Estado == EstadoVista.Vacio ? (HayFiltros ? "Ningún mensaje coincide" : "Aún no se ha enviado ningún correo") : "";
    public string? Desvio { get => _desvio; private set => Fijar(ref _desvio, value); }

    public IReadOnlyList<OpcionFiltroCorreo> OpcionesEstado { get; }
    public OpcionFiltroCorreo FiltroEstado { get => _filtroEstado; set { if (Fijar(ref _filtroEstado, value ?? OpcionesEstado[0])) Filtrar(); } }
    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public bool HayFiltros => !string.IsNullOrWhiteSpace(Texto) || FiltroEstado.Cumple is not null;

    public ObservableCollection<FilaRegistroVM> Filas { get; } = [];

    public FilaRegistroVM? Activa
    {
        get => _activa;
        set
        {
            if (!Fijar(ref _activa, value)) return;
            _ = CargarDetalleAsync(value);
            AvisarAcciones();
        }
    }

    public void CambiarSeleccion(IReadOnlyList<FilaRegistroVM> seleccion)
    {
        _seleccion = seleccion;
        AvisarAcciones();
    }

    /// <summary>Mensajes a los que afectan las acciones: los seleccionados o, si no hay, el activo.</summary>
    public IReadOnlyList<FilaRegistroVM> Ambito => _seleccion.Count > 0 ? _seleccion : _activa is null ? [] : [_activa];

    public string TextoReintentar => ConCuenta("Reintentar", Ambito.Count(f => f.Datos.Estado == EstadoMensaje.Fallido));
    public string TextoReenviar => ConCuenta("Reenviar…", Ambito.Count(f => f.Datos.Estado is EstadoMensaje.Aceptado or EstadoMensaje.AceptadoEnParte));
    public string TextoCancelar => ConCuenta("Cancelar el envío", Ambito.Count(f => f.Datos.Estado == EstadoMensaje.Pendiente));

    public string Resumen
    {
        get
        {
            var n = _todas.Count;
            if (n == 0) return "Sin mensajes";
            var aceptados = _todas.Count(f => f.Datos.Estado is EstadoMensaje.Aceptado);
            var parte = _todas.Count(f => f.Datos.Estado is EstadoMensaje.AceptadoEnParte);
            var cola = _todas.Count(f => f.Datos.Estado is EstadoMensaje.Pendiente or EstadoMensaje.EnProceso);
            var fallidos = _todas.Count(f => f.Datos.Estado == EstadoMensaje.Fallido);
            var inciertos = _todas.Count(f => f.Datos.Estado == EstadoMensaje.Incierto);
            var partes = new List<string> { $"{Formatos.Plural(n, "mensaje", "mensajes")}" };
            if (aceptados > 0) partes.Add($"{aceptados} aceptados");
            if (parte > 0) partes.Add($"{parte} aceptados en parte");
            if (cola > 0) partes.Add($"{cola} en la cola");
            if (fallidos > 0) partes.Add($"{fallidos} fallidos");
            if (inciertos > 0) partes.Add($"{inciertos} inciertos");
            var texto = string.Join(" · ", partes);
            return Filas.Count == n ? texto : $"Mostrando {Formatos.Recuento(Filas.Count)} de {texto}";
        }
    }

    // ---------- detalle ----------

    public DetalleMensaje? Detalle { get => _detalle; private set { if (Fijar(ref _detalle, value)) AvisarDetalle(); } }
    public bool HayDetalle => Detalle is not null;
    public string DetalleEstado => Detalle is null ? "" : ServicioCorreo.Texto(Detalle.Estado);
    public string DetalleDe => Detalle is null ? "" : string.IsNullOrWhiteSpace(Detalle.NombreRemitente) ? Detalle.Remitente : $"{Detalle.NombreRemitente} <{Detalle.Remitente}>";
    public IReadOnlyList<string> DetalleDestinatarios => Detalle?.Destinatarios.Select(d =>
        $"{ReglasMensaje.Campo(d.Tipo)}: {d.Direccion}" + d.Resultado switch
        {
            ResultadoDestinatario.Aceptado => " · aceptado",
            ResultadoDestinatario.Rechazado => $" · rechazado{(d.Respuesta is null ? "" : $" ({d.Respuesta})")}",
            _ => "",
        }).ToList() ?? [];
    public string DetalleCuerpo => Detalle is null ? "" : Detalle.CuerpoTexto ?? (Detalle.CuerpoHtml is null ? "" : TextoPlano.DeHtml(Detalle.CuerpoHtml));
    public IReadOnlyList<AdjuntoRegistrado> DetalleAdjuntos => Detalle?.Adjuntos ?? [];
    public bool HayAdjuntos => DetalleAdjuntos.Count > 0;
    public IReadOnlyList<string> DetalleIntentos => Detalle?.HistorialIntentos.Select(i =>
        $"Intento {i.Numero} · {i.Inicio:dd/MM/yyyy HH:mm:ss} · {i.Resultado switch { "EnCurso" => "en curso", var r => r }}{(i.RespuestaServidor is null ? "" : $" · {i.RespuestaServidor}")}{(i.Detalle is null ? "" : $"\n{i.Detalle}")}").ToList() ?? [];
    public string DetalleDatos => Detalle is null ? "" : string.Join("\n", new[]
    {
        $"Preparado el {Detalle.FechaCreacion:dd/MM/yyyy HH:mm} por {Detalle.Operador}" + (Detalle.FechaAceptacion is { } a ? $" · aceptado el {a:dd/MM/yyyy HH:mm}" : ""),
        Detalle.ReferenciaOrigen is null ? null : $"Origen: {Detalle.ReferenciaOrigen}",
        Detalle.IdMensajeOriginal is int o ? $"Reenvío del mensaje {o}" : null,
        Detalle.DesviadoA is null ? null : $"Desviado a {Detalle.DesviadoA} (pruebas): los destinatarios reales no lo han recibido",
        Detalle.ResueltoPor is null ? null : $"{Detalle.MotivoResolucion} ({Detalle.ResueltoPor}, {Detalle.FechaResolucion:dd/MM/yyyy HH:mm})",
        $"Message-ID: {Detalle.MessageId}",
    }.Where(l => l is not null));
    public string? DetalleError => Detalle?.Estado is EstadoMensaje.Aceptado or EstadoMensaje.Cancelado ? null : Detalle?.UltimoError;

    private void AvisarDetalle() =>
        Avisar(nameof(HayDetalle), nameof(DetalleEstado), nameof(DetalleDe), nameof(DetalleDestinatarios), nameof(DetalleCuerpo), nameof(DetalleAdjuntos),
            nameof(HayAdjuntos), nameof(DetalleIntentos), nameof(DetalleDatos), nameof(DetalleError));

    private async Task CargarDetalleAsync(FilaRegistroVM? fila)
    {
        if (fila is null) { Detalle = null; return; }
        try
        {
            var d = await _servicio.ObtenerAsync(fila.Id);
            if (_activa == fila) Detalle = d;
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No he podido leer el detalle del mensaje: {ex.Message}";
        }
    }

    // ---------- carga ----------

    public async Task CargarAsync(int? idActiva = null)
    {
        try
        {
            if (_todas.Count == 0) Estado = EstadoVista.Cargando;
            Desvio = await _servicio.DesvioAsync();
            var filas = await _servicio.ListarAsync(Filtro);
            _todas = filas.Select(f => new FilaRegistroVM(f)).ToList();
            Filtrar(idActiva ?? _activa?.Id);
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer el registro de correo: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    private void Filtrar(int? idActiva = null)
    {
        var texto = Texto.Trim();
        var visibles = _todas.Where(f => FiltroEstado.Cumple?.Invoke(f.Datos) != false && (texto.Length == 0 ||
            f.Asunto.Contains(texto, StringComparison.CurrentCultureIgnoreCase) || f.Datos.Para.Contains(texto, StringComparison.CurrentCultureIgnoreCase) ||
            f.Origen.Contains(texto, StringComparison.CurrentCultureIgnoreCase))).ToList();
        var id = idActiva ?? _activa?.Id;
        Filas.Clear();
        foreach (var f in visibles) Filas.Add(f);
        Estado = Filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        Activa = Filas.FirstOrDefault(f => f.Id == id) ?? Filas.FirstOrDefault();
        Avisar(nameof(Resumen), nameof(HayFiltros), nameof(TituloEstado));
    }

    // ---------- acciones ----------

    public ICommand RefrescarCommand { get; }
    public ICommand ReintentarCommand { get; }
    public ICommand ReenviarCommand { get; }
    public ICommand CancelarEnvioCommand { get; }
    public ICommand DarPorEnviadoCommand { get; }
    public ICommand VolverAEnviarCommand { get; }
    public ICommand AbrirOrigenCommand { get; }
    public ICommand AbrirAdjuntoCommand { get; }
    public ICommand LimpiarFiltrosCommand { get; }

    private async Task ReintentarAsync()
    {
        var ids = Ambito.Where(f => f.Datos.Estado == EstadoMensaje.Fallido).Select(f => f.Id).ToList();
        await EjecutarAsync(async () =>
        {
            var hechos = await _servicio.ReintentarAsync(ids);
            await _pasada();
            return hechos.Count == ids.Count ? $"{Formatos.Plural(hechos.Count, "mensaje vuelve", "mensajes vuelven")} a enviarse ahora."
                : $"{hechos.Count} de {ids.Count} vuelven a enviarse; los demás ya habían cambiado de estado.";
        });
    }

    private async Task ReenviarAsync()
    {
        var filas = Ambito.Where(f => f.Datos.Estado is EstadoMensaje.Aceptado or EstadoMensaje.AceptadoEnParte).ToList();
        var opcion = _dialogos.Preguntar(filas.Count == 1 ? "Reenviar el mensaje" : $"Reenviar {filas.Count} mensajes",
            "Ya los aceptó el servidor de correo: el destinatario los recibirá otra vez. Cada reenvío es un mensaje nuevo, con el mismo contenido, destinatarios y adjuntos que el original, enlazado a él.",
            filas.Select(f => $"{f.Fecha} · {f.Para} · {f.Asunto}").ToList(),
            new OpcionMensaje(filas.Count == 1 ? "Entiendo que se enviará de nuevo: reenviar" : $"Entiendo que se enviarán de nuevo: reenviar {filas.Count}", "Crea los mensajes nuevos y los envía", Principal: true),
            new OpcionMensaje("Cancelar", "No reenvía nada", Cancelar: true));
        if (opcion != 0) return;
        await EjecutarAsync(async () =>
        {
            var nuevos = await _servicio.ReenviarAsync(filas.Select(f => f.Id), _contexto.Actor, Filtro.IdLote);
            await _pasada();
            return $"{Formatos.Plural(nuevos.Count, "reenvío creado", "reenvíos creados")}.";
        });
    }

    private async Task CancelarAsync()
    {
        var ids = Ambito.Where(f => f.Datos.Estado == EstadoMensaje.Pendiente).Select(f => f.Id).ToList();
        await EjecutarAsync(async () =>
        {
            var hechos = await _servicio.CancelarAsync(ids, _contexto.Actor);
            return hechos.Count == ids.Count ? $"{Formatos.Plural(hechos.Count, "envío cancelado", "envíos cancelados")}."
                : $"Cancelados {hechos.Count} de {ids.Count}: los demás ya se estaban enviando o se habían enviado, y esos no se pueden detener.";
        });
    }

    private async Task ResolverAsync(bool enviado)
    {
        if (Ambito is not [var fila]) return;
        var motivo = _dialogos.PedirTexto(enviado ? "Dar por enviado" : "Volver a enviar",
            enviado
                ? "No consta si el servidor lo aceptó: la conexión se cortó a mitad. Dalo por enviado solo si sabes que ha llegado (por ejemplo, el destinatario lo confirma)."
                : "No consta si el servidor lo aceptó: si lo había aceptado, el destinatario lo recibirá dos veces. Se envía como otro intento del mismo mensaje, con el mismo Message-ID.",
            "Motivo *", enviado ? "Dar por enviado" : "Volver a enviar");
        if (motivo is null) return;
        await EjecutarAsync(async () =>
        {
            var r = await _servicio.ResolverInciertoAsync(fila.Id, enviado, motivo, _contexto.Actor);
            if (!r.EsCorrecto) return r.Error!.Mensaje + " " + r.Error.AccionCorrectora;
            if (!enviado) await _pasada();
            return enviado ? "Mensaje dado por enviado." : "Mensaje enviado de nuevo.";
        });
    }

    private void AbrirOrigen()
    {
        if (Ambito is [{ Datos: { TipoOrigen: { } tipo, IdOrigen: int id } }]) _abrirOrigen?.Invoke(tipo, id);
    }

    private void AbrirAdjunto(AdjuntoRegistrado a)
    {
        if (a.RutaCompleta is null || !File.Exists(a.RutaCompleta))
        {
            _dialogos.Preguntar("No encuentro el adjunto", $"La copia de «{a.Nombre}» que se envió no está en {a.RutaCompleta ?? a.RutaRelativa}.", null,
                new OpcionMensaje("Entendido", "Comprueba la carpeta de documentos de la empresa (Parámetros y tablas › Empresas) y que no se haya movido", Principal: true, Cancelar: true));
            return;
        }
        _dialogos.AbrirArchivo(a.RutaCompleta);
    }

    private async Task EjecutarAsync(Func<Task<string>> accion)
    {
        try
        {
            var aviso = await accion();
            await CargarAsync(_activa?.Id);
            _contexto.Avisar(aviso);
        }
        catch (Exception ex)
        {
            _dialogos.Preguntar("No se ha podido completar", $"{ex.Message} Actualiza el registro (F5) y vuelve a intentarlo.", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
    }

    private void AvisarAcciones()
    {
        Avisar(nameof(TextoReintentar), nameof(TextoReenviar), nameof(TextoCancelar));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string ConCuenta(string texto, int n) => n > 1 ? $"{texto} ({n})" : texto;
}
