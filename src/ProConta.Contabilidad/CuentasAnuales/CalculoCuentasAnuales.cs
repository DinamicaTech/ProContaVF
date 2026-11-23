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
public sealed record CifrasEjercicio(int Ejercicio, IReadOnlyDictionary<string, ImportePartida> Partidas, IReadOnlyList<SubcuentaSinPartida> SinPartida, bool Regularizado)
{
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
            var partida = ModeloPymes.PorClave[regla.Clave];
            var importe = partida.Estado == EstadoCuentas.Activo ? saldo : -saldo;
            Anadir(cuentas, regla.Clave, new CuentaEnPartida(s.Cuenta, s.Nombre, importe));
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
        return new CifrasEjercicio(ejercicio, partidas, sin.OrderBy(x => x.Cuenta, StringComparer.Ordinal).ToList(), regularizado);
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
                e.Regularizado
                    ? "La regularización no lleva a la 129 el saldo de todas las cuentas 6 y 7: rehaz el cierre del ejercicio (reabrir y cerrar) en Fin de ejercicio › Cierre del ejercicio."
                    : "Hay subcuentas de los grupos 6 o 7 sin clasificar: se listan aquí."));
        else
            c.Add(new(GravedadComprobacion.Correcta, $"{anio}: el resultado de pérdidas y ganancias es el del balance", Euros(e.Resultado)));
        foreach (var s in e.SinPartida)
            c.Add(new(GravedadComprobacion.Error, $"{anio}: la subcuenta {s.Cuenta} no tiene partida en el modelo",
                $"{s.Cuenta} {s.Nombre} tiene un saldo {(s.SaldoDeudor >= 0 ? "deudor" : "acreedor")} de {Euros(Math.Abs(s.SaldoDeudor))} y la correspondencia oficial del PGC de Pymes no la lleva a ninguna partida.",
                "Traspasa su saldo a una cuenta del PGC de Pymes con un asiento manual (o corrige sus apuntes) y vuelve a generar las cuentas."));
        // Signos: cada partida del balance tiene su signo; uno al revés suele ser un saldo que va en el otro lado (p. ej. un banco en descubierto).
        foreach (var p in ModeloPymes.Activo.Concat(ModeloPymes.PatrimonioNetoYPasivo).Where(p => !p.EsTotal && !AmbosSignos.Contains(p.Clave)))
        {
            var v = e[p.Clave];
            var mal = SiempreNegativas.Contains(p.Clave) ? v > 0m : v < 0m;
            if (!mal) continue;
            var culpables = e.Partidas[p.Clave].Cuentas.Where(x => SiempreNegativas.Contains(p.Clave) ? x.Importe > 0m : x.Importe < 0m).Take(5)
                .Select(x => $"{x.Cuenta} {x.Nombre} ({Euros(x.Importe)})");
            c.Add(new(GravedadComprobacion.Aviso, $"{anio}: «{p.Texto}» sale {(v < 0m ? "en negativo" : "en positivo")}",
                $"{Euros(v)}. Subcuentas: {string.Join("; ", culpables)}.",
                p.Clave == "12700"
                    ? "Un banco con saldo acreedor (en descubierto) es una deuda con entidades de crédito: traspásalo a una 5201 al 31/12 si quieres que salga en el pasivo."
                    : "Revisa esas subcuentas: un saldo al revés suele ir en otra partida o ser un error de apunte. El Registro lo admite, pero conviene explicarlo en la memoria."));
        }
    }
}
