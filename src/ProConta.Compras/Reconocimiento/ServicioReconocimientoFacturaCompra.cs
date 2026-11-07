using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProConta.Compras.Reconocimiento;

/// <summary>Parámetros del reconocimiento de una sociedad (ConfiguracionReconocimientoFacturasCompra).</summary>
public sealed class ConfiguracionReconocimiento
{
    public bool Activo { get; init; } = true;
    public bool SociedadTieneVies { get; init; }
    /// <summary>OCR (Textract) para los PDF sin texto nativo útil. Desactivable por sociedad.</summary>
    public bool UsarOcr { get; init; }
    public decimal UmbralAceptacion { get; init; } = 0.9m;
    public decimal UmbralDiscrepanciaImporte { get; init; } = 0.02m;
}

/// <summary>Lo que el reconocimiento necesita saber del proveedor y de la sociedad, ya cargado.</summary>
public sealed class ContextoProcesamiento
{
    public ConfiguracionReconocimiento Configuracion { get; init; } = new();
    public ContextoReconocimientoFacturaCompra Reconocimiento { get; init; } = new();
    /// <summary>Facturas completas ya registradas del proveedor de la carpeta: dos o más lo consolidan.</summary>
    public int FacturasAceptadasProveedor { get; init; }
    /// <summary>Plantillas de campo del proveedor de la carpeta, de más a menos aciertos.</summary>
    public IReadOnlyList<PlantillaCampo> Plantillas { get; init; } = [];
    /// <summary>
    /// Plantillas de la empresa por NIF del proveedor (solo letras y dígitos, en mayúsculas): se aplican cuando el NIF ya
    /// se ha leído, también a un PDF de otra carpeta o de la raíz (compras/reconocimiento 2026-11-07).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlantillaCampo>> PlantillasPorNif { get; init; } = new Dictionary<string, IReadOnlyList<PlantillaCampo>>();
}

/// <summary>Resultado final del reconocimiento de un PDF, listo para registrar o para mostrar en el modo «Probar».</summary>
public sealed class FacturaReconocida
{
    public required string RutaPdf { get; init; }
    public required string Md5 { get; init; }
    public required ResultadoReconocimientoFacturaCompra Lectura { get; init; }
    /// <summary>Número limpio para registrar: solo letras ASCII y dígitos.</summary>
    public required string NumeroFactura { get; init; }
    public required string Concepto { get; init; }
    public required decimal Confianza { get; init; }
    public required bool RequiereRevision { get; init; }
    /// <summary>pdf-local o ocr.</summary>
    public required string Fuente { get; init; }
    /// <summary>JSON versionado que explica cada decisión (FacturasCompra.LogImportacion).</summary>
    public required string Log { get; init; }
    /// <summary>Texto del documento leído (nativo u OCR), para buscar los textos clave.</summary>
    public string Texto { get; init; } = string.Empty;
}

/// <summary>
/// Orquesta el reconocimiento de un PDF: extracción nativa, OCR solo si el PDF no tiene texto nativo útil
/// (compras/reconocimiento 2026-11-05 11:24), conversión de divisa, confianza global, revisión y log.
/// No escribe en la BD: el registro lo hace el servicio de facturas de compra, y el modo «Probar» usa esto tal cual.
/// </summary>
public sealed class ServicioReconocimientoFacturaCompra(
    IExtractorDocumentoFacturaCompra extractorLocal,
    IExtractorOcrFacturaCompra? extractorOcr = null,
    IProveedorTiposCambioHistoricos? tiposCambio = null,
    string? motivoSinOcr = null)
{
    private readonly MotorReconocimientoFacturaCompra _motor = new();

    public async Task<FacturaReconocida> ReconocerAsync(string rutaPdf, ContextoProcesamiento contexto, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(rutaPdf)) throw new ArgumentException("Debe indicarse el PDF de compra.", nameof(rutaPdf));
        ArgumentNullException.ThrowIfNull(contexto);
        if (!File.Exists(rutaPdf))
            throw new FileNotFoundException($"No encuentro el PDF. Ruta intentada: {rutaPdf}. Comprueba que la carpeta compartida esté conectada.", rutaPdf);

        var documentoLocal = extractorLocal.Extraer(rutaPdf);
        var local = _motor.Reconocer(documentoLocal, contexto.Reconocimiento);
        var documento = documentoLocal;
        var elegido = local;
        var fuente = "pdf-local";
        var razonOcr = string.Empty;
        var advertencias = new List<string>(local.Advertencias);

        if (local.RequiereOcr)
        {
            if (contexto.Configuracion.Activo && contexto.Configuracion.UsarOcr && extractorOcr is not null)
            {
                razonOcr = "pdf_sin_texto_nativo";
                documento = await extractorOcr.ExtraerAsync(rutaPdf, cancelacion).ConfigureAwait(false);
                elegido = _motor.Reconocer(documento, contexto.Reconocimiento);
                fuente = "ocr";
                advertencias = [.. elegido.Advertencias, "Leído con OCR porque el PDF no tiene texto nativo."];
            }
            else advertencias.Add(contexto.Configuracion.Activo && contexto.Configuracion.UsarOcr && extractorOcr is null
                ? $"El PDF no tiene texto y este equipo no tiene el OCR configurado{(motivoSinOcr is null ? "" : $" ({motivoSinOcr.TrimEnd('.')})")}: rellena los campos a mano."
                : "El OCR no está activo para esta sociedad: rellena los campos a mano.");
        }

        elegido.Advertencias = advertencias;
        // Dos pasos: las de la carpeta (que pueden leer el NIF) y después las del NIF leído, que mandan.
        AplicarPlantillas(documento, elegido, contexto.Plantillas);
        if (PlantillasDelNif(contexto, elegido.NifProveedor) is { Count: > 0 } delNif) AplicarPlantillas(documento, elegido, delNif);

        if (elegido.RequiereConversionDivisa)
        {
            var convertido = tiposCambio is not null &&
                             await new ConversorDivisaFacturaCompra(tiposCambio).ConvertirAEurosAsync(elegido, cancelacion).ConfigureAwait(false);
            if (!convertido) elegido.Advertencias = [.. elegido.Advertencias, $"No hay cambio oficial de {elegido.Divisa} a EUR para la fecha de la factura: no se podrá contabilizar sin él."];
        }

        var confianza = ConfianzaGlobal(elegido, contexto);
        var requiereRevision = RequiereRevision(elegido, local, confianza, contexto);
        var numero = LimpiarNumeroParaRegistro(elegido.NumeroFactura);
        var log = JsonSerializer.Serialize(new
        {
            version = 1,
            fechaProcesoUtc = DateTime.UtcNow,
            confianza,
            requiereRevision,
            fuenteElegida = fuente,
            razonOcr,
            facturasAceptadasProveedor = contexto.FacturasAceptadasProveedor,
            candidato = elegido,
            candidatoLocal = ReferenceEquals(elegido, local) ? null : local,
            advertencias = elegido.Advertencias
        });

        return new FacturaReconocida
        {
            RutaPdf = rutaPdf,
            Md5 = CalcularMd5(rutaPdf),
            Lectura = elegido,
            NumeroFactura = numero,
            Concepto = ComponerConcepto(elegido.ProveedorIdentificado, numero),
            Confianza = confianza,
            RequiereRevision = requiereRevision,
            Fuente = fuente,
            Log = log,
            Texto = documento.TextoCompleto
        };
    }

    /// <summary>Texto y geometría del PDF con el extractor local, para aprender plantillas al guardar una factura revisada.</summary>
    public DocumentoFacturaExtraido Extraer(string rutaPdf) => extractorLocal.Extraer(rutaPdf);

    /// <summary>
    /// Los campos que una plantilla del proveedor encuentra se toman de ahí con confianza 1 (compras/reconocimiento
    /// 2026-11-05 18:42): la primera plantilla de cada campo, por aciertos, cuyo rótulo aparece y da un valor válido.
    /// </summary>
    internal static void AplicarPlantillas(DocumentoFacturaExtraido documento, ResultadoReconocimientoFacturaCompra r, IReadOnlyList<PlantillaCampo> plantillas)
    {
        var aplicados = new List<string>();
        foreach (var grupo in plantillas.GroupBy(p => p.Campo))
        {
            var valor = grupo.OrderByDescending(p => p.Aciertos).Select(p => PlantillasReconocimiento.Leer(documento, p)).FirstOrDefault(v => v is not null);
            if (valor is null) continue;
            switch (grupo.Key)
            {
                case PlantillasReconocimiento.Nif:
                    r.NifProveedor = valor; r.ConfianzaNif = 1m; r.NifAsignadoDesdeProveedor = false; break;
                case PlantillasReconocimiento.Numero:
                    r.NumeroFactura = valor; r.ConfianzaNumero = 1m; break;
                case PlantillasReconocimiento.Fecha:
                    r.FechaFactura = DateTime.ParseExact(valor, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); r.ConfianzaFecha = 1m; break;
                case PlantillasReconocimiento.Total:
                    var total = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);
                    r.ImporteTotal = total; r.ImporteTotalDivisa = total;
                    // Los importes solo pasan a 1 si la base y la cuota leídas cuadran con ese total.
                    if (r.BaseImponible is { } b && r.CuotaIva is { } c && b + c - (r.ImporteRetencion ?? 0m) == total) r.ConfianzaImportes = 1m;
                    break;
                case PlantillasReconocimiento.Concepto:
                    r.ConceptoFactura = valor; break;
                default: continue;
            }
            aplicados.Add(grupo.Key);
        }
        if (aplicados.Count == 0) return;
        r.CamposPlantilla = [.. r.CamposPlantilla.Union(aplicados)];
        r.Advertencias = r.Advertencias.Where(a => !(aplicados.Contains(PlantillasReconocimiento.Fecha) && a.Contains("fecha de factura", StringComparison.Ordinal))
                                                 && !(aplicados.Contains(PlantillasReconocimiento.Numero) && a.Contains("número de factura", StringComparison.Ordinal))
                                                 && !(aplicados.Contains(PlantillasReconocimiento.Nif) && a.Contains("NIF del proveedor", StringComparison.Ordinal))
                                                 && !(aplicados.Contains(PlantillasReconocimiento.Total) && a.Contains("importe total", StringComparison.Ordinal))).ToList();
    }

    /// <summary>Plantillas del NIF leído (con o sin prefijo de país).</summary>
    internal static IReadOnlyList<PlantillaCampo> PlantillasDelNif(ContextoProcesamiento contexto, string? nif)
    {
        var clave = new string((nif ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (clave.Length == 0) return [];
        if (contexto.PlantillasPorNif.TryGetValue(clave, out var p)) return p;
        if (clave.Length > 9 && clave.StartsWith("ES", StringComparison.Ordinal) && contexto.PlantillasPorNif.TryGetValue(clave[2..], out p)) return p;
        return contexto.PlantillasPorNif.TryGetValue("ES" + clave, out p) ? p : [];
    }

    /// <summary>50 % importes, 20 % fecha, 20 % número y 10 % NIF; el histórico aceptado suma hasta 0,08.</summary>
    internal static decimal ConfianzaGlobal(ResultadoReconocimientoFacturaCompra r, ContextoProcesamiento contexto)
    {
        var @base = r.ConfianzaImportes * 0.5m + r.ConfianzaFecha * 0.2m + r.ConfianzaNumero * 0.2m + r.ConfianzaNif * 0.1m;
        if (contexto.FacturasAceptadasProveedor >= 2) @base += Math.Min(0.08m, contexto.FacturasAceptadasProveedor * 0.01m);
        return Math.Min(1m, @base);
    }

    /// <summary>Casos que piden revisión del operador (especificación del reconocimiento, «Confianza y revisión»).</summary>
    internal static bool RequiereRevision(ResultadoReconocimientoFacturaCompra r, ResultadoReconocimientoFacturaCompra local, decimal confianza, ContextoProcesamiento contexto)
        => !r.ImporteTotal.HasValue || !r.FechaFactura.HasValue || string.IsNullOrWhiteSpace(r.NumeroFactura) ||
           r.PosiblesMultiplesFacturas || r.RequiereOcr && ReferenceEquals(r, local) ||
           r.RequiereConversionDivisa && !r.ImporteTotalEuros.HasValue ||
           r.Advertencias.Any(a => a.Contains("difiere del CIF registrado", StringComparison.Ordinal) || a.Contains("VIES", StringComparison.Ordinal)) ||
           confianza < contexto.Configuracion.UmbralAceptacion;

    /// <summary>Número persistido: exclusivamente letras ASCII y dígitos.</summary>
    internal static string LimpiarNumeroParaRegistro(string? numero) => Regex.Replace(numero ?? string.Empty, "[^A-Za-z0-9]", string.Empty);

    /// <summary>MD5 de los bytes del PDF en hexadecimal minúsculo, compatible con ProConta.</summary>
    public static string CalcularMd5(string fichero)
    {
        using var contenido = new FileStream(fichero, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(MD5.HashData(contenido)).ToLowerInvariant();
    }

    /// <summary>«Proveedor Fact: número»; solo «Fact: número» sin proveedor, o solo el proveedor sin número.</summary>
    internal static string ComponerConcepto(string? nombreProveedor, string? numeroFactura)
    {
        var proveedor = (nombreProveedor ?? string.Empty).Trim();
        var numero = (numeroFactura ?? string.Empty).Trim();
        if (proveedor.Length == 0) return numero.Length == 0 ? string.Empty : $"Fact: {numero}";
        return numero.Length == 0 ? proveedor : $"{proveedor} Fact: {numero}";
    }
}
