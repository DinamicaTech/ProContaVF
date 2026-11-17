using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ProConta.Contabilidad.Impuestos;

/// <summary>
/// Lo que se lee de los datos fiscales del Impuesto sobre Sociedades de la AEAT (contabilidad/procesos/impuesto-sociedades): la página de la
/// Sede guardada como HTML. Solo cifras: el fichero no se guarda.
/// </summary>
/// <param name="Nif">NIF de la sociedad (tabla de datos identificativos).</param>
/// <param name="Ejercicio">Ejercicio del título («Datos Fiscales Impuesto Sociedades 2025»).</param>
/// <param name="Retenciones">Pagos fraccionados (202) y retenciones soportadas (193, 180…), sin cuenta asignada.</param>
/// <param name="BasesNegativas">Bases negativas pendientes de compensar; null si la página no trae la tabla.</param>
/// <param name="ResultadoPresentado">Casilla 621 de la última declaración del ejercicio ya presentada; null si no hay.</param>
/// <param name="ArrendamientoViviendas">Si la sociedad tiene el carácter «Entidad dedicada al arrendamiento de viviendas» (null: la página no lo dice).</param>
/// <param name="Avisos">Lo que no se ha podido leer, para que el operador lo escriba a mano.</param>
public sealed record DatosFiscalesAeat(string? Nif, int? Ejercicio, IReadOnlyList<RetencionAeat> Retenciones, decimal? BasesNegativas,
    decimal? ResultadoPresentado, bool? ArrendamientoViviendas, IReadOnlyList<string> Avisos);

/// <summary>
/// Lector de la página «Consulta de Datos Fiscales Impuesto Sociedades» de la Sede de la AEAT guardada como HTML. Busca cada bloque por el
/// identificador de su tabla (TablaPf, TablaRe…, TablaAr, TablaCbi, TablaDec, TablamD2) y cada columna por su cabecera, no por su posición.
/// Lo que no encuentra lo dice en <see cref="DatosFiscalesAeat.Avisos"/>. Las tablas que declara la propia sociedad (190) no son retenciones soportadas.
/// </summary>
public static class LectorDatosFiscalesAeat
{
    public const string Modelo202 = "202";
    public const string Modelo193 = "193";
    public const string Modelo180 = "180";

    /// <summary>Página de ayuda de la Sede con el enlace a la consulta de datos fiscales (Configuración general, si está vacía).</summary>
    public const string UrlDefecto = "https://sede.agenciatributaria.gob.es/Sede/ayuda/consultas-informaticas/impuesto-sobre-sociedades-ayuda-tecnica/consulta-datos-fiscales-sociedades.html";

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly Regex Tabla = new(@"<table\b[^>]*\bid\s*=\s*""(?<id>[^""]+)""[^>]*>(?<c>.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Fila = new(@"<tr\b[^>]*>(?<c>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Celda = new(@"<t[dh]\b[^>]*>(?<c>.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Codigos = new(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Etiqueta = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Codigo = new(@"^[A-Za-z]{2,4}\d{3,6}$", RegexOptions.Compiled);
    private static readonly Regex Titulo = new(@"Datos\s+Fiscales\s+Impuesto\s+(?:sobre\s+)?Sociedades\s+(?<a>\d{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Charset = new(@"charset\s*=\s*[""']?(?<cs>[\w-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lee el fichero tal como lo guarda el navegador (la Sede lo sirve en ISO-8859-15).</summary>
    public static DatosFiscalesAeat Leer(byte[] contenido)
    {
        var ascii = Encoding.ASCII.GetString(contenido, 0, Math.Min(contenido.Length, 4096));
        var cs = Charset.Match(ascii) is { Success: true } m ? m.Groups["cs"].Value.ToLowerInvariant() : "utf-8";
        var tieneBom = contenido.Length >= 3 && contenido[0] == 0xEF && contenido[1] == 0xBB && contenido[2] == 0xBF;
        // Latin1 lee igual que ISO-8859-15 todo lo que trae la página (letras con tilde y eñes).
        var texto = tieneBom || cs is "utf-8" or "utf8" ? new UTF8Encoding(false).GetString(contenido) : Encoding.Latin1.GetString(contenido);
        return LeerHtml(texto);
    }

    public static DatosFiscalesAeat LeerHtml(string html)
    {
        var avisos = new List<string>();
        // Los scripts y estilos de la Sede no cuentan (pueden llevar «<table» dentro de un texto).
        html = Codigos.Replace(html, " ");
        var tablas = Tabla.Matches(html).ToDictionary(m => m.Groups["id"].Value, m => Filas(m.Groups["c"].Value), StringComparer.OrdinalIgnoreCase);
        int? ejercicio = Titulo.Match(Limpiar(html.Length > 200_000 ? html[..200_000] : html)) is { Success: true } t ? int.Parse(t.Groups["a"].Value, CultureInfo.InvariantCulture) : null;
        if (tablas.Count == 0 || ejercicio is null)
            return new(null, ejercicio, [], null, null, null,
                ["No parece la página de datos fiscales del Impuesto sobre Sociedades de la AEAT. Guárdala desde la Sede con «Guardar página como…» (HTML) o escribe las cifras a mano."]);

        string? nif = null;
        if (tablas.TryGetValue("TablaNif", out var tn))
            nif = tn.SelectMany(f => f).Select(c => Regex.Match(c, @"NIF:\s*(?<n>[A-Z0-9]{8,9})\b", RegexOptions.IgnoreCase)).FirstOrDefault(x => x.Success)?.Groups["n"].Value.ToUpperInvariant();

        var retenciones = new List<RetencionAeat>();
        if (tablas.TryGetValue("TablaPf", out var pf))
        {
            var (cab, datos) = Separar(pf);
            int iImp = Columna(cab, "Importe"), iPer = Columna(cab, "Periodo");
            if (iImp < 0) avisos.Add("No encuentro el importe de los pagos fraccionados: escríbelos a mano.");
            else
                foreach (var f in datos)
                    if (Importe(f, iImp) is decimal v && v != 0m)
                        retenciones.Add(new(Modelo202, null, $"Pago fraccionado {Valor(f, iPer)}".Trim(), v));
        }
        else avisos.Add("La página no trae pagos fraccionados (202).");

        foreach (var (id, filas) in tablas.Where(kv => kv.Key.StartsWith("TablaRe", StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Retenciones(filas, Modelo193, "NIF Declarante", "Nombre Declarante", retenciones, avisos, id);
        if (tablas.TryGetValue("TablaAr", out var ar))
            Retenciones(ar, Modelo180, "NIF Pagador", "Nombre Pagador", retenciones, avisos, "TablaAr");

        decimal? bases = null;
        if (tablas.TryGetValue("TablaCbi", out var cbi))
        {
            var (cab, datos) = Separar(cbi);
            var i = Columna(cab, "Importe");
            bases = i < 0 ? null : datos.Sum(f => Importe(f, i) ?? 0m);
            if (i < 0) avisos.Add("No encuentro el importe de las bases negativas pendientes: escríbelas a mano.");
        }

        decimal? presentado = null;
        if (tablas.TryGetValue("TablaDec", out var dec))
        {
            var (cab, datos) = Separar(dec);
            int i621 = Columna(cab, "621"), iFecha = Columna(cab, "Fecha");
            var ultima = datos.Where(f => Importe(f, i621) is not null)
                .OrderBy(f => DateTime.TryParseExact(Valor(f, iFecha), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue)
                .LastOrDefault();
            if (ultima is not null) presentado = Importe(ultima, i621);
        }

        bool? viviendas = null;
        if (tablas.TryGetValue("TablamD2", out var car))
            viviendas = car.SelectMany(f => f).Any(c => c == "00038" || c.Contains("arrend. de viviendas", StringComparison.OrdinalIgnoreCase)
                || c.Contains("arrendamiento de viviendas", StringComparison.OrdinalIgnoreCase));

        return new(nif, ejercicio, retenciones, bases, presentado, viviendas, avisos);
    }

    /// <summary>
    /// Asigna a cada retención de la AEAT la 473 que le corresponde: el pago fraccionado a la 473 cuyo nombre habla de pagos a cuenta o
    /// fraccionados; las demás, a la 473 con más palabras del pagador en su nombre o en el de sus pagadores (las 43 y 44 de sus asientos) y,
    /// si no, a la única con ese mismo saldo. Si dos cuentas empatan, se deja sin asignar para que elija el operador.
    /// </summary>
    public static IReadOnlyList<RetencionAeat> Asignar(IReadOnlyList<RetencionAeat> aeat, IReadOnlyList<SaldoCuentaImpuesto> cuentas)
    {
        var c473 = cuentas.Where(c => CalculoImpuestoSociedades.EsRetencion(c.Cuenta)).ToList();
        var r = new List<RetencionAeat>();
        foreach (var a in aeat)
        {
            if (!string.IsNullOrWhiteSpace(a.Cuenta)) { r.Add(a); continue; }
            string? cuenta;
            if (a.Modelo == Modelo202)
                cuenta = Unica(c473.Where(c => EsPagoACuenta(c.Nombre)).Select(c => c.Cuenta));
            else
            {
                var palabras = Palabras(a.Nombre);
                var puntos = c473.Select(c => (c.Cuenta, Puntos: Palabras(c.Nombre).Concat((c.Pagadores ?? []).SelectMany(Palabras)).Distinct().Count(palabras.Contains)))
                    .Where(x => x.Puntos > 0).ToList();
                var max = puntos.Count == 0 ? 0 : puntos.Max(x => x.Puntos);
                cuenta = Unica(puntos.Where(x => x.Puntos == max).Select(x => x.Cuenta));
            }
            cuenta ??= Unica(c473.Where(c => c.Saldo == a.Importe).Select(c => c.Cuenta));
            r.Add(a with { Cuenta = cuenta });
        }
        return r;
    }

    private static readonly HashSet<string> Vacias = new(StringComparer.OrdinalIgnoreCase)
    {
        "SL", "SLU", "SA", "SAU", "SCP", "CB", "SOCIEDAD", "LIMITADA", "ANONIMA", "BANCO", "RETENCION", "RETENCIONES", "ALQUILER", "LOCAL", "LOCALES",
        "PAGO", "PAGOS", "CTA", "CUENTA", "IMP", "SOCIEDADES", "DEL", "DE", "LA", "LOS", "LAS", "Y", "GROUP", "GRUPO",
    };

    /// <summary>Palabras significativas de un nombre, en mayúsculas y sin tildes (los números cuentan: «RENTA 4»).</summary>
    public static IReadOnlyList<string> Palabras(string? nombre)
    {
        var sin = new string((nombre ?? "").Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToUpperInvariant();
        return Regex.Split(sin, @"[^A-Z0-9]+").Where(p => p.Length >= 3 || (p.Length > 0 && p.All(char.IsAsciiDigit)))
            .Where(p => !Vacias.Contains(p)).Distinct().ToList();
    }

    private static bool EsPagoACuenta(string nombre)
    {
        var n = nombre.ToUpperInvariant();
        return n.Contains("FRACCION", StringComparison.Ordinal) || (n.Contains("PAGO", StringComparison.Ordinal) && (n.Contains("CTA", StringComparison.Ordinal) || n.Contains("CUENTA", StringComparison.Ordinal)));
    }

    private static string? Unica(IEnumerable<string> cuentas)
    {
        var l = cuentas.Distinct(StringComparer.Ordinal).Take(2).ToList();
        return l.Count == 1 ? l[0] : null;
    }

    private static void Retenciones(List<List<string>> filas, string modelo, string colNif, string colNombre, List<RetencionAeat> destino, List<string> avisos, string tabla)
    {
        var (cab, datos) = Separar(filas);
        int iRet = Columna(cab, "Retenciones"), iNif = Columna(cab, colNif), iNom = Columna(cab, colNombre);
        if (iRet < 0) { avisos.Add($"No encuentro la columna de retenciones del modelo {modelo} ({tabla}): escríbelas a mano."); return; }
        foreach (var f in datos)
            if (Importe(f, iRet) is decimal v && v != 0m)
            {
                var n = Valor(f, iNif).ToUpperInvariant();
                destino.Add(new(modelo, n.Length == 0 ? null : n, Valor(f, iNom) is { Length: > 0 } nom ? nom : (n.Length > 0 ? n : $"Modelo {modelo}"), v));
            }
    }

    private static List<List<string>> Filas(string tabla) =>
        Fila.Matches(tabla).Select(f => Celda.Matches(f.Groups["c"].Value).Select(c => Limpiar(c.Groups["c"].Value)).ToList()).ToList();

    /// <summary>La cabecera (la primera fila que empieza por «Código») y las filas de datos (las que empiezan por un código como PF00001).</summary>
    private static (List<string> Cabecera, List<List<string>> Datos) Separar(List<List<string>> filas)
    {
        var cab = filas.FirstOrDefault(f => f.Count > 1 && f[0].StartsWith("Código", StringComparison.OrdinalIgnoreCase)) ?? [];
        return (cab, filas.Where(f => f.Count > 1 && Codigo.IsMatch(f[0])).ToList());
    }

    private static int Columna(List<string> cabecera, string texto) => cabecera.FindIndex(c => c.Contains(texto, StringComparison.OrdinalIgnoreCase));

    private static string Valor(List<string> fila, int i) => i >= 0 && i < fila.Count ? fila[i].Trim() : "";

    private static decimal? Importe(List<string> fila, int i) =>
        decimal.TryParse(Valor(fila, i), NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, Es, out var v) ? v : null;

    private static string Limpiar(string html) => Regex.Replace(WebUtility.HtmlDecode(Etiqueta.Replace(html, " ")), @"\s+", " ").Trim();
}
