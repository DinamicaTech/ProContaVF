using ProConta.Datos.Persistencia;
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

    /// <summary>
    /// Movimientos N43 de una subcuenta de tercero que cambia (terceros/roles/cambio-subcuenta), con sus asientos (de ProContaVF o del ERP).
    /// Sin facturas (<paramref name="idsFacturaVenta"/> y <paramref name="idsRegistroCompra"/> null), los imputados a la identidad antigua;
    /// con facturas, los enlazados a ellas (la subcuenta la comparte otro tercero).
    /// </summary>
    Task<IReadOnlyList<MovimientoBancoTercero>> MovimientosDeSubcuentaAsync(int idIdentidadAntigua, IReadOnlyCollection<int>? idsFacturaVenta,
        IReadOnlyCollection<int>? idsRegistroCompra) => Task.FromResult<IReadOnlyList<MovimientoBancoTercero>>([]);

    /// <summary>
    /// Pasa a la identidad nueva las imputaciones vivas de esas líneas a la antigua, dentro de la transacción del cambio, y, con
    /// <paramref name="palabrasClave"/> (la subcuenta antigua era solo de este tercero), también las palabras clave bancarias que
    /// asignaban la antigua; devuelve cuántas imputaciones ha cambiado.
    /// </summary>
    Task<int> ReimputarAsync(ISesionDatos s, IReadOnlyCollection<int> lineas, int idIdentidadAntigua, int idIdentidadNueva, bool palabrasClave) => Task.FromResult(0);
}

/// <summary>Movimiento N43 afectado por el cambio de subcuenta de un tercero: sus asientos y si está imputado a la subcuenta antigua.</summary>
public sealed record MovimientoBancoTercero(int IdLinea, IReadOnlyList<int> Asientos, bool ImputadoALaAntigua);

/// <summary>No hay asientos de banco que actualizar (pruebas y pantallas sin Norma 43).</summary>
public sealed class SinAsientosBanco : IAsientosBancoEnlazados
{
    public static SinAsientosBanco Instancia { get; } = new();

    public Task<ResultadoAsientosBanco> FacturaVentaCambiadaAsync(int idFactura, Actor actor) => Task.FromResult(ResultadoAsientosBanco.Nada);

    public Task<ResultadoAsientosBanco> FacturaCompraCambiadaAsync(int idRegistro, Actor actor) => Task.FromResult(ResultadoAsientosBanco.Nada);

    public Task<ResultadoAsientosBanco> TerceroCambiadoAsync(int idTercero, Actor actor) => Task.FromResult(ResultadoAsientosBanco.Nada);
}
