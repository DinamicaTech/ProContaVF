using System.Globalization;
using ProConta.Correo;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Terceros.Contactos;

namespace ProConta.Facturacion.Documentos;

/// <summary>Mensaje preparado para una factura y, si no se han podido elegir destinatarios, por qué (el compositor lo muestra).</summary>
public sealed record CorreoFacturaPreparado(MensajeNuevo Mensaje, Error? AvisoDestinatarios);

/// <summary>
/// Prepara el correo de una factura validada (facturacion/pantallas 2026-11-10 19:40): su PDF archivado como adjunto, los
/// destinatarios del cliente para el envío de facturas (resolución de terceros/contactos) y el asunto y texto de la
/// plantilla «Factura» de la empresa. El transporte, la cola y el historial son de correo/envio: aquí solo se prepara.
/// </summary>
public sealed class PreparadorCorreoFacturas(ServicioFacturas facturas, ServicioPdfFacturas pdf, ServicioPlantillasCorreo plantillas, IServicioSociedades sociedades)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public async Task<Resultado<CorreoFacturaPreparado>> PrepararAsync(int idFactura, Actor actor)
    {
        var f = await facturas.ObtenerAsync(idFactura);
        if (f is null) return Resultado<CorreoFacturaPreparado>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura), null, "Actualiza el listado."));
        if (f.NumeroFactura is null)
            return Resultado<CorreoFacturaPreparado>.Fallo(new Error("Correo.FacturaBorrador", $"{f.Rotulo} es un borrador: solo se envían facturas validadas.", f.Rotulo, null,
                "Valídala con «Validar…» y vuelve a enviarla."));
        var ejercicio = await sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null) return Resultado<CorreoFacturaPreparado>.Fallo(new Error("Facturas.SinEjercicio", "El ejercicio de la factura no existe.", f.Rotulo));

        var documento = await pdf.ObtenerAsync(idFactura, actor);
        if (!documento.EsCorrecto) return Resultado<CorreoFacturaPreparado>.Fallo(documento.Errores);
        AdjuntoNuevo adjunto;
        try
        {
            adjunto = AdjuntoNuevo.DesdeFichero(documento.Valor.Ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado<CorreoFacturaPreparado>.Fallo(new Error("Correo.PdfNoLeido", $"No he podido leer el PDF de {f.Rotulo}: {ex.Message}", f.Rotulo,
                "Está abierto en otro programa que lo bloquea o se ha movido.", "Ciérralo y vuelve a enviar; si se ha movido, usa «Regenerar PDF»."));
        }

        var destinatarios = new List<Destinatario>();
        Error? aviso = null;
        var tercero = f.IdCliente is int idCliente ? await facturas.TerceroDeClienteAsync(idCliente) : null;
        if (tercero is not null)
        {
            var r = ResolutorDestinatarios.Resolver(tercero.DatosContacto, FinalidadEnvio.Facturas, f.Rotulo);
            destinatarios.AddRange(r.Emails.Select(e => new Destinatario(TipoDestinatario.Para, e, f.Cliente.NombreVisible)));
            aviso = r.Incidencia;
        }
        // Sin tercero o sin email elegible, el email congelado en la factura al validarla.
        if (destinatarios.Count == 0 && !string.IsNullOrWhiteSpace(f.Cliente.Email) && aviso?.Codigo != "Contactos.EmailAmbiguo")
        {
            destinatarios.Add(new Destinatario(TipoDestinatario.Para, f.Cliente.Email.Trim(), f.Cliente.NombreVisible));
            aviso = null;
        }
        if (destinatarios.Count == 0 && aviso is null)
            aviso = new Error("Contactos.SinEmail", $"El cliente de {f.Rotulo} no tiene email.", f.Rotulo, "No hay a quién enviarla.", "Añade un email en la ficha del cliente y márcalo para el envío de facturas.");

        var plantilla = await plantillas.ObtenerAsync(ejercicio.IdSociedad, TiposCorreo.Factura);
        var empresa = string.IsNullOrWhiteSpace(f.NombreEmisor) ? ejercicio.RazonSocial : f.NombreEmisor!;
        var valores = new Dictionary<string, string?>
        {
            ["NumeroFactura"] = f.NumeroFactura,
            ["FechaFactura"] = f.FechaFactura?.ToString("dd/MM/yyyy", Es),
            ["Importe"] = f.Totales.ImporteTotal.Valor.ToString("#,##0.00", Es) + " €",
            ["Cliente"] = f.Cliente.NombreVisible,
            ["Empresa"] = empresa,
            ["Ejercicio"] = ejercicio.Ejercicio.ToString(CultureInfo.InvariantCulture),
        };
        var cuerpo = ServicioPlantillasCorreo.Rellenar(plantilla.Cuerpo, valores, plantilla.EsHtml);
        var mensaje = new MensajeNuevo
        {
            IdSociedadEjercicio = f.IdSociedadEjercicio,
            TipoCorreo = TiposCorreo.Factura,
            TipoOrigen = "Factura",
            IdOrigen = idFactura,
            ReferenciaOrigen = $"{f.NumeroFactura} · {f.Cliente.NombreVisible}",
            Destinatarios = destinatarios,
            Asunto = ServicioPlantillasCorreo.Rellenar(plantilla.Asunto, valores, false),
            CuerpoHtml = plantilla.EsHtml ? cuerpo : ServicioPlantillasCorreo.HtmlDeTexto(cuerpo),
            CuerpoTexto = plantilla.EsHtml ? null : cuerpo,
            Adjuntos = [adjunto],
        };
        return Resultado<CorreoFacturaPreparado>.Correcto(new CorreoFacturaPreparado(mensaje, aviso));
    }
}
