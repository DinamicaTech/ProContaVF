using ProConta.Compras.Reconocimiento;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace ProConta.Compras.Integraciones;

/// <summary>Lee el texto nativo del PDF y la posición de cada palabra con PdfPig (como el extractor del motor VB).</summary>
public sealed class ExtractorPdfPig : IExtractorDocumentoFacturaCompra
{
    public DocumentoFacturaExtraido Extraer(string rutaPdf)
    {
        if (string.IsNullOrWhiteSpace(rutaPdf)) throw new ArgumentException("Debe indicarse la ruta del PDF.", nameof(rutaPdf));
        if (!File.Exists(rutaPdf)) throw new FileNotFoundException($"No encuentro el PDF. Ruta intentada: {rutaPdf}", rutaPdf);

        var paginas = new List<PaginaDocumento>();
        using (var pdf = PdfDocument.Open(rutaPdf))
        {
            foreach (var page in pdf.GetPages())
            {
                var palabras = page.GetWords(NearestNeighbourWordExtractor.Instance)
                    .Select(w => new PalabraDocumento
                    {
                        Pagina = page.Number,
                        Texto = w.Text,
                        X = w.BoundingBox.Left,
                        Y = w.BoundingBox.Bottom,
                        Ancho = w.BoundingBox.Width,
                        Alto = w.BoundingBox.Height
                    })
                    .ToArray();
                paginas.Add(new PaginaDocumento { Numero = page.Number, Texto = ContentOrderTextExtractor.GetText(page), Palabras = palabras });
            }
        }
        return new DocumentoFacturaExtraido { Ruta = rutaPdf, Paginas = paginas };
    }
}
