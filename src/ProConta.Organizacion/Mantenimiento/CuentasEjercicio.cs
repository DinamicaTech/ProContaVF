using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.Mantenimiento;

/// <summary>
/// Una cuenta configurable del ejercicio (columna de SociedadesEjercicios): dónde se ve en la ficha, qué subcuenta propone «Auto» y
/// cómo se explica (organizacion/sociedades-ejercicios 2026-11-18).
/// </summary>
public sealed record CuentaEjercicio(
    string Columna, string Pestana, string Subgrupo, string Etiqueta, string Ayuda, string CuandoSeUsa, string Asiento,
    IReadOnlyList<string> Grupos, string? Habitual = null, string? NombreHabitual = null, IReadOnlyList<IReadOnlyList<string>>? Palabras = null,
    IReadOnlyList<string>? Sinonimos = null, int Longitud = 10)
{
    /// <summary>Título de la pestaña donde está la cuenta, para los mensajes.</summary>
    public string TituloPestana => CuentasEjercicio.Pestanas.First(p => p.Clave == Pestana).Titulo;
}

/// <summary>
/// Cuentas configurables del ejercicio (organizacion/sociedades-ejercicios 2026-11-18). Siempre están asignadas (propietario 2026-11-18
/// 05:10): un proceso con su cuenta vacía se detiene y pide asignarla en Parámetros y tablas › Ejercicios, donde «Auto» propone la habitual.
/// </summary>
public static class CuentasEjercicio
{
    /// <summary>Pestañas de la ficha de Ejercicios, una por proceso (propietario 2026-11-18 05:10).</summary>
    public static IReadOnlyList<GrupoCampos> Pestanas { get; } =
        [new("ident", "Identificación"), new("compras", "Compras y ventas"), new("iva", "IVA"), new("is", "Impuesto sobre Sociedades"), new("fin", "Fin de ejercicio")];

    private static IReadOnlyList<IReadOnlyList<string>> P(params string[][] condiciones) => condiciones;

    public static IReadOnlyList<CuentaEjercicio> Todas { get; } =
    [
        // ---------- Compras y ventas ----------
        new("CuentaSuplido", "compras", "Suplidos y retenciones", "Suplido",
            "Cuenta de los importes suplidos: los gastos que se pagan en nombre del cliente y se le repercuten sin IVA.",
            "Al contabilizar una venta, en las líneas suplidas que no llevan cuenta propia, y al contabilizar una compra, para el importe suplido.",
            "Venta: 430 cliente al Debe por el total y esta cuenta al Haber por el suplido. Compra: esta cuenta al Debe por el suplido.",
            ["55"], Palabras: P(["suplido"]), Sinonimos: ["suplido", "555"], Longitud: 255),
        new("CuentaRetencionServicios", "compras", "Suplidos y retenciones", "Retención de servicios",
            "Retenciones de IRPF practicadas a profesionales y actividades económicas (modelo 111).",
            "Al contabilizar una factura de compra con retención de la clase «Actividades y profesionales».",
            "Proveedor 410 al Haber por el total menos la retención; esta cuenta al Haber por la retención. Se salda al pagar el modelo 111.",
            ["4751"], Palabras: P(["profesional", "servicio", "actividad"]), Sinonimos: ["retencion", "irpf", "111", "4751"]),
        new("CuentaRetencionAlquileres", "compras", "Suplidos y retenciones", "Retención de alquileres",
            "Retenciones de IRPF practicadas en los alquileres de locales (modelo 115).",
            "Al contabilizar una factura de alquiler con retención de la clase «Arrendamientos».",
            "Arrendador 410 al Haber por el total menos la retención; esta cuenta al Haber por la retención. Se salda al pagar el modelo 115.",
            ["4751"], Palabras: P(["alquiler", "arrendamiento"]), Sinonimos: ["retencion", "irpf", "115", "alquiler", "4751"]),
        new("CuentaIVAIntracomunitarioRepercutidoServicios", "compras", "IVA intracomunitario e inversión del sujeto pasivo", "Intracomunitario repercutido · servicios",
            "IVA que se autorrepercute al comprar servicios a una empresa de otro país de la UE.",
            "Al contabilizar una compra de servicios intracomunitaria: el proveedor no cobra IVA y la empresa lo declara a la vez como repercutido y como soportado.",
            "Esta cuenta (477) al Haber y la de soportado intracomunitario de servicios (472) al Debe, por la base al tipo general.",
            ["477"], Palabras: P(["intracomunitari"], ["servicio"]), Sinonimos: ["intracomunitario", "ue", "477"]),
        new("CuentaIVAIntracomunitarioRepercutidoBienes", "compras", "IVA intracomunitario e inversión del sujeto pasivo", "Intracomunitario repercutido · bienes",
            "IVA que se autorrepercute al comprar bienes a una empresa de otro país de la UE.",
            "Al contabilizar una adquisición intracomunitaria de bienes.",
            "Esta cuenta (477) al Haber y la de soportado intracomunitario de bienes (472) al Debe, por la base al tipo general.",
            ["477"], Palabras: P(["intracomunitari"], ["bien"]), Sinonimos: ["intracomunitario", "ue", "477"]),
        new("CuentaIVAIntracomunitarioSoportadoServicios", "compras", "IVA intracomunitario e inversión del sujeto pasivo", "Intracomunitario soportado · servicios",
            "IVA soportado deducible de las compras de servicios a otro país de la UE.",
            "Al contabilizar una compra de servicios intracomunitaria, junto con la de repercutido.",
            "Esta cuenta (472) al Debe y la de repercutido intracomunitario de servicios (477) al Haber.",
            ["472"], Palabras: P(["intracomunitari"], ["servicio"]), Sinonimos: ["intracomunitario", "ue", "472"]),
        new("CuentaIVAIntracomunitarioSoportadoBienes", "compras", "IVA intracomunitario e inversión del sujeto pasivo", "Intracomunitario soportado · bienes",
            "IVA soportado deducible de las compras de bienes a otro país de la UE.",
            "Al contabilizar una adquisición intracomunitaria de bienes, junto con la de repercutido.",
            "Esta cuenta (472) al Debe y la de repercutido intracomunitario de bienes (477) al Haber.",
            ["472"], Palabras: P(["intracomunitari"], ["bien"]), Sinonimos: ["intracomunitario", "ue", "472"]),
        new("CuentaIVAInversionSujetoPasivoRepercutido", "compras", "IVA intracomunitario e inversión del sujeto pasivo", "Inversión del sujeto pasivo · repercutido",
            "IVA que se autorrepercute en las compras con inversión del sujeto pasivo (obras, chatarra, servicios de no establecidos…).",
            "Al contabilizar una compra en la que la factura dice «inversión del sujeto pasivo»: el proveedor no cobra IVA y la empresa lo declara.",
            "Esta cuenta (477) al Haber y la de soportado por inversión del sujeto pasivo (472) al Debe, por la base al tipo general.",
            ["477"], Palabras: P(["sujeto pasivo", "isp"]), Sinonimos: ["isp", "inversion", "477"]),
        new("CuentaIVAInversionSujetoPasivoSoportado", "compras", "IVA intracomunitario e inversión del sujeto pasivo", "Inversión del sujeto pasivo · soportado",
            "IVA soportado deducible de las compras con inversión del sujeto pasivo.",
            "Al contabilizar una compra con inversión del sujeto pasivo, junto con la de repercutido.",
            "Esta cuenta (472) al Debe y la de repercutido por inversión del sujeto pasivo (477) al Haber.",
            ["472"], Palabras: P(["sujeto pasivo", "isp"]), Sinonimos: ["isp", "inversion", "472"]),
        new("CuentaIVAImportacion", "compras", "Importación y redondeo", "IVA de importación",
            "IVA soportado en las importaciones (el que se paga en la aduana con el DUA).",
            "Al contabilizar la factura del DUA: su importe se desglosa al tipo general como IVA de importación.",
            "Esta cuenta (472) al Debe por el IVA del DUA; el transitario o la aduana al Haber.",
            ["472"], Palabras: P(["importacion", "dua", "aduana"]), Sinonimos: ["importacion", "dua", "aduana", "472"]),
        new("CuentaRedondeoGastos", "compras", "Importación y redondeo", "Redondeo · gastos",
            "Recoge los céntimos que le faltan al Debe de un asiento para cuadrar.",
            "Al contabilizar una compra con un descuadre de céntimos (hasta 0,03 €, o más con motivo) y al saldar los céntimos residuales en Fin de ejercicio.",
            "Esta cuenta al Debe por la diferencia.",
            ["6"], "6590000", "Otras pérdidas en gestión corriente", Sinonimos: ["redondeo", "centimos", "659"]),
        new("CuentaRedondeoIngresos", "compras", "Importación y redondeo", "Redondeo · ingresos",
            "Recoge los céntimos que le sobran al Debe de un asiento para cuadrar.",
            "Al contabilizar una compra con un descuadre de céntimos a favor y al saldar los céntimos residuales en Fin de ejercicio.",
            "Esta cuenta al Haber por la diferencia.",
            ["7"], "7590000", "Ingresos por servicios diversos", Sinonimos: ["redondeo", "centimos", "759"]),

        // ---------- IVA ----------
        new("CuentaHaciendaAcreedoraIva", "iva", "Liquidación", "A ingresar",
            "Hacienda acreedora por IVA: lo que sale a pagar en cada trimestre.",
            "En Contabilidad › Liquidación de IVA, cuando el 303 del trimestre sale a ingresar. Se salda al pagarlo.",
            "Las 477 al Debe y las 472 al Haber por sus saldos del trimestre; esta cuenta al Haber por la diferencia a pagar.",
            ["475"], "4750000", "Hacienda Pública, acreedora por IVA", Sinonimos: ["4750", "liquidacion", "iva a ingresar"]),
        new("CuentaHaciendaDeudoraIva", "iva", "Liquidación", "A compensar",
            "Hacienda deudora por IVA: lo que sale a compensar o a devolver.",
            "En Contabilidad › Liquidación de IVA, cuando el 303 sale negativo. Su saldo se compensa en la siguiente liquidación que salga a ingresar.",
            "Esta cuenta al Debe por el importe a compensar; al compensarlo, al Haber.",
            ["470"], "4700000", "Hacienda Pública, deudora por IVA", Sinonimos: ["4700", "liquidacion", "iva a compensar"]),
        new("CuentaAjustesNegativosIva", "iva", "Liquidación", "No deducible y ajustes a pagar más",
            "Gasto por el IVA soportado que no se deduce y por los ajustes cuando se liquida más de lo que dicen las cuentas.",
            "En la liquidación de IVA (parte no deducible por prorrata o por actividades sin deducción, y ajustes para cuadrar con lo presentado) y en la regularización de la prorrata.",
            "Esta cuenta al Debe por el IVA no deducible o por el ajuste.",
            ["634"], "6341000", "Ajustes negativos en IVA de circulante", Sinonimos: ["6341", "prorrata", "no deducible"]),
        new("CuentaAjustesPositivosIva", "iva", "Liquidación", "Ajustes a pagar menos",
            "Ingreso por los ajustes cuando se liquida menos de lo que dicen las cuentas.",
            "En la liquidación de IVA cuando el importe se fija a mano por debajo del calculado (por ejemplo, para cuadrar con lo presentado) y en la regularización de la prorrata a favor.",
            "Esta cuenta al Haber por el ajuste.",
            ["639"], "6391000", "Ajustes positivos en IVA de circulante", Sinonimos: ["6391", "ajuste iva"]),
        new("CuentaRegularizacionProrrata", "iva", "Prorrata", "Regularización de la prorrata",
            "Recoge la regularización anual de la prorrata entre la provisional y la definitiva.",
            "En Contabilidad › Prorrata de IVA, el 31/12, al fijar la prorrata definitiva; la liquidación del 4T la salda.",
            "Esta cuenta al Debe (o al Haber) por la diferencia de IVA deducible, contra la de no deducible o la de ajustes a pagar menos.",
            ["472"], "4729001", "H.P. IVA soportado, regularización de la prorrata", Sinonimos: ["4729", "prorrata", "regularizacion"]),

        // ---------- Impuesto sobre Sociedades ----------
        new("CuentaImpuestoSociedades", "is", "Contabilización del impuesto", "Impuesto corriente",
            "Gasto por el Impuesto sobre Sociedades del ejercicio (la cuota líquida).",
            "En Contabilidad › Impuesto sobre Sociedades, al confirmar el cálculo a 31/12.",
            "Esta cuenta al Debe por la cuota; las retenciones y pagos a cuenta (473) al Haber y la diferencia a la de a ingresar o a devolver.",
            ["630"], "6300000", "Impuesto corriente", Sinonimos: ["6300", "sociedades", "impuesto"]),
        new("CuentaHaciendaAcreedoraIS", "is", "Contabilización del impuesto", "A ingresar",
            "Lo que queda a pagar del impuesto después de las retenciones y los pagos a cuenta.",
            "En el asiento del Impuesto sobre Sociedades cuando la cuota supera las retenciones y pagos a cuenta. Se salda al pagar el modelo 200.",
            "Esta cuenta al Haber por la cuota menos retenciones y pagos a cuenta.",
            ["475"], "4752000", "Hacienda Pública, acreedora por Impuesto sobre Sociedades", Sinonimos: ["4752", "sociedades", "a ingresar"]),
        new("CuentaHaciendaDeudoraIS", "is", "Contabilización del impuesto", "A devolver",
            "Lo que Hacienda tiene que devolver cuando las retenciones y los pagos a cuenta superan la cuota.",
            "En el asiento del Impuesto sobre Sociedades cuando sale a devolver. Se salda al cobrar la devolución.",
            "Esta cuenta al Debe por el importe a devolver.",
            ["470"], "4709000", "Hacienda Pública, deudora por devolución de impuestos", Sinonimos: ["4709", "sociedades", "a devolver"]),
        new("CuentaCreditoPerdidasIS", "is", "Contabilización del impuesto", "Crédito por pérdidas",
            "Crédito por las bases imponibles negativas que quedan por compensar.",
            "En el asiento del Impuesto sobre Sociedades: se carga al activar el crédito de un ejercicio con pérdidas y se abona al aplicarlo.",
            "Al aplicarlo: esta cuenta al Haber y la de impuesto diferido al Debe.",
            ["474"], "4745000", "Créditos por pérdidas a compensar del ejercicio", Sinonimos: ["4745", "sociedades", "bases negativas"]),
        new("CuentaImpuestoDiferidoIS", "is", "Contabilización del impuesto", "Impuesto diferido",
            "Gasto que recoge el crédito por pérdidas cuando se aplica.",
            "En el asiento del Impuesto sobre Sociedades de un ejercicio que compensa bases negativas de años anteriores.",
            "Esta cuenta al Debe y la de crédito por pérdidas al Haber, por el crédito aplicado.",
            ["630"], "6301000", "Impuesto diferido", Sinonimos: ["6301", "sociedades", "diferido"]),

        // ---------- Fin de ejercicio ----------
        new("CuentaResultadoEjercicio", "fin", "Cierre", "Cuenta de resultados",
            "Subcuenta del resultado del ejercicio (pérdidas y ganancias).",
            "En Contabilidad › Cierre del ejercicio: la regularización salda contra ella los grupos 6 y 7. La aplicación del resultado del año siguiente la salda.",
            "Grupo 7 al Debe y grupo 6 al Haber por sus saldos; esta cuenta por la diferencia (al Haber si hay beneficio).",
            ["129"], "1290000", "Resultado del ejercicio", Sinonimos: ["129", "resultado", "cierre", "regularizacion"]),
        new("CuentaReservaLegal", "fin", "Aplicación del resultado y dividendos", "Reserva legal",
            "Reserva obligatoria: el 10 % del beneficio hasta que llegue al 20 % del capital.",
            "En Contabilidad › Fin de ejercicio › Aplicación del resultado.",
            "Cuenta de resultados al Debe por el beneficio; esta cuenta al Haber por la parte de la reserva legal.",
            ["112"], "1120000", "Reserva legal", Sinonimos: ["1120", "reserva legal", "aplicacion", "resultado"]),
        new("CuentaResultadosNegativos", "fin", "Aplicación del resultado y dividendos", "Resultados negativos",
            "Pérdidas de ejercicios anteriores pendientes de compensar.",
            "En la aplicación del resultado: recibe la pérdida del año y se compensa con los beneficios siguientes.",
            "Esta cuenta al Debe y la cuenta de resultados al Haber por la pérdida.",
            ["121"], "1210000", "Resultados negativos de ejercicios anteriores", Sinonimos: ["1210", "perdidas", "resultados negativos", "aplicacion"]),
        new("CuentaReservasDividendos", "fin", "Aplicación del resultado y dividendos", "Reservas voluntarias · dividendos y aplicación",
            "Reservas voluntarias: de ellas salen los dividendos y a ellas va el beneficio que no se reparte.",
            "En Contabilidad › Reparto de dividendos (se carga por el bruto) y en la aplicación del resultado (recibe el resto del beneficio).",
            "Dividendos: esta cuenta al Debe por el bruto; socios (551) y retenciones (4751) al Haber.",
            ["113"], "1130000", "Reservas voluntarias", Sinonimos: ["1130", "reservas", "dividendos", "voluntarias", "aplicacion"]),
        new("CuentaFacturasPendientesRecibir", "fin", "Facturas pendientes de recibir", "Cuenta de facturas pendientes",
            "Proveedores con facturas pendientes de recibir: el gasto de los proveedores pagados sin factura al cerrar el año.",
            "En Contabilidad › Fin de ejercicio › Facturas pendientes de recibir, a 31/12; el 1/1 del año siguiente se revierte.",
            "Gasto al Debe y esta cuenta al Haber a 31/12; al revés el 1/1.",
            ["400"], "4009000", "Proveedores, facturas pendientes de recibir", Sinonimos: ["4009", "pendientes", "recibir"]),
        new("CuentaGastoFacturasPendientes", "fin", "Facturas pendientes de recibir", "Gasto por defecto",
            "Cuenta de gasto para los proveedores pagados sin factura que no tienen cuenta de gasto por defecto ni facturas contabilizadas.",
            "En Contabilidad › Fin de ejercicio › Facturas pendientes de recibir, solo cuando el proveedor no da otra cuenta de gasto.",
            "Esta cuenta al Debe y la de facturas pendientes al Haber.",
            ["6"], "6299000", "Otros servicios", Sinonimos: ["6299", "pendientes", "recibir", "gasto"]),
    ];

    public static CuentaEjercicio De(string columna) => Todas.First(c => string.Equals(c.Columna, columna, StringComparison.OrdinalIgnoreCase));

    /// <summary>El campo del mantenimiento de Ejercicios para una cuenta, con su explicación estructurada.</summary>
    public static CampoMantenimiento Campo(CuentaEjercicio c) =>
        new(c.Columna, c.Etiqueta, TipoCampo.Cuenta)
        {
            Grupo = c.Pestana, Subgrupo = c.Subgrupo, Longitud = c.Longitud, Ancho = "s", GruposCuenta = c.Grupos, Sinonimos = c.Sinonimos ?? [],
            CuentaHabitual = c.Habitual, NombreCuentaHabitual = c.NombreHabitual, PalabrasCuenta = c.Palabras ?? [],
            Ayuda = c.Ayuda + " " + (c.Habitual is string h ? $"«Auto» propone la {h}." : "«Auto» la propone si solo una subcuenta encaja."),
            Explicacion =
            [
                new("Para qué sirve", c.Ayuda),
                new("Cuándo se usa", c.CuandoSeUsa),
                new("Asiento", c.Asiento),
                new("Qué propone «Auto»", c.Habitual is string hab
                    ? $"La {hab} ({c.NombreHabitual}). Si no existe en el plan del ejercicio, la crea."
                    : $"La única subcuenta de {string.Join(", ", c.Grupos)} cuyo nombre lo dice ({Condiciones(c)}). Si encajan varias o ninguna, la deja vacía para que la elijas con la lupa."),
                new("Si está vacía", "El proceso que la usa se detiene y pide asignarla aquí. No hay cuenta por defecto implícita."),
            ],
        };

    private static string Condiciones(CuentaEjercicio c) =>
        string.Join(" y ", (c.Palabras ?? []).Select(alternativas => string.Join(" o ", alternativas.Select(p => $"«{p}»"))));

    /// <summary>
    /// Error de un proceso al que le faltan cuentas del ejercicio (propietario 2026-11-18 05:10: sin cuenta por defecto implícita), o null si
    /// están todas. Dice cuáles faltan, dónde se asignan y que «Auto» las propone.
    /// </summary>
    public static async Task<Error?> FaltanAsync(ISesionDatos s, int idSociedadEjercicio, params string[] columnas)
    {
        var esquema = s.LeerEsquema();
        var existentes = columnas.Where(c => esquema.ExisteColumna("SociedadesEjercicios", c)).ToList();
        if (existentes.Count == 0) return null;
        var fila = await s.ConsultarPrimeroAsync($"SELECT Ejercicio, {string.Join(", ", existentes)} FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => (Ejercicio: Convert.ToString(f.Valor("Ejercicio"), CultureInfo.InvariantCulture), Vacias: existentes.Where(c => string.IsNullOrWhiteSpace(f.Texto(c))).ToList()),
            idSociedadEjercicio);
        return fila.Vacias is { Count: > 0 } vacias ? Faltan(fila.Ejercicio ?? "", vacias) : null;
    }

    /// <summary>El error de <see cref="FaltanAsync"/> para unas columnas vacías.</summary>
    public static Error Faltan(string ejercicio, IReadOnlyList<string> columnas)
    {
        var cuentas = columnas.Select(De).ToList();
        var lista = string.Join(", ", cuentas.Select(c => $"«{c.Etiqueta}» ({c.TituloPestana})"));
        var pestanas = string.Join(" y ", cuentas.Select(c => $"«{c.TituloPestana}»").Distinct());
        return new Error("Ejercicio.FaltanCuentas",
            cuentas.Count == 1 ? $"Falta la cuenta {lista} del ejercicio {ejercicio}." : $"Faltan cuentas del ejercicio {ejercicio}: {lista}.",
            $"Ejercicio {ejercicio}",
            "Las cuentas de los procesos se configuran en cada ejercicio y no hay cuenta por defecto: hay que asignarlas.",
            $"Abre Configuración › Parámetros y tablas › Ejercicios, ejercicio {ejercicio}, pestaña {pestanas}, y pulsa «Auto»: propone la cuenta habitual y la crea si no existe. Después, guarda y vuelve a intentarlo.");
    }
}
