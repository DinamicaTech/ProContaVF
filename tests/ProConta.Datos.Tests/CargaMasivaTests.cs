using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Pruebas.Access;

namespace ProConta.Datos.Tests;

/// <summary>Carga masiva contra Access: tipos, nulos, acentos, autonuméricos y tandas.</summary>
[Trait("Categoria", "Access")]
public sealed class CargaMasivaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly IBaseDatos _bd;

    public CargaMasivaAccessTests()
    {
        _bd = _copia.Bd();
        _bd.EjecutarAsync(s => s.EjecutarAsync(
            "CREATE TABLE PruebaCarga (Id COUNTER CONSTRAINT PK_PruebaCarga PRIMARY KEY, Codigo TEXT(20), N LONG, Orden SHORT, Importe CURRENCY, Fecha DATETIME, Activo YESNO, Nota LONGTEXT)"))
            .GetAwaiter().GetResult();
    }

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task InsertaEnTandasConTiposNulosYAcentos()
    {
        IReadOnlyList<int> ids;
        using (var carga = new CargaMasivaAccess(_bd.Ruta))
            ids = await carga.InsertarAsync("PruebaCarga", ["Codigo", "N", "Orden", "Importe", "Fecha", "Activo", "Nota"],
                Enumerable.Range(1, 12000).Select(i => new object?[]
                {
                    i == 1 ? "Año € «x»" : $"C{i}", i == 2 ? null : i, (short)(i % 100), i == 3 ? null : 1234567.89m + i / 100m, new DateTime(2024, 12, 31, 23, 59, 58),
                    i % 2 == 1, new string('x', 400),
                }), "Id");
        Assert.Equal(12000, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // Lo grabado por DAO lo ve enseguida una conexión OLE DB nueva.
        var leidas = await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Id, Codigo, N, Importe, Fecha, Activo, Nota FROM PruebaCarga ORDER BY Id",
            f => (Id: f.Entero("Id"), Codigo: f.Texto("Codigo"), N: f.EnteroNulo("N"), Importe: f.DecimalNulo("Importe"), Fecha: f.Fecha("Fecha"), Activo: f.Logico("Activo"), Nota: f.Texto("Nota"))));
        Assert.Equal(12000, leidas.Count);
        Assert.Equal(ids, leidas.Select(l => l.Id));
        Assert.Equal("Año € «x»", leidas[0].Codigo);
        Assert.Equal(1234567.90m, leidas[0].Importe);
        Assert.Null(leidas[1].N);
        Assert.Null(leidas[2].Importe);
        Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 58), leidas[0].Fecha);
        Assert.True(leidas[0].Activo);
        Assert.False(leidas[1].Activo);
        Assert.Equal(400, leidas[0].Nota!.Length);
        Assert.Equal(1234687.89m, leidas[^1].Importe);
    }

    [Fact]
    public async Task UnaTandaQueFallaNoDejaFilas()
    {
        using (var carga = new CargaMasivaAccess(_bd.Ruta))
            await Assert.ThrowsAnyAsync<Exception>(() => carga.InsertarAsync("PruebaCarga", ["Codigo"],
                [["bien"], [new string('x', 50)]]));
        Assert.Equal(0, await _bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PruebaCarga"))));
    }
}
