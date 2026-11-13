using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;

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

    /// <summary>Roles Cliente, Proveedor y Contacto, con su estado y sus datos propios; se guardan con la ficha.</summary>
    public RolesFicha Roles { get; } = new();

    /// <summary>Asignaciones de perfiles (activas y retiradas); se guardan con la ficha (RF-TER-12/13).</summary>
    public List<AsignacionPerfil> Perfiles { get; } = [];

    /// <summary>Relaciones persona-organización en las que participa el tercero, como persona o como organización (RF-TER-11).</summary>
    public List<RelacionContacto> Relaciones { get; } = [];

    /// <summary>
    /// Cuentas de los roles en una sociedad (identidades contables). Null si la ficha no las ha cargado: entonces
    /// guardar no las toca. Las carga y las resuelve en el ejercicio <see cref="ServicioRoles"/>.
    /// </summary>
    public CuentasRolesFicha? CuentasRoles { get; set; }

    public bool EsNuevo => IdTercero == 0;

    /// <summary>Asigna un perfil (o reactiva su asignación retirada). Repetir el estado actual no cambia nada.</summary>
    public void AsignarPerfil(Perfil perfil)
    {
        var a = Perfiles.FirstOrDefault(x => x.IdPerfil == perfil.IdPerfil);
        if (a is null) Perfiles.Add(new AsignacionPerfil(perfil.IdPerfil, perfil.Nombre, perfil.Descripcion, perfil.Activo) { Activo = true });
        else a.Activo = true;
    }

    /// <summary>Retira un perfil asignado (la fila se conserva con su fecha de retirada al guardar).</summary>
    public void RetirarPerfil(int idPerfil)
    {
        var a = Perfiles.FirstOrDefault(x => x.IdPerfil == idPerfil);
        if (a is null) return;
        if (a.EsNueva) Perfiles.Remove(a);
        else a.Activo = false;
    }

    /// <summary>Copia profunda de la ficha (para comparar el estado original con el editado).</summary>
    public Tercero Clonar()
    {
        var copia = new Tercero
        {
            IdTercero = IdTercero, TipoPersona = TipoPersona, Nombre = Nombre, NombrePersona = NombrePersona, PrimerApellido = PrimerApellido,
            SegundoApellido = SegundoApellido, RazonSocial = RazonSocial, NombreComercial = NombreComercial, Nif = Nif, TipoIdentificacion = TipoIdentificacion,
            Telefono = Telefono, Email = Email, Pais = Pais, CodigoPostal = CodigoPostal, Provincia = Provincia, Poblacion = Poblacion, Direccion = Direccion,
            Notas = Notas, NotasInternas = NotasInternas, Activo = Activo, FechaAlta = FechaAlta, Version = Version,
            CuentasRoles = CuentasRoles?.Clonar(),
        };
        copia.Domicilios.AddRange(Domicilios);
        var grupos = new Dictionary<ContactoLibre, ContactoLibre>(ReferenceEqualityComparer.Instance);
        foreach (var g in ContactosLibres) grupos[g] = g.Clonar();
        copia.ContactosLibres.AddRange(ContactosLibres.Select(g => grupos[g]));
        copia.DatosContacto.AddRange(DatosContacto.Select(d => d.Clonar(d.ContactoLibre is { } g && grupos.TryGetValue(g, out var cg) ? cg : d.ContactoLibre)));
        copia.Roles.CopiarDe(Roles);
        copia.Perfiles.AddRange(Perfiles.Select(p => p.Clonar()));
        copia.Relaciones.AddRange(Relaciones.Select(r => r.Clonar()));
        return copia;
    }

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
        foreach (var d in Domicilios.Where(d => string.IsNullOrWhiteSpace(d.Direccion) || string.IsNullOrWhiteSpace(d.TipoDomicilio)))
            errores.Add(new Error("Tercero.DomicilioIncompleto", "Hay un domicilio adicional sin finalidad o sin vía.", registro,
                "Cada domicilio adicional necesita su finalidad (fiscal, envío…) y la vía con el número.", "Complétalo o quita el domicilio."));
        foreach (var r in Relaciones)
        {
            var e = r.Validar();
            if (e is not null) errores.Add(e with { RegistroAfectado = registro });
        }
        foreach (var repetida in Relaciones.GroupBy(r => (r.IdTerceroPersona, r.IdTerceroOrganizacion, Tipo: r.TipoRelacion.Trim().ToUpperInvariant(), r.FechaInicio)).Where(g => g.Count() > 1))
            errores.Add(new Error("Relaciones.Repetida", $"La relación «{repetida.First().TipoRelacion}» entre {repetida.First().NombrePersona} y {repetida.First().NombreOrganizacion} está repetida.", registro,
                "No puede haber dos relaciones del mismo tipo con la misma fecha de inicio.", "Cambia la fecha de inicio o quita la repetida."));
        if (Roles.Contacto is { } contacto)
        {
            if (contacto.Cargo is { Length: > 100 }) errores.Add(new Error("Roles.CargoLargo", "El cargo del contacto tiene más de 100 caracteres.", registro, "No cabe.", "Acórtalo."));
            if (contacto.Departamento is { Length: > 100 }) errores.Add(new Error("Roles.DepartamentoLargo", "El departamento del contacto tiene más de 100 caracteres.", registro, "No cabe.", "Acórtalo."));
        }
        if (Roles.Empleado?.CodigoTrabajador is { Length: > 30 })
            errores.Add(new Error("Empleados.CodigoLargo", "El código del trabajador tiene más de 30 caracteres.", registro, "No cabe.", "Escribe el código que usa la gestoría."));
        foreach (var grupo in ContactosLibres.Where(g => string.IsNullOrWhiteSpace(g.Nombre)))
            errores.Add(new Error("Contactos.SinNombre", "Un contacto libre no tiene nombre.", registro,
                "El nombre de la persona de contacto es obligatorio.", "Escribe su nombre o quita el contacto."));
        return Resultado.Combinar(errores.Select(Resultado.Fallo).ToArray());
    }
}
