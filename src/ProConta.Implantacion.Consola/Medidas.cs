using System.Collections.Concurrent;
using System.Diagnostics;
using ProConta.Datos.Persistencia;

namespace ProConta.Implantacion.Consola;

/// <summary>
/// Medición de la BD para diagnosticar el rendimiento (--medir): tiempo y número de ejecuciones de cada sentencia SQL,
/// de abrir cada sesión y de confirmar cada transacción. Envuelve la BD real; no cambia lo que se hace.
/// </summary>
internal sealed class BaseDatosMedida(IBaseDatos bd) : IBaseDatos
{
    private readonly ConcurrentDictionary<string, (long Ticks, int Veces)> _medidas = new();
    public string Ruta => bd.Ruta;

    internal void Anotar(string clave, long ticks) => _medidas.AddOrUpdate(clave, (ticks, 1), (_, v) => (v.Ticks + ticks, v.Veces + 1));

    public async Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        var reloj = Stopwatch.StartNew();
        var dentro = 0L;
        try
        {
            return await bd.EjecutarAsync(async s =>
            {
                Anotar("(abrir sesión)", reloj.ElapsedTicks);
                var inicio = reloj.ElapsedTicks;
                try { return await trabajo(new SesionMedida(s, this)); }
                finally { dentro = reloj.ElapsedTicks - inicio; }
            }, cancelacion);
        }
        finally { Anotar("(cerrar sesión)", reloj.ElapsedTicks - dentro); }
    }

    public async Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        var reloj = Stopwatch.StartNew();
        var dentro = 0L;
        try
        {
            return await bd.EnTransaccionAsync(async s =>
            {
                Anotar("(abrir transacción)", reloj.ElapsedTicks);
                var inicio = reloj.ElapsedTicks;
                try { return await trabajo(new SesionMedida(s, this)); }
                finally { dentro = reloj.ElapsedTicks - inicio; }
            }, cancelacion);
        }
        finally { Anotar("(abrir + confirmar + cerrar transacción)", reloj.ElapsedTicks - dentro); }
    }

    /// <summary>Las 25 sentencias con más tiempo total.</summary>
    public string Informe(TimeSpan total)
    {
        var filas = _medidas.OrderByDescending(m => m.Value.Ticks).Take(25)
            .Select(m => $"{m.Value.Ticks * 1000.0 / Stopwatch.Frequency,12:0} ms {m.Value.Veces,8} veces {m.Value.Ticks * 1000.0 / Stopwatch.Frequency / m.Value.Veces,8:0.00} ms/vez  {Recortar(m.Key)}");
        var bdTotal = _medidas.Sum(m => m.Value.Ticks) * 1000.0 / Stopwatch.Frequency;
        return $"Tiempo total {total.TotalSeconds:0} s; en la BD (sumado) {bdTotal / 1000:0} s; fuera de la BD ≈ {total.TotalSeconds - bdTotal / 1000:0} s\n" + string.Join("\n", filas);
    }

    private static string Recortar(string sql) => sql.Length > 150 ? sql[..150] + "…" : sql;
}

internal sealed class SesionMedida(ISesionDatos s, BaseDatosMedida medida) : ISesionDatos
{
    public bool EnTransaccion => s.EnTransaccion;

    private async Task<T> Medir<T>(string sql, Func<Task<T>> accion)
    {
        var reloj = Stopwatch.StartNew();
        try { return await accion(); }
        finally { medida.Anotar(sql, reloj.ElapsedTicks); }
    }

    public Task<IReadOnlyList<T>> ConsultarAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] parametros) => Medir(sql, () => s.ConsultarAsync(sql, mapear, parametros));
    public Task<T?> ConsultarPrimeroAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] parametros) => Medir(sql, () => s.ConsultarPrimeroAsync(sql, mapear, parametros));
    public Task<object?> EscalarAsync(string sql, params object?[] parametros) => Medir(sql, () => s.EscalarAsync(sql, parametros));
    public Task<int> EjecutarAsync(string sql, params object?[] parametros) => Medir(sql, () => s.EjecutarAsync(sql, parametros));
    public Task ActualizarConVersionAsync(string sql, string registroAfectado, params object?[] parametros) =>
        Medir(sql, async () => { await s.ActualizarConVersionAsync(sql, registroAfectado, parametros); return true; });
    public Task<int> UltimoIdAsync() => Medir("SELECT @@IDENTITY", s.UltimoIdAsync);
    public Task<int> SiguienteNumeroAsync(string tabla, string campo, string? filtro = null, params object?[] parametros) =>
        Medir($"SiguienteNumero {tabla}.{campo}", () => s.SiguienteNumeroAsync(tabla, campo, filtro, parametros));
    public EsquemaFisico LeerEsquema() => s.LeerEsquema();
}
