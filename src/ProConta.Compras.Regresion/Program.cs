using System.Globalization;
using System.Text;
using ProConta.Compras.Integraciones;
using ProConta.Compras.Reconocimiento;

namespace ProConta.Compras.Regresion;

/// <summary>
/// Regresión del reconocimiento sobre el corpus real. Uso:
///   ProConta.Compras.Regresion --corpus &lt;carpeta con 2025\, 2026\…&gt; --verdad &lt;FacturasCompra.csv&gt; --sociedades &lt;Sociedades.csv&gt; --salida &lt;informe.txt&gt;
/// Los CSV son la exportación de las tablas FacturasCompra y Sociedades de la copia de pruebas (fechas mm/dd/yy).
/// Cada PDF se empareja con su fila grabada por MD5. El histórico del proveedor que se da al motor excluye
/// la propia factura, para que no se reconozca copiando su número grabado.
/// </summary>
public static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task<int> Main(string[] args)
    {
        var a = LeerArgumentos(args);
        if (!a.TryGetValue("corpus", out var corpus) || !a.TryGetValue("verdad", out var verdad) || !a.TryGetValue("salida", out var salida))
        {
            Console.Error.WriteLine("Uso: --corpus <carpeta> --verdad <FacturasCompra.csv> [--sociedades <Sociedades.csv>] --salida <informe.txt> [--vies]");
            return 2;
        }
        var filas = Csv.Leer(verdad).ToList();
        var nifsSociedades = a.TryGetValue("sociedades", out var soc) ? Csv.Leer(soc).Select(f => f.Get("CIF")).Where(c => c != "").ToArray() : [];
        var porMd5 = filas.Where(f => f.Get("MD5") != "").GroupBy(f => f.Get("MD5").ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var servicio = new ServicioReconocimientoFacturaCompra(new ExtractorPdfPig());
        var vies = a.ContainsKey("vies");

        var pdfs = Directory.EnumerateFiles(corpus, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var lineas = new List<Linea>();
        foreach (var pdf in pdfs)
        {
            var md5 = ServicioReconocimientoFacturaCompra.CalcularMd5(pdf);
            porMd5.TryGetValue(md5, out var fila);
            var rel = Path.GetRelativePath(corpus, pdf).Replace('/', '\\');
            // Ruta con la forma de producción: ...\<ejercicio>\Proveedores\<carpeta>\<fichero>.
            var partes = rel.Split('\\');
            var rutaMotor = partes.Length >= 2 ? $@"X:\Datos\Sociedad\{partes[0]}\Proveedores\{string.Join('\\', partes[1..])}" : rel;
            var contexto = Contexto(filas, fila, rutaMotor, nifsSociedades, vies);
            FacturaReconocida? r = null;
            string error = "";
            try
            {
                r = await servicio.ReconocerAsync(pdf, contexto);
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            lineas.Add(new Linea(rel, fila, r, error, rutaMotor));
        }
        Escribir(salida, lineas);
        Console.WriteLine(Resumen(lineas));
        File.WriteAllText(Path.ChangeExtension(salida, ".resumen.txt"), Resumen(lineas), new UTF8Encoding(true));
        return 0;
    }

    /// <summary>Contexto como lo cargará el repositorio: proveedores por carpeta, NIF, histórico de números (sin la propia factura).</summary>
    private static ContextoProcesamiento Contexto(List<Fila> filas, Fila? actual, string rutaMotor, string[] nifsSociedades, bool vies)
    {
        var idActual = actual?.Get("IdRegistro");
        var proveedores = filas.Where(f => f.Get("CuentaContableProveedor") != "" && f.Get("Carpeta") != "" && f.Get("IdRegistro") != idActual)
            .Select(f => (Cuenta: f.Get("CuentaContableProveedor"), Nif: f.Get("NIF"), Alias: f.Get("Carpeta"))).Distinct().ToList();
        var proveedor = ElegirProveedor(rutaMotor, proveedores);
        var historico = proveedor is null ? [] : filas
            .Where(f => f.Get("CuentaContableProveedor") == proveedor.Value.Cuenta && f.Get("IdRegistro") != idActual)
            .ToList();
        var numeros = historico.Where(f => f.Get("NumeroFactura").Trim() != "")
            .OrderByDescending(f => Fecha(f.Get("Fecha")) ?? DateTime.MinValue).ThenByDescending(f => int.Parse(f.Get("IdRegistro"), Inv))
            .Take(20).Select(f => f.Get("NumeroFactura")).ToArray();
        var aceptadas = historico.Count(f => f.Get("NumeroFactura") != "" && f.Get("Fecha") != "" && f.Get("ImporteTotal") != "");
        return new ContextoProcesamiento
        {
            Configuracion = new ConfiguracionReconocimiento { SociedadTieneVies = vies },
            FacturasAceptadasProveedor = aceptadas,
            Reconocimiento = new ContextoReconocimientoFacturaCompra
            {
                NifsProveedoresConocidos = proveedores.Select(p => p.Nif).Where(n => n != "").Distinct().ToArray(),
                NifsSociedades = nifsSociedades,
                NifsPorAliasProveedor = proveedores.Where(p => p.Nif != "").GroupBy(p => p.Alias, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Nif, StringComparer.OrdinalIgnoreCase),
                NumerosFacturaProveedor = numeros,
                SociedadTieneVies = vies
            }
        };
    }

    /// <summary>Proveedor cuya carpeta coincide de forma unívoca con un tramo de la ruta (como el repositorio del motor VB).</summary>
    private static (string Cuenta, string Nif, string Alias)? ElegirProveedor(string ruta, List<(string Cuenta, string Nif, string Alias)> proveedores)
    {
        var partes = ruta.Split('\\', '/').Select(Normalizar).Where(p => p.Length >= 3).ToArray();
        var candidatos = proveedores.Select(p => (P: p, Puntos: partes.Max(parte => Puntuar(parte, Normalizar(p.Alias)))))
            .Where(c => c.Puntos >= 0.75m).OrderByDescending(c => c.Puntos).ToArray();
        if (candidatos.Length == 0 || (candidatos.Length > 1 && candidatos[0].Puntos == candidatos[1].Puntos && candidatos[0].P.Cuenta != candidatos[1].P.Cuenta)) return null;
        return candidatos[0].P;
    }

    private static decimal Puntuar(string ruta, string alias)
        => ruta == alias ? 1m : alias.Length > 0 && (ruta.Contains(alias, StringComparison.Ordinal) || alias.Contains(ruta, StringComparison.Ordinal)) ? 0.85m : 0m;

    private static string Normalizar(string valor)
    {
        var sb = new StringBuilder();
        foreach (var c in valor.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    internal static DateTime? Fecha(string s)
        => DateTime.TryParseExact(s, ["MM/dd/yy HH:mm:ss", "MM/dd/yyyy HH:mm:ss", "MM/dd/yy", "dd/MM/yyyy"], Inv, DateTimeStyles.None, out var d) ? d.Date : null;

    internal static decimal? Importe(string s) => decimal.TryParse(s, NumberStyles.Number, Inv, out var d) ? d : null;

    private sealed record Linea(string Ruta, Fila? Fila, FacturaReconocida? R, string Error, string RutaMotor);

    private static readonly string[] Campos = ["Total", "Base", "Iva", "Nif", "Fecha", "Numero"];

    /// <summary>¿Coincide el campo? null si no hay valor grabado con el que comparar.</summary>
    private static bool? Correcto(Linea l, string campo)
    {
        if (l.Fila is null || l.R is null) return null;
        var f = l.Fila;
        var r = l.R.Lectura;
        bool? Imp(string col, decimal? rec)
        {
            var bd = Importe(f.Get(col));
            if (!bd.HasValue) return null;
            return rec.HasValue && Math.Abs(Math.Round(bd.Value, 2, MidpointRounding.AwayFromZero) - Math.Round(rec.Value, 2, MidpointRounding.AwayFromZero)) <= 0.02m;
        }
        switch (campo)
        {
            case "Total":
                var totalRec = r.Divisa != "EUR" && (Importe(f.Get("ImporteDolares")) ?? 0m) != 0m ? r.ImporteTotal : r.ImporteTotalEuros ?? r.ImporteTotal;
                if (r.Divisa != "EUR" && (Importe(f.Get("ImporteDolares")) ?? 0m) != 0m) return Imp("ImporteDolares", totalRec);
                return Imp("ImporteTotal", totalRec);
            case "Base": return Imp("ImporteBI", r.BaseImponible);
            case "Iva": return Imp("ImporteIVA", r.CuotaIva ?? (r.BaseImponible.HasValue ? 0m : null));
            case "Nif":
                var nif = MotorReconocimientoFacturaCompra.CompactarNif(f.Get("NIF"));
                return nif == "" ? null : MotorReconocimientoFacturaCompra.CompactarNif(r.NifProveedor) == nif;
            case "Fecha":
                var fecha = Fecha(f.Get("Fecha"));
                return fecha.HasValue ? r.FechaFactura == fecha : null;
            case "Numero":
                var num = MotorReconocimientoFacturaCompra.CompactarNumeroFactura(f.Get("NumeroFactura"));
                return num == "" ? null : MotorReconocimientoFacturaCompra.CompactarNumeroFactura(l.R.NumeroFactura) == num;
        }
        return null;
    }

    private static void Escribir(string salida, List<Linea> lineas)
    {
        var sb = new StringBuilder();
        string[] cab = ["IdRegistro", "Ruta", "Proveedor", "RequiereOCR",
            "TotalBD", "TotalReconocido", "TotalCorrecto", "BaseBD", "BaseReconocida", "BaseCorrecta", "IvaBD", "IvaReconocido", "IvaCorrecto",
            "NifBD", "NifReconocido", "NifCorrecto", "FechaBD", "FechaReconocida", "FechaCorrecta",
            "NumeroBD", "NumeroEjemplo", "NumeroReconocido", "NumeroCorrecto",
            "ConfianzaImportes", "ConfianzaNif", "ConfianzaFecha", "ConfianzaNumero", "ConfianzaGlobal", "RequiereRevision", "Fuente", "Divisa", "Advertencias"];
        sb.AppendLine(string.Join('\t', cab));
        foreach (var l in lineas)
        {
            var f = l.Fila;
            var r = l.R?.Lectura;
            string D(decimal? d) => d.HasValue ? Math.Round(d.Value, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv) : "";
            string B(bool? b) => b switch { true => "SI", false => "NO", _ => "" };
            string Fe(DateTime? d) => d?.ToString("yyyy-MM-dd", Inv) ?? "";
            string[] v = [f?.Get("IdRegistro") ?? "(sin fila grabada)", l.Ruta, r?.ProveedorIdentificado ?? "", B(r?.RequiereOcr),
                D(Importe(f?.Get("ImporteTotal") ?? "")), D(r?.ImporteTotal), B(Correcto(l, "Total")),
                D(Importe(f?.Get("ImporteBI") ?? "")), D(r?.BaseImponible), B(Correcto(l, "Base")),
                D(Importe(f?.Get("ImporteIVA") ?? "")), D(r?.CuotaIva), B(Correcto(l, "Iva")),
                f?.Get("NIF") ?? "", r?.NifProveedor ?? "", B(Correcto(l, "Nif")),
                Fe(Fecha(f?.Get("Fecha") ?? "")), Fe(r?.FechaFactura), B(Correcto(l, "Fecha")),
                f?.Get("NumeroFactura") ?? "", r?.NumeroFacturaEjemplo ?? "", l.R?.NumeroFactura ?? "", B(Correcto(l, "Numero")),
                D(r?.ConfianzaImportes), D(r?.ConfianzaNif), D(r?.ConfianzaFecha), D(r?.ConfianzaNumero), D(l.R?.Confianza),
                B(l.R?.RequiereRevision), l.R?.Fuente ?? "", r?.Divisa ?? "",
                l.Error != "" ? l.Error : string.Join(" | ", r?.Advertencias ?? [])];
            sb.AppendLine(string.Join('\t', v.Select(x => x.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '))));
        }
        File.WriteAllText(salida, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Resumen(List<Linea> lineas)
    {
        var sb = new StringBuilder();
        var conFila = lineas.Where(l => l.Fila is not null && l.R is not null).ToList();
        sb.AppendLine($"PDF: {lineas.Count} · con factura grabada (por MD5): {conFila.Count} · sin factura grabada: {lineas.Count(l => l.Fila is null)} · errores: {lineas.Count(l => l.Error != "")}");
        sb.AppendLine($"Requieren OCR (sin texto nativo): {lineas.Count(l => l.R?.Lectura.RequiereOcr == true)}");
        foreach (var grupo in new[] { ("Todos", conFila) }.Concat(conFila.GroupBy(l => l.Ruta.Split('\\')[0]).OrderBy(g => g.Key).Select(g => (g.Key, g.ToList()))))
        {
            sb.Append($"{grupo.Item1,-6} ({grupo.Item2.Count,4}):");
            foreach (var c in Campos)
            {
                var vals = grupo.Item2.Select(l => Correcto(l, c)).Where(v => v.HasValue).ToList();
                var ok = vals.Count(v => v == true);
                sb.Append($"  {c} {ok}/{vals.Count} ({(vals.Count == 0 ? 0 : 100.0 * ok / vals.Count):0.0} %)");
            }
            sb.AppendLine();
        }
        var todos = conFila.Count(l => Campos.All(c => Correcto(l, c) != false));
        sb.AppendLine($"Facturas con los seis campos correctos (o sin valor grabado): {todos}/{conFila.Count} ({(conFila.Count == 0 ? 0 : 100.0 * todos / conFila.Count):0.0} %)");
        var revision = conFila.Count(l => l.R!.RequiereRevision);
        var erroneasSinRevision = conFila.Count(l => !l.R!.RequiereRevision && Campos.Any(c => Correcto(l, c) == false));
        sb.AppendLine($"Marcadas para revisión: {revision} · con algún campo distinto y SIN marcar para revisión: {erroneasSinRevision}");
        return sb.ToString();
    }

    private static Dictionary<string, string> LeerArgumentos(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var clave = args[i][2..];
            d[clave] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
        }
        return d;
    }
}

/// <summary>Fila de un CSV exportado (cabecera en la primera línea, comillas dobles, saltos dentro de comillas).</summary>
internal sealed class Fila(Dictionary<string, string> valores)
{
    public string Get(string columna) => valores.TryGetValue(columna, out var v) ? v : "";
}

internal static class Csv
{
    public static IEnumerable<Fila> Leer(string ruta)
    {
        var registros = Parsear(File.ReadAllText(ruta)).ToList();
        if (registros.Count == 0) yield break;
        var cab = registros[0];
        foreach (var r in registros.Skip(1))
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cab.Count && i < r.Count; i++) d[cab[i]] = r[i];
            yield return new Fila(d);
        }
    }

    private static IEnumerable<List<string>> Parsear(string texto)
    {
        var campo = new StringBuilder();
        var fila = new List<string>();
        var comillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (comillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                else if (c == '"') comillas = false;
                else campo.Append(c);
            }
            else if (c == '"') comillas = true;
            else if (c == ',') { fila.Add(campo.ToString()); campo.Clear(); }
            else if (c == '\n')
            {
                fila.Add(campo.ToString().TrimEnd('\r')); campo.Clear();
                yield return fila; fila = [];
            }
            else campo.Append(c);
        }
        if (campo.Length > 0 || fila.Count > 0) { fila.Add(campo.ToString()); yield return fila; }
    }
}
