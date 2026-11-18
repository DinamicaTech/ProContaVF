using System.Windows.Input;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Documentos.UI;

/// <summary>
/// Panel Documentos de una ficha (documentos 2026-11-18, maqueta validada): los documentos vinculados a ese registro con sus otros vínculos;
/// «Añadir…» y soltar ficheros los archivan y vinculan a él directamente (si falta el tipo o la fecha, quedan en «Por clasificar» ya vinculados);
/// «Vincular uno archivado…» busca entre los ya archivados. Menú contextual sobre el documento: abrir, carpeta, vínculos, versión y desvincular.
/// </summary>
public sealed class PanelDocumentosVM : ModeloVista
{
    private readonly ModuloDocumentos _modulo;
    private readonly AccionesDocumentos _acciones;
    private IReadOnlyList<FilaDocumentoVM> _filas = [];
    private FilaDocumentoVM? _actual;
    private bool _ocupado;
    private string _mensaje = "";

    internal PanelDocumentosVM(ModuloDocumentos modulo, Vinculo destino)
    {
        _modulo = modulo;
        _acciones = new AccionesDocumentos(modulo);
        Destino = destino;
        AnadirCommand = new ComandoDelegado(() => _ = AnadirAsync(), () => !_ocupado);
        VincularArchivadoCommand = new ComandoDelegado(() => _ = VincularArchivadoAsync(), () => !_ocupado);
        AbrirCommand = new ComandoDelegado(p => _acciones.Abrir(Fila(p)), p => Fila(p) is not null);
        CarpetaCommand = new ComandoDelegado(p => _acciones.Carpeta(Fila(p)), p => Fila(p) is not null);
        VincularCommand = new ComandoDelegado(p => { if (Fila(p) is { } f) _ = _acciones.VincularAsync([f], this).ContinueWith(_ => CargarAsync(), TaskScheduler.FromCurrentSynchronizationContext()); },
            p => Fila(p) is not null);
        NuevaVersionCommand = new ComandoDelegado(p => { if (Fila(p) is { } f) _ = _acciones.NuevaVersionAsync(f, this); }, p => Fila(p) is { Datos.PorClasificar: false });
        DesvincularCommand = new ComandoDelegado(p => { if (Fila(p) is { } f) _ = DesvincularAsync(f); }, p => Fila(p) is not null);
        VerEnDocumentosCommand = new ComandoDelegado(p => { if (Fila(p) is { } f) _modulo.AbrirDocumento(f.Id, f.Datos.PorClasificar); }, p => Fila(p) is not null);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
    }

    public Vinculo Destino { get; }
    public string Titulo => $"Documentos de {Destino.Etiqueta}";
    public string TextoSoltar => $"Suelta aquí ficheros: se archivan y quedan vinculados a {Destino.Etiqueta}";

    public ICommand AnadirCommand { get; }
    public ICommand VincularArchivadoCommand { get; }
    public ICommand AbrirCommand { get; }
    public ICommand CarpetaCommand { get; }
    public ICommand VincularCommand { get; }
    public ICommand NuevaVersionCommand { get; }
    public ICommand DesvincularCommand { get; }
    public ICommand VerEnDocumentosCommand { get; }
    public ICommand RefrescarCommand { get; }

    public bool Cargado { get; private set; }
    public IReadOnlyList<FilaDocumentoVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas), nameof(SinDocumentos), nameof(Recuento)); } }
    public FilaDocumentoVM? Actual { get => _actual; set => Fijar(ref _actual, value); }
    public bool SinDocumentos => Cargado && _filas.Count == 0;
    public string Recuento => ModuloDocumentos.Plural(_filas.Count, "documento", "documentos");
    public string Mensaje { get => _mensaje; private set => Fijar(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    private FilaDocumentoVM? Fila(object? p) => p as FilaDocumentoVM ?? _actual;

    public async Task CargarAsync()
    {
        try
        {
            var docs = await _modulo.Contexto.Servicio.DeAsync(_modulo.Contexto.IdSociedad, Destino);
            Cargado = true;
            Filas = docs.Select(d => new FilaDocumentoVM(d, () => { }) { Excluir = Destino }).ToList();
            Mensaje = "";
        }
        catch (Exception ex)
        {
            Cargado = true;
            Filas = [];
            Mensaje = $"No he podido leer los documentos: {ex.Message}";
        }
    }

    /// <summary>Ficheros soltados en el panel o elegidos con «Añadir…».</summary>
    public async Task SoltarAsync(IReadOnlyList<string> rutas)
    {
        if (rutas.Count == 0 || Ocupado) return;
        Ocupado = true;
        try { await _modulo.AnadirAsync(rutas, mover: false, Destino); }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task AnadirAsync()
    {
        if (_modulo.Dialogos.ElegirFicheros($"Añadir documentos a {Destino.Etiqueta}", varios: true) is { Count: > 0 } rutas) await SoltarAsync(rutas);
    }

    /// <summary>Busca entre los documentos archivados y vincula los elegidos a este registro.</summary>
    private async Task VincularArchivadoAsync()
    {
        if (!await _modulo.Contexto.Servicio.PuedeAsync()) { _modulo.SinPermiso(); return; }
        var archivados = await _modulo.Contexto.Servicio.ListarAsync(_modulo.Contexto.IdSociedad, EstadosDocumento.Archivado);
        var libres = archivados.Where(d => !d.Vinculos.Any(v => v.MismoDestino(Destino))).ToList();
        if (libres.Count == 0)
        {
            _modulo.Dialogos.Error("No hay documentos que vincular", "Todos los documentos archivados ya están vinculados aquí, o aún no hay ninguno. Usa «Añadir…» para subir uno nuevo.");
            return;
        }
        var elegido = _modulo.Dialogos.ElegirDocumentos($"Vincular a {Destino.Etiqueta}", "Busca por título, tipo o vínculo y marca los que quieras vincular aquí. Siguen también en sus otros vínculos.", libres);
        if (elegido is not { Count: > 0 }) return;
        var r = await _modulo.Contexto.Servicio.VincularAsync(elegido, [Destino], reemplazar: false);
        if (!r.EsCorrecto) { _modulo.Dialogos.Error("No se ha vinculado", r.Error!.TextoOperador()); return; }
        _modulo.Contexto.Avisar($"{ModuloDocumentos.Plural(elegido.Count, "documento vinculado", "documentos vinculados")} a {Destino.Etiqueta}.");
        _modulo.Cambiado(this);
        await CargarAsync();
    }

    private async Task DesvincularAsync(FilaDocumentoVM f)
    {
        if (await _acciones.DesvincularAsync(f, f.Datos.Vinculos.First(v => v.MismoDestino(Destino)), this)) await CargarAsync();
    }
}
