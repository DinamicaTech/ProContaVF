using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using ProConta.Compras.Proveedores;
using ProConta.Datos.Persistencia;

namespace ProConta.Compras.Integraciones;

/// <summary>
/// Servicio VNifV2 de la AEAT (aprobado en compras/reconocimiento 2026-11-05 11:24): comprueba un NIF y devuelve el nombre
/// o la razón social censal. Usa el certificado de la empresa de VeriFactuConfiguracion (huella o número de serie,
/// buscado en el almacén del equipo y después en el del usuario), aunque VeriFactu esté desactivado; sin certificado no
/// se consulta. Nunca guarda ni registra datos del certificado.
/// </summary>
public sealed class ConsultaNifAeatVNif(IBaseDatos bd, Func<X509Certificate2, HttpMessageHandler>? manejador = null) : IConsultaNifAeat
{
    public const string Endpoint = "https://www1.agenciatributaria.gob.es/wlpl/BURT-JDIT/ws/VNifV2SOAP";
    private const string EspacioEntrada = "http://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/ws/VNifV2Ent.xsd";

    public async Task<ResultadoNifAeat> ConsultarAsync(string idSociedad, string nif, string? nombre, CancellationToken cancelacion = default)
    {
        var datos = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT CertificateThumbprint, CertificateSerial FROM VeriFactuConfiguracion WHERE IdSociedad = ?",
            f => (Huella: f.Texto("CertificateThumbprint")?.Trim(), Serie: f.Texto("CertificateSerial")?.Trim()), idSociedad), cancelacion);
        using var certificado = BuscarCertificado(datos.Huella, datos.Serie);
        if (certificado is null)
            return ResultadoNifAeat.SinConsultar("La empresa no tiene certificado configurado en este equipo, así que no consulto la AEAT.");
        using var handler = manejador?.Invoke(certificado) ?? CrearManejador(certificado);
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };
        using var contenido = new StringContent(Peticion(nif, nombre), Encoding.UTF8, "text/xml");
        using var respuesta = await http.PostAsync(Endpoint, contenido, cancelacion);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(cancelacion);
        return Interpretar(cuerpo, (int)respuesta.StatusCode);
    }

    private static HttpMessageHandler CrearManejador(X509Certificate2 certificado)
    {
        var h = new HttpClientHandler { ClientCertificateOptions = ClientCertificateOption.Manual };
        h.ClientCertificates.Add(certificado);
        return h;
    }

    /// <summary>Sobre SOAP de VNifV2Ent con un contribuyente. El nombre es obligatorio en el servicio: sin él, se manda el NIF.</summary>
    internal static string Peticion(string nif, string? nombre)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace v = EspacioEntrada;
        var texto = string.IsNullOrWhiteSpace(nombre) ? nif : nombre.Trim();
        if (texto.Length > 80) texto = texto[..80];
        return new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soapenv", soap), new XAttribute(XNamespace.Xmlns + "vnif", v),
            new XElement(soap + "Header"),
            new XElement(soap + "Body", new XElement(v + "VNifV2Ent", new XElement(v + "Contribuyente", new XElement(v + "Nif", nif), new XElement(v + "Nombre", texto)))))).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Lee VNifV2Sal (o el fallo SOAP) por nombre local, sin depender de los prefijos.</summary>
    internal static ResultadoNifAeat Interpretar(string cuerpo, int estado)
    {
        XDocument doc;
        try { doc = XDocument.Parse(cuerpo); }
        catch (System.Xml.XmlException) { return ResultadoNifAeat.SinConsultar($"La AEAT ha respondido algo que no entiendo (HTTP {estado})."); }
        string? Valor(string local) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == local)?.Value.Trim();
        if (Valor("faultstring") is { } fallo) return ResultadoNifAeat.SinConsultar($"La AEAT ha rechazado la consulta: {fallo}");
        var resultado = Valor("Resultado");
        if (resultado is null) return ResultadoNifAeat.SinConsultar($"La respuesta de la AEAT no trae resultado (HTTP {estado}).");
        var identificado = string.Equals(resultado, "IDENTIFICADO", StringComparison.OrdinalIgnoreCase);
        var nombre = Valor("Nombre");
        return new ResultadoNifAeat(true, identificado && !string.IsNullOrWhiteSpace(nombre), identificado ? nombre : null,
            identificado ? null : $"La AEAT dice: {resultado}.");
    }

    private static X509Certificate2? BuscarCertificado(string? huella, string? serie)
    {
        if (string.IsNullOrEmpty(huella) && string.IsNullOrEmpty(serie)) return null;
        foreach (var ubicacion in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var almacen = new X509Store(StoreName.My, ubicacion);
            try { almacen.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly); }
            catch (System.Security.Cryptography.CryptographicException) { continue; }
            var encontrados = !string.IsNullOrEmpty(huella)
                ? almacen.Certificates.Find(X509FindType.FindByThumbprint, huella.Replace(" ", ""), validOnly: false)
                : almacen.Certificates.Find(X509FindType.FindBySerialNumber, serie!.Replace(" ", ""), validOnly: false);
            var c = encontrados.OfType<X509Certificate2>().FirstOrDefault(x => x.HasPrivateKey);
            if (c is not null) return c;
        }
        return null;
    }
}
