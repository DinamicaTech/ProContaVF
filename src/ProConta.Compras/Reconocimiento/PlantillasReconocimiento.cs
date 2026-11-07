using System.Globalization;
using System.Text.RegularExpressions;

namespace ProConta.Compras.Reconocimiento;

/// <summary>
/// Dónde está un campo en las facturas de un proveedor con un formato concreto (compras/reconocimiento 2026-11-05 18:42):
/// el rótulo que lo precede en la misma línea («derecha») o en la línea de encima («debajo»), su página, la posición del
/// rótulo y cuántas palabras ocupa el valor. Se aprende al guardar una factura revisada y se aplica al reconocer las
/// siguientes del mismo proveedor: si el rótulo aparece, el valor sale de ahí con confianza 1.
/// </summary>
public sealed record PlantillaCampo(int IdPlantilla, string Campo, int Pagina, string Ancla, string Posicion, int Palabras, double X, double Y, double DesplazamientoX, int Aciertos);

/// <summary>Aprende y aplica plantillas de campo sobre el documento extraído. No accede a la BD.</summary>
public static class PlantillasReconocimiento
{
    public const string Nif = "nif", Numero = "numero", Fecha = "fecha", Total = "total", Concepto = "concepto";
    public const string Derecha = "derecha", Debajo = "debajo";
    /// <summary>Concepto que es un texto fijo de la factura (un título como «Factura de llum»): si el texto está, es el concepto.</summary>
    public const string Texto = "texto";
    public static IReadOnlyList<string> Campos { get; } = [Nif, Numero, Fecha, Total, Concepto];

    private const int MaxPalabrasValor = 6, MaxPalabrasConcepto = 20, MaxPalabrasAncla = 3, MaxAncla = 120;

    /// <summary>Palabras que puede ocupar el valor: el concepto es texto libre (compras/reconocimiento 2026-11-07).</summary>
    /// <summary>Distancia máxima entre el rótulo y el valor de debajo: el concepto puede estar unas filas por debajo de la cabecera de la tabla.</summary>
    private static double Alcance(string campo, double alto) => campo == Concepto ? Math.Max(8 * alto, 90) : Math.Max(3 * alto, 30);

    private static int MaxPalabras(string campo) => campo == Concepto ? MaxPalabrasConcepto : MaxPalabrasValor;

    private sealed record Linea(int Pagina, double Y, double Alto, IReadOnlyList<PalabraDocumento> Palabras);

    /// <summary>
    /// Plantilla para que <paramref name="valor"/> (el valor bueno del campo, ya revisado) se vuelva a leer en facturas
    /// con el mismo formato. Null si el valor no aparece en el PDF o si no hay un rótulo que lo identifique sin ambigüedad.
    /// </summary>
    /// <remarks>
    /// Con <paramref name="elegido"/> (el punto del PDF en el que el operador eligió el valor en el visor) solo se aprende de
    /// esa aparición; si ahí no está el valor, de la mejor de todas (o null con <paramref name="soloAhi"/>).
    /// </remarks>
    public static PlantillaCampo? Aprender(DocumentoFacturaExtraido documento, string campo, string valor, PuntoDocumento? elegido = null, bool soloAhi = false)
    {
        if (elegido is not null && AprenderEn(documento, campo, valor, elegido) is { } enElegido) return enElegido;
        return elegido is not null && soloAhi ? null : AprenderEn(documento, campo, valor, null);
    }

    /// <summary>Las líneas tal como las ve el motor de plantillas (página, altura y cada palabra con su X), para diagnosticar.</summary>
    public static string DescribirLineas(DocumentoFacturaExtraido documento) =>
        string.Join(Environment.NewLine, Lineas(documento).Select(l =>
            $"p{l.Pagina} y={l.Y:0} | " + string.Join(" ", l.Palabras.Select(w => $"{w.Texto}@{w.X:0}-{w.X + w.Ancho:0}"))));

    /// <summary>¿Las dos plantillas leen del mismo sitio (mismo rótulo, posición y página)?</summary>
    public static bool MismoSitio(PlantillaCampo a, PlantillaCampo b) =>
        a.Campo == b.Campo && a.Ancla == b.Ancla && a.Posicion == b.Posicion && a.Pagina == b.Pagina;

    private static PlantillaCampo? AprenderEn(DocumentoFacturaExtraido documento, string campo, string valor, PuntoDocumento? elegido)
    {
        if (string.IsNullOrWhiteSpace(valor) || !Campos.Contains(campo)) return null;
        var lineas = Lineas(documento);
        PlantillaCampo? mejor = null;
        var mejorPuntos = int.MinValue;
        for (var li = 0; li < lineas.Count; li++)
        {
            var linea = lineas[li];
            for (var i = 0; i < linea.Palabras.Count; i++)
                for (var n = 1; n <= MaxPalabras(campo) && i + n <= linea.Palabras.Count; n++)
                {
                    var texto = Unir(linea.Palabras, i, n);
                    // El concepto puede seguir en las líneas de debajo: basta con que esta línea sea su principio.
                    if (Interpretar(campo, texto) is not { } leido || !(campo == Concepto ? EsPrincipio(leido, valor) : Coincide(campo, leido, valor))) continue;
                    if (elegido is not null && !Contiene(linea.Palabras, i, n, elegido)) break;
                    foreach (var candidata in Candidatas(lineas, li, i, n, campo))
                    {
                        // Solo vale si, aplicada a este mismo PDF, devuelve este valor (el rótulo no lleva a otro sitio).
                        if (Leer(documento, candidata, lineas) is not { } relectura || !Coincide(campo, relectura, valor)) continue;
                        // El concepto suele ir bajo la cabecera de una tabla; a su izquierda suele haber un código, no un rótulo.
                        var preferida = campo == Concepto ? Debajo : Derecha;
                        var puntos = (candidata.Posicion == preferida ? 100 : 50) - Ocurrencias(lineas, candidata.Ancla) * 10 + candidata.Ancla.Length / 4;
                        if (puntos > mejorPuntos) (mejor, mejorPuntos) = (candidata, puntos);
                    }
                    break;
                }
        }
        return mejor ?? (campo == Concepto && elegido is null ? AprenderTextoFijo(lineas, valor) : null);
    }

    /// <summary>¿Las palabras de <paramref name="leido"/> son el principio de las de <paramref name="valor"/>?</summary>
    private static bool EsPrincipio(string leido, string valor)
    {
        var a = Normalizar(leido).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = Normalizar(valor).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return a.Length > 0 && a.Length <= b.Length && a.SequenceEqual(b.Take(a.Length));
    }

    /// <summary>
    /// Concepto sin rótulo que es siempre el mismo texto (el título de la factura): la plantilla guarda el texto y, si está
    /// en otra factura, lo devuelve (compras/reconocimiento 2026-11-07).
    /// </summary>
    private static PlantillaCampo? AprenderTextoFijo(IReadOnlyList<Linea> lineas, string valor)
    {
        var buscado = Normalizar(valor);
        var n = buscado.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (n == 0 || n > MaxPalabrasConcepto || buscado.Length > MaxAncla || Interpretar(Concepto, valor) is null) return null;
        foreach (var l in lineas)
            for (var i = 0; i + n <= l.Palabras.Count; i++)
                if (Normalizar(Unir(l.Palabras, i, n)) == buscado)
                    return new PlantillaCampo(0, Concepto, l.Pagina, buscado, Texto, n, l.Palabras[i].X, l.Y, 0, 0);
        return null;
    }

    /// <summary>¿Alguna de las palabras i..i+n−1 está en el punto elegido (con 3 puntos de holgura)?</summary>
    private static bool Contiene(IReadOnlyList<PalabraDocumento> palabras, int i, int n, PuntoDocumento p)
    {
        const double holgura = 3;
        for (var k = i; k < i + n; k++)
        {
            var w = palabras[k];
            if (w.Pagina == p.Pagina && p.X >= w.X - holgura && p.X <= w.X + w.Ancho + holgura && p.Y >= w.Y - holgura && p.Y <= w.Y + w.Alto + holgura)
                return true;
        }
        return false;
    }

    /// <summary>Valor del campo según la plantilla, normalizado (fecha yyyy-MM-dd, importe con punto, NIF y número compactos), o null.</summary>
    public static string? Leer(DocumentoFacturaExtraido documento, PlantillaCampo plantilla) => Leer(documento, plantilla, Lineas(documento));

    /// <summary>
    /// Lo que lee la plantilla y dónde: el centro de la primera palabra del valor, para marcar en el visor la aparición que
    /// se ha usado (compras/pantallas 2026-11-07). Null si no lee nada.
    /// </summary>
    public static (string Valor, PuntoDocumento Punto)? LeerConPunto(DocumentoFacturaExtraido documento, PlantillaCampo plantilla) =>
        LeerEn(documento, plantilla, Lineas(documento)) is { } r
            ? (r.Valor, new PuntoDocumento(r.Palabra.Pagina, r.Palabra.X + r.Palabra.Ancho / 2, r.Palabra.Y + r.Palabra.Alto / 2)) : null;

    /// <summary>¿El valor leído es el mismo que el guardado? Fechas por día, importes por valor, NIF y número compactos.</summary>
    public static bool Coincide(string campo, string leido, string guardado)
    {
        var a = Interpretar(campo, leido);
        var b = Interpretar(campo, guardado) ?? (campo == Numero ? MotorReconocimientoFacturaCompra.CompactarNumeroFactura(guardado) : null);
        if (a is null || b is null) return false;
        return campo switch
        {
            Nif => SinPrefijoPais(a) == SinPrefijoPais(b),
            Concepto => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase),
            _ => a == b
        };
    }

    /// <summary>Interpretación del texto como valor del campo; null si no lo es.</summary>
    public static string? Interpretar(string campo, string texto)
    {
        var t = texto.Trim().Trim(':', ';', ',', '(', ')', '#');
        if (t.Length == 0) return null;
        switch (campo)
        {
            case Fecha:
                if (DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return MotorReconocimientoFacturaCompra.InterpretarFecha(t.TrimEnd('.')) is { } f ? f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            case Total:
                return MotorReconocimientoFacturaCompra.InterpretarImporte(t) is { } d
                    ? MotorReconocimientoFacturaCompra.Redondear(d).ToString("0.00", CultureInfo.InvariantCulture) : null;
            case Nif:
                var nif = MotorReconocimientoFacturaCompra.CompactarNif(t);
                return nif.Length is >= 8 and <= 14 && nif.Any(char.IsDigit) && MotorReconocimientoFacturaCompra.EsNifPlausible(SinPrefijoPais(nif)) ? nif : null;
            case Numero:
                var numero = MotorReconocimientoFacturaCompra.CompactarNumeroFactura(t);
                return numero.Length is >= 1 and <= 40 && numero.Any(char.IsDigit) ? numero : null;
            case Concepto:
                // Sin los importes del final: si se marca la línea entera («Fibra 600 Mb 45,00 € 21 %»), el concepto es el texto.
                var trozos = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                for (var cifras = false; trozos.Count > 0; trozos.RemoveAt(trozos.Count - 1))
                {
                    // Un número entero solo se quita si va antes de otra cifra («21 %»): «Línea 2» se queda como está.
                    if (EsCifraDeLinea(trozos[^1])) cifras = true;
                    else if (!(cifras && trozos[^1].All(char.IsDigit))) break;
                }
                var libre = string.Join(' ', trozos).Trim(':', ';', ',', '-');
                return libre.Length is >= 3 and <= 255 && libre.Count(char.IsLetter) >= 3 ? libre : null;
            default:
                return null;
        }
    }

    /// <summary>Palabras del concepto en la línea desde <paramref name="desde"/>: hasta el primer importe o un hueco grande.</summary>
    private static int TramoConcepto(Linea linea, int desde)
    {
        var n = 0;
        while (desde + n < linea.Palabras.Count && n < MaxPalabrasConcepto)
        {
            var w = linea.Palabras[desde + n];
            if (EsCifraDeLinea(w.Texto)) break;
            if (n > 0)
            {
                var previa = linea.Palabras[desde + n - 1];
                if (w.X - (previa.X + previa.Ancho) > Math.Max(2.5 * Math.Max(w.Alto, linea.Alto), 12)) break;
            }
            n++;
        }
        return n;
    }

    /// <summary>¿Es un importe, un porcentaje o un símbolo de moneda («45,00», «45,00€», «21%», «€»)? Corta el concepto.</summary>
    private static bool EsCifraDeLinea(string palabra)
    {
        var t = palabra.Trim();
        if (t is "€" or "%" or "EUR" or "eur") return true;
        var sinSimbolos = t.TrimEnd('€', '%').TrimStart('€').Trim();
        if (sinSimbolos.Length == 0 || !sinSimbolos.Any(char.IsDigit) || sinSimbolos.Any(char.IsLetter)) return false;
        return t.Contains('€') || t.Contains('%') || Regex.IsMatch(sinSimbolos, @"^-?[\d.]*\d[.,]\d{1,4}$");
    }

    private static string SinPrefijoPais(string nif) =>
        nif.Length > 9 && nif.StartsWith("ES", StringComparison.Ordinal) ? nif[2..] : nif;

    // ---------- aprender ----------

    private static IEnumerable<PlantillaCampo> Candidatas(IReadOnlyList<Linea> lineas, int li, int i, int n, string campo)
    {
        var linea = lineas[li];
        var valor = linea.Palabras[i];
        // Rótulo en la misma línea, justo antes del valor: hasta tres palabras sin cifras.
        var desde = i;
        while (desde > 0 && i - desde < MaxPalabrasAncla && EsPalabraDeRotulo(linea.Palabras[desde - 1].Texto)) desde--;
        for (var inicio = i - 1; inicio >= desde; inicio--)
        {
            var ancla = Normalizar(Unir(linea.Palabras, inicio, i - inicio));
            if (EsAnclaValida(ancla))
                yield return new PlantillaCampo(0, campo, linea.Pagina, ancla, Derecha, n, linea.Palabras[inicio].X, linea.Y, 0, 0);
        }
        // Rótulo en la línea de encima, en la misma columna.
        for (var arriba = li - 1; arriba >= 0 && lineas[arriba].Pagina == linea.Pagina && lineas[arriba].Y - linea.Y <= Alcance(campo, linea.Alto); arriba--)
        {
            var palabras = lineas[arriba].Palabras;
            var cerca = Enumerable.Range(0, palabras.Count)
                .Where(k => EsPalabraDeRotulo(palabras[k].Texto) && Math.Abs(palabras[k].X - valor.X) <= 40)
                .OrderBy(k => Math.Abs(palabras[k].X - valor.X)).ToList();
            if (cerca.Count == 0) continue;
            var k0 = cerca[0];
            var fin = k0;
            while (fin + 1 < palabras.Count && fin - k0 + 1 < MaxPalabrasAncla && EsPalabraDeRotulo(palabras[fin + 1].Texto) &&
                   palabras[fin + 1].X - (palabras[fin].X + palabras[fin].Ancho) < 8) fin++;
            var ancla = Normalizar(Unir(palabras, k0, fin - k0 + 1));
            if (EsAnclaValida(ancla))
                yield return new PlantillaCampo(0, campo, linea.Pagina, ancla, Debajo, n, palabras[k0].X, lineas[arriba].Y, valor.X - palabras[k0].X, 0);
            break;
        }
    }

    private static bool EsPalabraDeRotulo(string texto) => texto.Any(char.IsLetter) && !texto.Any(char.IsDigit);

    private static bool EsAnclaValida(string ancla) => ancla.Count(char.IsLetter) >= 2 && ancla.Length <= MaxAncla;

    private static int Ocurrencias(IReadOnlyList<Linea> lineas, string ancla) => lineas.Sum(l => Apariciones(l, ancla).Count());

    // ---------- leer ----------

    private static string? Leer(DocumentoFacturaExtraido documento, PlantillaCampo p, IReadOnlyList<Linea> lineas) => LeerEn(documento, p, lineas)?.Valor;

    private static (string Valor, PalabraDocumento Palabra)? LeerEn(DocumentoFacturaExtraido documento, PlantillaCampo p, IReadOnlyList<Linea> lineas)
    {
        if (p.Posicion == Texto)
        {
            foreach (var l in lineas.OrderBy(l => l.Pagina == p.Pagina ? 0 : 1))
                for (var i = 0; i + p.Palabras <= l.Palabras.Count; i++)
                    if (Normalizar(Unir(l.Palabras, i, p.Palabras)) == p.Ancla && Interpretar(Concepto, Unir(l.Palabras, i, p.Palabras)) is { } fijo)
                        return (fijo, l.Palabras[i]);
            return null;
        }
        var apariciones = new List<(int Linea, int Inicio, int Fin)>();
        for (var li = 0; li < lineas.Count; li++)
            if (lineas[li].Pagina == p.Pagina)
                apariciones.AddRange(Apariciones(lineas[li], p.Ancla).Select(a => (li, a.Inicio, a.Fin)));
        foreach (var (li, inicio, fin) in apariciones.OrderBy(a => Math.Abs(lineas[a.Linea].Palabras[a.Inicio].X - p.X) + Math.Abs(lineas[a.Linea].Y - p.Y)))
        {
            var leido = p.Posicion == Derecha ? LeerDerecha(lineas, li, fin + 1, p) : LeerDebajo(lineas, li, lineas[li].Palabras[inicio].X + p.DesplazamientoX, p);
            if (leido is not null) return leido;
        }
        return null;
    }

    private static (string Valor, PalabraDocumento Palabra)? LeerDerecha(IReadOnlyList<Linea> lineas, int li, int desde, PlantillaCampo p)
    {
        var linea = lineas[li];
        if (desde >= linea.Palabras.Count) return null;
        // El concepto ocupa lo que quede de la línea hasta el primer importe o un hueco grande (la columna siguiente de una
        // tabla): cada factura lo escribe con un número de palabras distinto (compras/reconocimiento 2026-11-07).
        if (p.Campo == Concepto)
        {
            var n = TramoConcepto(linea, desde);
            if (n == 0) return null;
            var texto = Unir(linea.Palabras, desde, n);
            // Sigue en las líneas de debajo que empiezan en la misma columna y no llevan importes (una descripción larga).
            var x0 = linea.Palabras[desde].X;
            var anterior = linea;
            for (var siguiente = li + 1; siguiente < lineas.Count && siguiente <= li + 3; siguiente++)
            {
                var l = lineas[siguiente];
                if (l.Pagina != linea.Pagina || anterior.Y - l.Y > 1.8 * Math.Max(anterior.Alto, l.Alto) || l.Palabras.Any(w => EsCifraDeLinea(w.Texto))) break;
                var k = Enumerable.Range(0, l.Palabras.Count).FirstOrDefault(j => Math.Abs(l.Palabras[j].X - x0) <= 6, -1);
                if (k < 0 || l.Palabras.Take(k).Any(w => w.X > x0 - 40)) break;
                var m = TramoConcepto(l, k);
                if (m == 0) break;
                texto += " " + Unir(l.Palabras, k, m);
                anterior = l;
            }
            return Interpretar(Concepto, texto) is { } concepto ? (concepto, linea.Palabras[desde]) : null;
        }
        if (desde + p.Palabras <= linea.Palabras.Count && Interpretar(p.Campo, Unir(linea.Palabras, desde, p.Palabras)) is { } exacto) return (exacto, linea.Palabras[desde]);
        for (var n = MaxPalabrasValor; n >= 1; n--)
            if (desde + n <= linea.Palabras.Count && Interpretar(p.Campo, Unir(linea.Palabras, desde, n)) is { } v) return (v, linea.Palabras[desde]);
        return null;
    }

    private static (string Valor, PalabraDocumento Palabra)? LeerDebajo(IReadOnlyList<Linea> lineas, int li, double x, PlantillaCampo p)
    {
        var ancla = lineas[li];
        for (var abajo = li + 1; abajo < lineas.Count && lineas[abajo].Pagina == ancla.Pagina && ancla.Y - lineas[abajo].Y <= Alcance(p.Campo, ancla.Alto); abajo++)
        {
            var palabras = lineas[abajo].Palabras;
            var k = Enumerable.Range(0, palabras.Count).Where(j => Math.Abs(palabras[j].X - x) <= 15).OrderBy(j => Math.Abs(palabras[j].X - x)).FirstOrDefault(-1);
            if (k < 0) continue;
            return LeerDerecha(lineas, abajo, k, p);
        }
        return null;
    }

    private static IEnumerable<(int Inicio, int Fin)> Apariciones(Linea linea, string ancla)
    {
        for (var i = 0; i < linea.Palabras.Count; i++)
        {
            var texto = "";
            for (var j = i; j < linea.Palabras.Count && j - i < MaxPalabrasAncla; j++)
            {
                texto = j == i ? Normalizar(linea.Palabras[j].Texto) : texto + " " + Normalizar(linea.Palabras[j].Texto);
                if (texto == ancla) { yield return (i, j); break; }
                if (!ancla.StartsWith(texto, StringComparison.Ordinal)) break;
            }
        }
    }

    // ---------- geometría ----------

    /// <summary>Palabras agrupadas en líneas (misma página y altura parecida), de arriba abajo y de izquierda a derecha.</summary>
    private static List<Linea> Lineas(DocumentoFacturaExtraido documento)
    {
        var lineas = new List<Linea>();
        foreach (var pagina in documento.Paginas)
        {
            var actuales = new List<(double Y, double Alto, List<PalabraDocumento> Palabras)>();
            foreach (var palabra in pagina.Palabras.Where(w => !string.IsNullOrWhiteSpace(w.Texto)).OrderByDescending(w => w.Y))
            {
                var tolerancia = Math.Max(2, palabra.Alto * 0.5);
                var i = actuales.FindIndex(l => Math.Abs(l.Y - palabra.Y) <= tolerancia);
                if (i >= 0) actuales[i].Palabras.Add(palabra);
                else actuales.Add((palabra.Y, Math.Max(palabra.Alto, 1), [palabra]));
            }
            lineas.AddRange(actuales.OrderByDescending(l => l.Y)
                .Select(l => new Linea(pagina.Numero, l.Y, l.Alto, l.Palabras.OrderBy(w => w.X).ToList())));
        }
        return lineas;
    }

    private static string Unir(IReadOnlyList<PalabraDocumento> palabras, int inicio, int n) =>
        string.Join(" ", palabras.Skip(inicio).Take(n).Select(w => w.Texto.Trim()));

    /// <summary>Rótulo comparable: minúsculas, sin acentos, sin los dos puntos finales y con un solo espacio.</summary>
    internal static string Normalizar(string texto) =>
        Regex.Replace(MotorReconocimientoFacturaCompra.QuitarDiacriticos(texto).ToLowerInvariant(), @"\s+", " ").Trim().TrimEnd(':', '.', ';').Trim();
}
