using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProConta.Documentos;

/// <summary>Presentación de un modelo ya grabada (PresentacionesAeat), para reconocer su justificante.</summary>
public sealed record PresentacionConocida(string Modelo, int Ejercicio, string Periodo, string Justificante, string? Csv, DateTime Fecha);

/// <summary>Tercero con NIF, para reconocerlo en un documento.</summary>
public sealed record TerceroConocido(int IdTercero, string Nif, string Nombre);

/// <summary>Lo que el lector necesita saber de la sociedad para proponer.</summary>
public sealed record DatosPropuesta(string? CifSociedad, Vinculo Empresa, IReadOnlyList<PresentacionConocida> Presentaciones, IReadOnlyList<TerceroConocido> Terceros);

/// <summary>
/// Lee el texto de un fichero y propone tipo, fecha, título y vínculos (documentos 2026-11-18, maqueta validada): el justificante de un modelo por su
/// número de justificante o su CSV, el modelo y el periodo citados, el tipo por palabras clave y un tercero por su NIF. Si parece una factura de
/// compra lo dice, porque esas se registran en Compras. No toca la BD ni el disco.
/// </summary>
public static class LectorPropuestas
{
    /// <summary>Modelos anuales: si el texto no dice el periodo, es «0A».</summary>
    private static readonly HashSet<string> _anuales = ["390", "190", "180", "193", "347", "200", "232", "296", "184"];

    private static readonly Regex _modelo = new(@"\bmodelo\s*(?:n[ºo.]?\s*)?(\d{3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex _ejercicio = new(@"\bejercicio\s*(?:fiscal)?\s*[:.]?\s*(20\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex _periodo = new(@"\bperiodo\s*[:.]?\s*([1-4]T|0A|[1-3]P|0[1-9]|1[0-2])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex _numero13 = new(@"(?<!\d)(\d{13})(?!\d)", RegexOptions.CultureInvariant);
    private static readonly Regex _csv = new(@"\b([A-Z0-9]{16})\b", RegexOptions.CultureInvariant);
    private static readonly Regex _nif = new(@"(?<![A-Z0-9])(?:ES)?([A-HJ-NP-SUVW]\d{7}[0-9A-J]|\d{8}[A-Z]|[XYZ]\d{7}[A-Z])(?![A-Z0-9])", RegexOptions.CultureInvariant);
    private static readonly Regex _fechaNumerica = new(@"(?<!\d)(\d{1,2})[/.-](\d{1,2})[/.-](20\d{2}|19\d{2})(?!\d)", RegexOptions.CultureInvariant);
    private static readonly Regex _fechaTexto = new(@"\b(\d{1,2})\s+de\s+(enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre)\s+(?:de|del)\s+(\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] _meses = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    public static Propuesta Proponer(string? texto, string nombreFichero, DatosPropuesta datos)
    {
        var plano = Normalizar(texto ?? "");
        var sinAcentos = QuitarAcentos(plano).ToLowerInvariant();
        var nombre = TituloDeFichero(nombreFichero);

        // 1. Justificante de un modelo ya presentado: el número de justificante (13 cifras) o el CSV de la presentación grabada.
        var presentacion = BuscarPresentacion(plano, datos.Presentaciones, out var dato);
        if (presentacion is not null && !EsNotificacion(sinAcentos))
        {
            var v = Vinculo.Modelo(presentacion.Modelo, presentacion.Ejercicio, presentacion.Periodo);
            return new Propuesta("MODELO", presentacion.Fecha.Date, $"{TituloModelo(presentacion.Modelo, presentacion.Ejercicio, presentacion.Periodo)} · justificante", [v],
                Confianza.Alta, $"El {dato} coincide con la presentación grabada del {v.Etiqueta}.");
        }

        // 2. Factura de compra: se registra en Compras.
        if (EsFacturaCompra(sinAcentos, plano, datos.CifSociedad))
            return new Propuesta(null, BuscarFecha(plano), nombre, [], Confianza.FacturaCompra,
                "Parece una factura de compra (dice «factura», con base imponible o IVA y total, y el NIF de la empresa). Las facturas de compra se registran en Compras › Facturas recibidas.");

        // 3. Tipo por palabras clave, modelo citado y tercero por su NIF.
        var tipo = Tipo(sinAcentos);
        var modelo = ModeloCitado(plano);
        var tercero = BuscarTercero(plano, datos);
        var vinculos = new List<Vinculo>();
        var motivos = new List<string>();
        if (modelo is { } m)
        {
            var v = Vinculo.Modelo(m.Modelo, m.Ejercicio, m.Periodo);
            vinculos.Add(v);
            motivos.Add($"El texto cita el modelo {m.Modelo}, ejercicio {m.Ejercicio}{(m.Periodo == "0A" ? "" : $", periodo {m.Periodo}")}: revisa que sea el {v.Etiqueta}.");
            tipo ??= "MODELO";
        }
        if (tercero is not null)
        {
            vinculos.Add(new Vinculo(TiposEntidad.Tercero, tercero.IdTercero, null, tercero.Nombre));
            motivos.Add($"El NIF {tercero.Nif} del documento es el de {tercero.Nombre}.");
        }
        if (tipo is "ESCRITURA" or "ACTA" or "CERTIFICADO" && !vinculos.Any(v => v.TipoEntidad == TiposEntidad.Empresa))
            vinculos.Add(datos.Empresa);
        if (tipo is not null && motivos.Count == 0) motivos.Add($"Tipo deducido del texto («{PalabraTipo(tipo)}»). Revisa la fecha y el vínculo.");

        var fecha = BuscarFecha(plano);
        var titulo = Titulo(tipo, plano, modelo, nombre);
        var confianza = tipo is not null && tercero is not null ? Confianza.Alta
            : tipo is not null || vinculos.Count > 0 ? Confianza.Media
            : Confianza.Ninguna;
        if (confianza == Confianza.Ninguna)
            motivos.Add(plano.Length == 0
                ? "No tiene texto legible (una imagen o un PDF escaneado): indica el tipo, la fecha y a qué se vincula."
                : "No reconozco el tipo de documento: indica el tipo, la fecha y a qué se vincula.");
        return new Propuesta(tipo, fecha, titulo, vinculos, confianza, string.Join(" ", motivos));
    }

    // ---------- reconocimientos ----------

    private static PresentacionConocida? BuscarPresentacion(string plano, IReadOnlyList<PresentacionConocida> presentaciones, out string dato)
    {
        dato = "";
        if (presentaciones.Count == 0) return null;
        foreach (Match m in _numero13.Matches(plano))
            if (presentaciones.FirstOrDefault(p => p.Justificante == m.Groups[1].Value) is { } p)
            {
                dato = $"número de justificante {p.Justificante}";
                return p;
            }
        var mayus = plano.ToUpperInvariant();
        foreach (Match m in _csv.Matches(mayus))
            if (presentaciones.FirstOrDefault(p => !string.IsNullOrEmpty(p.Csv) && string.Equals(p.Csv, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) is { } p)
            {
                dato = $"código seguro de verificación {p.Csv}";
                return p;
            }
        return null;
    }

    private static bool EsNotificacion(string t) =>
        t.Contains("notificacion") || t.Contains("requerimiento") || t.Contains("propuesta de liquidacion") || t.Contains("providencia de apremio")
        || t.Contains("tramite de audiencia") || t.Contains("comunicacion de inicio") || t.Contains("acuerdo de") || t.Contains("diligencia de embargo")
        || t.Contains("resolucion");

    private static bool EsFacturaCompra(string t, string plano, string? cif)
    {
        if (!Regex.IsMatch(t, @"\bfactura\b") || t.Contains("modelo 303")) return false;
        if (!(t.Contains("base imponible") || Regex.IsMatch(t, @"\biva\b")) || !t.Contains("total")) return false;
        var propio = NifLimpio(cif);
        return propio.Length > 0 && NifsDe(plano).Contains(propio);
    }

    private static string? Tipo(string t)
    {
        if (EsNotificacion(t) && (t.Contains("agencia tributaria") || t.Contains("aeat") || t.Contains("hacienda") || t.Contains("tesoreria general")))
            return "NOTIFICACION";
        if (Regex.IsMatch(t, @"\bmodelo\s*\d{3}\b") && (t.Contains("autoliquidacion") || t.Contains("declaracion") || t.Contains("justificante de presentacion")))
            return "MODELO";
        if (Regex.IsMatch(t, @"\bescritura\s+de\b") || t.Contains("ante mi, ") || t.Contains("notario del ilustre colegio")) return "ESCRITURA";
        if (Regex.IsMatch(t, @"\bacta\s+de\b") || t.Contains("junta general")) return "ACTA";
        if (Regex.IsMatch(t, @"\bcertificad[oa]\b") || Regex.IsMatch(t, @"\bcertifica\b")) return "CERTIFICADO";
        if (Regex.IsMatch(t, @"\bcontrato\b") || Regex.IsMatch(t, @"\bpoliza\b") || t.Contains("arrendamiento")) return "CONTRATO";
        if (EsNotificacion(t)) return "NOTIFICACION";
        return null;
    }

    private static string PalabraTipo(string tipo) => tipo switch
    {
        "NOTIFICACION" => "notificación / requerimiento",
        "MODELO" => "modelo … declaración",
        "ESCRITURA" => "escritura de",
        "ACTA" => "acta de / junta general",
        "CERTIFICADO" => "certificado",
        "CONTRATO" => "contrato / póliza / arrendamiento",
        _ => tipo,
    };

    private static (string Modelo, int Ejercicio, string Periodo)? ModeloCitado(string plano)
    {
        var mm = _modelo.Match(plano);
        if (!mm.Success) return null;
        var me = _ejercicio.Match(plano);
        if (!me.Success) return null;
        var modelo = mm.Groups[1].Value;
        var anio = int.Parse(me.Groups[1].Value, CultureInfo.InvariantCulture);
        var mp = _periodo.Match(plano);
        var periodo = mp.Success ? mp.Groups[1].Value.ToUpperInvariant() : _anuales.Contains(modelo) ? "0A" : null;
        return periodo is null ? null : (modelo, anio, periodo);
    }

    private static TerceroConocido? BuscarTercero(string plano, DatosPropuesta datos)
    {
        if (datos.Terceros.Count == 0) return null;
        var propio = NifLimpio(datos.CifSociedad);
        var porNif = datos.Terceros.Where(t => NifLimpio(t.Nif).Length > 0).GroupBy(t => NifLimpio(t.Nif)).ToDictionary(g => g.Key, g => g.First());
        foreach (var nif in NifsDe(plano))
            if (nif != propio && porNif.TryGetValue(nif, out var t)) return t;
        return null;
    }

    /// <summary>NIF que aparecen en el texto, sin guiones, puntos ni espacios intermedios («B-12.345.678» cuenta).</summary>
    internal static IReadOnlyList<string> NifsDe(string plano)
    {
        var mayus = plano.ToUpperInvariant();
        var compacto = Regex.Replace(mayus, @"(?<=[A-Z0-9])[\-. ](?=[0-9A-Z])", m => m.Value == " " ? " " : "");
        var lista = new List<string>();
        foreach (var fuente in new[] { mayus, compacto })
            foreach (Match m in _nif.Matches(fuente))
                if (!lista.Contains(m.Groups[1].Value)) lista.Add(m.Groups[1].Value);
        return lista;
    }

    internal static string NifLimpio(string? nif)
    {
        var n = Regex.Replace((nif ?? "").ToUpperInvariant(), @"[^A-Z0-9]", "");
        return n.StartsWith("ES", StringComparison.Ordinal) && n.Length == 11 ? n[2..] : n;
    }

    internal static DateTime? BuscarFecha(string plano)
    {
        var candidatos = new List<(int Pos, DateTime Fecha)>();
        foreach (Match m in _fechaNumerica.Matches(plano))
            if (Fecha(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value) is { } f) candidatos.Add((m.Index, f));
        foreach (Match m in _fechaTexto.Matches(plano))
        {
            var mes = Array.IndexOf(_meses, m.Groups[2].Value.ToLowerInvariant().Replace("setiembre", "septiembre")) + 1;
            if (Fecha(m.Groups[3].Value, mes.ToString(CultureInfo.InvariantCulture), m.Groups[1].Value) is { } f) candidatos.Add((m.Index, f));
        }
        return candidatos.Count == 0 ? null : candidatos.OrderBy(c => c.Pos).First().Fecha;
    }

    private static DateTime? Fecha(string anio, string mes, string dia)
    {
        if (!int.TryParse(anio, out var a) || !int.TryParse(mes, out var m) || !int.TryParse(dia, out var d)) return null;
        if (a < 1990 || a > 2100 || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(a, m)) return null;
        return new DateTime(a, m, d);
    }

    // ---------- títulos ----------

    private static string TituloModelo(string modelo, int ejercicio, string periodo) =>
        Vinculo.PeriodoTexto(periodo) is { Length: > 0 } p ? $"{modelo} {p} {ejercicio}" : $"{modelo} {ejercicio}";

    private static string Titulo(string? tipo, string plano, (string Modelo, int Ejercicio, string Periodo)? modelo, string nombre)
    {
        string? frase = tipo switch
        {
            "ESCRITURA" => Frase(plano, "escritura"),
            "ACTA" => Frase(plano, "acta"),
            "CERTIFICADO" => Frase(plano, "certificado"),
            "CONTRATO" => Frase(plano, "contrato") ?? Frase(plano, "p[oó]liza", "Póliza"),
            "NOTIFICACION" => FraseNotificacion(plano),
            _ => null,
        };
        if (tipo == "NOTIFICACION" && modelo is { } mn) return $"{frase ?? "Notificación"} {TituloModelo(mn.Modelo, mn.Ejercicio, mn.Periodo)}";
        if (tipo == "MODELO" && modelo is { } mm) return TituloModelo(mm.Modelo, mm.Ejercicio, mm.Periodo);
        return frase ?? nombre;
    }

    /// <summary>«Escritura de ampliación de capital»: la palabra clave seguida de «de» y unas pocas palabras, hasta un corte natural.</summary>
    private static string? Frase(string plano, string palabra, string? texto = null)
    {
        var m = Regex.Match(plano, $@"\b{palabra}\s+de\s+((?:[\p{{L}}]+\s*){{1,7}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        var cortes = new HashSet<string> { "en", "entre", "que", "otorgada", "otorgado", "celebrada", "celebrado", "por", "numero", "número", "n", "con", "del", "a", "y", "el", "la", "para" };
        var palabras = new List<string>();
        foreach (var p in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (palabras.Count > 0 && cortes.Contains(p.ToLowerInvariant()) && p.ToLowerInvariant() is not ("de" or "la" or "del")) break;
            palabras.Add(p.ToLowerInvariant());
            if (palabras.Count >= 4) break;
        }
        while (palabras.Count > 0 && cortes.Contains(palabras[^1])) palabras.RemoveAt(palabras.Count - 1);
        if (palabras.Count == 0) return null;
        return (texto ?? char.ToUpperInvariant(palabra[0]) + palabra[1..]) + " de " + string.Join(' ', palabras);
    }

    private static string? FraseNotificacion(string plano)
    {
        var t = QuitarAcentos(plano).ToLowerInvariant();
        foreach (var (clave, texto) in new[]
                 {
                     ("propuesta de liquidacion", "Propuesta de liquidación"), ("requerimiento", "Requerimiento"), ("providencia de apremio", "Providencia de apremio"),
                     ("tramite de audiencia", "Trámite de audiencia"), ("comunicacion de inicio", "Comunicación de inicio"), ("diligencia de embargo", "Diligencia de embargo"),
                     ("resolucion", "Resolución"), ("acuerdo de", "Acuerdo"), ("notificacion", "Notificación"),
                 })
            if (t.Contains(clave)) return texto;
        return null;
    }

    /// <summary>Título a partir del nombre del fichero: sin extensión, con espacios en lugar de guiones bajos.</summary>
    public static string TituloDeFichero(string nombreFichero)
    {
        var n = Path.GetFileNameWithoutExtension(nombreFichero ?? "").Replace('_', ' ').Trim();
        n = Regex.Replace(n, @"\s+", " ");
        return n.Length == 0 ? "Documento" : n;
    }

    // ---------- texto ----------

    internal static string Normalizar(string texto) => Regex.Replace(texto.Replace(' ', ' '), @"\s+", " ").Trim();

    internal static string QuitarAcentos(string texto)
    {
        var d = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
