using ProConta.Compras.Facturas;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>Servicios que usan las pantallas de compras, ya ligados a la sociedad y el ejercicio de trabajo.</summary>
public sealed record ServiciosCompras(
    ServicioFacturasCompra Facturas,
    ICatalogoCuentas Cuentas,
    ICatalogoCentrosCoste CentrosCoste,
    ContextoSociedadEjercicio Trabajo,
    string RazonSocial,
    Func<Task<bool>> PuedeEditar);

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
