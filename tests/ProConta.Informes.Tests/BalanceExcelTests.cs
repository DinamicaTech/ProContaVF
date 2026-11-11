using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using ProConta.Contabilidad.Balances;
using ProConta.Informes.Excel;

namespace ProConta.Informes.Tests;

public sealed class BalanceExcelTests : IDisposable
{
    private static readonly XNamespace X = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly string _carpeta = Directory.CreateTempSubdirectory("ProContaVF-balances-").FullName;

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("PROCONTA_CONSERVAR_TEMP") is { Length: > 0 }) return;
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private static readonly ModeloBalance Situacion = new("SIT", "Balance de situación PGC Pymes 2008", TipoBalance.Situacion,
    [
        new("572", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "VI. Efectivo y otros activos líquidos equivalentes", 217, null),
        new("430", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "II. Deudores comerciales y otras cuentas a cobrar.", 203, "1. Clientes por ventas y Prestaciones de servicios"),
        new("21", 1, 2, "ACTIVO", 0, "A. ACTIVO NO CORRIENTE", 101, "II. Inmovilizado material.", 103, null),
        new("100", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 301, "I. Capital"),
        new("7", -1, 1, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 308, "VII. Resultado del ejercicio"),
    ]);

    private static ApunteBalance A(int asiento, int mes, string cuenta, decimal debe, decimal haber, int orden = 1, int? centro = null) =>
        new(new DateTime(2025, mes, 3), asiento, 0, orden, cuenta, debe, haber, $"Concepto {asiento}", centro);

    private static ResultadoBalance Balance(IReadOnlyList<ApunteBalance>? apuntes = null) => Clasificador.Clasificar(Situacion, "Sociedad sintética", 2025,
        apuntes ??
        [
            A(1, 1, "5720000", 1000m, 0m), A(1, 1, "1000000", 0m, 1000m, 2),
            A(2, 2, "2160000", 400m, 0m), A(2, 2, "5720000", 0m, 400m, 2),
            A(3, 3, "4300001", 300m, 0m, centro: 1), A(3, 3, "7000000", 0m, 300m, 2, centro: 1),
            A(4, 3, "6660000", 5m, 0m), A(4, 3, "5720000", 0m, 5m, 2),
        ],
        new Dictionary<string, string> { ["572"] = "BANCOS", ["5720000"] = "Banco sintético" },
        new Dictionary<int, CentroBalance> { [1] = new("INMUEBLES", "3/304", "Local sintético") });

    private string Exportar(ResultadoBalance balance)
    {
        var ruta = Path.Combine(_carpeta, ExportadorBalanceExcel.NombreFichero(balance.Modelo.Tipo, balance.Ejercicio, new DateTime(2026, 11, 11, 15, 30, 0)));
        ExportadorBalanceExcel.Exportar(balance, ruta);
        return ruta;
    }

    [Fact]
    public void ElLibroTieneInformeDatosYSinClasificarConLasColumnasHistoricas()
    {
        var ruta = Exportar(Balance());
        Assert.EndsWith("BalanceSituacion_2025_20261111-153000.xlsx", ruta);
        using var libro = new XLWorkbook(ruta);
        Assert.Equal(["Situacion", "DatosSituacion", "SinClasificar"], libro.Worksheets.Select(w => w.Name));
        var datos = libro.Worksheet("DatosSituacion");
        var tabla = datos.Table(ExportadorBalanceExcel.NombreTabla);
        Assert.Equal(ExportadorBalanceExcel.Columnas, tabla.Fields.Select(f => f.Name));
        Assert.Equal(7, tabla.DataRange.RowCount());
        // Importes como número y en euros, la fecha como fecha: se pueden sumar y filtrar.
        Assert.Equal(1295m, tabla.DataRange.Rows().Where(r => r.Field("Bloque").GetString() == "ACTIVO").Sum(r => r.Field("Importe").GetValue<decimal>()));
        Assert.Equal(new DateTime(2025, 2, 3), tabla.DataRange.Rows().First().Field("Fecha").GetDateTime());
        Assert.Equal(2, tabla.DataRange.Rows().First().Field("Mes").GetValue<int>());
        var cliente = tabla.DataRange.Rows().Single(r => r.Field("CuentaContable").GetValue<long>() == 4300001);
        Assert.Equal(("INMUEBLES", "3/304", "Local sintético", "ACTIVO"),
            (cliente.Field("CentroDeCoste").GetString(), cliente.Field("SubDepartamento").GetString(), cliente.Field("NombreDepartamento").GetString(), cliente.Field("Bloque").GetString()));
        Assert.Equal("6660000", libro.Worksheet("SinClasificar").Cell(5, 1).GetString());
        Assert.Equal(5m, libro.Worksheet("SinClasificar").Cell(5, 6).GetValue<decimal>());
    }

    [Fact]
    public void LaTablaDinamicaTieneFilasColumnasFiltrosYValorComoElProContaAnterior()
    {
        var ruta = Exportar(Balance());
        var pivot = Parte(ruta, "pivotTables/pivotTable");
        var cache = Parte(ruta, "pivotCacheDefinition");
        var nombres = cache.Descendants(X + "cacheField").Select(c => (string)c.Attribute("name")!).ToList();
        string Campo(XElement f) => nombres[int.Parse((string)f.Attribute(f.Name == X + "pageField" ? "fld" : "x")!)];

        Assert.Equal("TablaDinamicaSituacion", (string)pivot.Root!.Attribute("name")!);
        Assert.Equal(["Bloque", "Apartado", "Titulo", "Cuenta", "SubCuenta"], pivot.Descendants(X + "rowFields").Elements(X + "field").Select(Campo));
        Assert.Equal(["Mes"], pivot.Descendants(X + "colFields").Elements(X + "field").Select(Campo));
        Assert.Equal(["CentroDeCoste", "NombreDepartamento"], pivot.Descendants(X + "pageFields").Elements(X + "pageField").Select(Campo));
        var valor = pivot.Descendants(X + "dataField").Single();
        Assert.Equal(("Suma de Importe", "Importe"), ((string)valor.Attribute("name")!, nombres[int.Parse((string)valor.Attribute("fld")!)]));
    }

    [Fact]
    public void LosApartadosYTitulosSalenEnElOrdenOficialNoAlfabetico()
    {
        var ruta = Exportar(Balance());
        var pivot = Parte(ruta, "pivotTables/pivotTable");
        var cache = Parte(ruta, "pivotCacheDefinition");
        var campos = cache.Descendants(X + "cacheField").ToList();
        var indice = campos.FindIndex(c => (string)c.Attribute("name") == "Titulo");
        var compartidos = campos[indice].Element(X + "sharedItems")!.Elements().Select(e => (string)e.Attribute("v")!).ToList();
        var campoPivot = pivot.Descendants(X + "pivotField").ElementAt(indice);
        var orden = campoPivot.Element(X + "items")!.Elements(X + "item").Where(i => i.Attribute("x") is not null)
            .Select(i => compartidos[int.Parse((string)i.Attribute("x")!)]).ToList();
        Assert.Equal(["II. Inmovilizado material.", "II. Deudores comerciales y otras cuentas a cobrar.", "VI. Efectivo y otros activos líquidos equivalentes", "A.1 Fondos propios"], orden);
    }

    [Fact]
    public void SinApuntesNoGeneraUnLibroVacioYExplicaPorQue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Exportar(Balance([])));
        Assert.Contains("no tiene apuntes contabilizados", ex.Message);
        Assert.Contains("ejercicio correcto", ex.Message);
    }

    private static XDocument Parte(string ruta, string parte)
    {
        using var zip = ZipFile.OpenRead(ruta);
        // Por su nombre, no por su ruta: cada generador de OOXML coloca las partes de la tabla dinámica a su manera (las une su .rels).
        using var flujo = zip.Entries.Single(e => e.FullName.Contains(parte, StringComparison.Ordinal) && !e.FullName.Contains("_rels", StringComparison.Ordinal)).Open();
        return XDocument.Load(flujo);
    }
}
