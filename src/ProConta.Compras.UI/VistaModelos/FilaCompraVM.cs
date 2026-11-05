using System.IO;
using ProConta.Compras.Facturas;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>Fila de la lista de facturas recibidas.</summary>
public sealed class FilaCompraVM(FilaFacturaCompra datos) : ModeloVista
{
    private FilaFacturaCompra _datos = datos;
    private bool _pdfExiste = File.Exists(datos.RutaPdf);

    public FilaFacturaCompra Datos
    {
        get => _datos;
        set
        {
            _datos = value;
            _pdfExiste = File.Exists(value.RutaPdf);
            Avisar(string.Empty);
        }
    }

    public int IdRegistro => _datos.IdRegistro;
    public string Fichero => _datos.NombreFichero;
    public string Carpeta => _datos.Carpeta;
    public string Proveedor => _datos.Proveedor.Length > 0 ? _datos.Proveedor : _datos.Carpeta;
    public string Nif => _datos.Nif ?? "";
    public string Numero => _datos.NumeroFactura;
    public string Fecha => _datos.Fecha is { } f ? Formatos.Fecha(f) : "";
    public DateTime? FechaOrden => _datos.Fecha;
    public decimal BaseOrden => _datos.BaseImponible ?? 0m;
    public decimal TotalOrden => _datos.ImporteTotal ?? 0m;
    public string Base => _datos.BaseImponible is { } b ? Formatos.Importe(b) : "";
    public string Total => _datos.ImporteTotal is { } t ? Formatos.Importe(t) : "";
    public string Asiento => _datos.Asiento is { } a ? a.ToString(Formatos.Cultura) : _datos.Contabilizada ? "Sí" : "";
    public bool PdfExiste => _pdfExiste;

    /// <summary>Situación de la factura para la columna de estado y los filtros rápidos.</summary>
    public string Estado => !_pdfExiste ? "PDF no encontrado"
        : _datos.Contabilizada ? "Contabilizada"
        : _datos.Duplicada ? "Duplicada"
        : _datos.Vacia ? "Sin rellenar"
        : _datos.Lista && !_datos.RequiereRevision ? "Lista"
        : "Por revisar";

    /// <summary>Marca corta de la columna de estado (✓ lista, ! por revisar, ○ sin rellenar, × sin PDF).</summary>
    public string Marca => Estado switch
    {
        "Lista" or "Contabilizada" => "✓",
        "Por revisar" => "!",
        "Sin rellenar" => "○",
        "PDF no encontrado" => "×",
        _ => "·"
    };

    public string Confianza => _datos.Confianza is { } c ? $"{Math.Round(c * 100m):0} %" : "";

    /// <summary>Búsqueda libre; con «f+» solo en la ruta del fichero.</summary>
    public bool Contiene(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return true;
        var t = texto.Trim();
        if (t.StartsWith("f+", StringComparison.OrdinalIgnoreCase))
        {
            var resto = t[2..].Trim();
            return resto.Length > 0 && _datos.RutaPdf.Contains(resto, StringComparison.OrdinalIgnoreCase);
        }
        return new[] { _datos.NombreFichero, _datos.NumeroFactura, _datos.Nif, Proveedor, _datos.Carpeta }
            .Any(x => x is not null && x.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Refresca la existencia del PDF (al buscar PDF nuevos o actualizar).</summary>
    internal void ComprobarPdf()
    {
        _pdfExiste = File.Exists(_datos.RutaPdf);
        Avisar(nameof(PdfExiste), nameof(Estado), nameof(Marca));
    }
}

/// <summary>Opción de un filtro rápido o de la lista desplegable.</summary>
public sealed record OpcionCompra(string Texto, string Clave, string Ayuda)
{
    public override string ToString() => Texto;
}
