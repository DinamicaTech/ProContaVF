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
    [InlineData(PlantillaFactura.Factura, 40, 2)]
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
        Assert.Empty(Regex.Matches(Latin1(sin), @"/Subtype\s*/Image"));
        Guardar("factura-logo-qr.pdf", con);
    }

    /// <summary>
    /// La segunda factura de una sesión lleva el logo igual que la primera: el mismo generador (y el conversor WebView2
    /// compartido) no arrastran nada entre exportaciones.
    /// </summary>
    [Fact]
    public void ElLogoSaleEnDosFacturasSeguidasDelMismoGenerador()
    {
        using var bitmap = new Bitmap(300, 60);
        using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.White); g.FillEllipse(Brushes.Orange, 5, 5, 50, 50); }
        using var png = new MemoryStream();
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var conLogo = new DatosFacturaPdf
        {
            Numero = Datos.Numero, Fecha = Datos.Fecha, EmisorNombre = Datos.EmisorNombre, EmisorNif = Datos.EmisorNif, EmisorDomicilio = Datos.EmisorDomicilio,
            ClienteNombre = Datos.ClienteNombre, ClienteNif = Datos.ClienteNif, ClienteDomicilio = Datos.ClienteDomicilio, Total = Datos.Total, Logo = png.ToArray()
        };
        var generador = new GeneradorPdfFastReport();
        var primera = generador.Generar(PlantillaFactura.Factura, conLogo, Lineas(3), Resumen);
        var segunda = generador.Generar(PlantillaFactura.Factura, conLogo, Lineas(3), Resumen);
        Assert.NotEmpty(Regex.Matches(Latin1(primera), @"/Subtype\s*/Image"));
        Assert.NotEmpty(Regex.Matches(Latin1(segunda), @"/Subtype\s*/Image"));
    }

    /// <summary>
    /// Muestras del diseño para revisarlo a ojo (se guardan si PROCONTA_PDF_PRUEBA indica una carpeta): transferencia sin
    /// VeriFactu, recibo con VeriFactu, proforma y una factura larga. El logo sale de PROCONTA_LOGO_PRUEBA si existe.
    /// </summary>
    [Fact]
    public void MuestrasDelDiseno()
    {
        var rutaLogo = Environment.GetEnvironmentVariable("PROCONTA_LOGO_PRUEBA");
        Assert.True(string.IsNullOrEmpty(rutaLogo) || File.Exists(rutaLogo), $"PROCONTA_LOGO_PRUEBA apunta a un fichero que no existe: {rutaLogo}");
        var logo = !string.IsNullOrEmpty(rutaLogo) ? File.ReadAllBytes(rutaLogo) : null;
        DatosFacturaPdf Muestra(string titulo, string numero, string aviso, string forma, string etiqueta, string cuenta, string detalle, string qr) => new()
        {
            Titulo = titulo, Aviso = aviso, Numero = numero, Fecha = "03/08/2026", Periodo = "01/08/2026 – 31/08/2026",
            Descripcion = "08AGO01 · Alquiler de agosto de 2026", EmisorNombre = "Sociedad sintética, S.L.", EmisorNif = "B12345674",
            EmisorDomicilio = "Calle Inventada, 1\r\n08000 Barcelona", EmisorContacto = "Tel. 600 000 000", Logo = logo,
            ClienteNombre = "COMERCIAL EJEMPLO, S.L.", ClienteNif = "B00000000", ClienteDomicilio = "Calle Mayor, 12, 3.º 2.ª\r\n08850 Gavà\r\nBarcelona",
            FormaPago = forma, PagoEtiqueta = etiqueta, PagoCuenta = cuenta, PagoDetalle = detalle, QrUrl = qr, Total = "797,40 €"
        };
        List<LineaFacturaPdf> lineas =
        [
            new("Alquiler local 18 BJ 1A", "1", "750,00", "", "750,00", "21 %", "907,50", "Alquiler del local en Avenida Inventada 18 Bajos 1a - 08850 Gavà (Barcelona)"),
            new("Suministro de agua", "1", "32,40", "", "32,40", "Supl.", "32,40", "Factura del 2.º bimestre, pagada por cuenta del cliente")
        ];
        List<ResumenFacturaPdf> resumen = [new("Base imponible", "750,00 €"), new("IVA 21 % sobre 750,00 €", "157,50 €"), new("Suplidos", "32,40 €"), new("Retención 19 %", "-142,50 €")];
        var transferencia = Muestra("FACTURA", "083204", "", "Transferencia bancaria · día de pago 5", "INGRESAR EN LA CUENTA", "ES12 0000 0000 0000 0000 0000",
            "Banco sintético\r\nIndique el número de factura en el concepto", "");
        var recibo = Muestra("FACTURA", "083204", "", "Recibo domiciliado · día de pago 5", "SE CARGARÁ EN SU CUENTA", "ES** **** **** **** **** 8356",
            "Banco sintético", "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=083204&fecha=03-08-2026&importe=797.40");
        var proforma = Muestra("FACTURA PROFORMA", "Borrador B-0007", "Documento informativo sin validez fiscal", "Transferencia bancaria", "INGRESAR EN LA CUENTA",
            "ES12 0000 0000 0000 0000 0000", "Banco sintético", "");
        var g = new GeneradorPdfFastReport();
        var a = g.Generar(PlantillaFactura.Factura, transferencia, lineas, resumen);
        var b = g.Generar(PlantillaFactura.Factura, recibo, lineas, resumen);
        var c = g.Generar(PlantillaFactura.Proforma, proforma, lineas, resumen);
        var larga = g.Generar(PlantillaFactura.Factura, transferencia, Enumerable.Range(1, 40).SelectMany(_ => lineas).ToList(), resumen);
        Assert.All(new[] { a, b, c }, p => Assert.Equal(1, Paginas(p)));
        Assert.True(Paginas(larga) >= 2);
        Guardar("muestra-A-transferencia.pdf", a);
        Guardar("muestra-B-verifactu-recibo.pdf", b);
        Guardar("muestra-C-proforma.pdf", c);
        Guardar("muestra-D-larga.pdf", larga);
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
