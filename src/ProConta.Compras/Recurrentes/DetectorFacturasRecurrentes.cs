using ProConta.Terceros.Roles;

namespace ProConta.Compras.Recurrentes;

/// <summary>
/// Factura de compra de la sociedad de trabajo que mira el detector: su registro, su proveedor, su fecha y su total, y si es duplicada o
/// rectificativa (factura rectificada indicada o importe negativo), que no cuentan.
/// </summary>
public sealed record FacturaParaRecurrencia(int IdRegistro, int? IdProveedor, DateTime Fecha, decimal ImporteTotal, bool Duplicada = false, bool Rectificativa = false);

/// <summary>Proveedor tal como lo enseña Compras › Facturas recibidas, con su tercero y la periodicidad fijada en su ficha (null, automática).</summary>
public sealed record ProveedorParaRecurrencia(int IdProveedor, int IdTercero, string Nombre, PeriodicidadFacturas? Periodicidad);

/// <summary>
/// Una factura recurrente que debería haber llegado: el proveedor, su periodicidad (y si es la fijada en la ficha), el mes en que se esperaba
/// (<paramref name="Periodo"/>, día 1) y la fecha exacta (<paramref name="FechaEsperada"/>), el importe estimado (el total de su última factura)
/// y esa última factura. <paramref name="Descartada"/>: el operador dijo «No llegará» para ese periodo.
/// </summary>
public sealed record FacturaRecurrentePendiente(
    int IdProveedor, int IdTercero, string Proveedor, PeriodicidadFacturas Periodicidad, bool PeriodicidadFijada, DateTime Periodo, DateTime FechaEsperada,
    decimal ImporteEstimado, DateTime FechaUltima, int IdRegistroUltima, bool Descartada = false)
{
    /// <summary>Ruta del PDF de la última factura, si se localiza (la pone el servicio; el detector no mira el disco).</summary>
    public string? RutaPdfUltima { get; init; }

    /// <summary>Meses de su periodicidad (1, 2 o 3).</summary>
    public int Meses => (int)Periodicidad;
}

/// <summary>
/// Detector de las facturas recurrentes pendientes (compras/facturas-recurrentes-pendientes 2026-11-23 18:55). Sin BD ni reloj: recibe las
/// facturas y el día de hoy. Reglas:
/// <list type="bullet">
/// <item>Solo cuentan las facturas con proveedor, no duplicadas, no rectificativas y con fecha en los 12 meses que acaban hoy.</item>
/// <item>Por proveedor, desde la más reciente hacia atrás, los meses entre facturas consecutivas (varias en un mes cuentan como una): con al menos
/// 3 facturas seguidas a 1, 2 o 3 meses, el proveedor es mensual, bimestral o trimestral.</item>
/// <item>La periodicidad fijada en la ficha manda: basta con una factura en esos 12 meses; «No controlar» lo deja fuera.</item>
/// <item>Fecha esperada = fecha de la última factura + su periodo; cuando hoy es posterior, sale una fila por cada periodo que falta hasta hoy.</item>
/// <item>Los periodos descartados no salen, salvo que se pidan (entonces van marcados).</item>
/// </list>
/// </summary>
public static class DetectorFacturasRecurrentes
{
    /// <summary>Facturas seguidas con el mismo intervalo que hacen falta para deducir la periodicidad.</summary>
    public const int FacturasMinimas = 3;

    /// <summary>Meses hacia atrás que se miran desde hoy.</summary>
    public const int MesesVentana = 12;

    /// <summary>Primer día de la ventana de facturas que se miran (hoy menos 12 meses).</summary>
    public static DateTime Desde(DateTime hoy) => hoy.Date.AddMonths(-MesesVentana);

    public static DateTime Periodo(DateTime fecha) => new(fecha.Year, fecha.Month, 1);

    /// <summary>Meses de diferencia entre dos fechas, mirando solo el mes y el año.</summary>
    public static int MesesEntre(DateTime anterior, DateTime posterior) => (posterior.Year - anterior.Year) * 12 + posterior.Month - anterior.Month;

    /// <summary>
    /// Periodicidad deducida de las fechas de las facturas de un proveedor: null si no tiene 3 facturas seguidas (contadas desde la más
    /// reciente, una por mes) a 1, 2 o 3 meses.
    /// </summary>
    public static PeriodicidadFacturas? Deducir(IEnumerable<DateTime> fechas)
    {
        var meses = fechas.Select(Periodo).Distinct().OrderByDescending(m => m).ToList();
        if (meses.Count < FacturasMinimas) return null;
        var intervalo = MesesEntre(meses[1], meses[0]);
        if (intervalo is < 1 or > 3) return null;
        var seguidas = 2;
        for (var i = 2; i < meses.Count && MesesEntre(meses[i], meses[i - 1]) == intervalo; i++) seguidas++;
        return seguidas >= FacturasMinimas ? (PeriodicidadFacturas)intervalo : null;
    }

    /// <summary>
    /// Facturas recurrentes que faltan hasta <paramref name="hoy"/>, ordenadas por proveedor y periodo.
    /// </summary>
    /// <param name="facturas">Facturas de compra de la sociedad (el detector aplica la ventana y las exclusiones).</param>
    /// <param name="proveedores">Proveedores por IdProveedor; uno que falte no sale.</param>
    /// <param name="descartados">Periodos descartados («No llegará»): proveedor y primer día del mes esperado.</param>
    /// <param name="mostrarDescartadas">true para incluirlos, marcados con <see cref="FacturaRecurrentePendiente.Descartada"/>.</param>
    public static IReadOnlyList<FacturaRecurrentePendiente> Detectar(
        IEnumerable<FacturaParaRecurrencia> facturas, IReadOnlyDictionary<int, ProveedorParaRecurrencia> proveedores,
        IReadOnlySet<(int IdProveedor, DateTime Periodo)> descartados, DateTime hoy, bool mostrarDescartadas = false)
    {
        hoy = hoy.Date;
        var desde = Desde(hoy);
        var validas = facturas.Where(f => f.IdProveedor is not null && !f.Duplicada && !f.Rectificativa && f.ImporteTotal >= 0m
                                          && f.Fecha.Date >= desde && f.Fecha.Date <= hoy);
        var filas = new List<FacturaRecurrentePendiente>();
        foreach (var grupo in validas.GroupBy(f => f.IdProveedor!.Value))
        {
            if (!proveedores.TryGetValue(grupo.Key, out var proveedor)) continue;
            if (proveedor.Periodicidad == PeriodicidadFacturas.NoControlar) continue;
            var fijada = proveedor.Periodicidad is not null;
            var periodicidad = proveedor.Periodicidad ?? Deducir(grupo.Select(f => f.Fecha));
            if (periodicidad is null) continue;
            var ultima = grupo.OrderByDescending(f => f.Fecha.Date).ThenByDescending(f => f.IdRegistro).First();
            var meses = (int)periodicidad.Value;
            for (var k = 1; ; k++)
            {
                var esperada = ultima.Fecha.Date.AddMonths(k * meses);
                if (hoy <= esperada) break;
                var periodo = Periodo(esperada);
                var descartada = descartados.Contains((grupo.Key, periodo));
                if (descartada && !mostrarDescartadas) continue;
                filas.Add(new FacturaRecurrentePendiente(grupo.Key, proveedor.IdTercero, proveedor.Nombre, periodicidad.Value, fijada, periodo, esperada,
                    ultima.ImporteTotal, ultima.Fecha.Date, ultima.IdRegistro, descartada));
            }
        }
        return filas.OrderBy(f => f.Proveedor, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.IdProveedor).ThenBy(f => f.Periodo).ToList();
    }

    /// <summary>Texto de la periodicidad en la pantalla y en Excel.</summary>
    public static string Texto(PeriodicidadFacturas p) => p switch
    {
        PeriodicidadFacturas.Mensual => "Mensual",
        PeriodicidadFacturas.Bimestral => "Bimestral",
        PeriodicidadFacturas.Trimestral => "Trimestral",
        _ => "No controlar",
    };
}
