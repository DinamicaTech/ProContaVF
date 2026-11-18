using System.Windows;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Documentos.UI;

/// <summary>Contexto del gestor documental y lo que abre fuera de él (lo conecta el contenedor).</summary>
public sealed record ContextoDocumentos(ServicioDocumentos Servicio, string IdSociedad, string RazonSocial, Action<string> Avisar)
{
    /// <summary>Abre el registro de un vínculo (el modelo, el tercero, el activo, la factura, el asiento…); null si no se sabe abrir.</summary>
    public Func<Vinculo, bool>? AbrirDestino { get; init; }

    /// <summary>
    /// Lleva un PDF que resultó ser una factura de compra a Compras (lo copia a la carpeta de facturas recibidas del ejercicio y abre la pantalla).
    /// Devuelve null si ha ido bien o el error que hay que enseñar.
    /// </summary>
    public Func<string, Task<string?>>? EnviarACompras { get; init; }
}

/// <summary>Diálogos del gestor documental (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosDocumentos
{
    /// <summary>Devuelve el índice de la opción elegida.</summary>
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    void Error(string titulo, string mensaje, IReadOnlyList<string>? detalles = null);

    /// <summary>Elige uno o varios ficheros; null si se cancela.</summary>
    IReadOnlyList<string>? ElegirFicheros(string titulo, bool varios);

    /// <summary>Elige una carpeta; null si se cancela.</summary>
    string? ElegirCarpeta(string titulo);

    /// <summary>El buscador «Vincular a…»; true si se acepta (lo elegido queda en <see cref="VincularVM.Elegidos"/>).</summary>
    bool Vincular(VincularVM vincular);

    /// <summary>Elige documentos archivados de una lista con buscador; devuelve sus Id o null si se cancela.</summary>
    IReadOnlyList<int>? ElegirDocumentos(string titulo, string explicacion, IReadOnlyList<DocumentoListado> documentos);

    /// <summary>Elige un tipo de documento; null si se cancela.</summary>
    TipoDocumento? ElegirTipo(IReadOnlyList<TipoDocumento> tipos, string explicacion);

    /// <summary>Pide un texto (por ejemplo, el motivo de una versión); null si se cancela.</summary>
    string? PedirTexto(string titulo, string explicacion, string propuesto);

    /// <summary>
    /// «Clasificar y archivar…» de un documento pendiente: tipo, fecha y título; null si se cancela. Lo que ya tiene viene propuesto.
    /// </summary>
    (string Tipo, DateTime Fecha, string Titulo)? Clasificar(IReadOnlyList<TipoDocumento> tipos, string? tipo, DateTime? fecha, string titulo, string explicacion);

    /// <summary>Copia un texto al portapapeles; false si no se ha podido.</summary>
    bool Copiar(string texto);

    /// <summary>Abre un fichero con el programa del sistema.</summary>
    void AbrirFichero(string ruta);

    /// <summary>Abre el Explorador con el fichero seleccionado.</summary>
    void MostrarEnCarpeta(string ruta);
}

/// <summary>
/// Gestor documental dentro del contenedor (documentos 2026-11-18): la pestaña Documentos (una sola) y los paneles Documentos de las fichas.
/// </summary>
public sealed class ModuloDocumentos(ContextoDocumentos contexto, IAnfitrionPestanas anfitrion, IDialogosDocumentos dialogos)
{
    private PantallaDocumentosVM? _pantalla;
    private readonly List<WeakReference<PanelDocumentosVM>> _paneles = [];

    internal ContextoDocumentos Contexto => contexto;
    internal IDialogosDocumentos Dialogos => dialogos;

    /// <summary>Abre (o activa) la pantalla Documentos; con <paramref name="bandeja"/>, en «Por clasificar».</summary>
    public void Abrir(bool bandeja = false)
    {
        if (_pantalla is null)
        {
            _pantalla = new PantallaDocumentosVM(this);
            anfitrion.Abrir(_pantalla);
        }
        else anfitrion.Activar(_pantalla);
        if (bandeja) _pantalla.VerBandeja = true;
        _ = _pantalla.CargarAsync();
    }

    /// <summary>Abre la pantalla con un documento a la vista (desde el panel de una ficha).</summary>
    public void AbrirDocumento(int idDocumento, bool porClasificar)
    {
        Abrir(porClasificar);
        _pantalla!.Enfocar(idDocumento);
    }

    internal void PantallaCerrada() => _pantalla = null;

    /// <summary>Panel Documentos para la ficha de un registro (un tercero, un activo, un modelo…). Se carga al enseñarse.</summary>
    public FrameworkElement CrearPanel(Vinculo destino)
    {
        var vm = new PanelDocumentosVM(this, destino);
        _paneles.Add(new WeakReference<PanelDocumentosVM>(vm));
        return new Vistas.VistaPanelDocumentos { DataContext = vm };
    }

    /// <summary>Lo llama cualquier parte tras cambiar documentos: la pantalla y los paneles abiertos vuelven a leer.</summary>
    internal void Cambiado(object? origen = null)
    {
        if (_pantalla is not null && !ReferenceEquals(origen, _pantalla)) _ = _pantalla.CargarAsync();
        _paneles.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in _paneles)
            if (w.TryGetTarget(out var p) && !ReferenceEquals(p, origen) && p.Cargado) _ = p.CargarAsync();
    }

    internal void SinPermiso() => dialogos.Error("Sin permiso para los documentos",
        "No tienes permiso para añadir, archivar, vincular ni eliminar documentos: la acción «Documentos» tiene grupos asignados y el tuyo no está entre ellos. " +
        "Puedes verlos y abrirlos. Pide el permiso a quien administra los permisos.");

    /// <summary>
    /// Añade ficheros: a la bandeja (pantalla) o, con <paramref name="destino"/>, vinculados a ese registro y archivados si la propuesta está completa
    /// (panel de una ficha). Avisa de lo añadido, lo repetido y los errores.
    /// </summary>
    internal async Task<IReadOnlyList<int>> AnadirAsync(IReadOnlyList<string> rutas, bool mover, Vinculo? destino)
    {
        if (rutas.Count == 0) return [];
        if (!await contexto.Servicio.PuedeAsync()) { SinPermiso(); return []; }
        var r = await contexto.Servicio.AnadirABandejaAsync(contexto.IdSociedad, rutas, mover, destino is null ? null : [destino], archivarSiListo: destino is not null);
        if (!r.EsCorrecto)
        {
            dialogos.Error("No he podido añadir los documentos", string.Join(Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
            return [];
        }
        var v = r.Valor;
        var detalles = v.Repetidos.Concat(v.Errores).ToList();
        if (v.Anadidos.Count > 0)
        {
            if (destino is null) contexto.Avisar($"{Plural(v.Anadidos.Count, "fichero añadido", "ficheros añadidos")} a «Por clasificar». Revisa las propuestas y archiva.");
            else
            {
                var docs = await contexto.Servicio.DeAsync(contexto.IdSociedad, destino);
                var pendientes = docs.Count(d => v.Anadidos.Contains(d.IdDocumento) && d.PorClasificar);
                contexto.Avisar(pendientes == 0
                    ? $"{Plural(v.Anadidos.Count, "documento archivado", "documentos archivados")} y vinculado{(v.Anadidos.Count == 1 ? "" : "s")} a {destino.Etiqueta}."
                    : $"Vinculado{(v.Anadidos.Count == 1 ? "" : "s")} a {destino.Etiqueta}. {Plural(pendientes, "queda", "quedan")} en Documentos › Por clasificar porque falta el tipo o la fecha.");
            }
        }
        if (detalles.Count > 0)
            dialogos.Error(v.Anadidos.Count == 0 ? "No se ha añadido nada" : "Algunos ficheros no se han añadido",
                v.Anadidos.Count == 0 ? "Ninguno de los ficheros se ha añadido:" : $"Se han añadido {v.Anadidos.Count}; el resto, no:", detalles);
        Cambiado();
        return v.Anadidos;
    }

    internal static string Plural(int n, string uno, string varios) => n == 1 ? $"1 {uno}" : $"{n} {varios}";
}
