namespace ProConta.Activos.Financieros;

/// <summary>Lectura de un indicador: puntuación de −2 (desfavorable) a +2 (favorable) y su traducción a frases para quien no es experto.</summary>
/// <param name="Clave">Identificador fijo (TENDENCIA, MEDIAS, RSI, MACD, BOLLINGER, ADX, VOLATILIDAD, MAXIMO, SUELO).</param>
/// <param name="Titulo">Qué mide, en lenguaje llano.</param>
/// <param name="Lectura">La conclusión en pocas palabras.</param>
/// <param name="Explicacion">Una o dos frases que la explican.</param>
/// <param name="Detalle">El indicador técnico y su valor (para la ayuda emergente).</param>
/// <param name="Puntuacion">−2 a +2; null si el indicador solo informa y no puntúa.</param>
public sealed record SenalTecnica(string Clave, string Titulo, string Lectura, string Explicacion, string Detalle, int? Puntuacion);

/// <summary>Soportes y resistencias del mes: puntos pivote clásicos con el máximo, mínimo y último cierre del mes anterior.</summary>
public sealed record PuntosPivote(DateTime Mes, decimal Pivote, decimal S1, decimal S2, decimal S3, decimal R1, decimal R2, decimal R3);

/// <summary>Un escenario de la proyección: el valor que saldría en 1, 3 y 5 años.</summary>
/// <param name="Desviaciones">−1 prudente, 0 medio, +1 optimista.</param>
public sealed record EscenarioProyeccion(string Nombre, int Desviaciones, IReadOnlyList<(int Anios, decimal Valor, decimal RentabilidadAnual)> Valores);

/// <summary>El análisis de un activo: señales, valoración conjunta, soportes y escenarios.</summary>
/// <param name="Valoracion">Favorable, Neutral o Desfavorable (la media de las puntuaciones; ver <see cref="AnalisisTecnico.UmbralValoracion"/>).</param>
/// <param name="RentabilidadHistorica">Rentabilidad anual compuesta del periodo usado (hasta 5 años), en tanto por uno.</param>
/// <param name="Volatilidad">Desviación típica anualizada de la rentabilidad diaria (logarítmica), en tanto por uno.</param>
/// <param name="AniosHistorico">Años de cotizaciones usados para la rentabilidad y la volatilidad.</param>
public sealed record ResultadoAnalisis(IReadOnlyList<SenalTecnica> Senales, decimal? PuntuacionMedia, string Valoracion, PuntosPivote? Pivotes,
    decimal? RentabilidadHistorica, decimal? Volatilidad, decimal AniosHistorico, IReadOnlyList<EscenarioProyeccion> Escenarios)
{
    public bool TieneProyeccion => Escenarios.Count > 0;
}

/// <summary>
/// Análisis técnico de la cotización diaria y proyección por escenarios, calculados por código (sin IA; activos/activos-financieros, 2026-11-23 20:13).
/// Reproduce los indicadores de technical-scanner sobre cierres diarios (los fondos solo tienen un valor al día: ATR y ADX se aproximan con cierres,
/// sin volumen ni patrones de velas). Los indicadores son razones, no importes: se calculan en coma flotante; los importes que salen (precios de
/// soporte y valores de los escenarios) se devuelven en decimal redondeados (precio a 4 decimales, valor a céntimos).
/// </summary>
public static class AnalisisTecnico
{
    /// <summary>Sesiones de bolsa en un año (para anualizar).</summary>
    public const int SesionesAnio = 252;

    /// <summary>Mínimo de cierres para calcular indicadores (las medias de 26 y la señal de 9 del MACD, con margen).</summary>
    public const int MinimoCierres = 40;

    /// <summary>Media de las puntuaciones a partir de la cual la valoración es favorable (o desfavorable si es menor que su opuesto).</summary>
    public const decimal UmbralValoracion = 0.5m;

    /// <summary>Banda alrededor de la media de 200 sesiones dentro de la cual no hay tendencia clara (±2 %).</summary>
    public const double BandaTendencia = 0.02;

    /// <summary>Sesiones hacia atrás en las que un cruce de medias o del MACD se considera reciente.</summary>
    public const int SesionesCruceReciente = 5;

    /// <summary>Plazos de la proyección, en años.</summary>
    public static readonly int[] Plazos = [1, 3, 5];

    /// <summary>
    /// Analiza los cierres en euros (ordenados por fecha) y proyecta el <paramref name="valorPosicion"/> de hoy. Con menos de
    /// <see cref="MinimoCierres"/> cierres no hay señales; con menos de un año de cierres no hay escenarios.
    /// </summary>
    public static ResultadoAnalisis Analizar(SeriePrecios serie, decimal valorPosicion)
    {
        var c = serie.Precios.Select(p => (double)p).ToArray();
        var f = serie.Fechas;
        var senales = new List<SenalTecnica>();
        PuntosPivote? pivotes = null;
        if (c.Length >= MinimoCierres)
        {
            AgregarSiHay(senales, Tendencia(c));
            senales.Add(Medias(c));
            senales.Add(RitmoRsi(c));
            senales.Add(Macd(c));
            senales.Add(Bandas(c));
            senales.Add(Fuerza(c));
            senales.Add(Volatilidad(c));
            senales.Add(Maximo(c));
            senales.Add(Suelo(c));
            pivotes = Pivotes(f, c);
        }
        var puntuadas = senales.Where(s => s.Puntuacion is not null).ToList();
        decimal? media = puntuadas.Count == 0 ? null : Math.Round((decimal)puntuadas.Average(s => s.Puntuacion!.Value), 2, MidpointRounding.AwayFromZero);
        var valoracion = media is null ? "Sin datos" : media >= UmbralValoracion ? "Favorable" : media <= -UmbralValoracion ? "Desfavorable" : "Neutral";

        var (anual, sd, anios) = Historica(c);
        var escenarios = new List<EscenarioProyeccion>();
        if (anual is double a && sd is double s && valorPosicion > 0m)
            foreach (var (nombre, k) in new[] { ("Prudente", -1), ("Medio", 0), ("Optimista", 1) })
                escenarios.Add(new EscenarioProyeccion(nombre, k, Plazos.Select(h =>
                {
                    var factor = Math.Pow(1 + a, h) * Math.Exp(k * s * Math.Sqrt(h));
                    var v = Math.Round(valorPosicion * (decimal)factor, 2, MidpointRounding.AwayFromZero);
                    return (h, v, Math.Round((decimal)(Math.Pow(factor, 1.0 / h) - 1), 6, MidpointRounding.AwayFromZero));
                }).ToList()));

        return new ResultadoAnalisis(senales, media, valoracion, pivotes,
            anual is null ? null : Math.Round((decimal)anual.Value, 6, MidpointRounding.AwayFromZero),
            sd is null ? null : Math.Round((decimal)sd.Value, 6, MidpointRounding.AwayFromZero),
            Math.Round((decimal)anios, 1, MidpointRounding.AwayFromZero), escenarios);
    }

    private static void AgregarSiHay(List<SenalTecnica> l, SenalTecnica? s) { if (s is not null) l.Add(s); }

    // ---------- indicadores (públicos para las pruebas) ----------

    /// <summary>Media simple de las últimas <paramref name="n"/> posiciones hasta <paramref name="fin"/> (excluido).</summary>
    public static double Sma(double[] c, int n, int fin) => c.Skip(fin - n).Take(n).Average();

    /// <summary>Media exponencial (α = 2/(n+1)), sembrada con la media simple de los n primeros valores; NaN antes de tener n valores.</summary>
    public static double[] Ema(double[] c, int n)
    {
        var r = new double[c.Length];
        Array.Fill(r, double.NaN);
        if (c.Length < n) return r;
        var a = 2.0 / (n + 1);
        r[n - 1] = c.Take(n).Average();
        for (var i = n; i < c.Length; i++) r[i] = a * c[i] + (1 - a) * r[i - 1];
        return r;
    }

    /// <summary>RSI de Wilder de <paramref name="n"/> sesiones (0 a 100) en cada punto; NaN al principio.</summary>
    public static double[] Rsi(double[] c, int n = 14)
    {
        var r = new double[c.Length];
        Array.Fill(r, double.NaN);
        if (c.Length <= n) return r;
        double g = 0, p = 0;
        for (var i = 1; i <= n; i++) { var d = c[i] - c[i - 1]; if (d > 0) g += d; else p -= d; }
        g /= n; p /= n;
        r[n] = Rsi(g, p);
        for (var i = n + 1; i < c.Length; i++)
        {
            var d = c[i] - c[i - 1];
            g = (g * (n - 1) + Math.Max(d, 0)) / n;
            p = (p * (n - 1) + Math.Max(-d, 0)) / n;
            r[i] = Rsi(g, p);
        }
        return r;
    }

    private static double Rsi(double g, double p) => p == 0 ? (g == 0 ? 50 : 100) : 100 - 100 / (1 + g / p);

    /// <summary>MACD (EMA 12 − EMA 26), su señal (EMA 9 del MACD) y el histograma (MACD − señal).</summary>
    public static (double[] Linea, double[] Senal, double[] Histograma) Macd(double[] c, int rapida = 12, int lenta = 26, int senal = 9)
    {
        var er = Ema(c, rapida); var el = Ema(c, lenta);
        var linea = c.Select((_, i) => er[i] - el[i]).ToArray();
        var inicio = lenta - 1;
        var s = new double[c.Length];
        Array.Fill(s, double.NaN);
        if (c.Length >= inicio + senal)
        {
            var e = Ema(linea.Skip(inicio).ToArray(), senal);
            for (var i = 0; i < e.Length; i++) s[inicio + i] = e[i];
        }
        return (linea, s, linea.Select((v, i) => v - s[i]).ToArray());
    }

    /// <summary>
    /// ATR y ADX de Wilder aproximados con cierres: el rango verdadero es |cierre − cierre anterior| y el movimiento direccional, la subida o la bajada
    /// del cierre. Devuelve los valores del último día (ATR en euros, ADX y ±DI de 0 a 100); NaN si no hay 2n+1 cierres.
    /// </summary>
    public static (double Atr, double Adx, double DiMas, double DiMenos) AtrAdx(double[] c, int n = 14)
    {
        if (c.Length < 2 * n + 1) return (double.NaN, double.NaN, double.NaN, double.NaN);
        double tr = 0, dm = 0, dn = 0;
        for (var i = 1; i <= n; i++) Paso(c, i, ref tr, ref dm, ref dn, 1);
        var dxs = new List<double>();
        double adx = double.NaN;
        for (var i = n + 1; i < c.Length; i++)
        {
            Paso(c, i, ref tr, ref dm, ref dn, n);
            var (mas, menos) = Di(tr, dm, dn);
            var dx = mas + menos == 0 ? 0 : 100 * Math.Abs(mas - menos) / (mas + menos);
            if (double.IsNaN(adx)) { dxs.Add(dx); if (dxs.Count == n) adx = dxs.Average(); }
            else adx = (adx * (n - 1) + dx) / n;
        }
        var (dp, dmn) = Di(tr, dm, dn);
        return (tr / n, adx, dp, dmn);

        static void Paso(double[] c, int i, ref double tr, ref double dm, ref double dn, int n)
        {
            var d = c[i] - c[i - 1];
            if (n == 1) { tr += Math.Abs(d); dm += Math.Max(d, 0); dn += Math.Max(-d, 0); return; }
            tr = tr - tr / n + Math.Abs(d);
            dm = dm - dm / n + Math.Max(d, 0);
            dn = dn - dn / n + Math.Max(-d, 0);
        }
        static (double, double) Di(double tr, double dm, double dn) => tr == 0 ? (0, 0) : (100 * dm / tr, 100 * dn / tr);
    }

    /// <summary>Puntos pivote clásicos con el máximo, el mínimo y el último cierre del último mes completo de la serie.</summary>
    public static PuntosPivote? Pivotes(IReadOnlyList<DateTime> f, double[] c)
    {
        if (c.Length == 0) return null;
        var ultimo = new DateTime(f[^1].Year, f[^1].Month, 1);
        var mes = ultimo.AddMonths(-1);
        var idx = Enumerable.Range(0, c.Length).Where(i => f[i] >= mes && f[i] < ultimo).ToList();
        if (idx.Count == 0) return null;
        double h = idx.Max(i => c[i]), l = idx.Min(i => c[i]), cl = c[idx[^1]];
        var p = (h + l + cl) / 3;
        static decimal R(double v) => Math.Round((decimal)v, 4, MidpointRounding.AwayFromZero);
        return new PuntosPivote(mes, R(p), R(2 * p - h), R(p - (h - l)), R(l - 2 * (h - p)), R(2 * p - l), R(p + (h - l)), R(h + 2 * (p - l)));
    }

    /// <summary>
    /// Rentabilidad anual compuesta y volatilidad anualizada de los últimos 5 años como mucho (al menos uno): (último / primero)^(252/n) − 1 y la
    /// desviación típica de los rendimientos diarios logarítmicos × √252.
    /// </summary>
    public static (double? Anual, double? Volatilidad, double Anios) Historica(double[] c)
    {
        var n = Math.Min(c.Length - 1, 5 * SesionesAnio);
        if (n < SesionesAnio) return (null, null, Math.Max(0, n) / (double)SesionesAnio);
        var ini = c[^(n + 1)];
        var anual = Math.Pow(c[^1] / ini, (double)SesionesAnio / n) - 1;
        var r = Enumerable.Range(c.Length - n, n).Select(i => Math.Log(c[i] / c[i - 1])).ToArray();
        var m = r.Average();
        var sd = Math.Sqrt(r.Sum(x => (x - m) * (x - m)) / r.Length * SesionesAnio);
        return (anual, sd, n / (double)SesionesAnio);
    }

    // ---------- traducción a frases ----------

    private static string N(double v, int d = 2) => v.ToString("N" + d, System.Globalization.CultureInfo.GetCultureInfo("es-ES"));

    private static SenalTecnica? Tendencia(double[] c)
    {
        if (c.Length < 200) return new SenalTecnica("TENDENCIA", "Tendencia de fondo", "Sin datos suficientes",
            "Hacen falta unos diez meses de cotizaciones para ver la tendencia de fondo.", $"Media de 200 sesiones: hay {c.Length} cierres", null);
        var mm = Sma(c, 200, c.Length);
        var mmAntes = c.Length >= 220 ? Sma(c, 200, c.Length - 20) : mm;
        var p = c[^1];
        var detalle = $"Precio {N(p)} € frente a la media de 200 sesiones {N(mm)} € ({N((p / mm - 1) * 100, 1)} %)";
        if (p > mm * (1 + BandaTendencia))
            return new SenalTecnica("TENDENCIA", "Tendencia de fondo", "Al alza",
                "El precio está por encima de su media de los últimos diez meses: lleva tiempo subiendo." + (mm > mmAntes ? " Y esa media sigue subiendo." : ""),
                detalle, mm > mmAntes ? 2 : 1);
        if (p < mm * (1 - BandaTendencia))
            return new SenalTecnica("TENDENCIA", "Tendencia de fondo", "A la baja",
                "El precio está por debajo de su media de los últimos diez meses: lleva tiempo bajando." + (mm < mmAntes ? " Y esa media sigue bajando." : ""),
                detalle, mm < mmAntes ? -2 : -1);
        return new SenalTecnica("TENDENCIA", "Tendencia de fondo", "Sin tendencia clara", "El precio está cerca de su media de los últimos diez meses.", detalle, 0);
    }

    private static int CruceReciente(double[] a, double[] b)
    {
        for (var i = a.Length - 1; i >= Math.Max(1, a.Length - SesionesCruceReciente); i--)
        {
            if (double.IsNaN(a[i - 1]) || double.IsNaN(b[i - 1])) break;
            if (a[i] > b[i] && a[i - 1] <= b[i - 1]) return 1;
            if (a[i] < b[i] && a[i - 1] >= b[i - 1]) return -1;
        }
        return 0;
    }

    private static SenalTecnica Medias(double[] c)
    {
        var e9 = Ema(c, 9); var e21 = Ema(c, 21);
        var cruce = CruceReciente(e9, e21);
        var arriba = e9[^1] > e21[^1];
        var det = $"Media exponencial de 9 sesiones {N(e9[^1])} € y de 21 sesiones {N(e21[^1])} €";
        return cruce switch
        {
            1 => new("MEDIAS", "Últimas semanas", "Acaba de girar al alza", "En los últimos días la media de dos semanas ha pasado por encima de la de un mes: el precio ha cambiado de rumbo hacia arriba.", det, 2),
            -1 => new("MEDIAS", "Últimas semanas", "Acaba de girar a la baja", "En los últimos días la media de dos semanas ha pasado por debajo de la de un mes: el precio ha cambiado de rumbo hacia abajo.", det, -2),
            _ when arriba => new("MEDIAS", "Últimas semanas", "Subiendo", "La media de las dos últimas semanas está por encima de la del último mes: el precio va hacia arriba.", det, 1),
            _ => new("MEDIAS", "Últimas semanas", "Bajando", "La media de las dos últimas semanas está por debajo de la del último mes: el precio va hacia abajo.", det, -1),
        };
    }

    private static SenalTecnica RitmoRsi(double[] c)
    {
        var r = Rsi(c)[^1];
        var det = $"RSI de 14 sesiones: {N(r, 0)}";
        return r switch
        {
            >= 80 => new("RSI", "Ritmo de las últimas semanas", "Ha subido muy deprisa", "En las últimas tres semanas ha subido muchísimo; lo habitual es que después se tome un respiro.", det, -2),
            >= 70 => new("RSI", "Ritmo de las últimas semanas", "Ha subido deprisa", "En las últimas tres semanas ha subido mucho; es habitual que después se tome un respiro.", det, -1),
            <= 20 => new("RSI", "Ritmo de las últimas semanas", "Ha bajado muy deprisa", "En las últimas tres semanas ha bajado muchísimo; es frecuente que rebote después.", det, 2),
            <= 30 => new("RSI", "Ritmo de las últimas semanas", "Ha bajado deprisa", "En las últimas tres semanas ha bajado mucho; a veces rebota después.", det, 1),
            _ => new("RSI", "Ritmo de las últimas semanas", "Ritmo normal", "En las últimas tres semanas no ha subido ni bajado de forma exagerada.", det, 0),
        };
    }

    private static SenalTecnica Macd(double[] c)
    {
        var (l, s, h) = Macd(c, 12, 26, 9);
        var cruce = CruceReciente(l, s);
        var crece = h.Length > 1 && h[^1] > h[^2];
        var det = $"MACD (12, 26, 9): línea {N(l[^1], 4)}, señal {N(s[^1], 4)}, histograma {N(h[^1], 4)}";
        // Un cruce a favor de la tendencia del MACD (por encima de cero al alza, por debajo a la baja) puntúa doble; en contra, simple.
        if (cruce == 1) return new("MACD", "Impulso", "Gana impulso", "El impulso de subida acaba de imponerse al de bajada." +
            (l[^1] > 0 ? "" : " Todavía dentro de una etapa de bajada."), det, l[^1] > 0 ? 2 : 1);
        if (cruce == -1) return new("MACD", "Impulso", "Pierde impulso", "El impulso de bajada acaba de imponerse al de subida." +
            (l[^1] < 0 ? "" : " Todavía dentro de una etapa de subida."), det, l[^1] < 0 ? -2 : -1);
        if (l[^1] > s[^1])
            return crece ? new("MACD", "Impulso", "Impulso al alza creciendo", "Domina el impulso de subida y va a más.", det, 1)
                         : new("MACD", "Impulso", "Impulso al alza frenándose", "Domina el impulso de subida, pero cada vez con menos fuerza.", det, 0);
        return !crece ? new("MACD", "Impulso", "Impulso a la baja creciendo", "Domina el impulso de bajada y va a más.", det, -1)
                      : new("MACD", "Impulso", "Impulso a la baja frenándose", "Domina el impulso de bajada, pero cada vez con menos fuerza.", det, 0);
    }

    private static SenalTecnica Bandas(double[] c)
    {
        var m = Sma(c, 20, c.Length);
        var sd = Math.Sqrt(c.Skip(c.Length - 20).Sum(x => (x - m) * (x - m)) / 20);
        double sup = m + 2 * sd, inf = m - 2 * sd, p = c[^1];
        var b = sup == inf ? 0.5 : (p - inf) / (sup - inf);
        var det = $"Bandas de Bollinger (20, 2): {N(inf)} € – {N(sup)} €; posición {N(b * 100, 0)} %";
        return b switch
        {
            > 1 => new("BOLLINGER", "Precio frente a lo normal del último mes", "Por encima de lo habitual", $"Cotiza por encima del rango normal del último mes ({N(inf)} € a {N(sup)} €); suele volver hacia dentro.", det, -1),
            < 0 => new("BOLLINGER", "Precio frente a lo normal del último mes", "Por debajo de lo habitual", $"Cotiza por debajo del rango normal del último mes ({N(inf)} € a {N(sup)} €); suele volver hacia dentro.", det, 1),
            _ => new("BOLLINGER", "Precio frente a lo normal del último mes", "Dentro de lo habitual", $"Cotiza dentro del rango normal del último mes ({N(inf)} € a {N(sup)} €).", det, 0),
        };
    }

    private static SenalTecnica Fuerza(double[] c)
    {
        var (_, adx, mas, menos) = AtrAdx(c);
        if (double.IsNaN(adx)) return new("ADX", "Fuerza de la tendencia", "Sin datos suficientes", "Hacen falta más cotizaciones.", "ADX de 14 sesiones", null);
        var det = $"ADX de 14 sesiones (aproximado con cierres): {N(adx, 0)}; +DI {N(mas, 0)}, −DI {N(menos, 0)}";
        var sube = mas >= menos;
        if (adx >= 25)
            return new("ADX", "Fuerza de la tendencia", sube ? "Tendencia fuerte al alza" : "Tendencia fuerte a la baja",
                sube ? "Las subidas dominan con claridad: el mercado tiene una dirección marcada." : "Las bajadas dominan con claridad: el mercado tiene una dirección marcada.", det, sube ? 2 : -2);
        if (adx >= 20)
            return new("ADX", "Fuerza de la tendencia", sube ? "Tendencia moderada al alza" : "Tendencia moderada a la baja",
                "Hay una dirección, pero todavía sin mucha fuerza.", det, sube ? 1 : -1);
        return new("ADX", "Fuerza de la tendencia", "Sin dirección", "Sube y baja sin una dirección clara (mercado lateral).", det, 0);
    }

    private static SenalTecnica Volatilidad(double[] c)
    {
        var (atr, _, _, _) = AtrAdx(c);
        var pct = atr / c[^1];
        var det = $"ATR de 14 sesiones (aproximado con cierres): {N(atr, 4)} € ({N(pct * 100, 2)} % del precio)";
        var (lect, exp) = pct switch
        {
            < 0.005 => ("Tranquilo", "Un día normal se mueve menos de medio punto porcentual."),
            < 0.015 => ("Movimiento normal", $"Un día normal se mueve alrededor de un {N(pct * 100, 1)} %."),
            _ => ("Movido", $"Un día normal se mueve alrededor de un {N(pct * 100, 1)} %: los sustos y las alegrías son grandes."),
        };
        return new("VOLATILIDAD", "Cuánto se mueve cada día", lect, exp, det, null);
    }

    private static SenalTecnica Maximo(double[] c)
    {
        var max = c.Skip(Math.Max(0, c.Length - SesionesAnio)).Max();
        var dist = c[^1] / max - 1;
        var det = $"Máximo de las últimas 52 semanas: {N(max)} €";
        return dist > -0.01
            ? new("MAXIMO", "Frente a su máximo del último año", "En máximos", "Cotiza en el precio más alto del último año.", det, null)
            : new("MAXIMO", "Frente a su máximo del último año", $"{N(dist * 100, 1)} %", $"Está un {N(-dist * 100, 1)} % por debajo del precio más alto del último año ({N(max)} €).", det, null);
    }

    private static SenalTecnica Suelo(double[] c)
    {
        var min = c.Skip(Math.Max(0, c.Length - 63)).Min();
        return new("SUELO", "Suelo reciente", $"{N(min)} €", "Es el precio más bajo de los últimos tres meses. Si bajara de ahí, sería una señal de debilidad.",
            "Mínimo de las últimas 63 sesiones", null);
    }
}
