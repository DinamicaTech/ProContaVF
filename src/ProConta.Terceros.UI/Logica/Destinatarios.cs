using ProConta.Terceros.Contactos;

namespace ProConta.Terceros.UI.Logica;

/// <summary>
/// A quién llegan los envíos automáticos de cada finalidad, visto en la tabla de emails de la ficha (RF-TER-09): qué
/// emails reciben una finalidad sin estar marcados (el único email activo) y, si no se puede elegir ninguno, el aviso
/// de esa columna. Se calcula con <see cref="ResolutorDestinatarios"/> sobre los datos tal como están en la ficha.
/// </summary>
public sealed record EstadoDestinatarios(
    IReadOnlySet<DatoContacto> FacturasImplicitas,
    IReadOnlySet<DatoContacto> AdministrativosImplicitos,
    string? AvisoFacturas,
    string? AvisoAdministrativos)
{
    public bool RecibeImplicito(DatoContacto d, FinalidadEnvio f) =>
        (f == FinalidadEnvio.Facturas ? FacturasImplicitas : AdministrativosImplicitos).Contains(d);
}

public static class DestinatariosFicha
{
    public static EstadoDestinatarios Calcular(IEnumerable<DatoContacto> datos)
    {
        var lista = datos.ToList();
        var (fi, fa) = Finalidad(lista, FinalidadEnvio.Facturas);
        var (ai, aa) = Finalidad(lista, FinalidadEnvio.Administrativos);
        return new EstadoDestinatarios(fi, ai, fa, aa);
    }

    private static (IReadOnlySet<DatoContacto> Implicitos, string? Aviso) Finalidad(List<DatoContacto> datos, FinalidadEnvio finalidad)
    {
        var r = ResolutorDestinatarios.Resolver(datos, finalidad);
        bool Marcado(DatoContacto d) => finalidad == FinalidadEnvio.Facturas ? d.EnvioFacturas : d.EnviosAdministrativos;
        var implicitos = new HashSet<DatoContacto>(ReferenceEqualityComparer.Instance);
        if (r.HayDestinatarios)
        {
            foreach (var d in datos.Where(d => d.Tipo == TipoDatoContacto.Email && d.Activo && !Marcado(d) &&
                         r.Emails.Contains(d.Valor.Trim(), StringComparer.OrdinalIgnoreCase)))
                implicitos.Add(d);
            return (implicitos, null);
        }
        if (r.Incidencia?.Codigo != "Contactos.EmailAmbiguo") return (implicitos, null);
        var que = finalidad == FinalidadEnvio.Facturas ? "las facturas" : "las comunicaciones administrativas";
        return (implicitos, $"Hay varios emails activos y ninguno marcado: no se enviarán {que} por email hasta que marques cuál o cuáles las reciben.");
    }

    public static string AyudaImplicita(FinalidadEnvio f) => f == FinalidadEnvio.Facturas
        ? "Recibe los envíos de facturas aunque no esté marcado, porque es el único email activo. Márcalo si quieres fijarlo."
        : "Recibe las comunicaciones administrativas aunque no esté marcado, porque es el único email activo. Márcalo si quieres fijarlo.";
}
