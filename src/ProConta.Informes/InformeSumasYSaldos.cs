using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ProConta.Contabilidad.Balances;

namespace ProConta.Informes;

/// <summary>
/// Salidas del balance de sumas y saldos (contabilidad/balances/sumas-y-saldos): el HTML que el motor de informes convierte en PDF (A4 vertical,
/// texto seleccionable) y el libro Excel con una tabla con filtros. Las dos llevan las mismas filas y totales; los niveles por encima de la
/// subcuenta van en negrita y con más sangría cuanto más detallados.
/// </summary>
public static class InformeSumasYSaldos
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Página del PDF: A4 vertical con 12 mm de margen.</summary>
    public static readonly PaginaPdf Pagina = new(210, 297, 12, 12, 12, 12);

    public const string FormatoImporte = "#,##0.00;[Red]-#,##0.00;\"\"";
    public const string NombreTabla = "TablaSumasYSaldos";

    public static readonly IReadOnlyList<string> Columnas = ["Cuenta", "Nombre", "Nivel", "Sumas Debe", "Sumas Haber", "Saldo deudor", "Saldo acreedor"];

    public static string TextoNivel(NivelSumasYSaldos n) => n switch
    {
        NivelSumasYSaldos.Grupo => "Grupo",
        NivelSumasYSaldos.Subgrupo => "Subgrupo",
        NivelSumasYSaldos.Cuenta => "Cuenta",
        _ => "Subcuenta",
    };

    private static string Importe(decimal v) => v == 0m ? "" : v.ToString("N2", Es);

    /// <summary>HTML del informe para el PDF.</summary>
    public static string Html(ResultadoSumasYSaldos r)
    {
        static string E(string? t) => (t ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var detalle = r.NivelDetalle;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Sumas y saldos</title><style>")
          .Append("body{font:8.5pt 'Segoe UI',Arial,sans-serif;color:#16213A;margin:0}h1{font-size:14pt;margin:0 0 2pt}p{margin:0 0 6pt;color:#3B4252}")
          .Append("table{border-collapse:collapse;width:100%}thead{display:table-header-group}tr{page-break-inside:avoid}")
          .Append("th{text-align:left;border-bottom:1pt solid #16213A;padding:3pt 4pt;font-size:8pt;white-space:nowrap}th.n{text-align:right}")
          .Append("td{padding:2pt 4pt;border-bottom:.4pt solid #E1DDD2;vertical-align:top}.n{text-align:right;white-space:nowrap}.m{font-family:Consolas,monospace;white-space:nowrap}")
          .Append("tr.v1 td{background:#ECE8DE;font-weight:700}tr.v2 td{background:#F3F0E9;font-weight:600}tr.v3 td{font-weight:600}")
          .Append("tr.f td{border-top:1pt solid #16213A;font-weight:700}")
          .Append("</style></head><body>");
        sb.Append("<h1>Balance de sumas y saldos · ").Append(E(r.RazonSocial)).Append("</h1><p>")
          .Append(E($"{r.Descripcion} · generado el {r.Generado:dd/MM/yyyy HH:mm}")).Append("</p>");
        sb.Append("<table><thead><tr><th>Cuenta</th><th>Nombre</th><th class=\"n\">Sumas Debe</th><th class=\"n\">Sumas Haber</th>")
          .Append("<th class=\"n\">Saldo deudor</th><th class=\"n\">Saldo acreedor</th></tr></thead><tbody>");
        foreach (var f in r.Filas)
        {
            var clase = f.Nivel == detalle ? "" : f.Nivel switch { NivelSumasYSaldos.Grupo => "v1", NivelSumasYSaldos.Subgrupo => "v2", _ => "v3" };
            sb.Append(clase.Length > 0 ? $"<tr class=\"{clase}\">" : "<tr>")
              .Append("<td class=\"m\">").Append(E(f.Cuenta)).Append("</td><td>").Append(E(f.Nombre)).Append("</td>")
              .Append("<td class=\"n\">").Append(Importe(f.Debe)).Append("</td><td class=\"n\">").Append(Importe(f.Haber)).Append("</td>")
              .Append("<td class=\"n\">").Append(Importe(f.SaldoDeudor)).Append("</td><td class=\"n\">").Append(Importe(f.SaldoAcreedor)).Append("</td></tr>");
        }
        sb.Append("<tr class=\"f\"><td></td><td>").Append(E($"Totales ({TextoNivelPlural(detalle)}: {r.Filas.Count(f => f.Nivel == detalle)})")).Append("</td>")
          .Append("<td class=\"n\">").Append(r.TotalDebe.ToString("N2", Es)).Append("</td><td class=\"n\">").Append(r.TotalHaber.ToString("N2", Es)).Append("</td>")
          .Append("<td class=\"n\">").Append(r.TotalSaldoDeudor.ToString("N2", Es)).Append("</td><td class=\"n\">").Append(r.TotalSaldoAcreedor.ToString("N2", Es)).Append("</td></tr>");
        sb.Append("</tbody></table>");
        if (r.Filas.Count == 0) sb.Append("<p>No hay apuntes contabilizados entre esas fechas.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string TextoNivelPlural(NivelSumasYSaldos n) => n switch
    {
        NivelSumasYSaldos.Grupo => "grupos",
        NivelSumasYSaldos.Subgrupo => "subgrupos",
        NivelSumasYSaldos.Cuenta => "cuentas",
        _ => "subcuentas",
    };

    /// <summary>
    /// Libro Excel: título y periodo arriba, la tabla «TablaSumasYSaldos» con filtros (una fila por cuenta de cada nivel, con su nivel en una
    /// columna para filtrar) y la fila de totales del nivel más detallado debajo, con fórmulas sobre la tabla.
    /// </summary>
    public static void Excel(ResultadoSumasYSaldos r, string ruta)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("SumasYSaldos");
        hoja.Cell(1, 1).Value = $"Balance de sumas y saldos · {r.RazonSocial}";
        hoja.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        hoja.Cell(2, 1).Value = $"{r.Descripcion} · generado el {r.Generado:dd/MM/yyyy HH:mm}";
        hoja.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#3B4252"));

        const int cab = 4;
        for (var c = 0; c < Columnas.Count; c++) hoja.Cell(cab, c + 1).Value = Columnas[c];
        var fila = cab + 1;
        foreach (var f in r.Filas)
        {
            hoja.Cell(fila, 1).Value = f.Cuenta;
            hoja.Cell(fila, 2).Value = f.Nombre;
            hoja.Cell(fila, 3).Value = TextoNivel(f.Nivel);
            hoja.Cell(fila, 4).Value = f.Debe;
            hoja.Cell(fila, 5).Value = f.Haber;
            hoja.Cell(fila, 6).Value = f.SaldoDeudor;
            hoja.Cell(fila, 7).Value = f.SaldoAcreedor;
            if (f.Nivel != r.NivelDetalle)
            {
                var rango = hoja.Range(fila, 1, fila, Columnas.Count);
                rango.Style.Font.SetBold();
                if (f.Nivel == NivelSumasYSaldos.Grupo) rango.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#ECE8DE"));
                else if (f.Nivel == NivelSumasYSaldos.Subgrupo) rango.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F3F0E9"));
            }
            fila++;
        }
        var ultima = Math.Max(fila - 1, cab + 1);
        hoja.Column(1).Style.NumberFormat.Format = "@";
        var tabla = hoja.Range(cab, 1, ultima, Columnas.Count).CreateTable(NombreTabla);
        tabla.Theme = XLTableTheme.TableStyleLight1;
        tabla.ShowAutoFilter = true;
        for (var c = 4; c <= 7; c++) hoja.Range(cab + 1, c, ultima + 2, c).Style.NumberFormat.Format = FormatoImporte;

        // Totales del nivel más detallado, debajo de la tabla y con fórmulas sobre ella (los mismos importes que el PDF).
        var tot = ultima + 2;
        var nivel = TextoNivel(r.NivelDetalle);
        hoja.Cell(tot, 2).Value = $"Totales ({TextoNivelPlural(r.NivelDetalle)})";
        string Col(int c) => XLHelper.GetColumnLetterFromNumber(c);
        for (var c = 4; c <= 7; c++)
            hoja.Cell(tot, c).FormulaA1 = $"SUMIFS({Col(c)}{cab + 1}:{Col(c)}{ultima},$C${cab + 1}:$C${ultima},\"{nivel}\")";
        var rt = hoja.Range(tot, 1, tot, Columnas.Count);
        rt.Style.Font.SetBold();
        rt.Style.Border.TopBorder = XLBorderStyleValues.Medium;

        hoja.Column(1).Width = 12;
        hoja.Column(2).Width = 52;
        hoja.Column(3).Width = 11;
        for (var c = 4; c <= 7; c++) hoja.Column(c).Width = 15;
        hoja.SheetView.FreezeRows(cab);
        hoja.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        hoja.PageSetup.FitToPages(1, 0);
        hoja.PageSetup.SetRowsToRepeatAtTop(cab, cab);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        libro.SaveAs(ruta);
    }
}
