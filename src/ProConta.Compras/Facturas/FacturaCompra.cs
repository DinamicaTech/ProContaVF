using ProConta.Compras.Reconocimiento;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Facturas;

/// <summary>Un tipo de IVA de la factura de compra (DesglosesFacturaCompra). <c>TipoIva</c> es un porcentaje (21 = 21 %).</summary>
public sealed record DesgloseFacturaCompra(short Orden, decimal TipoIva, decimal BaseImponible, decimal CuotaIva, decimal? Confianza = null, string? Evidencia = null);

/// <summary>Proveedor al que pertenece la carpeta de un PDF, tal como lo resuelve el contexto de la empresa.</summary>
public sealed record ProveedorCarpeta(int? IdProveedor, string? CuentaContable, string? Nif, string Carpeta);

/// <summary>
/// Ente factura de compra (FacturasCompra con sus DesglosesFacturaCompra), según la estructura nueva de
/// compras/factura-compra (2026-11-05 11:24). Los importes se guardan en euros; si la factura viene en otra moneda,
/// <see cref="ImporteTotalDivisa"/> conserva el original con el cambio aplicado y su fecha.
/// </summary>
public sealed class FacturaCompra
{
    public int IdRegistro { get; set; }
    public required ContextoSociedadEjercicio Ejercicio { get; init; }
    public int? IdProveedor { get; set; }
    public string? CuentaContableProveedor { get; set; }
    public string? NifProveedor { get; set; }
    public string? NombreProveedorAeat { get; set; }

    /// <summary>Número limpio: solo letras ASCII y dígitos.</summary>
    public string NumeroFactura { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public string Concepto { get; set; } = string.Empty;

    public decimal? BaseImponible { get; set; }
    public decimal? CuotaIva { get; set; }
    public decimal? ImporteTotal { get; set; }
    public decimal? PorcentajeRetencion { get; set; }
    public decimal? ImporteRetencion { get; set; }
    public bool InversionSujetoPasivo { get; set; }
    public List<DesgloseFacturaCompra> Desgloses { get; } = [];

    /// <summary>Código ISO de la moneda de la factura (EUR si es nacional).</summary>
    public string Divisa { get; set; } = "EUR";
    public decimal? ImporteTotalDivisa { get; set; }
    public decimal? TipoCambioAplicado { get; set; }
    public DateTime? FechaTipoCambio { get; set; }

    /// <summary>Factura del mismo proveedor a la que rectifica (rectificativa o abono).</summary>
    public int? IdRegistroFacturaRectificada { get; set; }

    /// <summary>Ruta completa del PDF al registrarlo.</summary>
    public required string RutaPdf { get; init; }
    /// <summary>Carpeta del proveedor (primer nivel bajo «Proveedores»).</summary>
    public string Carpeta { get; set; } = string.Empty;
    public string NombreFichero => Path.GetFileName(RutaPdf);
    /// <summary>MD5 de los bytes del PDF en hexadecimal minúsculo: identifica el PDF para los duplicados.</summary>
    public required string Md5 { get; init; }

    public decimal Confianza { get; set; }
    public bool RequiereRevision { get; set; }
    /// <summary>pdf-local u ocr.</summary>
    public string Fuente { get; set; } = "pdf-local";
    /// <summary>JSON versionado del reconocimiento.</summary>
    public string Log { get; set; } = string.Empty;
    public DateTime FechaHoraRegistro { get; set; }
    public int Version { get; set; }

    public bool EsDivisaExtranjera => !string.Equals(Divisa, "EUR", StringComparison.OrdinalIgnoreCase);

    /// <summary>Texto para mensajes: «Proveedor Fact: número» o el fichero si aún no tiene número.</summary>
    public string Rotulo => string.IsNullOrWhiteSpace(NumeroFactura) ? $"PDF {NombreFichero}" : $"Factura de compra {NumeroFactura}";

    /// <summary>
    /// Factura lista para registrar a partir de lo reconocido. En moneda extranjera, los euros salen del cambio oficial del
    /// día de la factura: total = total en divisa × cambio, cuota = cuota × cambio y base = total − cuota, todo redondeado a
    /// céntimos (alejándose de cero), para que base + cuota = total también en euros. Sin cambio, los euros quedan vacíos.
    /// </summary>
    public static FacturaCompra DesdeReconocimiento(FacturaReconocida reconocida, ContextoSociedadEjercicio ejercicio, ProveedorCarpeta? proveedor)
    {
        ArgumentNullException.ThrowIfNull(reconocida);
        ArgumentNullException.ThrowIfNull(ejercicio);
        var r = reconocida.Lectura;
        var f = new FacturaCompra
        {
            Ejercicio = ejercicio,
            RutaPdf = reconocida.RutaPdf,
            Md5 = reconocida.Md5,
            IdProveedor = proveedor?.IdProveedor,
            CuentaContableProveedor = proveedor?.CuentaContable,
            Carpeta = proveedor?.Carpeta ?? CarpetaDe(reconocida.RutaPdf),
            NifProveedor = Vacio(r.NifProveedor),
            NumeroFactura = reconocida.NumeroFactura,
            Fecha = r.FechaFactura,
            Concepto = reconocida.Concepto,
            PorcentajeRetencion = r.TipoRetencion,
            ImporteRetencion = r.ImporteRetencion,
            InversionSujetoPasivo = r.InversionSujetoPasivo,
            Divisa = string.IsNullOrWhiteSpace(r.Divisa) ? "EUR" : r.Divisa.Trim().ToUpperInvariant(),
            Confianza = reconocida.Confianza,
            RequiereRevision = reconocida.RequiereRevision,
            Fuente = reconocida.Fuente,
            Log = reconocida.Log
        };

        if (!f.EsDivisaExtranjera)
        {
            f.BaseImponible = r.BaseImponible;
            f.CuotaIva = r.CuotaIva;
            f.ImporteTotal = r.ImporteTotal;
            short orden = 1;
            foreach (var d in r.DesglosesIva.OrderBy(d => d.Tipo))
                f.Desgloses.Add(new DesgloseFacturaCompra(orden++, d.Tipo, d.Base, d.Cuota, d.Confianza, Vacio(d.Evidencia)));
            return f;
        }

        f.ImporteTotalDivisa = r.ImporteTotalDivisa ?? r.ImporteTotal;
        f.TipoCambioAplicado = r.TipoCambioAplicado;
        f.FechaTipoCambio = r.FechaTipoCambio;
        if (r.ImporteTotalEuros is { } total && r.TipoCambioAplicado is { } cambio)
        {
            var cuota = Redondear((r.CuotaIva ?? 0m) * cambio);
            f.ImporteTotal = total;
            f.CuotaIva = cuota;
            f.BaseImponible = total - cuota;
            if (r.DesglosesIva.Count == 1)
            {
                var d = r.DesglosesIva[0];
                f.Desgloses.Add(new DesgloseFacturaCompra(1, d.Tipo, total - cuota, cuota, d.Confianza, Vacio(d.Evidencia)));
            }
        }
        return f;
    }

    /// <summary>Reglas que impiden guardar: lo que falte o no cuadre por el reconocimiento se marca para revisión, no se rechaza.</summary>
    public Resultado Validar()
    {
        var errores = new List<Error>();
        if (Md5.Length != 32 || !Md5.All(Uri.IsHexDigit))
            errores.Add(new Error("Compras.SinMd5", "No puedo registrar el PDF sin su huella MD5.", Rotulo, "Falta calcularla al leer el PDF.", "Vuelve a buscar los PDF nuevos."));
        if (Ejercicio.IdSociedadEjercicio <= 0)
            errores.Add(new Error("Compras.SinEjercicio", "No sé en qué ejercicio registrar la factura de compra.", Rotulo, null, "Elige la empresa y el ejercicio."));
        if (Desgloses.Select(d => d.Orden).Distinct().Count() != Desgloses.Count)
            errores.Add(new Error("Compras.DesgloseRepetido", "La factura tiene dos desgloses de IVA con el mismo orden.", Rotulo));
        if (Divisa.Length != 3 || !Divisa.All(char.IsAsciiLetterUpper))
            errores.Add(new Error("Compras.Divisa", $"La moneda «{Divisa}» no es un código ISO de tres letras.", Rotulo, null, "Escribe el código, por ejemplo USD."));
        if (IdRegistroFacturaRectificada is { } rect && rect == IdRegistro && IdRegistro != 0)
            errores.Add(new Error("Compras.RectificaASiMisma", "Una factura no puede rectificarse a sí misma.", Rotulo, null, "Elige la factura original."));
        if (RutaPdf.Length > 255)
            errores.Add(new Error("Compras.RutaLarga", "La ruta del PDF pasa de 255 caracteres y no cabe en la ficha.", Rotulo, null, "Acorta el nombre del fichero o de la carpeta."));
        return errores.Count == 0 ? Resultado.Correcto() : Resultado.Fallo(errores);
    }

    /// <summary>Base + cuota = total (con la retención aparte), cuando los tres importes están.</summary>
    public bool Cuadra => BaseImponible is { } b && CuotaIva is { } c && ImporteTotal is { } t && b + c - (ImporteRetencion ?? 0m) == t;

    /// <summary>Primer nivel bajo la carpeta «Proveedores» (o la carpeta del PDF, si no está dentro de una).</summary>
    public static string CarpetaDe(string rutaPdf)
    {
        var partes = rutaPdf.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var i = Array.FindLastIndex(partes, p => p.Equals("Proveedores", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < partes.Length - 1) return partes[i + 1];
        return partes.Length >= 2 ? partes[^2] : string.Empty;
    }

    private static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
