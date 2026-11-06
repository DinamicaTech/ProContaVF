namespace ProConta.Compras.Reconocimiento;

/// <summary>Palabra de una página con su posición en puntos PDF (origen abajo a la izquierda).</summary>
/// <summary>Punto de una página del PDF en coordenadas de usuario del PDF (origen abajo a la izquierda, en puntos).</summary>
public sealed record PuntoDocumento(int Pagina, double X, double Y);

public sealed class PalabraDocumento
{
    public int Pagina { get; init; }
    public string Texto { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Ancho { get; init; }
    public double Alto { get; init; }
}

/// <summary>Página extraída: texto en orden de lectura y palabras posicionadas.</summary>
public sealed class PaginaDocumento
{
    public int Numero { get; init; }
    public string Texto { get; init; } = string.Empty;
    public IReadOnlyList<PalabraDocumento> Palabras { get; init; } = [];
}

/// <summary>
/// Documento en el formato neutral que comparten todos los extractores (PdfPig, OCR):
/// el motor no depende de ninguna biblioteca de PDF ni de la red.
/// </summary>
public sealed class DocumentoFacturaExtraido
{
    /// <summary>Ruta del PDF. El motor la usa como señal (carpeta del proveedor, ejercicio, nombre del fichero).</summary>
    public string Ruta { get; init; } = string.Empty;
    public IReadOnlyList<PaginaDocumento> Paginas { get; init; } = [];

    public string TextoCompleto => string.Join(Environment.NewLine, Paginas.Select(p => p.Texto));
}

/// <summary>Extrae texto y geometría de un PDF nativo sin interpretar sus datos fiscales.</summary>
public interface IExtractorDocumentoFacturaCompra
{
    DocumentoFacturaExtraido Extraer(string rutaPdf);
}

/// <summary>Extrae texto y geometría con OCR. Solo se usa con los PDF sin texto nativo útil.</summary>
public interface IExtractorOcrFacturaCompra
{
    Task<DocumentoFacturaExtraido> ExtraerAsync(string rutaPdf, CancellationToken cancelacion);
}

/// <summary>Catálogos que el motor necesita, ya cargados: el motor no accede a la BD.</summary>
public sealed class ContextoReconocimientoFacturaCompra
{
    /// <summary>NIF de proveedores registrados, para reconocerlos en el texto.</summary>
    public IReadOnlyCollection<string> NifsProveedoresConocidos { get; init; } = [];
    /// <summary>NIF de las sociedades propias: nunca son el emisor.</summary>
    public IReadOnlyCollection<string> NifsSociedades { get; init; } = [];
    /// <summary>NIF del proveedor por nombre de carpeta (alias).</summary>
    public IReadOnlyDictionary<string, string> NifsPorAliasProveedor { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Hasta veinte números de factura recientes del proveedor de la carpeta.</summary>
    public IReadOnlyCollection<string> NumerosFacturaProveedor { get; init; } = [];
    public bool SociedadTieneVies { get; init; }
}

/// <summary>Desglose de IVA reconocido. <see cref="Tipo"/> es el porcentaje (21 = 21 %).</summary>
public sealed class DesgloseIvaReconocido
{
    public decimal Tipo { get; set; }
    public decimal Base { get; set; }
    public decimal Cuota { get; set; }
    public decimal Confianza { get; set; }
    public string Evidencia { get; set; } = string.Empty;
}

/// <summary>Lectura de una factura por el motor: valores, confianza por campo y advertencias. No escribe nada.</summary>
public sealed class ResultadoReconocimientoFacturaCompra
{
    public bool RequiereOcr { get; set; }
    public bool RequiereOcrParaNif { get; set; }
    public bool PosiblesMultiplesFacturas { get; set; }
    public string Divisa { get; set; } = "EUR";
    public decimal? ImporteTotalDivisa { get; set; }
    public bool RequiereConversionDivisa { get; set; }
    public decimal? TipoCambioAplicado { get; set; }
    public DateTime? FechaTipoCambio { get; set; }
    public decimal? ImporteTotalEuros { get; set; }
    public decimal? TipoRetencion { get; set; }
    public decimal? ImporteRetencion { get; set; }
    public string ProveedorIdentificado { get; set; } = string.Empty;
    public string NifLeidoDocumento { get; set; } = string.Empty;
    public bool NifAsignadoDesdeProveedor { get; set; }
    public string NifProveedor { get; set; } = string.Empty;
    public decimal ConfianzaNif { get; set; }
    public string NumeroFactura { get; set; } = string.Empty;
    public string NumeroFacturaEjemplo { get; set; } = string.Empty;
    public decimal ConfianzaNumero { get; set; }
    public DateTime? FechaFactura { get; set; }
    public decimal ConfianzaFecha { get; set; }
    public decimal? BaseImponible { get; set; }
    public decimal? CuotaIva { get; set; }
    public decimal? ImporteTotal { get; set; }
    public decimal ConfianzaImportes { get; set; }
    public bool InversionSujetoPasivo { get; set; }
    public IReadOnlyList<DesgloseIvaReconocido> DesglosesIva { get; set; } = [];
    public IReadOnlyList<string> Advertencias { get; set; } = [];
    /// <summary>Campos que se han leído con una plantilla del proveedor (nif, numero, fecha, total).</summary>
    public IReadOnlyList<string> CamposPlantilla { get; set; } = [];
}
