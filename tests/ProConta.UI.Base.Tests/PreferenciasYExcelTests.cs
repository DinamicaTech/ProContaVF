using System.IO;
using ClosedXML.Excel;
using ProConta.UI.Base.Preferencias;
using ProConta.UI.Base.Tablas;

namespace ProConta.UI.Base.Tests;

public class PreferenciasTests
{
    [Fact]
    public void Guardar_y_leer_por_operador()
    {
        var dir = Path.Combine(Path.GetTempPath(), "proconta-pruebas-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = new PreferenciasJson("op1", dir);
            p.Guardar("tabla.mayor", new ConfiguracionColumnas { Orden = new() { "b", "a" } });
            var p2 = new PreferenciasJson("op1", dir);
            Assert.Equal(new[] { "b", "a" }, p2.Leer<ConfiguracionColumnas>("tabla.mayor")!.Orden);
            Assert.Null(new PreferenciasJson("op2", dir).Leer<ConfiguracionColumnas>("tabla.mayor"));
            p2.Borrar("tabla.mayor");
            Assert.Null(new PreferenciasJson("op1", dir).Leer<ConfiguracionColumnas>("tabla.mayor"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Fichero_danado_vuelve_a_fabrica()
    {
        var dir = Path.Combine(Path.GetTempPath(), "proconta-pruebas-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "op.json"), "{ esto no es json");
            var p = new PreferenciasJson("op", dir);
            Assert.Null(p.Leer<ConfiguracionColumnas>("x"));
            p.Guardar("x", 1);
            Assert.Equal(1, new PreferenciasJson("op", dir).Leer<int>("x"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class ExportadorExcelTests
{
    private sealed record Fila(DateOnly Fecha, string Concepto, decimal Debe);

    [Fact]
    public void Exporta_todas_las_filas_con_las_columnas_en_su_orden_y_tipos()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "proconta-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            var filas = Enumerable.Range(1, 1000).Select(i => (object)new Fila(new DateOnly(2026, 1, 1).AddDays(i % 300), "Apunte " + i, i * 1.5m)).ToList();
            var cols = new[]
            {
                new ColumnaExportable("Debe", f => ((Fila)f).Debe),
                new ColumnaExportable("Fecha", f => ((Fila)f).Fecha),
                new ColumnaExportable("Concepto", f => ((Fila)f).Concepto)
            };
            var n = ExportadorExcel.Exportar(ruta, cols, filas, "Mayor", "Mayor de prueba");
            Assert.Equal(1000, n);
            using var wb = new XLWorkbook(ruta);
            var ws = wb.Worksheet(1);
            Assert.Equal("Mayor de prueba", ws.Cell(1, 1).GetString());
            Assert.Equal("Debe", ws.Cell(3, 1).GetString());
            Assert.Equal("Fecha", ws.Cell(3, 2).GetString());
            Assert.Equal(1.5, ws.Cell(4, 1).GetDouble());
            Assert.True(ws.Cell(4, 2).DataType == XLDataType.DateTime);
            Assert.Equal("Apunte 1000", ws.Cell(1003, 3).GetString());
        }
        finally { if (File.Exists(ruta)) File.Delete(ruta); }
    }

    [Fact]
    public void Sin_columnas_visibles_da_un_error_claro() =>
        Assert.Throws<ArgumentException>(() => ExportadorExcel.Exportar("x.xlsx", Array.Empty<ColumnaExportable>(), Array.Empty<object>()));
}
