using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using FastReport;
using FastReport.Barcode;
using FastReport.Utils;

namespace ProConta.Informes.Tests;

/// <summary>Motor PDF (informes/reporting, 2026-11-12 15:19): .frx › HTML › PDF con WebView2, sin ventanas y con texto real.</summary>
public sealed class MotorPdfTests
{
    private static readonly DatosFacturaPdf Datos = new()
    {
        Numero = "F26-000123", Fecha = "12/11/2026", EmisorNombre = "Sociedad sintética, S.L.", EmisorNif = "B12345674",
        EmisorDomicilio = "Calle Inventada 1\n08000 Barcelona", ClienteNombre = "Cliente de prueba, S.A.", ClienteNif = "A11111111",
        ClienteDomicilio = "Avenida Imaginaria 45\n28000 Madrid", FormaPago = "Transferencia", Total = "1.234,56 €", Descripcion = "Servicios de noviembre"
    };

    private static List<LineaFacturaPdf> Lineas(int n) =>
        Enumerable.Range(1, n).Select(i => new LineaFacturaPdf($"Concepto {i} con ñ, acentos y €", "1,00", "100,00", "", "100,00", "21 %", "121,00")).ToList();

    private static readonly List<ResumenFacturaPdf> Resumen = [new("Base imponible", "1.020,30 €"), new("IVA 21 %", "214,26 €")];

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static int Paginas(byte[] pdf) => Regex.Matches(Latin1(pdf), @"/Type\s*/Page[^s]").Count;

    private static void Guardar(string nombre, byte[] pdf)
    {
        var salida = Environment.GetEnvironmentVariable("PROCONTA_PDF_PRUEBA");
        if (!string.IsNullOrEmpty(salida)) File.WriteAllBytes(Path.Combine(salida, nombre), pdf);
    }

    [Fact]
    public void LosMargenesDeLaPlantillaVanALaReglaPage()
    {
        // FastReport pone una regla @page por cada página del informe.
        const string pagina = "<style media=\"print\">div.frpage0 { break-after: always; }\n @page { size: portrait; margin: 0; }</style>";
        var html = pagina + pagina;
        var ajustado = ConversorHtmlPdf.AjustarPagina(html, new PaginaPdf(210, 297, 15, 12.5, 20, 10));
        Assert.Equal(2, Regex.Matches(ajustado, Regex.Escape("@page { size: 210mm 297mm; margin: 15mm 12.5mm 20mm 10mm; }")).Count);
        Assert.DoesNotContain("size: portrait", ajustado);
    }

    [Theory]
    [InlineData(PlantillaFactura.Factura, 6, 1)]
    [InlineData(PlantillaFactura.Factura, 60, 2)]
    [InlineData(PlantillaFactura.Proforma, 6, 1)]
    public void LaFacturaSaleConTextoRealYLaMismaPaginacion(PlantillaFactura plantilla, int lineas, int paginas)
    {
        var pdf = new GeneradorPdfFastReport().Generar(plantilla, Datos, Lineas(lineas), Resumen);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(paginas, Paginas(pdf));
        Assert.Contains("/Font", Latin1(pdf));
        // Con PdfSimple cada página era una imagen de cientos de KB; con texto real ronda los 60 KB.
        Assert.True(pdf.Length < 200_000, $"El PDF ocupa {pdf.Length} bytes");
        Guardar($"{plantilla}-{lineas}.pdf", pdf);
    }

    [Fact]
    public void LasImagenesYElQrSalenEnElPdf()
    {
        using var informe = new Report();
        var pagina = new ReportPage { Name = "Pagina", LeftMargin = 15, RightMargin = 15, TopMargin = 15, BottomMargin = 15 };
        informe.Pages.Add(pagina);
        var titulo = new ReportTitleBand { Name = "Titulo", Height = Units.Millimeters * 60 };
        pagina.ReportTitle = titulo;
        using var logo = new Bitmap(120, 60);
        using (var g = Graphics.FromImage(logo)) { g.Clear(Color.SteelBlue); g.FillEllipse(Brushes.Gold, 10, 10, 100, 40); }
        titulo.Objects.Add(new PictureObject { Name = "Logo", Image = (Image)logo.Clone(), Bounds = new RectangleF(0, 0, Units.Millimeters * 40, Units.Millimeters * 20) });
        titulo.Objects.Add(new BarcodeObject
        {
            Name = "Qr", Barcode = new BarcodeQR(), Text = "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=F26-1",
            ShowText = false, Bounds = new RectangleF(Units.Millimeters * 140, 0, Units.Millimeters * 40, Units.Millimeters * 40)
        });
        titulo.Objects.Add(new TextObject { Name = "Texto", Text = "Logo y QR", Bounds = new RectangleF(0, Units.Millimeters * 45, Units.Millimeters * 80, Units.Millimeters * 8) });
        informe.Prepare();

        var html = MotorPdf.Html(informe);
        Assert.Contains("data:image", html);
        var pdf = MotorPdf.Generar(informe, ConversorHtmlPdf.Compartido);
        Assert.True(Regex.Matches(Latin1(pdf), @"/Subtype\s*/Image").Count >= 2, "Faltan el logo o el QR en el PDF");
        Guardar("imagenes.pdf", pdf);
    }

    [Fact]
    public void LaFacturaLlevaElLogoYElQrSoloSiLosTiene()
    {
        using var bitmap = new Bitmap(300, 60);
        using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.White); g.FillEllipse(Brushes.Orange, 5, 5, 50, 50); }
        using var png = new MemoryStream();
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var completa = new DatosFacturaPdf
        {
            Numero = Datos.Numero, Fecha = Datos.Fecha, EmisorNombre = Datos.EmisorNombre, EmisorNif = Datos.EmisorNif, EmisorDomicilio = Datos.EmisorDomicilio,
            ClienteNombre = Datos.ClienteNombre, ClienteNif = Datos.ClienteNif, ClienteDomicilio = Datos.ClienteDomicilio, Total = Datos.Total,
            Descripcion = Datos.Descripcion, Logo = png.ToArray(), FormaPago = "Transferencia bancaria", PagoEtiqueta = "INGRESAR EN LA CUENTA",
            PagoCuenta = "ES12 0000 0000 0000 0000 0000", QrUrl = "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=F26-000123"
        };
        var con = new GeneradorPdfFastReport().Generar(PlantillaFactura.Factura, completa, Lineas(3), Resumen);
        var sin = new GeneradorPdfFastReport().Generar(PlantillaFactura.Factura, Datos, Lineas(3), Resumen);
        Assert.True(Regex.Matches(Latin1(con), @"/Subtype\s*/Image").Count >= 2, "Faltan el logo o el QR en el PDF");
        Assert.Equal(0, Regex.Matches(Latin1(sin), @"/Subtype\s*/Image").Count);
        Guardar("factura-logo-qr.pdf", con);
    }

    [Fact]
    public void SinWebView2ElErrorDiceComoInstalarlo()
    {
        var vacia = Directory.CreateTempSubdirectory("procontavf-sin-webview2-");
        try
        {
            var conversor = new ConversorHtmlPdf(carpetaNavegador: vacia.FullName);
            var error = Assert.Throws<ErrorMotorPdf>(() => new GeneradorPdfFastReport(conversor: conversor).Generar(PlantillaFactura.Factura, Datos, Lineas(1), Resumen));
            Assert.Contains("WebView2", error.Message);
            Assert.Contains(ConversorHtmlPdf.UrlInstalador, error.Message);
        }
        finally
        {
            vacia.Delete(true);
        }
    }

    [Fact]
    public void VariosPdfALaVezDesdeHilosDistintos()
    {
        var pdfs = Enumerable.Range(0, 4).AsParallel()
            .Select(_ => new GeneradorPdfFastReport().Generar(PlantillaFactura.Factura, Datos, Lineas(3), Resumen)).ToList();
        Assert.All(pdfs, p => Assert.Equal(1, Paginas(p)));
    }
}
