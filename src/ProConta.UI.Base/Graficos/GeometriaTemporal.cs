using System.Globalization;

namespace ProConta.UI.Base.Graficos;

/// <summary>Geometría pura del gráfico temporal (para probarla sin WPF).</summary>
public static class GeometriaTemporal
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Área de trazado; con marcas de compra y venta deja sitio debajo para los triángulos.</summary>
    public static Caja AreaDe(double ancho, double alto, Margenes m, bool conMarcas) =>
        GeometriaGrafico.Area(ancho, alto, conMarcas ? m with { Abajo = m.Abajo + 10 } : m);

    /// <summary>Posición horizontal de una fecha, proporcional al tiempo entre la primera y la última.</summary>
    public static double X(Caja area, DateTime desde, DateTime hasta, DateTime fecha)
    {
        var total = (hasta - desde).TotalDays;
        return total <= 0 ? area.X + area.Ancho / 2 : area.X + (fecha - desde).TotalDays / total * area.Ancho;
    }

    /// <summary>Índice de la fecha más cercana a una coordenada x (las fechas, ordenadas); −1 fuera del área.</summary>
    public static int IndiceMasCercano(Caja area, IReadOnlyList<DateTime> fechas, double x)
    {
        if (fechas.Count == 0 || x < area.X - 4 || x > area.Derecha + 4) return -1;
        var total = (fechas[^1] - fechas[0]).TotalDays;
        var objetivo = fechas[0].AddDays(total * Math.Clamp((x - area.X) / Math.Max(1, area.Ancho), 0, 1));
        int lo = 0, hi = fechas.Count - 1;
        while (hi - lo > 1)
        {
            var m = (lo + hi) / 2;
            if (fechas[m] <= objetivo) lo = m; else hi = m;
        }
        return (objetivo - fechas[lo]).Duration() <= (fechas[hi] - objetivo).Duration() ? lo : hi;
    }

    /// <summary>
    /// Rango del eje vertical con divisiones «redondas»: con <paramref name="incluirCero"/>, desde el cero; si no, ajustado a los valores (precios). Sin
    /// valores, de 0 a 1.
    /// </summary>
    public static RangoEje RangoValores(IEnumerable<double?> valores, bool incluirCero, int divisiones = 5)
    {
        var l = valores.Where(v => v is double x && !double.IsNaN(x) && !double.IsInfinity(x)).Select(v => v!.Value).ToList();
        if (l.Count == 0) return new RangoEje(0, 1, [0, 0.25, 0.5, 0.75, 1]);
        double min = l.Min(), max = l.Max();
        if (incluirCero) { min = Math.Min(0, min); max = Math.Max(0, max); }
        if (max - min < 1e-12) { var h = Math.Abs(max) * 0.05 + 1e-6; min -= h; max += h; }
        var paso = EscalasGrafico.MaximoRedondo((max - min) / Math.Max(1, divisiones));
        var desde = Math.Floor(min / paso + 1e-9) * paso;
        var hasta = Math.Ceiling(max / paso - 1e-9) * paso;
        if (hasta <= desde) hasta = desde + paso;
        var n = (int)Math.Round((hasta - desde) / paso);
        // Sin «−0»: los ceros de coma flotante se dejan en cero exacto.
        return new RangoEje(desde, hasta, Enumerable.Range(0, n + 1).Select(i => Math.Abs(desde + i * paso) < paso * 1e-9 ? 0 : desde + i * paso).ToList());
    }

    /// <summary>
    /// Rótulos del eje de fechas, como mucho unos <paramref name="maximo"/>: años («2024») si el periodo es largo; si no, meses («ene 26») cada 1, 2, 3 o 6
    /// meses; y en periodos de semanas, días («17 nov»).
    /// </summary>
    public static IReadOnlyList<(DateTime Fecha, string Texto)> MarcasFechas(DateTime desde, DateTime hasta, int maximo)
    {
        var r = new List<(DateTime, string)>();
        maximo = Math.Max(2, maximo);
        var dias = (hasta - desde).TotalDays;
        if (dias <= 0) return r;
        var meses = dias / 30.44;
        if (meses / maximo > 6)
        {
            var cada = Math.Max(1, (int)Math.Ceiling(dias / 365.25 / maximo));
            for (var a = desde.Year + 1; a <= hasta.Year; a++)
                if ((a - desde.Year - 1) % cada == 0) r.Add((new DateTime(a, 1, 1), a.ToString(CultureInfo.InvariantCulture)));
            return r;
        }
        if (meses >= 2)
        {
            var cada = new[] { 1, 2, 3, 6 }.FirstOrDefault(c => meses / c <= maximo, 6);
            for (var f = new DateTime(desde.Year, desde.Month, 1).AddMonths(1); f <= hasta; f = f.AddMonths(1))
                if ((f.Month - 1) % cada == 0) r.Add((f, f.ToString("MMM yy", Es).Replace(".", "")));
            return r;
        }
        var paso = Math.Max(1, (int)Math.Ceiling(dias / maximo));
        for (var f = desde.Date.AddDays(paso); f <= hasta; f = f.AddDays(paso)) r.Add((f, f.ToString("d MMM", Es).Replace(".", "")));
        return r;
    }

    /// <summary>
    /// Tramos de una serie partidos por estilo: cada tramo son índices consecutivos con valor, y es «sutil» cuando todos sus segmentos unen dos
    /// fechas sutiles. Dos tramos seguidos comparten el índice del cambio, para que la línea no se corte. Un nulo corta la línea.
    /// </summary>
    public static IReadOnlyList<(IReadOnlyList<int> Indices, bool Sutil)> TramosConEstilo(IReadOnlyList<double?> valores, IReadOnlyList<bool>? sutil)
    {
        var r = new List<(IReadOnlyList<int>, bool)>();
        bool EsSutil(int i) => sutil is not null && i < sutil.Count && sutil[i];
        foreach (var tramo in GeometriaGrafico.Tramos(valores))
        {
            if (tramo.Count == 1) { r.Add((tramo, EsSutil(tramo[0]))); continue; }
            var actual = new List<int> { tramo[0] };
            bool? estilo = null;
            for (var k = 1; k < tramo.Count; k++)
            {
                var s = EsSutil(tramo[k - 1]) && EsSutil(tramo[k]);
                if (estilo is bool e && e != s)
                {
                    r.Add((actual, e));
                    actual = [tramo[k - 1]];
                }
                estilo = s;
                actual.Add(tramo[k]);
            }
            r.Add((actual, estilo ?? false));
        }
        return r;
    }

    /// <summary>Índice de la fecha (en días OLE, ordenadas) más cercana a <paramref name="x"/>; −1 si no hay fechas.</summary>
    public static int IndiceMasCercano(IReadOnlyList<double> xs, double x)
    {
        if (xs.Count == 0 || double.IsNaN(x)) return -1;
        int lo = 0, hi = xs.Count - 1;
        if (x <= xs[0]) return 0;
        if (x >= xs[hi]) return hi;
        while (hi - lo > 1)
        {
            var m = (lo + hi) / 2;
            if (xs[m] <= x) lo = m; else hi = m;
        }
        return x - xs[lo] <= xs[hi] - x ? lo : hi;
    }

    /// <summary>
    /// Polígonos del sombreado entre la serie A y la serie B: los tramos donde A ≥ B (positivos) y donde A &lt; B (negativos), cortando en el punto donde
    /// se cruzan. Un hueco en cualquiera de las dos corta el sombreado.
    /// </summary>
    public static (IReadOnlyList<IReadOnlyList<Punto>> Positivos, IReadOnlyList<IReadOnlyList<Punto>> Negativos) Relleno(
        IReadOnlyList<double> xs, IReadOnlyList<double?> a, IReadOnlyList<double?> b, Func<double, double> y)
    {
        var pos = new List<IReadOnlyList<Punto>>();
        var neg = new List<IReadOnlyList<Punto>>();
        var n = Math.Min(xs.Count, Math.Min(a.Count, b.Count));
        List<(double X, double A, double B)> tramo = [];
        bool? signo = null;

        void Cerrar()
        {
            if (tramo.Count >= 2 && signo is bool s && tramo.Any(p => p.A != p.B))
            {
                var poli = tramo.Select(p => new Punto(p.X, y(p.A))).Concat(tramo.AsEnumerable().Reverse().Select(p => new Punto(p.X, y(p.B)))).ToList();
                (s ? pos : neg).Add(poli);
            }
            tramo = [];
            signo = null;
        }

        for (var i = 0; i < n; i++)
        {
            if (a[i] is not double va || b[i] is not double vb) { Cerrar(); continue; }
            var s = va >= vb;
            if (signo is bool previo && previo != s && tramo.Count > 0)
            {
                // Punto de cruce por interpolación lineal entre el punto anterior y este.
                var (x0, a0, b0) = tramo[^1];
                var d0 = a0 - b0;
                var d1 = va - vb;
                var t = d0 == d1 ? 0 : d0 / (d0 - d1);
                var xc = x0 + (xs[i] - x0) * t;
                var vc = a0 + (va - a0) * t;
                tramo.Add((xc, vc, vc));
                Cerrar();
                tramo.Add((xc, vc, vc));
            }
            signo = s;
            tramo.Add((xs[i], va, vb));
        }
        Cerrar();
        return (pos, neg);
    }
}
