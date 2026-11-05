using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace ProConta.Compras.Integraciones;

/// <summary>Parte un PDF en dos por una página, con PdfPig (compras/pantallas 2026-11-05 20:29, «Separar facturas»).</summary>
public static class DivisorPdf
{
    public static int ContarPaginas(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        return documento.NumberOfPages;
    }

    /// <summary>Páginas 1 a <paramref name="paginaSegunda"/> − 1 y de ahí al final. La página debe estar entre 2 y el total.</summary>
    public static (byte[] Primera, byte[] Segunda) Dividir(byte[] pdf, int paginaSegunda)
    {
        var total = ContarPaginas(pdf);
        if (paginaSegunda < 2 || paginaSegunda > total)
            throw new ArgumentOutOfRangeException(nameof(paginaSegunda), $"La segunda factura debe empezar entre la página 2 y la {total}.");
        var primera = PdfMerger.Merge([pdf], [Enumerable.Range(1, paginaSegunda - 1).ToList()]);
        var segunda = PdfMerger.Merge([pdf], [Enumerable.Range(paginaSegunda, total - paginaSegunda + 1).ToList()]);
        return (primera, segunda);
    }
}
