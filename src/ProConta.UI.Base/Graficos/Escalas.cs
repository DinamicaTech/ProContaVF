using System.Globalization;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Escala lineal de un eje: lleva un valor del dominio [<see cref="Min"/>, <see cref="Max"/>] a una coordenada de pantalla
/// [<see cref="Desde"/>, <see cref="Hasta"/>]. En el eje vertical <see cref="Desde"/> es la base (abajo) y <see cref="Hasta"/> el
/// tope (arriba), así que la coordenada decrece al crecer el valor. Clase pura, sin WPF (informes/graficos).
/// </summary>
public sealed record EscalaLineal(double Min, double Max, double Desde, double Hasta)
{
    public double Mapear(double valor) =>
        Max == Min ? (Desde + Hasta) / 2 : Desde + (valor - Min) / (Max - Min) * (Hasta - Desde);
}

/// <summary>Rango redondeado de un eje de valores, con sus divisiones.</summary>
public sealed record RangoEje(double Min, double Max, IReadOnlyList<double> Divisiones);

/// <summary>Cálculo de los ejes: máximos «redondos» y divisiones, con soporte de valores negativos (informes/graficos).</summary>
public static class EscalasGrafico
{
    /// <summary>
    /// El menor número «redondo» (1; 1,5; 2; 2,5; 5 o 10 por una potencia de 10) mayor o igual que <paramref name="valor"/>,
    /// como en la maqueta. Cero o negativo devuelve 1.
    /// </summary>
    public static double MaximoRedondo(double valor)
    {
        if (!(valor > 0) || double.IsInfinity(valor)) return 1;
        var potencia = Math.Pow(10, Math.Floor(Math.Log10(valor)));
        var m = valor / potencia;
        // Tolerancia de redondeo binario: 1,5000000001 cuenta como 1,5.
        const double e = 1e-9;
        var paso = m <= 1 + e ? 1 : m <= 1.5 + e ? 1.5 : m <= 2 + e ? 2 : m <= 2.5 + e ? 2.5 : m <= 5 + e ? 5 : 10;
        return paso * potencia;
    }

    /// <summary>
    /// Rango del eje para unos valores (los nulos son huecos y no cuentan): de 0 (o del mínimo redondeado si hay negativos) al
    /// máximo redondeado con un margen de <paramref name="margen"/>, partido en <paramref name="divisiones"/> tramos iguales.
    /// Sin valores, de 0 a 1.
    /// </summary>
    public static RangoEje Rango(IEnumerable<double?> valores, int divisiones = 4, double margen = 0.05, double? minFijo = null, double? maxFijo = null)
    {
        var lista = valores.Where(v => v is not null && !double.IsNaN(v.Value)).Select(v => v!.Value).ToList();
        var alto = lista.Count == 0 ? 0 : Math.Max(0, lista.Max());
        var bajo = lista.Count == 0 ? 0 : Math.Min(0, lista.Min());
        double max, min;
        if (bajo < 0 && alto <= 0)
        {
            max = 0;
            min = -MaximoRedondo(-bajo * (1 + margen));
        }
        else if (bajo < 0)
        {
            // Con positivos y negativos, un paso común para que el cero caiga en una división.
            var paso = MaximoRedondo(Math.Max(alto, -bajo) * (1 + margen) / Math.Max(1, divisiones / 2));
            max = Math.Ceiling(alto * (1 + margen) / paso) * paso;
            min = -Math.Ceiling(-bajo * (1 + margen) / paso) * paso;
            if (max == 0) max = paso;
            divisiones = (int)Math.Round((max - min) / paso);
        }
        else
        {
            min = 0;
            max = alto == 0 ? 1 : MaximoRedondo(alto * (1 + margen));
        }
        if (minFijo is double mn) min = mn;
        if (maxFijo is double mx) max = mx;
        if (max <= min) max = min + 1;
        divisiones = Math.Clamp(divisiones, 1, 12);
        var marcas = Enumerable.Range(0, divisiones + 1).Select(i => min + (max - min) * i / divisiones).ToList();
        return new RangoEje(min, max, marcas);
    }
}

/// <summary>Textos compactos de los ejes y los rótulos de los gráficos, en español (miles con «k», millones con «M»).</summary>
public static class FormatoGrafico
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>120000 → «120k», 1250000 → «1,25M», 950 → «950», −30000 → «−30k».</summary>
    public static string Compacto(double valor)
    {
        var a = Math.Abs(valor);
        var signo = valor < 0 ? "−" : "";
        if (a >= 1_000_000) return signo + (a / 1_000_000).ToString("0.##", Es) + "M";
        if (a >= 1_000) return signo + (a / 1_000).ToString(a >= 100_000 ? "0" : "0.#", Es) + "k";
        return signo + a.ToString("0", Es);
    }

    /// <summary>Importe en miles con un decimal, como los tooltips de la maqueta: 71300 → «71,3 k€».</summary>
    public static string Miles(decimal valor) => (valor / 1000m).ToString("#,##0.0", Es) + " k€";

    /// <summary>Variación porcentual de <paramref name="actual"/> sobre <paramref name="referencia"/>; null si la referencia es cero.</summary>
    public static decimal? Variacion(decimal actual, decimal referencia) =>
        referencia == 0 ? null : (actual - referencia) / Math.Abs(referencia) * 100m;

    /// <summary>«▲ 7,9 %» o «▼ 1,1 %».</summary>
    public static string TextoVariacion(decimal variacion) =>
        (variacion >= 0 ? "▲ " : "▼ ") + Math.Abs(variacion).ToString("0.0", Es) + " %";
}
