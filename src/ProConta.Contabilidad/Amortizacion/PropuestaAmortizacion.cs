namespace ProConta.Contabilidad.Amortizacion;

/// <summary>
/// Un activo que puede entrar en la amortización: su plan, sus subcuentas del ejercicio de los roles «Gasto por amortización» (68x) y
/// «Amortización acumulada» (28x), su centro de coste y, si no puede entrar, por qué (<see cref="Problema"/>).
/// </summary>
public sealed record ActivoAmortizacion(int IdActivo, string Codigo, string Nombre, int? IdCentroCoste, PlanAmortizacion? Plan, string? CuentaGasto, string? CuentaAcumulada,
    string? Problema = null);

/// <summary>
/// Un apunte de una 68x o 28x de los activos en el ejercicio (contabilizado, sin apertura de otro modo, regularización ni cierre).
/// <paramref name="ClaveLote"/>: la clave del lote si es de este proceso; null si es de otro origen (a mano, ContaSol…).
/// </summary>
public sealed record MovimientoAmortizacion(string Cuenta, DateTime Fecha, decimal Debe, decimal Haber, string? ClaveLote = null);

/// <summary>Una fila de la propuesta: el activo, su cuota en el periodo, si entra (casilla) y una nota para el operador.</summary>
public sealed record FilaAmortizacion(ActivoAmortizacion Activo, CuotaAmortizacion? Cuota, bool Incluido, string? Nota)
{
    public decimal Importe => Incluido && Cuota is { } c ? c.Importe : 0m;
    public bool ConProblema => Activo.Problema is not null;
}

/// <summary>Una línea del asiento propuesto o grabado.</summary>
public sealed record LineaAmortizacion(string Cuenta, decimal Debe, decimal Haber, string Concepto, int? IdCentroCoste = null);

/// <summary>Propuesta de un periodo: las filas (con problemas, excluidas e incluidas), el asiento y los avisos.</summary>
public sealed record PropuestaAmortizacion(PeriodoAmortizacion Periodo, IReadOnlyList<FilaAmortizacion> Filas, IReadOnlyList<LineaAmortizacion> Lineas, IReadOnlyList<string> Avisos)
{
    public decimal Total => Lineas.Sum(l => l.Debe);
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
    public IEnumerable<FilaAmortizacion> Incluidas => Filas.Where(f => f.Incluido && f.Importe > 0m);

    /// <summary>Activos excluidos a mano (lo que se guarda en AmortizacionExclusiones).</summary>
    public IReadOnlyList<int> Excluidos => Filas.Where(f => !f.Incluido && !f.ConProblema).Select(f => f.Activo.IdActivo).ToList();

    /// <summary>Mismo asiento (lo que se graba): sirve para confirmar solo lo que se ve.</summary>
    public bool Igual(PropuestaAmortizacion otra) => Periodo.Clave == otra.Periodo.Clave && MismasLineas(Lineas, otra.Lineas);

    /// <summary>Las mismas líneas, sin mirar el orden.</summary>
    public static bool MismasLineas(IReadOnlyList<LineaAmortizacion> a, IReadOnlyList<LineaAmortizacion> b) =>
        a.Count == b.Count && Ordenar(a).SequenceEqual(Ordenar(b));

    private static IEnumerable<LineaAmortizacion> Ordenar(IEnumerable<LineaAmortizacion> l) =>
        l.OrderBy(x => x.Cuenta, StringComparer.Ordinal).ThenBy(x => x.Debe).ThenBy(x => x.Haber).ThenBy(x => x.IdCentroCoste ?? -1).ThenBy(x => x.Concepto, StringComparer.Ordinal);
}

/// <summary>
/// Propuesta de amortización de un periodo sin base de datos (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59): por cada
/// activo con plan y subcuentas, su cuota (<see cref="CalculoAmortizacion"/>); un asiento con un par de líneas por activo, 68x al Debe con
/// su centro de coste y 28x al Haber. Si varios activos comparten una 68x o una 28x, lo contabilizado en ella se reparte entre ellos
/// (lo ya cargado, según lo que toca a cada uno; la acumulada, según lo amortizable).
/// </summary>
public static class ProcesoAmortizacion
{
    public static string Descripcion(PeriodoAmortizacion p) => $"Amortización de activos · {p.Nombre}";

    public static string Concepto(PeriodoAmortizacion p, ActivoAmortizacion a) => Recortar($"Amortización {p.Nombre} · {a.Nombre}", 255);

    public static PropuestaAmortizacion Proponer(PeriodoAmortizacion periodo, IReadOnlyList<ActivoAmortizacion> activos, IReadOnlyList<MovimientoAmortizacion> movimientos,
        IReadOnlySet<int> excluidos, IEnumerable<string>? avisosFijos = null)
    {
        // Los que tocan este periodo: con problema (se enseñan) o con plan que se solapa con el año hasta el fin del periodo.
        var conProblema = activos.Where(a => a.Problema is not null).ToList();
        var validos = activos.Where(a => a.Problema is null && a.Plan is not null && a.CuentaGasto is not null && a.CuentaAcumulada is not null
                && CalculoAmortizacion.Dias(a.Plan, periodo.Ejercicio, periodo.Hasta) > 0 && !(a.Plan.FechaBaja is DateTime b && b.Date < periodo.Desde))
            .OrderBy(a => a.Codigo, StringComparer.Ordinal).ThenBy(a => a.IdActivo).ToList();

        var hasta = movimientos.Where(m => m.Fecha.Date <= periodo.Hasta && m.ClaveLote != periodo.Clave).ToList();
        var cargado = hasta.GroupBy(m => m.Cuenta, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(m => m.Debe - m.Haber), StringComparer.Ordinal);
        var abonado = hasta.GroupBy(m => m.Cuenta, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(m => m.Haber - m.Debe), StringComparer.Ordinal);

        // Lo que toca a cada uno hasta el fin del periodo (para repartir lo cargado en una 68x compartida).
        var teorico = validos.ToDictionary(a => a.IdActivo,
            a => CalculoAmortizacion.Redondear(a.Plan!.CuotaAnual * CalculoAmortizacion.Dias(a.Plan, periodo.Ejercicio, periodo.Hasta) / CalculoAmortizacion.DiasAno(periodo.Ejercicio)));
        var ya = new Dictionary<int, decimal>();
        foreach (var g in validos.GroupBy(a => a.CuentaGasto!, StringComparer.Ordinal))
        {
            var lista = g.ToList();
            var partes = CalculoAmortizacion.Repartir(cargado.GetValueOrDefault(g.Key), lista.Select(a => teorico[a.IdActivo]).ToList());
            for (var i = 0; i < lista.Count; i++) ya[lista[i].IdActivo] = partes[i];
        }
        var acumulada = new Dictionary<int, decimal>();
        foreach (var g in validos.GroupBy(a => a.CuentaAcumulada!, StringComparer.Ordinal))
        {
            var lista = g.ToList();
            var partes = CalculoAmortizacion.Repartir(abonado.GetValueOrDefault(g.Key), lista.Select(a => a.Plan!.Amortizable).ToList());
            for (var i = 0; i < lista.Count; i++) acumulada[lista[i].IdActivo] = partes[i];
        }
        var compartidas = validos.GroupBy(a => a.CuentaGasto!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)
            .Concat(validos.GroupBy(a => a.CuentaAcumulada!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var filas = new List<FilaAmortizacion>();
        var lineas = new List<LineaAmortizacion>();
        foreach (var a in validos)
        {
            var c = CalculoAmortizacion.Calcular(a.Plan!, periodo, ya[a.IdActivo], acumulada[a.IdActivo]);
            var incluido = !excluidos.Contains(a.IdActivo);
            string? nota = c.TotalmenteAmortizado ? "Totalmente amortizado: no queda nada pendiente."
                : c.Topada ? $"La cuota se recorta a lo que queda por amortizar."
                : c.Cuota == 0m && c.YaContabilizado > 0m ? "Ya contabilizado en el año: no falta nada hasta el fin del periodo."
                : null;
            if (compartidas.Contains(a.CuentaGasto!, StringComparer.Ordinal) || compartidas.Contains(a.CuentaAcumulada!, StringComparer.Ordinal))
                nota = (nota is null ? "" : nota + " ") + "Comparte subcuenta con otro activo: lo contabilizado en ella se reparte.";
            filas.Add(new FilaAmortizacion(a, c, incluido, nota));
            if (!incluido || c.Importe <= 0m) continue;
            var concepto = Concepto(periodo, a);
            lineas.Add(new LineaAmortizacion(a.CuentaGasto!, c.Importe, 0m, concepto, a.IdCentroCoste));
            lineas.Add(new LineaAmortizacion(a.CuentaAcumulada!, 0m, c.Importe, concepto));
        }
        foreach (var a in conProblema.OrderBy(a => a.Codigo, StringComparer.Ordinal))
            filas.Add(new FilaAmortizacion(a, null, false, a.Problema));

        var avisos = new List<string>(avisosFijos ?? []);
        if (conProblema.Count > 0)
            avisos.Add(conProblema.Count == 1
                ? $"Un activo amortizable no entra porque le falta algo: {conProblema[0].Codigo} {conProblema[0].Nombre} ({conProblema[0].Problema})"
                : $"{conProblema.Count} activos amortizables no entran porque les falta algo (plan, % o subcuentas): están al final de la lista con lo que les falta.");
        if (compartidas.Count > 0)
            avisos.Add($"Varios activos comparten {(compartidas.Count == 1 ? "la subcuenta" : "las subcuentas")} {string.Join(", ", compartidas)}: lo contabilizado en ellas se reparte entre esos activos.");
        return new PropuestaAmortizacion(periodo, filas, lineas, avisos);
    }

    /// <summary>La misma propuesta con otras casillas (sin volver a leer nada): recalcula el asiento.</summary>
    public static PropuestaAmortizacion Aplicar(PropuestaAmortizacion p, IReadOnlySet<int> excluidos)
    {
        var filas = p.Filas.Select(f => f.ConProblema ? f : f with { Incluido = !excluidos.Contains(f.Activo.IdActivo) }).ToList();
        var lineas = new List<LineaAmortizacion>();
        foreach (var f in filas.Where(f => f.Incluido && f.Importe > 0m))
        {
            var concepto = Concepto(p.Periodo, f.Activo);
            lineas.Add(new LineaAmortizacion(f.Activo.CuentaGasto!, f.Importe, 0m, concepto, f.Activo.IdCentroCoste));
            lineas.Add(new LineaAmortizacion(f.Activo.CuentaAcumulada!, 0m, f.Importe, concepto));
        }
        return p with { Filas = filas, Lineas = lineas };
    }

    private static string Recortar(string t, int n) => t.Length <= n ? t : t[..n];
}
