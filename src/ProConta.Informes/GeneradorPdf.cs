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
    public string ClienteNombre { get; init; } = "";
    public string ClienteNif { get; init; } = "";
    public string ClienteDomicilio { get; init; } = "";
    public string FormaPago { get; init; } = "";
    public string Total { get; init; } = "";
}

/// <summary>Línea de la factura, con sus importes ya formateados.</summary>
public sealed record LineaFacturaPdf(string Concepto, string Cantidad, string Precio, string Descuento, string Base, string Iva, string Total);

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
public sealed class GeneradorPdfFastReport(string? carpetaPlantillas = null, ConversorHtmlPdf? conversor = null) : IGeneradorPdfFactura
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
        informe.RegisterData(resumen.ToList(), "Resumen");
        foreach (var fuente in new[] { "Factura", "Lineas", "Resumen" }) informe.GetDataSource(fuente).Enabled = true;
        informe.Prepare();
        return MotorPdf.Generar(informe, conversor ?? ConversorHtmlPdf.Compartido);
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
