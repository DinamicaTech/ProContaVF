using ProConta.Compras.Facturas;
using ProConta.Contabilidad.Mayor;
using ProConta.Nucleo.Identificacion;
using static ProConta.Inicio.Salud.CalendarioFiscal;

namespace ProConta.Inicio.Salud;

// Avisos de salud S1 a S10, a partir de las incidencias tratadas con la gestoría (contabilidad, Decisions 2026-11-15 21:10).
// Cada uno lo define la línea «Derived from contabilidad» de su nodo dueño. Solo leen; ninguno corrige nada ni se basa en el
// signo de una cuenta.

/// <summary>
/// Saldo justificado (contabilidad 2026-11-15 21:10): una subcuenta marcada «Cuadrada» en el Mayor en el ejercicio de trabajo no
/// cuenta en los avisos por saldo de subcuentas, mientras su saldo sea el que tenía al marcarla (la nota del cuadre es el
/// expediente de la excepción); si el saldo ha cambiado, el Mayor la pasa a «en curso» y vuelve a avisar.
/// </summary>
public static class SaldoJustificado
{
    public static bool Es(string cuenta, decimal saldo, IReadOnlyDictionary<string, decimal?>? cuadradas) =>
        cuadradas is not null && cuadradas.TryGetValue(cuenta, out var marcado) && (marcado is null || Math.Abs(marcado.Value - saldo) < CuadresMayor.Tolerancia);

    public const string Texto = "No cuentan las subcuentas marcadas «Cuadrada» en el Mayor con el mismo saldo (saldo justificado con su nota).";

    public const string ComoJustificar = "Si el saldo está justificado, márcala «Cuadrada» en el Mayor y explica el motivo en la nota: deja de avisar mientras el saldo no cambie.";
}

internal static class Apuntes
{
    public static bool Empieza(string cuenta, params string[] prefijos) => prefijos.Any(p => cuenta.StartsWith(p, StringComparison.Ordinal));

    public static string Lista(IEnumerable<string> partes, int max = 4)
    {
        var l = partes.ToList();
        return string.Join(", ", l.Take(max)) + (l.Count > max ? "…" : "");
    }

    /// <summary>Saldo (debe − haber) de cada subcuenta con los apuntes del ejercicio hasta la fecha, apertura incluida.</summary>
    public static Dictionary<string, decimal> Saldos(IEnumerable<ApunteCuenta> apuntes, int ejercicio, DateTime hasta) =>
        apuntes.Where(a => a.Ejercicio == ejercicio && a.Fecha <= hasta).GroupBy(a => a.Cuenta, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Saldo), StringComparer.Ordinal);
}

/// <summary>
/// S1 «Saldos inmóviles» (contabilidad/mayor), aviso: subcuentas de los grupos 44, 47 (sin 472, 477, 4700, 4750 ni 4751) y 55
/// (sin las de los bancos de Norma 43) con saldo a la fecha de corte y sin ningún apunte, salvo apertura y regularización,
/// en los últimos 12 meses (se miran el ejercicio de trabajo y el anterior). Las fianzas (18, 26 y 56) no entran: no se mueven
/// mientras dura el contrato.
/// </summary>
public sealed class RevisionSaldosInmoviles : IRevisionSalud
{
    public const int Meses = 12;
    public static readonly string[] Prefijos = ["44", "47", "55"];
    private static readonly string[] Excluidas = ["472", "477", "4700", "4750", "4751"];

    public string Clave => "saldos-inmoviles";
    public string Nombre => "Sin saldos inmóviles en cuentas puente";
    public string Nodo => "contabilidad/mayor";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var ejercicios = ctx.Ejercicios.Where(e => e.Ejercicio == ctx.Ejercicio.Ejercicio || e.Ejercicio == ctx.Ejercicio.Ejercicio - 1).ToList();
        var apuntes = await fuente.ApuntesCuentasAsync(ejercicios, Prefijos, c);
        var bancos = (await fuente.BancosAsync(ctx.IdSociedad, ctx, c)).Where(b => b.Cuenta is not null).Select(b => b.Cuenta!).ToHashSet(StringComparer.Ordinal);
        return Evaluar(apuntes, ctx.Ejercicio.Ejercicio, ctx.Corte, bancos, await fuente.CuadradasAsync(ctx.Ejercicio, c));
    }

    public static HallazgosSalud Evaluar(IReadOnlyList<ApunteCuenta> apuntes, int ejercicio, DateTime corte, IReadOnlySet<string> bancos, IReadOnlyDictionary<string, decimal?>? cuadradas = null)
    {
        var limite = corte.Date.AddMonths(-Meses);
        var ultimo = apuntes.Where(a => a.TipoAsiento is not (1 or 2) && a.Fecha <= corte).GroupBy(a => a.Cuenta, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(a => a.Fecha), StringComparer.Ordinal);
        var inmoviles = Apuntes.Saldos(apuntes, ejercicio, corte)
            .Select(s => (Cuenta: s.Key, Saldo: Math.Round(s.Value, 2, MidpointRounding.AwayFromZero), Ultimo: ultimo.TryGetValue(s.Key, out var u) ? u : (DateTime?)null))
            .Where(s => s.Saldo != 0 && Apuntes.Empieza(s.Cuenta, Prefijos) && !Apuntes.Empieza(s.Cuenta, Excluidas) && !bancos.Contains(s.Cuenta)
                        && (s.Ultimo is null || s.Ultimo < limite) && !SaldoJustificado.Es(s.Cuenta, s.Saldo, cuadradas))
            .OrderBy(s => s.Cuenta, StringComparer.Ordinal).ToList();
        if (inmoviles.Count == 0)
            return HallazgosSalud.Correcta($"Ninguna cuenta puente (44, 47, 55) tiene saldo sin moverse desde hace más de {Meses} meses", "contabilidad/mayor");
        var total = inmoviles.Sum(s => Math.Abs(s.Saldo));
        var detalle = Apuntes.Lista(inmoviles.Select(s => $"{s.Cuenta} {Imp(s.Saldo)}" + (s.Ultimo is DateTime u ? $" (último apunte {Fecha(u)})" : " (solo apertura)")));
        return HallazgosSalud.De(new ElementoSalud("saldos-inmoviles", TipoSalud.Aviso, GravedadSalud.Aviso,
            $"{N(inmoviles.Count, "subcuenta puente", "subcuentas puente")} con saldo sin moverse en {Meses} meses",
            $"{detalle}. En total {Imp(total)} en valor absoluto: subvenciones, retenciones, cuentas con socios, transferencias en curso o partidas que nadie ha revisado.",
            null, DestinoSalud.Mayor(inmoviles.Select(s => s.Cuenta).ToList(), "saldos inmóviles"), "contabilidad/mayor",
            $"Alguna subcuenta de los grupos 44, 47 (sin las de IVA y retenciones que vigilan otros avisos) o 55 (sin los bancos) tiene saldo al {Fecha(corte)} " +
            $"y ningún apunte, salvo apertura, desde el {Fecha(limite)}. {SaldoJustificado.Texto}",
            "Abre el Mayor de esas subcuentas y averigua el origen de cada saldo antes de regularizarlo: no lo saldes solo porque es antiguo. " + SaldoJustificado.ComoJustificar)
        { Cantidad = inmoviles.Count, Importe = total });
    }
}

/// <summary>
/// S2 «IVA a compensar ya usado» (contabilidad/procesos/liquidacion-iva), atención: la 4700 tiene saldo deudor a la fecha de corte
/// que ya tenía antes de una liquidación posterior que salió a ingresar (abona la 4750) sin abonar la 4700.
/// </summary>
public sealed class RevisionIvaCompensarUsado : IRevisionSalud
{
    public string Clave => "iva-compensar-usado";
    public string Nombre => "IVA a compensar aplicado";
    public string Nodo => "contabilidad/procesos/liquidacion-iva";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.ApuntesCuentasAsync([ctx.Ejercicio], ["4700", "4750"], c), ctx.Ejercicio.Ejercicio, ctx.Corte);

    public static HallazgosSalud Evaluar(IReadOnlyList<ApunteCuenta> apuntes, int ejercicio, DateTime corte)
    {
        var del = apuntes.Where(a => a.Ejercicio == ejercicio && a.Fecha <= corte).ToList();
        decimal Credito(Func<ApunteCuenta, bool> filtro) => del.Where(a => a.Cuenta.StartsWith("4700", StringComparison.Ordinal) && filtro(a)).Sum(a => a.Saldo);
        var credito = Credito(_ => true);
        if (Math.Round(credito, 2) <= 0) return HallazgosSalud.Nada;
        // Asientos de liquidación a ingresar (abonan la 4750), sin la apertura; en orden de fecha.
        var aIngresar = del.Where(a => a.TipoAsiento != 1 && a.Cuenta.StartsWith("4750", StringComparison.Ordinal) && a.Haber > 0)
            .GroupBy(a => a.IdAsiento).Select(g => (Id: g.Key, Fecha: g.Min(a => a.Fecha), Importe: g.Sum(a => a.Haber))).OrderBy(x => x.Fecha).ToList();
        foreach (var l in aIngresar)
        {
            var antes = Credito(a => a.Fecha < l.Fecha);
            var compensa = del.Any(a => a.IdAsiento == l.Id && a.Cuenta.StartsWith("4700", StringComparison.Ordinal) && a.Haber > 0);
            if (compensa || Math.Round(antes, 2) <= 0 || credito < antes - 0.005m) continue;
            var usado = Math.Min(antes, l.Importe);
            return HallazgosSalud.De(new ElementoSalud("iva-compensar-usado", TipoSalud.Aviso, GravedadSalud.Atencion,
                "IVA a compensar sin aplicar en la 4700",
                $"La 4700 tenía {Imp(antes)} a compensar antes de la liquidación del {Fecha(l.Fecha)}, que salió a ingresar {Imp(l.Importe)} sin aplicarlo, y al {Fecha(corte)} sigue con {Imp(credito)}. " +
                "Si ese crédito se descontó en el 303, la 4700 está inflada; si no, se pudo compensar.",
                null, DestinoSalud.Mayor(del.Where(a => a.Cuenta.StartsWith("4700", StringComparison.Ordinal)).Select(a => a.Cuenta).Distinct().ToList(), "IVA a compensar"),
                "contabilidad/procesos/liquidacion-iva",
                "La 4700 (Hacienda deudora por IVA) tiene saldo a la fecha de corte que ya tenía antes de una liquidación posterior que salió a ingresar (abona la 4750) sin abonar la 4700.",
                $"Comprueba en el 303 de esa liquidación si se compensó el crédito: si se compensó, contabiliza el traspaso de la 4700 a la 4750 por lo compensado (hasta {Imp(usado)}); " +
                "si no, compénsalo en el siguiente 303. El aviso desaparece cuando la 4700 ya no conserva ese crédito.")
            { Importe = credito });
        }
        return HallazgosSalud.Nada;
    }
}

/// <summary>
/// S3 «Liquidación incompleta» (contabilidad/procesos/liquidacion-iva), atención: en un trimestre terminado con asiento de
/// liquidación, alguna 472 o 477 (sin la 4729) no queda a cero con los apuntes hasta el fin del trimestre más el propio asiento
/// de liquidación (que puede tener fecha del plazo de presentación).
/// </summary>
public sealed class RevisionLiquidacionIncompleta : IRevisionSalud
{
    public string Clave => "liquidacion-incompleta";
    public string Nombre => "Liquidaciones de IVA completas";
    public string Nodo => "contabilidad/procesos/liquidacion-iva";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.ApuntesCuentasAsync([ctx.Ejercicio], ["472", "477", "4700", "4750"], c), ctx.Ejercicio.Ejercicio, ctx.Hoy);

    public static HallazgosSalud Evaluar(IReadOnlyList<ApunteCuenta> apuntes, int ejercicio, DateTime hoy)
    {
        var del = apuntes.Where(a => a.Ejercicio == ejercicio).ToList();
        // Asientos de liquidación: abonan la 4750 o cargan la 4700 (no la apertura), con el trimestre al que se asignan.
        var liquidaciones = del.Where(a => a.TipoAsiento != 1 && ((a.Cuenta.StartsWith("4750", StringComparison.Ordinal) && a.Haber > 0) || (a.Cuenta.StartsWith("4700", StringComparison.Ordinal) && a.Debe > 0)))
            .GroupBy(a => a.IdAsiento).Select(g => (Id: g.Key, Trimestre: RevisionPlazosIva.TrimestreLiquidado(g.Min(a => a.Fecha))))
            .Where(x => x.Trimestre.Anio == ejercicio).ToList();
        var problemas = new List<(int Q, string Cuenta, decimal Saldo)>();
        foreach (var q in liquidaciones.Select(l => l.Trimestre.Trimestre).Distinct().Order())
        {
            var (_, fin) = CalculoInicio.Trimestre(ejercicio, q);
            if (fin.Date >= hoy.Date) continue;
            var propias = liquidaciones.Where(l => l.Trimestre.Trimestre == q).Select(l => l.Id).ToHashSet();
            var saldos = del.Where(a => (a.Cuenta.StartsWith("472", StringComparison.Ordinal) || a.Cuenta.StartsWith("477", StringComparison.Ordinal)) && !a.Cuenta.StartsWith("4729", StringComparison.Ordinal))
                .Where(a => propias.Contains(a.IdAsiento) || a.Fecha <= fin.Date)
                .GroupBy(a => a.Cuenta, StringComparer.Ordinal).Select(g => (Cuenta: g.Key, Saldo: Math.Round(g.Sum(a => a.Saldo), 2, MidpointRounding.AwayFromZero)));
            problemas.AddRange(saldos.Where(s => s.Saldo != 0).Select(s => (q, s.Cuenta, s.Saldo)));
        }
        if (problemas.Count == 0)
            return liquidaciones.Count == 0 ? HallazgosSalud.Nada : HallazgosSalud.Correcta("Las liquidaciones de IVA dejan a cero las 472 y 477 de su trimestre", "contabilidad/procesos/liquidacion-iva");
        var trimestres = string.Join(", ", problemas.Select(p => $"{p.Q}T").Distinct());
        return HallazgosSalud.De(new ElementoSalud("liquidacion-incompleta", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"Liquidación de IVA incompleta: {trimestres}",
            $"Tras el asiento de liquidación quedan con saldo {Apuntes.Lista(problemas.Select(p => $"{p.Cuenta} {Imp(p.Saldo)} ({p.Q}T)"))}.",
            null, DestinoSalud.Mayor(problemas.Select(p => p.Cuenta).Distinct().ToList(), "IVA sin liquidar"), "contabilidad/procesos/liquidacion-iva",
            "En un trimestre terminado que tiene asiento de liquidación (abona la 4750 o carga la 4700), alguna 472 o 477 (sin la 4729 de la prorrata) no queda a cero " +
            "con los apuntes hasta el último día del trimestre más el propio asiento de liquidación: el asiento no recoge todo el IVA del trimestre o lleva una cuenta o un signo equivocados.",
            "Revisa en el Mayor qué apunte falta en la liquidación (una factura reclasificada después, una cuenta cambiada) y regenera el asiento de liquidación de ese trimestre. " +
            "El aviso desaparece cuando esas cuentas quedan a cero.")
        { Cantidad = problemas.Count, Importe = problemas.Sum(p => Math.Abs(p.Saldo)) });
    }
}

/// <summary>
/// S4 «Retenciones sin cuadrar» (contabilidad/modelos-tributarios), atención: lo que la 4751 tiene pendiente de ingresar al cerrar
/// un trimestre debe ser lo que se paga en el mes siguiente (el 111 y el 115). Por subcuenta 4751 y trimestre cuyo mes de pago
/// ya ha terminado: saldo acreedor al fin del trimestre (con la apertura) menos los cargos del mes siguiente; avisa si la
/// diferencia pasa de 0,05 €. El 4T del año anterior se mira con la apertura y los pagos de enero.
/// </summary>
public sealed class RevisionRetenciones : IRevisionSalud
{
    public const decimal Tolerancia = 0.05m;
    public string Clave => "retenciones-4751";
    public string Nombre => "Retenciones pagadas igual que las contabilizadas";
    public string Nodo => "contabilidad/modelos-tributarios";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.ApuntesCuentasAsync([ctx.Ejercicio], ["4751"], c), ctx.Ejercicio.Ejercicio, ctx.Hoy);

    public static HallazgosSalud Evaluar(IReadOnlyList<ApunteCuenta> apuntes, int ejercicio, DateTime hoy)
    {
        var del = apuntes.Where(a => a.Ejercicio == ejercicio && a.Cuenta.StartsWith("4751", StringComparison.Ordinal)).ToList();
        if (del.Count == 0) return HallazgosSalud.Nada;
        var diferencias = new List<(string Nombre, string Cuenta, decimal Pendiente, decimal Pagado)>();
        var revisados = 0;
        // (nombre, pendiente al cierre, mes de pago): el 4T anterior es la apertura; los demás, el saldo al fin del trimestre.
        var periodos = new List<(string Nombre, Func<ApunteCuenta, bool> Pendiente, int MesPago)>
        {
            ($"4T {ejercicio - 1}", a => a.TipoAsiento == 1, 1),
            ($"1T {ejercicio}", a => a.Fecha <= new DateTime(ejercicio, 3, 31), 4),
            ($"2T {ejercicio}", a => a.Fecha <= new DateTime(ejercicio, 6, 30), 7),
            ($"3T {ejercicio}", a => a.Fecha <= new DateTime(ejercicio, 9, 30), 10),
        };
        // Una diferencia que no se corrige se arrastra a los trimestres siguientes: solo se señala en el que aparece.
        var arrastre = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (nombre, pendienteDe, mes) in periodos)
        {
            if (new DateTime(ejercicio, mes, 1).AddMonths(1) > hoy.Date) continue;
            revisados++;
            foreach (var g in del.GroupBy(a => a.Cuenta, StringComparer.Ordinal))
            {
                var pendiente = -g.Where(pendienteDe).Sum(a => a.Saldo);
                var pagado = g.Where(a => a.TipoAsiento != 1 && a.Fecha.Month == mes && a.Debe > 0).Sum(a => a.Debe);
                var nueva = pendiente - pagado - arrastre.GetValueOrDefault(g.Key);
                arrastre[g.Key] = pendiente - pagado;
                if (Math.Abs(nueva) > Tolerancia) diferencias.Add((nombre, g.Key, pendiente, pagado));
            }
        }
        if (diferencias.Count == 0)
            return revisados == 0 ? HallazgosSalud.Nada : HallazgosSalud.Correcta("Las retenciones pendientes de cada trimestre se pagan por su importe (4751)", "contabilidad/modelos-tributarios");
        var total = diferencias.Sum(d => Math.Abs(d.Pendiente - d.Pagado));
        return HallazgosSalud.De(new ElementoSalud("retenciones-4751", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"Retenciones sin cuadrar: {string.Join(", ", diferencias.Select(d => d.Nombre).Distinct())}",
            Apuntes.Lista(diferencias.Select(d => $"{d.Cuenta} {d.Nombre}: pendiente {Imp(d.Pendiente)}, pagado {Imp(d.Pagado)}"), 3) + ".",
            null, DestinoSalud.Mayor(diferencias.Select(d => d.Cuenta).Distinct().ToList(), "retenciones"), "contabilidad/modelos-tributarios",
            "Lo que una subcuenta 4751 tiene pendiente de ingresar al cerrar un trimestre (saldo acreedor, con la apertura) no es lo que se carga en ella en el mes siguiente, " +
            $"cuando se pagan el 111 y el 115 (diferencia de más de {Imp(Tolerancia)}). Solo los trimestres cuyo mes de pago ha terminado.",
            "Una diferencia que no se corrige sigue en los trimestres siguientes, pero solo se señala en el primero. " +
            "Busca el origen antes de ajustar: una factura o nómina contabilizada después de presentar el modelo, una retención que falta o un pago con otro importe. " +
            "Corrige la factura o contabiliza el ajuste con la gestoría. El aviso desaparece cuando cuadran.")
        { Cantidad = diferencias.Count, Importe = total });
    }
}

/// <summary>S5 «Facturas pendientes de recibir sin llegar» (contabilidad/procesos/facturas-pendientes-recibir), atención: la 4009 con saldo pasado el 31 de marzo.</summary>
public sealed class RevisionPendientesRecibir : IRevisionSalud
{
    public string Clave => "pendientes-recibir";
    public string Nombre => "Facturas pendientes de recibir llegadas";
    public string Nodo => "contabilidad/procesos/facturas-pendientes-recibir";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        ctx.Corte < new DateTime(ctx.Ejercicio.Ejercicio, 4, 1) ? HallazgosSalud.Nada : Evaluar(await fuente.SaldosAsync(ctx.Ejercicio, "4009", "4009", ctx.Corte, c), ctx.Corte);

    public static HallazgosSalud Evaluar(IReadOnlyDictionary<string, decimal> saldos, DateTime corte)
    {
        if (corte.Month < 4) return HallazgosSalud.Nada;
        var con = saldos.Select(s => (Cuenta: s.Key, Saldo: Math.Round(s.Value, 2, MidpointRounding.AwayFromZero))).Where(s => s.Saldo != 0).OrderBy(s => s.Cuenta, StringComparer.Ordinal).ToList();
        if (con.Count == 0) return HallazgosSalud.Nada;
        var total = con.Sum(s => s.Saldo);
        return HallazgosSalud.De(new ElementoSalud("pendientes-recibir", TipoSalud.Aviso, GravedadSalud.Atencion,
            "Facturas pendientes de recibir que no han llegado",
            $"La 4009 tiene {Imp(Math.Abs(total))} al {Fecha(corte)}: {Apuntes.Lista(con.Select(s => $"{s.Cuenta} {Imp(s.Saldo)}"))}.",
            null, DestinoSalud.Mayor(con.Select(s => s.Cuenta).ToList(), "facturas pendientes de recibir"), "contabilidad/procesos/facturas-pendientes-recibir",
            "Pasado el 31 de marzo, la 4009 (facturas pendientes de recibir) aún tiene saldo: alguna factura provisionada al cierre no ha llegado o se ha contabilizado sin su reversión.",
            "Reclama la factura al proveedor o, si no va a llegar, regulariza la provisión con la gestoría. La reversión de la apertura deja el gasto con signo contrario: eso no es un error. " +
            "El aviso desaparece cuando la 4009 queda a cero.")
        { Cantidad = con.Count, Importe = total });
    }
}

/// <summary>S6 «NIF que no valida» (terceros/tercero), atención: clientes y proveedores con apuntes cuyo NIF español no es válido.</summary>
public sealed class RevisionNifInvalido : IRevisionSalud
{
    public string Clave => "nif-invalido";
    public string Nombre => "NIF válidos en clientes y proveedores";
    public string Nodo => "terceros/tercero";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.TercerosConNifAsync(ctx.IdSociedad, ctx.Ejercicio, c));

    /// <summary>Un NIF español mal escrito: no valida y no lleva el prefijo de otro país (VAT, pasaporte… no se validan).</summary>
    public static bool Invalido(string nif)
    {
        var t = new string(nif.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (t.Length == 0 || Nif.EsValido(t)) return false;
        if (t.Length > 2 && char.IsAsciiLetter(t[0]) && char.IsAsciiLetter(t[1]) && !t.StartsWith("ES", StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Identificaciones españolas, las mismas que valida la ficha del tercero (terceros/tercero 2026-10-31 19:05).</summary>
    private static readonly HashSet<string> Espanolas = new(StringComparer.Ordinal) { "", "NIF", "CIF", "NIE", "DNI" };

    /// <summary>
    /// Se valida como NIF español: identificación vacía, NIF, CIF, NIE o DNI, sin país o con España; u «OTRO» con el país España
    /// explícito (la migración dejó como «OTRO» los NIF que no validaban, también los españoles mal escritos).
    /// </summary>
    public static bool EsEspanol(TerceroNif t)
    {
        var pais = (t.Pais ?? "").Trim().ToUpperInvariant();
        var espana = pais is "ES" or "ESP" or "ESPAÑA" or "ESPANA" or "SPAIN";
        var tipo = (t.TipoIdentificacion ?? "").Trim().ToUpperInvariant();
        if (Espanolas.Contains(tipo)) return espana || pais.Length == 0;
        return tipo == "OTRO" && espana;
    }

    public static HallazgosSalud Evaluar(IReadOnlyList<TerceroNif> terceros)
    {
        var mal = terceros.Where(t => EsEspanol(t) && Invalido(t.Nif)).GroupBy(t => t.IdTercero).Select(g => g.First()).OrderBy(t => t.Nombre, StringComparer.CurrentCulture).ToList();
        if (mal.Count == 0)
            return terceros.Count == 0 ? HallazgosSalud.Nada : HallazgosSalud.Correcta("Los NIF españoles de los clientes y proveedores con apuntes son válidos", "terceros/tercero");
        return HallazgosSalud.De(new ElementoSalud("nif-invalido", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"{N(mal.Count, "cliente o proveedor", "clientes o proveedores")} con un NIF que no valida",
            $"{Apuntes.Lista(mal.Select(t => $"{t.Nombre} ({t.Nif})"))}. Darían problemas en el 347 y en los modelos.",
            null, new DestinoSalud(PantallaSalud.Terceros, "Datos maestros › Terceros"), "terceros/tercero",
            "Algún cliente o proveedor con apuntes en el ejercicio tiene en su ficha un NIF español (identificación vacía, NIF, CIF, NIE o DNI sin otro país, u «Otro» con el país España) cuya letra o dígito de control no es correcto. " +
            "El NIF solo se valida al darlo de alta o cambiarlo, así que los antiguos pueden estar mal. Los NIF de otros países no se validan.",
            "Abre la ficha de cada tercero, comprueba el NIF con su documentación y corrígelo; si es extranjero, pon su país o el tipo de identificación (VAT, pasaporte…). El aviso desaparece cuando todos validan.")
        { Cantidad = mal.Count });
    }
}

/// <summary>S7 «Gastos e ingresos sin centro de coste» (contabilidad/centros-coste), atención: apuntes de los grupos 6 y 7 sin centro.</summary>
public sealed class RevisionSinCentro : IRevisionSalud
{
    public string Clave => "sin-centro";
    public string Nombre => "Gastos e ingresos con centro de coste";
    public string Nodo => "contabilidad/centros-coste";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.CuentasSinCentroAsync(ctx.Ejercicio, c));

    public static HallazgosSalud Evaluar(IReadOnlyList<CuentaSinCentro> cuentas)
    {
        var sin = cuentas.Where(x => x.Apuntes > 0).OrderByDescending(x => Math.Abs(x.Importe)).ToList();
        if (sin.Count == 0) return HallazgosSalud.Correcta("Todos los gastos e ingresos del ejercicio tienen centro de coste", "contabilidad/centros-coste");
        var apuntes = sin.Sum(x => x.Apuntes);
        var importe = sin.Sum(x => Math.Abs(x.Importe));
        return HallazgosSalud.De(new ElementoSalud("sin-centro", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"{N(apuntes, "apunte de gasto o ingreso", "apuntes de gasto o ingreso")} sin centro de coste",
            $"En {N(sin.Count, "cuenta", "cuentas")} ({Imp(importe)} en valor absoluto): {Apuntes.Lista(sin.Select(x => $"{x.Cuenta} {N(x.Apuntes, "apunte", "apuntes")}"))}. " +
            "Sin centro no se pueden repartir por actividad en el Impuesto sobre Sociedades.",
            null, DestinoSalud.Mayor(sin.Select(x => x.Cuenta).ToList(), "sin centro de coste"), "contabilidad/centros-coste",
            "Hay apuntes contabilizados del ejercicio en cuentas de los grupos 6 y 7 sin centro de coste (sin contar la regularización ni el cierre).",
            "Abre el Mayor de esas cuentas y asigna el centro de coste a cada apunte (cambiar el centro no tiene efecto fiscal y se puede hacer también en trimestres cerrados). " +
            "El aviso desaparece cuando todos lo tienen.")
        { Cantidad = apuntes, Importe = importe });
    }
}

/// <summary>
/// S8 «Deuda antigua sin deterioro» (contabilidad/mayor), aviso: clientes (430 a 435) con saldo deudor a la fecha de corte del que
/// una parte tiene más de 6 meses (por el vencimiento o, sin él, por la fecha del cargo; los cobros saldan primero lo más antiguo).
/// </summary>
public sealed class RevisionDeudaAntigua : IRevisionSalud
{
    public const int Meses = 6;
    public string Clave => "deuda-antigua";
    public string Nombre => "Sin deuda de clientes de más de 6 meses";
    public string Nodo => "contabilidad/mayor";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.CarteraAsync(ctx.Ejercicio, c), ctx.Corte, await fuente.CuadradasAsync(ctx.Ejercicio, c));

    public static HallazgosSalud Evaluar(IReadOnlyList<ApunteCartera> cartera, DateTime corte, IReadOnlyDictionary<string, decimal?>? cuadradas = null)
    {
        var limite = corte.Date.AddMonths(-Meses);
        var antiguos = new List<(string Cuenta, decimal Saldo, decimal Antiguo)>();
        foreach (var g in cartera.Where(a => a.Fecha <= corte && string.CompareOrdinal(a.Cuenta.Length >= 3 ? a.Cuenta[..3] : a.Cuenta, "435") <= 0).GroupBy(a => a.Cuenta, StringComparer.Ordinal))
        {
            var saldo = Math.Round(g.Sum(a => a.Debe - a.Haber), 2, MidpointRounding.AwayFromZero);
            if (saldo <= 0 || SaldoJustificado.Es(g.Key, saldo, cuadradas)) continue;
            var pendiente = saldo;
            var antiguo = 0m;
            foreach (var cargo in g.Where(a => a.Debe - a.Haber > 0).OrderByDescending(a => a.Vencimiento ?? a.Fecha).ThenByDescending(a => a.Fecha))
            {
                if (pendiente <= 0) break;
                var parte = Math.Min(pendiente, cargo.Debe - cargo.Haber);
                if ((cargo.Vencimiento ?? cargo.Fecha) < limite) antiguo += parte;
                pendiente -= parte;
            }
            antiguo += Math.Max(pendiente, 0m);
            if (antiguo > 0.005m) antiguos.Add((g.Key, saldo, antiguo));
        }
        if (antiguos.Count == 0) return HallazgosSalud.Nada;
        var total = antiguos.Sum(x => x.Antiguo);
        return HallazgosSalud.De(new ElementoSalud("deuda-antigua", TipoSalud.Aviso, GravedadSalud.Aviso,
            $"{N(antiguos.Count, "cliente", "clientes")} con deuda de más de {Meses} meses",
            $"{Imp(total)} pendientes desde antes del {Fecha(limite)}: {Apuntes.Lista(antiguos.OrderByDescending(x => x.Antiguo).Select(x => $"{x.Cuenta} {Imp(x.Antiguo)}"))}.",
            null, DestinoSalud.Mayor(antiguos.Select(x => x.Cuenta).ToList(), "deuda antigua"), "contabilidad/mayor",
            $"Algún cliente (430 a 435) tiene saldo deudor al {Fecha(corte)} del que una parte venció (o se facturó) hace más de {Meses} meses. {SaldoJustificado.Texto}",
            "Comprueba si es deuda real (reclámala; si es dudosa, pásala a la 436 con la guía «Pasar un cliente de cobro dudoso a incobrable» y valora el deterioro con la gestoría) " +
            "o un error de conciliación (un cobro sin aplicar). No reconozcas dos veces la pérdida. " + SaldoJustificado.ComoJustificar)
        { Cantidad = antiguos.Count, Importe = total });
    }
}

/// <summary>
/// S9 «Seguridad Social pendiente» (nominas), atención, solo con nóminas en el ejercicio o en el anterior: la 476 con un saldo
/// acreedor mayor que sus abonos de los últimos 45 días, o la 471 con saldo deudor sin movimiento en 3 meses.
/// </summary>
public sealed class RevisionSeguridadSocial : IRevisionSalud
{
    public const int Dias476 = 45;
    public const int Meses471 = 3;
    public string Clave => "seguridad-social";
    public string Nombre => "Seguridad Social al día";
    public string Nodo => "nominas";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var ejercicios = ctx.Ejercicios.Where(e => e.Ejercicio == ctx.Ejercicio.Ejercicio || e.Ejercicio == ctx.Ejercicio.Ejercicio - 1).ToList();
        if ((await fuente.NominasAsync(ejercicios, c)).Count == 0) return HallazgosSalud.Nada;
        return Evaluar(await fuente.ApuntesCuentasAsync(ejercicios, ["471", "476"], c), ctx.Ejercicio.Ejercicio, ctx.Corte);
    }

    public static HallazgosSalud Evaluar(IReadOnlyList<ApunteCuenta> apuntes, int ejercicio, DateTime corte)
    {
        var desde476 = corte.Date.AddDays(-Dias476);
        var desde471 = corte.Date.AddMonths(-Meses471);
        var problemas = new List<(string Cuenta, string Texto, decimal Importe)>();
        foreach (var (cuenta, saldo) in Apuntes.Saldos(apuntes, ejercicio, corte).OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var s = Math.Round(saldo, 2, MidpointRounding.AwayFromZero);
            var mov = apuntes.Where(a => a.Cuenta == cuenta && a.TipoAsiento is not (1 or 2) && a.Fecha <= corte).ToList();
            if (cuenta.StartsWith("476", StringComparison.Ordinal) && s < 0)
            {
                var recientes = mov.Where(a => a.Fecha > desde476).Sum(a => a.Haber);
                if (-s > recientes + 0.05m) problemas.Add((cuenta, $"{cuenta}: {Imp(-s)} pendientes de pagar, más que las cuotas de los últimos {Dias476} días ({Imp(recientes)})", -s - recientes));
            }
            else if (cuenta.StartsWith("471", StringComparison.Ordinal) && s > 0 && (mov.Count == 0 || mov.Max(a => a.Fecha) < desde471))
                problemas.Add((cuenta, $"{cuenta}: {Imp(s)} a cobrar sin movimiento desde {(mov.Count == 0 ? "la apertura" : Fecha(mov.Max(a => a.Fecha)))}", s));
        }
        if (problemas.Count == 0) return HallazgosSalud.Correcta("La Seguridad Social (476 y 471) está al día", "nominas");
        return HallazgosSalud.De(new ElementoSalud("seguridad-social", TipoSalud.Aviso, GravedadSalud.Atencion,
            "Seguridad Social pendiente de aclarar",
            Apuntes.Lista(problemas.Select(p => p.Texto), 3) + ".",
            null, DestinoSalud.Mayor(problemas.Select(p => p.Cuenta).ToList(), "Seguridad Social"), "nominas",
            $"La 476 (Seguridad Social acreedora) tiene más saldo que las cuotas abonadas en los últimos {Dias476} días (hay cuotas de más de un mes sin pagar), " +
            $"o la 471 (Seguridad Social deudora: prestaciones y bajas) tiene saldo sin movimiento en {Meses471} meses.",
            "Concilia el resumen mensual de las nóminas con los recibos y los cargos del banco; reclama las devoluciones pendientes. " +
            "No confundas gasto (640, 642), deuda (476) y derecho de cobro (471). El aviso desaparece cuando cuadran.")
        { Cantidad = problemas.Count, Importe = problemas.Sum(p => p.Importe) });
    }
}

/// <summary>S10 «Posibles compras duplicadas» (compras/factura-compra), aviso: mismo proveedor y total, fechas a menos de 7 días y número distinto.</summary>
public sealed class RevisionComprasDuplicadas : IRevisionSalud
{
    public string Clave => "compras-duplicadas";
    public string Nombre => "Sin compras posiblemente duplicadas";
    public string Nodo => "compras/factura-compra";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.FacturasCompraAsync(ctx.IdSociedad, ctx.Ejercicio, c));

    public static HallazgosSalud Evaluar(IReadOnlyList<FilaFacturaCompra> facturas)
    {
        var grupos = ValidacionesCompra.PosiblesDuplicadas(facturas);
        if (grupos.Count == 0) return HallazgosSalud.Nada;
        var n = grupos.Sum(g => g.Count);
        var detalle = Apuntes.Lista(grupos.Select(g => $"{g[0].Proveedor}: {string.Join(" y ", g.Select(f => f.NumeroFactura.Length > 0 ? f.NumeroFactura : "sin número"))} ({Imp(g[0].ImporteTotal ?? 0m)})"), 3);
        return HallazgosSalud.De(new ElementoSalud("compras-duplicadas", TipoSalud.Aviso, GravedadSalud.Aviso,
            $"{N(grupos.Count, "posible compra duplicada", "posibles compras duplicadas")}",
            $"{detalle}. Pueden ser correctas: revísalas antes de tocar nada.",
            null, new DestinoSalud(PantallaSalud.FacturasCompra, "Compras › Facturas recibidas"), "compras/factura-compra",
            "Hay facturas de compra del ejercicio del mismo proveedor, con el mismo total, fechas a menos de 7 días y número distinto (sin las marcadas como duplicadas).",
            "En Compras › Facturas recibidas, compara los PDF: si una sobra, márcala como duplicada; si las dos son correctas (dos servicios iguales), no hagas nada. " +
            "Este aviso solo informa y sigue mientras se cumpla la condición.")
        { Cantidad = n });
    }
}
