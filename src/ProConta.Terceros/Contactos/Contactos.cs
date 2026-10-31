using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Contactos;

public enum TipoDatoContacto
{
    Email,
    Telefono
}

/// <summary>Finalidad de una comunicación automática (RF-TER-07/08).</summary>
public enum FinalidadEnvio
{
    Facturas,
    Administrativos
}

/// <summary>
/// Persona de contacto sin crear un tercero (tabla ContactosLibresTercero, RF-TER-10). Agrupa datos de
/// contacto dentro de su titular.
/// </summary>
public sealed class ContactoLibre
{
    public int IdContactoLibre { get; internal set; }
    public string Nombre { get; set; } = "";
    public string? Departamento { get; set; }
}

/// <summary>Email o teléfono de un tercero (tabla DatosContacto), opcionalmente de un contacto libre suyo.</summary>
public sealed class DatoContacto
{
    public int IdDatoContacto { get; internal set; }
    public TipoDatoContacto Tipo { get; set; }
    public string? Etiqueta { get; set; }
    public string Valor { get; set; } = "";
    public bool Activo { get; set; } = true;
    public bool EnvioFacturas { get; set; }
    public bool EnviosAdministrativos { get; set; }

    /// <summary>Contacto libre al que pertenece; null si es un dato general del tercero.</summary>
    public ContactoLibre? ContactoLibre { get; set; }

    internal Error? Validar()
    {
        var valor = Valor.Trim();
        if (valor.Length == 0)
            return new Error("Contactos.Vacio", "Hay un dato de contacto vacío.", null, "El valor es obligatorio.", "Escríbelo o quita la fila.");
        if (valor.Length > 254)
            return new Error("Contactos.Largo", $"El dato de contacto «{valor[..20]}…» tiene más de 254 caracteres.", null, "No cabe.", "Revísalo.");
        if (Tipo == TipoDatoContacto.Email && (!valor.Contains('@') || valor.StartsWith('@') || valor.EndsWith('@') || valor.Contains(' ')))
            return new Error("Contactos.EmailNoValido", $"«{valor}» no parece una dirección de email.", null,
                "Un email tiene la forma nombre@dominio.", "Corrige la dirección.");
        return null;
    }
}

/// <summary>Destinatarios efectivos para una finalidad, o la incidencia que impide elegirlos.</summary>
public sealed record Destinatarios(IReadOnlyList<string> Emails, Error? Incidencia)
{
    public bool HayDestinatarios => Emails.Count > 0;
}

/// <summary>
/// Resolución de destinatarios (RF-TER-08): todos los emails activos marcados para la finalidad; si no
/// hay ninguno marcado y solo hay una dirección activa distinta, esa (sin cambiar sus marcas); si hay
/// varias sin marcar, incidencia y ninguno, sin elegir uno al azar.
/// </summary>
public static class ResolutorDestinatarios
{
    public static Destinatarios Resolver(IEnumerable<DatoContacto> datos, FinalidadEnvio finalidad, string? registro = null)
    {
        var activos = datos.Where(d => d.Tipo == TipoDatoContacto.Email && d.Activo && !string.IsNullOrWhiteSpace(d.Valor)).ToList();
        var marcados = activos.Where(d => finalidad == FinalidadEnvio.Facturas ? d.EnvioFacturas : d.EnviosAdministrativos).ToList();
        if (marcados.Count > 0) return new Destinatarios(Distintos(marcados), null);

        var distintos = Distintos(activos);
        if (distintos.Count == 1) return new Destinatarios(distintos, null);
        if (distintos.Count == 0)
            return new Destinatarios([], new Error("Contactos.SinEmail", "El tercero no tiene ningún email activo.", registro,
                "No hay a quién enviar.", "Añade un email en la ficha del tercero."));
        return new Destinatarios([], new Error("Contactos.EmailAmbiguo",
            $"El tercero tiene {distintos.Count} emails activos y ninguno marcado para {(finalidad == FinalidadEnvio.Facturas ? "envío de facturas" : "envíos administrativos")}.",
            registro, "No elijo uno al azar.", "Marca en la ficha del tercero el email o los emails que deben recibir estos envíos."));
    }

    private static List<string> Distintos(IEnumerable<DatoContacto> datos) =>
        datos.Select(d => d.Valor.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
