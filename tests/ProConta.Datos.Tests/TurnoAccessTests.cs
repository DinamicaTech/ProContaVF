using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Pruebas.Access;

namespace ProConta.Datos.Tests;

/// <summary>
/// Todo el trabajo con ACE va por turno dentro del proceso (plataforma/registro-errores 2026-11-28): el ACE de Office cierra el
/// programa con una violación de acceso si dos hilos consultan a la vez, aunque cada uno tenga su conexión.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class TurnoAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly IBaseDatos _bd;
    private int _dentro;
    private int _maximo;

    public TurnoAccessTests() => _bd = _copia.Bd();

    public void Dispose() => _copia.Dispose();

    private void Entrar()
    {
        var n = Interlocked.Increment(ref _dentro);
        int m;
        while ((m = Volatile.Read(ref _maximo)) < n && Interlocked.CompareExchange(ref _maximo, n, m) != m) { }
    }

    [Fact]
    public async Task Muchas_lecturas_en_paralelo_incluida_la_estructura_van_una_detras_de_otra_sin_caerse()
    {
        // Como Inicio: varias tareas en hilos del pool leyendo la estructura (los autonuméricos) y consultando a la vez.
        var tareas = Enumerable.Range(0, 24).Select(i => Task.Run(() => _bd.EjecutarAsync(async s =>
        {
            Entrar();
            try
            {
                var esquema = s.LeerEsquema();
                Assert.NotEmpty(esquema.Tablas);
                var n = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Sociedades"));
                await Task.Delay(i % 3);
                return n;
            }
            finally { Interlocked.Decrement(ref _dentro); }
        }))).ToList();

        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromMinutes(3));

        Assert.Equal(24, resultados.Length);
        Assert.Equal(1, _maximo);
    }

    [Fact]
    public async Task Una_sesion_abierta_dentro_de_otra_no_se_queda_esperando()
    {
        var n = await _bd.EjecutarAsync(async _ =>
            await _bd.EnTransaccionAsync(async s2 => Convert.ToInt32(await s2.EscalarAsync("SELECT COUNT(*) FROM Sociedades"))))
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(n >= 0);
    }

    [Fact]
    public async Task Una_sesion_que_falla_suelta_el_turno()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _bd.EjecutarAsync<int>(_ => throw new InvalidOperationException("corte simulado")));

        var n = await _bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Sociedades")))
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(n >= 0);
    }
}

/// <summary>La memoria de la estructura en todo el proceso es estado global: estas pruebas no corren a la vez que las demás.</summary>
[CollectionDefinition(nameof(MemoriaEsquemaProcesoTests), DisableParallelization = true)]
public sealed class ColeccionMemoriaEsquemaProceso;

[Collection(nameof(MemoriaEsquemaProcesoTests))]
public sealed class MemoriaEsquemaProcesoTests : IDisposable
{
    private static readonly EsquemaFisico Vacio = new([]);

    public void Dispose() => MemoriaEsquema.DejarDeRecordar();

    [Fact]
    public void Encendida_lee_la_estructura_una_sola_vez_por_fichero()
    {
        var lecturas = 0;
        EsquemaFisico Leer() { lecturas++; return Vacio; }

        MemoriaEsquema.RecordarEnElProceso();
        MemoriaEsquema.Obtener(@"C:\a.accdb", Leer);
        MemoriaEsquema.Obtener(@"c:\A.accdb", Leer);
        MemoriaEsquema.Obtener(@"C:\b.accdb", Leer);

        Assert.Equal(2, lecturas);
    }

    [Fact]
    public void Apagada_o_olvidada_vuelve_a_leer()
    {
        var lecturas = 0;
        EsquemaFisico Leer() { lecturas++; return Vacio; }

        MemoriaEsquema.Obtener("x", Leer);
        MemoriaEsquema.Obtener("x", Leer);
        Assert.Equal(2, lecturas);

        MemoriaEsquema.RecordarEnElProceso();
        MemoriaEsquema.Obtener("x", Leer);
        MemoriaEsquema.Olvidar();
        MemoriaEsquema.Obtener("x", Leer);
        Assert.Equal(4, lecturas);
    }
}
