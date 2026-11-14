using System.Globalization;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Ventas;

/// <summary>Piso (activo) de una venta: nombre y centro de coste que hereda la línea.</summary>
public sealed record ActivoVenta(int IdActivo, string Nombre, int? IdCentroCoste);

/// <summary>Clave de una factura de ventas: cliente, mes de la fecha de referencia y piso (facturacion/reservas 2026-11-14 11:45).</summary>
public sealed record ClaveFacturaVentas(int IdCliente, int Anio, int Mes, int? IdActivo)
{
    public DateTime PrimerDia => new(Anio, Mes, 1);
    public DateTime UltimoDia => PrimerDia.AddMonths(1).AddDays(-1);
}

/// <summary>Grupo de ventas que van en una misma factura, o el motivo por el que no se puede facturar.</summary>
public sealed record GrupoVentas(ClaveFacturaVentas? Clave, string Cliente, string Piso, IReadOnlyList<VentaPorFacturar> Ventas, string? Problema)
{
    public decimal Total => Ventas.Sum(v => v.Importe);
    public bool Facturable => Problema is null && Clave is not null;
}

/// <summary>
/// Reglas puras de «Facturar ventas…»: agrupa las ventas pendientes por cliente efectivo (el propio de la venta o el de su
/// origen), mes de la fecha de referencia y piso, y construye la línea de cada venta conservando su importe exacto.
/// </summary>
public static class AgrupadorVentas
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Cliente al que se factura la venta: el suyo o, si no tiene, el de su origen.</summary>
    public static int? ClienteEfectivo(VentaPorFacturar v, IReadOnlyDictionary<int, OrigenVenta> origenes) =>
        v.IdCliente ?? (origenes.TryGetValue(v.IdOrigenVenta, out var o) ? o.IdCliente : null);

    public static IReadOnlyList<GrupoVentas> Agrupar(IEnumerable<VentaPorFacturar> ventas, IReadOnlyDictionary<int, OrigenVenta> origenes,
        IReadOnlyDictionary<int, string> clientes, IReadOnlyDictionary<int, ActivoVenta> activos, IReadOnlyDictionary<int, Concepto> conceptos)
    {
        var r = new List<GrupoVentas>();
        var facturables = new List<(VentaPorFacturar Venta, ClaveFacturaVentas Clave)>();
        foreach (var v in ventas.Where(v => v.Estado == EstadoVenta.Pendiente))
        {
            var problema = Problema(v, origenes, activos, conceptos, out var idCliente);
            if (problema is not null)
            {
                r.Add(new GrupoVentas(null, idCliente is int c && clientes.TryGetValue(c, out var nc) ? nc : "Sin cliente",
                    v.IdActivo is int a && activos.TryGetValue(a, out var act) ? act.Nombre : "Sin piso", [v], problema));
                continue;
            }
            facturables.Add((v, new ClaveFacturaVentas(idCliente!.Value, v.FechaReferencia.Year, v.FechaReferencia.Month, v.IdActivo)));
        }
        foreach (var g in facturables.GroupBy(x => x.Clave).OrderBy(g => g.Key.Anio).ThenBy(g => g.Key.Mes).ThenBy(g => g.Key.IdCliente).ThenBy(g => g.Key.IdActivo))
            r.Add(new GrupoVentas(g.Key, clientes.TryGetValue(g.Key.IdCliente, out var n) ? n : $"Cliente {g.Key.IdCliente}",
                g.Key.IdActivo is int a && activos.TryGetValue(a, out var act) ? act.Nombre : "Sin piso",
                g.Select(x => x.Venta).OrderBy(v => v.FechaReferencia).ThenBy(v => v.IdVentaPorFacturar).ToList(), null));
        return r;
    }

    private static string? Problema(VentaPorFacturar v, IReadOnlyDictionary<int, OrigenVenta> origenes, IReadOnlyDictionary<int, ActivoVenta> activos,
        IReadOnlyDictionary<int, Concepto> conceptos, out int? idCliente)
    {
        idCliente = ClienteEfectivo(v, origenes);
        var origen = origenes.TryGetValue(v.IdOrigenVenta, out var o) ? o.Nombre : $"origen {v.IdOrigenVenta}";
        if (idCliente is null)
            return $"No tiene cliente y su origen ({origen}) tampoco: ponle el cliente a la venta o al origen en Parámetros y tablas › Orígenes de venta.";
        if (v.IdActivo is int a && !activos.ContainsKey(a))
            return "Su piso ya no existe en Activos: elige otro en la venta.";
        if (v.IdConcepto is not int c || !conceptos.TryGetValue(c, out var concepto))
            return v.IdConcepto is null
                ? "No tiene concepto: elige uno en la venta (o en el alojamiento de Avaibook) para saber la cuenta de venta."
                : "Su concepto ya no existe: elige otro en la venta.";
        if (string.IsNullOrWhiteSpace(concepto.CuentaVenta))
            return $"El concepto «{concepto.Nombre}» no tiene cuenta de venta: pónsela en Parámetros y tablas › Conceptos.";
        if (!concepto.CuentaEnEjercicio)
            return $"La cuenta {concepto.CuentaVenta} del concepto «{concepto.Nombre}» no existe en el ejercicio de la factura: créala o cambia la cuenta del concepto.";
        if (v.Importe == 0m)
            return "Tiene importe 0,00 €: corrígelo o anúlala.";
        return null;
    }

    /// <summary>Descripción de la factura: origen o cliente, mes y piso («Reservas Airbnb · enero 2026 · Cádiz 3»).</summary>
    public static string DescripcionFactura(GrupoVentas g, IReadOnlyDictionary<int, OrigenVenta> origenes)
    {
        var nombresOrigen = g.Ventas.Select(v => origenes.TryGetValue(v.IdOrigenVenta, out var o) ? o.Nombre : null).Where(n => n is not null).Distinct().ToList();
        var quien = nombresOrigen.Count == 1 ? nombresOrigen[0] : "Ventas";
        var mes = g.Clave!.PrimerDia.ToString("MMMM yyyy", Es);
        return g.Clave.IdActivo is null ? $"{quien} · {mes}" : $"{quien} · {mes} · {g.Piso}";
    }

    /// <summary>
    /// Línea de la factura para una venta: el concepto da nombre, cuenta e IVA (salvo que la venta lleve el suyo); el piso, el
    /// centro de coste. La cantidad son las noches y el total se fija con el importe de la venta, así que la factura suma
    /// exactamente lo que pagaron los huéspedes (el precio es base ÷ cantidad con 4 decimales).
    /// </summary>
    public static LineaFacturaVenta Linea(VentaPorFacturar v, Concepto concepto, ActivoVenta? activo, OrigenVenta? origen)
    {
        var l = new LineaFacturaVenta
        {
            IdConcepto = concepto.IdConcepto,
            Nombre = concepto.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(v.Descripcion) ? DescripcionLinea(v, origen) : v.Descripcion.Trim(),
            Fecha = v.FechaReferencia.Date,
            Cantidad = v.Cantidad > 0m ? v.Cantidad : 1m,
            TipoIva = v.TipoIva ?? concepto.TipoIva ?? 0m,
            CausaExencion = string.IsNullOrWhiteSpace(v.CausaExencion) ? null : v.CausaExencion.Trim(),
            CuentaContable = concepto.CuentaVenta,
            IdDepartamento = concepto.IdDepartamento,
            IdCentroCoste = activo?.IdCentroCoste ?? concepto.IdCentroCoste,
        };
        if (l.CausaExencion is not null) l.TipoIva = 0m;
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
