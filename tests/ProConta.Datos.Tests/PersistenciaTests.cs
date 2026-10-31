using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Resultados;
using ProConta.Pruebas.Access;

namespace ProConta.Datos.Tests;

[Trait("Categoria", "Access")]
public sealed class PersistenciaTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly IBaseDatos _bd;

    public PersistenciaTests()
    {
        _bd = _copia.Bd();
        // Tabla propia de la prueba, solo en la copia temporal.
        _bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("CREATE TABLE PruebaPersistencia (Id COUNTER CONSTRAINT PK_PruebaPersistencia PRIMARY KEY, Codigo TEXT(10) NOT NULL, Importe CURRENCY, Fecha DATETIME, Activo YESNO, Nota LONGTEXT, Version LONG NOT NULL)");
            await s.EjecutarAsync("CREATE UNIQUE INDEX UX_PruebaPersistencia_Codigo ON PruebaPersistencia (Codigo)");
            return true;
        }).GetAwaiter().GetResult();
    }

    public void Dispose() => _copia.Dispose();

    private Task<int> ContarAsync() =>
        _bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PruebaPersistencia")));

    [Fact]
    public async Task TransaccionRevertidaPorExcepcionNoDejaFilas()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _bd.EnTransaccionAsync<bool>(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "A1", 1);
            await s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "A2", 1);
            throw new InvalidOperationException("corte simulado");
        }));
        Assert.Equal(0, await ContarAsync());
    }

    [Fact]
    public async Task TransaccionConResultadoFallidoSeRevierte()
    {
        var resultado = await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "B1", 1);
            return Resultado.Fallo(new Error("Prueba", "falla la validación"));
        });
        Assert.False(resultado.EsCorrecto);
        Assert.Equal(0, await ContarAsync());
    }

    [Fact]
    public async Task TransaccionCorrectaSeConfirma()
    {
        await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "C1", 1);
            return Resultado.Correcto();
        });
        Assert.Equal(1, await ContarAsync());
    }

    [Fact]
    public async Task ConflictoDeVersionSeDetecta()
    {
        var id = await _bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "D1", 1);
            return await s.UltimoIdAsync();
        });
        const string actualizar = "UPDATE PruebaPersistencia SET Codigo = ?, Version = Version + 1 WHERE Id = ? AND Version = ?";

        // Dos operadores leyeron la versión 1; el primero guarda y el segundo debe recibir conflicto.
        await _bd.EjecutarAsync(async s => { await s.ActualizarConVersionAsync(actualizar, "Prueba D1", "D2", id, 1); return true; });
        var conflicto = await Assert.ThrowsAsync<ConflictoConcurrenciaException>(() =>
            _bd.EjecutarAsync(async s => { await s.ActualizarConVersionAsync(actualizar, "Prueba D1", "D3", id, 1); return true; }));
        Assert.Equal("Prueba D1", conflicto.Error.RegistroAfectado);
        Assert.Equal("D2", await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT Codigo FROM PruebaPersistencia WHERE Id = ?", id)));
    }

    [Fact]
    public async Task TablaAusenteDaErrorExplicito()
    {
        var ex = await Assert.ThrowsAsync<TablaAusenteException>(() =>
            _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT * FROM TablaQueNoExiste", f => f.Texto("X"))));
        Assert.Equal("Datos.TablaAusente", ex.Error.Codigo);
        Assert.Contains("ActualizadorEsquema", ex.Error.AccionCorrectora);
    }

    [Fact]
    public async Task DuplicadoDaErrorDeUnicidad()
    {
        await _bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "E1", 1));
        await Assert.ThrowsAsync<UnicidadException>(() =>
            _bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "E1", 1)));
    }

    [Fact]
    public async Task ParametrosPosicionalesConservanTipos()
    {
        var fecha = new DateTime(2026, 10, 31, 18, 30, 15);
        var nota = new string('x', 300);
        await _bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO PruebaPersistencia (Codigo, Importe, Fecha, Activo, Nota, Version) VALUES (?, ?, ?, ?, ?, ?)",
            "F1", 1234.56m, fecha, true, nota, 1));
        await _bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO PruebaPersistencia (Codigo, Importe, Fecha, Activo, Nota, Version) VALUES (?, ?, ?, ?, ?, ?)",
            "F2", null, null, false, null, 1));

        var filas = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT * FROM PruebaPersistencia WHERE Codigo IN (?, ?) ORDER BY Codigo",
            f => (Importe: f.DecimalNulo("Importe"), Fecha: f.FechaNula("Fecha"), Activo: f.Logico("Activo"), Nota: f.Texto("Nota")),
            "F1", "F2"));
        Assert.Equal(1234.56m, filas[0].Importe);
        Assert.Equal(fecha, filas[0].Fecha);
        Assert.True(filas[0].Activo);
        Assert.Equal(nota, filas[0].Nota);
        Assert.Null(filas[1].Importe);
        Assert.Null(filas[1].Fecha);
        Assert.False(filas[1].Activo);
    }

    [Fact]
    public async Task NumeracionSoloDentroDeTransaccion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _bd.EjecutarAsync(s => s.SiguienteNumeroAsync("PruebaPersistencia", "Version")));
        var numero = await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PruebaPersistencia (Codigo, Version) VALUES (?, ?)", "G1", 7);
            return await s.SiguienteNumeroAsync("PruebaPersistencia", "Version", "Codigo LIKE ?", "G%");
        });
        Assert.Equal(8, numero);
    }

    [Fact]
    public async Task LeeLaEstructuraConAutonumericosIndicesYVinculadas()
    {
        var esquema = await _bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        var tabla = esquema.Tabla("PruebaPersistencia")!;
        Assert.Equal("COUNTER", tabla.Columna("Id")!.Tipo);
        Assert.Equal("TEXT(10)", tabla.Columna("Codigo")!.Tipo);
        Assert.Equal("CURRENCY", tabla.Columna("Importe")!.Tipo);
        Assert.Equal("LONGTEXT", tabla.Columna("Nota")!.Tipo);
        Assert.Contains(tabla.Indices, i => i.Nombre == "UX_PruebaPersistencia_Codigo" && i.EsUnico);
        Assert.True(esquema.Tabla("FormasPago")!.EsVinculada);
        Assert.True(esquema.ExisteColumna("Sociedades", "CIF"));
    }

    [Fact]
    public async Task BdInexistenteDaErrorExplicito()
    {
        var bd = new BaseDatosAccess(Path.Combine(BdPruebas.Carpeta, "no-existe.accdb"));
        var ex = await Assert.ThrowsAsync<BaseDatosNoDisponibleException>(() => bd.EjecutarAsync(s => s.EscalarAsync("SELECT 1")));
        Assert.Equal("Datos.BDNoEncontrada", ex.Error.Codigo);
    }

    [Fact]
    public async Task LaCopiaDePruebasNoTieneDatosDeNegocio()
    {
        var cuentas = await _bd.EjecutarAsync(async s => new[]
        {
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Terceros")),
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Asientos")),
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Sociedades")),
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM OperadoresSeguridad")),
        });
        Assert.All(cuentas, n => Assert.Equal(0, n));
    }
}
