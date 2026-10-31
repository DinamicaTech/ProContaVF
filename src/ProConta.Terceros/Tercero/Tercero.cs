using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Contactos;

namespace ProConta.Terceros;

/// <summary>Tipo de persona (RF-TER-02). Particular es persona física y Asociación, entidad sin personalidad.</summary>
public enum TipoPersona
{
    Fisica,
    Juridica,
    EntidadSinPersonalidad
}

/// <summary>Domicilio adicional con finalidad (tabla DomiciliosTercero).</summary>
public sealed record Domicilio(
    int IdDomicilio,
    string TipoDomicilio,
    string Direccion,
    string? CodigoPostal,
    string? Poblacion,
    string? Provincia,
    string? Pais,
    DateTime? FechaInicio = null,
    DateTime? FechaFin = null,
    bool EsPrincipal = false,
    bool Activo = true);

/// <summary>
/// Ente Tercero: identidad estable de una persona u organización (RF-TER-01). Agrupa su domicilio
/// principal, sus domicilios adicionales, sus datos de contacto y sus contactos libres, que se guardan
/// juntos en una transacción. El NIF es revisable y no único: nunca se fusionan terceros por NIF o nombre.
/// </summary>
public sealed class Tercero
{
    public int IdTercero { get; internal set; }

    /// <summary>Null solo en registros antiguos pendientes de clasificar; un tercero nuevo debe tenerlo.</summary>
    public TipoPersona? TipoPersona { get; set; }

    /// <summary>Nombre visible (columna Nombre). Se calcula del nombre y apellidos o de la razón social.</summary>
    public string Nombre { get; set; } = "";
    public string? NombrePersona { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string? RazonSocial { get; set; }
    public string? NombreComercial { get; set; }
    public string? Nif { get; set; }

    /// <summary>Clase de identificación (por ejemplo NIF, CIF, PASAPORTE, VAT); vacía o NIF/CIF/NIE se valida como NIF español.</summary>
    public string? TipoIdentificacion { get; set; }

    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Pais { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Provincia { get; set; }
    public string? Poblacion { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
    public string? NotasInternas { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime? FechaAlta { get; internal set; }

    /// <summary>Versión leída, para la concurrencia optimista.</summary>
    public int Version { get; internal set; }

    public List<Domicilio> Domicilios { get; } = [];
    public List<DatoContacto> DatosContacto { get; } = [];
    public List<ContactoLibre> ContactosLibres { get; } = [];

    public bool EsNuevo => IdTercero == 0;

    /// <summary>Nombre visible que corresponde a los datos actuales.</summary>
    public string NombreCalculado() => TipoPersona switch
    {
        Terceros.TipoPersona.Fisica => string.Join(' ', new[] { NombrePersona, PrimerApellido, SegundoApellido }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())),
        Terceros.TipoPersona.Juridica or Terceros.TipoPersona.EntidadSinPersonalidad => RazonSocial?.Trim() ?? "",
        _ => Nombre.Trim()
    };

    private static readonly string[] _identificacionesNif = ["", "NIF", "CIF", "NIE", "DNI"];

    /// <summary>
    /// Reglas de la ficha. El NIF español se valida si es nuevo o ha cambiado (<paramref name="nifAnterior"/>):
    /// un NIF antiguo mal escrito no impide guardar otros cambios.
    /// </summary>
    public Resultado Validar(string? nifAnterior)
    {
        var errores = new List<Error>();
        var registro = EsNuevo ? "Tercero nuevo" : $"Tercero {IdTercero}";
        if (EsNuevo && TipoPersona is null)
            errores.Add(new Error("Tercero.SinTipo", "Indica si el tercero es una persona física, una persona jurídica o una entidad sin personalidad.",
                registro, "Un tercero nuevo debe tener tipo de persona.", "Elige el tipo de persona."));
        if (TipoPersona == Terceros.TipoPersona.Fisica && string.IsNullOrWhiteSpace(NombrePersona))
            errores.Add(new Error("Tercero.SinNombre", "Falta el nombre de la persona.", registro,
                "Una persona física necesita nombre; los apellidos son opcionales.", "Escribe el nombre."));
        if (TipoPersona is Terceros.TipoPersona.Juridica or Terceros.TipoPersona.EntidadSinPersonalidad && string.IsNullOrWhiteSpace(RazonSocial))
            errores.Add(new Error("Tercero.SinRazonSocial", "Falta la razón social.", registro,
                "Una persona jurídica o entidad necesita razón social; el nombre comercial es opcional.", "Escribe la razón social."));
        if (TipoPersona is null && string.IsNullOrWhiteSpace(Nombre))
            errores.Add(new Error("Tercero.SinNombre", "Falta el nombre del tercero.", registro, "El nombre es obligatorio.", "Escribe el nombre."));

        var nif = Nif?.Trim();
        var cambiado = !string.Equals(Nucleo.Identificacion.Nif.Normalizar(nif), Nucleo.Identificacion.Nif.Normalizar(nifAnterior), StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(nif) && (EsNuevo || cambiado) && _identificacionesNif.Contains((TipoIdentificacion ?? "").Trim().ToUpperInvariant()))
        {
            var validado = Nucleo.Identificacion.Nif.Validar(nif);
            if (!validado.EsCorrecto) errores.Add(validado.Error! with { RegistroAfectado = registro });
        }
        if (Email is { Length: > 254 })
            errores.Add(new Error("Tercero.EmailLargo", "El email tiene más de 254 caracteres.", registro, "No cabe en la ficha.", "Revisa la dirección."));

        foreach (var dato in DatosContacto)
        {
            var e = dato.Validar();
            if (e is not null) errores.Add(e with { RegistroAfectado = registro });
        }
        foreach (var dato in DatosContacto.Where(d => d.ContactoLibre is { } grupo && !ContactosLibres.Contains(grupo)))
            errores.Add(new Error("Contactos.GrupoAjeno", "Un dato de contacto apunta a un contacto libre de otro tercero.", registro,
                "Los contactos libres pertenecen a su titular.", "Asigna el dato a un contacto libre de esta ficha o déjalo como dato general."));
        foreach (var grupo in ContactosLibres.Where(g => string.IsNullOrWhiteSpace(g.Nombre)))
            errores.Add(new Error("Contactos.SinNombre", "Un contacto libre no tiene nombre.", registro,
                "El nombre de la persona de contacto es obligatorio.", "Escribe su nombre o quita el contacto."));
        return Resultado.Combinar(errores.Select(Resultado.Fallo).ToArray());
    }
}
