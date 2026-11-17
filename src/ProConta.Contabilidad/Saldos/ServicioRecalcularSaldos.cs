using ProConta.Contabilidad.Asientos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Saldos;

/// <summary>
/// Proceso «Recalcular saldos» (contabilidad/procesos/recalcular-saldos): compara la caché de saldos de las cuentas del ejercicio
/// con sus apuntes sin grabar nada y, a petición, la rehace con <see cref="IMotorAsientos.RecalcularSaldosAsync"/>. No genera
/// asientos ni toca los apuntes, por eso no pide permiso propio y funciona también en un ejercicio cerrado.
/// </summary>
public sealed class ServicioRecalcularSaldos(IBaseDatos bd, IMotorAsientos motor)
{
    /// <summary>Suma de los apuntes de cada código de cuenta en los asientos del ejercicio.</summary>
    public const string ConsultaMovimientos =
        "SELECT ap.CuentaContable, SUM(IIF(ap.ImporteDebe IS NULL, 0, ap.ImporteDebe)) - SUM(IIF(ap.ImporteHaber IS NULL, 0, ap.ImporteHaber)) AS Saldo, COUNT(*) AS N " +
        "FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? GROUP BY ap.CuentaContable";

    public static Task<IReadOnlyList<MovimientosCuenta>> MovimientosAsync(ISesionDatos s, int idSociedadEjercicio) =>
        s.ConsultarAsync(ConsultaMovimientos,
            f => new MovimientosCuenta((f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("Saldo") ?? 0m, f.Entero("N")), idSociedadEjercicio);

    /// <summary>Compara la caché con los apuntes del ejercicio, sin grabar nada.</summary>
    public Task<ComparacionSaldosEjercicio> CompararAsync(ContextoSociedadEjercicio contexto) =>
        bd.EjecutarAsync(async s =>
        {
            var cuentas = await s.ConsultarAsync(
                "SELECT Codigo, Nombre, SaldoInicial, SaldoActual, NumeroApuntes FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => new CacheCuenta((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), f.DecimalNulo("SaldoInicial") ?? 0m,
                    f.DecimalNulo("SaldoActual") ?? 0m, f.EnteroNulo("NumeroApuntes") ?? 0),
                contexto.IdSociedadEjercicio);
            var movimientos = await MovimientosAsync(s, contexto.IdSociedadEjercicio);
            return ComparacionSaldos.Comparar(cuentas, movimientos);
        });

    /// <summary>Rehace la caché de todas las cuentas del ejercicio desde sus apuntes y devuelve la comparación de después.</summary>
    public async Task<ComparacionSaldosEjercicio> RecalcularAsync(ContextoSociedadEjercicio contexto)
    {
        await motor.RecalcularSaldosAsync(contexto);
        return await CompararAsync(contexto);
    }
}
