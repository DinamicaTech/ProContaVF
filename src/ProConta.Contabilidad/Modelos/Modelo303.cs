using System.Globalization;
using ProConta.Contabilidad.Iva;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Quien declara el 303: la sociedad (NIF y razón social) y la cuenta de domiciliación (Sociedades.IbanDomiciliacion).</summary>
public sealed record Declarante303(string Nif, string RazonSocial, string? Iban);

/// <summary>
/// Autoliquidación rectificativa (la «complementaria» desde 2025): el número de justificante de la anterior y lo que salió a ingresar
/// en ella (su casilla 71 si fue positiva; 0 si salió a compensar o a cero), que va en la casilla 70.
/// </summary>
public sealed record Rectificativa303(string JustificanteAnterior, decimal IngresadoAnterior);

/// <summary>
/// El 303 de un trimestre con sus casillas (número de casilla → importe) listo para el fichero, la forma de pago (tipo de
/// declaración: U domiciliación, C a compensar, N sin resultado) y lo que impide presentarlo (<see cref="Errores"/>) o conviene
/// revisar (<see cref="Avisos"/>).
/// </summary>
public sealed record Declaracion303(int Ejercicio, int Trimestre, Declarante303 Declarante, IReadOnlyDictionary<int, decimal> Casillas, char TipoDeclaracion,
    Rectificativa303? Rectificativa, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    /// <summary>Periodo como lo escribe la AEAT: «1T» … «4T».</summary>
    public string Periodo => $"{Trimestre}T";

    public decimal Casilla(int numero) => Casillas.TryGetValue(numero, out var v) ? v : 0m;

    /// <summary>
    /// Opción por la prorrata especial (posición 115 del fichero, Nota 6 del diseño): se marca en el último periodo (4T) del ejercicio que la
    /// aplica; en los demás periodos va en blanco («2»).
    /// </summary>
    public bool OpcionProrrataEspecial { get; init; }

    /// <summary>Resultado del régimen general (casilla 46 = 66 con el 100 % atribuible al Estado): lo que se liquida en la contabilidad, antes de compensar.</summary>
    public decimal ResultadoLiquidacion => Casilla(66);

    /// <summary>Resultado de la autoliquidación (casilla 71): positivo a ingresar, negativo a compensar.</summary>
    public decimal Resultado => Casilla(71);

    /// <summary>Lo que se ingresa, domiciliado o con NRC (0 si no sale a ingresar).</summary>
    public decimal ImporteAIngresar => TipoDeclaracion is 'U' or 'I' ? Casilla(71) : 0m;

    public bool Valida => Errores.Count == 0;

    /// <summary>Forma de pago en palabras.</summary>
    public string FormaPago => TipoDeclaracion switch
    {
        'U' => "Domiciliación del ingreso",
        'I' => "Ingreso con NRC (pagado antes en el banco o en la Sede)",
        'C' => "A compensar en los trimestres siguientes",
        'N' => "Sin resultado (cero)",
        _ => TipoDeclaracion.ToString(),
    };
}

/// <summary>
/// Casillas del modelo 303 de un trimestre (contabilidad/modelos-tributarios/modelo-303) desde el cálculo del 303 (<see cref="Calculo303"/>),
/// con el diseño de registro de 2026 (sources/aeat/DR303e26v101.xlsx). Sin BD.
/// <list type="bullet">
/// <item>01-09: repercutido al 4, 10 y 21 % sin rectificativas; 10/11 intracomunitarias; 12/13 inversión del sujeto pasivo; 14/15
/// rectificativas emitidas; 27 total devengado.</item>
/// <item>28-39: lo deducible por casilla (interiores, importaciones e intracomunitarias, corrientes o de inversión); 40/41 y 42/43 a 0;
/// 44 la regularización de la prorrata definitiva (4T); 45 total a deducir; 46 resultado.</item>
/// <item>Página 3: 59, 60, 120 y 122 informativas; 64 = 46; 65 = 100 % atribuible al Estado; 66 = 64; 77 = 0 (sin pago diferido del IVA
/// de importación); 110 cuotas a compensar pendientes de periodos anteriores (el crédito de la 4700, como la liquidación), 78 lo
/// aplicado (hasta dejar el resultado a cero) y 87 lo que queda; 69 = 66 + 77 − 78; 70 lo ingresado en la autoliquidación anterior (solo
/// en una rectificativa); 71 = 69 − 70; 111 lo que la rectificativa devuelve de lo ingresado.</item>
/// <item>Forma de pago (propietario 2026-11-17 14:38): a ingresar, domiciliación (U) con el IBAN de la sociedad; negativo, a compensar
/// (C), también en el 4T (nunca devolución); cero, N. Fuera del plazo de domiciliación (hasta el 15 del mes siguiente; en el 4T, el
/// 25 de enero) lo que sale a ingresar se paga antes en el banco o en la Sede y se presenta con su NRC (forma I; propietario 2026-11-17 15:32).</item>
/// </list>
/// </summary>
public static class Modelo303
{
    /// <summary>Primer ejercicio con el diseño de registro que genera <see cref="Fichero303"/> (DR303e26v101): los anteriores usan otro.</summary>
    public const int PrimerEjercicio = 2026;

    /// <summary>Tipos del régimen general que tienen casilla en el 303 de 2026: tipo → (casilla de base, casilla de cuota).</summary>
    public static readonly IReadOnlyDictionary<decimal, (int Base, int Cuota)> CasillasTipo = new Dictionary<decimal, (int, int)>
    {
        [4m] = (1, 3), [10m] = (4, 6), [21m] = (7, 9),
    };

    /// <summary>Casillas de lo deducible por cuotas soportadas: línea del cálculo → (base, cuota).</summary>
    public static readonly IReadOnlyList<(string Clave, int Base, int Cuota)> CasillasDeduccion =
    [
        (Calculo303.ClaveInterioresCorrientes, 28, 29), (Calculo303.ClaveInterioresInversion, 30, 31),
        (Calculo303.ClaveImportacionesCorrientes, 32, 33), (Calculo303.ClaveImportacionesInversion, 34, 35),
        (Calculo303.ClaveIntracomunitariasCorrientes, 36, 37), (Calculo303.ClaveIntracomunitariasInversion, 38, 39),
    ];

    /// <summary>Las casillas que calcula el programa, en el orden del modelo, con su concepto (para la pantalla y la comparación con el PDF).</summary>
    public static readonly IReadOnlyList<(int Casilla, string Concepto)> Conceptos =
    [
        (1, "Régimen general 4 % · base"), (3, "Régimen general 4 % · cuota"), (4, "Régimen general 10 % · base"), (6, "Régimen general 10 % · cuota"),
        (7, "Régimen general 21 % · base"), (9, "Régimen general 21 % · cuota"), (10, "Adquisiciones intracomunitarias · base"), (11, "Adquisiciones intracomunitarias · cuota"),
        (12, "Otras operaciones con inversión del sujeto pasivo · base"), (13, "Otras operaciones con inversión del sujeto pasivo · cuota"),
        (14, "Modificación de bases y cuotas · base"), (15, "Modificación de bases y cuotas · cuota"), (27, "Total cuota devengada"),
        (28, "Operaciones interiores corrientes · base"), (29, "Operaciones interiores corrientes · cuota"),
        (30, "Operaciones interiores con bienes de inversión · base"), (31, "Operaciones interiores con bienes de inversión · cuota"),
        (32, "Importaciones de bienes corrientes · base"), (33, "Importaciones de bienes corrientes · cuota"),
        (34, "Importaciones de bienes de inversión · base"), (35, "Importaciones de bienes de inversión · cuota"),
        (36, "Adquisiciones intracomunitarias corrientes · base"), (37, "Adquisiciones intracomunitarias corrientes · cuota"),
        (38, "Adquisiciones intracomunitarias de bienes de inversión · base"), (39, "Adquisiciones intracomunitarias de bienes de inversión · cuota"),
        (40, "Rectificación de deducciones · base"), (41, "Rectificación de deducciones · cuota"), (44, "Regularización por la prorrata definitiva"),
        (45, "Total a deducir"), (46, "Resultado régimen general"),
        (59, "Entregas intracomunitarias"), (60, "Exportaciones y operaciones asimiladas"), (120, "No sujetas por reglas de localización"),
        (122, "Sujetas con inversión del sujeto pasivo"), (64, "Suma de resultados"), (65, "% atribuible a la Administración del Estado"),
        (66, "Atribuible a la Administración del Estado"), (77, "IVA a la importación pendiente de ingreso"), (110, "Cuotas a compensar pendientes de periodos anteriores"),
        (78, "Cuotas a compensar aplicadas en este periodo"), (87, "Cuotas a compensar pendientes para periodos posteriores"), (69, "Resultado de la autoliquidación"),
        (70, "Ingresado en la autoliquidación anterior"), (71, "Resultado"), (111, "Rectificación · importe a devolver"),
    ];

    /// <summary>Casillas sin signo del diseño (Num): no admiten importes negativos.</summary>
    public static readonly IReadOnlySet<int> SinSigno = new HashSet<int> { 1, 3, 4, 6, 7, 9, 10, 11, 12, 13, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 77, 110, 78, 87, 70, 109, 112, 111 };

    /// <param name="r">El 303 del ejercicio (de <see cref="CalculoIvaEjercicio"/>).</param>
    /// <param name="pendienteCompensar">Cuotas a compensar de periodos anteriores pendientes (casilla 110): el saldo deudor de las 4700 el último día del trimestre sin su liquidación.</param>
    /// <param name="rectificativa">Si es una autoliquidación rectificativa, la anterior; null en la primera.</param>
    /// <param name="conNrc">Si sale a ingresar, se paga con NRC (forma I) en lugar de domiciliarlo (fuera del plazo de domiciliación).</param>
    public static Declaracion303 Calcular(Resultado303 r, int ejercicio, int trimestre, Declarante303 declarante, decimal pendienteCompensar, Rectificativa303? rectificativa = null,
        bool conNrc = false)
    {
        if (trimestre is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(trimestre), "El trimestre va de 1 a 4.");
        var c = new SortedDictionary<int, decimal>();
        var errores = new List<string>();
        var avisos = new List<string>();
        void Poner(int casilla, decimal valor) => c[casilla] = c.GetValueOrDefault(casilla) + valor;

        // Devengado.
        foreach (var l in r.Devengado.Where(l => l.Clave.StartsWith("dev.rep.", StringComparison.Ordinal)))
        {
            var tipoIva = decimal.Parse(l.Clave["dev.rep.".Length..], CultureInfo.InvariantCulture);
            var cifra = l.En(trimestre);
            if (CasillasTipo.TryGetValue(tipoIva, out var cas)) { Poner(cas.Base, cifra.Base); Poner(cas.Cuota, cifra.Cuota); }
            else if (cifra.Base != 0m || cifra.Cuota != 0m)
                errores.Add($"Hay IVA repercutido al {tipoIva:0.##} % ({Euros(cifra.Cuota)}) y el 303 de {ejercicio} solo tiene casillas para el 4, el 10 y el 21 %. " +
                    "Revisa en el informe para la gestoría (hoja «Datos IVA») las facturas con ese tipo y corrige su tipo de IVA.");
        }
        var intra = r.Linea(Calculo303.ClaveIntracomunitarias).En(trimestre);
        Poner(10, intra.Base); Poner(11, intra.Cuota);
        var isp = r.Linea(Calculo303.ClaveIsp).En(trimestre);
        Poner(12, isp.Base); Poner(13, isp.Cuota);
        var mod = r.Linea(Calculo303.ClaveModificacion).En(trimestre);
        Poner(14, mod.Base); Poner(15, mod.Cuota);
        c[27] = new[] { 3, 6, 9, 11, 13, 15 }.Sum(n => c.GetValueOrDefault(n));

        // Deducible.
        foreach (var (clave, b, q) in CasillasDeduccion)
        {
            var cifra = r.Linea(clave).En(trimestre);
            Poner(b, cifra.Base); Poner(q, cifra.Cuota);
        }
        c[40] = 0m; c[41] = 0m; c[42] = 0m; c[43] = 0m;
        c[44] = r.Linea(Calculo303.ClaveRegularizacionProrrata).En(trimestre).Cuota;
        c[45] = new[] { 29, 31, 33, 35, 37, 39, 41, 42, 43, 44 }.Sum(n => c.GetValueOrDefault(n));
        c[46] = c[27] - c[45];

        // Información adicional (página 3).
        c[59] = r.Linea(Calculo303.ClaveEntregasIntracomunitarias).En(trimestre).Base;
        c[60] = r.Linea(Calculo303.ClaveExportaciones).En(trimestre).Base;
        c[120] = r.Linea(Calculo303.ClaveNoSujetasLocalizacion).En(trimestre).Base;
        c[122] = r.Linea(Calculo303.ClaveSujetasIsp).En(trimestre).Base;

        // Resultado.
        c[76] = 0m;
        c[64] = c[46] + c[76];
        c[65] = 100m;
        c[66] = c[64];
        c[77] = 0m;
        var pendiente = Math.Max(0m, pendienteCompensar);
        c[110] = pendiente;
        c[78] = Math.Min(pendiente, Math.Max(0m, c[66] + c[77]));
        c[87] = c[110] - c[78];
        c[68] = 0m;
        c[108] = 0m;
        c[69] = c[66] + c[77] - c[78] + c[68] + c[108];
        c[70] = rectificativa is null ? 0m : Math.Max(0m, rectificativa.IngresadoAnterior);
        c[109] = 0m;
        c[112] = 0m;
        c[71] = c[69] - c[70] + c[109] - c[112];
        // Rectificativa que rebaja lo ingresado: la parte que se devuelve va en la 111 y el tipo de declaración se decide sin ella.
        c[111] = rectificativa is not null && c[71] < 0m && c[70] > 0m ? Math.Min(-c[71], c[70]) : 0m;
        var paraForma = c[71] + c[111];
        var tipo = paraForma > 0m ? (conNrc ? 'I' : 'U') : paraForma < 0m ? 'C' : 'N';

        // Comprobaciones antes del fichero.
        if (ejercicio < PrimerEjercicio)
            errores.Add($"El programa genera el 303 con el diseño de registro de {PrimerEjercicio} y la AEAT usa otro para {ejercicio}: presenta el 303 de {ejercicio} desde la Sede o con la gestoría.");
        if (string.IsNullOrWhiteSpace(declarante.Nif))
            errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        if (tipo == 'U' || c[111] > 0m)
        {
            if (string.IsNullOrWhiteSpace(declarante.Iban))
                errores.Add($"El 303 sale a ingresar ({Euros(c[71])}) y la sociedad no tiene cuenta de domiciliación: escríbela en Configuración › Parámetros y tablas › Empresas («IBAN de domiciliación»).");
            else if (!Ibanes.EsValido(declarante.Iban))
                errores.Add($"El IBAN de domiciliación de la sociedad ({declarante.Iban}) no es válido: corrígelo en Configuración › Parámetros y tablas › Empresas.");
        }
        foreach (var n in c.Keys.Where(n => SinSigno.Contains(n) && c[n] < 0m))
            errores.Add($"La casilla {n:00} sale negativa ({Euros(c[n])}) y el modelo no lo admite. Suele deberse a abonos de proveedores mayores que las compras del trimestre: " +
                "revisa las facturas rectificativas recibidas en el informe para la gestoría (hoja «Datos IVA»).");
        if (c[44] != 0m && trimestre != 4) avisos.Add("La regularización de la prorrata solo se declara en el 4T.");
        return new Declaracion303(ejercicio, trimestre, declarante, c, tipo, rectificativa, errores, avisos);
    }

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
