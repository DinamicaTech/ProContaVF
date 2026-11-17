using System.Globalization;

namespace ProConta.Contabilidad.Impuestos;

/// <summary>
/// Cuentas del Impuesto sobre Sociedades del ejercicio (organizacion/sociedades-ejercicios, esquema 64); vacías, las de siempre:
/// 6300000 impuesto corriente, 4752000 a ingresar, 4709000 a devolver, 4745000 crédito por pérdidas y 6301000 impuesto diferido.
/// </summary>
public sealed record CuentasImpuestoSociedades(string Impuesto, string Acreedora, string Deudora, string CreditoPerdidas, string Diferido)
{
    public const string ImpuestoDefecto = "6300000";
    public const string AcreedoraDefecto = "4752000";
    public const string DeudoraDefecto = "4709000";
    public const string CreditoPerdidasDefecto = "4745000";
    public const string DiferidoDefecto = "6301000";

    public static CuentasImpuestoSociedades Defecto { get; } = new(ImpuestoDefecto, AcreedoraDefecto, DeudoraDefecto, CreditoPerdidasDefecto, DiferidoDefecto);

    public static CuentasImpuestoSociedades Con(string? impuesto, string? acreedora, string? deudora, string? credito, string? diferido) => new(
        Valor(impuesto, ImpuestoDefecto), Valor(acreedora, AcreedoraDefecto), Valor(deudora, DeudoraDefecto), Valor(credito, CreditoPerdidasDefecto), Valor(diferido, DiferidoDefecto));

    private static string Valor(string? v, string defecto) => string.IsNullOrWhiteSpace(v) ? defecto : v.Trim();
}

/// <summary>
/// Parámetros del IS del ejercicio (Parámetros y tablas › Ejercicios). Tipos y bonificación en tanto por uno (0,25 = 25 %).
/// Con <see cref="LimiteTramo"/> y <see cref="TipoTramo"/>, la base hasta el límite va al tipo reducido y el resto al general.
/// </summary>
public sealed record ParametrosImpuestoSociedades(decimal TipoGeneral, decimal? LimiteTramo, decimal? TipoTramo, decimal Bonificacion,
    IReadOnlyList<string> PrefijosAjustes, CuentasImpuestoSociedades Cuentas)
{
    public const decimal TipoGeneralDefecto = 0.25m;
    public static readonly IReadOnlyList<string> PrefijosAjustesDefecto = ["678", "669", "778", "760", "766", "666"];

    public static ParametrosImpuestoSociedades Defecto { get; } = new(TipoGeneralDefecto, null, null, 0m, PrefijosAjustesDefecto, CuentasImpuestoSociedades.Defecto);

    /// <summary>Si hay tramo reducido (límite positivo y su tipo).</summary>
    public bool TieneTramo => LimiteTramo is > 0m && TipoTramo is not null;

    /// <summary>Lee los prefijos escritos («678, 669 778;760»); vacío: los de siempre.</summary>
    public static IReadOnlyList<string> LeerPrefijos(string? texto)
    {
        var l = (texto ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.All(char.IsAsciiDigit)).Distinct(StringComparer.Ordinal).ToList();
        return l.Count == 0 ? PrefijosAjustesDefecto : l;
    }
}

/// <summary>Línea de ajuste al resultado contable que escribe el operador (importe positivo).</summary>
/// <param name="Temporal">Ajuste temporal (si no, permanente).</param>
/// <param name="Aumento">Aumenta el resultado (si no, lo disminuye).</param>
/// <param name="Cuenta">Cuenta de la que sale, si se añadió desde los posibles ajustes.</param>
public sealed record AjusteImpuestoSociedades(bool Temporal, bool Aumento, string Descripcion, decimal Importe, string? Cuenta = null)
{
    public decimal ConSigno => Aumento ? Importe : -Importe;
}

/// <summary>Saldo de una subcuenta en el ejercicio (Debe − Haber) con sus apuntes y, en las 473, los pagadores (nombres de las 43 y 44 de sus asientos).</summary>
public sealed record SaldoCuentaImpuesto(string Cuenta, string Nombre, decimal Saldo, int Apuntes, IReadOnlyList<string>? Pagadores = null)
{
    public bool EsGasto => Cuenta.StartsWith('6');
}

/// <summary>
/// Retención o pago a cuenta que conoce la AEAT (datos fiscales del IS: modelos 202, 193, 180…), con la 473 a la que corresponde
/// (null: sin asignar, no se deduce).
/// </summary>
public sealed record RetencionAeat(string Modelo, string? Nif, string Nombre, decimal Importe, string? Cuenta = null);

/// <summary>
/// Una 473 en la propuesta: su saldo, lo que conoce la AEAT (null sin datos fiscales), lo propuesto y lo que se deduce.
/// Lo que no se deduce se queda en la cuenta.
/// </summary>
public sealed record RetencionPropuesta(string Cuenta, string Nombre, decimal Saldo, int Apuntes, IReadOnlyList<string> Pagadores, decimal? SegunAeat,
    decimal Propuesto, decimal Deducido)
{
    public bool Escrito => Deducido != Propuesto;
}

/// <summary>Lo que se lee de la contabilidad para el cálculo.</summary>
/// <param name="ResultadoContable">Grupos 6 y 7 sin las 630, 633 y 638, sin regularización ni cierre (Haber − Debe: positivo, beneficio).</param>
/// <param name="RentaViviendas">Lo mismo, solo de los centros cuya actividad tiene la bonificación por alquiler de viviendas.</param>
/// <param name="HayActividadBonificada">Si alguna actividad de la sociedad tiene la marca de bonificación.</param>
/// <param name="Retenciones">Subcuentas 473 con saldo a 31/12 (sin este proceso ni los asientos del impuesto hechos fuera de él).</param>
/// <param name="PosiblesAjustes">Subcuentas con saldo de los prefijos de posibles ajustes.</param>
/// <param name="SaldoCreditoPerdidas">Saldo deudor de la cuenta del crédito por pérdidas (4745) a 31/12.</param>
/// <param name="AsientosAjenos">Asientos del ejercicio con una 630 hechos fuera de este proceso (texto para el aviso).</param>
/// <param name="ApuntesRetencion">Apuntes de las 473 que forman su saldo, con el pagador de su asiento (null: solo se conocen los saldos).</param>
/// <param name="Viviendas">Desglose de la renta de viviendas (ingresos, gastos directos e indirectos imputados); null: solo la cifra.</param>
/// <param name="Ingresos">Ingresos del ejercicio que entran en el resultado (grupo 7, Haber − Debe); null: solo se conoce el resultado.</param>
public sealed record DatosImpuestoSociedades(int Ejercicio, decimal ResultadoContable, decimal RentaViviendas, bool HayActividadBonificada,
    IReadOnlyList<SaldoCuentaImpuesto> Retenciones, IReadOnlyList<SaldoCuentaImpuesto> PosiblesAjustes, decimal SaldoCreditoPerdidas,
    ParametrosImpuestoSociedades Parametros, IReadOnlyList<string> AsientosAjenos, IReadOnlyList<ApunteRetencion>? ApuntesRetencion = null,
    RentaViviendasDetalle? Viviendas = null, decimal? Ingresos = null)
{
    /// <summary>Gastos del ejercicio que entran en el resultado (ingresos − resultado); null si no se conocen los ingresos.</summary>
    public decimal? Gastos => Ingresos is decimal i ? i - ResultadoContable : null;
}

/// <summary>
/// Lo que escribe el operador. Null en <see cref="Bonificaciones"/>, <see cref="CreditoPerdidas"/> y <see cref="CuotaLiquida"/>: la propuesta.
/// </summary>
/// <param name="RetencionesAeat">Retenciones y pagos a cuenta de los datos fiscales de la AEAT (importados o escritos); null o vacío: sin datos fiscales.</param>
/// <param name="BasesNegativasAeat">Bases negativas pendientes de compensar según la AEAT.</param>
/// <param name="ResultadoPresentado">Resultado de la declaración ya presentada (casilla 621 del modelo 200) que traen los datos fiscales.</param>
/// <param name="Valores">Lo escrito en el contraste de retenciones: el valor para el IS de cada fila (null: el propuesto), su justificación y, en las
/// filas que solo conoce Hacienda, la 473 a la que van.</param>
/// <param name="RentaViviendas">Renta de viviendas que se aplica en la bonificación (null: la calculada).</param>
public sealed record EntradaImpuestoSociedades(IReadOnlyList<AjusteImpuestoSociedades> Ajustes, decimal BasesNegativas = 0m, decimal? Bonificaciones = null,
    decimal Deducciones = 0m, decimal? CreditoPerdidas = null, decimal? CuotaLiquida = null, IReadOnlyList<RetencionAeat>? RetencionesAeat = null,
    decimal? BasesNegativasAeat = null, decimal? ResultadoPresentado = null, IReadOnlyList<ValorRetencion>? Valores = null, decimal? RentaViviendas = null)
{
    public static EntradaImpuestoSociedades Vacia { get; } = new([]);

    public bool HayDatosAeat => RetencionesAeat is { Count: > 0 } || BasesNegativasAeat is not null || ResultadoPresentado is not null;

    public bool Equivale(EntradaImpuestoSociedades o) =>
        BasesNegativas == o.BasesNegativas && Bonificaciones == o.Bonificaciones && Deducciones == o.Deducciones && CreditoPerdidas == o.CreditoPerdidas
        && CuotaLiquida == o.CuotaLiquida && Ajustes.SequenceEqual(o.Ajustes) && (RetencionesAeat ?? []).SequenceEqual(o.RetencionesAeat ?? [])
        && BasesNegativasAeat == o.BasesNegativasAeat && ResultadoPresentado == o.ResultadoPresentado
        && RentaViviendas == o.RentaViviendas
        && (Valores ?? []).OrderBy(x => x.Clave, StringComparer.Ordinal).SequenceEqual((o.Valores ?? []).OrderBy(x => x.Clave, StringComparer.Ordinal));
}

/// <summary>Línea del asiento propuesto.</summary>
public sealed record LineaImpuestoSociedades(string Cuenta, decimal Debe, decimal Haber, string Concepto);

/// <summary>
/// Propuesta del Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades), con las cifras en el orden de la hoja de la
/// gestoría y el asiento del 31/12. <see cref="Resultado"/> positivo a ingresar y negativo a devolver.
/// </summary>
public sealed record PropuestaImpuestoSociedades(
    int Ejercicio, DateTime Fecha, decimal ResultadoContable,
    decimal AumentosPermanentes, decimal DisminucionesPermanentes, decimal AumentosTemporales, decimal DisminucionesTemporales,
    decimal BasesNegativas, decimal BaseImponible, decimal TipoGeneral, decimal? LimiteTramo, decimal? TipoTramo,
    decimal CuotaIntegra, decimal RentaViviendas, decimal BonificacionPropuesta, decimal Bonificaciones, decimal Deducciones,
    decimal CuotaCalculada, decimal CuotaLiquida, IReadOnlyList<RetencionPropuesta> Retenciones, decimal CreditoPropuesto, decimal CreditoPerdidas,
    IReadOnlyList<SaldoCuentaImpuesto> PosiblesAjustes, IReadOnlyList<LineaImpuestoSociedades> Lineas, IReadOnlyList<string> Avisos,
    EntradaImpuestoSociedades Entrada, IReadOnlyList<ContrasteRetencion>? Contraste = null, decimal RentaViviendasCalculada = 0m,
    RentaViviendasDetalle? Viviendas = null)
{
    public IReadOnlyList<ContrasteRetencion> FilasContraste => Contraste ?? [];
    /// <summary>Por qué no se puede confirmar el contraste de retenciones (null si se puede).</summary>
    public string? PorQueNoContraste => ContrasteRetenciones.PorQueNo(FilasContraste);
    public decimal ResultadoAjustado => ResultadoContable + AumentosPermanentes - DisminucionesPermanentes;
    public decimal BasePrevia => ResultadoAjustado + AumentosTemporales - DisminucionesTemporales;
    /// <summary>Lo que se deduce de las 473 (lo que va al Haber de cada una).</summary>
    public decimal TotalRetenciones => Retenciones.Sum(r => r.Deducido);
    /// <summary>Suma de los saldos de las 473 (sin las acreedoras).</summary>
    public decimal TotalSaldos473 => Retenciones.Where(r => r.Saldo > 0m).Sum(r => r.Saldo);
    public decimal? TotalAeat => Retenciones.Any(r => r.SegunAeat is not null) ? Retenciones.Sum(r => r.SegunAeat ?? 0m) : null;
    public decimal Resultado => CuotaLiquida - TotalRetenciones;
    public bool CuotaFijada => CuotaLiquida != CuotaCalculada;
    /// <summary>Tipo medio de la cuota íntegra sobre la base (0 sin base).</summary>
    public decimal TipoMedio => BaseImponible > 0m ? CuotaIntegra / BaseImponible : 0m;
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
}

/// <summary>
/// Cálculo del Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades), sin BD, en el orden de la hoja de la gestoría:
/// <list type="number">
/// <item>Resultado contable ± ajustes permanentes = resultado ajustado; ± ajustes temporales = base previa; − bases negativas = base imponible.</item>
/// <item>Cuota íntegra (0 si la base no es positiva): hasta el límite del tramo al tipo reducido y el resto al general, cada tramo redondeado al céntimo.</item>
/// <item>Bonificación por alquiler de viviendas propuesta: Redondear(cuota íntegra × renta de viviendas / base imponible (entre 0 y 1) × porcentaje, 2).</item>
/// <item>Cuota líquida = cuota íntegra − bonificaciones − deducciones (nunca negativa), o la que fija el operador.</item>
/// <item>Retenciones y pagos a cuenta: de cada 473 se propone deducir lo que conoce la AEAT si hay datos fiscales (0 si la AEAT no le asigna nada) y,
/// sin ellos, su saldo deudor; el operador puede escribir otro importe. Lo que no se deduce se queda en la cuenta.</item>
/// <item>Resultado = cuota líquida − retenciones deducidas: positivo a la 4752, negativo a la 4709.</item>
/// <item>Crédito por pérdidas (propuesto: el menor entre el saldo de la 4745 y las bases negativas por el tipo general): 6301 al Debe y 4745 al Haber.</item>
/// </list>
/// </summary>
public static class CalculoImpuestoSociedades
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static DateTime FechaAsiento(int ejercicio) => new(ejercicio, 12, 31);

    public static string Descripcion(int ejercicio) => $"Impuesto sobre Sociedades {ejercicio}";

    /// <summary>Si la cuenta es del gasto por impuesto sobre beneficios (630, 633 y 638), que no entra en el resultado contable.</summary>
    public static bool EsImpuestoBeneficios(string cuenta) => cuenta.StartsWith("630", StringComparison.Ordinal)
        || cuenta.StartsWith("633", StringComparison.Ordinal) || cuenta.StartsWith("638", StringComparison.Ordinal);

    /// <summary>Si la cuenta entra en el resultado contable antes de impuestos: grupos 6 y 7 sin las del impuesto.</summary>
    public static bool EntraEnResultado(string cuenta) => cuenta.Length > 0 && cuenta[0] is '6' or '7' && !EsImpuestoBeneficios(cuenta);

    public static bool EsRetencion(string cuenta) => cuenta.StartsWith("473", StringComparison.Ordinal);

    /// <summary>Cuota íntegra de una base: por tramos y redondeada al céntimo; 0 si la base no es positiva.</summary>
    public static decimal CuotaIntegra(decimal baseImponible, ParametrosImpuestoSociedades p)
    {
        if (baseImponible <= 0m) return 0m;
        if (!p.TieneTramo) return Redondear(baseImponible * p.TipoGeneral);
        var tramo = Math.Min(baseImponible, p.LimiteTramo!.Value);
        return Redondear(tramo * p.TipoTramo!.Value) + Redondear((baseImponible - tramo) * p.TipoGeneral);
    }

    /// <summary>Errores de lo escrito que impiden calcular (null si no hay).</summary>
    public static string? Validar(EntradaImpuestoSociedades e)
    {
        for (var i = 0; i < e.Ajustes.Count; i++)
        {
            var a = e.Ajustes[i];
            if (string.IsNullOrWhiteSpace(a.Descripcion)) return $"El ajuste {i + 1} no tiene descripción: escribe qué es (por ejemplo «Multas y sanciones»).";
            if (a.Importe <= 0m) return $"El ajuste «{a.Descripcion.Trim()}» tiene que tener un importe positivo; elige si aumenta o disminuye el resultado.";
        }
        if (e.BasesNegativas < 0m) return "La compensación de bases negativas no puede ser negativa.";
        if (e.Bonificaciones is < 0m) return "Las bonificaciones no pueden ser negativas.";
        if (e.Deducciones < 0m) return "Las deducciones no pueden ser negativas.";
        if (e.CreditoPerdidas is < 0m) return "El crédito por pérdidas aplicado no puede ser negativo.";
        if (e.CuotaLiquida is < 0m) return "La cuota líquida no puede ser negativa.";
        if (e.BasesNegativasAeat is < 0m) return "Las bases negativas pendientes según la AEAT no pueden ser negativas.";
        foreach (var r in e.RetencionesAeat ?? [])
            if (r.Importe < 0m) return $"La retención de la AEAT de «{r.Nombre}» no puede ser negativa.";
        foreach (var v in e.Valores ?? [])
            if (v.Importe is < 0m) return "El valor para el IS de una retención no puede ser negativo.";
        return null;
    }

    public static PropuestaImpuestoSociedades Calcular(DatosImpuestoSociedades d, EntradaImpuestoSociedades e)
    {
        var p = d.Parametros;
        var avisos = new List<string>();
        decimal Suma(bool temporal, bool aumento) => e.Ajustes.Where(a => a.Temporal == temporal && a.Aumento == aumento).Sum(a => a.Importe);
        var aumP = Suma(false, true);
        var disP = Suma(false, false);
        var aumT = Suma(true, true);
        var disT = Suma(true, false);
        var basePrevia = d.ResultadoContable + aumP - disP + aumT - disT;
        var baseImponible = basePrevia - e.BasesNegativas;
        var cuotaIntegra = CuotaIntegra(baseImponible, p);

        var renta = e.RentaViviendas ?? d.RentaViviendas;
        var bonifPropuesta = 0m;
        if (p.Bonificacion > 0m && baseImponible > 0m && renta > 0m)
            bonifPropuesta = Redondear(cuotaIntegra * Math.Min(1m, renta / baseImponible) * p.Bonificacion);
        var bonif = e.Bonificaciones ?? bonifPropuesta;
        var cuotaCalculada = Math.Max(0m, cuotaIntegra - bonif - e.Deducciones);
        var cuotaLiquida = e.CuotaLiquida ?? cuotaCalculada;

        var creditoPropuesto = e.BasesNegativas > 0m && d.SaldoCreditoPerdidas > 0m
            ? Math.Min(d.SaldoCreditoPerdidas, Redondear(e.BasesNegativas * p.TipoGeneral)) : 0m;
        var credito = e.CreditoPerdidas ?? creditoPropuesto;

        var contraste = ContrasteRetenciones.Filas(d, e);
        var retenciones = PorCuenta(d, contraste, e.RetencionesAeat is { Count: > 0 });
        var totalRet = retenciones.Sum(r => r.Deducido);
        var resultado = cuotaLiquida - totalRet;

        var concepto = Descripcion(d.Ejercicio);
        var lineas = new List<LineaImpuestoSociedades>();
        if (cuotaLiquida > 0m) lineas.Add(new(p.Cuentas.Impuesto, cuotaLiquida, 0m, $"Cuota líquida del {concepto}"));
        if (resultado < 0m) lineas.Add(new(p.Cuentas.Deudora, -resultado, 0m, $"{concepto} a devolver"));
        foreach (var r in retenciones.Where(r => r.Deducido > 0m))
            lineas.Add(new(r.Cuenta, 0m, r.Deducido, $"Retenciones y pagos a cuenta del {concepto}"));
        if (resultado > 0m) lineas.Add(new(p.Cuentas.Acreedora, 0m, resultado, $"{concepto} a ingresar"));
        if (credito > 0m)
        {
            lineas.Add(new(p.Cuentas.Diferido, credito, 0m, $"Crédito por pérdidas aplicado en el {concepto}"));
            lineas.Add(new(p.Cuentas.CreditoPerdidas, 0m, credito, $"Crédito por pérdidas aplicado en el {concepto}"));
        }

        // ---------- avisos ----------
        if (d.AsientosAjenos.Count > 0)
            avisos.Add($"Ya hay asiento del impuesto hecho fuera de este proceso ({string.Join("; ", d.AsientosAjenos)}). Sus retenciones se cuentan aquí como si no existiera, " +
                "pero para confirmar hay que borrarlo antes (o dejarlo y no usar este proceso este año).");
        if (baseImponible < 0m)
            avisos.Add($"La base imponible es negativa ({Euros(baseImponible)}): no hay cuota. Esa pérdida fiscal se podrá compensar en los próximos años; si la gestoría " +
                "activa el crédito fiscal (4745 contra la 6301 al Haber), hazlo con un asiento manual.");
        if (e.BasesNegativas > 0m && basePrevia > 0m && e.BasesNegativas > basePrevia)
            avisos.Add($"La compensación de bases negativas ({Euros(e.BasesNegativas)}) es mayor que la base previa ({Euros(basePrevia)}): la base imponible sale negativa.");
        if (!p.TieneTramo)
            avisos.Add($"El ejercicio no tiene tramo reducido: toda la base va al {Porcentaje(p.TipoGeneral)}. Si la gestoría aplica los tramos de microempresa, ponlos en Parámetros y tablas › Ejercicios (Impuestos).");
        if (p.Bonificacion > 0m && !d.HayActividadBonificada)
            avisos.Add($"Hay bonificación por alquiler de viviendas ({Porcentaje(p.Bonificacion)}), pero ninguna actividad la tiene marcada: márcala en Catálogos › Actividades (IVA) para que se proponga.");
        if (e.RentaViviendas is decimal rv && rv != d.RentaViviendas)
            avisos.Add($"Has escrito una renta de viviendas de {Euros(rv)}; la calculada es {Euros(d.RentaViviendas)}.");
        if (e.Bonificaciones is decimal b && b != bonifPropuesta)
            avisos.Add($"Has escrito una bonificación de {Euros(b)}; la propuesta es de {Euros(bonifPropuesta)}.");
        if (e.CuotaLiquida is decimal c && c != cuotaCalculada)
            avisos.Add($"Has fijado la cuota líquida en {Euros(c)}; la calculada es {Euros(cuotaCalculada)}. Hace falta motivo para confirmar.");
        if (credito > 0m && credito > d.SaldoCreditoPerdidas)
            avisos.Add($"El crédito por pérdidas aplicado ({Euros(credito)}) es mayor que el saldo de la {p.Cuentas.CreditoPerdidas} ({Euros(d.SaldoCreditoPerdidas)}).");
        foreach (var r in retenciones.Where(r => r.Saldo < 0m))
            avisos.Add($"La {r.Cuenta} {r.Nombre} tiene saldo acreedor ({Euros(-r.Saldo)}): no se salda. Revísala en el Mayor.");
        AvisosAeat(d, e, contraste, retenciones, resultado, avisos);
        var sinAjustar = d.PosiblesAjustes.Where(x => x.Saldo != 0m && !e.Ajustes.Any(a => a.Cuenta == x.Cuenta)).ToList();
        if (sinAjustar.Count > 0)
            avisos.Add($"Hay {sinAjustar.Count} {(sinAjustar.Count == 1 ? "cuenta" : "cuentas")} con posibles ajustes fiscales sin ajustar ({string.Join(", ", sinAjustar.Select(x => x.Cuenta))}): valídalas con la gestoría.");
        if (lineas.Count == 0)
            avisos.Add("No hay cuota ni retenciones: no hace falta asiento. Al confirmar solo se guarda el cálculo.");

        return new PropuestaImpuestoSociedades(d.Ejercicio, FechaAsiento(d.Ejercicio), d.ResultadoContable, aumP, disP, aumT, disT, e.BasesNegativas, baseImponible,
            p.TipoGeneral, p.TieneTramo ? p.LimiteTramo : null, p.TieneTramo ? p.TipoTramo : null, cuotaIntegra, renta, bonifPropuesta, bonif, e.Deducciones,
            cuotaCalculada, cuotaLiquida, retenciones, creditoPropuesto, credito, d.PosiblesAjustes, lineas, avisos, e, contraste, d.RentaViviendas, d.Viviendas);
    }

    /// <summary>Las 473 de la propuesta: lo de cada fila del contraste sumado por su cuenta, con el saldo de la cuenta.</summary>
    private static IReadOnlyList<RetencionPropuesta> PorCuenta(DatosImpuestoSociedades d, IReadOnlyList<ContrasteRetencion> filas, bool hayAeat)
    {
        var saldos = d.Retenciones.ToDictionary(r => r.Cuenta, StringComparer.Ordinal);
        var cuentas = d.Retenciones.Where(r => r.Saldo != 0m).Select(r => r.Cuenta)
            .Concat(filas.Where(f => f.Cuenta is not null && (f.Valor != 0m || f.Hacienda is > 0m)).Select(f => f.Cuenta!))
            .Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal);
        return cuentas.Select(c =>
        {
            var s = saldos.GetValueOrDefault(c);
            var suyas = filas.Where(f => f.Cuenta == c).ToList();
            return new RetencionPropuesta(c, s?.Nombre ?? suyas.FirstOrDefault()?.NombreCuenta ?? "", s?.Saldo ?? 0m, s?.Apuntes ?? 0, s?.Pagadores ?? [],
                hayAeat ? suyas.Sum(f => f.Hacienda ?? 0m) : null, suyas.Sum(f => f.Propuesto), suyas.Sum(f => f.Valor));
        }).ToList();
    }

    private static void AvisosAeat(DatosImpuestoSociedades d, EntradaImpuestoSociedades e, IReadOnlyList<ContrasteRetencion> filas,
        IReadOnlyList<RetencionPropuesta> retenciones, decimal resultado, List<string> avisos)
    {
        var hayAeat = e.RetencionesAeat is { Count: > 0 };
        if (!hayAeat)
        {
            if (retenciones.Any(r => r.Saldo > 0m))
                avisos.Add("Sin datos fiscales de la AEAT se propone deducir lo contabilizado en cada 473. Impórtalos en el contraste de retenciones antes de confirmar.");
        }
        else
        {
            var distintas = filas.Where(f => (f.Hacienda ?? 0m) != f.Contable).ToList();
            if (distintas.Count > 0)
                avisos.Add($"Hacienda y la contabilidad no coinciden en {Formato(distintas.Count, "pagador", "pagadores")} (Hacienda {Euros(filas.Sum(f => f.Hacienda ?? 0m))}, " +
                    $"contabilidad {Euros(filas.Sum(f => f.Contable))}): revísalos en el contraste de retenciones.");
            foreach (var f in filas.Where(f => f.Valor != 0m && f.Cuenta is null))
                avisos.Add($"{f.Pagador}: {Euros(f.Valor)} para el IS sin cuenta 473: elígela en el contraste de retenciones.");
            foreach (var f in filas.Where(f => f.FaltaJustificacion))
                avisos.Add($"{f.Pagador}: el valor para el IS ({Euros(f.Valor)}) no es el de Hacienda ({Euros(f.Hacienda ?? 0m)}); escribe la justificación.");
        }
        foreach (var r in retenciones.Where(r => r.Deducido > 0m && r.Deducido > Math.Max(0m, r.Saldo)))
            avisos.Add($"Se deduce de la {r.Cuenta} {r.Nombre} {Euros(r.Deducido)}, más que su saldo ({Euros(Math.Max(0m, r.Saldo))}): quedará con saldo acreedor.");
        foreach (var r in retenciones.Where(r => r.Saldo > 0m && r.Deducido < r.Saldo))
            avisos.Add($"De la {r.Cuenta} {r.Nombre} se deduce {Euros(r.Deducido)} de un saldo de {Euros(r.Saldo)}: la diferencia ({Euros(r.Saldo - r.Deducido)}) se queda en la cuenta.");
        if (e.BasesNegativasAeat is decimal bn && e.BasesNegativas > bn)
            avisos.Add($"Compensas {Euros(e.BasesNegativas)} de bases negativas y la AEAT solo tiene pendientes {Euros(bn)}.");
        if (e.ResultadoPresentado is decimal rp && rp != resultado)
            avisos.Add($"La declaración presentada del {d.Ejercicio} da {Euros(rp)} y este cálculo {Euros(resultado)}: si el cálculo es el correcto, habla con la gestoría de una rectificativa.");
    }

    private static string Formato(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";

    /// <summary>Texto para las notas del asiento: el cálculo resumido.</summary>
    public static string Notas(PropuestaImpuestoSociedades p, string? motivo)
    {
        var partes = new List<string>
        {
            $"Resultado {Euros(p.ResultadoContable)}; ajustes +{Euros(p.AumentosPermanentes + p.AumentosTemporales)} −{Euros(p.DisminucionesPermanentes + p.DisminucionesTemporales)};" +
            (p.BasesNegativas != 0m ? $" bases negativas {Euros(p.BasesNegativas)};" : "") + $" base {Euros(p.BaseImponible)}.",
            $"Cuota íntegra {Euros(p.CuotaIntegra)}" + (p.Bonificaciones != 0m ? $", bonificaciones {Euros(p.Bonificaciones)}" : "") +
            (p.Deducciones != 0m ? $", deducciones {Euros(p.Deducciones)}" : "") + $", cuota líquida {Euros(p.CuotaLiquida)}; retenciones {Euros(p.TotalRetenciones)}.",
        };
        if (!string.IsNullOrWhiteSpace(motivo)) partes.Add($"Motivo: {motivo.Trim()}");
        var texto = string.Join(" ", partes);
        return texto.Length <= 255 ? texto : texto[..255];
    }

    public static string Euros(decimal v) => v.ToString("#,##0.00", Es) + " €";

    public static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100m).ToString("0.##", Es) + " %";

    private static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
