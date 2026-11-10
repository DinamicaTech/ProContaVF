using ProConta.Contabilidad.CentrosCoste;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Documentos;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Programadas;
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
    Actor Actor,
    ICatalogoCentrosCoste CentrosCoste,
    ServicioProgramadas Programadas,
    ServicioPdfFacturas Pdf);

/// <summary>Diálogos de facturación (en la vista, para que los modelos de vista se prueben sin ventanas).</summary>
public interface IDialogosFacturacion
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    /// <summary>Abre un fichero con el programa predeterminado de Windows (el visor de PDF); si no puede, lo explica.</summary>
    void AbrirArchivo(string ruta);

    /// <summary>Busca y elige un cliente; null si se cancela.</summary>
    ClienteElegible? ElegirCliente();

    /// <summary>Elige un concepto activo de la sociedad para una línea nueva; null si se cancela.</summary>
    Concepto? ElegirConcepto(IReadOnlyList<Concepto> conceptos);

    /// <summary>Pide la cuenta (existente o nueva) para el rol Cliente; null si se cancela.</summary>
    string? PedirCuenta(ClaseCuentaRol clase, string nombreCliente, string propuesta, string razonSocial);

    /// <summary>Explica la rectificación y pide su motivo (obligatorio); null si se cancela.</summary>
    string? PedirMotivoRectificacion(string numero, string explicacion);

    /// <summary>
    /// Elige hasta qué fecha generar y qué programadas con vencimientos hasta entonces; al abrir están marcadas todas o,
    /// con <paramref name="soloMarcadas"/>, solo esas. <paramref name="pendientes"/> lee las de una fecha. Null si se cancela.
    /// </summary>
    (DateTime Hasta, IReadOnlyList<int> Ids)? ElegirProgramadasAGenerar(DateTime hasta, Func<DateTime, Task<IReadOnlyList<ProgramadaPendiente>>> pendientes, IReadOnlySet<int>? soloMarcadas = null);
}

/// <summary>
/// Facturación dentro del contenedor: una pestaña «Facturas», otra «Facturas programadas» y una por cada factura o
/// programada abierta en el editor (si ya está abierta, se activa).
/// </summary>
public sealed class ModuloFacturacion
{
    private readonly ServiciosFacturacion _s;
    private readonly IAnfitrionPestanas _anfitrion;
    private readonly IDialogosFacturacion _dialogos;
    private readonly Action<int> _abrirFichaTercero;
    private readonly List<EditorFacturaVM> _editores = [];
    private ListadoFacturasVM? _listado;
    private ListadoProgramadasVM? _programadas;

    public ModuloFacturacion(ServiciosFacturacion servicios, IAnfitrionPestanas anfitrion, IDialogosFacturacion dialogos, Action<int> abrirFichaTercero)
    {
        _s = servicios;
        _anfitrion = anfitrion;
        _dialogos = dialogos;
        _abrirFichaTercero = abrirFichaTercero;
    }

    /// <summary>Correo común (correo/pantallas): compositor, lotes con resumen y registro. Null, el envío no está disponible.</summary>
    public ProConta.Correo.IEnvioCorreoInteractivo? Correo { get; set; }

    /// <summary>Prepara el correo de una factura validada (PDF, destinatarios y plantilla).</summary>
    public ProConta.Facturacion.Documentos.PreparadorCorreoFacturas? CorreoFacturas { get; set; }

    internal ServiciosFacturacion Servicios => _s;
    internal IDialogosFacturacion Dialogos => _dialogos;
    internal void Avisar(string texto) => _anfitrion.Avisar(texto);

    /// <summary>Abre (o activa) la pestaña Facturas.</summary>
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

    /// <summary>Abre (o activa) la pestaña Facturas programadas.</summary>
    public void AbrirProgramadas()
    {
        if (_programadas is not null)
        {
            _anfitrion.Activar(_programadas);
            _ = _programadas.CargarAsync();
            return;
        }
        _programadas = new ListadoProgramadasVM(this);
        _anfitrion.Abrir(_programadas);
        _ = _programadas.CargarAsync();
    }

    internal void ProgramadasCerrado() => _programadas = null;

    /// <summary>«Ver sus facturas»: abre Facturas filtrada por la programada.</summary>
    internal void VerFacturasDeProgramada(int idProgramada)
    {
        AbrirListado();
        _listado!.FiltrarPorProgramada(idProgramada);
    }

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

    /// <summary>Abre una programada en el editor (o activa su pestaña si ya está abierta).</summary>
    public void AbrirProgramada(int idProgramada)
    {
        if (_editores.FirstOrDefault(e => e.IdProgramada == idProgramada) is { } abierto)
        {
            _anfitrion.Activar(abierto);
            return;
        }
        var editor = new EditorFacturaVM(this);
        _editores.Add(editor);
        _anfitrion.Abrir(editor);
        _ = editor.CargarProgramadaAsync(idProgramada);
    }

    /// <summary>Abre una programada nueva sin guardar (la casilla «Periódica» ya marcada).</summary>
    public void NuevaProgramada()
    {
        var editor = new EditorFacturaVM(this);
        _editores.Add(editor);
        _anfitrion.Abrir(editor);
        _ = editor.NuevaProgramadaAsync();
    }

    /// <summary>«Programar…»: abre una programada nueva sin guardar con el contenido de la factura.</summary>
    public void ProgramarDesde(int idFactura)
    {
        var editor = new EditorFacturaVM(this);
        _editores.Add(editor);
        _anfitrion.Abrir(editor);
        _ = editor.NuevaProgramadaAsync(idFactura);
    }

    internal void EditorCerrado(EditorFacturaVM editor) => _editores.Remove(editor);

    internal void CerrarEditor(EditorFacturaVM editor)
    {
        _editores.Remove(editor);
        _anfitrion.Cerrar(editor);
    }

    /// <summary>El editor guardó, validó o borró: el listado se refresca si está abierto.</summary>
    internal void FacturaCambiada(int? idActiva = null) => _ = _listado?.CargarAsync(idActiva);

    /// <summary>El editor guardó o borró una programada: su listado se refresca si está abierto.</summary>
    internal void ProgramadaCambiada(int? idProgramada = null) => _ = _programadas?.CargarAsync(idProgramada);

    internal void VolverAlListado()
    {
        if (_listado is null) AbrirListado();
        else _anfitrion.Activar(_listado);
    }

    internal void VolverAProgramadas()
    {
        if (_programadas is null) AbrirProgramadas();
        else _anfitrion.Activar(_programadas);
    }

    internal void AbrirFichaTercero(int idTercero) => _abrirFichaTercero(idTercero);

    /// <summary>
    /// Envía por correo facturas validadas (facturacion/pantallas 2026-11-10 19:40): con una abre el compositor con su PDF, la
    /// plantilla y los destinatarios del cliente; con varias, las envía en un lote sin abrir ventanas y muestra el resumen.
    /// </summary>
    internal async Task EnviarPorCorreoAsync(IReadOnlyList<(int IdFactura, string Rotulo)> facturas, string descripcionLote)
    {
        if (Correo is null || CorreoFacturas is null || facturas.Count == 0) return;
        if (facturas.Count == 1)
        {
            var r = await CorreoFacturas.PrepararAsync(facturas[0].IdFactura, _s.Actor);
            if (!r.EsCorrecto)
            {
                _dialogos.Preguntar($"No puedo preparar el correo de {facturas[0].Rotulo}", r.Error!.Mensaje + (r.Error.AccionCorrectora is { } a ? " " + a : ""), null,
                    new ProConta.UI.Base.Controles.OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
                return;
            }
            var aviso = r.Valor.AvisoDestinatarios is { } e ? e.Mensaje + (e.AccionCorrectora is { } c ? " " + c : "") : null;
            await Correo.ComponerAsync(r.Valor.Mensaje, aviso);
            return;
        }
        var lote = facturas.Select(f => new ProConta.Correo.PreparacionLote(f.Rotulo, async () =>
        {
            var r = await CorreoFacturas.PrepararAsync(f.IdFactura, _s.Actor);
            if (!r.EsCorrecto) return ProConta.Nucleo.Resultados.Resultado<ProConta.Correo.MensajeNuevo>.Fallo(r.Errores);
            // En un lote no hay compositor: sin destinatarios elegibles, la factura queda sin enviar con su motivo.
            if (r.Valor.AvisoDestinatarios is { } aviso && r.Valor.Mensaje.Destinatarios.Count == 0)
                return ProConta.Nucleo.Resultados.Resultado<ProConta.Correo.MensajeNuevo>.Fallo(aviso);
            return ProConta.Nucleo.Resultados.Resultado<ProConta.Correo.MensajeNuevo>.Correcto(r.Valor.Mensaje);
        })).ToList();
        await Correo.EnviarLoteAsync(_s.Trabajo.IdSociedadEjercicio, descripcionLote, "Facturas", lote);
    }
}
