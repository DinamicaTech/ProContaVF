using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.FormasPago;
using ProConta.Organizacion.Sociedades;
using ProConta.Terceros.Roles;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Facturacion.UI.VistaModelos;

/// <summary>Servicios que usan las pantallas de facturación, ya ligados a la sociedad y el ejercicio de trabajo.</summary>
public sealed record ServiciosFacturacion(
    ServicioFacturas Facturas,
    ConsultaFacturas Consulta,
    CatalogoConceptos Conceptos,
    ApoyoEditorFacturas Apoyo,
    ICatalogoFormasPago FormasPago,
    LectorConfiguracionEjercicio Configuracion,
    ContextoSociedadEjercicio Trabajo,
    string RazonSocial,
    Actor Actor);

/// <summary>Diálogos de facturación (en la vista, para que los modelos de vista se prueben sin ventanas).</summary>
public interface IDialogosFacturacion
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    /// <summary>Busca y elige un cliente; null si se cancela.</summary>
    ClienteElegible? ElegirCliente();

    /// <summary>Elige un concepto activo de la sociedad para una línea nueva; null si se cancela.</summary>
    Concepto? ElegirConcepto(IReadOnlyList<Concepto> conceptos);

    /// <summary>Pide la cuenta (existente o nueva) para el rol Cliente; null si se cancela.</summary>
    string? PedirCuenta(ClaseCuentaRol clase, string nombreCliente, string propuesta, string razonSocial);
}

/// <summary>
/// Facturación dentro del contenedor: una pestaña «Facturas emitidas» y una pestaña por factura abierta en el editor
/// (si ya está abierta, se activa).
/// </summary>
public sealed class ModuloFacturacion
{
    private readonly ServiciosFacturacion _s;
    private readonly IAnfitrionPestanas _anfitrion;
    private readonly IDialogosFacturacion _dialogos;
    private readonly Action<int> _abrirFichaTercero;
    private readonly List<EditorFacturaVM> _editores = [];
    private ListadoFacturasVM? _listado;

    public ModuloFacturacion(ServiciosFacturacion servicios, IAnfitrionPestanas anfitrion, IDialogosFacturacion dialogos, Action<int> abrirFichaTercero)
    {
        _s = servicios;
        _anfitrion = anfitrion;
        _dialogos = dialogos;
        _abrirFichaTercero = abrirFichaTercero;
    }

    internal ServiciosFacturacion Servicios => _s;
    internal IDialogosFacturacion Dialogos => _dialogos;
    internal void Avisar(string texto) => _anfitrion.Avisar(texto);

    /// <summary>Abre (o activa) la pestaña Facturas emitidas.</summary>
    public void AbrirListado()
    {
        if (_listado is not null)
        {
            _anfitrion.Activar(_listado);
            _ = _listado.CargarAsync();
            return;
        }
        _listado = new ListadoFacturasVM(this);
        _anfitrion.Abrir(_listado);
        _ = _listado.CargarAsync();
    }

    internal void ListadoCerrado() => _listado = null;

    /// <summary>Abre una factura en el editor (borrador editable o validada en solo lectura).</summary>
    public void AbrirFactura(int idFactura)
    {
        if (_editores.FirstOrDefault(e => e.IdFactura == idFactura) is { } abierto)
        {
            _anfitrion.Activar(abierto);
            return;
        }
        var editor = new EditorFacturaVM(this);
        _editores.Add(editor);
        _anfitrion.Abrir(editor);
        _ = editor.CargarAsync(idFactura);
    }

    /// <summary>Abre un borrador nuevo sin guardar.</summary>
    public void NuevaFactura()
    {
        var editor = new EditorFacturaVM(this);
        _editores.Add(editor);
        _anfitrion.Abrir(editor);
        _ = editor.NuevaAsync();
    }

    internal void EditorCerrado(EditorFacturaVM editor) => _editores.Remove(editor);

    internal void CerrarEditor(EditorFacturaVM editor)
    {
        _editores.Remove(editor);
        _anfitrion.Cerrar(editor);
    }

    /// <summary>El editor guardó, validó o borró: el listado se refresca si está abierto.</summary>
    internal void FacturaCambiada(int? idActiva = null) => _ = _listado?.CargarAsync(idActiva);

    internal void VolverAlListado()
    {
        if (_listado is null) AbrirListado();
        else _anfitrion.Activar(_listado);
    }

    internal void AbrirFichaTercero(int idTercero) => _abrirFichaTercero(idTercero);
}
