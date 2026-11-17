using ProConta.Contabilidad.Amortizacion;

namespace ProConta.Activos.Gestion;

/// <summary>
/// Lo contabilizado de verdad en un ejercicio que existe en el programa: lo cargado en la subcuenta de gasto por amortización del activo (Debe − Haber,
/// sin regularización ni cierre) y el saldo acreedor de su amortización acumulada al terminar el año (sin el cierre).
/// </summary>
public sealed record AmortizacionContabilizada(int Ejercicio, decimal Cargado, decimal AcumuladaAlFinal);

/// <summary>Estado de un ejercicio en la tabla de amortización de la ficha.</summary>
public enum EstadoAmortizacionEjercicio
{
    /// <summary>Ejercicio pasado (o el de trabajo ya cargado del todo): lo cargado de verdad en su subcuenta.</summary>
    Contabilizada,
    /// <summary>Ejercicio anterior a los datos del programa: calculado con el plan.</summary>
    Calculada,
    /// <summary>El ejercicio de trabajo, mientras no esté contabilizada del todo.</summary>
    PendienteDeContabilizar,
    /// <summary>Ejercicios siguientes: la proyección del plan.</summary>
    Prevista,
}

/// <summary>Una fila de la tabla: ejercicio, días amortizados de los del año, cuota, acumulado al final, lo que queda pendiente y el estado.</summary>
public sealed record FilaTablaAmortizacion(int Ejercicio, int Dias, int DiasAno, decimal Cuota, decimal Acumulado, decimal Pendiente, EstadoAmortizacionEjercicio Estado);

/// <summary>
/// Tabla de amortización de la ficha (activos 2026-11-17 18:50, apartado Amortización), sin base de datos: una fila por ejercicio desde el inicio del
/// plan. Los ejercicios pasados que existen en el programa muestran lo cargado de verdad y su acumulada real; los anteriores a los datos del programa, el
/// cálculo del plan. El ejercicio de trabajo y los siguientes son la proyección de <see cref="CalculoAmortizacion.DelAno"/> encadenada, que parte de
/// la acumulada real al empezar el ejercicio de trabajo; termina cuando no queda nada pendiente o el plan ya no tiene días (baja).
/// </summary>
public static class TablaAmortizacionActivo
{
    /// <summary>Tope de filas, por si un plan tiene un % muy pequeño.</summary>
    public const int MaximoFilas = 150;

    public static IReadOnlyList<FilaTablaAmortizacion> Construir(PlanAmortizacion plan, int ejercicioTrabajo, IReadOnlyDictionary<int, AmortizacionContabilizada> contabilizada)
    {
        var filas = new List<FilaTablaAmortizacion>();
        if (plan.Base <= 0m || plan.PorcentajeAnual <= 0m) return filas;
        var acumulado = 0m;
        for (var y = plan.FechaInicio.Year; filas.Count < MaximoFilas; y++)
        {
            var dias = CalculoAmortizacion.Dias(plan, y, new DateTime(y, 12, 31));
            var diasAno = CalculoAmortizacion.DiasAno(y);
            if (y < ejercicioTrabajo)
            {
                if (contabilizada.TryGetValue(y, out var real))
                {
                    acumulado = real.AcumuladaAlFinal;
                    if (dias == 0 && real.Cargado == 0m) continue; // ya de baja: nada que enseñar
                    filas.Add(new FilaTablaAmortizacion(y, dias, diasAno, real.Cargado, acumulado, Pendiente(plan, acumulado), EstadoAmortizacionEjercicio.Contabilizada));
                }
                else
                {
                    if (dias == 0) continue;
                    var c = CalculoAmortizacion.DelAno(plan, y, acumulado);
                    acumulado += c.Importe;
                    filas.Add(new FilaTablaAmortizacion(y, dias, diasAno, c.Importe, acumulado, Pendiente(plan, acumulado), EstadoAmortizacionEjercicio.Calculada));
                }
                continue;
            }
            if (dias == 0) break; // dado de baja antes de este año
            var cuota = CalculoAmortizacion.DelAno(plan, y, acumulado);
            if (y > ejercicioTrabajo && cuota.Importe == 0m) break;
            acumulado += cuota.Importe;
            var estado = y > ejercicioTrabajo ? EstadoAmortizacionEjercicio.Prevista
                : contabilizada.TryGetValue(y, out var hoy) && cuota.Importe > 0m && hoy.Cargado >= cuota.Importe ? EstadoAmortizacionEjercicio.Contabilizada
                : EstadoAmortizacionEjercicio.PendienteDeContabilizar;
            filas.Add(new FilaTablaAmortizacion(y, dias, diasAno, cuota.Importe, acumulado, Pendiente(plan, acumulado), estado));
            if (Pendiente(plan, acumulado) == 0m) break;
        }
        return filas;
    }

    /// <summary>Último ejercicio con cuota (el de la última fila con importe); null si no hay ninguno.</summary>
    public static int? TerminaEn(IReadOnlyList<FilaTablaAmortizacion> filas) => filas.LastOrDefault(f => f.Cuota > 0m)?.Ejercicio;

    private static decimal Pendiente(PlanAmortizacion plan, decimal acumulado) => Math.Max(0m, CalculoAmortizacion.Redondear(plan.Amortizable - acumulado));
}
