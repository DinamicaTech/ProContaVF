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
    public const string Nif = "nif", Numero = "numero", Fecha = "fecha", Total = "total";
    public const string Derecha = "derecha", Debajo = "debajo";
    public static IReadOnlyList<string> Campos { get; } = [Nif, Numero, Fecha, Total];

    private const int MaxPalabrasValor = 6, MaxPalabrasAncla = 3, MaxAncla = 120;

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
                for (var n = 1; n <= MaxPalabrasValor && i + n <= linea.Palabras.Count; n++)
                {
                    var texto = Unir(linea.Palabras, i, n);
                    if (Interpretar(campo, texto) is not { } leido || !Coincide(campo, leido, valor)) continue;
                    if (elegido is not null && !Contiene(linea.Palabras, i, n, elegido)) break;
                    foreach (var candidata in Candidatas(lineas, li, i, n, campo))
                    {
                        // Solo vale si, aplicada a este mismo PDF, devuelve este valor (el rótulo no lleva a otro sitio).
                        if (Leer(documento, candidata, lineas) is not { } relectura || !Coincide(campo, relectura, valor)) continue;
                        var puntos = (candidata.Posicion == Derecha ? 100 : 50) - Ocurrencias(lineas, candidata.Ancla) * 10 + candidata.Ancla.Length / 4;
                        if (puntos > mejorPuntos) (mejor, mejorPuntos) = (candidata, puntos);
                    }
                    break;
                }
        }
        return mejor;
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

    /// <summary>¿El valor leído es el mismo que el guardado? Fechas por día, importes por valor, NIF y número compactos.</summary>
    public static bool Coincide(string campo, string leido, string guardado)
    {
        var a = Interpretar(campo, leido);
        var b = Interpretar(campo, guardado) ?? (campo == Numero ? MotorReconocimientoFacturaCompra.CompactarNumeroFactura(guardado) : null);
        if (a is null || b is null) return false;
        return campo == Nif ? SinPrefijoPais(a) == SinPrefijoPais(b) : a == b;
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
            default:
                return null;
        }
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
        for (var arriba = li - 1; arriba >= 0 && lineas[arriba].Pagina == linea.Pagina && lineas[arriba].Y - linea.Y <= Math.Max(3 * linea.Alto, 30); arriba--)
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

    private static string? Leer(DocumentoFacturaExtraido documento, PlantillaCampo p, IReadOnlyList<Linea> lineas)
    {
        var apariciones = new List<(int Linea, int Inicio, int Fin)>();
        for (var li = 0; li < lineas.Count; li++)
            if (lineas[li].Pagina == p.Pagina)
                apariciones.AddRange(Apariciones(lineas[li], p.Ancla).Select(a => (li, a.Inicio, a.Fin)));
        foreach (var (li, inicio, fin) in apariciones.OrderBy(a => Math.Abs(lineas[a.Linea].Palabras[a.Inicio].X - p.X) + Math.Abs(lineas[a.Linea].Y - p.Y)))
        {
            var leido = p.Posicion == Derecha ? LeerDerecha(lineas[li], fin + 1, p) : LeerDebajo(lineas, li, lineas[li].Palabras[inicio].X + p.DesplazamientoX, p);
            if (leido is not null) return leido;
        }
        return null;
    }

    private static string? LeerDerecha(Linea linea, int desde, PlantillaCampo p)
    {
        if (desde >= linea.Palabras.Count) return null;
        if (desde + p.Palabras <= linea.Palabras.Count && Interpretar(p.Campo, Unir(linea.Palabras, desde, p.Palabras)) is { } exacto) return exacto;
        for (var n = MaxPalabrasValor; n >= 1; n--)
            if (desde + n <= linea.Palabras.Count && Interpretar(p.Campo, Unir(linea.Palabras, desde, n)) is { } v) return v;
        return null;
    }

    private static string? LeerDebajo(IReadOnlyList<Linea> lineas, int li, double x, PlantillaCampo p)
    {
        var ancla = lineas[li];
        for (var abajo = li + 1; abajo < lineas.Count && lineas[abajo].Pagina == ancla.Pagina && ancla.Y - lineas[abajo].Y <= Math.Max(3 * ancla.Alto, 30); abajo++)
        {
            var palabras = lineas[abajo].Palabras;
            var k = Enumerable.Range(0, palabras.Count).Where(j => Math.Abs(palabras[j].X - x) <= 15).OrderBy(j => Math.Abs(palabras[j].X - x)).FirstOrDefault(-1);
            if (k < 0) continue;
            var linea = lineas[abajo];
            return LeerDerecha(linea, k, p);
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
