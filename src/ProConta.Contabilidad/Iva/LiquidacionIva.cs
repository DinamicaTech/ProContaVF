using System.Globalization;

namespace ProConta.Contabilidad.Iva;

/// <summary>Saldo de una subcuenta (Debe − Haber) a una fecha.</summary>
public sealed record SaldoSubcuenta(string Cuenta, decimal Saldo);

/// <summary>
/// Cuentas de la liquidación del IVA de la sociedad y el ejercicio (organizacion/sociedades-ejercicios, esquema 56); vacías, las
/// de siempre: 4750000 Hacienda acreedora, 4700000 Hacienda deudora, 6341000 ajustes negativos y 6391000 ajustes positivos.
/// </summary>
public sealed record CuentasLiquidacionIva(string HaciendaAcreedora, string HaciendaDeudora, string AjustesNegativos, string AjustesPositivos)
{
    public const string AcreedoraDefecto = "4750000";
    public const string DeudoraDefecto = "4700000";
    public const string NegativosDefecto = "6341000";
    public const string PositivosDefecto = "6391000";

    public static CuentasLiquidacionIva Defecto { get; } = new(AcreedoraDefecto, DeudoraDefecto, NegativosDefecto, PositivosDefecto);

    public static CuentasLiquidacionIva Con(string? acreedora, string? deudora, string? negativos, string? positivos) => new(
        string.IsNullOrWhiteSpace(acreedora) ? AcreedoraDefecto : acreedora.Trim(),
        string.IsNullOrWhiteSpace(deudora) ? DeudoraDefecto : deudora.Trim(),
        string.IsNullOrWhiteSpace(negativos) ? NegativosDefecto : negativos.Trim(),
        string.IsNullOrWhiteSpace(positivos) ? PositivosDefecto : positivos.Trim());
}

/// <summary>
/// Datos de un trimestre para su liquidación: los saldos de las 472 y 477 (sin la 4729) y de las 4700 con saldo deudor el último
/// día del trimestre, sin contar la propia liquidación; el 303 calculado desde los libros con la prorrata provisional; las líneas
/// de IVA (para lo no deducible que está en una 472 y lo que no tiene clasificar); y el importe a liquidar que fija el operador
/// (null: el calculado).
/// </summary>
public sealed record DatosLiquidacionIva(
    int Ejercicio, int Trimestre, IReadOnlyList<SaldoSubcuenta> Saldos, IReadOnlyList<SaldoSubcuenta> Credito4700, Resultado303 Modelo303,
    IReadOnlyList<LineaIva> Lineas, CuentasLiquidacionIva Cuentas, decimal? ImporteFijado = null, int? CentroAjustes = null);

/// <summary>Una línea del asiento propuesto: cuenta, importes, concepto y centro de coste (las de gasto e ingreso lo llevan si se sabe).</summary>
public sealed record LineaLiquidacionIva(string Cuenta, decimal Debe, decimal Haber, string Concepto, int? IdCentroCoste = null);

/// <summary>
/// Propuesta de liquidación del IVA de un trimestre (contabilidad/procesos/liquidacion-iva): el asiento cuadrado y las cifras que
/// lo explican. Signos: el resultado es positivo a ingresar y negativo a compensar; el ajuste es positivo si sale a pagar menos de
/// lo que dicen las cuentas (6391, Haber) y negativo si sale a pagar más (6341, Debe).
/// </summary>
public sealed record PropuestaLiquidacionIva(
    int Ejercicio, int Trimestre, DateTime Fecha, IReadOnlyList<LineaLiquidacionIva> Lineas,
    decimal Repercutido477, decimal Soportado472, decimal Devengado303, decimal Deducible303, decimal NoDeducibleProrrata, decimal NoDeducible472,
    decimal SinClasificar472, decimal ResultadoCalculado, decimal ImporteLiquidado, decimal CreditoAnterior, decimal Compensado,
    decimal DiferenciaLibros, decimal DiferenciaFijada, decimal Ajuste, decimal Prorrata, IReadOnlyList<string> Avisos)
{
    /// <summary>Lo que queda a ingresar (4750) después de compensar; 0 si sale a compensar.</summary>
    public decimal AIngresar => ImporteLiquidado > 0m ? ImporteLiquidado - Compensado : 0m;

    /// <summary>Lo que queda a compensar o devolver (4700); 0 si sale a ingresar.</summary>
    public decimal ACompensar => ImporteLiquidado < 0m ? -ImporteLiquidado : 0m;

    public bool HayAjuste => Ajuste != 0m;
    public bool ImporteModificado => DiferenciaFijada != 0m;
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
    public string NombreTrimestre => CalculoLiquidacionIva.Nombre(Trimestre);
}

/// <summary>
/// Cálculo de la liquidación del IVA de un trimestre en un solo asiento (contabilidad/procesos/liquidacion-iva, opción A del
/// propietario 2026-11-13 12:00), sin BD. Fecha: el último día del trimestre.
/// <list type="bullet">
/// <item>Cada 472 y 477 se deja a cero por su saldo. La 4729 (regularización de la prorrata) solo en el 4T, donde lleva la
/// regularización anual que grabó la prorrata el 31/12 (contabilidad/procesos/prorrata) y que el 303 del 4T deduce.</item>
/// <item>IVA no deducible por prorrata a la cuenta de ajustes negativos (6341): la cuota sujeta a prorrata del 303 menos lo que se
/// deduce de ella (la parte de prorrata de cada casilla del 303, redondeada como el 303). También va a la 6341 el IVA de actividades no
/// deducibles que se contabilizó en una 472 (los datos importados lo traen así).</item>
/// <item>Importe del trimestre: el resultado del 303 (devengado − deducible), o el que fije el operador para ajustarlo a lo presentado
/// (propietario 2026-11-16 08:37). A ingresar: primero se compensa el crédito de la 4700 de trimestres anteriores (V4, abona la
/// 4700) y el resto va a la 4750; a compensar: carga la 4700.</item>
/// <item>Lo que no cuadra (cuentas que no coinciden con los libros de IVA, IVA sin clasificar, importe fijado distinto del calculado)
/// va a una línea de ajuste: 6391 al Haber si sale a pagar menos, 6341 al Debe si sale a pagar más (propietario 2026-11-16 08:37).</item>
/// <item>Importes en céntimos: los saldos y el 303 ya lo están; nada se redondea aquí.</item>
/// </list>
/// </summary>
public static class CalculoLiquidacionIva
{
    public const string PrefijoSoportado = "472";
    public const string PrefijoRepercutido = "477";
    public const string RegularizacionProrrata = "4729";
    public const string PrefijoCredito = "4700";

    /// <summary>Si la subcuenta se salda en la liquidación de los tres primeros trimestres: 472 y 477, sin la 4729.</summary>
    public static bool SeLiquida(string cuenta)
    {
        var c = (cuenta ?? "").Trim();
        return (c.StartsWith(PrefijoSoportado, StringComparison.Ordinal) && !EsRegularizacionProrrata(c))
            || c.StartsWith(PrefijoRepercutido, StringComparison.Ordinal);
    }

    /// <summary>Si la subcuenta se salda en la liquidación del trimestre: en el 4T también la 4729 de la regularización de la prorrata.</summary>
    public static bool SeLiquida(string cuenta, int trimestre) => SeLiquida(cuenta) || (trimestre == 4 && EsRegularizacionProrrata(cuenta));

    public static bool EsRegularizacionProrrata(string cuenta) => (cuenta ?? "").Trim().StartsWith(RegularizacionProrrata, StringComparison.Ordinal);

    public static DateTime FinTrimestre(int ejercicio, int trimestre) => new DateTime(ejercicio, 3 * trimestre, 1).AddMonths(1).AddDays(-1);

    public static string Nombre(int trimestre) => trimestre switch { 1 => "1T (ene-mar)", 2 => "2T (abr-jun)", 3 => "3T (jul-sep)", 4 => "4T (oct-dic)", _ => "?" };

    public static string Descripcion(int ejercicio, int trimestre) => $"Liquidación del IVA {Nombre(trimestre)} {ejercicio}";

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";

    public static PropuestaLiquidacionIva Calcular(DatosLiquidacionIva d)
    {
        if (d.Trimestre is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(d), "El trimestre va de 1 a 4.");
        var t = d.Trimestre;
        var r = d.Modelo303;
        var lineas = new List<LineaLiquidacionIva>();
        var avisos = new List<string>();
        var etiqueta = $"{Nombre(t)} {d.Ejercicio}";

        // 1. Saldar las 477 (Debe) y las 472 (Haber), cada subcuenta por su saldo.
        var saldos = d.Saldos.Where(s => SeLiquida(s.Cuenta, t) && s.Saldo != 0m)
            .GroupBy(s => s.Cuenta.Trim(), StringComparer.Ordinal).Select(g => new SaldoSubcuenta(g.Key, g.Sum(x => x.Saldo)))
            .Where(s => s.Saldo != 0m).OrderBy(s => s.Cuenta, StringComparer.Ordinal).ToList();
        foreach (var s in saldos.Where(s => s.Cuenta.StartsWith(PrefijoRepercutido, StringComparison.Ordinal)))
            lineas.Add(Saldar(s, $"IVA repercutido {etiqueta}"));
        foreach (var s in saldos.Where(s => s.Cuenta.StartsWith(PrefijoSoportado, StringComparison.Ordinal)))
            lineas.Add(Saldar(s, EsRegularizacionProrrata(s.Cuenta) ? $"Regularización de la prorrata definitiva {d.Ejercicio}" : $"IVA soportado {etiqueta}"));
        var repercutido = -saldos.Where(s => s.Cuenta.StartsWith(PrefijoRepercutido, StringComparison.Ordinal)).Sum(s => s.Saldo);
        var soportado = saldos.Where(s => s.Cuenta.StartsWith(PrefijoSoportado, StringComparison.Ordinal)).Sum(s => s.Saldo);

        // 2. Lo no deducible a la 6341.
        decimal Cuota(string clave) => r.Linea(clave).En(t).Cuota;
        var noDeducibleProrrata = Cuota(Calculo303.ClaveSujetaProrrata) - r.DeducidoProrrata(t);
        bool En472(LineaIva l) => l.Trimestre == t && Calculo303.EsSoportado(l) && (l.CuentaIva ?? "").StartsWith(PrefijoSoportado, StringComparison.Ordinal);
        var noDeducible472 = d.Lineas.Where(l => En472(l) && l.Deduccion == DeduccionIva.NoDeducible).Sum(l => l.Cuota);
        var sinClasificar472 = d.Lineas.Where(l => En472(l) && l.Deduccion == DeduccionIva.SinCentro).Sum(l => l.Cuota);
        // Por centro de coste, para que el gasto quede en su centro: la parte no deducible se reparte en proporción a la cuota sujeta
        // a prorrata de cada centro (resto mayor, para que la suma sea la del 303).
        var prorrataPorCentro = d.Lineas.Where(l => l.Trimestre == t && Calculo303.EsSoportado(l) && l.Deduccion == DeduccionIva.Prorrata)
            .GroupBy(l => l.IdCentroCoste).Select(g => (Centro: g.Key, Peso: g.Sum(l => l.Cuota))).OrderBy(x => x.Centro ?? int.MaxValue).ToList();
        var centroPrincipal = prorrataPorCentro.Where(x => x.Centro is not null).OrderByDescending(x => Math.Abs(x.Peso)).Select(x => x.Centro).FirstOrDefault();
        foreach (var (centro, importe) in Repartir(noDeducibleProrrata, prorrataPorCentro))
            lineas.Add(Linea(d.Cuentas.AjustesNegativos, importe, $"IVA no deducible por prorrata ({r.ProrrataProvisional * 100m:0.##} %) {etiqueta}", centro));
        foreach (var g in d.Lineas.Where(l => En472(l) && l.Deduccion == DeduccionIva.NoDeducible).GroupBy(l => l.IdCentroCoste).OrderBy(g => g.Key ?? int.MaxValue))
            if (g.Sum(l => l.Cuota) is var nd && nd != 0m)
                lineas.Add(Linea(d.Cuentas.AjustesNegativos, nd, $"IVA soportado de actividades no deducibles {etiqueta}", g.Key));
        if (sinClasificar472 != 0m)
            avisos.Add($"Hay {Euros(sinClasificar472)} de IVA soportado sin clasificar (asiento sin centro de coste o centro sin actividad): no se deduce y va al ajuste. " +
                "Ponle centro con actividad a esos asientos y vuelve a calcular (hoja Controles del informe para la gestoría).");

        // 3. Importe del trimestre: el del 303 o el fijado por el operador.
        var devengado = r.TotalDevengado(t);
        var deducible = r.TotalDeducible(t);
        var calculado = r.Resultado(t);
        var liquidado = d.ImporteFijado ?? calculado;
        var credito = d.Credito4700.Where(c => c.Saldo > 0m).Sum(c => c.Saldo);
        var compensado = 0m;
        if (liquidado > 0m)
        {
            // V4: el crédito de la 4700 de trimestres anteriores se aplica antes de ingresar (casilla de cuotas a compensar del 303).
            var pendiente = Math.Min(credito, liquidado);
            foreach (var c in d.Credito4700.Where(c => c.Saldo > 0m).OrderBy(c => c.Cuenta, StringComparer.Ordinal))
            {
                if (pendiente <= 0m) break;
                var usa = Math.Min(c.Saldo, pendiente);
                lineas.Add(new(c.Cuenta.Trim(), 0m, usa, $"Compensación del IVA a compensar de trimestres anteriores {etiqueta}"));
                pendiente -= usa;
                compensado += usa;
            }
            if (liquidado - compensado > 0m)
                lineas.Add(new(d.Cuentas.HaciendaAcreedora, 0m, liquidado - compensado, $"IVA a ingresar {etiqueta}"));
        }
        else if (liquidado < 0m)
            lineas.Add(new(d.Cuentas.HaciendaDeudora, -liquidado, 0m, $"IVA a compensar {etiqueta}"));

        // 4. Ajuste: lo que falta para cuadrar.
        var ajuste = lineas.Sum(l => l.Debe) - lineas.Sum(l => l.Haber);
        var centroAjuste = d.CentroAjustes ?? centroPrincipal;
        if (ajuste > 0m) lineas.Add(new(d.Cuentas.AjustesPositivos, 0m, ajuste, $"Ajuste de la liquidación del IVA {etiqueta}", centroAjuste));
        else if (ajuste < 0m) lineas.Add(new(d.Cuentas.AjustesNegativos, -ajuste, 0m, $"Ajuste de la liquidación del IVA {etiqueta}", centroAjuste));
        var diferenciaFijada = calculado - liquidado;
        // Ajuste = diferencia por el importe fijado + diferencia entre cuentas y libros − IVA sin clasificar (que no se deduce).
        var diferenciaLibros = ajuste - diferenciaFijada + sinClasificar472;
        if (diferenciaLibros != 0m)
            avisos.Add($"Las cuentas 472 y 477 no coinciden con los libros de IVA: con lo que dicen las cuentas saldría a {(diferenciaLibros > 0m ? "pagar" : "compensar")} " +
                $"{Euros(Math.Abs(diferenciaLibros))} más. La diferencia va al ajuste. Si no la esperas, revisa en el Mayor los apuntes de IVA del trimestre que no vienen de una factura.");
        if (diferenciaFijada != 0m)
            avisos.Add($"El importe a liquidar ({Euros(liquidado)}) difiere del calculado ({Euros(calculado)}) en {Euros(Math.Abs(diferenciaFijada))}: la diferencia va al ajuste.");
        if (lineas.Count == 0)
            avisos.Add("El trimestre no tiene IVA que liquidar: no hay saldos en las 472 y 477 ni resultado en el 303.");

        return new PropuestaLiquidacionIva(d.Ejercicio, t, FinTrimestre(d.Ejercicio, t), lineas, repercutido, soportado, devengado, deducible,
            noDeducibleProrrata, noDeducible472, sinClasificar472, calculado, liquidado, credito, compensado, diferenciaLibros, diferenciaFijada, ajuste,
            r.ProrrataProvisional, avisos);
    }

    private static LineaLiquidacionIva Saldar(SaldoSubcuenta s, string concepto) =>
        s.Saldo < 0m ? new(s.Cuenta, -s.Saldo, 0m, concepto) : new(s.Cuenta, 0m, s.Saldo, concepto);

    /// <summary>Línea al Debe si el importe es positivo y al Haber si es negativo (abonos del trimestre).</summary>
    private static LineaLiquidacionIva Linea(string cuenta, decimal importe, string concepto, int? centro = null) =>
        importe >= 0m ? new(cuenta, importe, 0m, concepto, centro) : new(cuenta, 0m, -importe, concepto, centro);

    /// <summary>
    /// Reparte un importe en céntimos en proporción a unos pesos, por el método del resto mayor: la suma de las partes es exactamente el
    /// total. Sin pesos (o con suma cero), todo va a un solo trozo sin centro. Las partes cero no salen.
    /// </summary>
    public static IReadOnlyList<(int? Centro, decimal Importe)> Repartir(decimal total, IReadOnlyList<(int? Centro, decimal Peso)> pesos)
    {
        if (total == 0m) return [];
        var suma = pesos.Sum(p => p.Peso);
        if (pesos.Count == 0 || suma == 0m) return [(null, total)];
        var centimos = (long)Math.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var partes = pesos.Select((p, i) => { var exacto = centimos * p.Peso / suma; var b = Math.Truncate(exacto); return (p.Centro, Base: (long)b, Resto: exacto - b, i); }).ToList();
        var falta = centimos - partes.Sum(p => p.Base);
        var orden = (falta >= 0 ? partes.OrderByDescending(p => p.Resto) : partes.OrderBy(p => p.Resto)).ThenBy(p => p.i).Select(p => p.i).ToList();
        var extra = new long[partes.Count];
        for (var k = 0; k < Math.Abs(falta); k++) extra[orden[k % orden.Count]] += Math.Sign(falta);
        return partes.Select(p => (p.Centro, Importe: (p.Base + extra[p.i]) / 100m)).Where(p => p.Importe != 0m).ToList();
    }
}
