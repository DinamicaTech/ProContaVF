using System.Globalization;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Ventas;

/// <summary>Activo relacionado de una venta (el piso de una reserva): nombre y centro de coste que hereda la línea.</summary>
public sealed record ActivoVenta(int IdActivo, string Nombre, int? IdCentroCoste);

/// <summary>Clave de una factura de ventas: cliente, mes de la fecha de referencia y activo (facturacion/reservas 2026-11-14 11:45).</summary>
public sealed record ClaveFacturaVentas(int IdCliente, int Anio, int Mes, int? IdActivo)
{
    public DateTime PrimerDia => new(Anio, Mes, 1);
    public DateTime UltimoDia => PrimerDia.AddMonths(1).AddDays(-1);
}

/// <summary>Grupo de ventas que van en una misma factura, o el motivo por el que no se puede facturar.</summary>
public sealed record GrupoVentas(ClaveFacturaVentas? Clave, string Cliente, string Activo, IReadOnlyList<VentaPorFacturar> Ventas, string? Problema)
{
    public decimal Total => Ventas.Sum(v => v.Importe);
    public bool Facturable => Problema is null && Clave is not null;
}

/// <summary>
/// Lo que se factura de una venta una vez aplicada su regla (facturacion/reservas 2026-11-14 14:20): cada dato es el propio
/// de la venta o, si no lo lleva, el de la regla de su origen y activo; el IVA, si tampoco lo da la regla, el del concepto.
/// </summary>
public sealed record VentaResuelta(int? IdCliente, int? IdConcepto, string? CausaExencion, decimal? TipoIva, ReglaVenta? Regla)
{
    /// <summary>«Exenta E1», «10 %» o «El del concepto».</summary>
    public string TextoIva => CausaExencion is not null ? $"Exenta {CausaExencion}" : TipoIva is decimal t ? $"{t:0.##} %" : "El del concepto";
}

/// <summary>Catálogos con los que se resuelven y agrupan las ventas.</summary>
public sealed record DatosVentas(IReadOnlyDictionary<int, OrigenVenta> Origenes, IReadOnlyList<ReglaVenta> Reglas, IReadOnlyDictionary<int, string> Clientes,
    IReadOnlyDictionary<int, ActivoVenta> Activos, IReadOnlyDictionary<int, Concepto> Conceptos);

/// <summary>
/// Reglas puras de «Facturar ventas…»: resuelve cada venta con su regla, agrupa las pendientes por cliente, mes de la fecha
/// de referencia y activo, y construye la línea de cada venta conservando su importe exacto.
/// </summary>
public static class AgrupadorVentas
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Aplica a la venta la regla de su origen y activo. El IVA propio de la venta (causa o tipo) sustituye entero al de la regla.</summary>
    public static VentaResuelta Resolver(VentaPorFacturar v, IEnumerable<ReglaVenta> reglas)
    {
        var regla = ReglaVenta.Para(reglas, v.IdOrigenVenta, v.IdActivo);
        var ivaPropio = !string.IsNullOrWhiteSpace(v.CausaExencion) || v.TipoIva is not null;
        var causa = ivaPropio ? v.CausaExencion : regla?.CausaExencion;
        return new VentaResuelta(v.IdCliente ?? regla?.IdCliente, v.IdConcepto ?? regla?.IdConcepto, string.IsNullOrWhiteSpace(causa) ? null : causa.Trim(),
            ivaPropio ? v.TipoIva : regla?.TipoIva, regla);
    }

    public static IReadOnlyList<GrupoVentas> Agrupar(IEnumerable<VentaPorFacturar> ventas, DatosVentas d)
    {
        var r = new List<GrupoVentas>();
        var facturables = new List<(VentaPorFacturar Venta, ClaveFacturaVentas Clave)>();
        foreach (var v in ventas.Where(v => v.Estado == EstadoVenta.Pendiente))
        {
            var res = Resolver(v, d.Reglas);
            var problema = Problema(v, res, d);
            if (problema is not null)
            {
                r.Add(new GrupoVentas(null, res.IdCliente is int c && d.Clientes.TryGetValue(c, out var nc) ? nc : "Sin cliente",
                    v.IdActivo is int a && d.Activos.TryGetValue(a, out var act) ? act.Nombre : "Sin activo", [v], problema));
                continue;
            }
            facturables.Add((v, new ClaveFacturaVentas(res.IdCliente!.Value, v.FechaReferencia.Year, v.FechaReferencia.Month, v.IdActivo)));
        }
        foreach (var g in facturables.GroupBy(x => x.Clave).OrderBy(g => g.Key.Anio).ThenBy(g => g.Key.Mes).ThenBy(g => g.Key.IdCliente).ThenBy(g => g.Key.IdActivo))
            r.Add(new GrupoVentas(g.Key, d.Clientes.TryGetValue(g.Key.IdCliente, out var n) ? n : $"Cliente {g.Key.IdCliente}",
                g.Key.IdActivo is int a && d.Activos.TryGetValue(a, out var act) ? act.Nombre : "Sin activo",
                g.Select(x => x.Venta).OrderBy(v => v.FechaReferencia).ThenBy(v => v.IdVentaPorFacturar).ToList(), null));
        return r;
    }

    private static string? Problema(VentaPorFacturar v, VentaResuelta res, DatosVentas d)
    {
        var origen = d.Origenes.TryGetValue(v.IdOrigenVenta, out var o) ? o.Nombre : $"origen {v.IdOrigenVenta}";
        var activo = v.IdActivo is int ia && d.Activos.TryGetValue(ia, out var av) ? av.Nombre : null;
        var donde = activo is null ? $"el origen {origen}" : $"{origen} y {activo}";
        const string enReglas = "en Parámetros y tablas › Reglas de venta";
        if (v.IdActivo is int a && !d.Activos.ContainsKey(a))
            return "Su activo relacionado ya no existe en Activos: elige otro en la venta.";
        if (res.IdCliente is null)
            return $"No tiene cliente: ponlo en la regla de {donde} ({enReglas}) o elige uno en la venta.";
        if (res.IdConcepto is not int c)
            return $"No tiene concepto: ponlo en la regla de {donde} ({enReglas}) o elige uno en la venta.";
        if (!d.Conceptos.TryGetValue(c, out var concepto))
            return $"Su concepto ya no existe: cámbialo en la regla de {donde} ({enReglas}) o en la venta.";
        if (string.IsNullOrWhiteSpace(concepto.CuentaVenta))
            return $"El concepto «{concepto.Nombre}» no tiene cuenta de venta: pónsela en Parámetros y tablas › Conceptos.";
        if (!concepto.CuentaEnEjercicio)
            return $"La cuenta {concepto.CuentaVenta} del concepto «{concepto.Nombre}» no existe en el ejercicio de la factura: créala o cambia la cuenta del concepto.";
        if (v.Importe == 0m)
            return "Tiene importe 0,00 €: corrígelo o anúlala.";
        return null;
    }

    /// <summary>Descripción de la factura: origen, mes y activo («Airbnb · enero 2026 · Cádiz 3»).</summary>
    public static string DescripcionFactura(GrupoVentas g, IReadOnlyDictionary<int, OrigenVenta> origenes)
    {
        var nombresOrigen = g.Ventas.Select(v => origenes.TryGetValue(v.IdOrigenVenta, out var o) ? o.Nombre : null).Where(n => n is not null).Distinct().ToList();
        var quien = nombresOrigen.Count == 1 ? nombresOrigen[0] : "Ventas";
        var mes = g.Clave!.PrimerDia.ToString("MMMM yyyy", Es);
        return g.Clave.IdActivo is null ? $"{quien} · {mes}" : $"{quien} · {mes} · {g.Activo}";
    }

    /// <summary>
    /// Línea de la factura para una venta: el concepto da nombre y cuenta; el IVA es el resuelto (venta, regla o concepto); el
    /// activo, el centro de coste. La cantidad son las noches y el total se fija con el importe de la venta (IVA incluido),
    /// así que la factura suma exactamente lo que pagaron los huéspedes (el precio es base ÷ cantidad con 4 decimales).
    /// </summary>
    public static LineaFacturaVenta Linea(VentaPorFacturar v, VentaResuelta res, Concepto concepto, ActivoVenta? activo, OrigenVenta? origen)
    {
        var l = new LineaFacturaVenta
        {
            IdConcepto = concepto.IdConcepto,
            Nombre = concepto.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(v.Descripcion) ? DescripcionLinea(v, origen) : v.Descripcion.Trim(),
            Fecha = v.FechaReferencia.Date,
            Cantidad = v.Cantidad > 0m ? v.Cantidad : 1m,
            TipoIva = res.CausaExencion is not null ? 0m : res.TipoIva ?? concepto.TipoIva ?? 0m,
            CausaExencion = res.CausaExencion,
            CuentaContable = concepto.CuentaVenta,
            IdDepartamento = concepto.IdDepartamento,
            IdCentroCoste = activo?.IdCentroCoste ?? concepto.IdCentroCoste,
        };
        l.FijarTotal(v.Importe);
        return l;
    }

    /// <summary>«Airbnb · 02/01/2026 – 13/01/2026 · 11 noches · reserva HM4JR5TNCD» (sin el nombre del huésped, que es dato personal).</summary>
    public static string DescripcionLinea(VentaPorFacturar v, OrigenVenta? origen)
    {
        var partes = new List<string>();
        if (origen is not null) partes.Add(origen.Nombre);
        partes.Add(v.FechaFin is DateTime fin && fin.Date > v.FechaReferencia.Date
            ? $"{v.FechaReferencia:dd/MM/yyyy} – {fin:dd/MM/yyyy}"
            : v.FechaReferencia.ToString("dd/MM/yyyy", Es));
        if (v.FechaFin is DateTime f2 && (f2.Date - v.FechaReferencia.Date).Days is var noches and > 0)
            partes.Add(noches == 1 ? "1 noche" : $"{noches} noches");
        if (!string.IsNullOrWhiteSpace(v.Referencia)) partes.Add($"reserva {v.Referencia.Trim()}");
        return string.Join(" · ", partes);
    }
}
