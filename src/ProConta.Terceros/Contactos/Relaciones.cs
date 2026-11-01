using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Contactos;

/// <summary>
/// Relación persona-organización con vigencia (tabla RelacionesContactos, RF-TER-11). La persona participa con su
/// rol Contacto, que se le da al guardar si no lo tiene. Un IdTercero 0 en un lado significa «este tercero» cuando
/// la ficha aún no se ha guardado. Una relación guardada no se borra: se cierra su vigencia con la fecha de fin.
/// </summary>
public sealed class RelacionContacto
{
    public const int LongitudMaximaTipo = 40;

    /// <summary>Tipos propuestos en la ficha; se admite cualquier otro texto.</summary>
    public static readonly IReadOnlyList<string> TiposHabituales =
        ["Persona de contacto", "Administrador", "Representante", "Empleado", "Socio", "Asesor"];

    public int IdRelacionContacto { get; internal set; }
    public int IdTerceroPersona { get; set; }
    public string NombrePersona { get; set; } = "";
    public int IdTerceroOrganizacion { get; set; }
    public string NombreOrganizacion { get; set; } = "";
    public string TipoRelacion { get; set; } = TiposHabituales[0];
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string? Observaciones { get; set; }

    public bool EsNueva => IdRelacionContacto == 0;

    /// <summary>Vigente si no tiene fecha de fin o aún no ha llegado.</summary>
    public bool EsVigente(DateTime hoy) => FechaFin is null || FechaFin.Value.Date >= hoy.Date;

    internal RelacionContacto Clonar() => (RelacionContacto)MemberwiseClone();

    internal Error? Validar()
    {
        var tipo = TipoRelacion?.Trim() ?? "";
        var quien = $"{NombrePersona} con {NombreOrganizacion}";
        if (IdTerceroPersona == IdTerceroOrganizacion)
            return new Error("Relaciones.ConsigoMismo", "Un tercero no puede relacionarse consigo mismo.", null,
                "La relación une una persona con una organización distinta.", "Elige otra persona u organización o quita la relación.");
        if (tipo.Length == 0)
            return new Error("Relaciones.SinTipo", $"Falta el tipo de la relación de {quien}.", null, "El tipo es obligatorio.", "Elige o escribe el tipo, por ejemplo «Persona de contacto».");
        if (tipo.Length > LongitudMaximaTipo)
            return new Error("Relaciones.TipoLargo", $"El tipo de la relación de {quien} tiene más de {LongitudMaximaTipo} caracteres.", null, "No cabe.", "Acórtalo.");
        if (FechaInicio is { } i && FechaFin is { } f && f.Date < i.Date)
            return new Error("Relaciones.FechasInvertidas", $"La relación de {quien} termina antes de empezar.", null,
                "La fecha de fin es anterior a la de inicio.", "Corrige una de las dos fechas.");
        return null;
    }
}
