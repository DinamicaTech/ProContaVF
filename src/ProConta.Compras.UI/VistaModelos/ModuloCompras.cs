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
}

/// <summary>
/// Compras dentro del contenedor (compras/pantallas): la pestaña «Facturas recibidas» y la de «Reconocimiento de
/// facturas», que es la misma pantalla en modo «Probar» y nunca escribe.
/// </summary>
public sealed class ModuloCompras(ServiciosCompras servicios, IAnfitrionPestanas anfitrion, IDialogosCompras dialogos, Action<int> abrirFichaTercero)
{
    private FacturasRecibidasVM? _recibidas;
    private FacturasRecibidasVM? _prueba;

    internal ServiciosCompras Servicios => servicios;
    internal IDialogosCompras Dialogos => dialogos;
    internal void Avisar(string texto) => anfitrion.Avisar(texto);
    internal void AbrirFichaTercero(int idTercero) => abrirFichaTercero(idTercero);

    /// <summary>Abre (o activa) Facturas recibidas; al abrir busca los PDF nuevos de la carpeta.</summary>
    public void AbrirRecibidas() => Abrir(ref _recibidas, prueba: false);

    /// <summary>Abre (o activa) Reconocimiento de facturas: el modo «Probar», que no guarda nada.</summary>
    public void AbrirReconocimiento() => Abrir(ref _prueba, prueba: true);

    private void Abrir(ref FacturasRecibidasVM? pestana, bool prueba)
    {
        if (pestana is not null)
        {
            anfitrion.Activar(pestana);
            return;
        }
        pestana = new FacturasRecibidasVM(this, prueba);
        anfitrion.Abrir(pestana);
        _ = pestana.IniciarAsync();
    }

    internal void Cerrada(FacturasRecibidasVM pestana)
    {
        if (ReferenceEquals(pestana, _recibidas)) _recibidas = null;
        if (ReferenceEquals(pestana, _prueba)) _prueba = null;
    }

    /// <summary>Una factura cambió en una pestaña: la otra se refresca si está abierta.</summary>
    internal void FacturaCambiada(FacturasRecibidasVM origen)
    {
        if (_recibidas is not null && !ReferenceEquals(origen, _recibidas)) _ = _recibidas.CargarAsync();
        if (_prueba is not null && !ReferenceEquals(origen, _prueba)) _ = _prueba.CargarAsync();
    }
}
