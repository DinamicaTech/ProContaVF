using System.Globalization;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Lineas;

namespace ProConta.Norma43.Remesas;

/// <summary>Factura de venta que puede ir en una remesa de recibos, tal como la leen las propuestas.</summary>
/// <param name="DiaDePago">Día del mes en que se carga el recibo (Facturas.DiaDePago); null si la factura no lo tiene.</param>
/// <param name="Sepa">Su forma de pago es de domiciliación SEPA (FormasPago.SEPA).</param>
public sealed record FacturaRemesa(int Id, string Numero, DateTime Fecha, decimal Importe, int? DiaDePago, bool Sepa, int? IdCliente, string? Tercero, string? Descripcion);

/// <summary>Factura propuesta para una remesa: con el importe que se cobra (el de la factura, salvo que el operador lo cambie) y si viene marcada.</summary>
public sealed record CandidataRemesa(FacturaRemesa Factura, bool Marcada);

/// <summary>Recibo ya cobrado que puede haberse devuelto: la línea del recibo, su importe cobrado y la factura.</summary>
/// <param name="IdLineaRecibo">Línea N43 con que se cobró (la de la remesa explotada o el propio movimiento).</param>
public sealed record ReciboCobrado(int IdLineaRecibo, DateTime Fecha, decimal Importe, FacturaRemesa Factura);

/// <summary>Recibo devuelto propuesto, y si viene marcado.</summary>
public sealed record CandidataDevolucion(ReciboCobrado Recibo, bool Marcada);

/// <summary>Lo que el operador deja en el diálogo de una remesa: la factura y el importe que cobró el banco por ella.</summary>
public sealed record ReciboRemesa(int IdFactura, decimal Importe);

/// <summary>
/// Reglas de la explosión de remesas y devoluciones (norma43/remesas 2026-11-16), sin BD para que las pruebas las fijen. El banco
/// cobra una remesa al mes con los recibos de las facturas domiciliadas: la propuesta son las facturas SEPA del mes de la línea
/// pendientes de cobro, marcadas las de día de cargo a ±<see cref="DiasCargo"/> días. Solo se graba si la suma cuadra al céntimo.
/// </summary>
public static class ReglasRemesasN43
{
    /// <summary>Días como mucho entre el día de cargo de la factura y el del movimiento para venir marcada (como el ERP).</summary>
    public const int DiasCargo = 3;

    /// <summary>Recibos como mucho que se combinan para buscar los que suman una devolución.</summary>
    public const int MaximoCombinacion = 40;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>
    /// Facturas propuestas para la remesa, por número: las SEPA del mes de la línea, marcadas las que se cargan a ±<see cref="DiasCargo"/>
    /// días de la fecha del banco. Si las marcadas no cuadran y todas juntas sí, vienen todas marcadas (una remesa al mes las lleva todas).
    /// </summary>
    public static IReadOnlyList<CandidataRemesa> ProponerRemesa(LineaN43 linea, IEnumerable<FacturaRemesa> facturas)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ArgumentNullException.ThrowIfNull(facturas);
        var dia = linea.FechaOperacion.Day;
        var delMes = facturas.Where(f => f.Sepa && f.Fecha.Year == linea.FechaOperacion.Year && f.Fecha.Month == linea.FechaOperacion.Month)
            .GroupBy(f => f.Id).Select(g => g.First()).OrderBy(f => f.Numero, StringComparer.Ordinal).ThenBy(f => f.Id).ToList();
        var cerca = delMes.Where(f => f.DiaDePago is int d && Math.Abs(d - dia) <= DiasCargo).Select(f => f.Id).ToHashSet();
        if (delMes.Where(f => cerca.Contains(f.Id)).Sum(f => f.Importe) != linea.Importe && delMes.Sum(f => f.Importe) == linea.Importe)
            cerca = delMes.Select(f => f.Id).ToHashSet();
        return delMes.Select(f => new CandidataRemesa(f, cerca.Contains(f.Id))).ToList();
    }

    /// <summary>
    /// Recibos propuestos para una devolución: los de la remesa elegida, marcada la combinación (la de menos recibos) cuya suma es la del
    /// movimiento; si no hay ninguna, nada marcado.
    /// </summary>
    public static IReadOnlyList<CandidataDevolucion> ProponerDevolucion(decimal importe, IReadOnlyList<ReciboCobrado> recibos)
    {
        ArgumentNullException.ThrowIfNull(recibos);
        var marcados = Combinacion(importe, recibos.Select(r => r.Importe).ToList());
        return recibos.Select((r, i) => new CandidataDevolucion(r, marcados.Contains(i))).ToList();
    }

    /// <summary>
    /// Índices de la combinación con menos elementos cuya suma es exactamente <paramref name="objetivo"/> (en céntimos); vacía si no hay
    /// ninguna o hay más de <see cref="MaximoCombinacion"/> importes. Programación dinámica sobre los céntimos.
    /// </summary>
    public static IReadOnlySet<int> Combinacion(decimal objetivo, IReadOnlyList<decimal> importes)
    {
        ArgumentNullException.ThrowIfNull(importes);
        var meta = Centimos(objetivo);
        if (meta <= 0 || importes.Count == 0 || importes.Count > MaximoCombinacion || importes.Any(i => i <= 0)) return new HashSet<int>();
        var valores = importes.Select(Centimos).ToArray();
        if (valores.Sum(v => (long)v) < meta) return new HashSet<int>();
        // mascara[s] = los índices (bits) de la combinación con menos elementos que suma s; 0 = ninguna (salvo s = 0).
        var mascara = new ulong[meta + 1];
        var alcanzable = new bool[meta + 1];
        alcanzable[0] = true;
        for (var i = 0; i < valores.Length; i++)
        {
            var v = valores[i];
            var bit = 1UL << i;
            for (var s = meta; s >= v; s--) // de mayor a menor: cada importe se usa una vez
            {
                if (!alcanzable[s - v]) continue;
                var nueva = mascara[s - v] | bit;
                if (!alcanzable[s] || System.Numerics.BitOperations.PopCount(nueva) < System.Numerics.BitOperations.PopCount(mascara[s]))
                {
                    alcanzable[s] = true;
                    mascara[s] = nueva;
                }
            }
        }
        var r = new HashSet<int>();
        if (!alcanzable[meta]) return r;
        for (var i = 0; i < valores.Length; i++)
            if ((mascara[meta] & (1UL << i)) != 0) r.Add(i);
        return r;
    }

    private static int Centimos(decimal importe) => (int)Math.Round(importe * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>Diferencia entre lo marcado y el movimiento: cero si cuadra.</summary>
    public static decimal Diferencia(decimal movimiento, IEnumerable<decimal> importes) => importes.Sum() - movimiento;

    /// <summary>Texto del cuadre para el diálogo: lo marcado, el movimiento y la diferencia.</summary>
    public static string TextoCuadre(decimal movimiento, IReadOnlyCollection<decimal> importes)
    {
        ArgumentNullException.ThrowIfNull(importes);
        var suma = importes.Sum();
        var dif = suma - movimiento;
        return $"{importes.Count} recibo(s) por {suma.ToString("N2", Es)} € frente a {movimiento.ToString("N2", Es)} € del banco: " +
            (dif == 0m ? "cuadra." : $"diferencia de {dif.ToString("N2", Es)} €.");
    }

    /// <summary>Concepto de la línea de un recibo, como las del ERP: «Recibo SEPA» y la descripción de la factura.</summary>
    public static string ConceptoRecibo(FacturaRemesa f, bool devolucion)
    {
        ArgumentNullException.ThrowIfNull(f);
        var texto = (devolucion ? "Recibo SEPA devuelto " : "Recibo SEPA ") + (string.IsNullOrWhiteSpace(f.Descripcion) ? $"factura {f.Numero}" : f.Descripcion.Trim());
        return texto.Length <= 255 ? texto : texto[..255];
    }

    /// <summary>Si la línea es un abono de remesa (o un cargo de devolución) por la palabra clave que le puso la cuenta.</summary>
    public static bool EsDeEspecial(LineaN43 l, int especial) => l.Especial == especial;

    /// <summary>Sentido de los recibos de una remesa (abono) o de una devolución (cargo).</summary>
    public static string Sentido(bool devolucion) => devolucion ? ServicioConciliacionN43.Cargo : ServicioConciliacionN43.Abono;
}
