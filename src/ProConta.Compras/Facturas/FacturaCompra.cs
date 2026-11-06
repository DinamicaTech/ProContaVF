using ProConta.Compras.Reconocimiento;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Facturas;

/// <summary>Un tipo de IVA de la factura de compra (DesglosesFacturaCompra). <c>TipoIva</c> es un porcentaje (21 = 21 %).</summary>
public sealed record DesgloseFacturaCompra(short Orden, decimal TipoIva, decimal BaseImponible, decimal CuotaIva, decimal? Confianza = null, string? Evidencia = null);

/// <summary>Tipo de operación de la compra (FacturasCompra.TipoOperacion, con los códigos del ERP).</summary>
public enum OperacionCompra { Nacional = 0, Intracomunitaria = 1, Importacion = 2 }

/// <summary>Tipo de producto (FacturasCompra.TipoProducto, con los códigos del ERP).</summary>
public enum ProductoCompra { Servicios = 1, Bienes = 2 }

/// <summary>Clase de la retención (FacturasCompra.TipoRetencion): profesionales (servicios remunerados) o arrendamientos.</summary>
public enum ClaseRetencion { Profesionales = 1, Arrendamientos = 2 }

/// <summary>Tipo de retención que se ofrece en la ficha (catálogo TiposRetencion de organizacion/sociedades-ejercicios).</summary>
public sealed record TipoRetencionCompra(decimal Porcentaje, string Descripcion, ClaseRetencion Clase);

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

    public OperacionCompra Operacion { get; set; }
    public ProductoCompra? Producto { get; set; }
    public ClaseRetencion? ClaseRetencion { get; set; }
    /// <summary>Gasto que el proveedor paga por cuenta nuestra, sin IVA (suma al total).</summary>
    public decimal? Suplido { get; set; }
    /// <summary>Cuenta de gasto (FacturasCompra.ConceptoGasto).</summary>
    public string? CuentaGasto { get; set; }
    public int? IdCentroCoste { get; set; }
    /// <summary>Cuenta de la forma de pago grabada por el ERP (dato heredado; la de ProContaVF es <see cref="IdFormaPago"/>).</summary>
    public string? CuentaFormaPago { get; set; }
    /// <summary>
    /// Forma de pago (FormasPago); su subcuenta en la empresa sale de FormasPagoSociedad. Al guardar pasa a ser la forma de
    /// pago por defecto del proveedor, que se propone en sus facturas nuevas (compras/pantallas 2026-11-06).
    /// </summary>
    public byte? IdFormaPago { get; set; }
    public string? Notas { get; set; }

    /// <summary>Contabilizada (FacturasCompra.Exportada): ya no se edita aquí.</summary>
    public bool Contabilizada { get; set; }
    /// <summary>Marcada como duplicada en el ERP.</summary>
    public bool Duplicada { get; set; }

    /// <summary>Código ISO de la moneda de la factura (EUR si es nacional).</summary>
    public string Divisa { get; set; } = "EUR";
    public decimal? ImporteTotalDivisa { get; set; }
    public decimal? TipoCambioAplicado { get; set; }
    public DateTime? FechaTipoCambio { get; set; }

    /// <summary>Factura del mismo proveedor a la que rectifica (rectificativa o abono).</summary>
    public int? IdRegistroFacturaRectificada { get; set; }

    /// <summary>Ruta completa del PDF: la grabada al registrarlo o, si ya no existe, la reubicada en la carpeta de la empresa al cargarla.</summary>
    public required string RutaPdf { get; set; }
    /// <summary>Rutas grabadas en la BD (ArchivoOriginal y Archivo) al cargar la factura; vacía en una factura nueva.</summary>
    public IReadOnlyList<string> RutasGuardadas { get; init; } = [];
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
        if (Fecha is { } fecha && Ejercicio.Ejercicio > 0 && Math.Abs(fecha.Year - Ejercicio.Ejercicio) > 1)
            errores.Add(new Error("Compras.FechaFuera", $"La fecha {fecha:dd/MM/yyyy} está muy lejos del ejercicio {Ejercicio.Ejercicio}.", Rotulo, null, "Comprueba el año de la fecha."));
        if (BaseImponible is { } bi && ImporteTotal is { } it && bi != 0 && it != 0 && bi < 0 != it < 0)
            errores.Add(new Error("Compras.Signos", "La base y el total tienen signos distintos.", Rotulo, "En un abono, los dos van en negativo.", "Corrige el signo."));
        if (CuentaGasto is { Length: > 10 })
            errores.Add(new Error("Compras.CuentaGasto", $"La cuenta de gasto «{CuentaGasto}» es demasiado larga.", Rotulo, null, "Elige una cuenta del plan."));
        if (PorcentajeRetencion is < 0 or > 100)
            errores.Add(new Error("Compras.Retencion", "El porcentaje de retención debe estar entre 0 y 100.", Rotulo));
        if (RutaPdf.Length > 255)
            errores.Add(new Error("Compras.RutaLarga", "La ruta del PDF pasa de 255 caracteres y no cabe en la ficha.", Rotulo, null, "Acorta el nombre del fichero o de la carpeta."));
        return errores.Count == 0 ? Resultado.Correcto() : Resultado.Fallo(errores);
    }

    /// <summary>Base + cuota − retención + suplido = total, cuando los tres importes están.</summary>
    public bool Cuadra => BaseImponible is { } b && CuotaIva is { } c && ImporteTotal is { } t && b + c - (ImporteRetencion ?? 0m) + (Suplido ?? 0m) == t;

    /// <summary>Lo que falta para que la factura esté lista para contabilizar (proveedor, número, fecha e importes que cuadren).</summary>
    public IReadOnlyList<string> Pendiente()
    {
        var l = new List<string>();
        if (IdProveedor is null && string.IsNullOrWhiteSpace(NifProveedor)) l.Add("proveedor");
        if (string.IsNullOrWhiteSpace(NumeroFactura)) l.Add("número");
        if (Fecha is null) l.Add("fecha");
        if (ImporteTotal is null) l.Add("total");
        else if (!Cuadra) l.Add("importes que cuadren");
        // Con inversión del sujeto pasivo la factura no lleva IVA: lo autorrepercute la empresa (compras/pantallas 2026-11-06 12:10).
        if (InversionSujetoPasivo && (CuotaIva ?? 0m) != 0m) l.Add("quitar la cuota de IVA (con inversión del sujeto pasivo no lleva IVA)");
        return l;
    }

    /// <summary>Con proveedor, número, fecha e importes que cuadran.</summary>
    public bool Lista => Pendiente().Count == 0;

    /// <summary>
    /// Añade avisos al log del reconocimiento (los ve la ficha) y marca la factura para revisión. Un log que no es el
    /// JSON del reconocimiento se sustituye por uno con solo los avisos.
    /// </summary>
    public void AnadirAvisos(IReadOnlyList<string> avisos)
    {
        if (avisos.Count == 0) return;
        System.Text.Json.Nodes.JsonObject raiz;
        try
        {
            raiz = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(Log) ? "{}" : Log) as System.Text.Json.Nodes.JsonObject ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            raiz = [];
        }
        if (raiz["advertencias"] is not System.Text.Json.Nodes.JsonArray lista)
            raiz["advertencias"] = lista = [];
        foreach (var a in avisos) lista.Add(a);
        Log = raiz.ToJsonString();
        RequiereRevision = true;
    }

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
