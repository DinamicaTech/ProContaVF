using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Importes;

namespace ProConta.Contabilidad.Cierre;

/// <summary>Saldo (Debe − Haber) de una subcuenta en el ejercicio que se cierra, con su nombre.</summary>
public sealed record SaldoCuentaCierre(string Cuenta, string Nombre, decimal Saldo);

/// <summary>Línea de uno de los asientos del cierre.</summary>
public sealed record LineaCierre(string Cuenta, string Nombre, decimal Debe, decimal Haber);

/// <summary>Uno de los tres asientos del cierre (regularización, cierre o apertura), con su tipo, fecha y descripción.</summary>
public sealed record AsientoCierre(TipoAsiento Tipo, DateTime Fecha, string Descripcion, IReadOnlyList<LineaCierre> Lineas)
{
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
    public bool Cuadra => TotalDebe == TotalHaber;

    /// <summary>Mismas líneas (para saber si la propuesta ha cambiado desde que se vio).</summary>
    public bool Igual(AsientoCierre? otro) => otro is not null && otro.Tipo == Tipo && otro.Fecha == Fecha && otro.Lineas.SequenceEqual(Lineas);
}

/// <summary>
/// Propuesta del cierre de un ejercicio: el resultado, los tres asientos (null si no hay nada que saldar) y lo que impide cerrar
/// (<see cref="Bloqueos"/>: si hay alguno, no se graba).
/// </summary>
public sealed record PropuestaCierre(int Ejercicio, string CuentaResultado, decimal Ingresos, decimal Gastos, AsientoCierre? Regularizacion, AsientoCierre? Cierre,
    AsientoCierre? Apertura, IReadOnlyList<string> Bloqueos)
{
    /// <summary>Resultado del ejercicio: ingresos − gastos (positivo, beneficio).</summary>
    public decimal Resultado => Ingresos - Gastos;

    /// <summary>Cada asiento cuadra y la apertura es exactamente el cierre al revés.</summary>
    public bool Cuadra =>
        (Regularizacion?.Cuadra ?? true) && (Cierre?.Cuadra ?? true) && (Apertura?.Cuadra ?? true)
        && (Cierre?.TotalDebe ?? 0m) == (Apertura?.TotalHaber ?? 0m) && (Cierre?.TotalHaber ?? 0m) == (Apertura?.TotalDebe ?? 0m)
        && CalculoCierreEjercicio.EsReverso(Cierre, Apertura);

    public bool SePuedeCerrar => Bloqueos.Count == 0;

    /// <summary>Misma propuesta (mismos asientos): para no grabar algo distinto de lo que se ha visto.</summary>
    public bool Igual(PropuestaCierre otra) =>
        otra.Ejercicio == Ejercicio && otra.CuentaResultado == CuentaResultado && Mismo(Regularizacion, otra.Regularizacion) && Mismo(Cierre, otra.Cierre)
        && Mismo(Apertura, otra.Apertura);

    private static bool Mismo(AsientoCierre? a, AsientoCierre? b) => a is null ? b is null : a.Igual(b);
}

/// <summary>
/// Cálculo del cierre del ejercicio sin BD (contabilidad/procesos/cierre-ejercicio 2026-11-17): con los saldos de las subcuentas del ejercicio
/// (apuntes contabilizados, sin la regularización, el cierre ni la apertura del propio cierre), propone
/// <list type="bullet">
/// <item>la regularización a 31/12: cada subcuenta de los grupos 6 y 7 con saldo se salda contra la cuenta de resultados (1290000), que
/// recibe el resultado (al Haber si es beneficio);</item>
/// <item>el cierre a 31/12: cada subcuenta de los grupos 1 a 5 con saldo, incluida la de resultados, se salda;</item>
/// <item>la apertura a 1/1 del ejercicio siguiente: el cierre al revés.</item>
/// </list>
/// Una línea por subcuenta, en orden de código y sin centro de coste. Los saldos se redondean a céntimos. Impiden cerrar: saldos en los
/// grupos 8 y 9 (o en cuentas que no empiezan por 1 a 9), porque no se regularizan contra la 129, y que los saldos del ejercicio no sumen
/// cero (algún asiento descuadrado), porque el cierre no cuadraría.
/// </summary>
public static class CalculoCierreEjercicio
{
    public const string CuentaResultadoDefecto = "1290000";
    public const string NombreResultadoDefecto = "Resultado del ejercicio";

    public static DateTime FechaCierre(int ejercicio) => new(ejercicio, 12, 31);
    public static DateTime FechaApertura(int ejercicio) => new(ejercicio + 1, 1, 1);

    public static string DescripcionRegularizacion(int ejercicio) => $"Regularización de cuentas {ejercicio}";
    public static string DescripcionCierre(int ejercicio) => $"Cierre del ejercicio {ejercicio}";
    public static string DescripcionApertura(int ejercicio) => $"Asiento de apertura {ejercicio + 1}";

    public static bool EsDeResultados(string cuenta) => cuenta.Length > 0 && cuenta[0] is '6' or '7';
    public static bool EsDeBalance(string cuenta) => cuenta.Length > 0 && cuenta[0] is >= '1' and <= '5';

    public static PropuestaCierre Calcular(int ejercicio, string? cuentaResultado, string? nombreResultado, IReadOnlyList<SaldoCuentaCierre> saldos)
    {
        var resultadoCuenta = string.IsNullOrWhiteSpace(cuentaResultado) ? CuentaResultadoDefecto : cuentaResultado.Trim();
        var nombre = string.IsNullOrWhiteSpace(nombreResultado) ? NombreResultadoDefecto : nombreResultado.Trim();
        var bloqueos = new List<string>();

        // Una fila por subcuenta, redondeada a céntimos; sin las que quedan a cero.
        var porCuenta = saldos
            .GroupBy(x => x.Cuenta.Trim(), StringComparer.Ordinal)
            .Select(g => new SaldoCuentaCierre(g.Key, g.Select(x => x.Nombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim() ?? "", Importe.Redondear(g.Sum(x => x.Saldo))))
            .Where(x => x.Saldo != 0m && x.Cuenta.Length > 0)
            .OrderBy(x => x.Cuenta, StringComparer.Ordinal)
            .ToList();

        var otras = porCuenta.Where(x => !EsDeResultados(x.Cuenta) && !EsDeBalance(x.Cuenta)).ToList();
        if (otras.Count > 0)
            bloqueos.Add($"Hay saldo en cuentas que el cierre no regulariza ({string.Join(", ", otras.Take(5).Select(x => $"{x.Cuenta} {Euros(x.Saldo)}"))}" +
                $"{(otras.Count > 5 ? "…" : "")}): los grupos 8 y 9 se llevan a patrimonio neto, no a la 129. Corrígelo con un asiento antes de cerrar.");
        var descuadre = porCuenta.Sum(x => x.Saldo);
        if (descuadre != 0m)
            bloqueos.Add($"Los saldos del ejercicio no suman cero (diferencia de {Euros(descuadre)}): algún asiento está descuadrado y el cierre no cuadraría. " +
                "Búscalo en el Diario (las sumas de cada asiento) y corrígelo antes de cerrar.");

        // Regularización: grupos 6 y 7 contra la cuenta de resultados.
        var resultados = porCuenta.Where(x => EsDeResultados(x.Cuenta)).ToList();
        var ingresos = -resultados.Where(x => x.Cuenta[0] == '7').Sum(x => x.Saldo);
        var gastos = resultados.Where(x => x.Cuenta[0] == '6').Sum(x => x.Saldo);
        var resultado = -resultados.Sum(x => x.Saldo);
        AsientoCierre? regularizacion = null;
        if (resultados.Count > 0)
        {
            var lineas = resultados.Select(x => Saldar(x)).ToList();
            if (resultado != 0m)
                lineas.Add(resultado > 0m ? new LineaCierre(resultadoCuenta, nombre, 0m, resultado) : new LineaCierre(resultadoCuenta, nombre, -resultado, 0m));
            regularizacion = new(TipoAsiento.Regularizacion, FechaCierre(ejercicio), DescripcionRegularizacion(ejercicio), lineas);
        }

        // Cierre: grupos 1 a 5 con la cuenta de resultados ya regularizada.
        var balance = porCuenta.Where(x => EsDeBalance(x.Cuenta)).ToDictionary(x => x.Cuenta, StringComparer.Ordinal);
        if (resultado != 0m)
            balance[resultadoCuenta] = balance.TryGetValue(resultadoCuenta, out var r)
                ? r with { Saldo = r.Saldo - resultado, Nombre = string.IsNullOrEmpty(r.Nombre) ? nombre : r.Nombre }
                : new SaldoCuentaCierre(resultadoCuenta, nombre, -resultado);
        var aCerrar = balance.Values.Where(x => x.Saldo != 0m).OrderBy(x => x.Cuenta, StringComparer.Ordinal).ToList();
        AsientoCierre? cierre = null, apertura = null;
        if (aCerrar.Count > 0)
        {
            cierre = new(TipoAsiento.Cierre, FechaCierre(ejercicio), DescripcionCierre(ejercicio), aCerrar.Select(x => Saldar(x)).ToList());
            apertura = new(TipoAsiento.Apertura, FechaApertura(ejercicio), DescripcionApertura(ejercicio),
                aCerrar.Select(x => x.Saldo > 0m ? new LineaCierre(x.Cuenta, x.Nombre, x.Saldo, 0m) : new LineaCierre(x.Cuenta, x.Nombre, 0m, -x.Saldo)).ToList());
        }
        if (regularizacion is null && cierre is null)
            bloqueos.Add($"El ejercicio {ejercicio} no tiene ningún saldo que cerrar.");

        return new PropuestaCierre(ejercicio, resultadoCuenta, ingresos, gastos, regularizacion, cierre, apertura, bloqueos);
    }

    /// <summary>La apertura tiene las mismas cuentas e importes que el cierre, cambiados de lado.</summary>
    public static bool EsReverso(AsientoCierre? cierre, AsientoCierre? apertura)
    {
        if (cierre is null || apertura is null) return cierre is null && apertura is null;
        return cierre.Lineas.Count == apertura.Lineas.Count
            && cierre.Lineas.Zip(apertura.Lineas).All(p => p.First.Cuenta == p.Second.Cuenta && p.First.Debe == p.Second.Haber && p.First.Haber == p.Second.Debe);
    }

    /// <summary>Asiento del motor con las líneas de la propuesta, del tipo de cierre que toca y marcado como generado por el cierre.</summary>
    public static Asiento AAsiento(Nucleo.Contexto.ContextoSociedadEjercicio contexto, AsientoCierre a, string? notas)
    {
        var asiento = new Asiento(contexto, a.Fecha, a.Descripcion) { Tipo = a.Tipo, Notas = notas };
        foreach (var l in a.Lineas)
        {
            var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe) : Apunte.AlHaber(l.Cuenta, l.Haber);
            ap.GeneradoAutomaticamente = true;
            ap.MotivoGeneracion = a.Tipo switch
            {
                TipoAsiento.Regularizacion => "Regularización del ejercicio",
                TipoAsiento.Cierre => "Cierre del ejercicio",
                _ => "Apertura del ejercicio",
            };
            asiento.Apuntes.Add(ap);
        }
        return asiento;
    }

    private static LineaCierre Saldar(SaldoCuentaCierre x) =>
        x.Saldo > 0m ? new LineaCierre(x.Cuenta, x.Nombre, 0m, x.Saldo) : new LineaCierre(x.Cuenta, x.Nombre, -x.Saldo, 0m);

    internal static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
