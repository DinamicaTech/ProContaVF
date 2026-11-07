namespace ProConta.Nucleo.Contexto;

/// <summary>
/// Trimestres contables y la fecha de los asientos de facturas (compras y ventas, propietario 2026-11-07 11:59): el asiento
/// lleva la fecha de la factura, salvo que sea anterior al trimestre activo, y entonces el día 1 del trimestre activo.
/// Así el asiento queda cerca de su cobro o pago (Norma 43) y nunca cae en un trimestre ya cerrado. El IVA va al trimestre
/// de la fecha del asiento.
/// </summary>
public static class TrimestreContable
{
    /// <summary>Trimestre de una fecha (1 a 4).</summary>
    public static int De(DateTime fecha) => (fecha.Month - 1) / 3 + 1;

    /// <summary>Día 1 del trimestre <paramref name="trimestre"/> del ejercicio.</summary>
    public static DateTime Inicio(int ejercicio, int trimestre) => new(ejercicio, 3 * (Math.Clamp(trimestre, 1, 4) - 1) + 1, 1);

    /// <summary>
    /// Trimestre activo del ejercicio; si no hay ninguno configurado, el de hoy si hoy es de ese ejercicio, el cuarto si el
    /// ejercicio ya pasó y el primero si aún no ha empezado.
    /// </summary>
    public static int Activo(int? configurado, int ejercicio, DateTime hoy) =>
        configurado is >= 1 and <= 4 and int t ? t : hoy.Year == ejercicio ? De(hoy) : hoy.Year > ejercicio ? 4 : 1;

    /// <summary>Fecha del asiento de una factura: la suya, o el día 1 del trimestre activo si es anterior.</summary>
    public static DateTime FechaAsiento(DateTime fechaFactura, int ejercicio, int trimestreActivo)
    {
        var inicio = Inicio(ejercicio, trimestreActivo);
        return fechaFactura.Date < inicio ? inicio : fechaFactura.Date;
    }
}
