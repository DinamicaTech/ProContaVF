using System.Windows.Input;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Documentos.UI;

/// <summary>Una opción de un filtro desplegable: el valor y su texto.</summary>
public sealed record OpcionFiltro(string Valor, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>
/// Pantalla Documentos (documentos 2026-11-18, maqueta validada): la bandeja «Por clasificar», donde se revisa lo que el programa propone al leer
/// cada fichero (tipo, fecha, título y vínculos) y se archiva de una vez con «Archivar los listos», y los archivados, con filtros por tipo y año y un
/// buscador. Selección múltiple con casilla y un clic; el menú contextual actúa sobre lo marcado; las acciones generales van en la cabecera. A la
/// derecha, el detalle del documento actual: vista previa, propuesta, vínculos (con su aspa) y versiones.
/// </summary>
public sealed class PantallaDocumentosVM : ModeloVista, IPestanaTrabajo
{
    public const string TodosLosTipos = "", SinVincular = "#SIN_VINCULO";

    private readonly ModuloDocumentos _modulo;
    private readonly AccionesDocumentos _acciones;
    private object? _vista;
    private IReadOnlyList<FilaDocumentoVM> _bandeja = [];
    private IReadOnlyList<FilaDocumentoVM> _archivo = [];
    private IReadOnlyList<FilaDocumentoVM> _filas = [];
    private IReadOnlyList<TipoDocumento> _tipos = [];
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _verBandeja;
    private string _busqueda = "";
    private string _filtroTipo = TodosLosTipos;
    private string _filtroAnio = "";
    private bool _ocupado, _marcando, _moverOriginal;
    private FilaDocumentoVM? _actual;
    private IReadOnlyList<VersionDocumento> _versiones = [];
    private string? _raiz;
    private int? _enfocar;

    internal PantallaDocumentosVM(ModuloDocumentos modulo)
    {
        _modulo = modulo;
        _acciones = new AccionesDocumentos(modulo);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        AnadirCommand = new ComandoDelegado(() => _ = AnadirAsync(carpeta: false), () => !_ocupado);
        AnadirCarpetaCommand = new ComandoDelegado(() => _ = AnadirAsync(carpeta: true), () => !_ocupado);
        ArchivarListosCommand = new ComandoDelegado(() => _ = ArchivarAsync(_bandeja.Where(f => f.Listo).ToList()), () => !_ocupado && _bandeja.Any(f => f.Listo));
        AbrirCommand = new ComandoDelegado(p => _acciones.Abrir(Unica(p)), p => Unica(p) is not null);
        CarpetaCommand = new ComandoDelegado(p => _acciones.Carpeta(Unica(p)), p => Unica(p) is not null);
        CopiarRutaCommand = new ComandoDelegado(p => _acciones.CopiarRuta(Unica(p)), p => Unica(p) is not null);
        ClasificarCommand = new ComandoDelegado(p => { if (Unica(p) is { } f) _ = ClasificarAsync(f); }, p => !_ocupado && Unica(p) is { Datos.PorClasificar: true, EsCompra: false });
        ArchivarFilaCommand = new ComandoDelegado(p => { if (p is FilaDocumentoVM f) _ = ArchivarAsync([f]); }, p => !_ocupado && p is FilaDocumentoVM { Listo: true });
        VincularCommand = new ComandoDelegado(_ => _ = _acciones.VincularAsync(Marcadas, this), _ => !_ocupado && Marcadas.Count > 0 && !Marcadas.Any(f => f.EsCompra));
        VincularActualCommand = new ComandoDelegado(_ => { if (_actual is { } a) _ = _acciones.VincularAsync([a], this); }, _ => _actual is { EsCompra: false });
        DesvincularCommand = new ComandoDelegado(p => { if (_actual is { } a && p is VinculoVM v) _ = DesvincularAsync(a, v.Vinculo); });
        AbrirDestinoCommand = new ComandoDelegado(p => { if (p is VinculoVM v) AbrirDestino(v.Vinculo); });
        PonerTipoCommand = new ComandoDelegado(_ => _ = PonerTipoAsync(), _ => !_ocupado && Marcadas.Count > 0 && Marcadas.All(f => f.Datos.PorClasificar && !f.EsCompra));
        ArchivarMarcadosCommand = new ComandoDelegado(_ => _ = ArchivarAsync(Marcadas), _ => !_ocupado && Marcadas.Count > 0 && Marcadas.All(f => f.Datos.PorClasificar));
        NuevaVersionCommand = new ComandoDelegado(p => { if (Unica(p) is { } f) _ = _acciones.NuevaVersionAsync(f, this).ContinueWith(_ => CargarVersionesAsync(), TaskScheduler.FromCurrentSynchronizationContext()); },
            p => Unica(p) is { Datos.PorClasificar: false });
        VincularFilaCommand = new ComandoDelegado(p => { if (p is FilaDocumentoVM f) _ = VincularFilaAsync(f); }, _ => !_ocupado);
        EnviarCompraFilaCommand = new ComandoDelegado(p => { if (p is FilaDocumentoVM f) _ = EnviarComprasAsync([f]); }, _ => !_ocupado && _modulo.Contexto.EnviarACompras is not null);
        EnviarComprasCommand = new ComandoDelegado(_ => _ = EnviarComprasAsync(Marcadas), _ => !_ocupado && _modulo.Contexto.EnviarACompras is not null && Marcadas.Count > 0 && Marcadas.All(f => f.Datos.PorClasificar));
        EliminarCommand = new ComandoDelegado(_ => _ = _acciones.EliminarAsync(Marcadas, null), _ => !_ocupado && Marcadas.Count > 0);
        MarcarTodosCommand = new ComandoDelegado(() => MarcarTodos(!TodosMarcados));
    }

    // ---------- pestaña ----------
    public string Titulo => "Documentos";
    public string Ayuda => $"Documentos de {_modulo.Contexto.RazonSocial}";
    public string Icono => "folder";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaDocumentos { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _modulo.PantallaCerrada();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand AnadirCommand { get; }
    public ICommand AnadirCarpetaCommand { get; }
    public ICommand ArchivarListosCommand { get; }
    public ICommand AbrirCommand { get; }
    public ICommand CarpetaCommand { get; }
    public ICommand CopiarRutaCommand { get; }
    public ICommand ClasificarCommand { get; }
    public ICommand ArchivarFilaCommand { get; }
    public ICommand VincularCommand { get; }
    public ICommand VincularActualCommand { get; }
    public ICommand DesvincularCommand { get; }
    public ICommand AbrirDestinoCommand { get; }
    public ICommand PonerTipoCommand { get; }
    public ICommand ArchivarMarcadosCommand { get; }
    public ICommand NuevaVersionCommand { get; }
    public ICommand EnviarComprasCommand { get; }
    public ICommand VincularFilaCommand { get; }
    public ICommand EnviarCompraFilaCommand { get; }
    public ICommand EliminarCommand { get; }
    public ICommand MarcarTodosCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => _raiz is null ? $"{_modulo.Contexto.RazonSocial} · sin carpeta de documentos" : $"{_modulo.Contexto.RazonSocial} · {_raiz}";
    public string AyudaSubtitulo => _raiz is null
        ? "La empresa no tiene carpeta de documentos: escríbela en Parámetros y tablas › Empresas para poder añadir documentos."
        : $"Carpeta de documentos de la empresa. Los que subes a mano se archivan en {Path.Combine(_raiz, RutasDocumentos.CarpetaManuales)}, por año y tipo.";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>Al añadir, el original va a la papelera de Windows una vez copiado (por ejemplo, para vaciar Descargas).</summary>
    public bool MoverOriginal { get => _moverOriginal; set => Fijar(ref _moverOriginal, value); }

    public bool VerBandeja
    {
        get => _verBandeja;
        set
        {
            if (!Fijar(ref _verBandeja, value)) return;
            foreach (var f in _bandeja.Concat(_archivo)) f.Marcado = false;
            Avisar(nameof(VerArchivo));
            Filtrar();
        }
    }

    public bool VerArchivo { get => !_verBandeja; set => VerBandeja = !value; }

    public int NumeroBandeja => _bandeja.Count;
    public int NumeroListos => _bandeja.Count(f => f.Listo);
    public string TextoArchivarListos => NumeroListos == 1 ? "Archivar el listo" : $"Archivar los {NumeroListos} listos";

    // ---------- filtros ----------
    public string Busqueda { get => _busqueda; set { if (Fijar(ref _busqueda, value ?? "")) Filtrar(); } }

    public IReadOnlyList<OpcionFiltro> OpcionesTipo =>
        [new(TodosLosTipos, "Todos los tipos"), .. _tipos.Select(t => new OpcionFiltro(t.Codigo, t.Nombre)), new(SinVincular, "Sin vincular")];

    public string FiltroTipo { get => _filtroTipo; set { if (Fijar(ref _filtroTipo, value ?? TodosLosTipos)) Filtrar(); } }

    public IReadOnlyList<OpcionFiltro> OpcionesAnio =>
        [new("", "Todos los años"), .. _archivo.Select(f => f.Ejercicio).Where(a => a is not null).Distinct().OrderByDescending(a => a)
            .Select(a => new OpcionFiltro(a!.Value.ToString(Formatos.Cultura), a.Value.ToString(Formatos.Cultura))), new("-", "Sin año")];

    public string FiltroAnio { get => _filtroAnio; set { if (Fijar(ref _filtroAnio, value ?? "")) Filtrar(); } }

    /// <summary>Tipos que se pueden elegir en la bandeja (los de los módulos productores no).</summary>
    public IReadOnlyList<TipoDocumento> TiposManuales => _tipos.Where(t => t.Manual).ToList();

    // ---------- filas ----------
    public IReadOnlyList<FilaDocumentoVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }
    public IReadOnlyList<FilaDocumentoVM> Marcadas => _filas.Where(f => f.Marcado).ToList();
    public bool TodosMarcados { get => _filas.Count > 0 && _filas.All(f => f.Marcado); set => MarcarTodos(value); }

    public string Recuento => (VerBandeja ? ModuloDocumentos.Plural(_filas.Count, "por clasificar", "por clasificar") + $" · {NumeroListos} listos"
                                          : ModuloDocumentos.Plural(_filas.Count, "documento", "documentos"))
                              + (Marcadas.Count > 0 ? $" · {ModuloDocumentos.Plural(Marcadas.Count, "marcado", "marcados")}" : "");

    public bool SinResultados => (VerBandeja ? _bandeja : _archivo).Count > 0 && _filas.Count == 0;
    public bool BandejaVacia => VerBandeja && _bandeja.Count == 0;

    // ---------- detalle ----------
    public FilaDocumentoVM? Actual
    {
        get => _actual;
        set
        {
            if (!Fijar(ref _actual, value)) return;
            Avisar(nameof(HayActual), nameof(NoHayActual), nameof(TextoSinActual));
            _ = CargarVersionesAsync();
        }
    }

    public bool HayActual => _actual is not null;
    public bool NoHayActual => _actual is null;
    public string TextoSinActual => Marcadas.Count > 1
        ? $"{Marcadas.Count} marcados. Clic derecho para actuar sobre todos a la vez (por ejemplo, vincularlos al mismo ejercicio)."
        : "Elige un documento para ver su vista previa, sus vínculos y sus versiones.";

    public IReadOnlyList<VersionDocumento> Versiones { get => _versiones; private set { _versiones = value; Avisar(nameof(Versiones)); } }

    private async Task CargarVersionesAsync()
    {
        var a = _actual;
        if (a is null || a.Datos.PorClasificar) { Versiones = []; return; }
        try
        {
            var v = await _modulo.Contexto.Servicio.VersionesAsync(a.Id);
            if (ReferenceEquals(a, _actual)) Versiones = v;
        }
        catch (Exception) { Versiones = []; }
    }

    /// <summary>Al abrir desde el panel de una ficha: deja ese documento a la vista.</summary>
    internal void Enfocar(int idDocumento) => _enfocar = idDocumento;

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        if (Ocupado) return;
        try
        {
            if (_bandeja.Count + _archivo.Count == 0) Estado = EstadoVista.Cargando;
            Ocupado = true;
            var servicio = _modulo.Contexto.Servicio;
            var marcados = _bandeja.Concat(_archivo).Where(f => f.Marcado).Select(f => f.Id).ToHashSet();
            var actual = _enfocar ?? _actual?.Id;
            _raiz = await servicio.RaizAsync(_modulo.Contexto.IdSociedad);
            if (_tipos.Count == 0) _tipos = await servicio.TiposAsync();
            var bandeja = await servicio.ListarAsync(_modulo.Contexto.IdSociedad, EstadosDocumento.PorClasificar);
            var archivo = await servicio.ListarAsync(_modulo.Contexto.IdSociedad, EstadosDocumento.Archivado);
            foreach (var f in _bandeja) f.Cambiado -= FilaCambiada;
            _bandeja = bandeja.OrderBy(d => d.FechaAlta).ThenBy(d => d.IdDocumento).Select(d => Fila(d, marcados)).ToList();
            _archivo = archivo.Select(d => Fila(d, marcados)).ToList();
            foreach (var f in _bandeja) f.Cambiado += FilaCambiada;
            if (_enfocar is int e)
            {
                VerBandeja = _bandeja.Any(f => f.Id == e);
                _filtroTipo = TodosLosTipos;
                _filtroAnio = "";
                _busqueda = "";
                Avisar(nameof(FiltroTipo), nameof(FiltroAnio), nameof(Busqueda));
                _enfocar = null;
            }
            Avisar(nameof(Subtitulo), nameof(AyudaSubtitulo), nameof(OpcionesTipo), nameof(OpcionesAnio), nameof(TiposManuales), nameof(NumeroBandeja));
            Filtrar();
            Actual = _filas.FirstOrDefault(f => f.Id == actual) ?? (VerBandeja ? _filas.FirstOrDefault() : null);
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los documentos: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private FilaDocumentoVM Fila(DocumentoListado d, HashSet<int> marcados) => new(d, AlMarcar) { Marcado = marcados.Contains(d.IdDocumento) };

    private void Filtrar()
    {
        var q = ServicioDocumentos.Palabras(_busqueda);
        var origen = VerBandeja ? _bandeja : _archivo;
        Filas = origen.Where(f =>
                (VerBandeja || _filtroTipo == TodosLosTipos || (_filtroTipo == SinVincular ? f.SinVinculos : f.Datos.Tipo == _filtroTipo))
                && (VerBandeja || _filtroAnio.Length == 0 || (_filtroAnio == "-" ? f.Ejercicio is null : f.Ejercicio?.ToString(Formatos.Cultura) == _filtroAnio))
                && (q.Count == 0 || Coincide(f, q)))
            .ToList();
        foreach (var f in origen.Except(_filas)) f.Marcado = false;
        if (Estado is not EstadoVista.Error)
        {
            Estado = EstadoVista.Contenido;
            MensajeEstado = "";
        }
        if (_actual is not null && !_filas.Contains(_actual)) Actual = null;
        AlMarcar();
        Avisar(nameof(SinResultados), nameof(BandejaVacia), nameof(NumeroBandeja), nameof(NumeroListos), nameof(TextoArchivarListos));
    }

    private static bool Coincide(FilaDocumentoVM f, IReadOnlyList<string> palabras)
    {
        var texto = LectorPropuestasTexto($"{f.Datos.Titulo} {f.Fichero} {f.NombreTipo} {f.TextoVinculos} {string.Join(' ', f.Vinculos.Select(v => v.Tipo))}");
        return palabras.All(p => texto.Contains(p, StringComparison.Ordinal));
    }

    private static string LectorPropuestasTexto(string s) => string.Join(' ', ServicioDocumentos.Palabras(s));

    private void AlMarcar()
    {
        if (_marcando) return;
        Avisar(nameof(Recuento), nameof(TodosMarcados), nameof(TextoSinActual));
        CommandManager.InvalidateRequerySuggested();
    }

    private void MarcarTodos(bool marcar)
    {
        _marcando = true;
        foreach (var f in _filas) f.Marcado = marcar;
        _marcando = false;
        AlMarcar();
    }

    /// <summary>Clic derecho en una fila sin marcar: se queda marcada solo ella.</summary>
    public void MarcarSolo(FilaDocumentoVM fila)
    {
        if (fila.Marcado) return;
        _marcando = true;
        foreach (var f in _filas) f.Marcado = ReferenceEquals(f, fila);
        _marcando = false;
        AlMarcar();
    }

    private FilaDocumentoVM? Unica(object? p) => p as FilaDocumentoVM ?? (Marcadas is { Count: 1 } m ? m[0] : _actual);

    // ---------- acciones ----------
    private async Task AnadirAsync(bool carpeta)
    {
        IReadOnlyList<string>? rutas = carpeta
            ? _modulo.Dialogos.ElegirCarpeta("Añadir una carpeta con todo lo que tenga dentro") is { } c ? [c] : null
            : _modulo.Dialogos.ElegirFicheros("Añadir documentos", varios: true);
        if (rutas is null) return;
        await SoltarAsync(rutas);
    }

    /// <summary>Ficheros arrastrados a la pantalla (o elegidos): a la bandeja, que queda a la vista.</summary>
    public async Task SoltarAsync(IReadOnlyList<string> rutas)
    {
        if (rutas.Count == 0 || Ocupado) return;
        Ocupado = true;
        IReadOnlyList<int> anadidos;
        try { anadidos = await _modulo.AnadirAsync(rutas, _moverOriginal, null); }
        finally { Ocupado = false; }
        if (anadidos.Count == 0) return;
        VerBandeja = true;
        _enfocar = anadidos[0];
        await CargarAsync();
    }

    private async void FilaCambiada(object? sender, EventArgs e)
    {
        if (sender is not FilaDocumentoVM f) return;
        try
        {
            var r = await _modulo.Contexto.Servicio.ActualizarAsync(f.Id, f.Tipo, f.Titulo, f.Fecha, f.Datos.Notas);
            if (!r.EsCorrecto) { _modulo.Dialogos.Error("No se ha guardado el cambio", r.Error!.TextoOperador()); await CargarAsync(); return; }
            if (await _modulo.Contexto.Servicio.LeerAsync(f.Id) is { } d) f.Actualizar(d);
            Avisar(nameof(NumeroListos), nameof(TextoArchivarListos), nameof(Recuento));
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No se ha guardado el cambio", $"{ex.Message} Pulsa Actualizar (F5) y vuelve a intentarlo."); }
    }

    private async Task ArchivarAsync(IReadOnlyList<FilaDocumentoVM> filas)
    {
        if (filas.Count == 0) return;
        if (!await _modulo.Contexto.Servicio.PuedeAsync()) { _modulo.SinPermiso(); return; }
        Ocupado = true;
        try
        {
            var r = await _modulo.Contexto.Servicio.ArchivarAsync(filas.Select(f => f.Id).ToList());
            if (!r.EsCorrecto) { _modulo.Dialogos.Error("No se ha archivado", r.Error!.TextoOperador()); return; }
            var v = r.Valor;
            var quedan = _bandeja.Count - v.Archivados;
            _modulo.Contexto.Avisar($"{ModuloDocumentos.Plural(v.Archivados, "documento archivado", "documentos archivados")} en la carpeta de documentos." +
                                    (quedan > 0 ? $" {ModuloDocumentos.Plural(quedan, "queda", "quedan")} por clasificar." : ""));
            var problemas = v.NoListos.Concat(v.Errores).ToList();
            if (problemas.Count > 0 && filas.Count <= 50)
                _modulo.Dialogos.Error(v.Archivados == 0 ? "No se ha archivado nada" : "Algunos no se han archivado",
                    "Estos siguen en «Por clasificar». Completa lo que falta en su fila y vuelve a archivar:", problemas);
        }
        finally { Ocupado = false; }
        await CargarAsync();
        if (_bandeja.Count == 0) VerBandeja = false;
        _modulo.Cambiado(this);
    }

    private async Task PonerTipoAsync()
    {
        var filas = Marcadas;
        if (_modulo.Dialogos.ElegirTipo(TiposManuales, $"Tipo para {ModuloDocumentos.Plural(filas.Count, "documento", "documentos")} de la bandeja.") is not { } tipo) return;
        foreach (var f in filas) f.Tipo = tipo.Codigo;
        await Task.CompletedTask;
    }

    private async Task DesvincularAsync(FilaDocumentoVM fila, Vinculo v)
    {
        if (await _acciones.DesvincularAsync(fila, v, this)) Filtrar();
    }

    private void AbrirDestino(Vinculo v)
    {
        if (_modulo.Contexto.AbrirDestino?.Invoke(v) != true)
            _modulo.Contexto.Avisar($"{TiposEntidad.Nombre(v.TipoEntidad)} {v.Etiqueta}: no tiene pantalla propia que abrir desde aquí.");
    }

    private async Task ClasificarAsync(FilaDocumentoVM f)
    {
        if (await _acciones.ClasificarAsync(f, this))
        {
            await CargarAsync();
            if (_bandeja.Count == 0) VerBandeja = false;
        }
    }

    private async Task VincularFilaAsync(FilaDocumentoVM f)
    {
        if (await _acciones.VincularAsync([f], this))
        {
            Avisar(nameof(NumeroListos), nameof(TextoArchivarListos), nameof(Recuento));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task EnviarComprasAsync(IReadOnlyList<FilaDocumentoVM> filas)
    {
        if (_modulo.Dialogos.Preguntar("Enviar a Facturas recibidas",
                "Se copian a la carpeta de facturas de compra del ejercicio, donde las recoge Compras › Facturas recibidas para leerlas y registrarlas, y se quitan de «Por clasificar».",
                filas.Select(f => f.Fichero).ToList(),
                new OpcionMensaje("Enviar", "Los lleva a Compras", Principal: true), new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true)) != 0)
            return;
        var errores = new List<string>();
        var enviados = new List<FilaDocumentoVM>();
        foreach (var f in filas)
        {
            if (f.Ruta is null) continue;
            var error = await _modulo.Contexto.EnviarACompras!(f.Ruta);
            if (error is null) enviados.Add(f); else errores.Add($"{f.Fichero}: {error}");
        }
        if (enviados.Count > 0) await _modulo.Contexto.Servicio.EliminarAsync(enviados.Select(f => f.Id).ToList());
        if (errores.Count > 0) _modulo.Dialogos.Error("Algunos no se han enviado", "Siguen en «Por clasificar»:", errores);
        else _modulo.Contexto.Avisar($"{ModuloDocumentos.Plural(enviados.Count, "factura enviada", "facturas enviadas")} a Compras › Facturas recibidas.");
        await CargarAsync();
    }
}
