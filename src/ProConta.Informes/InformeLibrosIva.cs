using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ProConta.Contabilidad.Iva;

namespace ProConta.Informes;

/// <summary>
/// Salidas de los libros registro de IVA (informes/excel/libros-registro-iva): el HTML que el motor de informes convierte en PDF (A4 apaisado,
/// texto seleccionable) y el libro Excel con una hoja por libro (tabla con filtros y fila de totales) y una hoja Resumen. Las dos salidas
/// llevan las mismas filas, los totales por tipo y el cuadre con el 303 del periodo.
/// </summary>
public static class InformeLibrosIva
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Página del PDF: A4 apaisado con 10 mm de margen.</summary>
    public static readonly PaginaPdf Pagina = new(297, 210, 10, 10, 10, 10);

    public const string FormatoImporte = "#,##0.00;[Red]-#,##0.00;\"\"";
    public const string TablaExpedidas = "TablaExpedidas";
    public const string TablaRecibidas = "TablaRecibidas";

    public static readonly IReadOnlyList<string> ColumnasExpedidas =
        ["Nº", "Fecha contable", "Fecha expedición", "Factura", "Cliente", "NIF", "Base imponible", "Tipo %", "Cuota repercutida", "Rectificativa", "Clave", "Asiento"];

    public static readonly IReadOnlyList<string> ColumnasRecibidas =
        ["Nº", "Fecha contable", "Fecha expedición", "Factura", "Proveedor", "NIF", "Operación", "Bien de inversión", "Base imponible", "Tipo %",
         "Cuota soportada", "Cuota autorrepercutida", "Deducción", "Asiento", "Aviso"];

    private static string Importe(decimal v) => v == 0m ? "" : v.ToString("N2", Es);
    private static string ImporteTotal(decimal v) => v.ToString("N2", Es);
    private static string Tipo(decimal t) => t.ToString("0.##", Es);
    private static string E(string? t) => (t ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Título del documento según los libros pedidos.</summary>
    public static string Titulo(ResultadoLibrosIva r) => r.Parametros.Expedidas && r.Parametros.Recibidas
        ? "Libros registro de IVA"
        : r.Parametros.Expedidas ? "Libro registro de facturas expedidas" : "Libro registro de facturas recibidas";

    /// <summary>Nota de la prorrata con la que se calcula la parte deducible del cuadre.</summary>
    public static string NotaProrrata(ResultadoLibrosIva r) => r.ProrrataInformada
        ? $"Deducible con la prorrata provisional del ejercicio ({(r.ProrrataProvisional * 100m).ToString("0.##", Es)} %), trimestre a trimestre como el 303; sin la regularización anual de la prorrata (casilla 44)."
        : "El ejercicio no tiene prorrata provisional: lo sujeto a prorrata se deduce al 100 %, como en el 303. Sin la regularización anual de la prorrata (casilla 44).";

    /// <summary>HTML del documento para el PDF.</summary>
    public static string Html(ResultadoLibrosIva r)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>").Append(E(Titulo(r))).Append("</title><style>")
          .Append("body{font:7.5pt 'Segoe UI',Arial,sans-serif;color:#16213A;margin:0}h1{font-size:13pt;margin:0 0 2pt}h2{font-size:11pt;margin:10pt 0 4pt}")
          .Append("h3{font-size:9pt;margin:8pt 0 3pt}p{margin:0 0 6pt;color:#3B4252}.salto{page-break-before:always}")
          .Append("table{border-collapse:collapse;width:100%}thead{display:table-header-group}tr{page-break-inside:avoid}")
          .Append("th{text-align:left;border-bottom:1pt solid #16213A;padding:2pt 3pt;font-size:7pt;white-space:nowrap}th.n{text-align:right}")
          .Append("td{padding:1.5pt 3pt;border-bottom:.4pt solid #E1DDD2;vertical-align:top}.n{text-align:right;white-space:nowrap}.c{white-space:nowrap}")
          .Append("tr.f td{border-top:1pt solid #16213A;font-weight:700}tr.s td{background:#F3F0E9;font-weight:600}.av{color:#9A3412}")
          .Append("table.r{width:auto;min-width:60%}")
          .Append("</style></head><body>");
        sb.Append("<h1>").Append(E(Titulo(r))).Append(" · ").Append(E(r.RazonSocial)).Append("</h1><p>")
          .Append(E($"{r.Descripcion} · generado el {r.Generado:dd/MM/yyyy HH:mm}")).Append("</p>");

        var primero = true;
        if (r.Parametros.Expedidas)
        {
            sb.Append("<h2>Facturas expedidas</h2>");
            sb.Append("<table><thead><tr><th>Nº</th><th>F. contable</th><th>F. expedición</th><th>Factura</th><th>Cliente</th><th>NIF</th>")
              .Append("<th class=\"n\">Base imponible</th><th class=\"n\">Tipo %</th><th class=\"n\">Cuota</th><th>Clave</th><th class=\"n\">Asiento</th></tr></thead><tbody>");
            foreach (var f in r.Expedidas)
                sb.Append("<tr><td class=\"n\">").Append(f.Orden).Append("</td><td class=\"c\">").Append(f.FechaContable.ToString("dd/MM/yyyy", Es))
                  .Append("</td><td class=\"c\">").Append(f.FechaExpedicion?.ToString("dd/MM/yyyy", Es)).Append("</td><td class=\"c\">").Append(E(f.Factura))
                  .Append(f.Rectificativa ? " (R)" : "").Append("</td><td>").Append(E(f.Tercero)).Append("</td><td class=\"c\">").Append(E(f.Nif))
                  .Append("</td><td class=\"n\">").Append(ImporteTotal(f.Base)).Append("</td><td class=\"n\">").Append(Tipo(f.TipoIva))
                  .Append("</td><td class=\"n\">").Append(Importe(f.Cuota)).Append("</td><td>").Append(E(f.ClaveOperacion))
                  .Append("</td><td class=\"n\">").Append(f.IdAsiento).Append("</td></tr>");
            sb.Append("<tr class=\"f\"><td></td><td colspan=\"5\">").Append(E($"Total ({r.FacturasExpedidas} facturas)")).Append("</td><td class=\"n\">")
              .Append(ImporteTotal(r.Expedidas.Sum(f => f.Base))).Append("</td><td></td><td class=\"n\">").Append(ImporteTotal(r.Expedidas.Sum(f => f.Cuota)))
              .Append("</td><td></td><td></td></tr></tbody></table>");
            if (r.Expedidas.Count == 0) sb.Append("<p>No hay facturas expedidas contabilizadas en el periodo.</p>");
            else if (r.Expedidas.Any(f => f.Rectificativa)) sb.Append("<p>(R): factura rectificativa.</p>");
            primero = false;
        }
        if (r.Parametros.Recibidas)
        {
            sb.Append(primero ? "<h2>" : "<h2 class=\"salto\">").Append("Facturas recibidas</h2>");
            sb.Append("<table><thead><tr><th>Nº</th><th>F. contable</th><th>F. expedición</th><th>Factura</th><th>Proveedor</th><th>NIF</th><th>Operación</th>")
              .Append("<th class=\"n\">Base imponible</th><th class=\"n\">Tipo %</th><th class=\"n\">Cuota</th><th class=\"n\">Autorrep.</th><th>Deducción</th><th class=\"n\">Asiento</th></tr></thead><tbody>");
            foreach (var f in r.Recibidas)
                sb.Append("<tr><td class=\"n\">").Append(f.Orden).Append("</td><td class=\"c\">").Append(f.FechaContable.ToString("dd/MM/yyyy", Es))
                  .Append("</td><td class=\"c\">").Append(f.FechaExpedicion?.ToString("dd/MM/yyyy", Es)).Append("</td><td class=\"c\">").Append(E(f.Factura))
                  .Append("</td><td>").Append(E(f.Tercero)).Append("</td><td class=\"c\">").Append(E(f.Nif)).Append("</td><td class=\"c\">").Append(E(f.TextoOperacion))
                  .Append(f.BienInversion ? " · BI" : "").Append("</td><td class=\"n\">").Append(ImporteTotal(f.Base)).Append("</td><td class=\"n\">").Append(Tipo(f.TipoIva))
                  .Append("</td><td class=\"n\">").Append(Importe(f.Cuota)).Append("</td><td class=\"n\">").Append(Importe(f.CuotaAutorrepercutida))
                  .Append("</td><td class=\"c\">").Append(E(f.Deduccion.Texto())).Append(f.Aviso is null ? "" : $" <span class=\"av\">({E(f.Aviso)})</span>")
                  .Append("</td><td class=\"n\">").Append(f.IdAsiento).Append("</td></tr>");
            sb.Append("<tr class=\"f\"><td></td><td colspan=\"6\">").Append(E($"Total ({r.FacturasRecibidas} facturas)")).Append("</td><td class=\"n\">")
              .Append(ImporteTotal(r.Recibidas.Sum(f => f.Base))).Append("</td><td></td><td class=\"n\">").Append(ImporteTotal(r.Recibidas.Sum(f => f.Cuota)))
              .Append("</td><td class=\"n\">").Append(ImporteTotal(r.Recibidas.Sum(f => f.CuotaAutorrepercutida))).Append("</td><td></td><td></td></tr></tbody></table>");
            if (r.Recibidas.Count == 0) sb.Append("<p>No hay facturas recibidas contabilizadas en el periodo.</p>");
            else if (r.Recibidas.Any(f => f.BienInversion)) sb.Append("<p>BI: bien de inversión. ISP: inversión del sujeto pasivo.</p>");
        }

        sb.Append("<h2 class=\"salto\">Resumen</h2>");
        void TotalesTipo(string titulo, IReadOnlyList<TotalTipoIva> totales, bool autorrep)
        {
            sb.Append("<h3>").Append(E(titulo)).Append("</h3><table class=\"r\"><thead><tr><th class=\"n\">Tipo %</th><th class=\"n\">Filas</th><th class=\"n\">Base imponible</th><th class=\"n\">Cuota</th>")
              .Append(autorrep ? "<th class=\"n\">Autorrepercutida</th>" : "").Append("</tr></thead><tbody>");
            foreach (var t in totales)
                sb.Append("<tr><td class=\"n\">").Append(Tipo(t.TipoIva)).Append("</td><td class=\"n\">").Append(t.Filas).Append("</td><td class=\"n\">").Append(ImporteTotal(t.Base))
                  .Append("</td><td class=\"n\">").Append(ImporteTotal(t.Cuota)).Append("</td>").Append(autorrep ? $"<td class=\"n\">{ImporteTotal(t.CuotaAutorrepercutida)}</td>" : "").Append("</tr>");
            sb.Append("<tr class=\"f\"><td>Total</td><td class=\"n\">").Append(totales.Sum(t => t.Filas)).Append("</td><td class=\"n\">").Append(ImporteTotal(totales.Sum(t => t.Base)))
              .Append("</td><td class=\"n\">").Append(ImporteTotal(totales.Sum(t => t.Cuota))).Append("</td>")
              .Append(autorrep ? $"<td class=\"n\">{ImporteTotal(totales.Sum(t => t.CuotaAutorrepercutida))}</td>" : "").Append("</tr></tbody></table>");
        }
        if (r.Parametros.Expedidas) TotalesTipo("Facturas expedidas por tipo", r.TotalesExpedidas, false);
        if (r.Parametros.Recibidas) TotalesTipo("Facturas recibidas por tipo", r.TotalesRecibidas, true);

        sb.Append("<h3>Cuadre con el 303 del periodo</h3><p>").Append(E(NotaProrrata(r))).Append("</p>")
          .Append("<table class=\"r\"><thead><tr><th>Concepto</th><th class=\"n\">Base imponible</th><th class=\"n\">Cuota</th></tr></thead><tbody>");
        foreach (var (texto, lineas, total) in Bloques303(r))
        {
            sb.Append("<tr class=\"s\"><td colspan=\"3\">").Append(E(texto)).Append("</td></tr>");
            foreach (var l in lineas)
                sb.Append("<tr><td>").Append(E(l.Concepto)).Append("</td><td class=\"n\">").Append(l.ConBase ? ImporteTotal(l.En(0).Base) : "")
                  .Append("</td><td class=\"n\">").Append(l.ConCuota ? ImporteTotal(l.En(0).Cuota) : "").Append("</td></tr>");
            if (total is { } t) sb.Append("<tr class=\"f\"><td>").Append(E(t.Texto)).Append("</td><td></td><td class=\"n\">").Append(ImporteTotal(t.Importe)).Append("</td></tr>");
        }
        sb.Append("<tr class=\"f\"><td>Resultado (devengado − deducible)</td><td></td><td class=\"n\">").Append(ImporteTotal(r.Cuadre303.Resultado(0))).Append("</td></tr>");
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    /// <summary>Bloques del cuadre con el 303: devengado, deducible e informativo, con su total si lo tiene. Solo las líneas con importe.</summary>
    public static IReadOnlyList<(string Texto, IReadOnlyList<Linea303> Lineas, (string Texto, decimal Importe)? Total)> Bloques303(ResultadoLibrosIva r)
    {
        static bool ConImporte(Linea303 l) => l.En(0).Base != 0m || l.En(0).Cuota != 0m;
        var m = r.Cuadre303;
        return
        [
            ("IVA devengado", m.Devengado.Where(ConImporte).ToList(), ("Total devengado", m.TotalDevengado(0))),
            ("IVA deducible", m.Deducible.Where(ConImporte).ToList(), ("Total a deducir", m.TotalDeducible(0))),
            ("Informativo", m.Informativo.Where(ConImporte).ToList(), null),
        ];
    }

    /// <summary>
    /// Libro Excel: una hoja por libro pedido («Expedidas», «Recibidas») con título, periodo y la tabla con filtros y fila de totales
    /// (subtotales que siguen a los filtros), y una hoja «Resumen» con los totales por tipo y el cuadre con el 303.
    /// </summary>
    public static void Excel(ResultadoLibrosIva r, string ruta)
    {
        using var libro = new XLWorkbook();
        if (r.Parametros.Expedidas)
        {
            var hoja = Hoja(libro, "Expedidas", "Libro registro de facturas expedidas", r);
            var filas = r.Expedidas.Select(f => new object?[]
            {
                f.Orden, f.FechaContable, f.FechaExpedicion, f.Factura, f.Tercero, f.Nif, f.Base, f.TipoIva, f.Cuota,
                f.Rectificativa ? "Sí" : "", f.ClaveOperacion, f.IdAsiento,
            }).ToList();
            Tabla(hoja, TablaExpedidas, ColumnasExpedidas, filas, importes: [7, 9], tipo: 8, fechas: [2, 3],
                anchos: [6, 11, 11, 16, 40, 13, 14, 7, 14, 11, 7, 9]);
        }
        if (r.Parametros.Recibidas)
        {
            var hoja = Hoja(libro, "Recibidas", "Libro registro de facturas recibidas", r);
            var filas = r.Recibidas.Select(f => new object?[]
            {
                f.Orden, f.FechaContable, f.FechaExpedicion, f.Factura, f.Tercero, f.Nif, f.TextoOperacion, f.BienInversion ? "Sí" : "",
                f.Base, f.TipoIva, f.Cuota, f.CuotaAutorrepercutida, f.Deduccion.Texto(), f.IdAsiento, f.Aviso,
            }).ToList();
            Tabla(hoja, TablaRecibidas, ColumnasRecibidas, filas, importes: [9, 11, 12], tipo: 10, fechas: [2, 3],
                anchos: [6, 11, 11, 18, 40, 13, 20, 10, 14, 7, 14, 14, 15, 9, 26]);
        }
        Resumen(libro, r);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        libro.SaveAs(ruta);
    }

    private static IXLWorksheet Hoja(XLWorkbook libro, string nombre, string titulo, ResultadoLibrosIva r)
    {
        var hoja = libro.Worksheets.Add(nombre);
        hoja.Cell(1, 1).Value = $"{titulo} · {r.RazonSocial}";
        hoja.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        hoja.Cell(2, 1).Value = $"{r.Descripcion} · generado el {r.Generado:dd/MM/yyyy HH:mm}";
        hoja.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#3B4252"));
        return hoja;
    }

    private const int Cabecera = 4;

    private static void Tabla(IXLWorksheet hoja, string nombre, IReadOnlyList<string> columnas, IReadOnlyList<object?[]> filas,
        int[] importes, int tipo, int[] fechas, int[] anchos)
    {
        for (var c = 0; c < columnas.Count; c++) hoja.Cell(Cabecera, c + 1).Value = columnas[c];
        var fila = Cabecera + 1;
        foreach (var valores in filas)
        {
            for (var c = 0; c < valores.Length; c++)
                hoja.Cell(fila, c + 1).Value = valores[c] switch
                {
                    null => Blank.Value,
                    int i => i,
                    decimal d => d,
                    DateTime t => t,
                    var o => o.ToString(),
                };
            fila++;
        }
        var ultima = Math.Max(fila - 1, Cabecera + 1);
        foreach (var c in new[] { 4, 6 }) hoja.Range(Cabecera + 1, c, ultima, c).Style.NumberFormat.Format = "@";
        var tabla = hoja.Range(Cabecera, 1, ultima, columnas.Count).CreateTable(nombre);
        tabla.Theme = XLTableTheme.TableStyleLight1;
        tabla.ShowAutoFilter = true;
        tabla.ShowTotalsRow = true;
        tabla.Field(0).TotalsRowLabel = "Total";
        foreach (var c in importes) tabla.Field(c - 1).TotalsRowFunction = XLTotalsRowFunction.Sum;
        var hasta = ultima + 1;
        foreach (var c in importes) hoja.Range(Cabecera + 1, c, hasta, c).Style.NumberFormat.Format = FormatoImporte;
        hoja.Range(Cabecera + 1, tipo, ultima, tipo).Style.NumberFormat.Format = "0.##";
        foreach (var c in fechas) hoja.Range(Cabecera + 1, c, ultima, c).Style.NumberFormat.Format = "dd/mm/yyyy";
        for (var c = 0; c < anchos.Length; c++) hoja.Column(c + 1).Width = anchos[c];
        hoja.SheetView.FreezeRows(Cabecera);
        hoja.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        hoja.PageSetup.FitToPages(1, 0);
        hoja.PageSetup.SetRowsToRepeatAtTop(Cabecera, Cabecera);
    }

    private static void Resumen(XLWorkbook libro, ResultadoLibrosIva r)
    {
        var hoja = Hoja(libro, "Resumen", "Libros registro de IVA · resumen", r);
        var fila = 4;
        void Cab(params string[] textos)
        {
            for (var i = 0; i < textos.Length; i++) hoja.Cell(fila, i + 1).Value = textos[i];
            var rango = hoja.Range(fila, 1, fila, textos.Length);
            rango.Style.Font.SetBold();
            rango.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            fila++;
        }
        void Titulo(string texto)
        {
            hoja.Cell(fila, 1).Value = texto;
            hoja.Cell(fila, 1).Style.Font.SetBold().Font.SetFontSize(12);
            fila++;
        }
        void Totales(string titulo, IReadOnlyList<TotalTipoIva> totales, bool autorrep)
        {
            Titulo(titulo);
            Cab(autorrep ? ["Tipo %", "Filas", "Base imponible", "Cuota", "Autorrepercutida"] : ["Tipo %", "Filas", "Base imponible", "Cuota"]);
            foreach (var t in totales)
            {
                hoja.Cell(fila, 1).Value = t.TipoIva;
                hoja.Cell(fila, 2).Value = t.Filas;
                hoja.Cell(fila, 3).Value = t.Base;
                hoja.Cell(fila, 4).Value = t.Cuota;
                if (autorrep) hoja.Cell(fila, 5).Value = t.CuotaAutorrepercutida;
                fila++;
            }
            hoja.Cell(fila, 1).Value = "Total";
            hoja.Cell(fila, 2).Value = totales.Sum(t => t.Filas);
            hoja.Cell(fila, 3).Value = totales.Sum(t => t.Base);
            hoja.Cell(fila, 4).Value = totales.Sum(t => t.Cuota);
            if (autorrep) hoja.Cell(fila, 5).Value = totales.Sum(t => t.CuotaAutorrepercutida);
            hoja.Range(fila, 1, fila, autorrep ? 5 : 4).Style.Font.SetBold().Border.TopBorder = XLBorderStyleValues.Thin;
            fila += 2;
        }
        if (r.Parametros.Expedidas) Totales("Facturas expedidas por tipo", r.TotalesExpedidas, false);
        if (r.Parametros.Recibidas) Totales("Facturas recibidas por tipo", r.TotalesRecibidas, true);

        Titulo("Cuadre con el 303 del periodo");
        hoja.Cell(fila, 1).Value = NotaProrrata(r);
        hoja.Cell(fila, 1).Style.Font.SetItalic();
        fila++;
        Cab("Concepto", "", "Base imponible", "Cuota");
        foreach (var (texto, lineas, total) in Bloques303(r))
        {
            hoja.Cell(fila, 1).Value = texto;
            hoja.Range(fila, 1, fila, 4).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#F3F0E9"));
            fila++;
            foreach (var l in lineas)
            {
                hoja.Cell(fila, 1).Value = l.Concepto;
                if (l.ConBase) hoja.Cell(fila, 3).Value = l.En(0).Base;
                if (l.ConCuota) hoja.Cell(fila, 4).Value = l.En(0).Cuota;
                if (l.Nota.Length > 0) hoja.Cell(fila, 1).CreateComment().AddText(l.Nota);
                fila++;
            }
            if (total is { } t)
            {
                hoja.Cell(fila, 1).Value = t.Texto;
                hoja.Cell(fila, 4).Value = t.Importe;
                hoja.Range(fila, 1, fila, 4).Style.Font.SetBold().Border.TopBorder = XLBorderStyleValues.Thin;
                fila++;
            }
        }
        hoja.Cell(fila, 1).Value = "Resultado (devengado − deducible)";
        hoja.Cell(fila, 4).Value = r.Cuadre303.Resultado(0);
        hoja.Range(fila, 1, fila, 4).Style.Font.SetBold().Border.TopBorder = XLBorderStyleValues.Medium;

        hoja.Column(1).Width = 58;
        hoja.Column(2).Width = 8;
        hoja.Column(3).Width = 16;
        hoja.Column(4).Width = 16;
        hoja.Column(5).Width = 16;
        hoja.Range(5, 3, fila, 5).Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00";
        hoja.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        hoja.PageSetup.FitToPages(1, 0);
    }
}
