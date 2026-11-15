using System.Globalization;
using ProConta.Contabilidad.Balances;

namespace ProConta.Contabilidad.Ratios;

/// <summary>Suma de apuntes contabilizados de una cuenta (por día, o por ejercicio para las series a cierre) con su tipo de asiento.</summary>
public sealed record MovimientoRatio(DateTime Fecha, string Cuenta, byte TipoAsiento, decimal Debe, decimal Haber);

/// <summary>Familia de la pantalla de inicio en la que se agrupa un ratio.</summary>
public enum FamiliaRatio { Liquidez, SolvenciaEndeudamiento, RentabilidadMargenes, Gestion }

/// <summary>Unidad en que se expresa un ratio: veces (2 decimales), porcentaje (1 decimal) o días (enteros).</summary>
public enum UnidadRatio { Veces, Porcentaje, Dias }

/// <summary>Estado de un ratio frente a su rango de referencia; <see cref="SinDatos"/> si no se puede calcular.</summary>
public enum EstadoRatio { SinDatos, Bien, Vigilar, Mal }

/// <summary>
/// Un ratio con su fórmula y su rango de referencia estándar (no configurable por sociedad). <see cref="Minimo"/> o
/// <see cref="Maximo"/> nulos: rango abierto por ese lado. <see cref="MenosEsMejor"/>: cuanto más bajo, mejor (solo cuenta el máximo).
/// </summary>
public sealed record DefinicionRatio(
    string Clave, string Nombre, FamiliaRatio Familia, string Formula, UnidadRatio Unidad, decimal? Minimo, decimal? Maximo, bool MenosEsMejor, string Referencia)
{
    /// <summary>
    /// Bien dentro del rango; por debajo del mínimo, Vigilar y Mal si baja del 80 % del mínimo; por encima del máximo, Vigilar.
    /// En los de «menos es mejor»: Bien hasta el máximo, Vigilar hasta 1,3 veces el máximo y Mal por encima. Sobre el valor sin redondear.
    /// </summary>
    public EstadoRatio Estado(decimal valor)
    {
        if (MenosEsMejor && Maximo is decimal tope)
            return valor <= tope ? EstadoRatio.Bien : valor <= tope * 1.3m ? EstadoRatio.Vigilar : EstadoRatio.Mal;
        if (Minimo is decimal min && valor < min) return valor < min * 0.8m ? EstadoRatio.Mal : EstadoRatio.Vigilar;
        if (Maximo is decimal max && valor > max) return EstadoRatio.Vigilar;
        return EstadoRatio.Bien;
    }
}

/// <summary>Subcuenta de una partida con su importe (con el signo de la partida).</summary>
public sealed record CuentaPartida(string Cuenta, decimal Importe);

/// <summary>Partida (cifra de balance o de resultados) que forma un ratio: qué cuentas incluye y su importe.</summary>
public sealed record PartidaRatio(string Nombre, string Cuentas, decimal Importe, IReadOnlyList<CuentaPartida> Detalle)
{
    /// <summary>Cuentas para abrir el Mayor: las subcuentas si son pocas; si no, sus cuentas de 3 dígitos.</summary>
    public IReadOnlyList<string> CuentasMayor => Detalle.Count <= 40
        ? Detalle.Select(d => d.Cuenta).ToList()
        : Detalle.Select(d => d.Cuenta.Length >= 3 ? d.Cuenta[..3] : d.Cuenta).Distinct().ToList();
}

/// <summary>Un ratio calculado: valor sin redondear (null si no hay datos, con el motivo), estado y partidas.</summary>
public sealed record ValorRatio(DefinicionRatio Definicion, decimal? Valor, EstadoRatio Estado, string? Motivo, IReadOnlyList<PartidaRatio> Partidas);

/// <summary>Los 12 ratios a una fecha de balance, con el periodo del resultado (12 meses hasta esa fecha).</summary>
public sealed record ResultadoRatios(DateTime FechaBalance, DateTime DesdeResultado, bool HayBalance, IReadOnlyList<ValorRatio> Ratios, IReadOnlyList<string> SinClasificar)
{
    public int Bien => Ratios.Count(r => r.Estado == EstadoRatio.Bien);
    public int Vigilar => Ratios.Count(r => r.Estado == EstadoRatio.Vigilar);
    public int Mal => Ratios.Count(r => r.Estado == EstadoRatio.Mal);

    public ValorRatio? Ratio(string clave) => Ratios.FirstOrDefault(r => r.Definicion.Clave == clave);
}

/// <summary>
/// Ratios financieros y de gestión (informes/ratios), sin BD. El balance se clasifica con el modelo de situación de
/// contabilidad/balances (PGC Pymes: activo no corriente y corriente, patrimonio neto, pasivo no corriente y corriente) y el
/// resultado es el de los últimos 12 meses hasta la fecha del balance. Importes en decimal; aquí no se redondea nada.
/// </summary>
public static class CalculoRatios
{
    public const byte TipoRegularizacion = 2;
    public const byte TipoCierre = 3;

    public static IReadOnlyList<DefinicionRatio> Definiciones { get; } =
    [
        new("liquidez", "Liquidez general", FamiliaRatio.Liquidez, "Activo corriente / Pasivo corriente", UnidadRatio.Veces, 1.5m, 2.5m, false, "entre 1,5 y 2,5"),
        new("acida", "Prueba ácida", FamiliaRatio.Liquidez, "(Realizable + Disponible) / Pasivo corriente", UnidadRatio.Veces, 1m, 1.8m, false, "entre 1 y 1,8"),
        new("tesoreria", "Tesorería inmediata", FamiliaRatio.Liquidez, "Disponible / Pasivo corriente", UnidadRatio.Veces, 0.1m, 0.4m, false, "entre 0,1 y 0,4 · por encima, dinero ocioso"),
        new("solvencia", "Solvencia", FamiliaRatio.SolvenciaEndeudamiento, "Activo total / Pasivo exigible", UnidadRatio.Veces, 1.5m, null, false, "mayor que 1,5"),
        new("endeudamiento", "Endeudamiento", FamiliaRatio.SolvenciaEndeudamiento, "Pasivo exigible / Patrimonio neto", UnidadRatio.Veces, 0.4m, 1m, false, "entre 0,4 y 1"),
        new("calidad", "Calidad de la deuda", FamiliaRatio.SolvenciaEndeudamiento, "Pasivo corriente / Pasivo exigible", UnidadRatio.Veces, null, 0.5m, true, "menor que 0,5 · más bajo, deuda a más largo plazo"),
        new("roe", "Rentabilidad financiera (ROE)", FamiliaRatio.RentabilidadMargenes, "Resultado neto 12 meses / Patrimonio neto", UnidadRatio.Porcentaje, 6m, null, false, "mayor que 6 %"),
        new("roa", "Rentabilidad económica (ROA)", FamiliaRatio.RentabilidadMargenes, "Resultado de explotación 12 meses / Activo total", UnidadRatio.Porcentaje, 5m, null, false, "mayor que 5 %"),
        new("ebitda", "Margen EBITDA", FamiliaRatio.RentabilidadMargenes, "EBITDA 12 meses / Ventas 12 meses", UnidadRatio.Porcentaje, 20m, null, false, "mayor que 20 %"),
        new("neto", "Margen neto", FamiliaRatio.RentabilidadMargenes, "Resultado neto 12 meses / Ventas 12 meses", UnidadRatio.Porcentaje, 8m, null, false, "mayor que 8 %"),
        new("pmc", "Periodo medio de cobro", FamiliaRatio.Gestion, "Clientes / Ventas con IVA 12 meses × 365", UnidadRatio.Dias, null, 30m, true, "menos de 30 días"),
        new("pmp", "Periodo medio de pago", FamiliaRatio.Gestion, "Proveedores y acreedores / Compras y gastos con IVA 12 meses × 365", UnidadRatio.Dias, null, 60m, true, "menos de 60 días (plazo legal)"),
    ];

    /// <summary>Fecha del balance: el último día del mes anterior al de la fecha de corte; con el ejercicio terminado, el 31/12.</summary>
    public static DateTime FechaBalance(DateTime corte, bool terminado) =>
        terminado ? new DateTime(corte.Year, 12, 31) : new DateTime(corte.Year, corte.Month, 1).AddDays(-1);

    /// <summary>Primer día de los 12 meses que terminan en la fecha del balance.</summary>
    public static DateTime DesdeResultado(DateTime fechaBalance) => fechaBalance.AddDays(1).AddMonths(-12);

    /// <summary>Cuentas de resultados: grupos 6 y 7.</summary>
    public static bool EsResultado(string c) => c.Length > 0 && c[0] is '6' or '7';

    private static bool Empieza(string c, params string[] prefijos) => prefijos.Any(p => c.StartsWith(p, StringComparison.Ordinal));

    /// <summary>
    /// Cuentas del EBITDA: grupos 7 y 6 sin el impuesto sobre beneficios (630), las amortizaciones (68), los deterioros y
    /// provisiones (69 y 79) ni los resultados financieros (66 y 76).
    /// </summary>
    public static bool EsEbitda(string c) => EsResultado(c) && !Empieza(c, "630", "68", "69", "79", "66", "76");

    /// <summary>Cuentas del resultado de explotación: grupos 7 y 6 sin los financieros (66 y 76) ni el impuesto (630).</summary>
    public static bool EsExplotacion(string c) => EsResultado(c) && !Empieza(c, "630", "66", "76");

    /// <summary>Movimientos que cuentan para el resultado: sin regularización ni cierre.</summary>
    public static bool EntraEnResultado(byte tipo) => tipo is not (TipoRegularizacion or TipoCierre);

    /// <summary>EBITDA (haber − debe de sus cuentas) de unos movimientos hasta una fecha, sin regularización ni cierre.</summary>
    public static decimal Ebitda(IEnumerable<MovimientoRatio> movimientos, DateTime hasta) =>
        movimientos.Where(m => m.Fecha <= hasta && EntraEnResultado(m.TipoAsiento) && EsEbitda(m.Cuenta)).Sum(m => m.Haber - m.Debe);

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string Imp(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("#,##0.00", Es) + " €";

    /// <param name="situacion">Modelo de balance de situación (con los códigos de apartado y título de sus reglas).</param>
    /// <param name="balance">Movimientos del ejercicio del balance; cuentan los de fecha hasta <paramref name="fechaBalance"/>, sin el cierre.</param>
    /// <param name="resultado">Movimientos de los que sale el resultado de los 12 meses (pueden ser de dos ejercicios).</param>
    /// <param name="inversiones">Subcuentas bancarias marcadas como inversión: no son disponible, cuentan como realizable.</param>
    /// <param name="sinResultado">Si no hay datos para los 12 meses, el motivo: los ratios que usan el resultado salen sin datos.</param>
    /// <param name="bancos">
    /// Cuentas contables de las cuentas bancarias de Norma 43: son disponible (con su saldo deudor como activo) aunque el modelo las
    /// clasifique en otro sitio (por ejemplo una 555 que se usa como banco), igual que en la tesorería de la pantalla de inicio.
    /// </param>
    public static ResultadoRatios Calcular(
        ModeloBalance situacion, IEnumerable<MovimientoRatio> balance, IEnumerable<MovimientoRatio> resultado, DateTime fechaBalance,
        IReadOnlySet<string>? inversiones = null, string? sinResultado = null, IReadOnlySet<string>? bancos = null)
    {
        inversiones ??= new HashSet<string>();
        bancos ??= new HashSet<string>();
        var desde = DesdeResultado(fechaBalance);
        var saldos = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var hayBalance = false;
        foreach (var m in balance)
        {
            if (m.Fecha > fechaBalance || m.TipoAsiento == TipoCierre || m.Cuenta.Length == 0) continue;
            hayBalance = true;
            saldos[m.Cuenta] = saldos.GetValueOrDefault(m.Cuenta) + m.Debe - m.Haber;
        }

        // Masas del balance con el modelo de situación.
        var masas = new Dictionary<string, List<CuentaPartida>>(StringComparer.Ordinal);
        var sinClasificar = new List<string>();
        void Sumar(string masa, string cuenta, decimal importe)
        {
            if (!masas.TryGetValue(masa, out var l)) masas[masa] = l = [];
            l.Add(new CuentaPartida(cuenta, importe));
        }
        foreach (var (cuenta, saldo) in saldos.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (saldo == 0) continue;
            if (bancos.Contains(cuenta))
            {
                Sumar(inversiones.Contains(cuenta) ? "realizable" : "disponible", cuenta, saldo);
                continue;
            }
            var regla = situacion.Regla(cuenta);
            if (regla?.CodigoApartado is not { } ap) { sinClasificar.Add(cuenta); continue; }
            var importe = saldo * regla.Multiplicador;
            var titulo = regla.CodigoTitulo ?? "";
            switch (ap)
            {
                case "ACT.A": Sumar("anc", cuenta, importe); break;
                case "ACT.B":
                    if (titulo.StartsWith("ACT.B.1.", StringComparison.Ordinal)) Sumar("existencias", cuenta, importe);
                    else if (titulo.StartsWith("ACT.B.6.", StringComparison.Ordinal) && !inversiones.Contains(cuenta)) Sumar("disponible", cuenta, importe);
                    else Sumar("realizable", cuenta, importe);
                    break;
                case "PNP.A": Sumar("pn", cuenta, importe); break;
                case "PNP.B": Sumar("pnc", cuenta, importe); break;
                case "PNP.C": Sumar("pc", cuenta, importe); break;
                default: sinClasificar.Add(cuenta); break;
            }
        }
        IReadOnlyList<CuentaPartida> M(params string[] claves) => claves.SelectMany(k => masas.GetValueOrDefault(k) ?? []).OrderBy(c => c.Cuenta, StringComparer.Ordinal).ToList();
        PartidaRatio P(string nombre, string cuentas, IReadOnlyList<CuentaPartida> detalle) => new(nombre, cuentas, detalle.Sum(d => d.Importe), detalle);
        string Grupos(IReadOnlyList<CuentaPartida> d, string? extra = null)
        {
            var g = d.Select(c => c.Cuenta.Length >= 3 ? c.Cuenta[..3] : c.Cuenta).Distinct().ToList();
            var texto = g.Count == 0 ? "ninguna cuenta con saldo" : string.Join(", ", g.Take(14)) + (g.Count > 14 ? "…" : "");
            return extra is null ? texto : texto + " · " + extra;
        }
        var invEnBalance = M("realizable").Where(c => inversiones.Contains(c.Cuenta)).Select(c => c.Cuenta).ToList();
        var notaInv = invEnBalance.Count == 0 ? null : $"incluye las cuentas de inversión {string.Join(", ", invEnBalance)}";
        var notaSinInv = invEnBalance.Count == 0 ? null : $"sin las cuentas de inversión {string.Join(", ", invEnBalance)}";

        var activoCorriente = M("existencias", "realizable", "disponible");
        var pAC = P("Activo corriente", "B. Activo corriente del balance: " + Grupos(activoCorriente), activoCorriente);
        var realizable = M("realizable");
        var pReal = P("Realizable", "Activo corriente sin existencias ni efectivo: " + Grupos(realizable, notaInv), realizable);
        var disponible = M("disponible");
        var bancosFuera = disponible.Where(c => bancos.Contains(c.Cuenta) && situacion.Regla(c.Cuenta)?.CodigoTitulo?.StartsWith("ACT.B.6.", StringComparison.Ordinal) != true)
            .Select(c => c.Cuenta).ToList();
        var notaDisp = string.Join(" · ", new[] { notaSinInv, bancosFuera.Count == 0 ? null : $"incluye las cuentas de bancos de Norma 43 {string.Join(", ", bancosFuera)}" }.Where(x => x is not null));
        var pDisp = P("Disponible", "B.VI Efectivo y otros activos líquidos: " + Grupos(disponible, notaDisp.Length == 0 ? null : notaDisp), disponible);
        var pc = M("pc");
        var pPC = P("Pasivo corriente", "C. Pasivo corriente del balance: " + Grupos(pc), pc);
        var exigible = M("pnc", "pc");
        var pPE = P("Pasivo exigible", "Pasivo no corriente + pasivo corriente: " + Grupos(exigible), exigible);
        var activo = M("anc", "existencias", "realizable", "disponible");
        var pAT = P("Activo total", "Activo no corriente + activo corriente: " + Grupos(activo), activo);
        var pn = M("pn");
        var pPN = P("Patrimonio neto", "A. Patrimonio neto, con el resultado del ejercicio hasta la fecha del balance: " + Grupos(pn), pn);

        var clientes = saldos.Where(x => x.Value != 0 && EnRango(x.Key, "430", "436")).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new CuentaPartida(x.Key, x.Value)).ToList();
        var pCli = P("Clientes", "Saldo de las 430 a 436", clientes);
        var proveedores = saldos.Where(x => x.Value != 0 && EnRango(x.Key, "400", "410")).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new CuentaPartida(x.Key, -x.Value)).ToList();
        var pProv = P("Proveedores y acreedores", "Saldo acreedor de las 400 a 410", proveedores);

        // Resultado de los 12 meses.
        var pyg = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var ivaRep = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var ivaSop = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var m in resultado)
        {
            if (m.Fecha < desde || m.Fecha > fechaBalance || !EntraEnResultado(m.TipoAsiento)) continue;
            if (EsResultado(m.Cuenta)) pyg[m.Cuenta] = pyg.GetValueOrDefault(m.Cuenta) + m.Haber - m.Debe;
            // IVA: el haber de las 477 y el debe de las 472; los asientos de liquidación cargan la 477 y abonan la 472, así que no suman.
            else if (m.Cuenta.StartsWith("477", StringComparison.Ordinal) && m.Haber != 0) ivaRep[m.Cuenta] = ivaRep.GetValueOrDefault(m.Cuenta) + m.Haber;
            else if (m.Cuenta.StartsWith("472", StringComparison.Ordinal) && m.Debe != 0) ivaSop[m.Cuenta] = ivaSop.GetValueOrDefault(m.Cuenta) + m.Debe;
        }
        IReadOnlyList<CuentaPartida> R(Func<string, bool> filtro, int signo = 1) =>
            pyg.Where(x => x.Value != 0 && filtro(x.Key)).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new CuentaPartida(x.Key, x.Value * signo)).ToList();
        var periodo = $"del {desde:dd/MM/yyyy} al {fechaBalance:dd/MM/yyyy}";
        var pRN = P("Resultado neto (12 meses)", $"Grupo 7 menos grupo 6, impuesto incluido, {periodo}", R(EsResultado));
        var pRE = P("Resultado de explotación (12 meses)", $"Grupos 7 y 6 sin los financieros (66, 76) ni el impuesto (630), {periodo}", R(EsExplotacion));
        var pEB = P("EBITDA (12 meses)", $"Grupos 7 y 6 sin 630, amortizaciones (68), deterioros y provisiones (69, 79) ni financieros (66, 76), {periodo}", R(EsEbitda));
        var ventas = R(c => c.StartsWith("70", StringComparison.Ordinal));
        var pVen = P("Ventas (12 meses)", $"Grupo 70, haber menos debe, {periodo}", ventas);
        var rep = ivaRep.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new CuentaPartida(x.Key, x.Value)).ToList();
        var pVenIva = P("Ventas con IVA (12 meses)", $"Grupo 70 más el IVA repercutido (haber de las 477), {periodo}", [.. ventas, .. rep]);
        var compras = R(c => c.StartsWith("60", StringComparison.Ordinal) || c.StartsWith("62", StringComparison.Ordinal), -1);
        var sop = ivaSop.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new CuentaPartida(x.Key, x.Value)).ToList();
        var pComIva = P("Compras y gastos con IVA (12 meses)", $"Grupos 60 y 62 más el IVA soportado (debe de las 472), {periodo}", [.. compras, .. sop]);

        var d = Definiciones.ToDictionary(x => x.Clave);
        var ratios = new List<ValorRatio>
        {
            Ratio(d["liquidez"], pAC, pPC, 1m, null, pAC, pPC).ConNumeradorPositivo(pAC),
            Ratio(d["acida"], P("Realizable + Disponible", "", [.. realizable, .. disponible]), pPC, 1m, null, pReal, pDisp, pPC),
            Ratio(d["tesoreria"], pDisp, pPC, 1m, null, pDisp, pPC).ConNumeradorPositivo(pDisp),
            Ratio(d["solvencia"], pAT, pPE, 1m, null, pAT, pPE).ConNumeradorPositivo(pAT),
            Ratio(d["endeudamiento"], pPE, pPN, 1m, null, pPE, pPN).ConNumeradorPositivo(pPE),
            Ratio(d["calidad"], pPC, pPE, 1m, null, pPC, pPE).ConNumeradorPositivo(pPC),
            Ratio(d["roe"], pRN, pPN, 100m, sinResultado, pRN, pPN),
            Ratio(d["roa"], pRE, pAT, 100m, sinResultado, pRE, pAT),
            Ratio(d["ebitda"], pEB, pVen, 100m, sinResultado, pEB, pVen),
            Ratio(d["neto"], pRN, pVen, 100m, sinResultado, pRN, pVen),
            Ratio(d["pmc"], pCli, pVenIva, 365m, sinResultado, pCli, pVenIva),
            Ratio(d["pmp"], pProv, pComIva, 365m, sinResultado, pProv, pComIva),
        };
        if (!hayBalance)
            ratios = ratios.Select(r => r with { Valor = null, Estado = EstadoRatio.SinDatos, Motivo = $"No hay balance a {fechaBalance:dd/MM/yyyy}: no hay apuntes contabilizados hasta esa fecha." }).ToList();
        return new ResultadoRatios(fechaBalance, desde, hayBalance, ratios, sinClasificar);
    }

    /// <summary>
    /// Valor = numerador / divisor × factor. Sin datos (con el motivo, nunca un número inventado) si el divisor es cero o negativo,
    /// o si falta el resultado de los 12 meses.
    /// </summary>
    private static ValorRatio Ratio(DefinicionRatio def, PartidaRatio numerador, PartidaRatio divisor, decimal factor, string? sinResultado, params PartidaRatio[] partidas)
    {
        if (sinResultado is not null) return new ValorRatio(def, null, EstadoRatio.SinDatos, sinResultado, partidas);
        if (divisor.Importe <= 0)
            return new ValorRatio(def, null, EstadoRatio.SinDatos,
                divisor.Importe == 0 ? $"{divisor.Nombre} es cero: el ratio no se puede calcular." : $"{divisor.Nombre} es negativo ({Imp(divisor.Importe)}): el ratio no tiene sentido.",
                partidas);
        var v = numerador.Importe / divisor.Importe * factor;
        return new ValorRatio(def, v, def.Estado(v), null, partidas);
    }

    /// <summary>
    /// En los ratios de balance, una masa negativa en el numerador (un pasivo corriente con saldo deudor, por ejemplo) no da un
    /// valor con sentido: sin datos, con el motivo.
    /// </summary>
    private static ValorRatio ConNumeradorPositivo(this ValorRatio v, PartidaRatio numerador) =>
        v.Valor is null || numerador.Importe >= 0 ? v
            : v with { Valor = null, Estado = EstadoRatio.SinDatos, Motivo = $"{numerador.Nombre} es negativo ({Imp(numerador.Importe)}): el ratio no tiene sentido." };

    private static bool EnRango(string cuenta, string desde, string hasta) =>
        cuenta.Length >= 3 && string.CompareOrdinal(cuenta[..3], desde) >= 0 && string.CompareOrdinal(cuenta[..3], hasta) <= 0;
}
