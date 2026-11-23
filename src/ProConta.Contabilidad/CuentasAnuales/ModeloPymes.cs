namespace ProConta.Contabilidad.CuentasAnuales;

/// <summary>Estado de las cuentas anuales al que pertenece una partida.</summary>
public enum EstadoCuentas
{
    Activo,
    PatrimonioNetoYPasivo,
    PerdidasYGanancias,
}

/// <summary>
/// Partida del modelo oficial de cuentas anuales PYMES para el depósito en el Registro Mercantil (BOE 03/06/2025, Resolución de la DGSJFP
/// de 26/05/2025, anexo I: páginas BP2.1, BP2.2 y PP). <paramref name="Clave"/> es la clave oficial del ejercicio (la del ejercicio anterior
/// es la misma con un 9 al final, p. ej. 11000 → 110009). <paramref name="Sumandos"/> son las claves que la suman (vacío: partida de cuentas).
/// <paramref name="Nivel"/> es la sangría de la presentación (0 total, 1 letra, 2 romano, 3 número, 4 letra minúscula).
/// </summary>
public sealed record PartidaModelo(EstadoCuentas Estado, string Clave, string Ordinal, string Nombre, int Nivel, IReadOnlyList<string> Sumandos)
{
    public bool EsTotal => Sumandos.Count > 0;

    /// <summary>La clave del ejercicio anterior (la del ejercicio con un 9 al final).</summary>
    public string ClaveAnterior => Clave + "9";

    /// <summary>El texto de la partida como lo escribe el modelo: «A) ACTIVO NO CORRIENTE», «1. Capital escriturado».</summary>
    public string Texto => Ordinal.Length == 0 ? Nombre : $"{Ordinal} {Nombre}";
}

/// <summary>
/// Regla de correspondencia entre cuentas del PGC de Pymes y partidas del modelo (anexo I, «Correspondencia entre los conceptos del balance
/// de pymes y el PGC de pymes» y su igual de pérdidas y ganancias). Una subcuenta va a la partida de la regla con el prefijo más largo que la
/// empieza. <paramref name="SoloSaldo"/>: la regla solo vale con ese signo del saldo de la subcuenta (las cuentas que el modelo pone en el
/// activo si son deudoras y en el pasivo si son acreedoras, como la 551 o la 5525).
/// </summary>
public sealed record ReglaCorrespondencia(string Prefijo, string Clave, SignoSaldo SoloSaldo = SignoSaldo.Cualquiera);

public enum SignoSaldo
{
    Cualquiera,
    Deudor,
    Acreedor,
}

/// <summary>
/// El modelo PYMES de cuentas anuales para el Registro Mercantil (contabilidad/procesos/cuentas-anuales): partidas, claves, sumas y la
/// correspondencia oficial con las cuentas del PGC de Pymes, tal como los publica el BOE de 03/06/2025 (BOE-A-2025-11004).
/// </summary>
public static class ModeloPymes
{
    private static PartidaModelo P(EstadoCuentas e, string clave, string ordinal, string nombre, int nivel, params string[] sumandos) =>
        new(e, clave, ordinal, nombre, nivel, sumandos);

    private const EstadoCuentas A = EstadoCuentas.Activo;
    private const EstadoCuentas Pn = EstadoCuentas.PatrimonioNetoYPasivo;
    private const EstadoCuentas Pg = EstadoCuentas.PerdidasYGanancias;

    /// <summary>Balance de pymes, activo (página BP1), en el orden del modelo.</summary>
    public static readonly IReadOnlyList<PartidaModelo> Activo =
    [
        P(A, "11000", "A)", "ACTIVO NO CORRIENTE", 1, "11100", "11200", "11300", "11400", "11500", "11600", "11700"),
        P(A, "11100", "I.", "Inmovilizado intangible", 2),
        P(A, "11200", "II.", "Inmovilizado material", 2),
        P(A, "11300", "III.", "Inversiones inmobiliarias", 2),
        P(A, "11400", "IV.", "Inversiones en empresas del grupo y asociadas a largo plazo", 2),
        P(A, "11500", "V.", "Inversiones financieras a largo plazo", 2),
        P(A, "11600", "VI.", "Activos por impuesto diferido", 2),
        P(A, "11700", "VII.", "Deudores comerciales no corrientes", 2),
        P(A, "12000", "B)", "ACTIVO CORRIENTE", 1, "12200", "12300", "12400", "12500", "12600", "12700"),
        P(A, "12200", "I.", "Existencias", 2),
        P(A, "12300", "II.", "Deudores comerciales y otras cuentas a cobrar", 2, "12380", "12370", "12390"),
        P(A, "12380", "1.", "Clientes por ventas y prestaciones de servicios", 3, "12381", "12382"),
        P(A, "12381", "a)", "Clientes por ventas y prestaciones de servicios a largo plazo", 4),
        P(A, "12382", "b)", "Clientes por ventas y prestaciones de servicios a corto plazo", 4),
        P(A, "12370", "2.", "Accionistas (socios) por desembolsos exigidos", 3),
        P(A, "12390", "3.", "Otros deudores", 3),
        P(A, "12400", "III.", "Inversiones en empresas del grupo y asociadas a corto plazo", 2),
        P(A, "12500", "IV.", "Inversiones financieras a corto plazo", 2),
        P(A, "12600", "V.", "Periodificaciones a corto plazo", 2),
        P(A, "12700", "VI.", "Efectivo y otros activos líquidos equivalentes", 2),
        P(A, "10000", "", "TOTAL ACTIVO (A + B)", 0, "11000", "12000"),
    ];

    /// <summary>Balance de pymes, patrimonio neto y pasivo (páginas BP2.1 y BP2.2), en el orden del modelo.</summary>
    public static readonly IReadOnlyList<PartidaModelo> PatrimonioNetoYPasivo =
    [
        P(Pn, "20000", "A)", "PATRIMONIO NETO", 1, "21000", "22000", "23000"),
        P(Pn, "21000", "A-1)", "Fondos propios", 2, "21100", "21200", "21300", "21400", "21500", "21600", "21700", "21800"),
        P(Pn, "21100", "I.", "Capital", 2, "21110", "21120"),
        P(Pn, "21110", "1.", "Capital escriturado", 3),
        P(Pn, "21120", "2.", "(Capital no exigido)", 3),
        P(Pn, "21200", "II.", "Prima de emisión", 2),
        P(Pn, "21300", "III.", "Reservas", 2, "21350", "21360"),
        P(Pn, "21350", "1.", "Reserva de capitalización", 3),
        P(Pn, "21360", "2.", "Otras reservas", 3),
        P(Pn, "21400", "IV.", "(Acciones y participaciones en patrimonio propias)", 2),
        P(Pn, "21500", "V.", "Resultados de ejercicios anteriores", 2),
        P(Pn, "21600", "VI.", "Otras aportaciones de socios", 2),
        P(Pn, "21700", "VII.", "Resultado del ejercicio", 2),
        P(Pn, "21800", "VIII.", "(Dividendo a cuenta)", 2),
        P(Pn, "22000", "A-2)", "Ajustes en patrimonio neto", 2),
        P(Pn, "23000", "A-3)", "Subvenciones, donaciones y legados recibidos", 2),
        P(Pn, "31000", "B)", "PASIVO NO CORRIENTE", 1, "31100", "31200", "31300", "31400", "31500", "31600", "31700"),
        P(Pn, "31100", "I.", "Provisiones a largo plazo", 2),
        P(Pn, "31200", "II.", "Deudas a largo plazo", 2, "31220", "31230", "31290"),
        P(Pn, "31220", "1.", "Deudas con entidades de crédito", 3),
        P(Pn, "31230", "2.", "Acreedores por arrendamiento financiero", 3),
        P(Pn, "31290", "3.", "Otras deudas a largo plazo", 3),
        P(Pn, "31300", "III.", "Deudas con empresas del grupo y asociadas a largo plazo", 2),
        P(Pn, "31400", "IV.", "Pasivos por impuesto diferido", 2),
        P(Pn, "31500", "V.", "Periodificaciones a largo plazo", 2),
        P(Pn, "31600", "VI.", "Acreedores comerciales no corrientes", 2),
        P(Pn, "31700", "VII.", "Deuda con características especiales a largo plazo", 2),
        P(Pn, "32000", "C)", "PASIVO CORRIENTE", 1, "32200", "32300", "32400", "32500", "32600", "32700"),
        P(Pn, "32200", "I.", "Provisiones a corto plazo", 2),
        P(Pn, "32300", "II.", "Deudas a corto plazo", 2, "32320", "32330", "32390"),
        P(Pn, "32320", "1.", "Deudas con entidades de crédito", 3),
        P(Pn, "32330", "2.", "Acreedores por arrendamiento financiero", 3),
        P(Pn, "32390", "3.", "Otras deudas a corto plazo", 3),
        P(Pn, "32400", "III.", "Deudas con empresas del grupo y asociadas a corto plazo", 2),
        P(Pn, "32500", "IV.", "Acreedores comerciales y otras cuentas a pagar", 2, "32580", "32590"),
        P(Pn, "32580", "1.", "Proveedores", 3, "32581", "32582"),
        P(Pn, "32581", "a)", "Proveedores a largo plazo", 4),
        P(Pn, "32582", "b)", "Proveedores a corto plazo", 4),
        P(Pn, "32590", "2.", "Otros acreedores", 3),
        P(Pn, "32600", "V.", "Periodificaciones a corto plazo", 2),
        P(Pn, "32700", "VI.", "Deuda con características especiales a corto plazo", 2),
        P(Pn, "30000", "", "TOTAL PATRIMONIO NETO Y PASIVO (A + B + C)", 0, "20000", "31000", "32000"),
    ];

    /// <summary>Cuenta de pérdidas y ganancias de pymes (página PP), en el orden del modelo. Importes con signo: (Debe) negativo, Haber positivo.</summary>
    public static readonly IReadOnlyList<PartidaModelo> PerdidasYGanancias =
    [
        P(Pg, "40100", "1.", "Importe neto de la cifra de negocios", 2),
        P(Pg, "40200", "2.", "Variación de existencias de productos terminados y en curso de fabricación", 2),
        P(Pg, "40300", "3.", "Trabajos realizados por la empresa para su activo", 2),
        P(Pg, "40400", "4.", "Aprovisionamientos", 2),
        P(Pg, "40500", "5.", "Otros ingresos de explotación", 2),
        P(Pg, "40600", "6.", "Gastos de personal", 2),
        P(Pg, "40700", "7.", "Otros gastos de explotación", 2),
        P(Pg, "40800", "8.", "Amortización del inmovilizado", 2),
        P(Pg, "40900", "9.", "Imputación de subvenciones de inmovilizado no financiero y otras", 2),
        P(Pg, "41000", "10.", "Excesos de provisiones", 2),
        P(Pg, "41100", "11.", "Deterioro y resultado por enajenaciones del inmovilizado", 2),
        P(Pg, "41300", "12.", "Otros resultados", 2),
        P(Pg, "49100", "A)", "RESULTADO DE EXPLOTACIÓN (1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10 + 11 + 12)", 1,
            "40100", "40200", "40300", "40400", "40500", "40600", "40700", "40800", "40900", "41000", "41100", "41300"),
        P(Pg, "41400", "13.", "Ingresos financieros", 2, "41430", "41490"),
        P(Pg, "41430", "a)", "Imputación de subvenciones, donaciones y legados de carácter financiero", 4),
        P(Pg, "41490", "b)", "Otros ingresos financieros", 4),
        P(Pg, "41500", "14.", "Gastos financieros", 2),
        P(Pg, "41600", "15.", "Variación de valor razonable en instrumentos financieros", 2),
        P(Pg, "41700", "16.", "Diferencias de cambio", 2),
        P(Pg, "41800", "17.", "Deterioro y resultado por enajenaciones de instrumentos financieros", 2),
        P(Pg, "42100", "18.", "Otros ingresos y gastos de carácter financiero", 2, "42110", "42120", "42130"),
        P(Pg, "42110", "a)", "Incorporación al activo de gastos financieros", 4),
        P(Pg, "42120", "b)", "Ingresos financieros derivados de convenios de acreedores", 4),
        P(Pg, "42130", "c)", "Resto de ingresos y gastos", 4),
        P(Pg, "49200", "B)", "RESULTADO FINANCIERO (13 + 14 + 15 + 16 + 17 + 18)", 1, "41400", "41500", "41600", "41700", "41800", "42100"),
        P(Pg, "49300", "C)", "RESULTADO ANTES DE IMPUESTOS (A + B)", 1, "49100", "49200"),
        P(Pg, "41900", "19.", "Impuestos sobre beneficios", 2),
        P(Pg, "49500", "D)", "RESULTADO DEL EJERCICIO (C + 19)", 1, "49300", "41900"),
    ];

    /// <summary>Todas las partidas, por clave.</summary>
    public static readonly IReadOnlyDictionary<string, PartidaModelo> PorClave =
        Activo.Concat(PatrimonioNetoYPasivo).Concat(PerdidasYGanancias).ToDictionary(p => p.Clave, StringComparer.Ordinal);

    private static IEnumerable<ReglaCorrespondencia> R(string clave, string prefijos, SignoSaldo signo = SignoSaldo.Cualquiera) =>
        prefijos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => new ReglaCorrespondencia(p, clave, signo));

    /// <summary>
    /// Correspondencia del balance (activo y patrimonio neto y pasivo). Las cuentas que el modelo pone entre paréntesis restan solas, porque el
    /// importe es el saldo de la subcuenta con el signo de su lado (Debe − Haber en el activo, Haber − Debe en el patrimonio neto y el pasivo).
    /// Añadidos a la tabla oficial, documentados en el nodo: 473 en «Otros deudores» (la tabla de pymes no la cita) y los grupos 6 y 7 en
    /// «Resultado del ejercicio» mientras no se haya hecho la regularización.
    /// </summary>
    public static readonly IReadOnlyList<ReglaCorrespondencia> CorrespondenciaBalance =
    [
        // Activo (página «Correspondencia… balance de pymes», activo).
        .. R("11100", "20,280,290"),
        .. R("11200", "21,281,291,23"),
        .. R("11300", "22,282,292"),
        .. R("11400", "2403,2404,2413,2414,2423,2424,2493,2494,2933,2934,2943,2944,2953,2954"),
        .. R("11500", "2405,2415,2425,2495,250,251,252,253,254,255,258,259,26,2935,2945,2955,296,297,298"),
        .. R("11600", "474"),
        .. R("12200", "30,31,32,33,34,35,36,39,407"),
        .. R("12382", "430,431,432,433,434,435,436,437,490,493"),
        .. R("12370", "5580"),
        .. R("12390", "44,460,470,471,472,473,544"),
        .. R("12400", "5303,5304,5313,5314,5323,5324,5333,5334,5343,5344,5353,5354,5393,5394,5933,5934,5943,5944,5953,5954"),
        .. R("12400", "5523,5524", SignoSaldo.Deudor),
        .. R("12500", "5305,5315,5325,5335,5345,5355,5395,540,541,542,543,545,546,547,548,549,5590,565,566,5935,5945,5955,596,597,598"),
        .. R("12500", "551,5525", SignoSaldo.Deudor),
        .. R("12600", "480,567"),
        .. R("12700", "57"),
        // Patrimonio neto y pasivo.
        .. R("21110", "100,101,102"),
        .. R("21120", "1030,1040"),
        .. R("21200", "110"),
        .. R("21360", "112,113,114,119"),
        .. R("21400", "108,109"),
        .. R("21500", "120,121"),
        .. R("21600", "118"),
        .. R("21700", "129,6,7"),
        .. R("21800", "557"),
        .. R("23000", "130,131,132"),
        .. R("31100", "14"),
        .. R("31220", "1605,170"),
        .. R("31230", "1625,174"),
        .. R("31290", "1615,1635,171,172,173,175,176,177,179,180,185"),
        .. R("31300", "1603,1604,1613,1614,1623,1624,1633,1634"),
        .. R("31400", "479"),
        .. R("31500", "181"),
        .. R("32200", "499,529"),
        .. R("32320", "5105,520,527"),
        .. R("32330", "5125,524"),
        .. R("32390", "1034,1044,190,192,194,500,505,506,509,5115,5135,5145,521,522,523,525,526,528,555,5565,5566,5595,560,561"),
        .. R("32390", "551,5525", SignoSaldo.Acreedor),
        .. R("32400", "5103,5104,5113,5114,5123,5124,5133,5134,5143,5144,5563,5564"),
        .. R("32400", "5523,5524", SignoSaldo.Acreedor),
        .. R("32582", "400,401,403,404,405,406"),
        .. R("32590", "41,438,465,475,476,477"),
        .. R("32600", "485,568"),
        .. R("32700", "502,507"),
    ];

    /// <summary>Correspondencia de la cuenta de pérdidas y ganancias (grupos 6 y 7). Importe: Haber − Debe.</summary>
    public static readonly IReadOnlyList<ReglaCorrespondencia> CorrespondenciaPyG =
    [
        .. R("40100", "700,701,702,703,704,705,706,708,709"),
        .. R("40200", "6930,71,7930"),
        .. R("40300", "73"),
        .. R("40400", "600,601,602,606,607,608,609,61,6931,6932,6933,7931,7932,7933"),
        .. R("40500", "740,747,75"),
        .. R("40600", "64"),
        .. R("40700", "62,631,634,636,639,65,694,695,794,7954"),
        .. R("40800", "68"),
        .. R("40900", "746"),
        .. R("41000", "7951,7952,7955"),
        .. R("41100", "670,671,672,690,691,692,770,771,772,790,791,792"),
        .. R("41300", "678,778"),
        .. R("41490", "760,761,762,769"),
        .. R("41500", "660,661,662,664,665,669"),
        .. R("41600", "663,763"),
        .. R("41700", "668,768"),
        .. R("41800", "666,667,673,675,696,697,698,699,766,773,775,796,797,798,799"),
        .. R("41900", "6300,6301,633,638"),
    ];

    /// <summary>
    /// La partida de una subcuenta según la correspondencia: la regla de prefijo más largo cuyo signo admite el saldo; null si ninguna
    /// (la subcuenta se lista como sin clasificar).
    /// </summary>
    /// <param name="saldoDeudor">Saldo Debe − Haber de la subcuenta.</param>
    public static ReglaCorrespondencia? Regla(IReadOnlyList<ReglaCorrespondencia> reglas, string cuenta, decimal saldoDeudor)
    {
        ReglaCorrespondencia? mejor = null;
        foreach (var r in reglas)
        {
            if (!cuenta.StartsWith(r.Prefijo, StringComparison.Ordinal)) continue;
            if (r.SoloSaldo == SignoSaldo.Deudor && saldoDeudor < 0m) continue;
            if (r.SoloSaldo == SignoSaldo.Acreedor && saldoDeudor >= 0m) continue;
            if (mejor is null || r.Prefijo.Length > mejor.Prefijo.Length) mejor = r;
        }
        return mejor;
    }
}
