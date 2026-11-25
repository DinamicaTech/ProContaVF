using System.Globalization;

namespace ProConta.Contabilidad.CuentasAnuales;

/// <summary>Sumas de una subcuenta en los apuntes que entran en un estado (Debe y Haber), con su nombre.</summary>
public sealed record SaldoSubcuenta(string Cuenta, string Nombre, decimal Debe, decimal Haber)
{
    public decimal SaldoDeudor => Debe - Haber;
}

/// <summary>Una subcuenta dentro de una partida, con el importe que aporta (con el signo del estado).</summary>
public sealed record CuentaEnPartida(string Cuenta, string Nombre, decimal Importe);

/// <summary>Importe de una partida en un ejercicio y las subcuentas que lo forman (vacío en los totales).</summary>
public sealed record ImportePartida(string Clave, decimal Importe, IReadOnlyList<CuentaEnPartida> Cuentas);

/// <summary>Subcuenta con saldo que la correspondencia oficial no sabe a qué partida llevar: no entra y se avisa.</summary>
public sealed record SubcuentaSinPartida(string Cuenta, string Nombre, decimal SaldoDeudor, bool EsPyG);

/// <summary>Las cifras de un ejercicio: el balance y la cuenta de pérdidas y ganancias por clave, y lo que no se ha podido clasificar.</summary>
/// <summary>Subcuenta con el saldo al revés que se presenta en otra partida: de <paramref name="Desde"/> a <paramref name="Hacia"/>, por su importe.</summary>
public sealed record SubcuentaReclasificada(string Cuenta, string Nombre, string Desde, string Hacia, decimal Importe);

/// <summary>Movimiento de una subcuenta en el ejercicio para la memoria: saldo de la apertura (Debe − Haber) y sumas del resto de asientos ordinarios.</summary>
public sealed record MovimientoSubcuenta(string Cuenta, decimal Apertura, decimal Debe, decimal Haber)
{
    public decimal Final => Apertura + Debe - Haber;
}

public sealed record CifrasEjercicio(int Ejercicio, IReadOnlyDictionary<string, ImportePartida> Partidas, IReadOnlyList<SubcuentaSinPartida> SinPartida, bool Regularizado)
{
    /// <summary>Subcuentas con el saldo al revés que se presentan en la partida del otro lado del balance.</summary>
    public IReadOnlyList<SubcuentaReclasificada> Reclasificadas { get; init; } = [];

    /// <summary>Movimientos por subcuenta (apertura y asientos ordinarios), para las tablas de la memoria.</summary>
    public IReadOnlyDictionary<string, MovimientoSubcuenta> Movimientos { get; init; } = new Dictionary<string, MovimientoSubcuenta>();

    public decimal this[string clave] => Partidas.TryGetValue(clave, out var p) ? p.Importe : 0m;

    public decimal TotalActivo => this["10000"];
    public decimal TotalPatrimonioNetoYPasivo => this["30000"];
    public decimal Resultado => this["49500"];
}

public enum GravedadComprobacion
{
    Correcta,
    Aviso,
    Error,
}

/// <summary>
/// Una comprobación de las cuentas antes de presentarlas: las del Registro (tests de errores del modelo pymes del BOE, «PBL_…») y las propias.
/// <paramref name="QueHacer"/> dice cómo resolverla.
/// </summary>
public sealed record ComprobacionCuentas(GravedadComprobacion Gravedad, string Titulo, string Detalle, string? QueHacer = null);

/// <summary>Una fila de un estado con los dos ejercicios, en el orden del modelo.</summary>
public sealed record FilaCuentasAnuales(PartidaModelo Partida, decimal Actual, decimal Anterior, IReadOnlyList<CuentaEnPartida> CuentasActual, IReadOnlyList<CuentaEnPartida> CuentasAnterior)
{
    public bool TieneImporte => Actual != 0m || Anterior != 0m;
}

/// <summary>Las cuentas anuales del modelo pymes de un ejercicio, con la columna del ejercicio anterior, y sus comprobaciones.</summary>
public sealed record CuentasAnualesPymes(
    string RazonSocial, string Nif, CifrasEjercicio Actual, CifrasEjercicio? Anterior, IReadOnlyList<ComprobacionCuentas> Comprobaciones)
{
    public int Ejercicio => Actual.Ejercicio;

    public IReadOnlyList<FilaCuentasAnuales> Filas(IReadOnlyList<PartidaModelo> estado) => estado.Select(p =>
    {
        var a = Actual.Partidas.GetValueOrDefault(p.Clave);
        var b = Anterior?.Partidas.GetValueOrDefault(p.Clave);
        return new FilaCuentasAnuales(p, a?.Importe ?? 0m, b?.Importe ?? 0m, a?.Cuentas ?? [], b?.Cuentas ?? []);
    }).ToList();

    public IReadOnlyList<FilaCuentasAnuales> Activo => Filas(ModeloPymes.Activo);
    public IReadOnlyList<FilaCuentasAnuales> PatrimonioNetoYPasivo => Filas(ModeloPymes.PatrimonioNetoYPasivo);
    public IReadOnlyList<FilaCuentasAnuales> PerdidasYGanancias => Filas(ModeloPymes.PerdidasYGanancias);

    /// <summary>Hay algún error que impide presentarlas (los avisos no lo impiden).</summary>
    public bool TieneErrores => Comprobaciones.Any(c => c.Gravedad == GravedadComprobacion.Error);
}

/// <summary>
/// Cálculo sin BD de las cuentas anuales del modelo pymes (contabilidad/procesos/cuentas-anuales): cada subcuenta va a su partida por la
/// correspondencia oficial (ModeloPymes), los totales se suman con las fórmulas del modelo y se aplican las comprobaciones del Registro.
/// Los importes llegan en céntimos; las sumas se redondean a dos decimales (al alza en el medio céntimo, como piden las instrucciones del modelo).
/// </summary>
public static class CalculoCuentasAnuales
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Euros(decimal v) => v.ToString("#,##0.00", Es) + " €";

    /// <param name="balance">Sumas por subcuenta de los apuntes contabilizados del ejercicio salvo el asiento de cierre (apertura incluida).</param>
    /// <param name="perdidasYGanancias">Sumas por subcuenta de los grupos 6 y 7 sin la regularización ni el cierre.</param>
    /// <param name="regularizado">El ejercicio tiene ya la regularización (el resultado está en la 129).</param>
    public static CifrasEjercicio Cifras(int ejercicio, IEnumerable<SaldoSubcuenta> balance, IEnumerable<SaldoSubcuenta> perdidasYGanancias, bool regularizado)
    {
        var cuentas = new Dictionary<string, List<CuentaEnPartida>>(StringComparer.Ordinal);
        var sin = new List<SubcuentaSinPartida>();
        var reclasificadas = new List<SubcuentaReclasificada>();

        // Primero cada subcuenta a su partida; después, solo en las partidas que en neto quedan con el signo que el modelo no admite, sus
        // subcuentas de deudores y acreedores con el saldo al revés pasan al otro lado (como hizo la gestoría con 2025: compensa dentro de la partida).
        var clasificadas = new List<(SaldoSubcuenta S, decimal Saldo, string Clave)>();
        foreach (var s in balance)
        {
            var saldo = Math.Round(s.SaldoDeudor, 2, MidpointRounding.AwayFromZero);
            if (saldo == 0m) continue;
            var regla = ModeloPymes.Regla(ModeloPymes.CorrespondenciaBalance, s.Cuenta, saldo);
            if (regla is null)
            {
                sin.Add(new SubcuentaSinPartida(s.Cuenta, s.Nombre, saldo, false));
                continue;
            }
            clasificadas.Add((s, saldo, regla.Clave));
        }
        var netas = clasificadas.GroupBy(x => x.Clave).ToDictionary(g => g.Key,
            g => g.Sum(x => ModeloPymes.PorClave[g.Key].Estado == EstadoCuentas.Activo ? x.Saldo : -x.Saldo), StringComparer.Ordinal);
        foreach (var (s, saldo, regla) in clasificadas)
        {
            var clave = regla;
            if (SignoAlReves(clave, netas[clave]) && Reclasificacion(clave, s.Cuenta, saldo) is { } hacia)
            {
                reclasificadas.Add(new SubcuentaReclasificada(s.Cuenta, s.Nombre, clave, hacia, Math.Abs(saldo)));
                clave = hacia;
            }
            var partida = ModeloPymes.PorClave[clave];
            var importe = partida.Estado == EstadoCuentas.Activo ? saldo : -saldo;
            Anadir(cuentas, clave, new CuentaEnPartida(s.Cuenta, s.Nombre, importe));
        }
        foreach (var s in perdidasYGanancias)
        {
            var saldo = Math.Round(s.SaldoDeudor, 2, MidpointRounding.AwayFromZero);
            if (saldo == 0m) continue;
            var regla = ModeloPymes.Regla(ModeloPymes.CorrespondenciaPyG, s.Cuenta, saldo);
            if (regla is null)
            {
                sin.Add(new SubcuentaSinPartida(s.Cuenta, s.Nombre, saldo, true));
                continue;
            }
            Anadir(cuentas, regla.Clave, new CuentaEnPartida(s.Cuenta, s.Nombre, -saldo));
        }

        var importes = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (clave, lista) in cuentas) importes[clave] = lista.Sum(c => c.Importe);
        // Los totales, de las hojas a la raíz: cada estado se recorre al revés para que los sumandos estén calculados antes que su total.
        foreach (var estado in new[] { ModeloPymes.Activo, ModeloPymes.PatrimonioNetoYPasivo, ModeloPymes.PerdidasYGanancias })
            foreach (var p in estado.Reverse().Where(p => p.EsTotal))
                importes[p.Clave] = Total(p, importes);

        var partidas = ModeloPymes.PorClave.Values.ToDictionary(p => p.Clave,
            p => new ImportePartida(p.Clave, Math.Round(importes.GetValueOrDefault(p.Clave), 2, MidpointRounding.AwayFromZero),
                cuentas.TryGetValue(p.Clave, out var l) ? l.OrderBy(c => c.Cuenta, StringComparer.Ordinal).ToList() : []),
            StringComparer.Ordinal);
        return new CifrasEjercicio(ejercicio, partidas, sin.OrderBy(x => x.Cuenta, StringComparer.Ordinal).ToList(), regularizado)
        {
            Reclasificadas = reclasificadas.OrderBy(x => x.Cuenta, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>La partida del balance con ese importe tiene el signo que la taxonomía del ICAC no admite.</summary>
    public static bool SignoAlReves(string clave, decimal importe) =>
        ModeloPymes.PorClave[clave].Estado != EstadoCuentas.PerdidasYGanancias && !ModeloPymes.PorClave[clave].EsTotal && !AmbosSignos.Contains(clave)
        && (SiempreNegativas.Contains(clave) ? importe > 0m : importe < 0m);

    /// <summary>
    /// Saldos al revés de deudores y acreedores (cuentas de los grupos 4 y 5, sin las correctoras): una subcuenta que la correspondencia lleva al
    /// activo con saldo acreedor se presenta en el pasivo, y una del pasivo con saldo deudor, en el activo, cuando su partida queda en neto con el
    /// signo que el modelo no admite (si no, se compensa dentro de la partida, como hace la gestoría) (un cliente con
    /// anticipo es un acreedor; un banco en descubierto, una deuda con entidades de crédito). Así ninguna partida de la taxonomía del ICAC que
    /// solo admite importes positivos queda en negativo. Devuelve la partida de destino, o null si se queda donde está.
    /// </summary>
    public static string? Reclasificacion(string clave, string cuenta, decimal saldoDeudor)
    {
        if (cuenta.Length == 0 || cuenta[0] is not ('4' or '5') || saldoDeudor == 0m) return null;
        if (cuenta.StartsWith("437", StringComparison.Ordinal) || cuenta.StartsWith("49", StringComparison.Ordinal) || cuenta.StartsWith("59", StringComparison.Ordinal))
            return null;
        var activo = ModeloPymes.PorClave[clave].Estado == EstadoCuentas.Activo;
        if (activo && saldoDeudor < 0m)
            return clave switch
            {
                "12700" => "32320",
                "12400" or "12500" => "32390",
                _ => "32590",
            };
        if (!activo && saldoDeudor > 0m && !AmbosSignos.Contains(clave) && !SiempreNegativas.Contains(clave))
            return clave switch
            {
                "32320" or "32330" or "32390" or "32400" or "32700" => "12500",
                "21800" => null,
                _ => "12390",
            };
        return null;
    }

    private static decimal Total(PartidaModelo p, Dictionary<string, decimal> importes)
    {
        var suma = 0m;
        foreach (var c in p.Sumandos)
        {
            if (!importes.ContainsKey(c) && ModeloPymes.PorClave[c].EsTotal) importes[c] = Total(ModeloPymes.PorClave[c], importes);
            suma += importes.GetValueOrDefault(c);
        }
        return suma;
    }

    private static void Anadir(Dictionary<string, List<CuentaEnPartida>> d, string clave, CuentaEnPartida c)
    {
        if (!d.TryGetValue(clave, out var l)) d[clave] = l = [];
        l.Add(c);
    }

    /// <summary>Partidas del balance cuyo importe tiene que ser positivo (las que el modelo no pone entre paréntesis ni admiten los dos signos).</summary>
    private static readonly HashSet<string> SiempreNegativas = new(["21120", "21400", "21800"], StringComparer.Ordinal);

    private static readonly HashSet<string> AmbosSignos = new(["21500", "21700", "22000", "20000", "21000", "21300", "21360"], StringComparer.Ordinal);

    /// <summary>Las cuentas anuales completas con sus comprobaciones.</summary>
    /// <param name="cerrado">El ejercicio está cerrado (si no, se avisa: las cifras aún pueden cambiar).</param>
    /// <param name="resultadoBalances">El resultado del ejercicio según la cuenta de pérdidas y ganancias de Balances (contabilidad/balances), para compararlo; null si no se calculó.</param>
    public static CuentasAnualesPymes Componer(string razonSocial, string nif, CifrasEjercicio actual, CifrasEjercicio? anterior, bool cerrado, decimal? resultadoBalances = null)
    {
        var c = new List<ComprobacionCuentas>();
        if (!cerrado)
            c.Add(new(GravedadComprobacion.Aviso, $"El ejercicio {actual.Ejercicio} no está cerrado",
                "Las cuentas anuales se formulan con el ejercicio cerrado: hasta entonces las cifras pueden cambiar.",
                "Cierra el ejercicio en Fin de ejercicio › Cierre del ejercicio y vuelve a generarlas."));
        foreach (var e in anterior is null ? new[] { actual } : new[] { actual, anterior })
            Comprobar(c, e);
        if (anterior is null)
            c.Add(new(GravedadComprobacion.Aviso, $"Sin datos de {actual.Ejercicio - 1}",
                $"No hay ejercicio {actual.Ejercicio - 1} de esta sociedad en la BD: la columna del ejercicio anterior sale vacía.",
                "Si la sociedad ya existía, importa ese ejercicio (Configuración › Importar ContaSol) antes de generar las cuentas."));
        if (resultadoBalances is decimal rb && rb != actual.Resultado)
            c.Add(new(GravedadComprobacion.Aviso, "El resultado no coincide con el de Balances",
                $"La cuenta de pérdidas y ganancias del modelo da {Euros(actual.Resultado)} y la de Informes › Pérdidas y ganancias {Euros(rb)}.",
                "Revisa las subcuentas sin clasificar de los grupos 6 y 7 que se listan aquí."));
        return new CuentasAnualesPymes(razonSocial, nif, actual, anterior, c);
    }

    /// <summary>Saldo acreedor de la 129 en la apertura (el resultado del ejercicio anterior aún no aplicado); null si no hay movimientos.</summary>
    private static decimal? SinAplicar(CifrasEjercicio e) =>
        e.Movimientos.Count == 0 ? null : -e.Movimientos.Values.Where(m => m.Cuenta.StartsWith("129", StringComparison.Ordinal)).Sum(m => m.Apertura + m.Debe);

    private static void Comprobar(List<ComprobacionCuentas> c, CifrasEjercicio e)
    {
        var anio = e.Ejercicio;
        // PBL_001/002: activo igual a patrimonio neto y pasivo.
        if (e.TotalActivo != e.TotalPatrimonioNetoYPasivo)
            c.Add(new(GravedadComprobacion.Error, $"{anio}: el activo no es igual al patrimonio neto y pasivo",
                $"Total activo {Euros(e.TotalActivo)}; total patrimonio neto y pasivo {Euros(e.TotalPatrimonioNetoYPasivo)}; diferencia {Euros(e.TotalActivo - e.TotalPatrimonioNetoYPasivo)}.",
                e.SinPartida.Any(s => !s.EsPyG)
                    ? "La diferencia viene de las subcuentas sin clasificar que se listan aquí: dales una cuenta del PGC de Pymes o corrige sus apuntes."
                    : "Comprueba en Sumas y saldos que el ejercicio cuadra (Debe igual a Haber)."));
        else
            c.Add(new(GravedadComprobacion.Correcta, $"{anio}: el activo es igual al patrimonio neto y pasivo", Euros(e.TotalActivo)));
        // PBL_007/008: el resultado de la cuenta de pérdidas y ganancias es el del balance (VII. Resultado del ejercicio).
        if (e["21700"] != e.Resultado)
            c.Add(new(GravedadComprobacion.Error, $"{anio}: el resultado de pérdidas y ganancias no es el del balance",
                $"Pérdidas y ganancias {Euros(e.Resultado)}; balance, VII. Resultado del ejercicio {Euros(e["21700"])}.",
                SinAplicar(e) is decimal sa && sa != 0m && sa == e["21700"] - e.Resultado
                    ? $"El resultado de {anio - 1} ({Euros(sa)}) sigue en la 129 desde la apertura, sin aplicar: aplícalo en {anio} › Fin de ejercicio › Aplicación del resultado y vuelve a generar las cuentas."
                    : e.Regularizado
                    ? "La regularización no lleva a la 129 el saldo de todas las cuentas 6 y 7: rehaz el cierre del ejercicio (reabrir y cerrar) en Fin de ejercicio › Cierre del ejercicio."
                    : "Hay subcuentas de los grupos 6 o 7 sin clasificar: se listan aquí."));
        else
            c.Add(new(GravedadComprobacion.Correcta, $"{anio}: el resultado de pérdidas y ganancias es el del balance", Euros(e.Resultado)));
        foreach (var s in e.SinPartida)
            c.Add(new(GravedadComprobacion.Error, $"{anio}: la subcuenta {s.Cuenta} no tiene partida en el modelo",
                $"{s.Cuenta} {s.Nombre} tiene un saldo {(s.SaldoDeudor >= 0 ? "deudor" : "acreedor")} de {Euros(Math.Abs(s.SaldoDeudor))} y la correspondencia oficial del PGC de Pymes no la lleva a ninguna partida.",
                "Traspasa su saldo a una cuenta del PGC de Pymes con un asiento manual (o corrige sus apuntes) y vuelve a generar las cuentas."));
        // Signos: la taxonomía del ICAC solo admite importes positivos (o negativos, las partidas entre paréntesis) en cada partida del balance que no
        // sea un resultado o un total de patrimonio neto. Los saldos al revés de deudores y acreedores ya se presentan en el otro lado (Reclasificacion).
        foreach (var p in ModeloPymes.Activo.Concat(ModeloPymes.PatrimonioNetoYPasivo).Where(p => !p.EsTotal && !AmbosSignos.Contains(p.Clave)))
        {
            var v = e[p.Clave];
            var mal = SiempreNegativas.Contains(p.Clave) ? v > 0m : v < 0m;
            if (!mal) continue;
            var culpables = e.Partidas[p.Clave].Cuentas.Where(x => SiempreNegativas.Contains(p.Clave) ? x.Importe > 0m : x.Importe < 0m).Take(5)
                .Select(x => $"{x.Cuenta} {x.Nombre} ({Euros(x.Importe)})");
            c.Add(new(GravedadComprobacion.Error, $"{anio}: «{p.Texto}» sale {(v < 0m ? "en negativo" : "en positivo")}",
                $"{Euros(v)}. Subcuentas: {string.Join("; ", culpables)}. El programa D2 del Registro no admite esta partida con ese signo.",
                "Revisa esas subcuentas: suele ser un error de apunte o una corrección de valor mayor que el bien que corrige. Corrige los apuntes y vuelve a generar las cuentas."));
        }
        if (e.Reclasificadas.Count > 0)
            c.Add(new(GravedadComprobacion.Aviso, $"{anio}: {e.Reclasificadas.Count} {(e.Reclasificadas.Count == 1 ? "subcuenta con el saldo al revés se presenta" : "subcuentas con el saldo al revés se presentan")} en el otro lado del balance",
                string.Join("; ", e.Reclasificadas.Take(8).Select(r => $"{r.Cuenta} {r.Nombre} ({Euros(r.Importe)}): de «{ModeloPymes.PorClave[r.Desde].Texto}» a «{ModeloPymes.PorClave[r.Hacia].Texto}»")) +
                (e.Reclasificadas.Count > 8 ? $" y {e.Reclasificadas.Count - 8} más." : "."),
                "Es lo que pide el PGC (un cliente con anticipo es un acreedor; un socio que debe dinero, un deudor). Si alguna es un error de apunte, corrígelo; si no, no hay que hacer nada."));
    }
}
