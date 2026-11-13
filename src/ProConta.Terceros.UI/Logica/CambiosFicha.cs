using ProConta.Terceros.Contactos;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros.UI.Logica;

/// <summary>Pestañas de la ficha, en el orden de la maqueta.</summary>
public enum PestanaFicha
{
    Identidad,
    Contacto,
    Roles,
    Perfiles,
    Ventas,
    Compras,
    Nominas,
    Contabilidad,
    Notas,
    Documentos,
    Historial
}

/// <summary>Un cambio pendiente de la ficha: en qué pestaña está y qué ha cambiado, en una frase corta.</summary>
public sealed record CambioFicha(PestanaFicha Pestana, string Descripcion)
{
    public override string ToString() => $"{Pestana switch { PestanaFicha.Identidad => "Identidad", PestanaFicha.Contacto => "Contacto", PestanaFicha.Roles => "Roles", PestanaFicha.Perfiles => "Perfiles", PestanaFicha.Notas => "Notas", _ => Pestana.ToString() }} · {Descripcion}";
}

/// <summary>
/// Cambios pendientes de una ficha: compara la ficha tal como se leyó con la editada. Es lo que cuentan el pie («3
/// cambios sin guardar»), «Ver cuáles», el punto de la pestaña y el aviso al cerrar.
/// </summary>
public static class CambiosFicha
{
    public static IReadOnlyList<CambioFicha> Comparar(Tercero original, Tercero actual)
    {
        var cambios = new List<CambioFicha>();
        void Campo(PestanaFicha p, string nombre, string? a, string? b)
        {
            if (!string.Equals(Limpio(a), Limpio(b), StringComparison.Ordinal)) cambios.Add(new(p, nombre));
        }

        if (original.TipoPersona != actual.TipoPersona) cambios.Add(new(PestanaFicha.Identidad, "Tipo de persona"));
        Campo(PestanaFicha.Identidad, "Tipo de identificación", original.TipoIdentificacion, actual.TipoIdentificacion);
        Campo(PestanaFicha.Identidad, Identificaciones.Etiqueta(actual.TipoIdentificacion), original.Nif, actual.Nif);
        Campo(PestanaFicha.Identidad, "Nombre", original.NombrePersona, actual.NombrePersona);
        Campo(PestanaFicha.Identidad, "Primer apellido", original.PrimerApellido, actual.PrimerApellido);
        Campo(PestanaFicha.Identidad, "Segundo apellido", original.SegundoApellido, actual.SegundoApellido);
        Campo(PestanaFicha.Identidad, "Razón social", original.RazonSocial, actual.RazonSocial);
        Campo(PestanaFicha.Identidad, "Nombre comercial", original.NombreComercial, actual.NombreComercial);
        if (original.Activo != actual.Activo) cambios.Add(new(PestanaFicha.Identidad, actual.Activo ? "Ficha reactivada" : "Ficha desactivada"));
        Campo(PestanaFicha.Identidad, "Domicilio principal · País", original.Pais, actual.Pais);
        Campo(PestanaFicha.Identidad, "Domicilio principal · CP", original.CodigoPostal, actual.CodigoPostal);
        Campo(PestanaFicha.Identidad, "Domicilio principal · Provincia", original.Provincia, actual.Provincia);
        Campo(PestanaFicha.Identidad, "Domicilio principal · Población", original.Poblacion, actual.Poblacion);
        Campo(PestanaFicha.Identidad, "Domicilio principal · Vía y número", original.Direccion, actual.Direccion);
        Lista(cambios, PestanaFicha.Identidad, "Domicilio", original.Domicilios, actual.Domicilios,
            d => $"{TiposDomicilio.Texto(d.TipoDomicilio)}", d => $"{d.TipoDomicilio}|{d.Direccion}|{d.CodigoPostal}|{d.Poblacion}|{d.Provincia}|{d.Pais}|{d.Activo}");

        Campo(PestanaFicha.Contacto, "Teléfono principal", original.Telefono, actual.Telefono);
        Campo(PestanaFicha.Contacto, "Email principal", original.Email, actual.Email);
        Lista(cambios, PestanaFicha.Contacto, "Email", original.DatosContacto.Where(d => d.Tipo == TipoDatoContacto.Email).ToList(),
            actual.DatosContacto.Where(d => d.Tipo == TipoDatoContacto.Email).ToList(), d => string.IsNullOrWhiteSpace(d.Valor) ? "nuevo" : d.Valor.Trim(), Firma);
        Lista(cambios, PestanaFicha.Contacto, "Teléfono", original.DatosContacto.Where(d => d.Tipo == TipoDatoContacto.Telefono).ToList(),
            actual.DatosContacto.Where(d => d.Tipo == TipoDatoContacto.Telefono).ToList(), d => string.IsNullOrWhiteSpace(d.Valor) ? "nuevo" : d.Valor.Trim(), Firma);
        Lista(cambios, PestanaFicha.Contacto, "Persona de contacto", original.ContactosLibres, actual.ContactosLibres,
            g => string.IsNullOrWhiteSpace(g.Nombre) ? "nueva" : g.Nombre.Trim(), g => $"{g.Nombre}|{g.Departamento}");
        foreach (var r in actual.Relaciones)
        {
            var o = original.Relaciones.FirstOrDefault(x => !r.EsNueva && x.IdRelacionContacto == r.IdRelacionContacto);
            var quien = $"{r.TipoRelacion}: {r.NombrePersona} → {r.NombreOrganizacion}";
            if (o is null) cambios.Add(new(PestanaFicha.Contacto, $"Relación nueva · {quien}"));
            else if (Firma(o) != Firma(r)) cambios.Add(new(PestanaFicha.Contacto, o.FechaFin is null && r.FechaFin is not null ? $"Relación cerrada · {quien}" : $"Relación · {quien}"));
        }

        Rol(cambios, "Cliente", original.Roles.Cliente?.Activo, actual.Roles.Cliente?.Activo);
        Campo(PestanaFicha.Roles, "Cliente · Notas", original.Roles.Cliente?.Notas, actual.Roles.Cliente?.Notas);
        Rol(cambios, "Proveedor", original.Roles.Proveedor?.Activo, actual.Roles.Proveedor?.Activo);
        Campo(PestanaFicha.Roles, "Proveedor · Notas", original.Roles.Proveedor?.Notas, actual.Roles.Proveedor?.Notas);
        Rol(cambios, "Contacto", original.Roles.Contacto?.Activo, actual.Roles.Contacto?.Activo);
        Campo(PestanaFicha.Roles, "Contacto · Cargo", original.Roles.Contacto?.Cargo, actual.Roles.Contacto?.Cargo);
        Campo(PestanaFicha.Roles, "Contacto · Departamento", original.Roles.Contacto?.Departamento, actual.Roles.Contacto?.Departamento);
        Campo(PestanaFicha.Roles, "Contacto · Notas", original.Roles.Contacto?.Notas, actual.Roles.Contacto?.Notas);
        Rol(cambios, "Empleado", original.Roles.Empleado?.Activo, actual.Roles.Empleado?.Activo);
        Campo(PestanaFicha.Roles, "Empleado · Código en la gestoría", original.Roles.Empleado?.CodigoTrabajador, actual.Roles.Empleado?.CodigoTrabajador);
        foreach (var clase in new[] { ClaseCuentaRol.Cliente, ClaseCuentaRol.Retencion, ClaseCuentaRol.Proveedor, ClaseCuentaRol.Acreedor, ClaseCuentaRol.Empleado })
            if (original.CuentasRoles?.De(clase) != actual.CuentasRoles?.De(clase))
                cambios.Add(new(PestanaFicha.Roles, Mayuscula(ServicioRoles.Describir(clase))));

        foreach (var a in actual.Perfiles)
        {
            var o = original.Perfiles.FirstOrDefault(x => x.IdPerfil == a.IdPerfil);
            var antes = o?.Activo ?? false;
            if (antes != a.Activo) cambios.Add(new(PestanaFicha.Perfiles, a.Activo ? $"Perfil asignado · {a.Nombre}" : $"Perfil retirado · {a.Nombre}"));
        }
        foreach (var o in original.Perfiles.Where(o => o.Activo && actual.Perfiles.All(a => a.IdPerfil != o.IdPerfil)))
            cambios.Add(new(PestanaFicha.Perfiles, $"Perfil retirado · {o.Nombre}"));

        Campo(PestanaFicha.Notas, "Notas", original.Notas, actual.Notas);
        Campo(PestanaFicha.Notas, "Notas internas", original.NotasInternas, actual.NotasInternas);
        return cambios;
    }

    private static void Rol(List<CambioFicha> cambios, string rol, bool? antes, bool? despues)
    {
        if ((antes ?? false) == (despues ?? false)) return;
        cambios.Add(new(PestanaFicha.Roles, despues == true ? $"Rol {rol} activado" : $"Rol {rol} desactivado"));
    }

    /// <summary>Altas, bajas y modificaciones de una lista, emparejando por posición entre los que ya existían.</summary>
    private static void Lista<T>(List<CambioFicha> cambios, PestanaFicha p, string nombre, IReadOnlyList<T> antes, IReadOnlyList<T> despues, Func<T, string> etiqueta, Func<T, string> firma)
    {
        var n = Math.Min(antes.Count, despues.Count);
        for (var i = 0; i < n; i++)
            if (firma(antes[i]) != firma(despues[i])) cambios.Add(new(p, $"{nombre} · {etiqueta(despues[i])}"));
        for (var i = n; i < despues.Count; i++) cambios.Add(new(p, $"{nombre} añadido · {etiqueta(despues[i])}"));
        for (var i = n; i < antes.Count; i++) cambios.Add(new(p, $"{nombre} quitado · {etiqueta(antes[i])}"));
    }

    private static string Firma(DatoContacto d) => $"{d.Etiqueta}|{d.Valor}|{d.Activo}|{d.EnvioFacturas}|{d.EnviosAdministrativos}|{d.ContactoLibre?.Nombre}";

    private static string Firma(RelacionContacto r) => $"{r.TipoRelacion}|{r.FechaInicio:d}|{r.FechaFin:d}|{r.Observaciones}";

    private static string Mayuscula(string t) => char.ToUpperInvariant(t[0]) + t[1..];

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
