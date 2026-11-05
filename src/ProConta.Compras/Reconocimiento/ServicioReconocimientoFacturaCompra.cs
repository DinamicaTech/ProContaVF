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
}

/// <summary>
/// Orquesta el reconocimiento de un PDF: extracción nativa, OCR solo si el PDF no tiene texto nativo útil
/// (compras/reconocimiento 2026-11-05 11:24), conversión de divisa, confianza global, revisión y log.
/// No escribe en la BD: el registro lo hace el servicio de facturas de compra, y el modo «Probar» usa esto tal cual.
/// </summary>
public sealed class ServicioReconocimientoFacturaCompra(
    IExtractorDocumentoFacturaCompra extractorLocal,
    IExtractorOcrFacturaCompra? extractorOcr = null,
    IProveedorTiposCambioHistoricos? tiposCambio = null)
{
    private readonly MotorReconocimientoFacturaCompra _motor = new();

    public async Task<FacturaReconocida> ReconocerAsync(string rutaPdf, ContextoProcesamiento contexto, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(rutaPdf)) throw new ArgumentException("Debe indicarse el PDF de compra.", nameof(rutaPdf));
        ArgumentNullException.ThrowIfNull(contexto);
        if (!File.Exists(rutaPdf))
            throw new FileNotFoundException($"No encuentro el PDF. Ruta intentada: {rutaPdf}. Comprueba que la carpeta compartida esté conectada.", rutaPdf);

        var local = _motor.Reconocer(extractorLocal.Extraer(rutaPdf), contexto.Reconocimiento);
        var elegido = local;
        var fuente = "pdf-local";
        var razonOcr = string.Empty;
        var advertencias = new List<string>(local.Advertencias);

        if (local.RequiereOcr)
        {
            if (contexto.Configuracion.Activo && contexto.Configuracion.UsarOcr && extractorOcr is not null)
            {
                razonOcr = "pdf_sin_texto_nativo";
                elegido = _motor.Reconocer(await extractorOcr.ExtraerAsync(rutaPdf, cancelacion).ConfigureAwait(false), contexto.Reconocimiento);
                fuente = "ocr";
                advertencias = [.. elegido.Advertencias, "Leído con OCR porque el PDF no tiene texto nativo."];
            }
            else advertencias.Add("El OCR no está activo para esta sociedad: rellena los campos a mano.");
        }

        if (elegido.RequiereConversionDivisa)
        {
            var convertido = tiposCambio is not null &&
                             await new ConversorDivisaFacturaCompra(tiposCambio).ConvertirAEurosAsync(elegido, cancelacion).ConfigureAwait(false);
            if (!convertido) advertencias.Add($"No hay cambio oficial de {elegido.Divisa} a EUR para la fecha de la factura: no se podrá contabilizar sin él.");
        }
        elegido.Advertencias = advertencias;

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
            Log = log
        };
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
