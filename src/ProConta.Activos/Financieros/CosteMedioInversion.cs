namespace ProConta.Activos.Financieros;

/// <summary>Una operación de un activo financiero para calcular su coste: tipo (1 compra, 2 venta), fecha, participaciones e importe en euros.</summary>
public sealed record MovimientoCoste(int IdOperacion, int Tipo, DateTime Fecha, decimal Participaciones, decimal ImporteTotalEur);

/// <summary>Participaciones y coste que quedan, y el coste de lo vendido en cada venta.</summary>
public sealed record CosteInversion(decimal Participaciones, decimal Coste, IReadOnlyDictionary<int, decimal> CosteVendido)
{
    /// <summary>Si en algún momento se vendieron más participaciones de las que había.</summary>
    public bool Descubierto { get; init; }
}

/// <summary>
/// Coste medio ponderado de un activo financiero (activos 2026-11-11 18:21, Q5 a): las compras suman participaciones y coste; cada venta se lleva el
/// coste medio de las participaciones que vende, a céntimos, y si las vende todas, el coste que quedaba entero (sin restos de redondeo). Las operaciones
/// van por fecha y, el mismo día, las compras antes que las ventas.
/// </summary>
public static class CosteMedioInversion
{
    public static CosteInversion Calcular(IEnumerable<MovimientoCoste> operaciones)
    {
        ArgumentNullException.ThrowIfNull(operaciones);
        decimal participaciones = 0m, coste = 0m;
        var descubierto = false;
        var vendido = new Dictionary<int, decimal>();
        foreach (var o in operaciones.OrderBy(o => o.Fecha.Date).ThenBy(o => o.Tipo).ThenBy(o => o.IdOperacion))
        {
            if (o.Tipo == OperacionDeInversion.TipoCompra)
            {
                participaciones += o.Participaciones;
                coste += o.ImporteTotalEur;
                continue;
            }
            if (o.Tipo != OperacionDeInversion.TipoVenta) continue;
            if (o.Participaciones > participaciones) descubierto = true;
            var c = o.Participaciones >= participaciones ? coste
                : participaciones == 0m ? 0m
                : Math.Round(coste * o.Participaciones / participaciones, 2, MidpointRounding.AwayFromZero);
            vendido[o.IdOperacion] = c;
            coste -= c;
            participaciones -= o.Participaciones;
        }
        return new CosteInversion(participaciones, coste, vendido) { Descubierto = descubierto };
    }
}
