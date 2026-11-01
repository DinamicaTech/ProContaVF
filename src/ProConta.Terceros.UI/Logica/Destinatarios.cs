using ProConta.Terceros.Contactos;

namespace ProConta.Terceros.UI.Logica;

/// <summary>Una línea de la pastilla «Destinatarios efectivos»: la finalidad y a quién se enviaría, o por qué a nadie.</summary>
public sealed record LineaDestinatarios(FinalidadEnvio Finalidad, string TextoFinalidad, bool Correcta, string Texto, string Ayuda);

/// <summary>
/// Pastilla de destinatarios efectivos de cada finalidad (RF-TER-09), calculada con <see cref="ResolutorDestinatarios"/>
/// a partir de los emails tal como están en la ficha (también sin guardar), para que el operador vea al momento a
/// quién llegarán las facturas y los envíos administrativos.
/// </summary>
public static class PastillaDestinatarios
{
    public static IReadOnlyList<LineaDestinatarios> Calcular(IEnumerable<DatoContacto> datos, string? emailPrincipal = null)
    {
        var lista = datos.ToList();
        return [Linea(lista, FinalidadEnvio.Facturas, emailPrincipal), Linea(lista, FinalidadEnvio.Administrativos, emailPrincipal)];
    }

    private static LineaDestinatarios Linea(List<DatoContacto> datos, FinalidadEnvio finalidad, string? emailPrincipal)
    {
        var texto = finalidad == FinalidadEnvio.Facturas ? "Envío de facturas" : "Comunic. administrativas";
        var r = ResolutorDestinatarios.Resolver(datos, finalidad);
        if (r.HayDestinatarios)
        {
            var marcados = datos.Any(d => d.Tipo == TipoDatoContacto.Email && d.Activo &&
                (finalidad == FinalidadEnvio.Facturas ? d.EnvioFacturas : d.EnviosAdministrativos));
            return new(finalidad, texto, true, string.Join(", ", r.Emails),
                marcados ? "Emails activos marcados para esta finalidad." : "No hay ninguno marcado, pero solo hay un email activo: se usa ese.");
        }
        var problema = r.Incidencia!.Mensaje + " " + r.Incidencia.AccionCorrectora;
        if (r.Incidencia.Codigo == "Contactos.SinEmail" && !string.IsNullOrWhiteSpace(emailPrincipal))
            problema = $"El email de la ficha antigua ({emailPrincipal.Trim()}) no se usa para los envíos automáticos. Añádelo a la lista de emails.";
        return new(finalidad, texto, false, problema, "Sin destinatario: los envíos automáticos de esta finalidad quedarán como incidencia.");
    }
}
