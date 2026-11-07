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

    /// <summary>
    /// Columna «Mín. %»: la confianza más baja de NIF, número, fecha e importes, para revisar primero las peores. Una
    /// factura ya guardada desde la pantalla sale «Revisada» (salvo que una lectura nueva, al separarla, la deje pendiente); sin reconocimiento (del ERP o a mano), vacía.
    /// </summary>
    public string Minimo => Revisada ? "Revisada" : _datos.ConfianzaMinima is { } c ? $"{Math.Round(c * 100m):0} %" : "";

    private bool Revisada => _datos.Version > 0 && !_datos.RequiereRevision;

    /// <summary>Orden de «Mín. %»: de la más baja a la más alta, luego las revisadas y al final las que no tienen reconocimiento.</summary>
    public decimal MinimoOrden => Revisada ? 2m : _datos.ConfianzaMinima ?? 3m;

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

/// <summary>Pastilla del asiento de la factura: cabecera y apuntes ya formateados.</summary>
public sealed record AsientoFacturaVM(string Titulo, string? Mensaje, IReadOnlyList<ApunteFacturaVM> Apuntes)
{
    public bool HayApuntes => Apuntes.Count > 0;
    public string TotalDebe => Formatos.Importe(Apuntes.Sum(a => a.DebeValor));
    public string TotalHaber => Formatos.Importe(Apuntes.Sum(a => a.HaberValor));

    public static AsientoFacturaVM De(AsientoFactura a) => new(
        $"Asiento {a.IdAsiento.ToString(Formatos.Cultura)}{(a.Fecha is { } f ? $" · {Formatos.Fecha(f)}" : "")}{(a.Descripcion.Length > 0 ? $" · {a.Descripcion}" : "")}",
        null,
        a.Apuntes.Select(x => new ApunteFacturaVM(x.Cuenta, x.NombreCuenta ?? "", x.Debe, x.Haber, x.Descripcion ?? "")).ToList());

    /// <summary>El asiento que generaría exportar (sin grabar), con lo que habrá que responder o lo que se creará.</summary>
    public static AsientoFacturaVM Propuesta(AsientoFactura? a, string? mensaje) => a is null
        ? new("Propuesta de asiento", mensaje ?? "No hay propuesta.", [])
        : new($"Propuesta de asiento (sin grabar){(a.Fecha is { } f ? $" · {Formatos.Fecha(f)}" : "")}{(a.Descripcion.Length > 0 ? $" · {a.Descripcion}" : "")}",
            mensaje, a.Apuntes.Select(x => new ApunteFacturaVM(x.Cuenta, x.NombreCuenta ?? "", x.Debe, x.Haber, x.Descripcion ?? "")).ToList());
}

/// <summary>Un apunte en la pastilla del asiento.</summary>
public sealed record ApunteFacturaVM(string Cuenta, string NombreCuenta, decimal DebeValor, decimal HaberValor, string Descripcion)
{
    public string Debe => DebeValor == 0m ? "" : Formatos.Importe(DebeValor);
    public string Haber => HaberValor == 0m ? "" : Formatos.Importe(HaberValor);
}
