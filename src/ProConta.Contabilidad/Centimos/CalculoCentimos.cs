using System.Globalization;

namespace ProConta.Contabilidad.Centimos;

/// <summary>
/// Parámetros del ejercicio (organizacion/sociedades-ejercicios, esquema 69); vacíos, los de siempre: umbral de 1,00 €, 6590000 para las
/// diferencias de redondeo en contra (gasto) y 7590000 para las a favor (ingreso). Las cuentas de redondeo son las mismas que usa Compras.
/// </summary>
public sealed record ConfiguracionCentimos(decimal Umbral, string CuentaGastos, string CuentaIngresos)
{
    public const decimal UmbralDefecto = 1m;
    public const string CuentaGastosDefecto = "6590000";
    public const string CuentaIngresosDefecto = "7590000";

    public static ConfiguracionCentimos Defecto { get; } = new(UmbralDefecto, CuentaGastosDefecto, CuentaIngresosDefecto);

    public static ConfiguracionCentimos Con(decimal? umbral, string? gastos, string? ingresos) => new(
        umbral is decimal u and > 0m ? u : UmbralDefecto,
        string.IsNullOrWhiteSpace(gastos) ? CuentaGastosDefecto : gastos.Trim(),
        string.IsNullOrWhiteSpace(ingresos) ? CuentaIngresosDefecto : ingresos.Trim());
}

/// <summary>Saldo de una subcuenta a 31/12 (Debe − Haber de los apuntes contabilizados del ejercicio sin regularización, cierre ni los lotes de este proceso).</summary>
public sealed record SaldoSubcuenta(string Cuenta, string Nombre, decimal Saldo);

/// <summary>Una fila de la propuesta: una subcuenta con céntimos residuales, la cuenta de redondeo que la salda y si entra (casilla).</summary>
public sealed record FilaCentimos(string Cuenta, string Nombre, decimal Saldo, string Contrapartida, bool Incluido)
{
    /// <summary>Saldo deudor: se salda al Haber contra la cuenta de gastos de redondeo.</summary>
    public bool Deudora => Saldo > 0m;
    public decimal Importe => Math.Abs(Saldo);
}

/// <summary>Una línea de un asiento propuesto o grabado.</summary>
public sealed record LineaCentimos(string Cuenta, decimal Debe, decimal Haber, string Concepto);

/// <summary>Un asiento propuesto o grabado: el de una subcuenta, con sus dos líneas.</summary>
public sealed record AsientoCentimos(string Subcuenta, IReadOnlyList<LineaCentimos> Lineas)
{
    public decimal Importe => Lineas.Sum(l => l.Debe);
}

/// <summary>Propuesta del ejercicio: las filas (incluidas y excluidas), un asiento por subcuenta incluida y los avisos.</summary>
public sealed record PropuestaCentimos(int Ejercicio, DateTime Fecha, ConfiguracionCentimos Configuracion, IReadOnlyList<FilaCentimos> Filas,
    IReadOnlyList<AsientoCentimos> Asientos, IReadOnlyList<string> Avisos)
{
    public IEnumerable<FilaCentimos> Incluidas => Filas.Where(f => f.Incluido);
    public decimal Total => Asientos.Sum(a => a.Importe);
    /// <summary>A la cuenta de gastos de redondeo (subcuentas deudoras).</summary>
    public decimal TotalGastos => Incluidas.Where(f => f.Deudora).Sum(f => f.Importe);
    /// <summary>A la cuenta de ingresos de redondeo (subcuentas acreedoras).</summary>
    public decimal TotalIngresos => Incluidas.Where(f => !f.Deudora).Sum(f => f.Importe);

    /// <summary>Subcuentas excluidas a mano (lo que se guarda en CentimosResidualesExclusiones).</summary>
    public IReadOnlyList<string> Excluidas => Filas.Where(f => !f.Incluido).Select(f => f.Cuenta).ToList();

    /// <summary>Mismos asientos (lo que se graba): sirve para confirmar solo lo que se ve.</summary>
    public bool Igual(PropuestaCentimos otra) => Ejercicio == otra.Ejercicio && MismosAsientos(Asientos, otra.Asientos);

    /// <summary>Los mismos asientos con las mismas líneas, sin mirar el orden de los asientos.</summary>
    public static bool MismosAsientos(IReadOnlyList<AsientoCentimos> a, IReadOnlyList<AsientoCentimos> b)
    {
        static IEnumerable<string> Claves(IEnumerable<AsientoCentimos> l) => l
            .Select(x => string.Join("|", x.Lineas.OrderBy(y => y.Cuenta, StringComparer.Ordinal).ThenBy(y => y.Debe)
                .Select(y => $"{y.Cuenta};{y.Debe.ToString(CultureInfo.InvariantCulture)};{y.Haber.ToString(CultureInfo.InvariantCulture)};{y.Concepto}")))
            .Order(StringComparer.Ordinal);
        return a.Count == b.Count && Claves(a).SequenceEqual(Claves(b));
    }
}

/// <summary>
/// Céntimos residuales (contabilidad/procesos/centimos-residuales 2026-11-17 19:05), sin BD.
/// <list type="bullet">
/// <item>Entran las subcuentas de 7 dígitos de los grupos 1 a 5, salvo las 47 (Administraciones Públicas: las salda la liquidación) y
/// las 57 (bancos y caja: su saldo es real), cuyo saldo a 31/12, redondeado a céntimos, no es cero y es menor que el umbral en valor
/// absoluto.</item>
/// <item>Un asiento por subcuenta a 31/12 (propietario 2026-11-17 19:05): la deudora se abona contra la cuenta de gastos de redondeo
/// (Debe) y la acreedora se carga contra la de ingresos (Haber), sin compensar unas con otras.</item>
/// <item>El operador puede excluir subcuentas; se recuerda mientras sigan en la lista.</item>
/// </list>
/// </summary>
public static class CalculoCentimos
{
    public static DateTime Fecha(int ejercicio) => new(ejercicio, 12, 31);

    public static string Descripcion(int ejercicio) => $"Céntimos residuales {ejercicio}";

    public static string Concepto(string cuenta, string nombre)
    {
        var texto = $"Céntimos residuales · {cuenta} {nombre.Trim()}".TrimEnd();
        return texto.Length <= 255 ? texto : texto[..255];
    }

    public static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";

    /// <summary>Subcuenta que puede tener céntimos residuales: 7 dígitos, grupos 1 a 5, sin las 47 ni las 57 ni las cuentas de redondeo.</summary>
    public static bool EsCandidata(string cuenta, ConfiguracionCentimos cfg)
    {
        var c = (cuenta ?? "").Trim();
        return c.Length == 7 && c.All(char.IsAsciiDigit) && c[0] is >= '1' and <= '5'
            && !c.StartsWith("47", StringComparison.Ordinal) && !c.StartsWith("57", StringComparison.Ordinal)
            && c != cfg.CuentaGastos && c != cfg.CuentaIngresos;
    }

    /// <summary>Si un saldo es un céntimo residual: redondeado a céntimos, distinto de cero y menor que el umbral en valor absoluto.</summary>
    public static bool EsResidual(decimal saldo, decimal umbral)
    {
        var s = Math.Round(saldo, 2, MidpointRounding.AwayFromZero);
        return s != 0m && Math.Abs(s) < umbral;
    }

    public static PropuestaCentimos Calcular(int ejercicio, ConfiguracionCentimos cfg, IEnumerable<SaldoSubcuenta> saldos, IReadOnlySet<string> excluidas,
        IEnumerable<string>? avisos = null)
    {
        var filas = saldos
            .Where(s => EsCandidata(s.Cuenta, cfg) && EsResidual(s.Saldo, cfg.Umbral))
            .GroupBy(s => s.Cuenta.Trim(), StringComparer.Ordinal)
            .Select(g => (Cuenta: g.Key, Nombre: g.First().Nombre, Saldo: Math.Round(g.Sum(x => x.Saldo), 2, MidpointRounding.AwayFromZero)))
            .Where(s => s.Saldo != 0m)
            .OrderBy(s => s.Cuenta, StringComparer.Ordinal)
            .Select(s => new FilaCentimos(s.Cuenta, s.Nombre, s.Saldo, s.Saldo > 0m ? cfg.CuentaGastos : cfg.CuentaIngresos, !excluidas.Contains(s.Cuenta)))
            .ToList();
        var lista = new List<string>(avisos ?? []);
        return new PropuestaCentimos(ejercicio, Fecha(ejercicio), cfg, filas, Asientos(filas), lista);
    }

    /// <summary>La misma propuesta con otras casillas (sin volver a leer nada): recalcula los asientos.</summary>
    public static PropuestaCentimos Aplicar(PropuestaCentimos p, IReadOnlySet<string> excluidas)
    {
        var filas = p.Filas.Select(f => f with { Incluido = !excluidas.Contains(f.Cuenta) }).ToList();
        return p with { Filas = filas, Asientos = Asientos(filas) };
    }

    /// <summary>Un asiento por subcuenta incluida: siempre el Debe primero.</summary>
    private static List<AsientoCentimos> Asientos(IEnumerable<FilaCentimos> filas) => filas.Where(f => f.Incluido).Select(f =>
    {
        var concepto = Concepto(f.Cuenta, f.Nombre);
        IReadOnlyList<LineaCentimos> lineas = f.Deudora
            ? [new(f.Contrapartida, f.Importe, 0m, concepto), new(f.Cuenta, 0m, f.Importe, concepto)]
            : [new(f.Cuenta, f.Importe, 0m, concepto), new(f.Contrapartida, 0m, f.Importe, concepto)];
        return new AsientoCentimos(f.Cuenta, lineas);
    }).ToList();
}
