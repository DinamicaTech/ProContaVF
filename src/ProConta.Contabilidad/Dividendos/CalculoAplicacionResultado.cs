using System.Globalization;

namespace ProConta.Contabilidad.Dividendos;

/// <summary>
/// Cuentas de la aplicación del resultado (contabilidad/procesos/aplicacion-resultado 2026-11-17): la cuenta de resultados que se salda (la
/// del cierre del ejercicio anterior, 1290000), la reserva legal (1120000), las voluntarias (la cuenta de reservas de los dividendos,
/// 1130000) y los resultados negativos de ejercicios anteriores (1210000).
/// </summary>
public sealed record ConfiguracionAplicacion(string CuentaResultado, string CuentaReservaLegal, string CuentaVoluntarias, string CuentaResultadosNegativos)
{
    public const string ResultadoDefecto = "1290000";
    public const string ReservaLegalDefecto = "1120000";
    public const string ResultadosNegativosDefecto = "1210000";

    public static ConfiguracionAplicacion Con(string? resultado, string? legal, string? voluntarias, string? negativos) => new(
        Valor(resultado, ResultadoDefecto), Valor(legal, ReservaLegalDefecto), Valor(voluntarias, ConfiguracionDividendos.ReservasDefecto), Valor(negativos, ResultadosNegativosDefecto));

    private static string Valor(string? v, string defecto) => string.IsNullOrWhiteSpace(v) ? defecto : v.Trim();
}

/// <summary>Saldos que necesita la propuesta, a la fecha de la aplicación y sin sus propios asientos.</summary>
/// <param name="Resultado">Saldo acreedor de la cuenta de resultados (positivo, beneficio; negativo, pérdida).</param>
/// <param name="PerdidasPendientes">Saldo deudor de la 121: pérdidas de ejercicios anteriores aún sin compensar.</param>
/// <param name="ReservaLegal">Saldo acreedor de la reserva legal.</param>
/// <param name="Capital">Saldo acreedor de las cuentas 100 (capital social).</param>
public sealed record SaldosAplicacion(decimal Resultado, decimal PerdidasPendientes, decimal ReservaLegal, decimal Capital);

/// <summary>Importes que escribe el operador; null en uno es «el propuesto».</summary>
public sealed record ImportesAplicacion(decimal? Compensacion = null, decimal? ReservaLegal = null, decimal DividendosNeto = 0m);

/// <summary>Una línea del asiento de la aplicación.</summary>
public sealed record LineaAplicacion(string Cuenta, decimal Debe, decimal Haber, string Concepto);

/// <summary>
/// Propuesta de la aplicación del resultado de <see cref="EjercicioResultado"/>: el reparto del resultado, el asiento (sin los dividendos, que van
/// en su propio reparto con cargo a la cuenta de resultados), los avisos y los errores que impiden confirmarla.
/// </summary>
public sealed record PropuestaAplicacion(int EjercicioResultado, DateTime Fecha, ConfiguracionAplicacion Config, SaldosAplicacion Saldos, decimal Compensacion,
    decimal CompensacionPropuesta, decimal ReservaLegal, decimal MinimoLegal, decimal DividendosBruto, decimal Voluntarias, IReadOnlyList<LineaAplicacion> Lineas,
    IReadOnlyList<string> Avisos, IReadOnlyList<string> Errores)
{
    public decimal Resultado => Saldos.Resultado;
    public bool EsPerdida => Resultado < 0m;
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
    public bool SePuedeConfirmar => Errores.Count == 0 && Resultado != 0m;
}

/// <summary>
/// Cálculo de la aplicación del resultado, sin BD (contabilidad/procesos/aplicacion-resultado 2026-11-17 20:10). Con pérdida, todo a la 121
/// (121 al Debe, 129 al Haber). Con beneficio, en este orden:
/// <list type="number">
/// <item>compensa la 121 si tiene saldo deudor (propuesta: el menor del saldo y el beneficio);</item>
/// <item>reserva legal: el mínimo de la ley (art. 274 LSC), el 10 % del beneficio hasta que la reserva legal llegue al 20 % del capital,
/// redondeado al céntimo y sin pasar de lo que queda tras compensar;</item>
/// <item>dividendos: el bruto del reparto con cargo al resultado (cero si no se reparte);</item>
/// <item>el resto, a reservas voluntarias.</item>
/// </list>
/// El asiento carga la cuenta de resultados por todo menos los dividendos (su reparto ya la carga por el bruto) y abona la 121, la 112 y la 113.
/// Las líneas a cero no salen.
/// </summary>
public static class CalculoAplicacionResultado
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Descripcion(int ejercicioResultado) => $"Aplicación del resultado {ejercicioResultado}";

    /// <summary>Fecha propuesta: 30/06 del ejercicio en que se aplica (la junta ordinaria aprueba las cuentas en los seis meses siguientes).</summary>
    public static DateTime FechaPropuesta(int ejercicio) => new(ejercicio, 6, 30);

    /// <summary>Mínimo legal de la reserva legal: 10 % del beneficio hasta el 20 % del capital, al céntimo.</summary>
    public static decimal MinimoReservaLegal(decimal beneficio, decimal reservaLegal, decimal capital)
    {
        if (beneficio <= 0m) return 0m;
        var diez = Math.Round(beneficio * 0.10m, 2, MidpointRounding.AwayFromZero);
        var falta = Math.Max(0m, Math.Round(capital * 0.20m, 2, MidpointRounding.AwayFromZero) - Math.Max(0m, reservaLegal));
        return Math.Min(diez, falta);
    }

    public static PropuestaAplicacion Calcular(int ejercicioResultado, DateTime fecha, ConfiguracionAplicacion config, SaldosAplicacion saldos, ImportesAplicacion importes,
        decimal dividendosBruto = 0m)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(saldos);
        ArgumentNullException.ThrowIfNull(importes);
        var avisos = new List<string>();
        var errores = new List<string>();
        var lineas = new List<LineaAplicacion>();
        var r = Math.Round(saldos.Resultado, 2, MidpointRounding.AwayFromZero);
        var texto = Descripcion(ejercicioResultado);
        var perdidas = Math.Max(0m, saldos.PerdidasPendientes);

        if (r == 0m)
        {
            errores.Add($"La cuenta de resultados {config.CuentaResultado} no tiene saldo: no hay resultado de {ejercicioResultado} que aplicar " +
                $"(¿está cerrado {ejercicioResultado} con su apertura en este ejercicio, o ya se aplicó fuera de este proceso?).");
            return new(ejercicioResultado, fecha.Date, config, saldos with { Resultado = r }, 0m, 0m, 0m, 0m, 0m, 0m, lineas, avisos, errores);
        }

        if (r < 0m)
        {
            if (dividendosBruto != 0m) errores.Add("Con pérdida no se pueden repartir dividendos con cargo al resultado.");
            if (importes.Compensacion is > 0m || importes.ReservaLegal is > 0m) avisos.Add("Con pérdida no hay compensación ni reserva legal: todo va a resultados negativos de ejercicios anteriores.");
            lineas.Add(new(config.CuentaResultadosNegativos, -r, 0m, texto));
            lineas.Add(new(config.CuentaResultado, 0m, -r, texto));
            return new(ejercicioResultado, fecha.Date, config, saldos with { Resultado = r }, 0m, 0m, 0m, 0m, 0m, 0m, lineas, avisos, errores);
        }

        var compensacionPropuesta = Math.Min(perdidas, r);
        var compensacion = Math.Round(importes.Compensacion ?? compensacionPropuesta, 2, MidpointRounding.AwayFromZero);
        if (compensacion < 0m) errores.Add("La compensación de pérdidas no puede ser negativa.");
        if (compensacion > perdidas)
            errores.Add($"La compensación ({Euros(compensacion)}) pasa de las pérdidas pendientes de la {config.CuentaResultadosNegativos} ({Euros(perdidas)}): la dejaría con saldo acreedor.");
        var minimo = Math.Min(MinimoReservaLegal(r, saldos.ReservaLegal, saldos.Capital), Math.Max(0m, r - Math.Max(0m, compensacion)));
        var legal = Math.Round(importes.ReservaLegal ?? minimo, 2, MidpointRounding.AwayFromZero);
        if (legal < 0m) errores.Add("La reserva legal no puede ser negativa.");
        else if (legal < minimo)
            avisos.Add($"La reserva legal ({Euros(legal)}) es menor que el mínimo de la ley ({Euros(minimo)}: el 10 % del beneficio hasta que llegue al 20 % del capital).");
        if (saldos.Capital <= 0m)
            avisos.Add("Las cuentas de capital (100) no tienen saldo: no se ha podido calcular el tope del 20 % de la reserva legal. Revisa el importe.");
        var dividendos = Math.Round(dividendosBruto, 2, MidpointRounding.AwayFromZero);
        if (dividendos < 0m) errores.Add("Los dividendos no pueden ser negativos.");
        if (dividendos > 0m && perdidas - compensacion > 0m)
            avisos.Add($"Quedan {Euros(perdidas - compensacion)} de pérdidas de ejercicios anteriores sin compensar y se reparten dividendos: la ley no lo permite si el patrimonio neto queda por debajo del capital.");
        var voluntarias = r - compensacion - legal - dividendos;
        if (voluntarias < 0m)
            errores.Add($"Compensación, reserva legal y dividendos suman {Euros(compensacion + legal + dividendos)} y el beneficio es {Euros(r)}: bájalos para que no pasen del beneficio.");

        if (errores.Count == 0)
        {
            var cargo = r - dividendos;
            if (cargo != 0m)
            {
                lineas.Add(new(config.CuentaResultado, cargo, 0m, texto));
                if (compensacion != 0m) lineas.Add(new(config.CuentaResultadosNegativos, 0m, compensacion, $"Compensación de pérdidas anteriores {ejercicioResultado}"));
                if (legal != 0m) lineas.Add(new(config.CuentaReservaLegal, 0m, legal, $"Reserva legal {ejercicioResultado}"));
                if (voluntarias != 0m) lineas.Add(new(config.CuentaVoluntarias, 0m, voluntarias, $"Reservas voluntarias {ejercicioResultado}"));
            }
        }
        return new(ejercicioResultado, fecha.Date, config, saldos with { Resultado = r }, compensacion, compensacionPropuesta, legal, minimo, dividendos,
            Math.Max(0m, voluntarias), lineas, avisos, errores);
    }

    public static string Euros(decimal v) => v.ToString("#,##0.00", Es) + " €";
}
