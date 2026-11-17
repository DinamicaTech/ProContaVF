namespace ProConta.Contabilidad.Iva;

/// <summary>Base y cuota de una casilla.</summary>
public readonly record struct CifraIva(decimal Base, decimal Cuota)
{
    public static CifraIva Cero => default;
    public static CifraIva operator +(CifraIva a, CifraIva b) => new(a.Base + b.Base, a.Cuota + b.Cuota);
}

/// <summary>Línea del modelo 303 calculada, con las cifras de los cuatro trimestres (índices 0 a 3) y su suma en el año.</summary>
/// <param name="Clave">Identificador estable de la línea (lo usan el libro Excel, las casillas del modelo y las pruebas).</param>
/// <param name="ConBase">La línea muestra base (la regularización de la prorrata solo cuota).</param>
/// <param name="ConCuota">La línea muestra cuota (las ventas exentas solo base).</param>
/// <param name="ParteProrrata">
/// En las líneas de lo deducible, la parte de la cuota que sale de la prorrata (ya redondeada) de cada trimestre; null en las demás.
/// La usan la prorrata (lo deducido con la provisional) y la liquidación (lo que no se deduce va a la 6341).
/// </param>
public sealed record Linea303(string Clave, string Concepto, string Nota, IReadOnlyList<CifraIva> Trimestres, bool ConBase = true, bool ConCuota = true,
    IReadOnlyList<decimal>? ParteProrrata = null)
{
    /// <summary>Cifra de un trimestre (1 a 4) o del año (0): la suma de los trimestres, ya redondeados.</summary>
    public CifraIva En(int trimestre) => trimestre == 0 ? Trimestres.Aggregate(CifraIva.Cero, (a, b) => a + b) : Trimestres[trimestre - 1];

    /// <summary>Parte de la cuota deducida por la prorrata en un trimestre (1 a 4) o en el año (0).</summary>
    public decimal Prorrata(int trimestre) => ParteProrrata is null ? 0m : trimestre == 0 ? ParteProrrata.Sum() : ParteProrrata[trimestre - 1];
}

/// <summary>
/// Modelo 303 de los cuatro trimestres del ejercicio (contabilidad/modelos-tributarios), calculado desde las líneas de IVA:
/// devengado, deducible, resultado e informativo. <see cref="ProrrataProvisional"/> en tanto por uno (0,15 = 15 %).
/// </summary>
public sealed record Resultado303(decimal ProrrataProvisional, IReadOnlyList<Linea303> Devengado, IReadOnlyList<Linea303> Deducible, IReadOnlyList<Linea303> Informativo)
{
    public Linea303 Linea(string clave) => Devengado.Concat(Deducible).Concat(Informativo).First(l => l.Clave == clave);

    public decimal TotalDevengado(int trimestre) => Devengado.Sum(l => l.En(trimestre).Cuota);
    public decimal TotalDeducible(int trimestre) => Deducible.Sum(l => l.En(trimestre).Cuota);

    /// <summary>Lo deducido por la prorrata provisional en un trimestre (1 a 4) o el año (0): la suma de las partes de cada casilla, ya redondeadas.</summary>
    public decimal DeducidoProrrata(int trimestre) => Deducible.Sum(l => l.Prorrata(trimestre));

    /// <summary>Resultado del trimestre (1 a 4) o del año (0): a ingresar si es positivo, a compensar si es negativo (casilla 46 del 303).</summary>
    public decimal Resultado(int trimestre) => TotalDevengado(trimestre) - TotalDeducible(trimestre);
}

/// <summary>
/// Cálculo del 303 por trimestre (sin BD), con una línea por casilla del modelo (contabilidad/modelos-tributarios/modelo-303,
/// acordado con el propietario el 2026-11-17 14:38). Lo usan las casillas del modelo, la hoja «IVA 303» del informe para la gestoría,
/// la liquidación del IVA y la prorrata:
/// <list type="bullet">
/// <item>Devengado: el IVA repercutido de cada tipo con cuota (sea cual sea el centro) que no es de una rectificativa (01 a 09);
/// las adquisiciones intracomunitarias (10/11: toda línea soportada intracomunitaria); la inversión del sujeto pasivo no
/// intracomunitaria (12/13); y las rectificativas emitidas (14/15). La importación con DUA no se devenga: el IVA se pagó en la aduana
/// (solo se devengaría, en la 77, con el pago diferido, que la sociedad no usa).</item>
/// <item>Deducible, separado como pide el modelo: interiores de bienes y servicios corrientes (28/29, con la inversión del sujeto
/// pasivo no intracomunitaria) y de bienes de inversión (30/31), importaciones de bienes corrientes (32/33) y de inversión (34/35),
/// intracomunitarias corrientes (36/37) y de inversión (38/39), y la regularización de la prorrata definitiva (44, solo en el 4T, la
/// que da <see cref="CalculoProrrata"/> cuando el ejercicio tiene la definitiva fijada; si no, 0). En cada casilla: lo de actividades
/// 100 % deducibles entero más Redondear(lo de actividades en prorrata × prorrata provisional, 2); la base igual que la cuota (solo
/// las líneas con tipo). Las líneas «Sin centro» y las no deducibles no deducen.</item>
/// <item>Redondeo: las bases y cuotas son las del libro (céntimos) y se suman sin redondear; lo único que se redondea es el
/// producto por la prorrata, a 2 decimales alejándose de cero, una vez por casilla y trimestre (como la AEAT), no por apunte. El
/// año es la suma de los cuatro trimestres ya redondeados.</item>
/// <item>Informativo: no deducible, sujeto a prorrata, sin clasificar, ventas exentas y las casillas informativas de la página 3
/// (59 entregas intracomunitarias, 60 exportaciones, 120 no sujetas por localización y 122 con inversión del sujeto pasivo).</item>
/// <item>Las líneas con trimestre 0 (fecha fuera del ejercicio) no entran en ningún trimestre.</item>
/// </list>
/// </summary>
public static class Calculo303
{
    public const string ClaveIntracomunitarias = "dev.intracomunitarias";
    public const string ClaveIsp = "dev.isp";
    public const string ClaveModificacion = "dev.modificacion";
    public const string ClaveInterioresCorrientes = "ded.interioresCorrientes";
    public const string ClaveInterioresInversion = "ded.interioresInversion";
    public const string ClaveImportacionesCorrientes = "ded.importacionesCorrientes";
    public const string ClaveImportacionesInversion = "ded.importacionesInversion";
    public const string ClaveIntracomunitariasCorrientes = "ded.intracomunitariasCorrientes";
    public const string ClaveIntracomunitariasInversion = "ded.intracomunitariasInversion";
    public const string ClaveRegularizacionProrrata = "ded.regularizacionProrrata";
    public const string ClaveNoDeducible = "inf.noDeducible";
    public const string ClaveSujetaProrrata = "inf.sujetaProrrata";
    public const string ClaveSinClasificar = "inf.sinClasificar";
    public const string ClaveExentas = "inf.exentas";
    public const string ClaveEntregasIntracomunitarias = "inf.entregasIntracomunitarias";
    public const string ClaveExportaciones = "inf.exportaciones";
    public const string ClaveNoSujetasLocalizacion = "inf.noSujetasLocalizacion";
    public const string ClaveSujetasIsp = "inf.sujetasIsp";

    /// <summary>Claves de las líneas de lo deducible por cuotas soportadas (casillas 28 a 39), en el orden del modelo.</summary>
    public static readonly IReadOnlyList<string> ClavesDeduccionSoportado =
    [
        ClaveInterioresCorrientes, ClaveInterioresInversion, ClaveImportacionesCorrientes, ClaveImportacionesInversion,
        ClaveIntracomunitariasCorrientes, ClaveIntracomunitariasInversion,
    ];

    /// <summary>Clave de la línea de repercutido de un tipo, por ejemplo «dev.rep.21».</summary>
    public static string ClaveRepercutido(decimal tipo) => "dev.rep." + tipo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Tipos de repercutido que salen siempre (aunque no tengan cuota), además de los que aparezcan en las líneas.</summary>
    public static readonly IReadOnlyList<decimal> TiposFijos = [21m, 10m, 4m];

    /// <summary>Claves de operación (causa de exención o calificación) de cada casilla informativa de la página 3.</summary>
    public static readonly IReadOnlyList<string> ClavesEntregasIntracomunitarias = ["E5"];
    public static readonly IReadOnlyList<string> ClavesExportaciones = ["E2", "E3", "E4"];
    public static readonly IReadOnlyList<string> ClavesNoSujetasLocalizacion = ["N2"];
    public static readonly IReadOnlyList<string> ClavesSujetasIsp = ["S2"];

    /// <summary>Redondeo de las cifras por prorrata: 2 decimales, alejándose de cero (REDONDEAR de Excel).</summary>
    public static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // Criterios de cada línea del modelo (los mismos que las fórmulas del libro).
    public static bool EsRepercutido(LineaIva l) => l.Sentido == SentidoIva.Repercutido;
    public static bool EsSoportado(LineaIva l) => l.Sentido == SentidoIva.Soportado;
    public static bool EsIntracomunitaria(LineaIva l) => EsSoportado(l) && l.Operacion == OperacionIva.Intracomunitaria;
    public static bool EsIspNoIntracomunitaria(LineaIva l) => EsSoportado(l) && l.Isp && l.Operacion != OperacionIva.Intracomunitaria;
    public static bool EsImportacion(LineaIva l) => EsSoportado(l) && l.Operacion == OperacionIva.Importacion && !l.Isp;
    public static bool EsInterior(LineaIva l) => EsSoportado(l) && l.Operacion == OperacionIva.Nacional && !l.Isp;

    /// <summary>Soportado con el IVA autorrepercutido (se devenga y se deduce): intracomunitarias e inversión del sujeto pasivo. La importación con DUA no.</summary>
    public static bool EsAutorrepercutida(LineaIva l) => EsIntracomunitaria(l) || EsIspNoIntracomunitaria(l);

    /// <summary>Soportado que se deduce en las casillas de operaciones interiores (28 a 31): interiores e inversión del sujeto pasivo no intracomunitaria.</summary>
    public static bool EsDeducibleInterior(LineaIva l) => EsInterior(l) || EsIspNoIntracomunitaria(l);

    /// <param name="regularizacionProrrata">Regularización anual de la prorrata (contabilidad/procesos/prorrata): positiva a favor,
    /// negativa en contra. Va en la casilla 44 del 4T; 0 mientras no haya prorrata definitiva.</param>
    public static Resultado303 Calcular(IReadOnlyList<LineaIva> lineas, decimal prorrataProvisional, decimal regularizacionProrrata = 0m)
    {
        CifraIva[] Sumar(Func<LineaIva, bool> filtro, Func<LineaIva, bool>? filtroBase = null) =>
            Enumerable.Range(1, 4).Select(t =>
            {
                var del = lineas.Where(l => l.Trimestre == t && filtro(l)).ToList();
                return new CifraIva(del.Where(l => filtroBase?.Invoke(l) ?? true).Sum(l => l.Base), del.Sum(l => l.Cuota));
            }).ToArray();

        static bool ConTipo(LineaIva l) => l.TipoIva > 0m;
        var tipos = TiposFijos.Concat(lineas.Where(l => EsRepercutido(l) && !l.Rectificativa && l.TipoIva > 0m).Select(l => l.TipoIva)).Distinct().OrderByDescending(t => t).ToList();
        var devengado = tipos.Select(t => new Linea303(ClaveRepercutido(t), $"Régimen general {t:0.##} %",
                t == 21m ? "Todo el IVA repercutido de este tipo, sea cual sea el centro de coste (sin las rectificativas, que van en la modificación de bases y cuotas)." : "",
                Sumar(l => EsRepercutido(l) && !l.Rectificativa && l.TipoIva == t)))
            .ToList();
        devengado.Add(new(ClaveIntracomunitarias, "Adquisiciones intracomunitarias de bienes y servicios",
            "Compras y servicios de proveedores de la UE (Airbnb, Booking…): el IVA se autorrepercute, en cualquier centro (casillas 10 y 11).", Sumar(EsIntracomunitaria)));
        devengado.Add(new(ClaveIsp, "Otras operaciones con inversión del sujeto pasivo",
            "Servicios con inversión del sujeto pasivo fuera de la UE o nacionales, en cualquier centro (casillas 12 y 13).", Sumar(EsIspNoIntracomunitaria)));
        devengado.Add(new(ClaveModificacion, "Modificación de bases y cuotas (rectificativas)",
            "IVA de las facturas rectificativas emitidas, con su signo (casillas 14 y 15).", Sumar(l => EsRepercutido(l) && l.Rectificativa && l.TipoIva > 0m, ConTipo)));

        Linea303 Deducible(string clave, string concepto, string nota, Func<LineaIva, bool> grupo)
        {
            var cifras = new CifraIva[4];
            var prorrata = new decimal[4];
            for (var t = 1; t <= 4; t++)
            {
                var del = lineas.Where(l => l.Trimestre == t && EsSoportado(l) && grupo(l)).ToList();
                var cien = del.Where(l => l.Deduccion == DeduccionIva.Deducible100).ToList();
                var pro = del.Where(l => l.Deduccion == DeduccionIva.Prorrata).ToList();
                var cuotaProrrata = Redondear(pro.Sum(l => l.Cuota) * prorrataProvisional);
                var baseProrrata = Redondear(pro.Where(ConTipo).Sum(l => l.Base) * prorrataProvisional);
                cifras[t - 1] = new CifraIva(cien.Where(ConTipo).Sum(l => l.Base) + baseProrrata, cien.Sum(l => l.Cuota) + cuotaProrrata);
                prorrata[t - 1] = cuotaProrrata;
            }
            return new Linea303(clave, concepto, nota, cifras, ParteProrrata: prorrata);
        }

        const string notaProrrata = " De actividades 100 % deducibles, entero; de actividades en prorrata, × prorrata provisional, redondeado una vez por casilla y trimestre.";
        var deducible = new List<Linea303>
        {
            Deducible(ClaveInterioresCorrientes, "Operaciones interiores · bienes y servicios corrientes",
                "Gastos de proveedores nacionales y servicios con inversión del sujeto pasivo no intracomunitaria (casillas 28 y 29)." + notaProrrata,
                l => EsDeducibleInterior(l) && !l.BienInversion),
            Deducible(ClaveInterioresInversion, "Operaciones interiores · bienes de inversión",
                "Compras de inmovilizado (cuentas 20 a 23 o marca de bien de inversión) de proveedores nacionales (casillas 30 y 31)." + notaProrrata,
                l => EsDeducibleInterior(l) && l.BienInversion),
            Deducible(ClaveImportacionesCorrientes, "Importaciones · bienes corrientes",
                "IVA pagado en la aduana (DUA): solo se deduce, no se devenga (casillas 32 y 33)." + notaProrrata,
                l => EsImportacion(l) && !l.BienInversion),
            Deducible(ClaveImportacionesInversion, "Importaciones · bienes de inversión", "Casillas 34 y 35." + notaProrrata,
                l => EsImportacion(l) && l.BienInversion),
            Deducible(ClaveIntracomunitariasCorrientes, "Adquisiciones intracomunitarias · bienes y servicios corrientes",
                "La cuota autorrepercutida de las adquisiciones intracomunitarias (casillas 36 y 37)." + notaProrrata,
                l => EsIntracomunitaria(l) && !l.BienInversion),
            Deducible(ClaveIntracomunitariasInversion, "Adquisiciones intracomunitarias · bienes de inversión", "Casillas 38 y 39." + notaProrrata,
                l => EsIntracomunitaria(l) && l.BienInversion),
            new(ClaveRegularizacionProrrata, "Regularización por la prorrata definitiva",
                "Solo en el 4T: cuota sujeta a prorrata del año × (definitiva − provisional), casilla 44. 0 mientras el ejercicio no tenga la prorrata definitiva (Contabilidad › Prorrata de IVA).",
                [CifraIva.Cero, CifraIva.Cero, CifraIva.Cero, new CifraIva(0m, regularizacionProrrata)], ConBase: false),
        };
        static bool Clave(LineaIva l, IReadOnlyList<string> claves) => l.ClaveOperacion is { } c && claves.Contains(c, StringComparer.Ordinal);
        var informativo = new List<Linea303>
        {
            new(ClaveNoDeducible, "IVA soportado no deducible (mayor gasto)", "Actividades exentas sin derecho a deducir.",
                Sumar(l => EsSoportado(l) && l.Deduccion == DeduccionIva.NoDeducible)),
            new(ClaveSujetaProrrata, "IVA soportado sujeto a prorrata (cuota íntegra)", "De esta cuota solo se deduce el porcentaje de prorrata.",
                Sumar(l => EsSoportado(l) && l.Deduccion == DeduccionIva.Prorrata)),
            new(ClaveSinClasificar, "IVA soportado sin clasificar (sin centro o sin actividad)", "No entra en lo deducible hasta que el apunte tenga centro con actividad. Ver «Controles».",
                Sumar(l => EsSoportado(l) && l.Deduccion == DeduccionIva.SinCentro)),
            new(ClaveExentas, "Ventas exentas (tipo 0 %)", "Cuentan en el denominador de la prorrata.",
                Sumar(l => EsRepercutido(l) && l.TipoIva == 0m), ConCuota: false),
            new(ClaveEntregasIntracomunitarias, "Entregas intracomunitarias exentas (casilla 59)", "Ventas con exención E5.",
                Sumar(l => EsRepercutido(l) && Clave(l, ClavesEntregasIntracomunitarias)), ConCuota: false),
            new(ClaveExportaciones, "Exportaciones y operaciones asimiladas (casilla 60)", "Ventas con exención E2, E3 o E4.",
                Sumar(l => EsRepercutido(l) && Clave(l, ClavesExportaciones)), ConCuota: false),
            new(ClaveNoSujetasLocalizacion, "No sujetas por reglas de localización (casilla 120)", "Ventas con calificación N2.",
                Sumar(l => EsRepercutido(l) && Clave(l, ClavesNoSujetasLocalizacion)), ConCuota: false),
            new(ClaveSujetasIsp, "Sujetas con inversión del sujeto pasivo (casilla 122)", "Ventas con calificación S2: el IVA lo declara el cliente.",
                Sumar(l => EsRepercutido(l) && Clave(l, ClavesSujetasIsp)), ConCuota: false),
        };
        return new Resultado303(prorrataProvisional, devengado, deducible, informativo);
    }
}

/// <summary>Volumen de operaciones (bases repercutidas) de una actividad por trimestre (índices 0 a 3).</summary>
public sealed record VolumenActividad(string Actividad, DeduccionIva Deduccion, IReadOnlyList<decimal> Trimestres)
{
    public decimal Total => Trimestres.Sum();
}

/// <summary>
/// Prorrata del ejercicio (contabilidad/procesos/prorrata): provisional (la definitiva del año anterior), real acumulada (la legal),
/// definitiva fijada (la del gestor, null si aún no se ha fijado) y regularización por trimestre (índices 0 a 3) con la aplicada: la
/// definitiva si está fijada y, si no, la real (entonces es una estimación). Porcentajes en tanto por uno.
/// </summary>
public sealed record ResultadoProrrata(
    decimal Provisional, bool ProvisionalInformada, decimal Numerador, decimal Denominador, decimal Cociente, decimal Real,
    IReadOnlyList<VolumenActividad> Volumen, IReadOnlyList<decimal> CuotaSujeta, IReadOnlyList<decimal> DeducidoProvisional,
    IReadOnlyList<decimal> DeducibleReal, decimal? Definitiva = null, IReadOnlyList<decimal>? DeducibleDefinitiva = null)
{
    /// <summary>La que se usa para regularizar: la definitiva fijada o, sin ella, la real acumulada.</summary>
    public decimal Aplicada => Definitiva ?? Real;

    public bool DefinitivaFijada => Definitiva is not null;

    /// <summary>Deducible con la prorrata aplicada, por trimestre.</summary>
    public IReadOnlyList<decimal> DeducibleAplicada => DeducibleDefinitiva ?? DeducibleReal;

    /// <summary>Regularización a favor (+) o en contra (−) de un trimestre (1 a 4) o del año (0), con la prorrata aplicada.</summary>
    public decimal Regularizacion(int trimestre) => trimestre == 0
        ? Enumerable.Range(1, 4).Sum(Regularizacion)
        : DeducibleAplicada[trimestre - 1] - DeducidoProvisional[trimestre - 1];

    /// <summary>Cuota sujeta a prorrata del año.</summary>
    public decimal CuotaSujetaAnual => CuotaSujeta.Sum();
}

/// <summary>
/// Cálculo de la prorrata (sin BD), con las mismas fórmulas que la hoja «Prorrata» del informe:
/// <list type="bullet">
/// <item>Real acumulada = bases repercutidas de actividades 100 % deducibles ÷ total de bases repercutidas del ejercicio
/// (todas las líneas con trimestre), redondeada al entero superior (art. 104 LIVA). Para que el ruido decimal no suba un punto,
/// el cociente en porcentaje se redondea antes a 6 decimales: 19,88 % → 20 %, 19,00 % → 19 %, 19,001 % → 20 %. Sin ventas, 0.</item>
/// <item>Por trimestre: cuota sujeta a prorrata (todas las soportadas en prorrata), deducido con la provisional (las dos líneas de
/// partes de prorrata de las casillas del 303), deducible con la real = Redondear(cuota sujeta × real, 2), deducible con la aplicada (la definitiva fijada o,
/// sin ella, la real) y regularización = deducible con la aplicada − deducido. La del año es la suma de los cuatro trimestres, como
/// en la hoja «Prorrata» del informe.</item>
/// </list>
/// </summary>
public static class CalculoProrrata
{
    /// <summary>Redondeo al entero superior del porcentaje, en tanto por uno: Techo(Redondear(cociente × 100, 6)) / 100.</summary>
    public static decimal RedondearArriba(decimal cociente) => Math.Ceiling(Math.Round(cociente * 100m, 6, MidpointRounding.AwayFromZero)) / 100m;

    /// <param name="provisional">Prorrata provisional en tanto por uno; null si el ejercicio no la tiene informada (se usa 100 %).</param>
    /// <param name="definitiva">Prorrata definitiva fijada en tanto por uno; null si aún no se ha fijado (se regulariza con la real).</param>
    public static ResultadoProrrata Calcular(IReadOnlyList<LineaIva> lineas, decimal? provisional, Resultado303 r303, decimal? definitiva = null)
    {
        var rep = lineas.Where(l => l.Trimestre is >= 1 and <= 4 && l.Sentido == SentidoIva.Repercutido).ToList();
        var numerador = rep.Where(l => l.Deduccion == DeduccionIva.Deducible100).Sum(l => l.Base);
        var denominador = rep.Sum(l => l.Base);
        var cociente = denominador == 0m ? 0m : numerador / denominador;
        var real = RedondearArriba(cociente);
        var volumen = rep.GroupBy(l => (l.Actividad ?? "", l.Deduccion))
            .OrderByDescending(g => g.Key.Deduccion == DeduccionIva.Deducible100).ThenBy(g => g.Key.Item1, StringComparer.CurrentCulture)
            .Select(g => new VolumenActividad(g.Key.Item1, g.Key.Deduccion, Enumerable.Range(1, 4).Select(t => g.Where(l => l.Trimestre == t).Sum(l => l.Base)).ToList()))
            .ToList();
        var sujeta = Enumerable.Range(1, 4).Select(t => lineas.Where(l => l.Trimestre == t && l.Sentido == SentidoIva.Soportado && l.Deduccion == DeduccionIva.Prorrata).Sum(l => l.Cuota)).ToList();
        var deducido = Enumerable.Range(1, 4).Select(r303.DeducidoProrrata).ToList();
        var deducibleReal = sujeta.Select(c => Calculo303.Redondear(c * real)).ToList();
        var deducibleDefinitiva = definitiva is decimal d ? sujeta.Select(c => Calculo303.Redondear(c * d)).ToList() : null;
        return new ResultadoProrrata(provisional ?? 1m, provisional is not null, numerador, denominador, cociente, real, volumen, sujeta, deducido, deducibleReal,
            definitiva, deducibleDefinitiva);
    }
}

/// <summary>El 303 y la prorrata de un ejercicio calculados juntos.</summary>
public sealed record IvaEjercicio(Resultado303 Modelo303, ResultadoProrrata Prorrata);

/// <summary>
/// Calcula juntos el 303 y la prorrata del ejercicio (contabilidad/procesos/prorrata): primero el 303 con la provisional, después la
/// prorrata y, si el ejercicio tiene la definitiva fijada, otra vez el 303 con la regularización anual en el 4T. Es la única forma
/// en que la liquidación, el informe para la gestoría y la pantalla de la prorrata obtienen el 303, para que den lo mismo.
/// </summary>
public static class CalculoIvaEjercicio
{
    /// <param name="provisional">Prorrata provisional en tanto por uno (null: no informada, se usa 100 %).</param>
    /// <param name="definitiva">Prorrata definitiva fijada en tanto por uno (null: no fijada, sin regularización en el 303).</param>
    public static IvaEjercicio Calcular(IReadOnlyList<LineaIva> lineas, decimal? provisional, decimal? definitiva)
    {
        var base303 = Calculo303.Calcular(lineas, provisional ?? 1m);
        var prorrata = CalculoProrrata.Calcular(lineas, provisional, base303, definitiva);
        var r303 = definitiva is null ? base303 : Calculo303.Calcular(lineas, provisional ?? 1m, prorrata.Regularizacion(0));
        return new IvaEjercicio(r303, prorrata);
    }
}
