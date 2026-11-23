using System.Globalization;
using System.Text;
using ProConta.Contabilidad.CuentasAnuales;

namespace ProConta.Informes;

/// <summary>
/// PDF de las cuentas anuales del modelo pymes (contabilidad/procesos/cuentas-anuales): las páginas del modelo oficial del Registro Mercantil
/// (BP1 activo, BP2 patrimonio neto y pasivo y PP pérdidas y ganancias), cada una con el NIF, la denominación, el espacio para las firmas
/// de los administradores y la fecha de formulación, y las columnas «Notas de la memoria», ejercicio y ejercicio anterior. Es el mismo
/// PDF que usa la legalización del libro de Inventarios y Cuentas Anuales. Los importes van con dos decimales y los negativos entre paréntesis.
/// </summary>
public static class InformeCuentasAnuales
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Página del PDF: A4 vertical con 12 mm de margen.</summary>
    public static readonly PaginaPdf Pagina = new(210, 297, 12, 12, 12, 12);

    /// <summary>Importe como en el modelo: vacío si es cero; los negativos entre paréntesis.</summary>
    public static string Importe(decimal v) => v == 0m ? "" : v < 0m ? $"({(-v).ToString("N2", Es)})" : v.ToString("N2", Es);

    public static string NombreFichero(CuentasAnualesPymes c) => $"Cuentas anuales {c.Ejercicio}.pdf";

    /// <param name="notas">Número de la nota de la memoria de cada clave (columna «Notas de la memoria»); null, la columna vacía.</param>
    /// <param name="formuladas">Fecha de formulación que se escribe junto a las firmas; null, en blanco.</param>
    public static string Html(CuentasAnualesPymes c, IReadOnlyDictionary<string, string>? notas = null, DateTime? formuladas = null)
    {
        static string E(string? t) => (t ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Cuentas anuales ").Append(c.Ejercicio).Append("</title><style>")
          .Append("body{font:8.5pt 'Segoe UI',Arial,sans-serif;color:#111;margin:0}")
          .Append(".pag{page-break-after:always}.pag:last-child{page-break-after:auto}")
          .Append("h1{font-size:13pt;text-align:center;margin:0 0 6pt;position:relative}h1 span{position:absolute;right:0;font-size:12pt}")
          .Append(".cab{display:flex;border:1pt solid #111;margin-bottom:0}.cab .id{width:34%;padding:6pt;border-right:1pt solid #111}.cab .fi{flex:1;padding:6pt;min-height:52pt;position:relative}")
          .Append(".cab .fi small{position:absolute;bottom:4pt;left:6pt;color:#444}")
          .Append("table{border-collapse:collapse;width:100%}th,td{border:.6pt solid #111;padding:2.5pt 4pt}th{font-size:8pt;font-weight:600;text-align:center}")
          .Append("td.c{text-align:center;font-weight:600;width:42pt;font-family:Consolas,monospace}td.n{text-align:right;white-space:nowrap;width:78pt}td.m{width:44pt;text-align:center}")
          .Append("td.t{border-right:none}td.o{width:30pt;border-right:none;border-left:.6pt solid #111}")
          .Append("tr.l0 td,tr.l1 td{font-weight:700}tr.l2 td.t{font-weight:600}tr.l4 td.t{font-style:italic}")
          .Append("p.pie{font-size:7.5pt;margin:4pt 0 0}")
          .Append("</style></head><body>");
        Hoja(sb, c, "BALANCE DE PYMES", "BP1", "ACTIVO", c.Activo, notas, formuladas, E);
        Hoja(sb, c, "BALANCE DE PYMES", "BP2", "PATRIMONIO NETO Y PASIVO", c.PatrimonioNetoYPasivo, notas, formuladas, E);
        Hoja(sb, c, "CUENTA DE PÉRDIDAS Y GANANCIAS DE PYMES", "PP", "(DEBE) / HABER", c.PerdidasYGanancias, notas, formuladas, E);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void Hoja(StringBuilder sb, CuentasAnualesPymes c, string titulo, string codigo, string columna, IReadOnlyList<FilaCuentasAnuales> filas,
        IReadOnlyDictionary<string, string>? notas, DateTime? formuladas, Func<string?, string> E)
    {
        sb.Append("<div class=\"pag\"><h1>").Append(E(titulo)).Append("<span>").Append(codigo).Append("</span></h1>")
          .Append("<div class=\"cab\"><div class=\"id\"><b>NIF:</b> ").Append(E(c.Nif)).Append("<br><br>DENOMINACIÓN SOCIAL:<br><b>").Append(E(c.RazonSocial)).Append("</b></div>")
          .Append("<div class=\"fi\"><small>Espacio destinado para las firmas de los administradores")
          .Append(formuladas is DateTime fecha ? $" · Formuladas el {fecha:dd/MM/yyyy}" : "").Append("</small></div></div>");
        sb.Append("<table><thead><tr><th colspan=\"3\">").Append(E(columna)).Append("</th><th>NOTAS DE<br>LA MEMORIA</th><th>EJERCICIO ")
          .Append(c.Ejercicio).Append(" (1)</th><th>EJERCICIO ").Append(c.Ejercicio - 1).Append(" (2)</th></tr></thead><tbody>");
        foreach (var f in filas)
        {
            var p = f.Partida;
            sb.Append("<tr class=\"l").Append(p.Nivel).Append("\"><td class=\"o\">").Append(E(p.Ordinal)).Append("</td><td class=\"t\">").Append(E(p.Nombre)).Append("</td>")
              .Append("<td class=\"c\">").Append(p.Clave).Append("</td><td class=\"m\">").Append(E(notas?.GetValueOrDefault(p.Clave))).Append("</td>")
              .Append("<td class=\"n\">").Append(Importe(f.Actual)).Append("</td><td class=\"n\">").Append(c.Anterior is null ? "" : Importe(f.Anterior)).Append("</td></tr>");
        }
        sb.Append("</tbody></table><p class=\"pie\">(1) Ejercicio al que van referidas las cuentas anuales. (2) Ejercicio anterior.</p></div>");
    }
}
