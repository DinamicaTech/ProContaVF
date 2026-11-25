using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// PDF justificante de la AEAT inventado para las pruebas: la hoja «Información de la presentación» y una hoja del modelo con los números de
/// casilla en News Gothic y los importes en Nimbus Sans, en la misma línea y con la casilla a la izquierda, como los PDF reales. Datos sintéticos.
/// </summary>
internal static class JustificanteSintetico
{
    /// <summary>Un texto de la hoja: «D» dato (Nimbus Sans) o «C» número de casilla (News Gothic), con su posición.</summary>
    public sealed record Texto(char Fuente, double X, double Y, string Valor);

    public static Texto D(double x, double y, string v) => new('D', x, y, v);
    public static Texto C(double x, double y, string v) => new('C', x, y, v);

    /// <summary>Primera hoja con los datos de la presentación.</summary>
    public static IReadOnlyList<Texto> Cabecera(string modelo, string justificante, string csv, string fecha = "20-10-2026 a las 10:15:00", decimal? importe = null,
        string nifPresentador = "A00000000") =>
    [
        D(160, 771, "INFORMACIÓN DE LA PRESENTACIÓN DE LA DECLARACIÓN"), D(300, 731, $"Modelo {modelo}"),
        D(62, 683, $"Presentación realizada el: {fecha}"),
        D(62, 663, $"Expediente/Referencia (nº registro asignado): 2026{modelo}99999999X"),
        D(62, 643, $"Código Seguro de Verificación: {csv}"),
        D(62, 623, $"Número de justificante: {justificante}"),
        D(62, 553, $"NIF Presentador: {nifPresentador}"), D(62, 533, "Apellidos y Nombre / Razón social: GESTORIA SINTETICA SL"), D(62, 513, "En calidad de: Colaborador"),
        .. importe is decimal i ? new[] { D(42, 488, "INGRESAR"), D(42, 475, $"NRC: {justificante}ABCDEFGHI IMPORTE: {i.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES"))}") }
            : [D(42, 488, "NEGATIVA/SIN ACTIVIDAD/RESULTADO CERO")],
    ];

    /// <summary>Hoja del modelo: ejercicio, periodo y NIF arriba, y cada casilla con su importe en una línea.</summary>
    public static IReadOnlyList<Texto> Hoja(int ejercicio, string periodo, string nif, params (string Casilla, string Importe)[] casillas)
    {
        var r = new List<Texto> { D(422, 721, ejercicio.ToString(CultureInfo.InvariantCulture)), D(540, 721, periodo), D(67, 627, nif) };
        var y = 560.0;
        foreach (var (c, i) in casillas)
        {
            r.Add(C(460, y + 1.5, c));
            r.Add(D(521, y, i));
            y -= 14;
        }
        return r;
    }

    /// <summary>El PDF con esas hojas (fuentes sin incrustar, con su nombre, que es lo que mira el lector).</summary>
    public static byte[] Pdf(params IReadOnlyList<Texto>[] hojas)
    {
        var objs = new List<string>();
        var anchos = string.Join(" ", Enumerable.Repeat("556", 224));
        objs.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objs.Add("");
        objs.Add($"<< /Type /Font /Subtype /TrueType /BaseFont /NimbusSanL-Regu /FontDescriptor 5 0 R /FirstChar 32 /LastChar 255 /Widths [{anchos}] /Encoding /WinAnsiEncoding >>");
        objs.Add($"<< /Type /Font /Subtype /TrueType /BaseFont /NewsGothicStd /FontDescriptor 6 0 R /FirstChar 32 /LastChar 255 /Widths [{anchos}] /Encoding /WinAnsiEncoding >>");
        objs.Add("<< /Type /FontDescriptor /FontName /NimbusSanL-Regu /Flags 32 /FontBBox [0 -200 1000 900] /ItalicAngle 0 /Ascent 900 /Descent -200 /CapHeight 700 /StemV 80 >>");
        objs.Add("<< /Type /FontDescriptor /FontName /NewsGothicStd /Flags 32 /FontBBox [0 -200 1000 900] /ItalicAngle 0 /Ascent 900 /Descent -200 /CapHeight 700 /StemV 80 >>");
        var paginas = new List<int>();
        foreach (var hoja in hojas)
        {
            var sb = new StringBuilder();
            foreach (var t in hoja)
                sb.Append(CultureInfo.InvariantCulture, $"BT /{(t.Fuente == 'D' ? "F1" : "F2")} 9 Tf {t.X} {t.Y} Td ({t.Valor.Replace("(", "\\(").Replace(")", "\\)")}) Tj ET\n");
            var contenido = sb.ToString();
            objs.Add($"<< /Length {Encoding.Latin1.GetByteCount(contenido)} >>\nstream\n{contenido}endstream");
            var idContenido = objs.Count;
            objs.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {idContenido} 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>");
            paginas.Add(objs.Count);
        }
        objs[1] = $"<< /Type /Pages /Kids [{string.Join(" ", paginas.Select(k => $"{k} 0 R"))}] /Count {paginas.Count} >>";
        using var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.4\n");
        var posiciones = new List<long>();
        for (var i = 0; i < objs.Count; i++) { posiciones.Add(ms.Position); W($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = ms.Position;
        W($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var p in posiciones) W($"{p:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
