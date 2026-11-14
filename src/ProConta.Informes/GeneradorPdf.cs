using System.IO;
using FastReport;
using FastReport.Export.Html;

namespace ProConta.Informes;

/// <summary>Cabecera de una factura para su plantilla: textos ya formateados; la plantilla no calcula nada (RF-REP-02).</summary>
public sealed class DatosFacturaPdf
{
    public string Titulo { get; init; } = "FACTURA";
    public string Aviso { get; init; } = "";
    public string Numero { get; init; } = "";
    public string Fecha { get; init; } = "";
    public string FechaOperacion { get; init; } = "";
    public string Periodo { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public string Rectifica { get; init; } = "";
    public string EmisorNombre { get; init; } = "";
    public string EmisorNif { get; init; } = "";
    public string EmisorDomicilio { get; init; } = "";
    /// <summary>Teléfono y correo de la empresa, en una línea (vacío: no sale).</summary>
    public string EmisorContacto { get; init; } = "";
    /// <summary>Logo de la empresa (JPG o PNG); sin logo, en su lugar sale el nombre de la empresa.</summary>
    public byte[]? Logo { get; init; }
    public string ClienteNombre { get; init; } = "";
    public string ClienteNif { get; init; } = "";
    public string ClienteDomicilio { get; init; } = "";
    /// <summary>Forma de pago y día de pago, en una línea («Recibo domiciliado · día de pago 5»).</summary>
    public string FormaPago { get; init; } = "";
    /// <summary>Rótulo de la cuenta: «Ingresar en la cuenta» (transferencia) o «Se cargará en su cuenta» (recibo).</summary>
    public string PagoEtiqueta { get; init; } = "";
    /// <summary>IBAN: el de la empresa entero en una transferencia; el del cliente enmascarado en un recibo.</summary>
    public string PagoCuenta { get; init; } = "";
    /// <summary>Banco y avisos del pago (por ejemplo, que se indique el número de factura en el concepto).</summary>
    public string PagoDetalle { get; init; } = "";
    /// <summary>URL del QR tributario de VeriFactu; vacía mientras VeriFactu esté desactivado, y entonces no sale el QR.</summary>
    public string QrUrl { get; init; } = "";
    public string Total { get; init; } = "";

    /// <summary>Pie de página: nombre, NIF y domicilio del emisor en una línea.</summary>
    public string PieEmisor => string.Join(" · ", new[] { EmisorNombre, string.IsNullOrWhiteSpace(EmisorNif) ? "" : $"NIF {EmisorNif}",
        string.Join(", ", EmisorDomicilio.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) }.Where(x => x.Length > 0));
}

/// <summary>Línea de la factura, con sus importes ya formateados: el concepto en negrita y su detalle (descripción, fecha) debajo.</summary>
public sealed record LineaFacturaPdf(string Concepto, string Cantidad, string Precio, string Descuento, string Base, string Iva, string Total, string Detalle = "");

/// <summary>Resumen de importes en dos textos de varias líneas, para la zona fija del pie de la factura.</summary>
public sealed record TotalesFacturaPdf(string Etiquetas, string Importes);

/// <summary>Fila del resumen (base, cuotas por tipo, suplidos, retención); el total va aparte.</summary>
public sealed record ResumenFacturaPdf(string Etiqueta, string Importe);

/// <summary>Plantillas de factura que se publican junto al programa.</summary>
public enum PlantillaFactura
{
    Factura,
    Proforma
}

/// <summary>Convierte el modelo estable de una factura en un PDF.</summary>
public interface IGeneradorPdfFactura
{
    byte[] Generar(PlantillaFactura plantilla, DatosFacturaPdf datos, IReadOnlyList<LineaFacturaPdf> lineas, IReadOnlyList<ResumenFacturaPdf> resumen);
}

/// <summary>
/// Generador con FastReport (informes/reporting): las plantillas .frx de la carpeta Plantillas junto al programa se pueden
/// editar con FastReport Designer sin recompilar, y solo reciben el modelo: nunca leen Access ni calculan importes.
/// FastReport prepara y pagina el informe, lo exporta a HTML y WebView2 lo pasa a PDF con texto real (2026-11-12 15:19).
/// </summary>
public sealed partial class GeneradorPdfFastReport(string? carpetaPlantillas = null, ConversorHtmlPdf? conversor = null) : IGeneradorPdfFactura
{
    private readonly string _carpeta = carpetaPlantillas ?? Path.Combine(AppContext.BaseDirectory, "Plantillas");

    public byte[] Generar(PlantillaFactura plantilla, DatosFacturaPdf datos, IReadOnlyList<LineaFacturaPdf> lineas, IReadOnlyList<ResumenFacturaPdf> resumen)
    {
        var ruta = Path.Combine(_carpeta, plantilla == PlantillaFactura.Proforma ? "FacturaProforma.frx" : "Factura.frx");
        if (!File.Exists(ruta)) throw new FileNotFoundException($"No encuentro la plantilla {ruta}. Vuelve a instalar el programa o restaura esa plantilla.", ruta);
        using var informe = new Report();
        informe.Load(ruta);
        informe.RegisterData(new[] { datos }, "Factura");
        informe.RegisterData(lineas.ToList(), "Lineas");
        informe.RegisterData(new[] { new TotalesFacturaPdf(string.Join("\r\n", resumen.Select(r => r.Etiqueta)), string.Join("\r\n", resumen.Select(r => r.Importe))) }, "Totales");
        foreach (var fuente in new[] { "Factura", "Lineas", "Totales" }) informe.GetDataSource(fuente).Enabled = true;
        AjustarDiseno(informe, datos);
        informe.Prepare();
        return MotorPdf.Generar(informe, conversor ?? ConversorHtmlPdf.Compartido);
    }
}

public sealed partial class GeneradorPdfFastReport
{
    /// <summary>
    /// Partes opcionales del diseño (facturacion/factura-venta 2026-11-13): el logo o, sin logo, el nombre de la empresa; el QR
    /// de VeriFactu solo con su URL, y entonces el recuadro del cliente se estrecha para dejarle sitio; los rótulos sin dato no salen.
    /// Si una plantilla sustituida por el usuario no tiene alguno de esos objetos, se deja como está.
    /// </summary>
    private static void AjustarDiseno(Report informe, DatosFacturaPdf datos)
    {
        T? Objeto<T>(string nombre) where T : ComponentBase => informe.FindObject(nombre) as T;
        void Ocultar(params string[] nombres) { foreach (var n in nombres) if (Objeto<ComponentBase>(n) is { } o) o.Visible = false; }

        if (datos.Logo is { Length: > 0 } logo && Objeto<PictureObject>("Logo") is { } imagen)
        {
            using var flujo = new MemoryStream(logo);
            using var leida = System.Drawing.Image.FromStream(flujo);
            imagen.Image = new System.Drawing.Bitmap(leida);
            Ocultar("LogoTexto");
        }
        else Ocultar("Logo");

        if (string.IsNullOrWhiteSpace(datos.QrUrl)) Ocultar("EtQr", "Qr", "QrVeriFactu", "QrLeyenda");
        else
        {
            var ancho = FastReport.Utils.Units.Millimeters;
            if (Objeto<TextObject>("CajaCliente") is { } caja) caja.Width = 74 * ancho;
            foreach (var n in new[] { "ClienteNombre", "ClienteDatos" }) if (Objeto<TextObject>(n) is { } t) t.Width = 66 * ancho;
        }

        if (string.IsNullOrWhiteSpace(datos.Periodo)) Ocultar("EtPeriodo");
        if (string.IsNullOrWhiteSpace(datos.PagoCuenta)) Ocultar("EtCuenta");
        if (string.IsNullOrWhiteSpace(datos.FormaPago) && string.IsNullOrWhiteSpace(datos.PagoCuenta)) Ocultar("EtPago");
    }
}

/// <summary>Motor PDF único de los informes: un informe de FastReport ya preparado › HTML › PDF con WebView2.</summary>
public static class MotorPdf
{
    /// <summary>Exporta el informe preparado a una sola página HTML (capas e imágenes incrustadas) y la convierte en PDF.</summary>
    public static byte[] Generar(Report informe, ConversorHtmlPdf conversor) => conversor.Convertir(Html(informe), Pagina(informe));

    /// <summary>El HTML tal como lo exporta FastReport, con las imágenes incrustadas (sirve también de salida HTML).</summary>
    public static string Html(Report informe)
    {
        using var html = new HTMLExport { SinglePage = true, Navigator = false, EmbedPictures = true, Layers = true, SubFolder = false, Wysiwyg = true, Print = false };
        using var salida = new MemoryStream();
        informe.Export(html, salida);
        return System.Text.Encoding.UTF8.GetString(salida.ToArray());
    }

    /// <summary>Tamaño y márgenes de la primera página de la plantilla, en milímetros.</summary>
    public static PaginaPdf Pagina(Report informe)
    {
        var p = informe.Pages.OfType<ReportPage>().FirstOrDefault() ?? throw new InvalidOperationException("La plantilla no tiene ninguna página.");
        return new PaginaPdf(p.PaperWidth, p.PaperHeight, p.TopMargin, p.RightMargin, p.BottomMargin, p.LeftMargin);
    }
}
