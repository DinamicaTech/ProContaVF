using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>
/// Titular del certificado con el que se presenta (el «presentador» para la AEAT), leído del propio certificado: NIF y nombre de la
/// persona (SERIALNUMBER «IDCES-…», nombre y apellidos) y, en un certificado de representante, NIF y razón social de la entidad
/// representada (organizationIdentifier «VATES-…» y O).
/// </summary>
public sealed record TitularCertificado(string? Nif, string? Nombre, string? NifRepresentada, string? Representada, DateTime Caduca)
{
    /// <summary>
    /// Los pares NIF/nombre que se prueban como FIRNIF/FIRNOMBRE, por orden: el de la entidad representada (la sociedad) y, si la AEAT
    /// no lo acepta, el de la persona. Una presentación rechazada no graba nada, así que probar el segundo no tiene riesgo.
    /// </summary>
    public IReadOnlyList<(string Nif, string Nombre)> Firmantes
    {
        get
        {
            var r = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(NifRepresentada)) r.Add((NifRepresentada!, Representada ?? NifRepresentada!));
            if (!string.IsNullOrWhiteSpace(Nif)) r.Add((Nif!, Nombre ?? Nif!));
            return r;
        }
    }

    /// <summary>Texto para la conformidad: «Nombre (NIF) en representación de Sociedad (NIF)».</summary>
    public string Descripcion => (Nombre, Nif, Representada, NifRepresentada) switch
    {
        ({ } n, { } f, { } r, { } fr) => $"{n} ({f}) en representación de {r} ({fr})",
        ({ } n, { } f, _, _) => $"{n} ({f})",
        (_, _, { } r, { } fr) => $"{r} ({fr})",
        _ => "titular sin identificar",
    };

    public static TitularCertificado De(X509Certificate2 c)
    {
        string? Oid(string oid)
        {
            foreach (var rdn in c.SubjectName.EnumerateRelativeDistinguishedNames())
                if (rdn.GetSingleElementType().Value == oid) return rdn.GetSingleElementValue()?.Trim();
            return null;
        }
        static string? SinPrefijo(string? v, string prefijo) => v is null ? null : v.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? v[prefijo.Length..] : v;
        var serie = SinPrefijo(SinPrefijo(Oid("2.5.4.5"), "IDCES-"), "IDESP-");
        var org = SinPrefijo(Oid("2.5.4.97"), "VATES-");
        var nombre = (Oid("2.5.4.42"), Oid("2.5.4.4")) switch
        {
            ({ } g, { } sn) => $"{sn} {g}",
            _ => Oid("2.5.4.3"),
        };
        return new TitularCertificado(serie, nombre, org, Oid("2.5.4.10"), c.NotAfter);
    }
}

/// <summary>
/// El certificado de la sociedad para la AEAT: el mismo que ya usan VeriFactu y la consulta de NIF (VeriFactuConfiguracion: huella o
/// número de serie), buscado en el almacén del equipo y después en el del usuario, con clave privada, aunque VeriFactu esté desactivado.
/// </summary>
public static class CertificadosAeat
{
    public static async Task<X509Certificate2?> DeSociedadAsync(IBaseDatos bd, string idSociedad, CancellationToken cancelacion = default)
    {
        var datos = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT CertificateThumbprint, CertificateSerial FROM VeriFactuConfiguracion WHERE IdSociedad = ?",
            f => (Huella: f.Texto("CertificateThumbprint")?.Trim(), Serie: f.Texto("CertificateSerial")?.Trim()), idSociedad), cancelacion);
        return Buscar(datos.Huella, datos.Serie);
    }

    public static X509Certificate2? Buscar(string? huella, string? serie)
    {
        if (string.IsNullOrEmpty(huella) && string.IsNullOrEmpty(serie)) return null;
        foreach (var ubicacion in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var almacen = new X509Store(StoreName.My, ubicacion);
            try { almacen.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly); }
            catch (CryptographicException) { continue; }
            var encontrados = !string.IsNullOrEmpty(huella)
                ? almacen.Certificates.Find(X509FindType.FindByThumbprint, huella.Replace(" ", ""), validOnly: false)
                : almacen.Certificates.Find(X509FindType.FindBySerialNumber, serie!.Replace(" ", ""), validOnly: false);
            var c = encontrados.OfType<X509Certificate2>().FirstOrDefault(x => x.HasPrivateKey);
            if (c is not null) return c;
        }
        return null;
    }
}
