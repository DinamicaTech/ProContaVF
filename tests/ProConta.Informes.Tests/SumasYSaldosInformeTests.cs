using ClosedXML.Excel;
using ProConta.Contabilidad.Balances;

namespace ProConta.Informes.Tests;

/// <summary>Salidas del balance de sumas y saldos (contabilidad/balances/sumas-y-saldos): HTML del PDF y libro Excel.</summary>
public sealed class SumasYSaldosInformeTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("ProContaVF-sumas-saldos-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private static ResultadoSumasYSaldos Resultado() => CalculadoraSumasYSaldos.Calcular("Sociedad <sintética> & Cía", 2026,
        new ParametrosSumasYSaldos(new(2026, 1, 1), new(2026, 6, 30), [NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta]),
        [new("5720001", 1000m, 0m), new("1000000", 0m, 1000m), new("4300001", 1210m, 0m), new("7000001", 0m, 1000m), new("4770021", 0m, 210m)],
        new Dictionary<string, string> { ["572"] = "BANCOS", ["5720001"] = "Banco", ["430"] = "CLIENTES" }, new DateTime(2026, 11, 18, 19, 30, 0));

    [Fact]
    public void El_html_lleva_cabecera_filas_en_negrita_por_nivel_y_totales()
    {
        var html = InformeSumasYSaldos.Html(Resultado());
        Assert.Contains("Sociedad &lt;sintética&gt; &amp; Cía", html);
        Assert.Contains("del 01/01/2026 al 30/06/2026 · cuentas y subcuentas · sin el asiento de cierre", html);
        Assert.Contains("<tr class=\"v3\"><td class=\"m\">430</td><td>CLIENTES</td>", html);
        Assert.Contains("Totales (subcuentas: 5)", html);
        Assert.Contains("<td class=\"n\">2.210,00</td><td class=\"n\">2.210,00</td><td class=\"n\">2.210,00</td><td class=\"n\">2.210,00</td></tr></tbody>", html);
    }

    [Fact]
    public void El_excel_tiene_la_tabla_con_filtros_y_los_totales_con_formulas()
    {
        var ruta = Path.Combine(_carpeta, "s.xlsx");
        InformeSumasYSaldos.Excel(Resultado(), ruta);
        using var libro = new XLWorkbook(ruta);
        var hoja = libro.Worksheet("SumasYSaldos");
        var tabla = hoja.Table(InformeSumasYSaldos.NombreTabla);
        Assert.Equal(InformeSumasYSaldos.Columnas, tabla.Fields.Select(f => f.Name));
        Assert.Equal(10, tabla.DataRange.RowCount()); // 5 cuentas + 5 subcuentas
        Assert.Equal("430", tabla.DataRange.Row(3).Cell(1).GetString());
        Assert.Equal("Cuenta", tabla.DataRange.Row(3).Cell(3).GetString());
        Assert.True(tabla.DataRange.Row(3).Cell(1).Style.Font.Bold);
        var tot = hoja.Row(4 + 10 + 2);
        Assert.Equal("Totales (subcuentas)", tot.Cell(2).GetString());
        Assert.Contains("SUMIFS(D5:D14", tot.Cell(4).FormulaA1);
        Assert.Contains("\"Subcuenta\"", tot.Cell(4).FormulaA1);
    }
}
