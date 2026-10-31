using ClosedXML.Excel;

namespace ProConta.UI.Base.Tablas;

/// <summary>Columna de una exportación: título y cómo sacar su valor de una fila.</summary>
public sealed record ColumnaExportable(string Titulo, Func<object, object?> Valor);

/// <summary>
/// «Exportar a Excel» común a todas las tablas: todas las filas de la vista, las mismas columnas visibles en el mismo orden.
/// Importes como número con formato de euros y fechas como fecha, para que se puedan sumar y filtrar en Excel.
/// </summary>
public static class ExportadorExcel
{
    public const string FormatoImporte = "#,##0.00 \"€\";-#,##0.00 \"€\"";
    public const string FormatoFecha = "dd/mm/yyyy";

    /// <summary>Escribe el libro y devuelve el número de filas exportadas.</summary>
    public static int Exportar(string ruta, IReadOnlyList<ColumnaExportable> columnas, IEnumerable<object> filas, string hoja = "Datos", string? titulo = null)
    {
        ArgumentNullException.ThrowIfNull(columnas);
        if (columnas.Count == 0) throw new ArgumentException("No hay columnas visibles que exportar.", nameof(columnas));
        using var libro = new XLWorkbook();
        var ws = libro.Worksheets.Add(NombreHoja(hoja));
        var f = 1;
        if (!string.IsNullOrWhiteSpace(titulo))
        {
            ws.Cell(f, 1).Value = titulo;
            ws.Cell(f, 1).Style.Font.Bold = true;
            ws.Cell(f, 1).Style.Font.FontSize = 13;
            f += 2;
        }
        var cab = f;
        for (var c = 0; c < columnas.Count; c++)
        {
            var celda = ws.Cell(cab, c + 1);
            celda.Value = columnas[c].Titulo;
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#FBFAF7");
            celda.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        var n = 0;
        foreach (var fila in filas)
        {
            f++; n++;
            for (var c = 0; c < columnas.Count; c++) Poner(ws.Cell(f, c + 1), columnas[c].Valor(fila));
        }
        if (n > 0) ws.Range(cab, 1, f, columnas.Count).SetAutoFilter();
        ws.SheetView.FreezeRows(cab);
        ws.Columns(1, columnas.Count).AdjustToContents(cab, Math.Min(f, cab + 500), 8, 80);
        libro.SaveAs(ruta);
        return n;
    }

    internal static void Poner(IXLCell celda, object? v)
    {
        switch (v)
        {
            case null: break;
            case decimal d: celda.Value = d; celda.Style.NumberFormat.Format = FormatoImporte; break;
            case double x: celda.Value = x; break;
            case int i: celda.Value = i; break;
            case long l: celda.Value = l; break;
            case bool b: celda.Value = b ? "Sí" : "No"; break;
            case DateOnly d: celda.Value = d.ToDateTime(TimeOnly.MinValue); celda.Style.NumberFormat.Format = FormatoFecha; break;
            case DateTime t: celda.Value = t; celda.Style.NumberFormat.Format = t.TimeOfDay == TimeSpan.Zero ? FormatoFecha : "dd/mm/yyyy hh:mm"; break;
            default: celda.Value = v.ToString(); break;
        }
    }

    private static string NombreHoja(string s)
    {
        var limpio = new string(s.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (limpio.Length == 0) limpio = "Datos";
        return limpio.Length > 31 ? limpio[..31] : limpio;
    }
}
