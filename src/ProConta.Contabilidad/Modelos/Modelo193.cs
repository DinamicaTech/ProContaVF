using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Un perceptor del 193 (socio): identificación, dividendos íntegros, porcentaje y retención del año, y de dónde sale.</summary>
public sealed record Perceptor193(PerceptorInformativa Socio, decimal Base, decimal Porcentaje, decimal Retencion, string Origen);

public sealed record Declaracion193(int Ejercicio, IReadOnlyList<Perceptor193> Perceptores, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    public decimal TotalBase => Perceptores.Sum(p => p.Base);
    public decimal TotalRetenciones => Perceptores.Sum(p => p.Retencion);
}

/// <summary>
/// Modelo 193, resumen anual de retenciones del capital mobiliario (contabilidad/modelos-tributarios/modelo-193), solo el cálculo: el
/// diseño de registro no está en el proyecto (pendiente del OK del propietario para descargarlo), así que no hay fichero.
/// <list type="bullet">
/// <item>Dividendos repartidos en el año desde el programa (RepartosDividendos y sus socios, por la fecha del reparto): bruto y retención
/// de cada socio.</item>
/// <item>Sin repartos en el programa (los migrados del ERP son solo asientos): lo abonado en el año en la subcuenta de retención de cada
/// socio (SociosSociedad.IdIdentidadRetencion) y la base = retención × 100 / porcentaje de retención de dividendos del ejercicio
/// (19 % si no está informado), redondeada a céntimos.</item>
/// </list>
/// </summary>
public sealed class Servicio193(IBaseDatos bd)
{
    public const string Modelo = "193";

    public Task<Declaracion193> CalcularAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) => bd.EjecutarAsync(async s =>
    {
        var esquema = s.LeerEsquema();
        var errores = new List<string>();
        var avisos = new List<string>();
        var socios = await s.ConsultarAsync(
            "SELECT so.IdSocio, ss.IdIdentidadRetencion, t.NIF, t.TipoPersona, t.RazonSocial, t.Nombre, t.PrimerApellido, t.SegundoApellido, t.NombrePersona, t.CodigoPostal " +
            "FROM (SociosSociedad AS ss INNER JOIN Socios AS so ON ss.IdSocio = so.IdSocio) LEFT JOIN Terceros AS t ON so.IdTercero = t.IdTercero WHERE ss.IdSociedad = ?",
            f =>
            {
                var juridica = string.Equals(f.Texto("TipoPersona")?.Trim(), "JURIDICA", StringComparison.OrdinalIgnoreCase);
                var nombre = juridica ? f.Texto("RazonSocial") ?? f.Texto("Nombre")
                    : string.Join(' ', new[] { f.Texto("PrimerApellido"), f.Texto("SegundoApellido"), f.Texto("NombrePersona") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())) is { Length: > 0 } n
                        ? n : f.Texto("Nombre") ?? "";
                var cp = f.Texto("CodigoPostal")?.Trim();
                return (Id: f.Entero("IdSocio"), Retencion: f.EnteroNulo("IdIdentidadRetencion"),
                    P: new PerceptorInformativa(Modelo347.Nif(f.Texto("NIF")), (nombre ?? "").Trim(), cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) ? cp[..2] : null));
            }, ctx.IdSociedad);
        var r = new List<Perceptor193>();
        if (esquema.ExisteTabla("RepartosDividendosSocios"))
        {
            var repartos = await s.ConsultarAsync(
                "SELECT rs.IdSocio, rs.Bruto, rs.Retencion, r.PorcentajeRetencion FROM (RepartosDividendosSocios AS rs INNER JOIN RepartosDividendos AS r ON rs.IdRepartoDividendos = r.IdRepartoDividendos) " +
                "INNER JOIN SociedadesEjercicios AS se ON r.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ? AND r.Fecha >= ? AND r.Fecha < ?",
                f => (Socio: f.Entero("IdSocio"), Bruto: f.DecimalNulo("Bruto") ?? 0m, Ret: f.DecimalNulo("Retencion") ?? 0m, Pct: f.DecimalNulo("PorcentajeRetencion") ?? 0m),
                ctx.IdSociedad, new DateTime(ctx.Ejercicio, 1, 1), new DateTime(ctx.Ejercicio + 1, 1, 1));
            foreach (var g in repartos.GroupBy(x => x.Socio))
            {
                var socio = socios.FirstOrDefault(x => x.Id == g.Key).P ?? new PerceptorInformativa("", $"Socio {g.Key}", null);
                r.Add(new Perceptor193(socio, g.Sum(x => x.Bruto), g.First().Pct, g.Sum(x => x.Ret), "reparto de dividendos del programa"));
            }
        }
        if (r.Count == 0)
        {
            var pct = esquema.ExisteColumna("SociedadesEjercicios", "PorcentajeRetencionDividendos")
                ? await s.EscalarAsync("SELECT PorcentajeRetencionDividendos FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) is { } v and not DBNull
                    ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : (decimal?)null
                : null;
            var porcentaje = pct is > 0m ? pct.Value : 19m;
            foreach (var so in socios.Where(x => x.Retencion is not null))
            {
                var cuenta = await s.EscalarAsync("SELECT Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?", ctx.IdSociedadEjercicio, so.Retencion!.Value) as string;
                if (string.IsNullOrWhiteSpace(cuenta)) continue;
                var abonado = await s.EscalarAsync(
                    "SELECT SUM(p.ImporteHaber) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento = 0 " +
                    "AND p.CuentaContable = ?", ctx.IdSociedadEjercicio, cuenta.Trim()) is { } h and not DBNull ? Convert.ToDecimal(h, CultureInfo.InvariantCulture) : 0m;
                if (abonado == 0m) continue;
                r.Add(new Perceptor193(so.P, decimal.Round(abonado * 100m / porcentaje, 2, MidpointRounding.AwayFromZero), porcentaje, abonado, $"subcuenta de retención {cuenta.Trim()}"));
            }
            if (r.Count > 0)
                avisos.Add($"El reparto no está grabado en el programa: la base sale de la retención abonada en la subcuenta de cada socio al {porcentaje:0.##} %. Comprueba el bruto con el acta del reparto.");
        }
        foreach (var p in r)
        {
            if (!Modelo347.EsNifEspanol(p.Socio.Nif)) errores.Add($"{p.Socio.Nombre} no tiene un NIF español válido ({p.Socio.Nif}): corrígelo en su ficha.");
            if (p.Socio.Provincia is null) errores.Add($"{p.Socio.Nombre} no tiene código postal en su ficha: el 193 pide su provincia.");
        }
        errores.Add("El diseño de registro del 193 no está en el proyecto: el 193 se calcula pero aún no se genera su fichero.");
        return new Declaracion193(ctx.Ejercicio, r, errores, avisos);
    }, c);
}
