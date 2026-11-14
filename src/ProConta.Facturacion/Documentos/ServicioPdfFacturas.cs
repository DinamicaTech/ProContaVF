using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;
using ProConta.Informes;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;

namespace ProConta.Facturacion.Documentos;

/// <summary>Domicilio del emisor para el PDF (el congelado en la factura o, si no lo tiene, el actual de la sociedad).</summary>
public sealed record DomicilioEmisor(string? Direccion, string? CodigoPostal, string? Poblacion, string? Provincia);

/// <summary>
/// De una factura al modelo de su plantilla y al nombre de su fichero, sin BD: los importes salen del cálculo de la factura
/// y se formatean aquí, para que la plantilla no calcule nada.
/// </summary>
public static class ConstructorPdfFactura
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private static string Euros(decimal v) => v.ToString("N2", Es) + " €";
    private static string Numero(decimal v, string formato = "0.####") => v.ToString(formato, Es);
    private static string Fecha(DateTime? d) => d is { } x ? x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";

    private static string Lineas(params string?[] partes) =>
        string.Join(Environment.NewLine, partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    private static string Domicilio(string? direccion, string? codigoPostal, string? poblacion, string? provincia) =>
        Lineas(direccion, string.Join(" ", new[] { codigoPostal, poblacion }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())), provincia);

    public static (DatosFacturaPdf Datos, IReadOnlyList<LineaFacturaPdf> Lineas, IReadOnlyList<ResumenFacturaPdf> Resumen) Construir(
        FacturaVenta f, InstantaneaCliente cliente, string nombreEmisor, string nifEmisor, DomicilioEmisor emisor, string? formaPago, PresentacionPdf? presentacion = null)
    {
        var p = presentacion ?? PresentacionPdf.Vacia;
        var proforma = f.NumeroFactura is null;
        var t = f.Totales;
        var periodo = f.FechaInicioValidez is not null || f.FechaFinValidez is not null ? $"{Fecha(f.FechaInicioValidez)} – {Fecha(f.FechaFinValidez)}" : "";
        var pago = string.Join(" · ", new[] { string.IsNullOrWhiteSpace(formaPago) ? null : formaPago.Trim(), f.DiaDePago is byte d and > 0 ? $"día de pago {d}" : null }.Where(x => x is not null));
        var datos = new DatosFacturaPdf
        {
            Titulo = proforma ? "FACTURA PROFORMA" : f.EsRectificativa ? "FACTURA RECTIFICATIVA" : "FACTURA",
            Aviso = proforma ? "Documento informativo sin validez fiscal" : "",
            Numero = proforma ? FacturaVenta.RotuloBorrador(f.IdFactura) : f.NumeroFactura!,
            Fecha = Fecha(f.FechaFactura ?? (proforma ? DateTime.Today : null)),
            FechaOperacion = f.FechaOperacion is { } o && o.Date != f.FechaFactura?.Date ? $"Fecha de operación: {Fecha(o)}" : "",
            Periodo = periodo,
            Descripcion = f.Descripcion.Trim(),
            Rectifica = f.EsRectificativa ? Lineas(f.NumeroFacturaRectificada is { } n ? $"Rectifica a la factura {n}." : null, string.IsNullOrWhiteSpace(f.MotivoRectificacion) ? null : $"Motivo: {f.MotivoRectificacion.Trim()}") : "",
            EmisorNombre = nombreEmisor,
            EmisorNif = nifEmisor,
            EmisorDomicilio = Domicilio(emisor.Direccion, emisor.CodigoPostal, emisor.Poblacion, emisor.Provincia),
            ClienteNombre = cliente.NombreVisible,
            ClienteNif = cliente.Nif ?? "",
            ClienteDomicilio = Domicilio(cliente.Direccion, cliente.CodigoPostal, cliente.Poblacion, cliente.Provincia),
            EmisorContacto = string.IsNullOrWhiteSpace(p.Telefono) ? "" : $"Tel. {p.Telefono.Trim()}",
            Logo = p.Logo,
            FormaPago = pago,
            PagoEtiqueta = p.Pago?.Etiqueta ?? "",
            PagoCuenta = p.Pago?.Cuenta ?? "",
            PagoDetalle = p.Pago?.Detalle ?? "",
            Total = Euros(t.ImporteTotal.Valor)
        };
        var lineas = f.Lineas.Select(l =>
        {
            // Las líneas del ERP guardan en precio y descuentos valores que no lo son: si con ellos no sale su importe, se
            // muestran la cantidad y el precio que le corresponden, sin descuentos. Base y total son siempre los guardados.
            var v = Programadas.LineasProgramada.DesdeLineaFactura(l);
            return new LineaFacturaPdf(
                (l.Nombre ?? "").Trim(),
                Numero(v.Cantidad),
                Numero(v.PrecioUnitario, "#,##0.00##"),
                string.Join(" ", new[] { v.DescuentoPorcentaje != 0m ? Numero(v.DescuentoPorcentaje) + " %" : null, v.DescuentoImporte != 0m ? Euros(v.DescuentoImporte) : null }.Where(y => y is not null)),
                Numero(l.BaseImponible.Valor, "N2"),
                l.EsSuplido ? "Supl." : Numero(l.TipoIva) + " %",
                Numero(l.ImporteTotal.Valor, "N2"),
                Lineas(l.Descripcion, l.Fecha is { } x ? $"Fecha: {Fecha(x)}" : null));
        }).ToList();
        var resumen = new List<ResumenFacturaPdf> { new("Base imponible", Euros(t.BaseImponible.Valor)) };
        foreach (var d2 in CalculoFactura.Desglose(f.Lineas))
        {
            resumen.Add(new($"IVA {Numero(d2.TipoIva)} % sobre {Euros(d2.BaseImponible.Valor)}", Euros(d2.CuotaIva.Valor)));
            if (!d2.CuotaRecargo.EsCero) resumen.Add(new($"Recargo {Numero(d2.TipoRecargo)} %", Euros(d2.CuotaRecargo.Valor)));
        }
        if (!t.Suplidos.EsCero) resumen.Add(new("Suplidos", Euros(t.Suplidos.Valor)));
        if (!t.Retencion.EsCero) resumen.Add(new($"Retención {Numero(f.TipoRetencion)} %", Euros(-t.Retencion.Valor)));
        return (datos, lineas, resumen);
    }

    /// <summary>
    /// Bloque de pago (facturacion/factura-venta 2026-11-13 12:13): con domiciliación SEPA, la cuenta del cliente enmascarada; si
    /// no, y la forma de pago tiene cuenta bancaria de cobro en la empresa, su IBAN entero para la transferencia. Sin ninguna de
    /// las dos, solo sale la forma de pago.
    /// </summary>
    public static BloquePagoPdf? BloquePago(bool sepa, string? ibanCliente, string? ibanEmpresa, string? numeroFactura)
    {
        if (sepa)
            return Ibanes.Enmascarar(ibanCliente) is { } oculto ? new BloquePagoPdf("SE CARGARÁ EN SU CUENTA", oculto, "") : null;
        if (Ibanes.Agrupar(ibanEmpresa) is not { } iban) return null;
        return new BloquePagoPdf("INGRESAR EN LA CUENTA", iban,
            numeroFactura is null ? "Indique el número de factura en el concepto de la transferencia." : $"Indique «{numeroFactura}» en el concepto de la transferencia.");
    }

    /// <summary>
    /// Ruta del PDF relativa a la carpeta de documentos de la sociedad: &lt;ejercicio&gt;\FacturasVenta\&lt;número&gt;-&lt;razón
    /// social del cliente&gt;.pdf, sin los caracteres que Windows no admite en un nombre de fichero.
    /// </summary>
    public static string RutaRelativa(int ejercicio, string numero, string razonSocial)
    {
        var nombre = Limpiar($"{numero.Trim()}-{razonSocial.Trim()}");
        if (nombre.Length > 150) nombre = nombre[..150].TrimEnd(' ', '.');
        return Path.Combine(ejercicio.ToString(CultureInfo.InvariantCulture), "FacturasVenta", nombre + ".pdf");
    }

    private static string Limpiar(string texto)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(texto.Select(c => invalidos.Contains(c) ? ' ' : c).ToArray());
        return string.Join(" ", limpio.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', ' ');
    }
}

/// <summary>Rótulo, cuenta y aviso del bloque de pago de la factura.</summary>
public sealed record BloquePagoPdf(string Etiqueta, string Cuenta, string Detalle);

/// <summary>Lo que sale en la factura además de sus datos: el teléfono y el logo de la empresa y el bloque de pago.</summary>
public sealed record PresentacionPdf(string? Telefono, byte[]? Logo, BloquePagoPdf? Pago)
{
    public static readonly PresentacionPdf Vacia = new(null, null, null);
}

/// <summary>PDF listo para abrir: su ruta completa; una proforma es un fichero temporal que no se archiva.</summary>
public sealed record PdfFactura(string Ruta, bool EsProforma, bool Generado);

/// <summary>
/// PDF de las facturas (facturacion/factura-venta): una factura numerada se archiva en la carpeta de documentos de su
/// sociedad con los datos congelados, y abrirla otra vez no la vuelve a generar salvo que se pida; un borrador sale como
/// proforma temporal, sin número ni validez fiscal, con los datos actuales del cliente. Nunca cambia número ni importes.
/// </summary>
public sealed class ServicioPdfFacturas
{
    private readonly IBaseDatos _bd;
    private readonly ServicioFacturas _facturas;
    private readonly IServicioSociedades _sociedades;
    private readonly IGeneradorPdfFactura _generador;
    private readonly IAutorizadorAcciones _autorizador;
    private readonly IReloj _reloj;

    public ServicioPdfFacturas(IBaseDatos bd, ServicioFacturas facturas, IServicioSociedades sociedades, IGeneradorPdfFactura generador, IAutorizadorAcciones? autorizador = null, IReloj? reloj = null)
    {
        _bd = bd;
        _facturas = facturas;
        _sociedades = sociedades;
        _generador = generador;
        _autorizador = autorizador ?? AutorizadorTodo.Instancia;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    /// <summary>
    /// El PDF de la factura. Si ya está archivado, lo devuelve tal cual, salvo con <paramref name="regenerar"/>, que lo vuelve
    /// a crear desde los datos congelados con la plantilla actual.
    /// </summary>
    public async Task<Resultado<PdfFactura>> ObtenerAsync(int idFactura, Actor actor, bool regenerar = false)
    {
        var f = await _facturas.ObtenerAsync(idFactura);
        var rotulo = FacturaVenta.RotuloBorrador(idFactura);
        if (f is null) return Resultado<PdfFactura>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", rotulo, "Otro operador la ha borrado.", "Actualiza el listado."));
        var ejercicio = await _sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null) return Resultado<PdfFactura>.Fallo(new Error("Facturas.SinEjercicio", "El ejercicio de la factura no existe.", f.Rotulo));

        try
        {
            var soc = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
                "SELECT Nombre, CIF, DireccionFiscal, CodigoPostalFiscal, PoblacionFiscal, ProvinciaFiscal, RutaDocumentos, Telefono, Logo FROM Sociedades WHERE IdSociedad = ?",
                r => (Nombre: r.Texto("Nombre") ?? "", Nif: r.Texto("CIF") ?? "", Domicilio: new DomicilioEmisor(r.Texto("DireccionFiscal"), r.Texto("CodigoPostalFiscal"), r.Texto("PoblacionFiscal"), r.Texto("ProvinciaFiscal")),
                    Raiz: r.Texto("RutaDocumentos"), Telefono: r.Texto("Telefono"), Logo: r.Texto("Logo")), ejercicio.IdSociedad));
            if (soc == default) return Resultado<PdfFactura>.Fallo(new Error("Facturas.SinSociedad", "La sociedad de la factura no existe.", f.Rotulo));

            var proforma = f.NumeroFactura is null;
            string? destino = null;
            string? relativa = null;
            if (!proforma)
            {
                if (string.IsNullOrWhiteSpace(soc.Raiz))
                    return Resultado<PdfFactura>.Fallo(new Error("Facturas.SinCarpetaDocumentos", $"La empresa {soc.Nombre} no tiene carpeta de documentos, y es donde se guarda el PDF.", f.Rotulo, null,
                        "Escríbela en Configuración › Parámetros y tablas › Empresas (grupo «Rutas y bases de datos») y vuelve a pedir el PDF."));
                relativa = await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT PDFFactura FROM Facturas WHERE IdFactura = ?", idFactura)) as string;
                if (!regenerar && !string.IsNullOrWhiteSpace(relativa) && File.Exists(Path.Combine(soc.Raiz.Trim(), relativa)))
                    return Resultado<PdfFactura>.Correcto(new PdfFactura(Path.Combine(soc.Raiz.Trim(), relativa), false, false));
                if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar))
                    return Resultado<PdfFactura>.Fallo(new Error("Seguridad.SinPermiso", "No tienes permiso para generar el PDF de las facturas.", f.Rotulo, null, "Pídeselo a quien administra los permisos."));
                relativa = ConstructorPdfFactura.RutaRelativa(ejercicio.Ejercicio, f.NumeroFactura!, f.Cliente.NombreVisible);
                destino = Path.Combine(soc.Raiz.Trim(), relativa);
            }

            var cliente = proforma && f.IdCliente is int idCliente ? await _facturas.InstantaneaClienteAsync(idCliente) ?? f.Cliente : f.Cliente;
            var congelado = f.DireccionEmisor is not null ? new DomicilioEmisor(f.DireccionEmisor, f.CodigoPostalEmisor, f.PoblacionEmisor, f.ProvinciaEmisor) : soc.Domicilio;
            var forma = f.FormaDePago is byte fp
                ? await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT Descripcion, SEPA FROM FormasPago WHERE Id = ?", r => (Descripcion: r.Texto("Descripcion"), Sepa: r.Logico("SEPA")), (int)fp))
                : default;
            var bloque = await BloquePagoAsync(f, ejercicio.IdSociedad, forma.Sepa);
            var presentacion = new PresentacionPdf(soc.Telefono, LeerLogo(soc.Logo, soc.Raiz), bloque);
            var (datos, lineas, resumen) = ConstructorPdfFactura.Construir(f, cliente,
                string.IsNullOrWhiteSpace(f.NombreEmisor) ? ejercicio.RazonSocial : f.NombreEmisor!, string.IsNullOrWhiteSpace(f.NifEmisor) ? soc.Nif : f.NifEmisor!, congelado, forma.Descripcion, presentacion);
            var bytes = await Task.Run(() => _generador.Generar(proforma ? PlantillaFactura.Proforma : PlantillaFactura.Factura, datos, lineas, resumen));

            if (proforma)
            {
                var carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF", "Proformas");
                Directory.CreateDirectory(carpeta);
                var temporal = Path.Combine(carpeta, $"Proforma-{idFactura}-{_reloj.Ahora:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(temporal, bytes);
                return Resultado<PdfFactura>.Correcto(new PdfFactura(temporal, true, true));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destino!)!);
            // Primero a un temporal en la misma carpeta: un fallo a medias no deja un PDF cortado con el nombre definitivo.
            var parcial = destino + ".tmp";
            await File.WriteAllBytesAsync(parcial, bytes);
            File.Move(parcial, destino!, overwrite: true);
            f.Anotar(_reloj.Ahora, actor.Nombre, regenerar ? "PDF regenerado" : "PDF generado");
            await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Facturas SET PDFFactura = ?, Historial = ? WHERE IdFactura = ?", relativa, f.Historial, idFactura));
            return Resultado<PdfFactura>.Correcto(new PdfFactura(destino!, false, true));
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<PdfFactura>.Fallo(ex.Error);
        }
        catch (ErrorMotorPdf ex)
        {
            return Resultado<PdfFactura>.Fallo(new Error("Facturas.PdfSinMotor", ex.Message, f.Rotulo,
                "El PDF se genera con el componente WebView2 de Microsoft Edge, y en este equipo falta o no ha arrancado.", "Instala WebView2 desde " + ConversorHtmlPdf.UrlInstalador + " y vuelve a generar el PDF. La factura no cambia."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return Resultado<PdfFactura>.Fallo(new Error("Facturas.PdfNoGuardado", $"No he podido crear el PDF de {f.Rotulo}: {ex.Message}", f.Rotulo,
                "La carpeta no existe, no se puede escribir en ella o el PDF está abierto en otro programa.", "Comprueba la carpeta de documentos de la empresa, cierra el PDF si está abierto y repite. La factura no cambia."));
        }
    }

    /// <summary>Cuenta del bloque de pago: la del cliente (domiciliación) o la de cobro de la forma de pago en la empresa.</summary>
    private Task<BloquePagoPdf?> BloquePagoAsync(FacturaVenta f, string idSociedad, bool sepa) =>
        _bd.EjecutarAsync(async s =>
        {
            string? ibanCliente = null, ibanEmpresa = null;
            if (sepa && f.IdCliente is int idCliente)
                ibanCliente = await s.EscalarAsync("SELECT IBAN FROM Clientes WHERE IdCliente = ?", idCliente) as string;
            if (!sepa && f.FormaDePago is byte fp)
                ibanEmpresa = await s.EscalarAsync("SELECT b.Iban FROM FormasPagoSociedad AS fps INNER JOIN CuentasBancarias AS b ON fps.IdCuentaBancaria = b.IdCuentaBancaria " +
                    "WHERE fps.IdSociedad = ? AND fps.IdFormaPago = ?", idSociedad, (int)fp) as string;
            return ConstructorPdfFactura.BloquePago(sepa, ibanCliente, ibanEmpresa, f.NumeroFactura);
        });

    /// <summary>
    /// El logo de la empresa (Sociedades.Logo): una ruta relativa se busca en su carpeta de documentos. Si no está o no se puede
    /// leer, la factura sale con el nombre de la empresa en su lugar: un logo perdido no impide hacer facturas.
    /// </summary>
    private static byte[]? LeerLogo(string? logo, string? raiz)
    {
        if (string.IsNullOrWhiteSpace(logo)) return null;
        try
        {
            var ruta = Path.IsPathRooted(logo.Trim()) || string.IsNullOrWhiteSpace(raiz) ? logo.Trim() : Path.Combine(raiz.Trim(), logo.Trim());
            return File.Exists(ruta) ? File.ReadAllBytes(ruta) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
