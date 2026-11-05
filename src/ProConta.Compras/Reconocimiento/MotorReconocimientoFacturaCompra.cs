using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProConta.Compras.Reconocimiento;

/// <summary>
/// Motor determinista que reconoce los campos de una factura de compra a partir de un documento ya extraído.
/// Es el port a C# del motor VB del desarrollo anterior (MotorReconocimientoFacturaCompra.vb), con sus reglas,
/// puntuaciones y umbrales sin cambios (compras/reconocimiento 2026-10-31 16:54 y 2026-11-05 11:24).
/// Las reglas de familias documentales concretas viven en <see cref="FamiliasDocumentales"/>.
/// No accede a ficheros, red ni base de datos.
/// </summary>
public sealed class MotorReconocimientoFacturaCompra
{
    private static readonly Regex PatronNif = new(@"(?<![A-Z0-9])(?:[A-Z][0-9]{7}[A-Z0-9]|[0-9]{8}[A-Z]|[A-Z]{2}[A-Z0-9]{8,12})(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Nombres de mes en español, catalán e inglés (completos o abreviados), para las fechas con el mes escrito y sin «de».</summary>
    private const string MesNombre = @"(?:enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|gener|febrer|mar[cç]|maig|juny|juliol|agost|setembre|novembre|desembre|january|february|march|april|may|june|july|august|september|october|november|december|ene|feb|mar|abr|ago|sept?|oct|nov|dic|jan|apr|jun|jul|aug|dec)\.?";
    private static readonly Regex PatronFecha = new(@"(?<![0-9])(?:[0-3]?[0-9][./-][01]?[0-9][./-](?:(?:19|20)?[0-9]{2})|(?:19|20)[0-9]{2}[./-][01]?[0-9][./-][0-3]?[0-9]|[0-3]?[0-9](?:\s+de\s+|[./-])[A-ZÀ-ÿ]+(?:\s+de\s+|[./-])(?:19|20)[0-9]{2}" +
        @"|[0-3]?[0-9]\s+" + MesNombre + @"\s+(?:19|20)[0-9]{2}|(?<![A-Za-z])" + MesNombre + @"\s+[0-3]?[0-9],\s*(?:19|20)[0-9]{2})(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PatronImporte = new(@"(?<![0-9])[-+]?(?:[0-9]{1,3}(?:[.\s][0-9]{3})+|[0-9]+)[,.][0-9]{2,4}[-+]?(?![0-9])", RegexOptions.Compiled);
    private static readonly Regex PatronImporteMonetario = new(@"(?<![A-Z0-9])(?:[-+]?\s*[€$£]\s*[-+]?\s*(?:[0-9]{1,3}(?:[.,\s][0-9]{3})+|[0-9]+)(?:[,.][0-9]{1,6})?|[-+]?(?:[0-9]{1,3}(?:[.,\s][0-9]{3})+|[0-9]+)(?:[,.][0-9]{1,6})?\s*(?:€|EUR(?:O)?|USD|GBP)|[-+]?(?:[0-9]{1,3}(?:[.\s][0-9]{3})+|[0-9]+)[,.][0-9]{2,6}[-+]?)(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PatronTipoIva = new(@"(?<![0-9])([0-9]{1,2}(?:[,.][0-9]+)?)\s*%", RegexOptions.Compiled);
    private static readonly decimal[] TiposHabituales = [0.21m, 0.1m, 0.04m, 0.05m, 0m];

    /// <summary>Reconoce los campos principales de una factura. Sin efectos externos.</summary>
    public ResultadoReconocimientoFacturaCompra Reconocer(DocumentoFacturaExtraido documento, ContextoReconocimientoFacturaCompra contexto)
    {
        ArgumentNullException.ThrowIfNull(documento);
        ArgumentNullException.ThrowIfNull(contexto);

        var warnings = new List<string>();
        var text = NormalizarTexto(documento.TextoCompleto);
        var result = new ResultadoReconocimientoFacturaCompra { RequiereOcr = !ContieneTextoUtil(text) };

        if (result.RequiereOcr) warnings.Add("El PDF no contiene texto nativo útil; requiere OCR.");
        ReconocerMetadatosMonetarios(documento, text, result, warnings);

        var textoPrimeraFactura = text;
        if (result.PosiblesMultiplesFacturas)
            textoPrimeraFactura = documento.Paginas.Count > 1 ? documento.Paginas[0].Texto : LimitarAPrimeraFactura(text);

        var nif = BuscarNifProveedor(textoPrimeraFactura, contexto);
        result.NifLeidoDocumento = nif.Value;
        var proveedor = IdentificarProveedorPorRuta(documento, contexto);
        result.ProveedorIdentificado = proveedor.Nombre;
        if (nif.Confidence >= 0.7m)
        {
            result.NifProveedor = nif.Value;
            result.ConfianzaNif = nif.Confidence;
            if (proveedor.Nif != string.Empty && !proveedor.Nif.Equals(nif.Value, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"El CIF leído en la factura ({nif.Value}) difiere del CIF registrado para {proveedor.Nombre} ({proveedor.Nif}); se conserva el valor documental.");
        }
        else if (proveedor.Nif != string.Empty)
        {
            result.NifProveedor = proveedor.Nif;
            result.ConfianzaNif = 0.68m;
            result.NifAsignadoDesdeProveedor = true;
            warnings.Add(nif.Value == string.Empty
                ? $"No se ha leído un CIF fiable; se asigna {proveedor.Nif} al identificar unívocamente al proveedor {proveedor.Nombre}."
                : $"Se descarta el candidato fiscal de baja confianza {nif.Value} y se asigna {proveedor.Nif} al identificar al proveedor {proveedor.Nombre}.");
        }
        else
        {
            result.NifProveedor = nif.Value;
            result.ConfianzaNif = nif.Confidence;
        }
        if (string.IsNullOrWhiteSpace(result.NifLeidoDocumento) && FamiliasDocumentales.NifEnCabeceraGrafica(documento))
        {
            result.RequiereOcrParaNif = true;
            warnings.Add("El CIF del proveedor parece formar parte de una cabecera gráfica; requiere OCR para identificar el NIF.");
        }

        var invoiceNumber = ElegirMejor(
            ElegirMejor(ElegirMejor(BuscarNumeroFacturaPorGeometria(documento), BuscarNumeroFactura(textoPrimeraFactura)),
                        BuscarNumeroFacturaPorHistorico(textoPrimeraFactura, contexto.NumerosFacturaProveedor)),
            BuscarNumeroFacturaEnNombreArchivo(documento.Ruta, textoPrimeraFactura, contexto.NumerosFacturaProveedor));
        if (contexto.NumerosFacturaProveedor.Count == 0 && FamiliasDocumentales.NumeroSoloConHistorico(documento))
            invoiceNumber = (string.Empty, 0m);
        result.NumeroFactura = invoiceNumber.Value;
        result.ConfianzaNumero = invoiceNumber.Confidence;
        result.NumeroFacturaEjemplo = contexto.NumerosFacturaProveedor.FirstOrDefault(string.Empty);

        var invoiceDate = ElegirMejor(BuscarFechaFacturaPorGeometria(documento), BuscarFechaFactura(text, documento.Ruta));
        result.FechaFactura = invoiceDate.Value;
        result.ConfianzaFecha = invoiceDate.Confidence;

        ReconocerImportes(documento, text, result, warnings);
        if (result.ImporteTotal.HasValue) result.ImporteTotalDivisa = result.ImporteTotal;
        ReconocerRetencion(text, result);
        CompletarBaseEIva(documento, text, result, contexto, warnings);
        ElevarConfianzaPorReconciliacionAritmetica(text, result);
        if (result.ImporteRetencion.HasValue && result.ImporteTotal.HasValue && result.BaseImponible.HasValue && !result.CuotaIva.HasValue)
            result.CuotaIva = result.ImporteTotal.Value - result.BaseImponible.Value + result.ImporteRetencion.Value;
        var lowerText = QuitarDiacriticos(text).ToLowerInvariant();
        result.InversionSujetoPasivo = lowerText.Contains("inversion", StringComparison.Ordinal) &&
                                       lowerText.Contains("sujeto", StringComparison.Ordinal) &&
                                       lowerText.Contains("pasivo", StringComparison.Ordinal) &&
                                       (result.CuotaIva ?? 0m) == 0m;

        if (string.IsNullOrWhiteSpace(result.NifProveedor)) warnings.Add("No se ha identificado el NIF del proveedor.");
        if (string.IsNullOrWhiteSpace(result.NumeroFactura)) warnings.Add("No se ha identificado el número de factura.");
        if (!result.FechaFactura.HasValue) warnings.Add("No se ha identificado la fecha de factura.");
        if (!result.ImporteTotal.HasValue) warnings.Add("No se ha reconciliado el importe total.");
        result.Advertencias = warnings;
        return result;
    }

    /// <summary>Identifica moneda extranjera y señales de que un PDF agrupa más de una factura.</summary>
    private static void ReconocerMetadatosMonetarios(DocumentoFacturaExtraido documento, string text, ResultadoReconocimientoFacturaCompra result, ICollection<string> warnings)
    {
        var normalizado = QuitarDiacriticos(text).ToUpperInvariant();
        var lineasTotal = string.Join("\n", text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(linea => Regex.IsMatch(QuitarDiacriticos(linea), @"(?:total|amount\s+due|neto\s+a\s+pagar)", RegexOptions.IgnoreCase)));
        if (FamiliasDocumentales.EsRutaDe(documento, "ebay") || Regex.IsMatch(lineasTotal, "(?<![A-Z])GBP(?![A-Z])", RegexOptions.IgnoreCase) || lineasTotal.Contains('£'))
            result.Divisa = "GBP";
        else if (FamiliasDocumentales.EsRutaDe(documento, "PDFEscape") || Regex.IsMatch(lineasTotal, "(?<![A-Z])USD(?![A-Z])", RegexOptions.IgnoreCase) || lineasTotal.Contains('$'))
            result.Divisa = "USD";
        result.RequiereConversionDivisa = !result.Divisa.Equals("EUR", StringComparison.Ordinal);

        var numerosFactura = Regex.Matches(normalizado, @"(?:NUMERO\s+DE\s+FACTURA|INVOICE\s+(?:NUMBER|NO)|FACTURA\s+(?:N[Oº°]|NUMERO))", RegexOptions.IgnoreCase).Count;
        var totalesPendientes = Regex.Matches(normalizado, @"TOTAL\s+PENDIENTE", RegexOptions.IgnoreCase).Count;
        result.PosiblesMultiplesFacturas = numerosFactura >= 2 || totalesPendientes >= 2;
        if (result.PosiblesMultiplesFacturas) warnings.Add("El PDF parece contener más de una factura; requiere revisión antes de contabilizar.");
    }

    /// <summary>Extrae el porcentaje y el importe de una retención de IRPF explícita.</summary>
    private static void ReconocerRetencion(string text, ResultadoReconocimientoFacturaCompra result)
    {
        var patron = new Regex(@"(?:IMPORTE\s+)?(?:I\.?R\.?P\.?F\.?|RETENCI[OÓ]N)\s*\(?\s*([0-9]{1,2}(?:[,.][0-9]+)?)\s*%\s*\)?[^\r\n0-9+-]*([-+]?\s*(?:€\s*)?[0-9.]+(?:,[0-9]{1,6})?)", RegexOptions.IgnoreCase);
        var coincidencia = patron.Match(text);
        if (!coincidencia.Success) return;
        result.TipoRetencion = InterpretarNumeroFlexible(coincidencia.Groups[1].Value);
        result.ImporteRetencion = InterpretarImporte(coincidencia.Groups[2].Value);
    }

    /// <summary>Hay texto nativo útil cuando hay al menos diez caracteres alfanuméricos.</summary>
    internal static bool ContieneTextoUtil(string text)
        => !string.IsNullOrWhiteSpace(text) && text.Count(char.IsLetterOrDigit) >= 10;

    private static (T Value, decimal Confidence) ElegirMejor<T>((T Value, decimal Confidence) first, (T Value, decimal Confidence) second)
        => first.Confidence >= second.Confidence ? first : second;

    /// <summary>Normaliza saltos y compatibilidad Unicode sin destruir separadores fiscales.</summary>
    private static string NormalizarTexto(string? text)
        => (text ?? string.Empty).Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>Busca el NIF del emisor evitando el de las sociedades receptoras.</summary>
    private static (string Value, decimal Confidence) BuscarNifProveedor(string text, ContextoReconocimientoFacturaCompra context)
    {
        var compactText = Regex.Replace(text.ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
        var companyNifs = new HashSet<string>(context.NifsSociedades.Select(CompactarNif), StringComparer.OrdinalIgnoreCase);
        foreach (var companyNif in companyNifs.ToArray())
            companyNifs.Add(companyNif.StartsWith("ES", StringComparison.OrdinalIgnoreCase) ? companyNif[2..] : "ES" + companyNif);
        var best = string.Empty;
        var bestScore = 0;
        var strongIssuerLabel = false;

        // Una etiqueta fiscal acompañada por la razón social del emisor es más
        // significativa que un DNI de cliente que también figure en la factura.
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var upperLine = QuitarDiacriticos(line).ToUpperInvariant();
            var label = Regex.Match(upperLine, @"(?<![A-Z])(?:NIF|CIF|VAT)\s*[:.\-]?", RegexOptions.IgnoreCase);
            if (!label.Success) continue;
            var tail = upperLine[(label.Index + label.Length)..];
            foreach (Match match in Regex.Matches(tail, @"(?<![A-Z0-9])(?:ES[\s.\-]*)?[A-Z](?:[\s.\-]*[0-9]){7}[\s.\-]*[A-Z0-9](?![A-Z0-9])", RegexOptions.IgnoreCase))
            {
                var nif = CompactarNif(match.Value);
                if (!EsNifPlausible(nif) || companyNifs.Contains(nif)) continue;
                var prefix = upperLine[..label.Index];
                var score = EsNifEspanolValido(nif) ? 25 : 10;
                if (Regex.IsMatch(prefix, @"(?:S\.?\s*L\.?\s*U?\.?|S\.?\s*A\.?|SOCIEDAD|LIMITADA|ANONIMA)\s*[,.-]?\s*$", RegexOptions.IgnoreCase)) score += 50;
                if (score > bestScore)
                {
                    best = nif;
                    bestScore = score;
                    strongIssuerLabel = score >= 50 && Regex.IsMatch(nif, "^[AB][0-9]{8}$", RegexOptions.IgnoreCase);
                }
            }
        }

        foreach (var rawNif in context.NifsProveedoresConocidos)
        {
            var nif = CompactarNif(rawNif);
            if (!EsNifPlausible(nif) || companyNifs.Contains(nif)) continue;
            var position = compactText.IndexOf(nif, StringComparison.OrdinalIgnoreCase);
            if (position < 0) continue;
            var score = 10 + Math.Min(5, ContarOcurrencias(compactText, nif));
            if (!strongIssuerLabel) score += 100;
            if (score > bestScore)
            {
                best = nif;
                bestScore = score;
            }
        }

        if (best != string.Empty) return (best, strongIssuerLabel ? 0.94m : Math.Min(0.98m, 0.72m + bestScore / 100m));

        foreach (Match match in PatronNif.Matches(text.ToUpperInvariant()))
        {
            var nif = CompactarNif(match.Value);
            if (EsNifPlausible(nif) && !companyNifs.Contains(nif))
                return (nif, EsNifEspanolValido(nif) ? 0.78m : 0.45m);
        }
        return (string.Empty, 0m);
    }

    /// <summary>Descarta marcadores, nombres comerciales y tokens sin estructura fiscal mínima.</summary>
    internal static bool EsNifPlausible(string nif)
    {
        if (nif.StartsWith("ES", StringComparison.OrdinalIgnoreCase))
        {
            if (nif.Length != 11) return false;
            return Regex.IsMatch(nif, "^ES(?:[0-9]{8}[A-Z]|[A-Z][0-9]{7}[A-Z0-9])$", RegexOptions.IgnoreCase);
        }
        if (nif.Length < 8 || nif.Length > 14) return false;
        if (nif == "55555554" || nif == "55555555") return false;
        if (nif.All(char.IsDigit)) return false;
        var digits = nif.Count(char.IsDigit);
        if (digits < 5) return false;
        if (char.IsLetter(nif[0]) && nif.Length >= 3 && char.IsLetter(nif[1]) && char.IsLetter(nif[2])) return false;
        return true;
    }

    /// <summary>Valida el dígito o letra de control de NIF, NIE y CIF españoles.</summary>
    internal static bool EsNifEspanolValido(string valor)
    {
        var nif = CompactarNif(valor);
        if (nif.StartsWith("ES", StringComparison.OrdinalIgnoreCase)) nif = nif[2..];
        const string letras = "TRWAGMYFPDXBNJZSQVHLCKE";

        if (Regex.IsMatch(nif, "^[0-9]{8}[A-Z]$"))
        {
            var numero = int.Parse(nif[..8], CultureInfo.InvariantCulture);
            return nif[8] == letras[numero % 23];
        }
        if (Regex.IsMatch(nif, "^[XYZ][0-9]{7}[A-Z]$"))
        {
            var prefijo = "XYZ".IndexOf(nif[0]);
            var numero = int.Parse(prefijo.ToString(CultureInfo.InvariantCulture) + nif.Substring(1, 7), CultureInfo.InvariantCulture);
            return nif[8] == letras[numero % 23];
        }
        if (!Regex.IsMatch(nif, "^[ABCDEFGHJNPQRSUVW][0-9]{7}[0-9A-J]$")) return false;
        var suma = 0;
        for (var indice = 1; indice <= 7; indice++)
        {
            var digito = nif[indice] - '0';
            if (indice % 2 == 1)
            {
                var doble = digito * 2;
                suma += doble / 10 + doble % 10;
            }
            else suma += digito;
        }
        var control = (10 - suma % 10) % 10;
        const string controles = "JABCDEFGHI";
        return nif[8] == (char)('0' + control) || nif[8] == controles[control];
    }

    /// <summary>Limita el análisis fiscal al primer documento cuando un PDF contiene varias facturas.</summary>
    private static string LimitarAPrimeraFactura(string texto)
    {
        var marcadores = Regex.Matches(QuitarDiacriticos(texto), @"(?:numero\s+de\s+factura|invoice\s+(?:number|no))", RegexOptions.IgnoreCase);
        if (marcadores.Count < 2) return texto;
        return texto[..marcadores[1].Index];
    }

    /// <summary>Identifica el proveedor cuando la ruta contiene un alias (carpeta) asociado a un NIF registrado.</summary>
    private static (string Nombre, string Nif) IdentificarProveedorPorRuta(DocumentoFacturaExtraido documento, ContextoReconocimientoFacturaCompra contexto)
    {
        var ruta = (documento.Ruta ?? string.Empty).Replace('/', '\\');
        foreach (var item in contexto.NifsPorAliasProveedor.OrderByDescending(par => par.Key.Length))
            if (ruta.Contains("\\" + item.Key + "\\", StringComparison.OrdinalIgnoreCase)) return (item.Key, CompactarNif(item.Value));
        return (string.Empty, string.Empty);
    }

    public static string CompactarNif(string? nif) => Regex.Replace((nif ?? string.Empty).ToUpperInvariant(), "[^A-Z0-9]", string.Empty);

    private static int ContarOcurrencias(string source, string value)
    {
        var count = 0;
        var position = 0;
        while (position < source.Length)
        {
            position = source.IndexOf(value, position, StringComparison.OrdinalIgnoreCase);
            if (position < 0) break;
            count++;
            position += value.Length;
        }
        return count;
    }

    /// <summary>Busca un número junto a etiquetas habituales de factura.</summary>
    private static (string Value, decimal Confidence) BuscarNumeroFactura(string text)
    {
        string[] explicitPatterns =
        [
            @"FACTURA\s+NUM\.?\s*/\s*FACTURA\s+NOM\.?\s*:\s*([A-Z0-9][A-Z0-9./_-]*(?:\s+[A-Z0-9][A-Z0-9./_-]*)?)",
            @"NUMERO\s+DE\s+FACTURA\s*:?\s*([A-Z0-9][A-Z0-9./_-]*(?:\s+[A-Z0-9][A-Z0-9./_-]*)?)",
            @"INVOICE\s*(?:#|NO\.?|NUMBER)?\s*[:#-]?\s*([A-Z0-9][A-Z0-9./_-]*(?:\s+[A-Z0-9][A-Z0-9./_-]*)?)"
        ];
        var normalizedText = QuitarDiacriticos(text).ToUpperInvariant();
        foreach (var pattern in explicitPatterns)
            foreach (Match explicitMatch in Regex.Matches(normalizedText, pattern, RegexOptions.IgnoreCase))
            {
                var candidate = NormalizarNumeroTrasEtiqueta(explicitMatch.Groups[1].Value);
                if (EsNumeroFacturaPlausible(candidate)) return (candidate, 0.999m);
            }

        var airbnb = Regex.Match(text, @"(?<![A-Z0-9])AIUC[-\s]*[0-9]{8}[-\s]*ES[-\s]*[0-9]+", RegexOptions.IgnoreCase);
        if (airbnb.Success) return (CompactarNumeroFactura(airbnb.Value), 0.995m);

        var yearSeries = Regex.Match(text, @"(?<![0-9])((?:19|20)[0-9]{2}\s*/\s*[0-9]+\s*/\s*[0-9]+)(?![0-9])");
        if (yearSeries.Success) return (CompactarNumeroFactura(yearSeries.Groups[1].Value), 0.985m);

        var tableNumber = Regex.Match(text, @"(?:CLIENT\s+DATA\s+NUMERO\s+FULL)[^\r\n]*[\r\n]+\s*[A-Z0-9]+\s+[0-3]?[0-9][-/.][01]?[0-9][-/.](?:20)?[0-9]{2}\s+([A-Z0-9./_-]+)", RegexOptions.IgnoreCase);
        if (tableNumber.Success) return (CompactarNumeroFactura(tableNumber.Groups[1].Value), 0.99m);

        var lineas = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i <= lineas.Length; i++)
        {
            // Valor pegado delante de la etiqueta en la misma línea («IEE2025000000271Número de factura»).
            var pegado = Regex.Match(QuitarDiacriticos(lineas[i - 1]).Trim(), @"^([A-Z0-9][A-Z0-9./_-]{3,39}?)\s*NUMERO\s+DE\s+FACTURA\s*:?$", RegexOptions.IgnoreCase);
            if (pegado.Success && EsNumeroFacturaPlausible(CompactarNumeroFactura(pegado.Groups[1].Value)) && !PatronFecha.IsMatch(pegado.Groups[1].Value))
                return (CompactarNumeroFactura(pegado.Groups[1].Value), 0.99m);
            // Etiqueta sola debajo del valor («IEE2025000000271» y en la línea siguiente «Número de factura»).
            if (i == lineas.Length) continue;
            // Cabecera de tabla «FACTURA Nº  FECHA  CLIENTE…» con los valores en la línea siguiente: el primero es el número.
            if (Regex.IsMatch(QuitarDiacriticos(lineas[i - 1]).Trim(), @"^FACTURA\s+N\s*[.º°O]*\s+(?:FECHA|DATA)\b", RegexOptions.IgnoreCase))
            {
                var primero = Regex.Match(lineas[i].Trim(), @"^[A-Z0-9][A-Z0-9./_-]{2,39}", RegexOptions.IgnoreCase);
                if (primero.Success && !PatronFecha.IsMatch(primero.Value) && EsNumeroFacturaPlausible(CompactarNumeroFactura(primero.Value)))
                    return (CompactarNumeroFactura(primero.Value), 0.99m);
            }
            if (!Regex.IsMatch(QuitarDiacriticos(lineas[i]).Trim(), @"^(?:N[UÚ]MERO\s+DE\s+FACTURA|N\s*[.º°]*\s*(?:DE\s+)?FACTURA|INVOICE\s+(?:NUMBER|NO\.?))\s*:?$", RegexOptions.IgnoreCase)) continue;
            var anterior = lineas[i - 1].Trim();
            if (!Regex.IsMatch(anterior, @"^[A-Z0-9][A-Z0-9./_-]{3,39}$", RegexOptions.IgnoreCase)) continue;
            var candidato = CompactarNumeroFactura(anterior);
            if (EsNumeroFacturaPlausible(candidato) && !PatronFecha.IsMatch(anterior)) return (candidato, 0.99m);
        }

        foreach (var line in lineas)
        {
            var normalized = QuitarDiacriticos(line).ToUpperInvariant();
            var labelled = Regex.Match(normalized, @"(?:N\s*[.º°O]*\s*DE\s+FACTURA|NUMERO\s+DE\s+(?:LA\s+)?FACTURA|N\W*FACTURA|N\s*[.º°O]+\s*FACTURA|FACTURA\s+NUM(?:ERO)?\.?|FACTURA\s*(?:N[º°O]|#|-)|NUMERO)\s*[:#-]?\s*(.+)$", RegexOptions.IgnoreCase);
            if (!labelled.Success) continue;
            var parts = Regex.Matches(labelled.Groups[1].Value, @"[A-Z0-9]+(?:[./_-][A-Z0-9]+)*").Select(m => m.Value).Take(2).ToArray();
            if (parts.Length == 0) continue;
            var candidate = NormalizarNumeroTrasEtiqueta(string.Join(" ", parts));
            if (EsNumeroFacturaPlausible(candidate)) return (candidate, 0.99m);
        }
        return (string.Empty, 0m);
    }

    private static string NormalizarNumeroTrasEtiqueta(string value)
    {
        var parts = Regex.Matches(value.ToUpperInvariant(), @"[A-Z0-9]+(?:[./_-][A-Z0-9]+)*").Select(m => m.Value).Take(2).ToArray();
        if (parts.Length == 0) return string.Empty;
        var candidate = parts[0];
        if (parts.Length > 1 && parts[0].Length <= 3 && parts[0].Any(char.IsLetter)) candidate += parts[1];
        return CompactarNumeroFactura(candidate);
    }

    /// <summary>Busca tokens cuya estructura se parece a números ya registrados del mismo proveedor.</summary>
    private static (string Value, decimal Confidence) BuscarNumeroFacturaPorHistorico(string text, IReadOnlyCollection<string>? ejemplos)
    {
        if (ejemplos is null || ejemplos.Count == 0) return (string.Empty, 0m);
        var patrones = ejemplos.Select(CompactarNumeroFactura).Where(EsNumeroFacturaPlausible)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (patrones.Length == 0) return (string.Empty, 0m);

        var best = string.Empty;
        var bestScore = int.MinValue;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            foreach (var rawCandidate in GenerarCandidatosNumero(line))
            {
                if (Regex.IsMatch(rawCandidate, @"^[0-3]?[0-9][./-][01]?[0-9][./-](?:[0-9]{2}|(?:19|20)[0-9]{2})$")) continue;
                var candidate = CompactarNumeroFactura(rawCandidate);
                if (!EsNumeroFacturaPlausible(candidate)) continue;
                foreach (var ejemplo in patrones)
                {
                    var score = PuntuarNumeroFacturaParecido(candidate, ejemplo);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
            }
        if (bestScore < 55) return (string.Empty, 0m);
        return (best, Math.Clamp(0.58m + (bestScore - 55) / 100m, 0.58m, 0.97m));
    }

    private static IEnumerable<string> GenerarCandidatosNumero(string line)
    {
        var matches = Regex.Matches(line.ToUpperInvariant(), @"[A-Z0-9][A-Z0-9./_-]{0,39}").ToArray();
        for (var index = 0; index < matches.Length; index++)
        {
            yield return matches[index].Value;
            var first = matches[index].Value;
            if (first.IndexOfAny(['/', '-', '.', '_']) >= 0 && !Regex.IsMatch(first, @"^(?:19|20)[0-9]{2}[/_-][0-9]+$")) continue;
            if (!first.Any(char.IsLetter) && !Regex.IsMatch(first, @"^(?:19|20)[0-9]{2}$")) continue;
            var combined = matches[index].Value;
            for (var following = index + 1; following <= Math.Min(index + 2, matches.Length - 1); following++)
            {
                var gapStart = matches[following - 1].Index + matches[following - 1].Length;
                var gap = line.Substring(gapStart, matches[following].Index - gapStart);
                if (!Regex.IsMatch(gap, @"^[\s./_-]+$")) break;
                combined += matches[following].Value;
                yield return combined;
            }
        }
    }

    private static (string Value, decimal Confidence) BuscarNumeroFacturaEnNombreArchivo(string? path, string text, IReadOnlyCollection<string>? ejemplos)
    {
        if (string.IsNullOrWhiteSpace(path)) return (string.Empty, 0m);
        var stem = NombreSinExtension(path);
        if (!Regex.IsMatch(stem, "^[A-Z0-9]{3,40}$", RegexOptions.IgnoreCase)) return (string.Empty, 0m);
        if (stem.StartsWith("FACTURA", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("INVOICE", StringComparison.OrdinalIgnoreCase)) return (string.Empty, 0m);
        var candidate = CompactarNumeroFactura(stem);
        if (EsNumeroFacturaPlausible(candidate) && CompactarNumeroFactura(text).Contains(candidate, StringComparison.OrdinalIgnoreCase)) return (candidate, 0.98m);
        if (ejemplos is null || ejemplos.Count == 0) return (string.Empty, 0m);
        var historical = BuscarNumeroFacturaPorHistorico(stem, ejemplos);
        return historical.Value == string.Empty ? historical : (historical.Value, Math.Max(0.98m, historical.Confidence));
    }

    /// <summary>Nombre del fichero sin extensión, admitiendo rutas Windows también fuera de Windows.</summary>
    private static string NombreSinExtension(string path)
    {
        var nombre = path.Replace('\\', '/');
        nombre = nombre[(nombre.LastIndexOf('/') + 1)..];
        var punto = nombre.LastIndexOf('.');
        return punto > 0 ? nombre[..punto] : nombre;
    }

    private static int PuntuarNumeroFacturaParecido(string candidate, string example)
    {
        var difference = Math.Abs(candidate.Length - example.Length);
        var yearSeries = Regex.IsMatch(candidate, "^[0-9]+$") && Regex.IsMatch(example, "^[0-9]+$") &&
                         candidate.Length >= 3 && example.Length >= 3 && difference <= 4 &&
                         Math.Abs(int.Parse(candidate[..2], CultureInfo.InvariantCulture) - int.Parse(example[..2], CultureInfo.InvariantCulture)) <= 1;
        if (candidate.Length < 3 || (difference > 1 && !yearSeries)) return int.MinValue;
        var score = 30 - Math.Min(difference, 4) * 4;
        if (yearSeries) score += 40;
        var candidateLetters = candidate.Count(char.IsLetter);
        var exampleLetters = example.Count(char.IsLetter);
        if (Math.Abs(candidateLetters - exampleLetters) > 1) return int.MinValue;
        score += 15;
        if (candidate.Length >= 2 && example.Length >= 2 && candidate[..2].Equals(example[..2], StringComparison.OrdinalIgnoreCase)) score += 20;
        if (PatronEstructuralNumero(candidate) == PatronEstructuralNumero(example)) score += 35;
        if (PrefijoAlfabetico(candidate).Equals(PrefijoAlfabetico(example), StringComparison.OrdinalIgnoreCase)) score += 15;
        if (candidate.Count(char.IsDigit) == example.Count(char.IsDigit)) score += 10;
        if (CadenasParecidasLegacy(candidate, example)) score += 55;
        return score;
    }

    /// <summary>Heurística contrastada por ProConta (CadenasParecidas) para comparar formatos de número de factura.</summary>
    internal static bool CadenasParecidasLegacy(string candidate, string example)
    {
        if (candidate.Length < 3 || Math.Abs(candidate.Length - example.Length) > 1) return false;

        var similar = candidate.StartsWith(example[..Math.Min(2, example.Length)], StringComparison.OrdinalIgnoreCase) &&
                      EsNumerico(candidate[Math.Max(0, candidate.Length - 2)..]) &&
                      EsNumerico(example[Math.Max(0, example.Length - 2)..]);

        var candidateSuffix2 = candidate[Math.Max(0, candidate.Length - 2)..];
        var exampleSuffix2 = example[Math.Max(0, example.Length - 2)..];
        similar = similar || (candidate.EndsWith(exampleSuffix2, StringComparison.OrdinalIgnoreCase) && !EsNumerico(candidateSuffix2));

        if (candidate.Length >= 6 && example.Length >= 4)
            similar = similar || candidate.EndsWith(example[^4..], StringComparison.OrdinalIgnoreCase);
        else if (candidate.Length >= 4 && example.Length >= 2)
            similar = similar || candidate.EndsWith(exampleSuffix2, StringComparison.OrdinalIgnoreCase);

        if (!similar) return false;
        var candidateLetters = candidate.Count(c => char.ToUpperInvariant(c) > 58);
        var exampleLetters = example.Count(c => char.ToUpperInvariant(c) > 58);
        return Math.Abs(exampleLetters - candidateLetters) < 2;
    }

    private static bool EsNumerico(string value) => value.Length > 0 && value.All(char.IsDigit);

    private static string PatronEstructuralNumero(string value)
        => Regex.Replace(Regex.Replace(value, "[A-Z]", "L", RegexOptions.IgnoreCase), "[0-9]", "D");

    private static string PrefijoAlfabetico(string value)
    {
        var match = Regex.Match(value, "^[A-Z]+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : string.Empty;
    }

    /// <summary>Busca tokens próximos a las etiquetas de número y factura usando coordenadas de página.</summary>
    private static (string Value, decimal Confidence) BuscarNumeroFacturaPorGeometria(DocumentoFacturaExtraido document)
    {
        var best = string.Empty;
        var bestScore = 0;
        foreach (var page in document.Paginas)
            foreach (var word in page.Palabras)
            {
                var candidate = LimpiarNumeroFactura(word.Texto);
                if (!EsNumeroFacturaPlausible(candidate)) continue;
                var nearby = page.Palabras.Where(label => EstaCerca(word, label, 240d, 65d)).Select(label => QuitarDiacriticos(label.Texto).ToLowerInvariant()).ToArray();
                var aligned = page.Palabras.Where(label => EstaCerca(word, label, 120d, 65d)).Select(label => QuitarDiacriticos(label.Texto).ToLowerInvariant()).ToArray();
                var score = 0;
                if (nearby.Any(l => l.Contains("factura", StringComparison.Ordinal) || l.Contains("invoice", StringComparison.Ordinal))) score += 6;
                if (aligned.Any(l => l.Contains("factura", StringComparison.Ordinal) || l.Contains("invoice", StringComparison.Ordinal))) score += 4;
                if (nearby.Any(l => l.Contains("numero", StringComparison.Ordinal) || l == "nº" || l == "no" || Regex.IsMatch(l, "^n[^a-z0-9]+$"))) score += 4;
                if (nearby.Any(l => l.Contains("nif", StringComparison.Ordinal) || l.Contains("cif", StringComparison.Ordinal))) score -= 5;
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
        if (bestScore < 10) return (string.Empty, 0m);
        return (best, Math.Clamp(0.55m + bestScore * 0.04m, 0.55m, 0.92m));
    }

    private static string LimpiarNumeroFactura(string? value) => (value ?? string.Empty).Trim().Trim('#', ':', ';', ',', '.', '(', ')');

    /// <summary>Número limpio: solo letras ASCII y dígitos, en mayúsculas.</summary>
    public static string CompactarNumeroFactura(string? value)
        => Regex.Replace(LimpiarNumeroFactura(value).ToUpperInvariant(), "[^A-Z0-9]", string.Empty);

    /// <summary>Descarta palabras y fechas que no pueden ser números de factura.</summary>
    private static bool EsNumeroFacturaPlausible(string value)
    {
        if (value.Length < 4 || value.Length > 40) return false;
        if (PatronFecha.IsMatch(value)) return false;
        if (Regex.IsMatch(QuitarDiacriticos(value), "^[0-3]?[0-9][./-]?(?:ENE|FEB|MAR|ABR|MAY|JUN|JUL|AGO|SEP|OCT|NOV|DIC)[./-]?[0-9]{2,4}", RegexOptions.IgnoreCase)) return false;
        var digits = value.Count(char.IsDigit);
        if (digits < 1 || value.Equals("FACTURA", StringComparison.OrdinalIgnoreCase)) return false;
        return !EsTextoQueNoEsNumero(value);
    }

    /// <summary>
    /// Valores que aparecen junto a las etiquetas pero no son el número: un año suelto, un NIF o CIF válido (también con su
    /// prefijo NIF, CIF, IVA, VAT o de país) y un código postal pegado a la población.
    /// </summary>
    private static bool EsTextoQueNoEsNumero(string value)
    {
        var compacto = CompactarNif(value);
        if (Regex.IsMatch(compacto, "^(?:19|20)[0-9]{2}$")) return true;
        if (Regex.IsMatch(compacto, "^[0-9]{5}[A-Z]{3,}$")) return true;
        var sinPrefijo = Regex.Replace(compacto, "^(?:NIF|CIF|IVA|VAT|DNI|NIE)", string.Empty);
        if (EsNifEspanolValido(sinPrefijo)) return true;
        return sinPrefijo.Length > 2 && Regex.IsMatch(sinPrefijo, "^ES") && EsNifEspanolValido(sinPrefijo[2..]);
    }

    /// <summary>Puntúa fechas según sus etiquetas cercanas y penaliza vencimientos.</summary>
    private static (DateTime? Value, decimal Confidence) BuscarFechaFactura(string text, string rutaDocumento)
    {
        DateTime? bestDate = null;
        var bestScore = int.MinValue;
        var ejercicioEsperado = ObtenerEjercicioEsperado(rutaDocumento);
        foreach (Match match in PatronFecha.Matches(text))
        {
            var parsed = InterpretarFecha(match.Value);
            if (!parsed.HasValue) continue;
            var contextStart = Math.Max(0, match.Index - 90);
            var context = QuitarDiacriticos(text.Substring(contextStart, match.Index - contextStart)).ToLowerInvariant();
            var score = 1;
            if (context.Contains("fecha factura", StringComparison.Ordinal) || context.Contains("fecha de factura", StringComparison.Ordinal) || context.Contains("fecha de facturacion", StringComparison.Ordinal)) score += 12;
            if (context.Contains("fecha de la factura", StringComparison.Ordinal) || context.Contains("data factura", StringComparison.Ordinal) || context.Contains("data de factura", StringComparison.Ordinal) ||
                context.Contains("num. factura", StringComparison.Ordinal) || context.Contains("data d'emissio", StringComparison.Ordinal) || context.Contains("data emissio", StringComparison.Ordinal)) score += 12;
            if (context.Contains("cobrament", StringComparison.Ordinal) || context.Contains("carrec", StringComparison.Ordinal) || context.Contains("periode", StringComparison.Ordinal) ||
                context.Contains("pedido", StringComparison.Ordinal) || context.Contains("cobertura", StringComparison.Ordinal) || context.Contains("entrega prevista", StringComparison.Ordinal) ||
                context.Contains("contracte", StringComparison.Ordinal) || context.Contains("rebut", StringComparison.Ordinal)) score -= 10;
            if (context.Contains("fecha de emision", StringComparison.Ordinal) || context.Contains("fecha emision", StringComparison.Ordinal) || context.Contains("fecha de expedicion", StringComparison.Ordinal)) score += 12;
            if (context.Contains("fecha fra", StringComparison.Ordinal)) score += 10;
            if (context.Contains("emision", StringComparison.Ordinal) || context.Contains("expedicion", StringComparison.Ordinal)) score += 5;
            if (context.Contains("factura", StringComparison.Ordinal) || context.Contains("invoice date", StringComparison.Ordinal)) score += 3;
            if (context.Contains("venc", StringComparison.Ordinal) || context.Contains("due date", StringComparison.Ordinal) || context.Contains("periodo", StringComparison.Ordinal) || context.Contains("lectura", StringComparison.Ordinal)) score -= 10;
            if (context.Contains("tarifa", StringComparison.Ordinal) || context.Contains("bop", StringComparison.Ordinal) || context.Contains("boe", StringComparison.Ordinal) || context.Contains("dogc", StringComparison.Ordinal)) score -= 12;
            if (ejercicioEsperado.HasValue) score += parsed.Value.Year == ejercicioEsperado.Value ? 4 : -12;
            if (score > bestScore)
            {
                bestScore = score;
                bestDate = parsed;
            }
        }
        if (!bestDate.HasValue) return (null, 0m);
        return (bestDate, Math.Clamp(0.45m + bestScore * 0.05m, 0.25m, 0.95m));
    }

    /// <summary>Relaciona fechas con etiquetas situadas a su izquierda o encima.</summary>
    private static (DateTime? Value, decimal Confidence) BuscarFechaFacturaPorGeometria(DocumentoFacturaExtraido document)
    {
        DateTime? bestDate = null;
        var bestScore = int.MinValue;
        var ejercicioEsperado = ObtenerEjercicioEsperado(document.Ruta);
        foreach (var page in document.Paginas)
            foreach (var word in page.Palabras)
            {
                var parsed = InterpretarFecha(word.Texto.Trim(' ', ':', ';', ','));
                if (!parsed.HasValue) continue;
                var nearbyWords = page.Palabras.Where(label => EstaCerca(word, label, 260d, 75d)).ToArray();
                var nearby = nearbyWords.Select(label => QuitarDiacriticos(label.Texto).ToLowerInvariant()).ToArray();
                var aligned = nearbyWords.Where(label => Math.Abs(word.X - label.X) <= 120d).Select(label => QuitarDiacriticos(label.Texto).ToLowerInvariant()).ToArray();
                var score = 1;
                if (aligned.Any(l => l.Contains("emision", StringComparison.Ordinal) || l.Contains("expedicion", StringComparison.Ordinal))) score += 12;
                if (aligned.Any(l => l.Contains("fecha", StringComparison.Ordinal) || l.Contains("data", StringComparison.Ordinal))) score += 4;
                if (aligned.Any(l => l.Contains("factura", StringComparison.Ordinal) || l.Contains("invoice", StringComparison.Ordinal))) score += 4;
                if (nearby.Any(l => l.Contains("venc", StringComparison.Ordinal) || l.Contains("cobro", StringComparison.Ordinal) || l.Contains("periodo", StringComparison.Ordinal))) score -= 10;
                if (nearby.Any(l => l.Contains("lectura", StringComparison.Ordinal) || l.Contains("tarifa", StringComparison.Ordinal) || l == "bop" || l == "boe" || l == "dogc")) score -= 12;
                if (ejercicioEsperado.HasValue) score += parsed.Value.Year == ejercicioEsperado.Value ? 4 : -12;
                if (score > bestScore)
                {
                    bestDate = parsed;
                    bestScore = score;
                }
            }
        if (!bestDate.HasValue || bestScore <= 0) return (null, 0m);
        return (bestDate, Math.Clamp(0.45m + bestScore * 0.035m, 0.45m, 0.96m));
    }

    private static bool EstaCerca(PalabraDocumento value, PalabraDocumento label, double maximumHorizontalDistance, double maximumVerticalDistance)
    {
        if (value.Pagina != label.Pagina || ReferenceEquals(value, label)) return false;
        return Math.Abs(value.X - label.X) <= maximumHorizontalDistance && Math.Abs(value.Y - label.Y) <= maximumVerticalDistance;
    }

    /// <summary>Interpreta fechas europeas, ISO y textuales sin depender de la cultura del equipo.</summary>
    internal static DateTime? InterpretarFecha(string value)
    {
        var normalized = value.Replace('.', '/').Replace('-', '/');
        string[] formats = ["d/M/yyyy", "dd/MM/yyyy", "yyyy/M/d", "yyyy/MM/dd"];
        if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed.Date;
        var corta = Regex.Match(normalized, "^(?<dia>[0-3]?[0-9])/(?<mes>[01]?[0-9])/(?<anio>[0-9]{2})$");
        if (corta.Success && DateTime.TryParseExact(
                $"{corta.Groups["dia"].Value}/{corta.Groups["mes"].Value}/{2000 + int.Parse(corta.Groups["anio"].Value, CultureInfo.InvariantCulture)}",
                "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return parsed.Date;
        var sinAcentos = QuitarDiacriticos(value.Trim());
        var textual = Regex.Match(sinAcentos, @"^(?<dia>[0-3]?[0-9])(?:\s+de\s+|[./-]|\s+)(?<mes>[A-Z]+)\.?(?:\s+de\s+|[./-]|\s+)(?<anio>(?:19|20)[0-9]{2})$", RegexOptions.IgnoreCase);
        if (!textual.Success)
            textual = Regex.Match(sinAcentos, @"^(?<mes>[A-Z]+)\.?\s+(?<dia>[0-3]?[0-9]),\s*(?<anio>(?:19|20)[0-9]{2})$", RegexOptions.IgnoreCase);
        if (textual.Success)
        {
            var mes = ObtenerMes(textual.Groups["mes"].Value);
            if (mes > 0 && int.TryParse(textual.Groups["dia"].Value, CultureInfo.InvariantCulture, out var dia) &&
                int.TryParse(textual.Groups["anio"].Value, CultureInfo.InvariantCulture, out var anio))
            {
                try { return new DateTime(anio, mes, dia); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
        return null;
    }

    /// <summary>Meses en español, catalán e inglés.</summary>
    private static int ObtenerMes(string value)
    {
        var mes = value.ToUpperInvariant();
        bool Empieza(params string[] prefijos) => prefijos.Any(p => mes.StartsWith(p, StringComparison.Ordinal));
        if (Empieza("ENE", "GEN", "JAN")) return 1;
        if (Empieza("FEB")) return 2;
        if (Empieza("MAR")) return 3;
        if (Empieza("ABR", "APR")) return 4;
        if (Empieza("MAY", "MAI")) return 5;
        if (Empieza("JUN")) return 6;
        if (Empieza("JUL")) return 7;
        if (Empieza("AGO", "AUG")) return 8;
        if (Empieza("SEP", "SET")) return 9;
        if (Empieza("OCT")) return 10;
        if (Empieza("NOV")) return 11;
        if (Empieza("DIC", "DES", "DEC")) return 12;
        return 0;
    }

    /// <summary>Ejercicio esperado según el primer año 20xx de la ruta.</summary>
    private static int? ObtenerEjercicioEsperado(string? rutaDocumento)
    {
        var match = Regex.Match(rutaDocumento ?? string.Empty, "(?<![0-9])(20[0-9]{2})(?![0-9])");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Reconoce importes etiquetados, filas de IVA y combinaciones aritméticamente coherentes.</summary>
    private static void ReconocerImportes(DocumentoFacturaExtraido documento, string text, ResultadoReconocimientoFacturaCompra result, ICollection<string> warnings)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var totalLabeled = BuscarImporteEtiquetado(lines, ["importe total", "total factura", "total a pagar", "total due", "grand total"]);
        var totalInequivoco = BuscarTotalInequivoco(lines);
        var baseLabeled = BuscarImporteEtiquetado(lines, ["base imponible", "taxable amount", "subtotal"]);
        var taxLabeled = BuscarImporteEtiquetado(lines, ["cuota iva", "importe iva", "total iva", "vat amount", "tax amount"]);
        var totalGeometrico = BuscarImporteTotalPorGeometria(documento);
        var totalAirbnb = BuscarTotalAirbnb(documento, text);

        if (FamiliasDocumentales.ImportesPorFamilia(documento, text, lines, result)) return;

        if (totalAirbnb.HasValue)
        {
            result.ImporteTotal = totalAirbnb;
            result.ConfianzaImportes = 0.96m;
            return;
        }

        if (totalInequivoco.HasValue && FamiliasDocumentales.PriorizaTotalInequivoco(documento))
        {
            result.ImporteTotal = totalInequivoco;
            result.ConfianzaImportes = 0.91m;
            if (baseLabeled.HasValue) result.BaseImponible = baseLabeled;
            if (taxLabeled.HasValue) result.CuotaIva = taxLabeled;
            return;
        }

        if (FamiliasDocumentales.UsaTotalGeometrico(documento) && totalGeometrico.Confidence >= 0.82m)
        {
            result.ImporteTotal = totalGeometrico.Value;
            result.ConfianzaImportes = totalGeometrico.Confidence;
            if (baseLabeled.HasValue) result.BaseImponible = baseLabeled;
            if (taxLabeled.HasValue) result.CuotaIva = taxLabeled;
            return;
        }

        var breakdowns = BuscarDesgloses(lines);
        if (breakdowns.Count > 0)
        {
            var totalBase = breakdowns.Sum(item => item.Base);
            var totalTax = breakdowns.Sum(item => item.Cuota);
            var reconciledTotal = Redondear(totalBase + totalTax);
            if (totalLabeled.HasValue && Math.Abs(totalLabeled.Value - reconciledTotal) > 0.04m)
                warnings.Add($"El desglose suma {reconciledTotal.ToString("N2", Cultura)}, pero el total etiquetado es {totalLabeled.Value.ToString("N2", Cultura)}.");
            else
            {
                result.BaseImponible = totalBase;
                result.CuotaIva = totalTax;
                result.ImporteTotal = totalLabeled ?? reconciledTotal;
                result.ConfianzaImportes = totalLabeled.HasValue ? 0.94m : 0.78m;
                result.DesglosesIva = breakdowns;
                return;
            }
        }

        if (baseLabeled.HasValue && totalLabeled.HasValue)
        {
            var inferredTax = Redondear(totalLabeled.Value - baseLabeled.Value);
            var tax = taxLabeled ?? inferredTax;
            if (Math.Abs(baseLabeled.Value + tax - totalLabeled.Value) <= 0.04m)
            {
                result.BaseImponible = baseLabeled;
                result.CuotaIva = tax;
                result.ImporteTotal = totalLabeled;
                result.ConfianzaImportes = taxLabeled.HasValue ? 0.9m : 0.82m;
                return;
            }
        }

        var allAmounts = ExtraerImportes(text).Distinct().Take(250).ToArray();
        decimal? bestBase = null, bestTotal = null, bestRate = null;
        foreach (var baseAmount in allAmounts)
        {
            if (baseAmount == 0m) continue;
            foreach (var taxRate in TiposHabituales.Where(item => item > 0m))
            {
                var expected = Redondear(baseAmount * (1m + taxRate));
                var matchingTotal = allAmounts.Where(item => Math.Abs(item - expected) <= 0.02m).OrderByDescending(Math.Abs).FirstOrDefault();
                if (matchingTotal != 0m && (!bestTotal.HasValue || Math.Abs(matchingTotal) > Math.Abs(bestTotal.Value)))
                {
                    bestBase = baseAmount;
                    bestTotal = matchingTotal;
                    bestRate = taxRate;
                }
            }
        }

        if (bestBase.HasValue && bestTotal.HasValue && bestRate.HasValue)
        {
            var tax = Redondear(bestTotal.Value - bestBase.Value);
            var cuotaPresente = allAmounts.Any(item => Math.Abs(item - tax) <= 0.02m);
            var confianzaAritmetica = cuotaPresente ? 0.97m : 0.9m;
            result.BaseImponible = bestBase;
            result.CuotaIva = tax;
            result.ImporteTotal = bestTotal;
            result.ConfianzaImportes = confianzaAritmetica;
            result.DesglosesIva =
            [
                new DesgloseIvaReconocido
                {
                    Tipo = bestRate.Value * 100m,
                    Base = bestBase.Value,
                    Cuota = tax,
                    Confianza = confianzaAritmetica,
                    Evidencia = cuotaPresente ? "BI, IVA y total presentes y reconciliados aritméticamente" : "BI y total reconciliados; IVA calculado"
                }
            ];
            return;
        }

        if (totalLabeled.HasValue)
        {
            result.ImporteTotal = totalLabeled;
            result.BaseImponible = totalLabeled;
            result.CuotaIva = 0m;
            result.ConfianzaImportes = 0.42m;
        }
    }

    /// <summary>Eleva la confianza, sin cambiar importes, cuando BI + IVA = total con un tipo habitual.</summary>
    private static void ElevarConfianzaPorReconciliacionAritmetica(string texto, ResultadoReconocimientoFacturaCompra resultado)
    {
        if (!resultado.BaseImponible.HasValue || !resultado.CuotaIva.HasValue || !resultado.ImporteTotal.HasValue) return;
        if (Math.Abs(resultado.BaseImponible.Value + resultado.CuotaIva.Value - resultado.ImporteTotal.Value) > 0.02m) return;
        if (resultado.BaseImponible.Value == 0m) return;
        var tipo = Math.Abs(resultado.CuotaIva.Value / resultado.BaseImponible.Value);
        if (!TiposHabituales.Any(habitual => Math.Abs(habitual - tipo) <= 0.0002m)) return;
        var cuotaPresente = ExtraerImportesMonetarios(texto).Any(importe => Math.Abs(importe - resultado.CuotaIva.Value) <= 0.02m);
        resultado.ConfianzaImportes = Math.Max(resultado.ConfianzaImportes, cuotaPresente ? 0.97m : 0.9m);
        if (resultado.DesglosesIva.Count == 1)
        {
            resultado.DesglosesIva[0].Confianza = resultado.ConfianzaImportes;
            resultado.DesglosesIva[0].Evidencia = cuotaPresente ? "BI, IVA y total presentes y reconciliados aritméticamente" : "BI y total reconciliados; IVA calculado";
        }
    }

    /// <summary>Completa base y cuota sin alterar el total documental ya reconocido.</summary>
    private static void CompletarBaseEIva(DocumentoFacturaExtraido documento, string texto, ResultadoReconocimientoFacturaCompra resultado, ContextoReconocimientoFacturaCompra contexto, ICollection<string> warnings)
    {
        if (!resultado.ImporteTotal.HasValue) return;
        if (FamiliasDocumentales.CompletarBaseEIvaPorFamilia(documento, texto, resultado, contexto, warnings)) return;
    }

    // ------------------------------------------------------------------------------------------
    // Utilidades compartidas con las familias documentales.
    // ------------------------------------------------------------------------------------------

    internal static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-ES");

    internal static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    /// <summary>Lee la fila final de las facturas LIDL usando las columnas Neto, Importe IVA y Bruto.</summary>
    internal static (decimal Base, decimal Cuota, decimal Total)? BuscarResumenLidl(DocumentoFacturaExtraido documento)
    {
        foreach (var pagina in documento.Paginas)
            foreach (var etiqueta in pagina.Palabras.Where(p => p.Texto.Equals("Total", StringComparison.OrdinalIgnoreCase)))
            {
                var importes = pagina.Palabras
                    .Where(p => p.X > etiqueta.X && Math.Abs(p.Y - etiqueta.Y) <= 5d)
                    .Select(p => (Palabra: p, Importe: InterpretarImporte(p.Texto)))
                    .Where(i => i.Importe.HasValue)
                    .OrderBy(i => i.Palabra.X)
                    .Select(i => i.Importe!.Value)
                    .ToArray();
                if (importes.Length >= 3 && Math.Abs(importes[0] + importes[1] - importes[2]) <= 0.04m)
                    return (importes[0], importes[1], importes[2]);
            }
        return null;
    }

    /// <summary>Cuota de Aigües a partir de la base sujeta indicada junto a su tipo.</summary>
    internal static decimal? BuscarCuotaAigues(string texto)
    {
        var patron = new Regex(@"iva\s+del\s+([0-9]{1,2}(?:[,.][0-9]+)?)\s*%\s+de\s+la\s+base\s+sujeta\s+([-+]?[0-9.]+,[0-9]{2,4})", RegexOptions.IgnoreCase);
        var coincidencia = patron.Match(QuitarDiacriticos(texto));
        if (!coincidencia.Success) return null;
        var tipo = InterpretarNumeroFlexible(coincidencia.Groups[1].Value);
        var baseSujeta = InterpretarImporte(coincidencia.Groups[2].Value);
        if (!tipo.HasValue || !baseSujeta.HasValue) return null;
        return baseSujeta.Value * tipo.Value / 100m;
    }

    /// <summary>Cuota de líneas que expresan el tipo y la base gravada mediante «IVA (x %) sobre».</summary>
    internal static decimal? BuscarCuotaSobreBase(string texto)
    {
        var patron = new Regex(@"iva\s*\(\s*([0-9]{1,2}(?:[,.][0-9]+)?)\s*%\s*\)\s*sobre\s*([-+]?[0-9.]+(?:,[0-9]+)?)", RegexOptions.IgnoreCase);
        var coincidencia = patron.Match(QuitarDiacriticos(texto));
        if (!coincidencia.Success) return null;
        var tipo = InterpretarNumeroFlexible(coincidencia.Groups[1].Value);
        var @base = InterpretarNumeroFlexible(coincidencia.Groups[2].Value.Replace(".", string.Empty));
        if (!tipo.HasValue || !@base.HasValue) return null;
        return @base.Value * tipo.Value / 100m;
    }

    /// <summary>Tipo de IVA operativo de la factura (en porcentaje), evitando porcentajes ajenos al resumen fiscal.</summary>
    internal static decimal? BuscarTipoIvaPrincipal(string texto, DocumentoFacturaExtraido documento)
    {
        var normalizado = QuitarDiacriticos(texto);
        var patron = new Regex(@"iva\s*(?:del)?\s*\(?\s*([0-9]{1,2}(?:[,.][0-9]+)?)\s*%", RegexOptions.IgnoreCase);
        var tipos = patron.Matches(normalizado).Select(c => InterpretarNumeroFlexible(c.Groups[1].Value)).Where(t => t.HasValue).Select(t => t!.Value).ToArray();
        if (tipos.Length > 0) return tipos[0];

        if (FamiliasDocumentales.EsRutaDe(documento, "Amazon"))
        {
            if (Regex.IsMatch(normalizado, @"(?<![0-9])21\s*%", RegexOptions.IgnoreCase)) return 21m;
            if (Regex.IsMatch(normalizado, @"(?<![0-9])0\s*%", RegexOptions.IgnoreCase)) return 0m;
        }
        if (FamiliasDocumentales.EsRutaDe(documento, "AluminiosCasas"))
        {
            var tipoSuelto = Regex.Match(normalizado, @"(?<![0-9])([0-9]{1,2}(?:[,.][0-9]+)?)\s*%", RegexOptions.IgnoreCase);
            if (tipoSuelto.Success) return InterpretarNumeroFlexible(tipoSuelto.Groups[1].Value);
        }
        var coincidenciaTipo = Regex.Match(normalizado, @"([0-9]{1,2}(?:[,.][0-9]+)?)\s*%\s*(?:s/|sobre)", RegexOptions.IgnoreCase);
        return coincidenciaTipo.Success ? InterpretarNumeroFlexible(coincidenciaTipo.Groups[1].Value) : null;
    }

    /// <summary>Porcentaje con coma o punto decimal, sin separadores de millares.</summary>
    internal static decimal? InterpretarNumeroFlexible(string valor)
        => decimal.TryParse(valor.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var r) ? r : null;

    /// <summary>Base y cuota cuando todo el total comparte un único tipo (en porcentaje). Conserva el total.</summary>
    internal static void AsignarBaseDesdeTipo(ResultadoReconocimientoFacturaCompra resultado, decimal tipoPorcentaje)
    {
        if (!resultado.ImporteTotal.HasValue || tipoPorcentaje < 0m) return;
        var @base = resultado.ImporteTotal.Value / (1m + tipoPorcentaje / 100m);
        resultado.BaseImponible = @base;
        resultado.CuotaIva = resultado.ImporteTotal.Value - @base;
    }

    /// <summary>Base contable restando del total la cuota fiscal del documento. Conserva el total.</summary>
    internal static void AsignarBaseDesdeCuota(ResultadoReconocimientoFacturaCompra resultado, decimal cuota)
    {
        if (!resultado.ImporteTotal.HasValue) return;
        resultado.CuotaIva = cuota;
        resultado.BaseImponible = resultado.ImporteTotal.Value - cuota;
    }

    /// <summary>Valor monetario alineado con una etiqueta inequívoca de total.</summary>
    private static (decimal? Value, decimal Confidence) BuscarImporteTotalPorGeometria(DocumentoFacturaExtraido documento)
    {
        decimal? mejorImporte = null;
        var mejorPuntuacion = int.MinValue;
        var esAmazon = FamiliasDocumentales.EsRutaDe(documento, "Amazon");
        var esNaturgy = FamiliasDocumentales.EsRutaDe(documento, "Naturgy");

        foreach (var pagina in documento.Paginas)
            foreach (var palabra in pagina.Palabras)
            {
                var importe = InterpretarImporte(palabra.Texto);
                if (!importe.HasValue) continue;
                if (esAmazon)
                {
                    var tieneEtiquetaAmazon = pagina.Palabras.Any(e => e.X < palabra.X && EstaCerca(palabra, e, 260d, 18d) &&
                        (QuitarDiacriticos(e.Texto).Contains("pendiente", StringComparison.OrdinalIgnoreCase) ||
                         QuitarDiacriticos(e.Texto).Contains("payable", StringComparison.OrdinalIgnoreCase)));
                    if (!tieneEtiquetaAmazon) continue;
                }
                var cercanas = pagina.Palabras.Where(e => EstaCerca(palabra, e, 360d, 12d)).Select(e => QuitarDiacriticos(e.Texto).ToLowerInvariant()).ToArray();
                if (!cercanas.Any(e => e.Contains("total", StringComparison.Ordinal))) continue;

                var puntuacion = 8;
                if (cercanas.Any(e => e.Contains("pagar", StringComparison.Ordinal) || e.Contains("factura", StringComparison.Ordinal) ||
                                      e.Contains("pendiente", StringComparison.Ordinal) || e.Contains("payable", StringComparison.Ordinal) ||
                                      e.Contains("due", StringComparison.Ordinal))) puntuacion += 8;
                if (cercanas.Any(e => e.Contains("subtotal", StringComparison.Ordinal))) puntuacion -= 12;
                if (cercanas.Any(e => e.Contains("base", StringComparison.Ordinal) || e.Contains("kwh", StringComparison.Ordinal) ||
                                      e.Contains("unitario", StringComparison.Ordinal))) puntuacion -= 6;
                if (pagina.Numero == 1) puntuacion += 2;
                if (esNaturgy)
                {
                    var repeticiones = documento.Paginas.SelectMany(p => p.Palabras).Select(p => InterpretarImporte(p.Texto))
                        .Count(i => i.HasValue && Math.Abs(i.Value - importe.Value) <= 0.005m);
                    puntuacion += Math.Min(8, Math.Max(0, repeticiones - 1) * 4);
                }
                if (puntuacion > mejorPuntuacion || (puntuacion == mejorPuntuacion && Math.Abs(importe.Value) > Math.Abs(mejorImporte ?? 0m)))
                {
                    mejorImporte = importe;
                    mejorPuntuacion = puntuacion;
                }
            }

        if (!mejorImporte.HasValue || mejorPuntuacion < 8) return (null, 0m);
        return (mejorImporte, Math.Clamp(0.62m + mejorPuntuacion * 0.02m, 0.62m, 0.98m));
    }

    /// <summary>Suma la columna de totales de los listados mensuales de comisiones de Airbnb.</summary>
    private static decimal? BuscarTotalAirbnb(DocumentoFacturaExtraido documento, string texto)
    {
        var normalizado = QuitarDiacriticos(texto).ToLowerInvariant();
        if (!FamiliasDocumentales.EsRutaDe(documento, "Airbnb") && !normalizado.Contains("airbnb", StringComparison.Ordinal)) return null;

        var cabecera = documento.Paginas.SelectMany(p => p.Palabras).FirstOrDefault(p => QuitarDiacriticos(p.Texto).Equals("total", StringComparison.OrdinalIgnoreCase));
        if (cabecera is null) return null;
        var importes = new List<decimal>();
        var centroColumna = cabecera.X + cabecera.Ancho / 2d;
        foreach (var pagina in documento.Paginas)
            importes.AddRange(pagina.Palabras
                .Where(p => (pagina.Numero != cabecera.Pagina || p.Y < cabecera.Y) && Math.Abs(p.X + p.Ancho / 2d - centroColumna) <= 120d)
                .Select(p => InterpretarImporte(p.Texto))
                .Where(i => i.HasValue && i.Value != 0m)
                .Select(i => i!.Value));
        if (importes.Count == 0) return null;
        return Redondear(importes.Sum());
    }

    /// <summary>Último importe de la línea con más etiquetas coincidentes.</summary>
    internal static decimal? BuscarImporteEtiquetado(IEnumerable<string> lines, IEnumerable<string> labels)
    {
        decimal? best = null;
        var bestScore = -1;
        var etiquetas = labels.ToArray();
        foreach (var line in lines)
        {
            var normalized = QuitarDiacriticos(line).ToLowerInvariant();
            var score = etiquetas.Count(label => normalized.Contains(label, StringComparison.Ordinal));
            if (score == 0) continue;
            var amounts = ExtraerImportes(line);
            if (amounts.Count == 0) continue;
            if (score > bestScore || (score == bestScore && Math.Abs(amounts[^1]) > Math.Abs(best ?? 0m)))
            {
                best = amounts[^1];
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Como <see cref="BuscarImporteEtiquetado"/>, admitiendo moneda, enteros y hasta seis decimales.</summary>
    internal static decimal? BuscarImporteEtiquetadoMonetario(IEnumerable<string> lines, IEnumerable<string> labels)
    {
        decimal? mejor = null;
        var mejorPuntuacion = -1;
        var etiquetas = labels.ToArray();
        foreach (var line in lines)
        {
            var normalizada = QuitarDiacriticos(line).ToLowerInvariant();
            var puntuacion = etiquetas.Count(label => normalizada.Contains(label, StringComparison.Ordinal));
            if (puntuacion == 0) continue;
            var importes = ExtraerImportesMonetarios(line);
            if (importes.Count == 0) continue;
            if (puntuacion > mejorPuntuacion)
            {
                mejor = importes[^1];
                mejorPuntuacion = puntuacion;
            }
        }
        return mejor;
    }

    /// <summary>Línea de cierre inequívoca, con moneda prefijada, punto decimal o importes enteros.</summary>
    private static decimal? BuscarTotalInequivoco(IEnumerable<string> lines)
    {
        var candidatos = new List<(int Prioridad, decimal Importe)>();
        foreach (var line in lines)
        {
            var normalizada = QuitarDiacriticos(line).Trim().ToLowerInvariant();
            var prioridad = 0;
            if (normalizada.Contains("total a pagar", StringComparison.Ordinal) || normalizada.Contains("total factura", StringComparison.Ordinal) ||
                normalizada.Contains("neto a pagar", StringComparison.Ordinal) || normalizada.Contains("grand total", StringComparison.Ordinal) ||
                normalizada.Contains("gran total", StringComparison.Ordinal) || normalizada.Contains("order total", StringComparison.Ordinal))
                prioridad = 3;
            else if (normalizada.Contains("totale fattura", StringComparison.Ordinal))
                prioridad = 3;
            else if (Regex.IsMatch(normalizada, @"^precio\s+total\b"))
                prioridad = 2;
            else if (Regex.IsMatch(normalizada, @"^total\s*(?::|$|[-+€$£0-9])") &&
                     !Regex.IsMatch(normalizada, @"^total\s+(?:iva|impuesto|tax|neto|productos|parcial|papel|promocion|envio|\(imp\.?(?:uesto)?\s+excl)"))
                prioridad = 1;
            if (prioridad == 0) continue;
            var importes = ExtraerImportesMonetarios(line);
            if (importes.Count > 0) candidatos.Add((prioridad, importes[^1]));
        }
        if (candidatos.Count == 0) return null;
        var maximaPrioridad = candidatos.Max(i => i.Prioridad);
        return candidatos.Where(i => i.Prioridad == maximaPrioridad).OrderByDescending(i => Math.Abs(i.Importe)).First().Importe;
    }

    /// <summary>Filas con tipo, base y cuota de IVA reconciliables; el mejor desglose de cada tipo.</summary>
    private static List<DesgloseIvaReconocido> BuscarDesgloses(IEnumerable<string> lines)
    {
        var byRate = new Dictionary<decimal, DesgloseIvaReconocido>();
        foreach (var line in lines)
            foreach (Match rateMatch in PatronTipoIva.Matches(line))
            {
                if (!decimal.TryParse(rateMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage)) continue;
                var rate = percentage / 100m;
                if (rate < 0m || rate > 0.3m) continue;
                var amounts = ExtraerImportes(line);
                foreach (var baseAmount in amounts)
                {
                    var expectedTax = Redondear(baseAmount * rate);
                    var tax = amounts.FirstOrDefault(item => item != baseAmount && Math.Abs(item - expectedTax) <= 0.02m);
                    if (tax == 0m && expectedTax != 0m) continue;
                    var candidate = new DesgloseIvaReconocido { Tipo = percentage, Base = baseAmount, Cuota = tax, Confianza = 0.86m, Evidencia = line.Trim() };
                    if (!byRate.TryGetValue(rate, out var actual) || Math.Abs(candidate.Base) > Math.Abs(actual.Base)) byRate[rate] = candidate;
                }
            }
        return byRate.OrderBy(par => par.Key).Select(par => par.Value).ToList();
    }

    /// <summary>Importes decimales respetando separadores europeos y anglosajones, en orden de aparición.</summary>
    internal static List<decimal> ExtraerImportes(string text)
    {
        var amounts = new List<decimal>();
        foreach (Match match in PatronImporte.Matches(text))
        {
            var parsed = InterpretarImporte(match.Value);
            if (parsed.HasValue && Math.Abs(parsed.Value) < 100000000m) amounts.Add(parsed.Value);
        }
        return amounts;
    }

    /// <summary>Importes de líneas monetarias explícitas, incluidos enteros y símbolos prefijados.</summary>
    internal static List<decimal> ExtraerImportesMonetarios(string text)
    {
        var amounts = new List<decimal>();
        foreach (Match match in PatronImporteMonetario.Matches(text))
        {
            var parsed = InterpretarImporte(match.Value, permitirEntero: true);
            if (parsed.HasValue && Math.Abs(parsed.Value) < 100000000m) amounts.Add(parsed.Value);
        }
        return amounts;
    }

    /// <summary>Importe con coma o punto decimal y separadores de millares opcionales; signo delante o detrás.</summary>
    internal static decimal? InterpretarImporte(string value, bool permitirEntero = false)
    {
        var compact = Regex.Replace(value, "(?i)EURO|EUR|USD|GBP|€|\\$|£", string.Empty).Replace(" ", string.Empty).Trim();
        if (compact.EndsWith('.') && compact.Contains(',')) compact = compact.TrimEnd('.');
        var signoSufijo = compact.EndsWith('-');
        if (signoSufijo || compact.EndsWith('+')) compact = compact[..^1];
        if (Regex.IsMatch(compact, @"^[0-3]?[0-9]([.])(?:0?[1-9]|1[0-2])\1(?:19|20)[0-9]{2}$")) return null;
        var decimalPosition = Math.Max(compact.LastIndexOf(','), compact.LastIndexOf('.'));
        if (decimalPosition < 0)
        {
            if (!permitirEntero) return null;
            return decimal.TryParse(compact, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var entero)
                ? (signoSufijo ? -Math.Abs(entero) : entero)
                : null;
        }
        var integerPart = Regex.Replace(compact[..decimalPosition], "[.,]", string.Empty);
        var decimalPart = compact[(decimalPosition + 1)..];
        return decimal.TryParse(integerPart + "." + decimalPart, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? (signoSufijo ? -Math.Abs(parsed) : parsed)
            : null;
    }

    /// <summary>Quita las marcas diacríticas para comparar etiquetas de forma estable.</summary>
    internal static string QuitarDiacriticos(string? value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
