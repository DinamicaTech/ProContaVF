namespace ProConta.UI.Base.Graficos;

/// <summary>Rectángulo en coordenadas de pantalla (sin WPF, para poder probar la geometría).</summary>
public readonly record struct Caja(double X, double Y, double Ancho, double Alto)
{
    public double Derecha => X + Ancho;
    public double Abajo => Y + Alto;
    public bool Contiene(double x, double y) => x >= X && x < X + Ancho && y >= Y && y < Y + Alto;
}

/// <summary>Punto en coordenadas de pantalla.</summary>
public readonly record struct Punto(double X, double Y);

/// <summary>Márgenes del área de trazado dentro del control.</summary>
public readonly record struct Margenes(double Izquierda, double Arriba, double Derecha, double Abajo);

/// <summary>
/// Geometría de los gráficos, separada del dibujo WPF para poder probarla (informes/graficos): bandas de categorías, barras
/// agrupadas con su tramo apilado, tramos de una línea sin interpolar huecos y segmentos de una barra apilada.
/// </summary>
public static class GeometriaGrafico
{
    /// <summary>Área de trazado: el control menos los márgenes (nunca negativa).</summary>
    public static Caja Area(double ancho, double alto, Margenes m) =>
        new(m.Izquierda, m.Arriba, Math.Max(0, ancho - m.Izquierda - m.Derecha), Math.Max(0, alto - m.Arriba - m.Abajo));

    /// <summary>Banda horizontal de la categoría <paramref name="indice"/> de <paramref name="categorias"/> repartidas a partes iguales.</summary>
    public static Caja Banda(Caja area, int categorias, int indice)
    {
        var ancho = categorias <= 0 ? area.Ancho : area.Ancho / categorias;
        return new Caja(area.X + indice * ancho, area.Y, ancho, area.Alto);
    }

    /// <summary>Centro horizontal de cada punto de una línea: el centro de la banda de su categoría.</summary>
    public static double CentroCategoria(Caja area, int categorias, int indice)
    {
        var b = Banda(area, categorias, indice);
        return b.X + b.Ancho / 2;
    }

    /// <summary>Índice de la categoría bajo una coordenada x; −1 si queda fuera del área.</summary>
    public static int CategoriaEn(Caja area, int categorias, double x)
    {
        if (categorias <= 0 || x < area.X || x >= area.Derecha || area.Ancho <= 0) return -1;
        return Math.Clamp((int)((x - area.X) / (area.Ancho / categorias)), 0, categorias - 1);
    }

    /// <summary>
    /// Posición horizontal de cada barra de un grupo: el grupo ocupa el 72 % de la banda (máximo <paramref name="anchoMaximoGrupo"/>)
    /// centrado, y sus <paramref name="series"/> barras se separan <paramref name="separacion"/> píxeles.
    /// </summary>
    public static IReadOnlyList<(double X, double Ancho)> BarrasDelGrupo(Caja banda, int series, double anchoMaximoGrupo = 46, double separacion = 2)
    {
        series = Math.Max(1, series);
        var grupo = Math.Min(banda.Ancho * 0.72, anchoMaximoGrupo);
        var una = Math.Max(1, (grupo - separacion * (series - 1)) / series);
        var x0 = banda.X + (banda.Ancho - grupo) / 2;
        return Enumerable.Range(0, series).Select(i => (x0 + i * (una + separacion), una)).ToList();
    }

    /// <summary>
    /// Rectángulo vertical de una barra entre dos valores (<paramref name="desde"/> abajo, <paramref name="hasta"/> arriba) en la
    /// escala; si <paramref name="hasta"/> es menor (valor negativo) el rectángulo queda por debajo de <paramref name="desde"/>.
    /// Alto cero si los dos valores coinciden.
    /// </summary>
    public static Caja Barra(EscalaLineal escala, double x, double ancho, double desde, double hasta)
    {
        var y1 = escala.Mapear(desde);
        var y2 = escala.Mapear(hasta);
        return new Caja(x, Math.Min(y1, y2), ancho, Math.Abs(y1 - y2));
    }

    /// <summary>
    /// Tramos continuos de una serie: cada tramo es una lista de índices consecutivos con valor. Un nulo corta la línea
    /// (no se interpola el hueco); un punto aislado es un tramo de un solo índice.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> Tramos<T>(IReadOnlyList<T?> valores) where T : struct
    {
        var tramos = new List<IReadOnlyList<int>>();
        List<int>? actual = null;
        for (var i = 0; i < valores.Count; i++)
        {
            if (valores[i] is null)
            {
                actual = null;
                continue;
            }
            if (actual is null) tramos.Add(actual = []);
            actual.Add(i);
        }
        return tramos;
    }

    /// <summary>
    /// Segmentos de una barra apilada horizontal de ancho <paramref name="ancho"/>: cada valor positivo ocupa su proporción, con
    /// <paramref name="hueco"/> píxeles entre segmentos y un mínimo de <paramref name="minimo"/> para que ningún tramo desaparezca.
    /// Los valores cero o negativos no tienen segmento (ancho 0).
    /// </summary>
    public static IReadOnlyList<(double X, double Ancho)> Apilada(IReadOnlyList<decimal> valores, double ancho, double hueco = 2, double minimo = 4)
    {
        var visibles = valores.Count(v => v > 0);
        var r = new (double X, double Ancho)[valores.Count];
        if (visibles == 0 || ancho <= 0) return r;
        var util = Math.Max(0, ancho - hueco * (visibles - 1));
        var total = (double)valores.Where(v => v > 0).Sum();
        // Primero los mínimos; el resto se reparte en proporción entre los que lo superan.
        var anchos = valores.Select(v => v > 0 ? util * (double)v / total : 0).ToArray();
        var pequenos = anchos.Count(a => a > 0 && a < minimo);
        if (pequenos > 0 && util > minimo * visibles)
        {
            var reservado = minimo * pequenos;
            var restoTotal = valores.Where((v, i) => v > 0 && anchos[i] >= minimo).Sum(v => (double)v);
            for (var i = 0; i < anchos.Length; i++)
                if (anchos[i] > 0) anchos[i] = anchos[i] < minimo ? minimo : (util - reservado) * (double)valores[i] / restoTotal;
        }
        var x = 0.0;
        for (var i = 0; i < valores.Count; i++)
        {
            if (anchos[i] <= 0) continue;
            r[i] = (x, anchos[i]);
            x += anchos[i] + hueco;
        }
        return r;
    }

    /// <summary>Proporción (0 a 1) de un valor sobre el máximo de la escala, para las barras horizontales; 0 si el máximo es 0.</summary>
    public static double Proporcion(decimal valor, decimal maximo) =>
        maximo <= 0 || valor <= 0 ? 0 : Math.Min(1, (double)(valor / maximo));

    /// <summary>
    /// Franja de rango de un ratio (informes/graficos): posiciones de 0 a 1 de la banda del rango de referencia, del valor y de la
    /// referencia (el ejercicio comparado) sobre una escala de 0 a un máximo que deja ver el rango y los valores. Rango abierto por
    /// arriba (máximo null): la banda llega al final. «Menos es mejor»: la banda empieza en 0. Los valores fuera de escala (o
    /// negativos) se quedan en el borde; un valor null no tiene marca.
    /// </summary>
    public static (double BandaDesde, double BandaHasta, double? Valor, double? Referencia) Franja(
        decimal? minimo, decimal? maximo, bool menosEsMejor, decimal? valor, decimal? referencia)
    {
        var lo = minimo ?? 0m;
        var v = Math.Max(valor ?? 0m, referencia ?? 0m);
        var tope = menosEsMejor && maximo is decimal h1 ? Math.Max(h1 * 1.6m, v * 1.2m)
            : maximo is decimal h2 ? Math.Max(h2 * 1.3m, v * 1.15m)
            : Math.Max(lo * 2m, v * 1.25m);
        if (tope <= 0) tope = 1m;
        double P(decimal x) => Math.Clamp((double)(x / tope), 0, 1);
        var desde = menosEsMejor ? 0 : P(lo);
        var hasta = maximo is decimal h ? P(h) : 1;
        return (desde, hasta, valor is decimal a ? P(a) : null, referencia is decimal r ? P(r) : null);
    }
}

/// <summary>Geometría pura del minigráfico (para probarla sin WPF).</summary>
public static class MinigraficoGeometria
{
    /// <summary>Un punto por valor (null en los huecos), repartidos a lo ancho; el mínimo abajo y el máximo arriba, con 3 px de margen.</summary>
    public static IReadOnlyList<Punto?> Puntos(IReadOnlyList<decimal?> valores, double ancho, double alto)
    {
        var con = valores.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (con.Count == 0) return valores.Select(_ => (Punto?)null).ToList();
        var min = con.Min();
        var max = con.Max();
        var n = valores.Count;
        return valores.Select((v, i) => v is not decimal d ? (Punto?)null : new Punto(
            n == 1 ? ancho / 2 : 3 + i * (ancho - 6) / (n - 1),
            alto - 3 - (max == min ? 0.5 : (double)((d - min) / (max - min))) * (alto - 8))).ToList();
    }
}
