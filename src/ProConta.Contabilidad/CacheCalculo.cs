using System.Collections.Concurrent;

namespace ProConta.Contabilidad;

/// <summary>
/// Memoria de cálculos de solo lectura mientras dura una carga (contabilidad/modelos-tributarios 2026-11-18): la pantalla de modelos
/// tributarios abre un ámbito con <see cref="Abrir"/> y, dentro de él, las lecturas caras que comparten varios modelos (las líneas de IVA
/// del ejercicio, las retenciones, la base del pago fraccionado…) se calculan una sola vez. Fuera de un ámbito no guarda nada, así que
/// el resto del programa calcula como siempre. El ámbito sigue al flujo asíncrono (AsyncLocal) y se cierra al salir del using.
/// </summary>
public static class CacheCalculo
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, object>?> Actual = new();

    /// <summary>Abre un ámbito de memoria; al cerrarlo (Dispose) se olvida todo.</summary>
    public static IDisposable Abrir()
    {
        var anterior = Actual.Value;
        Actual.Value = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        // También la estructura de la BD, que cada servicio consulta para saber si existen sus columnas.
        return new Cierre(anterior, ProConta.Datos.Persistencia.MemoriaEsquema.Abrir());
    }

    /// <summary>Si hay un ámbito abierto en este flujo.</summary>
    public static bool Activa => Actual.Value is not null;

    /// <summary>El resultado de <paramref name="calcular"/> para <paramref name="clave"/>: el ya calculado en el ámbito o, si no, lo calcula (sin ámbito, siempre lo calcula).</summary>
    public static Task<T> ObtenerAsync<T>(string clave, Func<Task<T>> calcular)
    {
        var d = Actual.Value;
        if (d is null) return calcular();
        var t = (Task<T>)d.GetOrAdd(clave, _ => calcular());
        // Un fallo no se recuerda: el siguiente lo vuelve a intentar.
        if (t.IsFaulted || t.IsCanceled) { d.TryRemove(clave, out _); return calcular(); }
        return t;
    }

    private sealed class Cierre(ConcurrentDictionary<string, object>? anterior, IDisposable esquema) : IDisposable
    {
        public void Dispose() { esquema.Dispose(); Actual.Value = anterior; }
    }
}
