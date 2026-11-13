using System.Globalization;
using ProConta.Terceros.Contactos;

namespace ProConta.Terceros.UI.Logica;

/// <summary>
/// Error de un campo de la ficha: en qué pestaña está, qué objeto lo tiene (la ficha, un domicilio, un email…), qué
/// campo (el nombre de la propiedad del modelo de vista) y el mensaje que va en su tooltip y en el resumen superior.
/// </summary>
public sealed record ErrorFicha(PestanaFicha Pestana, object Objeto, string Campo, string Mensaje);

/// <summary>Domicilio editable de la ficha (el principal, en columnas de Terceros, o uno adicional).</summary>
public interface IDomicilioEditable
{
    string Etiqueta { get; }
    string? Pais { get; }
    string? CodigoPostal { get; }
    string? Direccion { get; }
    string? TipoDomicilio { get; }
    bool EsPrincipal { get; }
}

/// <summary>Reglas de los campos que la ficha comprueba mientras se edita (además de las del ente al guardar).</summary>
public static class ValidacionFicha
{
    public static string? ErrorCodigoPostal(string? pais, string? cp, string etiqueta)
    {
        var v = cp?.Trim() ?? "";
        if (v.Length == 0 || !Paises.EsEspana(pais)) return null;
        return v.Length == 5 && v.All(char.IsAsciiDigit) ? null : $"El código postal del domicilio «{etiqueta}» debe tener 5 dígitos.";
    }

    public static string? ErrorEmail(string? valor)
    {
        var v = valor?.Trim() ?? "";
        if (v.Length == 0) return "Escribe la dirección o quita la fila.";
        var arroba = v.IndexOf('@');
        if (arroba <= 0 || arroba != v.LastIndexOf('@') || arroba == v.Length - 1 || v.Contains(' ') || !v[(arroba + 1)..].Contains('.'))
            return $"El email «{v}» no tiene un formato válido (nombre@dominio.es).";
        return v.Length > 254 ? "El email tiene más de 254 caracteres." : null;
    }

    public static string? ErrorTelefono(string? valor)
    {
        var v = valor?.Trim() ?? "";
        if (v.Length == 0) return "Escribe el número o quita la fila.";
        return v.Count(char.IsAsciiDigit) < 6 ? $"El teléfono «{v}» tiene menos de 6 cifras." : null;
    }

    /// <summary>Lee una fecha escrita como dd/mm/aaaa (también con guiones o sin año, que toma el actual).</summary>
    public static bool LeerFecha(string? texto, out DateTime? fecha)
    {
        fecha = null;
        var t = texto?.Trim() ?? "";
        if (t.Length == 0) return true;
        string[] formatos = ["d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yy", "d-M-yy", "d/M", "ddMMyyyy"];
        if (DateTime.TryParseExact(t, formatos, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.None, out var f))
        {
            fecha = f.Date;
            return true;
        }
        return false;
    }

    /// <summary>Errores de la ficha completa (los que se ven en el resumen al intentar guardar).</summary>
    public static IReadOnlyList<ErrorFicha> Revisar(Tercero t, IEnumerable<IDomicilioEditable> domicilios)
    {
        var errores = new List<ErrorFicha>();
        if (t.TipoPersona is null && (t.EsNuevo || string.IsNullOrWhiteSpace(t.Nombre)))
            errores.Add(new(PestanaFicha.Identidad, t, "TipoPersona", "Elige si es un particular, una empresa o una entidad."));
        if (t.TipoPersona == TipoPersona.Fisica && string.IsNullOrWhiteSpace(t.NombrePersona))
            errores.Add(new(PestanaFicha.Identidad, t, "NombrePersona", "Escribe el nombre de la persona (los apellidos son opcionales)."));
        if (t.TipoPersona is TipoPersona.Juridica or TipoPersona.EntidadSinPersonalidad && string.IsNullOrWhiteSpace(t.RazonSocial))
            errores.Add(new(PestanaFicha.Identidad, t, "RazonSocial", "Escribe la razón social."));
        if (t.TipoPersona is null && string.IsNullOrWhiteSpace(t.Nombre))
            errores.Add(new(PestanaFicha.Identidad, t, "Nombre", "Escribe el nombre del tercero."));
        if (t.Roles.Cliente is { Activo: true, Iban: { } iban } && !string.IsNullOrWhiteSpace(iban) && !Nucleo.Identificacion.Ibanes.EsValido(iban))
            errores.Add(new(PestanaFicha.Roles, t, "IbanCliente", "El IBAN no es válido: revisa los dígitos (uno español tiene ES y 22 cifras) o déjalo vacío."));
        foreach (var d in domicilios)
        {
            if (ErrorCodigoPostal(d.Pais, d.CodigoPostal, d.Etiqueta) is { } cp) errores.Add(new(PestanaFicha.Identidad, d, "CodigoPostal", cp));
            if (!d.EsPrincipal && string.IsNullOrWhiteSpace(d.Direccion)) errores.Add(new(PestanaFicha.Identidad, d, "Direccion", $"Escribe la vía y el número del domicilio «{d.Etiqueta}» o quítalo."));
            if (!d.EsPrincipal && string.IsNullOrWhiteSpace(d.TipoDomicilio)) errores.Add(new(PestanaFicha.Identidad, d, "TipoDomicilio", "Elige la finalidad del domicilio."));
        }
        foreach (var d in t.DatosContacto)
        {
            var e = d.Tipo == TipoDatoContacto.Email ? ErrorEmail(d.Valor) : ErrorTelefono(d.Valor);
            if (e is not null) errores.Add(new(PestanaFicha.Contacto, d, "Valor", e));
        }
        foreach (var g in t.ContactosLibres.Where(g => string.IsNullOrWhiteSpace(g.Nombre)))
            errores.Add(new(PestanaFicha.Contacto, g, "Nombre", "Escribe el nombre de la persona de contacto o quítala."));
        foreach (var r in t.Relaciones)
        {
            if (string.IsNullOrWhiteSpace(r.TipoRelacion)) errores.Add(new(PestanaFicha.Contacto, r, "TipoRelacion", $"Elige el tipo de la relación con {Otro(t, r)}."));
            if (r.FechaInicio is { } i && r.FechaFin is { } f && f < i)
                errores.Add(new(PestanaFicha.Contacto, r, "TextoHasta", $"La relación con {Otro(t, r)} termina antes de empezar: revisa las fechas."));
        }
        if (t.Roles.Empleado?.CodigoTrabajador is { Length: > 30 })
            errores.Add(new(PestanaFicha.Roles, t, "CodigoTrabajador", "El código del trabajador tiene más de 30 caracteres: escribe el que usa la gestoría."));
        return errores;
    }

    private static string Otro(Tercero t, RelacionContacto r) =>
        r.IdTerceroPersona == t.IdTercero || r.IdTerceroPersona == 0 ? r.NombreOrganizacion : r.NombrePersona;
}
