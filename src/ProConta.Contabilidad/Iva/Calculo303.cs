namespace ProConta.Contabilidad.Iva;

/// <summary>Base y cuota de una casilla.</summary>
public readonly record struct CifraIva(decimal Base, decimal Cuota)
{
    public static CifraIva Cero => default;
    public static CifraIva operator +(CifraIva a, CifraIva b) => new(a.Base + b.Base, a.Cuota + b.Cuota);
}

/// <summary>Línea del modelo 303 calculada, con las cifras de los cuatro trimestres (índices 0 a 3) y su suma en el año.</summary>
/// <param name="Clave">Identificador estable de la línea (lo usan el libro Excel y las pruebas).</param>
/// <param name="ConBase">La línea muestra base (las totales y la rectificación solo cuota).</param>
/// <param name="ConCuota">La línea muestra cuota (las ventas exentas solo base).</param>
public sealed record Linea303(string Clave, string Concepto, string Nota, IReadOnlyList<CifraIva> Trimestres, bool ConBase = true, bool ConCuota = true)
{
    /// <summary>Cifra de un trimestre (1 a 4) o del año (0): la suma de los trimestres, ya redondeados.</summary>
    public CifraIva En(int trimestre) => trimestre == 0 ? Trimestres.Aggregate(CifraIva.Cero, (a, b) => a + b) : Trimestres[trimestre - 1];
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

    /// <summary>Resultado del trimestre (1 a 4) o del año (0): a ingresar si es positivo, a compensar si es negativo.</summary>
    public decimal Resultado(int trimestre) => TotalDevengado(trimestre) - TotalDeducible(trimestre);
}

/// <summary>
/// Cálculo del 303 por trimestre (sin BD). Reproduce las fórmulas de la hoja «IVA 303» del informe para la gestoría, que suman
/// la tabla de líneas de IVA:
/// <list type="bullet">
/// <item>Devengado: el IVA repercutido de cada tipo con cuota (sea cual sea el centro), las adquisiciones intracomunitarias
/// (toda línea soportada intracomunitaria), la inversión del sujeto pasivo no intracomunitaria y las importaciones con IVA
/// autorrepercutido.</item>
/// <item>Deducible: interiores de actividades 100 % deducibles; interiores en prorrata = Redondear(cuota × prorrata provisional,
/// 2); intracomunitarias, inversión del sujeto pasivo e importación de actividades 100 % deducibles; ídem en prorrata;
/// regularización de la prorrata definitiva (solo en el 4T, la que da <see cref="CalculoProrrata"/> cuando el ejercicio tiene la
/// definitiva fijada; si no, 0). Las líneas «Sin centro» y las no deducibles no deducen.</item>
/// <item>Redondeo: las bases y cuotas son las del libro (céntimos) y se suman sin redondear; lo único que se redondea es el
/// producto por la prorrata, a 2 decimales (alejándose de cero, como REDONDEAR de Excel), sobre el total de cada línea del modelo
/// y trimestre, no por apunte. El año es la suma de los cuatro trimestres ya redondeados.</item>
/// <item>Las líneas con trimestre 0 (fecha fuera del ejercicio) no entran en ningún trimestre.</item>
/// </list>
/// </summary>
public static class Calculo303
{
    public const string ClaveIntracomunitarias = "dev.intracomunitarias";
    public const string ClaveIsp = "dev.isp";
    public const string ClaveImportaciones = "dev.importaciones";
    public const string ClaveInteriores100 = "ded.interiores100";
    public const string ClaveInterioresProrrata = "ded.interioresProrrata";
    public const string ClaveAutorrepercutidas100 = "ded.autorrepercutidas100";
    public const string ClaveAutorrepercutidasProrrata = "ded.autorrepercutidasProrrata";
    public const string ClaveRectificacion = "ded.rectificacion";
    public const string ClaveNoDeducible = "inf.noDeducible";
    public const string ClaveSujetaProrrata = "inf.sujetaProrrata";
    public const string ClaveSinClasificar = "inf.sinClasificar";
    public const string ClaveExentas = "inf.exentas";

    /// <summary>Clave de la línea de repercutido de un tipo, por ejemplo «dev.rep.21».</summary>
    public static string ClaveRepercutido(decimal tipo) => "dev.rep." + tipo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Tipos de repercutido que salen siempre (aunque no tengan cuota), además de los que aparezcan en las líneas.</summary>
    public static readonly IReadOnlyList<decimal> TiposFijos = [21m, 10m, 4m];

    /// <summary>Redondeo de las cifras por prorrata: 2 decimales, alejándose de cero (REDONDEAR de Excel).</summary>
    public static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // Criterios de cada línea del modelo (los mismos que las fórmulas del libro).
    public static bool EsRepercutido(LineaIva l) => l.Sentido == SentidoIva.Repercutido;
    public static bool EsSoportado(LineaIva l) => l.Sentido == SentidoIva.Soportado;
    public static bool EsIntracomunitaria(LineaIva l) => EsSoportado(l) && l.Operacion == OperacionIva.Intracomunitaria;
    public static bool EsIspNoIntracomunitaria(LineaIva l) => EsSoportado(l) && l.Isp && l.Operacion != OperacionIva.Intracomunitaria;
    public static bool EsImportacion(LineaIva l) => EsSoportado(l) && l.Operacion == OperacionIva.Importacion && !l.Isp;
    public static bool EsInterior(LineaIva l) => EsSoportado(l) && l.Operacion == OperacionIva.Nacional && !l.Isp;
    public static bool EsAutorrepercutida(LineaIva l) => EsIntracomunitaria(l) || EsIspNoIntracomunitaria(l) || EsImportacion(l);

    /// <param name="regularizacionProrrata">Regularización anual de la prorrata (contabilidad/procesos/prorrata): positiva a favor,
    /// negativa en contra. Va en la línea de regularización del 4T; 0 mientras no haya prorrata definitiva.</param>
    public static Resultado303 Calcular(IReadOnlyList<LineaIva> lineas, decimal prorrataProvisional, decimal regularizacionProrrata = 0m)
    {
        CifraIva[] Sumar(Func<LineaIva, bool> filtro, Func<LineaIva, bool>? filtroBase = null, bool prorrata = false) =>
            Enumerable.Range(1, 4).Select(t =>
            {
                var del = lineas.Where(l => l.Trimestre == t && filtro(l)).ToList();
                var b = del.Where(l => filtroBase?.Invoke(l) ?? true).Sum(l => l.Base);
                var c = del.Sum(l => l.Cuota);
                return new CifraIva(b, prorrata ? Redondear(c * prorrataProvisional) : c);
            }).ToArray();

        var tipos = TiposFijos.Concat(lineas.Where(l => EsRepercutido(l) && l.TipoIva > 0m).Select(l => l.TipoIva)).Distinct().OrderByDescending(t => t).ToList();
        var devengado = tipos.Select(t => new Linea303(ClaveRepercutido(t), $"Régimen general {t:0.##} %",
                t == 21m ? "Todo el IVA repercutido de este tipo, sea cual sea el centro de coste." : "",
                Sumar(l => EsRepercutido(l) && l.TipoIva == t)))
            .ToList();
        devengado.Add(new(ClaveIntracomunitarias, "Adquisiciones intracomunitarias",
            "Compras y servicios de proveedores de la UE (Airbnb, Booking…): el IVA se autorrepercute, en cualquier centro.", Sumar(EsIntracomunitaria)));
        devengado.Add(new(ClaveIsp, "Inversión del sujeto pasivo (no intracomunitaria)",
            "Servicios con inversión del sujeto pasivo fuera de la UE o nacionales, en cualquier centro.", Sumar(EsIspNoIntracomunitaria)));
        devengado.Add(new(ClaveImportaciones, "Importaciones con IVA autorrepercutido",
            "A confirmar con la gestoría: una importación de bienes con DUA no se devenga en el 303.", Sumar(EsImportacion)));

        bool Ded(LineaIva l, DeduccionIva d) => l.Deduccion == d;
        bool ConTipo(LineaIva l) => l.TipoIva > 0m;
        var deducible = new List<Linea303>
        {
            new(ClaveInteriores100, "Operaciones interiores · 100 % deducible", "Gastos de centros cuya actividad deduce todo el IVA.",
                Sumar(l => EsInterior(l) && Ded(l, DeduccionIva.Deducible100), ConTipo)),
            new(ClaveInterioresProrrata, "Operaciones interiores · prorrata", "Gastos comunes: cuota soportada × prorrata provisional, redondeado por trimestre.",
                Sumar(l => EsInterior(l) && Ded(l, DeduccionIva.Prorrata), ConTipo, prorrata: true)),
            new(ClaveAutorrepercutidas100, "Intracomunitarias, ISP e importación · 100 % deducible", "La cuota autorrepercutida de centros 100 % deducibles se deduce entera.",
                Sumar(l => EsAutorrepercutida(l) && Ded(l, DeduccionIva.Deducible100))),
            new(ClaveAutorrepercutidasProrrata, "Intracomunitarias, ISP e importación · prorrata", "Cuota autorrepercutida de centros en prorrata × prorrata provisional.",
                Sumar(l => EsAutorrepercutida(l) && Ded(l, DeduccionIva.Prorrata), prorrata: true)),
            new(ClaveRectificacion, "Regularización de la prorrata definitiva",
                "Solo en el 4T: cuota sujeta a prorrata del año × (definitiva − provisional). 0 mientras el ejercicio no tenga la prorrata definitiva (Contabilidad › Prorrata de IVA).",
                [CifraIva.Cero, CifraIva.Cero, CifraIva.Cero, new CifraIva(0m, regularizacionProrrata)], ConBase: false),
        };
        var informativo = new List<Linea303>
        {
            new(ClaveNoDeducible, "IVA soportado no deducible (mayor gasto)", "Actividades exentas sin derecho a deducir.",
                Sumar(l => EsSoportado(l) && Ded(l, DeduccionIva.NoDeducible))),
            new(ClaveSujetaProrrata, "IVA soportado sujeto a prorrata (cuota íntegra)", "De esta cuota solo se deduce el porcentaje de prorrata.",
                Sumar(l => EsSoportado(l) && Ded(l, DeduccionIva.Prorrata))),
            new(ClaveSinClasificar, "IVA soportado sin clasificar (sin centro o sin actividad)", "No entra en lo deducible hasta que el apunte tenga centro con actividad. Ver «Controles».",
                Sumar(l => EsSoportado(l) && Ded(l, DeduccionIva.SinCentro))),
            new(ClaveExentas, "Ventas exentas (tipo 0 %)", "Cuentan en el denominador de la prorrata.",
                Sumar(l => EsRepercutido(l) && l.TipoIva == 0m), ConCuota: false),
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
/// prorrata del 303), deducible con la real = Redondear(cuota sujeta × real, 2), deducible con la aplicada (la definitiva fijada o,
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
        var deducido = Enumerable.Range(1, 4).Select(t =>
            r303.Linea(Calculo303.ClaveInterioresProrrata).En(t).Cuota + r303.Linea(Calculo303.ClaveAutorrepercutidasProrrata).En(t).Cuota).ToList();
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
