using System.Globalization;
using System.IO;
using System.Text;
using ProConta.Contabilidad.Legalizacion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ProConta.Informes;

/// <summary>
/// Los dos libros de la legalización telemática (contabilidad/procesos/legalizacion-libros), en PDF con su nombre oficial:
/// <list type="bullet">
/// <item><c>DIARIO_001.pdf</c>: portada y el Diario en A4 apaisado, con número de orden y número de asiento, y «Suma anterior» y «Suma y sigue» en cada página.</item>
/// <item><c>INV_CUEN_001.pdf</c>: portada con índice, balance inicial (la apertura), balances de sumas y saldos al final de cada trimestre
/// (el informe de contabilidad/balances/sumas-y-saldos), inventario de cierre (saldos de los grupos 1 a 5 y activos) y cuentas anuales con
/// su memoria (el PDF de contabilidad/procesos/cuentas-anuales), sin recalcular nada.</item>
/// </list>
/// Cada parte se convierte a PDF por separado (HTML › PDF con WebView2), se unen con PdfPig y todas las páginas llevan al pie la sociedad,
/// el libro y el folio («Folio 3 de 120»).
/// </summary>
public static class InformeLegalizacion
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Página del Diario: A4 apaisado, 12 mm de margen y 14 mm abajo para el pie con el folio.</summary>
    public static readonly PaginaPdf PaginaDiario = new(297, 210, 12, 12, 14, 12);

    /// <summary>Páginas propias del libro de Inventarios (portada, balance inicial, inventario de cierre): A4 vertical.</summary>
    public static readonly PaginaPdf PaginaInventarios = new(210, 297, 12, 12, 14, 12);

    /// <summary>Apuntes por página del Diario: con filas de alto fijo caben sin que una página se parta en dos.</summary>
    public const int LineasPorPagina = 34;

    private static string E(string? t) => (t ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string Importe(decimal v) => v == 0m ? "" : v.ToString("N2", Es);
    private static string Total(decimal v) => v.ToString("N2", Es);

    private const string Estilo =
        "body{font:8pt 'Segoe UI',Arial,sans-serif;color:#111;margin:0}h1{font-size:13pt;margin:0 0 2pt}h2{font-size:11pt;margin:0 0 4pt}" +
        "p{margin:0 0 5pt}.apagado{color:#444}table{border-collapse:collapse;width:100%}thead{display:table-header-group}tr{page-break-inside:avoid}" +
        "th{text-align:left;border-bottom:1pt solid #111;padding:2pt 4pt;font-size:7.5pt;white-space:nowrap}th.n,td.n{text-align:right;white-space:nowrap}" +
        "td{padding:1.5pt 4pt;border-bottom:.4pt solid #ccc;vertical-align:top}.m{font-family:Consolas,monospace;white-space:nowrap}" +
        "tr.t td{border-top:1pt solid #111;border-bottom:none;font-weight:700}tr.c td{font-weight:700;background:#efefef}" +
        ".portada{height:240mm;display:flex;flex-direction:column;justify-content:center}.portada h1{font-size:22pt;margin-bottom:8pt}" +
        ".portada table{width:auto;margin-top:14pt}.portada td{border:none;padding:2pt 16pt 2pt 0;font-size:10pt}" +
        ".salto{page-break-before:always}";

    // ---------- Diario ----------

    /// <summary>HTML del libro Diario: la portada y una página por cada <see cref="PaginaDiario"/>, con filas de alto fijo.</summary>
    public static string HtmlDiario(LibroDiario libro, DateTime generado)
    {
        var paginas = LegalizacionLibros.Paginar(libro, LineasPorPagina);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Libro Diario ").Append(libro.Ejercicio).Append("</title><style>").Append(Estilo)
          .Append(".portada{height:170mm}.pag{height:183mm;overflow:hidden;page-break-before:always}")
          .Append(".pag table{table-layout:fixed}.pag td,.pag th{height:4.4mm;line-height:4.4mm;padding:0 3pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;vertical-align:middle}")
          .Append(".pag tr.p td{border-top:.8pt solid #111}.pag tr.s td{font-style:italic;color:#333}.pag .cab{display:flex;justify-content:space-between;align-items:baseline;height:7mm}")
          .Append("</style></head><body>");
        var primera = libro.Asientos.FirstOrDefault()?.Fecha;
        var ultima = libro.Asientos.LastOrDefault()?.Fecha;
        Portada(sb, "LIBRO DIARIO", libro.RazonSocial, libro.Nif, libro.Ejercicio,
        [
            ("Ejercicio", $"del 01/01/{libro.Ejercicio} al 31/12/{libro.Ejercicio}"),
            ("Asientos", $"{libro.Asientos.Count.ToString("N0", Es)} (números de orden 1 a {libro.Asientos.Count.ToString("N0", Es)})"),
            ("Apuntes", libro.NumeroApuntes.ToString("N0", Es)),
            ("Primer y último asiento", primera is null ? "—" : $"{primera:dd/MM/yyyy} y {ultima:dd/MM/yyyy}"),
            ("Total Debe", Total(libro.TotalDebe) + " €"),
            ("Total Haber", Total(libro.TotalHaber) + " €"),
            ("Páginas del Diario", paginas.Count.ToString("N0", Es)),
            ("Generado", $"{generado:dd/MM/yyyy HH:mm} con ProContaVF"),
        ], "Cada asiento lleva su número de orden en el libro (correlativo por fecha) y, en la columna «Asiento», su número en el programa. " +
           "La apertura va la primera del 1 de enero y la regularización y el cierre, los últimos del 31 de diciembre.");

        foreach (var p in paginas)
        {
            sb.Append("<div class=\"pag\"><div class=\"cab\"><h2>Libro Diario · ").Append(E(libro.RazonSocial)).Append("</h2><span class=\"apagado\">NIF ")
              .Append(E(libro.Nif)).Append(" · Ejercicio ").Append(libro.Ejercicio).Append(" · Página ").Append(p.Numero).Append(" de ").Append(paginas.Count).Append("</span></div>");
            sb.Append("<table><colgroup><col style=\"width:13mm\"><col style=\"width:27mm\"><col style=\"width:14mm\"><col style=\"width:21mm\"><col style=\"width:52mm\">")
              .Append("<col><col style=\"width:24mm\"><col style=\"width:24mm\"><col style=\"width:24mm\"></colgroup>")
              .Append("<thead><tr><th class=\"n\">Nº</th><th>Fecha</th><th class=\"n\">Asiento</th><th>Cuenta</th><th>Nombre de la cuenta</th><th>Concepto</th><th>Documento</th>")
              .Append("<th class=\"n\">Debe</th><th class=\"n\">Haber</th></tr></thead><tbody>");
            if (p.Numero > 1)
                sb.Append("<tr class=\"c\"><td colspan=\"7\">Suma anterior</td><td class=\"n\">").Append(Total(p.AnteriorDebe)).Append("</td><td class=\"n\">")
                  .Append(Total(p.AnteriorHaber)).Append("</td></tr>");
            foreach (var l in p.Lineas)
            {
                var cabeza = l.PrimeraDelAsiento || l.SigueDeLaAnterior;
                sb.Append(l.PrimeraDelAsiento ? "<tr class=\"p\">" : "<tr>");
                if (cabeza)
                    sb.Append("<td class=\"n\">").Append(l.Asiento.Numero).Append("</td><td>").Append(l.Asiento.Fecha.ToString("dd/MM/yyyy", Es))
                      .Append(l.SigueDeLaAnterior ? " (sigue)" : "").Append("</td><td class=\"n\">").Append(l.Asiento.IdAsiento).Append("</td>");
                else sb.Append("<td></td><td></td><td></td>");
                sb.Append("<td class=\"m\">").Append(E(l.Apunte.Cuenta)).Append("</td><td>").Append(E(l.Apunte.Nombre)).Append("</td><td>").Append(E(l.Apunte.Concepto))
                  .Append("</td><td>").Append(E(l.Apunte.Documento)).Append("</td><td class=\"n\">").Append(Importe(l.Apunte.Debe)).Append("</td><td class=\"n\">")
                  .Append(Importe(l.Apunte.Haber)).Append("</td></tr>");
            }
            sb.Append("<tr class=\"t\"><td colspan=\"7\">").Append(p.EsUltima ? $"Total del ejercicio {libro.Ejercicio}" : "Suma y sigue").Append("</td><td class=\"n\">")
              .Append(Total(p.SigueDebe)).Append("</td><td class=\"n\">").Append(Total(p.SigueHaber)).Append("</td></tr></tbody></table></div>");
        }
        if (libro.Asientos.Count == 0) sb.Append("<div class=\"pag\"><p>No hay asientos contabilizados en el ejercicio.</p></div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Genera <c>DIARIO_001.pdf</c> con el conversor dado (WebView2 en el programa) y le pone el pie con el folio.</summary>
    public static FicheroLegalizacion Diario(LibroDiario libro, DateTime generado, Func<string, PaginaPdf, byte[]> convertir)
    {
        var pdf = convertir(HtmlDiario(libro, generado), PaginaDiario);
        var (contenido, paginas) = Foliar([pdf], $"{libro.RazonSocial} · NIF {libro.Nif} · Libro Diario {libro.Ejercicio}");
        return new FicheroLegalizacion(LegalizacionLibros.FicheroDiario, "Libro Diario", contenido, paginas);
    }

    // ---------- Inventarios y Cuentas Anuales ----------

    /// <summary>Una parte del libro de Inventarios: su título en el índice y su PDF.</summary>
    private sealed record Parte(string Titulo, byte[] Pdf);

    /// <summary>
    /// Genera <c>INV_CUEN_001.pdf</c>: cada parte por separado, una portada con el índice y el folio en que empieza cada parte, todo unido y
    /// con el pie con el folio en cada página.
    /// </summary>
    public static FicheroLegalizacion Inventarios(DatosLegalizacion d, Func<string, PaginaPdf, byte[]> convertir)
    {
        var partes = new List<Parte>
        {
            new("1. Balance inicial (asiento de apertura)", convertir(HtmlSaldos(d, "Balance inicial",
                $"Saldo de cada subcuenta en el asiento de apertura del ejercicio {d.Ejercicio}.", d.BalanceInicial,
                "El ejercicio no tiene asiento de apertura: no hay balance inicial (primer ejercicio de la sociedad)."), PaginaInventarios)),
        };
        var trimestre = 0;
        foreach (var t in d.Trimestres)
        {
            trimestre++;
            partes.Add(new($"2.{trimestre}. Balance de sumas y saldos al {t.Parametros.Hasta:dd/MM/yyyy} ({trimestre}.º trimestre, acumulado)",
                convertir(InformeSumasYSaldos.Html(t), InformeSumasYSaldos.Pagina)));
        }
        partes.Add(new("3. Inventario de cierre a 31/12", convertir(HtmlInventario(d), PaginaInventarios)));
        partes.Add(new("4. Cuentas anuales: balance, pérdidas y ganancias y memoria",
            convertir(InformeCuentasAnuales.Html(d.CuentasAnuales, d.Memoria), InformeCuentasAnuales.Pagina)));

        // La portada ocupa una página; las partes empiezan en el folio 2.
        var inicio = 2;
        var indice = new List<(string, int)>();
        foreach (var p in partes)
        {
            indice.Add((p.Titulo, inicio));
            inicio += ContarPaginas(p.Pdf);
        }
        var portada = convertir(HtmlPortadaInventarios(d, indice, inicio - 1), PaginaInventarios);
        var (contenido, paginas) = Foliar([portada, .. partes.Select(p => p.Pdf)],
            $"{d.RazonSocial} · NIF {d.Nif} · Libro de Inventarios y Cuentas Anuales {d.Ejercicio}");
        return new FicheroLegalizacion(LegalizacionLibros.FicheroInventarios, "Libro de Inventarios y Cuentas Anuales", contenido, paginas);
    }

    public static string HtmlPortadaInventarios(DatosLegalizacion d, IReadOnlyList<(string Titulo, int Folio)> indice, int total)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Libro de Inventarios y Cuentas Anuales</title><style>").Append(Estilo)
          .Append(".indice{margin-top:18pt}.indice td{font-size:10pt;padding:3pt 10pt 3pt 0;border-bottom:.4pt solid #ccc}</style></head><body>");
        sb.Append("<div class=\"portada\"><h1>LIBRO DE INVENTARIOS Y CUENTAS ANUALES</h1><h2>").Append(E(d.RazonSocial)).Append(" · NIF ").Append(E(d.Nif)).Append("</h2>")
          .Append("<p style=\"font-size:11pt\">Ejercicio ").Append(d.Ejercicio).Append(": del 01/01/").Append(d.Ejercicio).Append(" al 31/12/").Append(d.Ejercicio)
          .Append(" · ").Append(total).Append(" folios</p>")
          .Append("<table class=\"indice\"><thead><tr><th>Contenido</th><th class=\"n\">Folio</th></tr></thead><tbody>");
        foreach (var (titulo, folio) in indice)
            sb.Append("<tr><td>").Append(E(titulo)).Append("</td><td class=\"n\">").Append(folio).Append("</td></tr>");
        sb.Append("</tbody></table><p class=\"apagado\" style=\"margin-top:14pt\">Contenido según el artículo 28 del Código de Comercio. Generado el ")
          .Append(d.Generado.ToString("dd/MM/yyyy HH:mm", Es)).Append(" con ProContaVF.</p></div></body></html>");
        return sb.ToString();
    }

    /// <summary>HTML de una relación de saldos por subcuenta (el balance inicial), con sus totales.</summary>
    public static string HtmlSaldos(DatosLegalizacion d, string titulo, string descripcion, IReadOnlyList<SaldoInventario> saldos, string siVacio)
    {
        var sb = Cabecera(d, titulo, descripcion);
        TablaSaldos(sb, saldos, siVacio);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>HTML del inventario de cierre: los saldos a 31/12 de las subcuentas de los grupos 1 a 5 y el detalle de los activos.</summary>
    public static string HtmlInventario(DatosLegalizacion d)
    {
        var sb = Cabecera(d, "Inventario de cierre",
            $"Saldo a 31/12/{d.Ejercicio} de cada subcuenta del balance (grupos 1 a 5), antes del asiento de cierre, y detalle del inmovilizado.");
        sb.Append("<h2>3.1. Saldos de las cuentas del balance</h2>");
        TablaSaldos(sb, d.InventarioCierre, "No hay subcuentas del balance con saldo a 31/12.");
        sb.Append("<h2 class=\"salto\">3.2. Inmovilizado: elementos de la sociedad</h2>");
        var activos = d.Activos.Where(a => a.FechaCompra is null || a.FechaCompra.Value.Year <= d.Ejercicio).OrderBy(a => a.Codigo, StringComparer.Ordinal).ToList();
        if (activos.Count == 0) sb.Append("<p>No hay activos dados de alta en la sociedad (Datos maestros › Activos).</p>");
        else
        {
            sb.Append("<table><thead><tr><th>Código</th><th>Elemento</th><th>Tipo</th><th>Compra</th><th class=\"n\">Coste</th><th class=\"n\">Amortización acumulada</th>")
              .Append("<th class=\"n\">Valor neto</th></tr></thead><tbody>");
            foreach (var a in activos)
                sb.Append("<tr><td class=\"m\">").Append(E(a.Codigo)).Append("</td><td>").Append(E(a.Nombre)).Append("</td><td>").Append(E(a.Tipo)).Append("</td><td>")
                  .Append(a.FechaCompra?.ToString("dd/MM/yyyy", Es)).Append("</td><td class=\"n\">").Append(Importe(a.Coste)).Append("</td><td class=\"n\">")
                  .Append(Importe(a.Amortizado)).Append("</td><td class=\"n\">").Append(Importe(a.ValorNeto)).Append("</td></tr>");
            sb.Append("<tr class=\"t\"><td colspan=\"4\">Totales (").Append(activos.Count).Append(" elementos)</td><td class=\"n\">").Append(Total(activos.Sum(a => a.Coste)))
              .Append("</td><td class=\"n\">").Append(Total(activos.Sum(a => a.Amortizado))).Append("</td><td class=\"n\">").Append(Total(activos.Sum(a => a.ValorNeto)))
              .Append("</td></tr></tbody></table><p class=\"apagado\" style=\"margin-top:4pt\">Coste según la ficha de cada activo; amortización acumulada, la de sus subcuentas 28 hasta el 31/12 sin el cierre.</p>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static StringBuilder Cabecera(DatosLegalizacion d, string titulo, string descripcion)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>").Append(E(titulo)).Append("</title><style>").Append(Estilo)
          .Append("h2{margin-top:10pt}</style></head><body><h1>").Append(E(titulo)).Append(" · ").Append(E(d.RazonSocial)).Append("</h1><p class=\"apagado\">NIF ")
          .Append(E(d.Nif)).Append(" · Ejercicio ").Append(d.Ejercicio).Append(" · ").Append(E(descripcion)).Append("</p>");
        return sb;
    }

    private static void TablaSaldos(StringBuilder sb, IReadOnlyList<SaldoInventario> saldos, string siVacio)
    {
        if (saldos.Count == 0) { sb.Append("<p>").Append(E(siVacio)).Append("</p>"); return; }
        sb.Append("<table><thead><tr><th>Subcuenta</th><th>Nombre</th><th class=\"n\">Saldo deudor</th><th class=\"n\">Saldo acreedor</th></tr></thead><tbody>");
        foreach (var s in saldos)
            sb.Append("<tr><td class=\"m\">").Append(E(s.Cuenta)).Append("</td><td>").Append(E(s.Nombre)).Append("</td><td class=\"n\">").Append(Importe(s.Deudor))
              .Append("</td><td class=\"n\">").Append(Importe(s.Acreedor)).Append("</td></tr>");
        sb.Append("<tr class=\"t\"><td></td><td>Totales (").Append(saldos.Count).Append(" subcuentas)</td><td class=\"n\">").Append(Total(saldos.Sum(s => s.Deudor)))
          .Append("</td><td class=\"n\">").Append(Total(saldos.Sum(s => s.Acreedor))).Append("</td></tr></tbody></table>");
    }

    private static void Portada(StringBuilder sb, string titulo, string razon, string nif, int ejercicio, IReadOnlyList<(string, string)> datos, string nota)
    {
        sb.Append("<div class=\"portada\"><h1>").Append(E(titulo)).Append("</h1><h2>").Append(E(razon)).Append(" · NIF ").Append(E(nif)).Append("</h2>")
          .Append("<p style=\"font-size:11pt\">Ejercicio ").Append(ejercicio).Append("</p><table>");
        foreach (var (k, v) in datos) sb.Append("<tr><td>").Append(E(k)).Append("</td><td><b>").Append(E(v)).Append("</b></td></tr>");
        sb.Append("</table><p class=\"apagado\" style=\"margin-top:14pt;max-width:180mm\">").Append(E(nota)).Append("</p></div>");
    }

    // ---------- unir y foliar ----------

    public static int ContarPaginas(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return doc.NumberOfPages;
    }

    /// <summary>Une los PDF en uno y escribe al pie de cada página el texto del libro y «Folio n de N». Devuelve el PDF y su número de páginas.</summary>
    public static (byte[] Contenido, int Paginas) Foliar(IReadOnlyList<byte[]> pdfs, string pie)
    {
        // Cada página se copia de su PDF de origen (no de uno unido antes con PdfMerger: sus páginas dejaban el texto añadido girado y diminuto).
        var docs = pdfs.Select(p => PdfDocument.Open(p)).ToList();
        try
        {
            return Foliar(docs, pie);
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }

    private static (byte[] Contenido, int Paginas) Foliar(IReadOnlyList<PdfDocument> docs, string pie)
    {
        var total = docs.Sum(d => d.NumberOfPages);
        using var builder = new PdfDocumentBuilder();
        // Arial de Windows (TrueType incrustada) para que salgan bien las tildes y la «ñ»; sin ella, Helvetica con el texto sin tildes.
        var arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        PdfDocumentBuilder.AddedFont fuente;
        var texto = pie;
        try
        {
            fuente = builder.AddTrueTypeFont(File.ReadAllBytes(arial));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            fuente = builder.AddStandard14Font(Standard14Font.Helvetica);
            texto = SinTildes(pie);
        }
        var n = 0;
        foreach (var doc in docs)
        for (var i = 1; i <= doc.NumberOfPages; i++)
        {
            n++;
            var pagina = builder.AddPage(doc, i);
            var ancho = doc.GetPage(i).Width;
            var folio = $"Folio {n} de {total}";
            var tam = 7d;
            pagina.AddText(texto, tam, new PdfPoint(34, 18), fuente);
            var medida = pagina.MeasureText(folio, tam, new PdfPoint(0, 0), fuente);
            var largo = medida.Count == 0 ? folio.Length * 3.6 : medida[^1].GlyphRectangle.Right - medida[0].GlyphRectangle.Left;
            pagina.AddText(folio, tam, new PdfPoint(ancho - 34 - largo, 18), fuente);
        }
        return (builder.Build(), total);
    }

    /// <summary>Las fuentes estándar del PDF que escribe PdfPig solo tienen ASCII: quita tildes y cambia lo demás por algo parecido.</summary>
    private static string SinTildes(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c < 0x80 ? c : c == '·' ? '-' : '?');
        }
        return sb.ToString();
    }
}
