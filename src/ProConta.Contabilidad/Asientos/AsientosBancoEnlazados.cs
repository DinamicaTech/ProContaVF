using ProConta.Nucleo.Auditoria;

namespace ProConta.Contabilidad.Asientos;

/// <summary>
/// Resultado de actualizar los asientos de banco de los movimientos N43 enlazados a una factura o a un tercero
/// (norma43/conciliacion/actualizar-textos): cuántos asientos han cambiado y, si alguno no se ha podido actualizar, por qué.
/// </summary>
public sealed record ResultadoAsientosBanco(int Actualizados, int NoActualizados, IReadOnlyList<string> Motivos)
{
    public static ResultadoAsientosBanco Nada { get; } = new(0, 0, []);

    /// <summary>Texto para el aviso de la pantalla, o null si no hay nada que decir.</summary>
    public string? Aviso
    {
        get
        {
            var partes = new List<string>();
            if (Actualizados > 0)
                partes.Add(Actualizados == 1 ? "He actualizado el texto de 1 asiento de banco." : $"He actualizado el texto de {Actualizados} asientos de banco.");
            if (NoActualizados > 0)
                partes.Add((NoActualizados == 1 ? "1 asiento de banco no se ha actualizado: " : $"{NoActualizados} asientos de banco no se han actualizado: ")
                    + string.Join(" ", Motivos));
            return partes.Count == 0 ? null : string.Join(" ", partes);
        }
    }
}

/// <summary>
/// Ventas, compras y terceros avisan aquí cuando cambia algo que sale en los asientos de banco de los movimientos N43
/// enlazados (la descripción abreviada o el número de una factura, el nombre del tercero), para que la conciliación
/// vuelva a generar sus textos en el mismo asiento (norma43/conciliacion/actualizar-textos). La implementación está en
/// Norma 43; sin ella (<see cref="SinAsientosBanco"/>) no se hace nada.
/// </summary>
public interface IAsientosBancoEnlazados
{
    Task<ResultadoAsientosBanco> FacturaVentaCambiadaAsync(int idFactura, Actor actor);

    Task<ResultadoAsientosBanco> FacturaCompraCambiadaAsync(int idRegistro, Actor actor);

    Task<ResultadoAsientosBanco> TerceroCambiadoAsync(int idTercero, Actor actor);
}

/// <summary>No hay asientos de banco que actualizar (pruebas y pantallas sin Norma 43).</summary>
public sealed class SinAsientosBanco : IAsientosBancoEnlazados
{
    public static SinAsientosBanco Instancia { get; } = new();

    public Task<ResultadoAsientosBanco> FacturaVentaCambiadaAsync(int idFactura, Actor actor) => Task.FromResult(ResultadoAsientosBanco.Nada);

    public Task<ResultadoAsientosBanco> FacturaCompraCambiadaAsync(int idRegistro, Actor actor) => Task.FromResult(ResultadoAsientosBanco.Nada);

    public Task<ResultadoAsientosBanco> TerceroCambiadoAsync(int idTercero, Actor actor) => Task.FromResult(ResultadoAsientosBanco.Nada);
}
