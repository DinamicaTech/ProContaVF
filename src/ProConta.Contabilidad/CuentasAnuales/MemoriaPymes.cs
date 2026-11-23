using System.Globalization;

namespace ProConta.Contabilidad.CuentasAnuales;

/// <summary>Una fila de una tabla de la memoria: el concepto y sus importes (null: celda vacía).</summary>
public sealed record FilaTablaMemoria(string Concepto, IReadOnlyList<decimal?> Importes, bool EsTotal = false);

/// <summary>Una tabla numérica de la memoria, calculada desde la contabilidad.</summary>
public sealed record TablaMemoria(string Titulo, IReadOnlyList<string> Columnas, IReadOnlyList<FilaTablaMemoria> Filas);

/// <summary>
/// Un apartado de la memoria: su número y título del modelo pymes, el texto (el guardado por el operador o, si no hay, el propuesto) y las
/// tablas que el programa calcula. <paramref name="Guardado"/> dice si el texto es el que escribió el operador.
/// </summary>
public sealed record ApartadoMemoria(string Numero, string Titulo, string Texto, string TextoPropuesto, bool Guardado, IReadOnlyList<TablaMemoria> Tablas)
{
    public string Encabezado => $"{Numero}. {Titulo}";
}

/// <summary>Dividendo repartido en el ejercicio a un socio (operación con parte vinculada).</summary>
public sealed record DividendoSocio(string Socio, DateTime Fecha, decimal Bruto, decimal Retencion);

/// <summary>La aplicación del resultado del ejercicio grabada en el siguiente (contabilidad/procesos/aplicacion-resultado).</summary>
public sealed record AplicacionMemoria(decimal Resultado, decimal Compensacion, decimal ReservaLegal, decimal Dividendos, decimal Voluntarias);

/// <summary>Datos de la sociedad que la memoria toma de fuera de los apuntes. Los que faltan dejan su frase o su tabla para escribirla a mano.</summary>
public sealed record DatosMemoria(
    string? Domicilio = null, long? Participaciones = null, decimal? ValorNominal = null, IReadOnlyList<DividendoSocio>? Dividendos = null,
    AplicacionMemoria? Aplicacion = null, decimal? PlantillaMedia = null);

/// <summary>
/// Memoria del modelo pymes (contabilidad/procesos/cuentas-anuales, propietario 2026-11-23 P2 a): los diez apartados del contenido de la memoria
/// de pymes (PGC de Pymes tras el RD 602/2016, como el modelo del BOE de 03/06/2025), cada uno con un texto propuesto que el operador corrige y
/// guarda por ejercicio, y las tablas numéricas que salen de la contabilidad (inmovilizado, activos y pasivos financieros, fondos propios,
/// situación fiscal, partes vinculadas, plantilla) y la aplicación del resultado. En el depósito va como fichero adjunto MEMORIA.PDF.
/// </summary>
public static class MemoriaPymes
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Número y título de los apartados, en orden. «0» es la aplicación del resultado, que el modelo pide en los datos generales.</summary>
    public static readonly IReadOnlyList<(string Numero, string Titulo)> Apartados =
    [
        ("1", "Actividad de la empresa"),
        ("2", "Bases de presentación de las cuentas anuales"),
        ("3", "Normas de registro y valoración"),
        ("4", "Inmovilizado material, intangible e inversiones inmobiliarias"),
        ("5", "Activos financieros"),
        ("6", "Pasivos financieros"),
        ("7", "Fondos propios"),
        ("8", "Situación fiscal"),
        ("9", "Operaciones con partes vinculadas"),
        ("10", "Otra información"),
        ("11", "Aplicación del resultado"),
    ];

    private static string E(decimal v) => v.ToString("#,##0.00", Es) + " euros";

    /// <summary>Compone la memoria con los textos guardados (por número de apartado) y, donde no hay, los propuestos.</summary>
    public static IReadOnlyList<ApartadoMemoria> Componer(CuentasAnualesPymes c, DatosMemoria d, IReadOnlyDictionary<string, string>? guardados = null)
    {
        var n = c.Ejercicio;
        var anterior = n - 1;
        var r = new List<ApartadoMemoria>();
        foreach (var (num, titulo) in Apartados)
        {
            var propuesto = TextoPropuesto(num, c, d);
            var tablas = Tablas(num, c, d);
            var g = guardados?.GetValueOrDefault(num);
            r.Add(new ApartadoMemoria(num, titulo, string.IsNullOrWhiteSpace(g) ? propuesto : g!, propuesto, !string.IsNullOrWhiteSpace(g), tablas));
        }
        return r;
    }

    /// <summary>El texto que se propone para un apartado; los datos que no se conocen quedan entre corchetes para completarlos.</summary>
    public static string TextoPropuesto(string numero, CuentasAnualesPymes c, DatosMemoria d)
    {
        var n = c.Ejercicio;
        return numero switch
        {
            "1" => $"{c.RazonSocial} (en adelante, la Sociedad), con NIF {c.Nif}, tiene su domicilio social en {d.Domicilio ?? "[domicilio social]"}. " +
                   "Está inscrita en el Registro Mercantil de [provincia], tomo [ ], folio [ ], hoja [ ].\n\n" +
                   "Su objeto social es [objeto social según los estatutos]. Su actividad principal en el ejercicio ha sido [actividad].",
            "2" => $"1. Imagen fiel. Las cuentas anuales del ejercicio {n} se han preparado a partir de los registros contables de la Sociedad, aplicando la " +
                   "legislación mercantil vigente y las normas del Plan General de Contabilidad de Pequeñas y Medianas Empresas (Real Decreto 1515/2007 y sus " +
                   "modificaciones), de forma que muestran la imagen fiel del patrimonio, de la situación financiera y de los resultados de la Sociedad. No ha habido " +
                   "razones excepcionales para dejar de aplicar ninguna disposición legal en materia contable.\n\n" +
                   "2. Principios contables no obligatorios aplicados. No se han aplicado.\n\n" +
                   "3. Aspectos críticos de la valoración y estimación de la incertidumbre. No hay incertidumbres importantes sobre la continuidad de la Sociedad: " +
                   "las cuentas se han formulado bajo el principio de empresa en funcionamiento.\n\n" +
                   $"4. Comparación de la información. Las cifras del ejercicio {n} se presentan junto a las de {n - 1}, que son comparables.\n\n" +
                   "5. Elementos recogidos en varias partidas. No hay.\n\n" +
                   "6. Cambios en criterios contables. No ha habido.\n\n" +
                   "7. Corrección de errores. No ha habido.",
            "3" => "1. Inmovilizado intangible y material e inversiones inmobiliarias. Se valoran por su precio de adquisición o coste de producción, menos la " +
                   "amortización acumulada y, en su caso, las pérdidas por deterioro. La amortización es lineal durante la vida útil estimada de cada elemento. " +
                   "Los inmuebles destinados al alquiler se clasifican como inversiones inmobiliarias; el valor del suelo no se amortiza.\n\n" +
                   "2. Instrumentos financieros. Los créditos y débitos por operaciones comerciales con vencimiento no superior a un año se valoran por su valor " +
                   "nominal. Las inversiones y deudas financieras se valoran por su coste amortizado o, cuando corresponde, a valor razonable con cambios en la " +
                   "cuenta de pérdidas y ganancias.\n\n" +
                   "3. Impuesto sobre beneficios. El gasto por impuesto es el impuesto corriente del ejercicio, calculado sobre la base imponible.\n\n" +
                   "4. Ingresos y gastos. Se registran por el principio de devengo, con independencia del momento del cobro o del pago. Los ingresos se " +
                   "registran por el valor de la contraprestación, sin el IVA.",
            "4" => "El movimiento del inmovilizado en el ejercicio se resume en la tabla siguiente. [Describe aquí las altas y bajas importantes del ejercicio.]",
            "5" => "Los activos financieros de la Sociedad, sin las inversiones en patrimonio de empresas del grupo ni los saldos con las Administraciones " +
                   "Públicas salvo que se indique, son los de la tabla siguiente. [Indica aquí si hay activos financieros a valor razonable o deteriorados.]",
            "6" => "Los pasivos financieros de la Sociedad son los de la tabla siguiente. [Indica aquí los vencimientos de las deudas a largo plazo y si " +
                   "alguna tiene garantía real.]",
            "7" => (d.Participaciones is long p && d.ValorNominal is decimal v
                       ? $"El capital social es de {E(p * v)}, representado por {p.ToString("N0", Es)} participaciones sociales de {E(v)} de valor nominal cada una, " +
                         "totalmente suscritas y desembolsadas."
                       : $"El capital social es de {E(c.Actual["21110"])}, representado por [número] participaciones sociales de [valor nominal] euros cada una, totalmente suscritas y desembolsadas.") +
                   " La reserva legal y el resto de reservas se muestran en el balance. [Añade aquí las restricciones a la disponibilidad de las reservas, si las hay.]",
            "8" => $"El gasto por Impuesto sobre Sociedades del ejercicio {n} asciende a {E(-c.Actual["41900"])}, sobre un resultado antes de impuestos de {E(c.Actual["49300"])}. " +
                   "[Indica aquí las diferencias entre el resultado contable y la base imponible, las bases imponibles negativas pendientes y los ejercicios abiertos a inspección.]",
            "9" => (d.Dividendos is { Count: > 0 } dv
                       ? $"En el ejercicio {n} se han repartido dividendos a los socios por {E(dv.Sum(x => x.Bruto))} brutos, según la tabla siguiente. "
                       : $"En el ejercicio {n} no se han repartido dividendos a los socios. ") +
                   "[Indica aquí la remuneración del órgano de administración, los anticipos y créditos concedidos a sus miembros y las obligaciones asumidas por cuenta de ellos.]",
            "10" => (d.PlantillaMedia is decimal pm
                        ? $"El número medio de personas empleadas en el ejercicio ha sido de {pm.ToString("N2", Es)}. "
                        : "El número medio de personas empleadas en el ejercicio ha sido de [número]. ") +
                    "No hay acuerdos de la Sociedad que no figuren en el balance ni hechos posteriores al cierre que afecten a las cuentas anuales.",
            "11" => d.Aplicacion is { } a
                ? a.Resultado >= 0m
                    ? $"La propuesta de aplicación del resultado del ejercicio {n}, un beneficio de {E(a.Resultado)}, es la de la tabla siguiente."
                    : $"El resultado del ejercicio {n}, una pérdida de {E(-a.Resultado)}, se aplica a resultados negativos de ejercicios anteriores."
                : c.Actual.Resultado >= 0m
                    ? $"La propuesta de aplicación del resultado del ejercicio {n}, un beneficio de {E(c.Actual.Resultado)}, es: [a reserva legal, a reservas voluntarias, a dividendos]. " +
                      $"(Aún no se ha grabado en {n + 1} › Fin de ejercicio › Aplicación del resultado: cuando se grabe, la tabla saldrá sola.)"
                    : $"El resultado del ejercicio {n}, una pérdida de {E(-c.Actual.Resultado)}, se aplicará a resultados negativos de ejercicios anteriores.",
            _ => "",
        };
    }

    /// <summary>Las tablas que el programa calcula para un apartado.</summary>
    public static IReadOnlyList<TablaMemoria> Tablas(string numero, CuentasAnualesPymes c, DatosMemoria d)
    {
        var a = c.Actual;
        var b = c.Anterior;
        string[] dos = [$"Ejercicio {c.Ejercicio}", $"Ejercicio {c.Ejercicio - 1}"];
        FilaTablaMemoria Partida(string clave, bool total = false) =>
            new(ModeloPymes.PorClave[clave].Texto, [a[clave], b is null ? null : b[clave]], total);
        IReadOnlyList<FilaTablaMemoria> Partidas(params string[] claves) =>
            claves.Select(k => Partida(k)).Where(f => f.Importes.Any(x => x is decimal v && v != 0m)).ToList();
        switch (numero)
        {
            case "4":
                {
                    // Bruto y amortización acumulada de cada clase: saldo inicial (apertura), entradas y salidas (resto de asientos), saldo final.
                    (string[] Bruto, string[] Amort)[] clases = [(["20"], ["280"]), (["21", "23"], ["281"]), (["22"], ["282"])];
                    var filas = new List<FilaTablaMemoria>();
                    decimal?[] Fila(Func<(string[] Bruto, string[] Amort), decimal> f) => clases.Select(x => (decimal?)f(x)).ToArray();
                    MovimientoSubcuenta Suma(string[] pre) => a.Movimientos.Values.Where(m => pre.Any(p => m.Cuenta.StartsWith(p, StringComparison.Ordinal)))
                        .Aggregate(new MovimientoSubcuenta("", 0m, 0m, 0m), (s, m) => s with { Apertura = s.Apertura + m.Apertura, Debe = s.Debe + m.Debe, Haber = s.Haber + m.Haber });
                    MovimientoSubcuenta Bruto((string[] Bruto, string[] Amort) x) => Suma(x.Bruto);
                    filas.Add(new("A) Saldo inicial bruto", Fila(x => Bruto(x).Apertura)));
                    filas.Add(new("(+) Entradas", Fila(x => Bruto(x).Debe)));
                    filas.Add(new("(−) Salidas", Fila(x => Bruto(x).Haber)));
                    filas.Add(new("B) Saldo final bruto", Fila(x => Bruto(x).Final), true));
                    filas.Add(new("C) Amortización acumulada inicial", Fila(x => -Suma(x.Amort).Apertura)));
                    filas.Add(new("(+) Dotación a la amortización", Fila(x => Suma(x.Amort).Haber)));
                    filas.Add(new("(−) Disminuciones por salidas y traspasos", Fila(x => Suma(x.Amort).Debe)));
                    filas.Add(new("D) Amortización acumulada final", Fila(x => -Suma(x.Amort).Final), true));
                    filas.Add(new("Valor neto contable (B − D)", Fila(x => Bruto(x).Final + Suma(x.Amort).Final), true));
                    return [new TablaMemoria($"Movimiento del inmovilizado en {c.Ejercicio}", ["Inmovilizado intangible", "Inmovilizado material", "Inversiones inmobiliarias"], filas)];
                }
            case "5":
                return [new TablaMemoria("Activos financieros", dos,
                    [.. Partidas("11400", "11500", "12380", "12390", "12400", "12500", "12700"),
                     new("Total", [Sum(a, "11400", "11500", "12380", "12390", "12400", "12500", "12700"), b is null ? null : Sum(b, "11400", "11500", "12380", "12390", "12400", "12500", "12700")], true)])];
            case "6":
                return [new TablaMemoria("Pasivos financieros", dos,
                    [.. Partidas("31220", "31230", "31290", "31300", "32320", "32330", "32390", "32400", "32580", "32590"),
                     new("Total", [Sum(a, "31200", "31300", "32300", "32400", "32500"), b is null ? null : Sum(b, "31200", "31300", "32300", "32400", "32500")], true)])];
            case "7":
                return [new TablaMemoria("Fondos propios", dos, [.. Partidas("21110", "21120", "21200", "21350", "21360", "21400", "21500", "21600", "21700", "21800"), Partida("21000", true)])];
            case "8":
                return [new TablaMemoria("Resultado e impuesto", dos, [Partida("49300", true), Partida("41900"), Partida("49500", true)])];
            case "9" when d.Dividendos is { Count: > 0 } dv:
                return [new TablaMemoria($"Dividendos repartidos a los socios en {c.Ejercicio}", ["Bruto", "Retención", "Neto"],
                    [.. dv.Select(x => new FilaTablaMemoria($"{x.Socio} ({x.Fecha:dd/MM/yyyy})", [x.Bruto, x.Retencion, x.Bruto - x.Retencion])),
                     new("Total", [dv.Sum(x => x.Bruto), dv.Sum(x => x.Retencion), dv.Sum(x => x.Bruto - x.Retencion)], true)])];
            case "11" when d.Aplicacion is { Resultado: >= 0m } ap:
                return [new TablaMemoria($"Aplicación del resultado de {c.Ejercicio}", ["Importe"],
                [
                    new("Base de reparto: saldo de la cuenta de pérdidas y ganancias", [ap.Resultado], true),
                    new("A reserva legal", [ap.ReservaLegal]),
                    new("A reservas voluntarias", [ap.Voluntarias]),
                    new("A dividendos", [ap.Dividendos]),
                    new("A compensación de pérdidas de ejercicios anteriores", [ap.Compensacion]),
                    new("Total aplicación", [ap.ReservaLegal + ap.Voluntarias + ap.Dividendos + ap.Compensacion], true),
                ])];
            default:
                return [];
        }
    }

    private static decimal Sum(CifrasEjercicio e, params string[] claves) => claves.Sum(k => e[k]);
}
