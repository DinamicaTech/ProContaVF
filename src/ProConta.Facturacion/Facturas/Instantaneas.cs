using ProConta.Datos.Persistencia;
using ProConta.Terceros;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Instantánea del destinatario al validar (RF-FAC-05): se toma del tercero del cliente (su domicilio fiscal, o el
/// principal si no tiene uno fiscal) y queda congelada en la factura.
/// </summary>
public static class Instantaneas
{
    /// <summary>IdTercero del cliente (Clientes.IdCliente), o null si no existe.</summary>
    public static Task<int?> TerceroDelClienteAsync(ISesionDatos s, int idCliente) =>
        s.ConsultarPrimeroAsync("SELECT IdTercero FROM Clientes WHERE IdCliente = ?", f => (int?)f.Entero("IdTercero"), idCliente);

    public static InstantaneaCliente DeTercero(Tercero t)
    {
        var fiscal = t.Domicilios.FirstOrDefault(d => d.Activo && d.TipoDomicilio.Contains("FISCAL", StringComparison.OrdinalIgnoreCase));
        var (direccion, cp, poblacion, provincia, pais) = fiscal is not null
            ? (fiscal.Direccion, fiscal.CodigoPostal, fiscal.Poblacion, fiscal.Provincia, fiscal.Pais ?? t.Pais)
            : (t.Direccion, t.CodigoPostal, t.Poblacion, t.Provincia, t.Pais);
        return new InstantaneaCliente(
            Recortar(t.Nif?.Trim().ToUpperInvariant(), 25),
            Recortar(t.Nombre, 150),
            Recortar(string.IsNullOrWhiteSpace(t.RazonSocial) ? t.Nombre : t.RazonSocial, 200),
            PaisIso(pais),
            TipoIdentificacionAeat(t.TipoIdentificacion),
            Recortar(direccion, 255), Recortar(cp, 20), Recortar(poblacion, 100), Recortar(provincia, 100), Recortar(t.Email, 254));
    }

    /// <summary>
    /// País en ISO 3166-1 alfa-2 (Facturas.PaisCliente). Terceros guarda hasta 3 caracteres: ES, ESP o el código
    /// numérico 724 del ERP. Lo que no se reconoce queda vacío (se trata como España).
    /// </summary>
    public static string? PaisIso(string? pais)
    {
        var p = pais?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(p)) return null;
        if (p.Length == 2 && p.All(char.IsAsciiLetterUpper)) return p;
        return p switch
        {
            "ESP" or "724" => "ES",
            "FRA" or "250" => "FR",
            "PRT" or "620" => "PT",
            "DEU" or "276" => "DE",
            "ITA" or "380" => "IT",
            "GBR" or "826" => "GB",
            "AND" or "020" or "20" => "AD",
            "BEL" or "056" or "56" => "BE",
            "NLD" or "528" => "NL",
            "CHE" or "756" => "CH",
            "USA" or "840" => "US",
            _ => null
        };
    }

    /// <summary>
    /// Tipo de identificación AEAT para identificadores extranjeros (Facturas.TipoIdentificacionCliente); null para el
    /// NIF español (vacío, NIF, CIF o NIE).
    /// </summary>
    public static byte? TipoIdentificacionAeat(string? tipo) => tipo?.Trim().ToUpperInvariant() switch
    {
        null or "" or "NIF" or "CIF" or "NIE" => null,
        "VAT" or "NIF-IVA" or "NIFIVA" or "IVA" => 2,
        "PASAPORTE" => 3,
        "DOCUMENTO OFICIAL" or "DNI EXTRANJERO" => 4,
        "CERTIFICADO DE RESIDENCIA" or "RESIDENCIA" => 5,
        "NO CENSADO" => 7,
        _ => 6
    };

    private static string? Recortar(string? texto, int maximo)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length <= maximo ? t : t[..maximo];
    }
}
