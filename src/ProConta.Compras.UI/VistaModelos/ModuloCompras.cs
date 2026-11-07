using ProConta.Compras.Facturas;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>
/// Servicios que usan las pantallas de compras, ya ligados a la sociedad y el ejercicio de trabajo. <c>TiposIva</c>: los tipos
/// de IVA 1 a 4 del ejercicio (organizacion/sociedades-ejercicios), para el desplegable de la ficha. <c>FormasPago</c>: el
/// catálogo de formas de pago (organizacion/formas-pago). <c>TiposRetencion</c>: los tipos de retención vigentes.
/// <c>TextosClave</c>: los textos clave de compras de la empresa. <c>AltaProveedor</c>: el alta de proveedor desde la factura.
/// <c>Contabilizador</c>: exportar, re-exportar y deshacer la exportación, como <c>Operador</c>.
/// </summary>
public sealed record ServiciosCompras(
    ServicioFacturasCompra Facturas,
    ICatalogoCuentas Cuentas,
    ICatalogoCentrosCoste CentrosCoste,
    ContextoSociedadEjercicio Trabajo,
    string RazonSocial,
    Func<Task<bool>> PuedeEditar,
    Func<Task<IReadOnlyList<decimal>>>? TiposIva = null,
    Func<Task<IReadOnlyList<(byte Id, string Descripcion)>>>? FormasPago = null,
    Func<Task<IReadOnlyList<TipoRetencionCompra>>>? TiposRetencion = null,
    CatalogoTextosClaveCompra? TextosClave = null,
    ProConta.Compras.Proveedores.ServicioAltaProveedorCompra? AltaProveedor = null,
    ContabilizadorCompras? Contabilizador = null,
    ProConta.Nucleo.Auditoria.Actor? Operador = null,
    Func<Task>? ComprobarTrimestre = null);

/// <summary>Diálogos de compras (en la vista, para que los modelos de vista se prueben sin ventanas).</summary>
public interface IDialogosCompras
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    /// <summary>Abre un fichero o una carpeta con el programa predeterminado de Windows; si no puede, lo explica.</summary>
    void Abrir(string ruta);

    /// <summary>Copia el texto al portapapeles; false si Windows no lo deja.</summary>
    bool Copiar(string texto);

    /// <summary>«Separar facturas»: página en la que empieza la segunda factura y su nombre de fichero; null si se cancela.</summary>
    (int Pagina, string Nombre)? PedirSeparacion(string fichero, string nombrePropuesto);

    /// <summary>Buscador de cuentas común (contabilidad/cuenta-contable): la cuenta elegida o creada, o null si se cancela.</summary>
    ProConta.Contabilidad.Cuentas.CuentaContable? ElegirCuenta(ProConta.Contabilidad.UI.BuscadorCuentasVM buscador, string titulo);

    /// <summary>
    /// «Añadir texto clave…»: pide el texto y qué asigna (la cuenta, el centro o los dos, de los que se dan) y lo guarda con
    /// <paramref name="guardar"/>, que devuelve el error que se enseña en el diálogo o null. True si se guardó.
    /// </summary>
    /// <param name="textoInicial">Texto ya escrito (el seleccionado en el PDF).</param>
    /// <param name="soloCampo">«cuenta» o «centro»: solo esa casilla marcada (la del campo con el foco); null, las dos.</param>
    bool PedirTextoClave(string? cuenta, string? centro, Func<string, bool, bool, Task<string?>> guardar, string? textoInicial = null, string? soloCampo = null);

    /// <summary>
    /// Alta de proveedor sin razón social confirmada por la AEAT: formulario con el NIF y la razón social obligatorios y
    /// el nombre comercial, la dirección, el teléfono y el email opcionales. <paramref name="motivo"/>: por qué no se
    /// confirmó. Con <paramref name="urlEInforma"/>, un botón «Buscar en eInforma» la abre en el navegador.
    /// <paramref name="guardar"/> da de alta y devuelve el error que se enseña o null. True si se dio de alta.
    /// </summary>
    bool PedirDatosProveedor(ProConta.Compras.Proveedores.DatosAltaProveedor propuesta, string? motivo, string? urlEInforma,
        Func<ProConta.Compras.Proveedores.DatosAltaProveedor, Task<string?>> guardar);

    /// <summary>
    /// Descuadre de más de 0,03 € al exportar (compras/factura-compra/contabilizacion 2026-10-29 21:05): explica cuánto
    /// falta y pide el motivo del ajuste manual a la cuenta de redondeo. Null si se cancela.
    /// </summary>
    string? PedirMotivoAjuste(string explicacion);
}

/// <summary>
/// Compras dentro del contenedor (compras/pantallas): una sola pestaña, «Facturas recibidas», con el modo prueba (que
/// vuelve a reconocer y compara sin escribir) en su menú contextual (compras/pantallas 2026-11-05 20:29).
/// </summary>
public sealed class ModuloCompras(ServiciosCompras servicios, IAnfitrionPestanas anfitrion, IDialogosCompras dialogos, Action<int> abrirFichaTercero)
{
    private FacturasRecibidasVM? _recibidas;

    internal ServiciosCompras Servicios => servicios;
    internal IDialogosCompras Dialogos => dialogos;
    internal void Avisar(string texto) => anfitrion.Avisar(texto);
    internal void AbrirFichaTercero(int idTercero) => abrirFichaTercero(idTercero);

    /// <summary>Abre (o activa) Facturas recibidas; al abrir busca los PDF nuevos de la carpeta.</summary>
    public void AbrirRecibidas()
    {
        if (_recibidas is not null)
        {
            anfitrion.Activar(_recibidas);
            return;
        }
        _recibidas = new FacturasRecibidasVM(this, prueba: false);
        anfitrion.Abrir(_recibidas);
        _ = _recibidas.IniciarAsync();
    }

    internal void Cerrada(FacturasRecibidasVM pestana)
    {
        if (ReferenceEquals(pestana, _recibidas)) _recibidas = null;
    }

    /// <summary>Una factura cambió (reservado para otras pantallas que muestren facturas de compra).</summary>
    internal void FacturaCambiada(FacturasRecibidasVM origen)
    {
        if (_recibidas is not null && !ReferenceEquals(origen, _recibidas)) _ = _recibidas.CargarAsync();
    }
}
