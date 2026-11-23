using ClosedXML.Excel;
using ProConta.Contabilidad.Iva;

namespace ProConta.Informes.Tests;

/// <summary>Salidas de los libros registro de IVA (informes/excel/libros-registro-iva): HTML del PDF y libro Excel.</summary>
public sealed class LibrosIvaInformeTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("ProContaVF-libros-iva-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private static LineaIva L(DateTime fecha, int asiento, SentidoIva sentido, string factura, decimal tipo, decimal b, decimal cuota, bool rect = false,
        OperacionIva op = OperacionIva.Nacional, DeduccionIva ded = DeduccionIva.Deducible100) =>
        new(fecha, (fecha.Month - 1) / 3 + 1, asiento, sentido, op, false, tipo, "R", "C", "A", ded,
            sentido == SentidoIva.Repercutido ? "Cliente <sintético> & Cía" : "Proveedor sintético", "B00000000", factura, b, cuota,
            sentido == SentidoIva.Soportado && op != OperacionIva.Nacional ? cuota : 0m, null, null)
        { FechaExpedicion = fecha, Rectificativa = rect };

    private static ResultadoLibrosIva Resultado(bool expedidas = true, bool recibidas = true) => CalculadoraLibrosIva.Calcular("Sociedad sintética", 2026,
        ParametrosLibrosIva.Trimestre(2026, 1, expedidas, recibidas),
        [
            L(new(2026, 1, 10), 1, SentidoIva.Repercutido, "F-1", 21m, 100m, 21m),
            L(new(2026, 1, 10), 1, SentidoIva.Repercutido, "F-1", 10m, 50m, 5m),
            L(new(2026, 2, 3), 2, SentidoIva.Repercutido, "R-1", 21m, -20m, -4.20m, rect: true),
            L(new(2026, 3, 31), 3, SentidoIva.Soportado, "P-77", 21m, 200m, 42m, ded: DeduccionIva.Prorrata),
            L(new(2026, 2, 20), 5, SentidoIva.Soportado, "AIRBNB-1", 21m, 10m, 2.10m, op: OperacionIva.Intracomunitaria, ded: DeduccionIva.NoDeducible),
        ], 0.15m, new DateTime(2026, 11, 23, 15, 30, 0));

    [Fact]
    public void El_html_lleva_los_dos_libros_con_totales_y_el_cuadre_con_el_303()
    {
        var html = InformeLibrosIva.Html(Resultado());
        Assert.Contains("Libros registro de IVA · Sociedad sintética", html);
        Assert.Contains("Ejercicio 2026 · 1T 2026 · por fecha contable del asiento, sin el asiento de cierre", html);
        Assert.Contains("Cliente &lt;sintético&gt; &amp; Cía", html);
        Assert.Contains("R-1 (R)", html);
        Assert.Contains("Total (2 facturas)", html);
        Assert.Contains("<h2 class=\"salto\">Facturas recibidas</h2>", html);
        Assert.Contains("Intracomunitaria", html);
        Assert.Contains("Cuadre con el 303 del periodo", html);
        Assert.Contains("prorrata provisional del ejercicio (15 %)", html);
        // Devengado: 21 + 5 de las facturas, −4,20 de la rectificativa y 2,10 autorrepercutido; deducible: 42 × 15 % = 6,30; resultado 23,90 − 6,30.
        Assert.Contains("<td>Resultado (devengado − deducible)</td><td></td><td class=\"n\">17,60</td>", html);
    }

    [Fact]
    public void Con_un_solo_libro_el_titulo_es_el_de_ese_libro()
    {
        var html = InformeLibrosIva.Html(Resultado(expedidas: false));
        Assert.Contains("Libro registro de facturas recibidas · Sociedad sintética", html);
        Assert.DoesNotContain("Facturas expedidas</h2>", html);
        Assert.Contains("<h2>Facturas recibidas</h2>", html);
    }

    [Fact]
    public void El_excel_tiene_una_hoja_por_libro_con_tabla_y_totales_y_el_resumen()
    {
        var ruta = Path.Combine(_carpeta, "l.xlsx");
        InformeLibrosIva.Excel(Resultado(), ruta);
        using var libro = new XLWorkbook(ruta);
        Assert.Equal(["Expedidas", "Recibidas", "Resumen"], libro.Worksheets.Select(h => h.Name));
        var exp = libro.Worksheet("Expedidas").Table(InformeLibrosIva.TablaExpedidas);
        Assert.Equal(InformeLibrosIva.ColumnasExpedidas, exp.Fields.Select(f => f.Name));
        Assert.Equal(3, exp.DataRange.RowCount());
        Assert.True(exp.ShowTotalsRow);
        Assert.Equal(130m, exp.DataRange.Rows().Sum(r => r.Cell(7).GetValue<decimal>()));
        Assert.Equal("Sí", exp.DataRange.Row(3).Cell(10).GetString());
        Assert.Equal(new DateTime(2026, 1, 10), exp.DataRange.Row(1).Cell(2).GetDateTime());
        var rec = libro.Worksheet("Recibidas").Table(InformeLibrosIva.TablaRecibidas);
        Assert.Equal(InformeLibrosIva.ColumnasRecibidas, rec.Fields.Select(f => f.Name));
        Assert.Equal("Intracomunitaria", rec.DataRange.Row(1).Cell(7).GetString());
        Assert.Equal("Prorrata", rec.DataRange.Row(2).Cell(13).GetString());
        var resumen = libro.Worksheet("Resumen");
        Assert.Contains(resumen.CellsUsed(), c => c.GetString() == "Cuadre con el 303 del periodo");
        var resultado = resumen.CellsUsed().Single(c => c.GetString() == "Resultado (devengado − deducible)");
        Assert.Equal(17.60m, resultado.WorksheetRow().Cell(4).GetValue<decimal>());
    }

    [Fact]
    public void Sin_facturas_el_pdf_lo_dice_y_el_excel_se_genera()
    {
        var vacio = CalculadoraLibrosIva.Calcular("S", 2026, ParametrosLibrosIva.Trimestre(2026, 4), [], null, DateTime.Now);
        Assert.Contains("No hay facturas expedidas contabilizadas en el periodo.", InformeLibrosIva.Html(vacio));
        Assert.Contains("se deduce al 100 %", InformeLibrosIva.Html(vacio));
        var ruta = Path.Combine(_carpeta, "v.xlsx");
        InformeLibrosIva.Excel(vacio, ruta);
        using var libro = new XLWorkbook(ruta);
        Assert.Equal(3, libro.Worksheets.Count);
    }
}
