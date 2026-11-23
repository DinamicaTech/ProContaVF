using ProConta.Contabilidad.Diario;
using ProConta.Contabilidad.Legalizacion;
using ProConta.Informes;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ProConta.Informes.Tests;

/// <summary>
/// PDF de la legalización (contabilidad/procesos/legalizacion-libros) sin WebView2: el HTML del Diario lleva la portada, las páginas con
/// «Suma anterior», «Suma y sigue» y el total del ejercicio; al unir los PDF, cada página lleva su folio.
/// </summary>
public sealed class LegalizacionInformeTests
{
    private static readonly EjercicioDiario E = new(1, 2025, true, 4);

    private static LibroDiario Libro(int asientos)
    {
        var lista = new List<AsientoDiario>();
        var id = 0;
        for (var i = 1; i <= asientos; i++)
            lista.Add(new AsientoDiario { IdAsiento = i * 10, Ejercicio = E, Fecha = new DateTime(2025, 1, 1).AddDays(i), Descripcion = $"Asiento <{i}>" }.Con(
                new ApunteDiario { IdApunte = ++id, Orden = 1, Cuenta = "6290001", Debe = 10m, NombreCuenta = "Gastos & otros" },
                new ApunteDiario { IdApunte = ++id, Orden = 2, Cuenta = "5720001", Haber = 10m, NombreCuenta = "Banco" }));
        return LegalizacionLibros.Diario("Sociedad Sintética SL", "B00000000", 2025, lista);
    }

    [Fact]
    public void El_HTML_del_Diario_tiene_portada_suma_y_sigue_y_el_total_del_ejercicio()
    {
        var libro = Libro(40);  // 80 apuntes: 3 páginas de 34
        var html = InformeLegalizacion.HtmlDiario(libro, new DateTime(2026, 1, 15, 10, 0, 0));

        Assert.Contains("LIBRO DIARIO", html);
        Assert.Contains("Página 1 de 3", html);
        Assert.Contains("Página 3 de 3", html);
        Assert.Equal(2, CuantasVeces(html, ">Suma y sigue<"));
        Assert.Equal(2, CuantasVeces(html, ">Suma anterior<"));
        Assert.Contains("Total del ejercicio 2025", html);
        Assert.Contains("400,00", html);
        // Los textos de la BD van escapados.
        Assert.Contains("Asiento &lt;1&gt;", html);
        Assert.Contains("Gastos &amp; otros", html);
        Assert.DoesNotContain("<1>", html);
    }

    [Fact]
    public void Al_unir_los_PDF_cada_pagina_lleva_el_libro_y_su_folio()
    {
        var (pdf, paginas) = InformeLegalizacion.Foliar([Pdf(2), Pdf(1)], "Sociedad Sintética SL · NIF B00000000 · Libro Diario 2025");

        Assert.Equal(3, paginas);
        using var doc = PdfDocument.Open(pdf);
        Assert.Equal(3, doc.NumberOfPages);
        for (var n = 1; n <= 3; n++)
        {
            var texto = doc.GetPage(n).Text;
            Assert.Contains($"Folio {n} de 3", texto);
            Assert.Contains("Libro Diario 2025", texto);
            Assert.Contains("Sintética", texto);
        }
        Assert.Equal(3, InformeLegalizacion.ContarPaginas(pdf));
    }

    private static byte[] Pdf(int paginas)
    {
        using var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < paginas; i++) b.AddPage(PageSize.A4).AddText($"Contenido {i}", 12, new PdfPoint(50, 700), f);
        return b.Build();
    }

    private static int CuantasVeces(string texto, string buscado)
    {
        var n = 0;
        for (var i = texto.IndexOf(buscado, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(buscado, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
