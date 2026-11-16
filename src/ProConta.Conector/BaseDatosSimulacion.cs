using ProConta.Datos.Persistencia;

namespace ProConta.Conector;

/// <summary>
/// BD que ejecuta las transacciones de verdad pero siempre las revierte: con ella el motor de asientos valida una propuesta con todas sus
/// reglas (cuadre, cuentas, ejercicio y trimestre cerrados, versión) sin dejar nada grabado. Las lecturas sueltas van a la BD real.
/// </summary>
internal sealed class BaseDatosSimulacion(IBaseDatos real) : IBaseDatos
{
    private sealed class Revertir(object? resultado) : Exception("Simulación: se revierte la transacción.")
    {
        public object? Resultado { get; } = resultado;
    }

    public string Ruta => real.Ruta;

    public Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) =>
        // Una escritura suelta no se podría deshacer: también va en una transacción que se revierte.
        EnTransaccionAsync(trabajo, cancelacion);

    public async Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        try
        {
            await real.EnTransaccionAsync<T>(async s => throw new Revertir(await trabajo(s)), cancelacion);
            throw new InvalidOperationException("La simulación no se ha revertido.");
        }
        catch (Revertir r)
        {
            return (T)r.Resultado!;
        }
    }
}
