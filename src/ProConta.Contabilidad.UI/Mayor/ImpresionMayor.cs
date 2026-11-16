using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Tablas;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>
/// Imprime la vista del Mayor desde la aplicación, sin librerías nuevas (contabilidad/mayor): un FlowDocument de WPF con la
/// cabecera (cuentas, ejercicios, vista y filtros), las columnas visibles en su orden, todas las filas de la vista con sus
/// separadores de ejercicio y los totales. Se imprime en horizontal con el diálogo de impresión de Windows.
/// </summary>
public static class ImpresionMayor
{
    public static void Imprimir(TablaProConta tabla, string titulo, string descripcion, string totales, Action<string> avisar)
    {
        var dialogo = new PrintDialog();
        try { dialogo.PrintTicket.PageOrientation = PageOrientation.Landscape; } catch (PrintQueueException) { }
        if (dialogo.ShowDialog() != true) return;
        try
        {
            var doc = Documento(tabla, titulo, descripcion, totales, dialogo.PrintableAreaWidth, dialogo.PrintableAreaHeight);
            dialogo.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, titulo);
            avisar($"Enviado a la impresora: {titulo} ({Formatos.Plural(tabla.Items.Count, "fila", "filas")}).");
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            avisar($"No he podido imprimir: {ex.Message} Comprueba que la impresora está encendida y conectada, o elige «Microsoft Print to PDF» para guardarlo en un PDF.");
        }
    }

    /// <summary>Documento de la vista (público para poder probarlo o previsualizarlo).</summary>
    public static FlowDocument Documento(TablaProConta tabla, string titulo, string descripcion, string totales, double ancho, double alto)
    {
        var columnas = tabla.ColumnasExportables();
        var doc = new FlowDocument
        {
            PageWidth = ancho, PageHeight = alto, PagePadding = new Thickness(36), ColumnWidth = ancho,
            FontFamily = tabla.FontFamily, FontSize = 9, Foreground = Brushes.Black,
        };
        doc.Blocks.Add(new Paragraph(new Run(titulo)) { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        doc.Blocks.Add(new Paragraph(new Run(descripcion + " · impreso el " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"))) { Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });

        var t = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 0.5) };
        var numericas = columnas.Select(c => c.Titulo is "Debe" or "Haber" or "Saldo").ToArray();
        foreach (var c in columnas)
            t.Columns.Add(new TableColumn { Width = c.Titulo == "Concepto" ? new GridLength(3, GridUnitType.Star) : c.Titulo.StartsWith("Descripción") || c.Titulo == "Notas" ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
        var grupo = new TableRowGroup();
        var cab = new TableRow { FontWeight = FontWeights.SemiBold, Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF5)) };
        for (var i = 0; i < columnas.Count; i++) cab.Cells.Add(Celda(columnas[i].Titulo, numericas[i]));
        grupo.Rows.Add(cab);

        var agrupada = tabla.Items.Groups is { Count: > 0 };
        string? ejercicio = null;
        foreach (var o in tabla.Items)
        {
            if (o is not FilaMayorVM fila) continue;
            if (agrupada && fila.CabeceraEjercicio != ejercicio)
            {
                ejercicio = fila.CabeceraEjercicio;
                var sep = new TableRow { FontWeight = FontWeights.SemiBold };
                sep.Cells.Add(new TableCell(new Paragraph(new Run(ejercicio)) { Margin = new Thickness(2, 6, 2, 2) }) { ColumnSpan = columnas.Count });
                grupo.Rows.Add(sep);
            }
            var r = new TableRow { FontStyle = fila.EsApertura ? FontStyles.Italic : FontStyles.Normal };
            for (var i = 0; i < columnas.Count; i++) r.Cells.Add(Celda(Texto(columnas[i].Titulo, columnas[i].Valor(fila)), numericas[i]));
            grupo.Rows.Add(r);
        }
        t.RowGroups.Add(grupo);
        doc.Blocks.Add(t);
        doc.Blocks.Add(new Paragraph(new Run(totales)) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        return doc;
    }

    private static TableCell Celda(string texto, bool derecha) =>
        new(new Paragraph(new Run(texto)) { Margin = new Thickness(2, 1, 2, 1), TextAlignment = derecha ? TextAlignment.Right : TextAlignment.Left })
        {
            BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.5),
        };

    private static string Texto(string columna, object? valor) => valor switch
    {
        null => "",
        decimal d when columna is "Debe" or "Haber" => Formatos.ImporteOVacio(d),
        decimal d => Formatos.Importe(d),
        DateTime f => Formatos.Fecha(f),
        _ => valor.ToString() ?? "",
    };
}
