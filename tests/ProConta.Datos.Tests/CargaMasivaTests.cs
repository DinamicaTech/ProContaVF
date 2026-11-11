using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Pruebas.Access;

namespace ProConta.Datos.Tests;

/// <summary>Formato de los ficheros de carga masiva (sin BD).</summary>
public sealed class CargaMasivaFicheroTests
{
    [Fact]
    public void EscribeFicheroUtf8SinCabeceraYSuEsquema()
    {
        using var carga = new CargaMasivaAccess();
        var origen = carga.Preparar([new("Codigo", TipoCarga.Texto), new("N", TipoCarga.Entero), new("Nota", TipoCarga.TextoLargo)],
            [["Año\tcon tabulador", 7, "línea 1\r\nlínea 2"], [null, null, "€"]]);
        Assert.Equal($"[Text;DATABASE={carga.Carpeta};HDR=No].[c1#txt]", origen);
        var bytes = File.ReadAllBytes(Path.Combine(carga.Carpeta, "c1.txt"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("Año con tabulador\t7\tlínea 1 línea 2\r\n\t\t€\r\n", Encoding.UTF8.GetString(bytes));
        var esquema = File.ReadAllText(Path.Combine(carga.Carpeta, "schema.ini"));
        Assert.Contains("[c1.txt]", esquema);
        Assert.Contains("Col1=Codigo Text Width 255", esquema);
        Assert.Contains("Col2=N Long", esquema);
        Assert.Contains("Col3=Nota LongChar", esquema);
        Assert.Contains("CharacterSet=65001", esquema);
    }

    [Fact]
    public void ValoresIndependientesDeLaCulturaYLiterales()
    {
        Assert.Equal("1234.50", CargaMasivaAccess.Valor(1234.5m));
        Assert.Equal("-0.01", CargaMasivaAccess.Valor(-0.01m));
        Assert.Equal("2024-02-29 13:05:09", CargaMasivaAccess.Valor(new DateTime(2024, 2, 29, 13, 5, 9)));
        Assert.Equal("", CargaMasivaAccess.Valor(null));
        Assert.Equal("1", CargaMasivaAccess.Valor(true));
        Assert.Equal("'O''Brien'", CargaMasivaAccess.Literal("O'Brien"));
        Assert.Equal("#02/29/2024 13:05:09#", CargaMasivaAccess.Literal(new DateTime(2024, 2, 29, 13, 5, 9)));
    }

    [Fact]
    public void BorraSuCarpetaAlTerminar()
    {
        string carpeta;
        using (var carga = new CargaMasivaAccess())
        {
            carpeta = carga.Carpeta;
            carga.Preparar([new("N", TipoCarga.Entero)], [[1]]);
        }
        Assert.False(Directory.Exists(carpeta));
    }
}

/// <summary>Carga masiva contra Access: tipos, nulos, acentos y tandas.</summary>
[Trait("Categoria", "Access")]
public sealed class CargaMasivaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly IBaseDatos _bd;

    public CargaMasivaAccessTests()
    {
        _bd = _copia.Bd();
        _bd.EjecutarAsync(s => s.EjecutarAsync(
            "CREATE TABLE PruebaCarga (Id COUNTER CONSTRAINT PK_PruebaCarga PRIMARY KEY, Codigo TEXT(20), N LONG, Importe CURRENCY, Fecha DATETIME, Activo YESNO, Nota LONGTEXT)"))
            .GetAwaiter().GetResult();
    }

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task InsertaEnTandasConTiposNulosYAcentos()
    {
        using var carga = new CargaMasivaAccess();
        var filas = Enumerable.Range(1, 2500).Select(i => new object?[]
        {
            i == 1 ? "Año €" : $"C{i}", i == 2 ? null : i, i == 3 ? null : 1234567.89m + i / 100m, new DateTime(2024, 12, 31, 23, 59, 58), i % 2, new string('x', 400),
        });
        var insertadas = await carga.InsertarAsync(_bd, "INSERT INTO PruebaCarga (Codigo, N, Importe, Fecha, Activo, Nota)",
            $"t.Codigo, t.N, {CargaMasivaAccess.Importe("t.Importe")}, {CargaMasivaAccess.Fecha("t.Fecha")}, IIF(t.Activo = 1, True, False), t.Nota",
            [new("Codigo", TipoCarga.Texto), new("N", TipoCarga.Entero), new("Importe", TipoCarga.Texto), new("Fecha", TipoCarga.Texto), new("Activo", TipoCarga.Entero),
                new("Nota", TipoCarga.TextoLargo)], filas);
        Assert.Equal(2500, insertadas);
        var leidas = await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Codigo, N, Importe, Fecha, Activo, Nota FROM PruebaCarga ORDER BY Id",
            f => (Codigo: f.Texto("Codigo"), N: f.EnteroNulo("N"), Importe: f.DecimalNulo("Importe"), Fecha: f.Fecha("Fecha"), Activo: f.Logico("Activo"), Nota: f.Texto("Nota"))));
        Assert.Equal(2500, leidas.Count);
        Assert.Equal("Año €", leidas[0].Codigo);
        Assert.Equal(1234567.90m, leidas[0].Importe);
        Assert.Null(leidas[1].N);
        Assert.Null(leidas[2].Importe);
        Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 58), leidas[0].Fecha);
        Assert.True(leidas[0].Activo);
        Assert.False(leidas[1].Activo);
        Assert.Equal(400, leidas[0].Nota!.Length);
        Assert.Equal(1234592.89m, leidas[^1].Importe);
    }
}
