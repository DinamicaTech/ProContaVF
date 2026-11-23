using System.Security.Cryptography.X509Certificates;
using ProConta.Datos.Persistencia;

namespace ProConta.Inicio.Bloques;

/// <summary>Color de la etiqueta del certificado en la cabecera de Inicio (contabilidad/modelos-tributarios/presentacion/aviso-certificado).</summary>
public enum SemaforoCertificado { Verde, Amarillo, Rojo, Gris }

/// <summary>
/// Lo que se sabe del certificado de la empresa (VeriFactuConfiguracion: el que se elige en Parámetros › «Certificado de la empresa»):
/// si hay uno elegido, si está instalado en este equipo y, si lo está, su nombre (CN), NIF y caducidad.
/// </summary>
public sealed record DatosCertificado(bool Elegido, bool Instalado, string? Nombre = null, string? Nif = null, DateTime? Caduca = null)
{
    public static DatosCertificado SinElegir { get; } = new(false, false);
    public static DatosCertificado NoInstalado { get; } = new(true, false);
}

/// <summary>La etiqueta fija del certificado: color, texto corto y explicación para el tooltip.</summary>
public sealed record EstadoCertificado(SemaforoCertificado Semaforo, string Texto, string Ayuda, int? Dias)
{
    /// <summary>Por encima de estos días, verde.</summary>
    public const int DiasVerde = 60;
    /// <summary>Por debajo de estos días, rojo; entre este valor y <see cref="DiasVerde"/> (los dos incluidos), amarillo.</summary>
    public const int DiasRojo = 30;

    private const string ComoRenovar =
        "Para renovarlo, pide el nuevo a tu proveedor (FNMT, Camerfirma…) antes de que caduque, instálalo en Windows (doble clic en el .pfx) " +
        "y elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa». Clic: abre esa pantalla.";

    /// <summary>Verde si caduca en más de 60 días, amarillo entre 30 y 60, rojo con menos de 30 o ya caducado (propietario 2026-11-23 13:59).</summary>
    public static SemaforoCertificado Color(int dias) => dias > DiasVerde ? SemaforoCertificado.Verde : dias >= DiasRojo ? SemaforoCertificado.Amarillo : SemaforoCertificado.Rojo;

    public static EstadoCertificado Calcular(DatosCertificado d, DateTime hoy)
    {
        if (!d.Elegido)
            return new(SemaforoCertificado.Gris, "Sin certificado de la empresa",
                "La sociedad no tiene certificado elegido: sin él no se puede presentar a la AEAT, consultar NIF ni enviar a VeriFactu. " +
                "Instálalo en Windows (doble clic en el .pfx) y elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa». Clic: abre esa pantalla.", null);
        if (!d.Instalado || d.Caduca is not DateTime caduca)
            return new(SemaforoCertificado.Rojo, "Certificado no instalado en este equipo",
                "El certificado elegido para la sociedad no está en el almacén de Windows de este equipo (ni del equipo ni de tu usuario), así que no se puede usar para presentar a la AEAT. " +
                "Instálalo aquí (doble clic en el .pfx) o elige otro en Configuración › Parámetros y tablas › «Certificado de la empresa». Clic: abre esa pantalla.", null);
        var dias = (caduca.Date - hoy.Date).Days;
        var nombre = d.Nombre is { Length: > 0 } n ? n : "Certificado de la empresa";
        var cuando = dias switch
        {
            < 0 => $"caducó el {caduca:dd/MM/yyyy}",
            0 => $"caduca hoy, {caduca:dd/MM/yyyy}",
            1 => $"caduca el {caduca:dd/MM/yyyy} (mañana)",
            _ => $"caduca el {caduca:dd/MM/yyyy} ({dias} días)",
        };
        var semaforo = Color(dias);
        var regla = semaforo switch
        {
            SemaforoCertificado.Verde => $"Faltan más de {DiasVerde} días: no hay que hacer nada.",
            SemaforoCertificado.Amarillo => $"Faltan entre {DiasRojo} y {DiasVerde} días: conviene pedir ya la renovación.",
            _ when dias < 0 => "Ya ha caducado: no se puede presentar a la AEAT ni enviar a VeriFactu con él.",
            _ => $"Faltan menos de {DiasRojo} días: renuévalo cuanto antes.",
        };
        var ayuda = $"Certificado de la empresa: {nombre}{(d.Nif is { Length: > 0 } nif ? $" (NIF {nif})" : "")}, {cuando}. {regla}" +
                    (semaforo == SemaforoCertificado.Verde ? " Clic: abre Configuración › Parámetros y tablas › «Certificado de la empresa»." : " " + ComoRenovar);
        return new(semaforo, $"{nombre} · {cuando}", ayuda, dias);
    }

    /// <summary>Lee el certificado de la sociedad: el guardado en VeriFactuConfiguracion, buscado en el almacén del equipo y del usuario.</summary>
    public static async Task<DatosCertificado> LeerAsync(IBaseDatos bd, string idSociedad, CancellationToken c = default)
    {
        var guardado = await bd.EjecutarAsync(async s =>
        {
            if (!s.LeerEsquema().ExisteTabla("VeriFactuConfiguracion")) return (Huella: (string?)null, Serie: (string?)null);
            return await s.ConsultarPrimeroAsync("SELECT CertificateThumbprint, CertificateSerial FROM VeriFactuConfiguracion WHERE IdSociedad = ?",
                f => (Huella: f.Texto("CertificateThumbprint")?.Trim(), Serie: f.Texto("CertificateSerial")?.Trim()), idSociedad);
        }, c);
        if (string.IsNullOrEmpty(guardado.Huella) && string.IsNullOrEmpty(guardado.Serie)) return DatosCertificado.SinElegir;
        using var cert = ProConta.Contabilidad.Presentacion.CertificadosAeat.Buscar(guardado.Huella, guardado.Serie);
        if (cert is null) return DatosCertificado.NoInstalado;
        var titular = ProConta.Contabilidad.Presentacion.TitularCertificado.De(cert);
        return new DatosCertificado(true, true, cert.GetNameInfo(X509NameType.SimpleName, false), titular.NifRepresentada ?? titular.Nif, cert.NotAfter);
    }
}
