using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.Textract;
using Amazon.Textract.Model;
using ProConta.Compras.Reconocimiento;
using UglyToad.PdfPig;

namespace ProConta.Compras.Integraciones;

/// <summary>
/// OCR con Amazon Textract (compras/reconocimiento 2026-11-07) para los PDF sin texto nativo útil. Cada página se manda
/// por separado a DetectDocumentText (la llamada síncrona solo admite PDF de una página) y sus palabras se pasan a
/// coordenadas del PDF (puntos, desde abajo a la izquierda), las mismas que da <see cref="ExtractorPdfPig"/>, para que
/// las plantillas y el visor funcionen igual.
/// </summary>
/// <remarks>
/// Las credenciales salen de la configuración del reconocimiento de la sociedad (cifradas, <see cref="ConCredenciales"/>);
/// si no las tiene, del perfil de AWS del puesto (el fichero de credenciales estándar, o el que diga
/// AWS_SHARED_CREDENTIALS_FILE), con el nombre <see cref="PerfilPorDefecto"/> o el de la variable PROCONTA_AWS_PERFIL. La
/// región, de ese perfil (AWS_CONFIG_FILE) o de AWS_REGION.
/// </remarks>
public sealed class ExtractorOcrTextract(IAmazonTextract cliente) : IExtractorOcrFacturaCompra
{
    public const string PerfilPorDefecto = "proconta-textract";
    /// <summary>Máximo de páginas que se leen con OCR por PDF (coste y tiempo).</summary>
    public const int MaxPaginas = 10;

    /// <summary>El extractor con el perfil del puesto, o null (con el motivo) si no hay credenciales.</summary>
    public static (ExtractorOcrTextract? Extractor, string? Motivo) Crear(Func<string, string?>? entorno = null)
    {
        entorno ??= Environment.GetEnvironmentVariable;
        var perfil = entorno("PROCONTA_AWS_PERFIL") is { Length: > 0 } p ? p : PerfilPorDefecto;
        try
        {
            var cadena = new CredentialProfileStoreChain();
            if (!cadena.TryGetAWSCredentials(perfil, out var credenciales))
                return (null, $"No encuentro el perfil de AWS «{perfil}» en este equipo.");
            var region = cadena.TryGetProfile(perfil, out var datos) && datos.Region is not null ? datos.Region
                : entorno("AWS_REGION") is { Length: > 0 } r ? RegionEndpoint.GetBySystemName(r) : null;
            if (region is null) return (null, $"El perfil de AWS «{perfil}» no dice la región.");
            return (new ExtractorOcrTextract(new AmazonTextractClient(credenciales, region)), null);
        }
        catch (Exception ex) when (ex is AmazonClientException or IOException or UnauthorizedAccessException)
        {
            return (null, $"No he podido leer el perfil de AWS «{perfil}»: {ex.Message}");
        }
    }

    /// <summary>El extractor con las claves guardadas en la configuración de la sociedad.</summary>
    public static ExtractorOcrTextract ConCredenciales(CredencialesOcr c) =>
        new(new AmazonTextractClient(new BasicAWSCredentials(c.ClaveAcceso, c.Secreto), RegionEndpoint.GetBySystemName(c.Region)));

    public async Task<DocumentoFacturaExtraido> ExtraerAsync(string rutaPdf, CancellationToken cancelacion)
    {
        var bytes = await File.ReadAllBytesAsync(rutaPdf, cancelacion).ConfigureAwait(false);
        var paginas = new List<PaginaDocumento>();
        using var pdf = PdfDocument.Open(bytes);
        var total = Math.Min(pdf.NumberOfPages, MaxPaginas);
        for (var n = 1; n <= total; n++)
        {
            var pagina = pdf.GetPage(n);
            var unaPagina = pdf.NumberOfPages == 1 ? bytes : UglyToad.PdfPig.Writer.PdfMerger.Merge([bytes], [[n]]);
            DetectDocumentTextResponse respuesta;
            try
            {
                respuesta = await cliente.DetectDocumentTextAsync(new DetectDocumentTextRequest { Document = new Document { Bytes = new MemoryStream(unaPagina) } }, cancelacion)
                    .ConfigureAwait(false);
            }
            catch (AmazonServiceException ex)
            {
                throw new InvalidOperationException($"El OCR (Amazon Textract) no ha podido leer la página {n}: {ex.Message}", ex);
            }
            paginas.Add(Pagina(n, pagina.Width, pagina.Height, respuesta.Blocks ?? []));
        }
        return new DocumentoFacturaExtraido { Ruta = rutaPdf, Paginas = paginas };
    }

    /// <summary>Página con el texto por líneas (en el orden de Textract) y las palabras en puntos del PDF.</summary>
    internal static PaginaDocumento Pagina(int numero, double ancho, double alto, IEnumerable<Block> bloques)
    {
        var lista = bloques.ToList();
        var lineas = lista.Where(b => b.BlockType == BlockType.LINE).Select(b => b.Text ?? "");
        var palabras = lista.Where(b => b.BlockType == BlockType.WORD && b.Geometry?.BoundingBox is not null)
            .Select(b =>
            {
                var c = b.Geometry.BoundingBox;
                double izq = c.Left ?? 0f, arriba = c.Top ?? 0f, w = c.Width ?? 0f, h = c.Height ?? 0f;
                return new PalabraDocumento
                {
                    Pagina = numero,
                    Texto = b.Text ?? "",
                    X = izq * ancho,
                    Y = (1 - arriba - h) * alto,
                    Ancho = w * ancho,
                    Alto = h * alto
                };
            })
            .ToArray();
        return new PaginaDocumento { Numero = numero, Texto = string.Join(Environment.NewLine, lineas), Palabras = palabras };
    }
}
