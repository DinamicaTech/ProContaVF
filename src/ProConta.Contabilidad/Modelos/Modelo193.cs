using System.Globalization;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Un perceptor del 193 (socio): identificación, dividendos íntegros, porcentaje y retención, de dónde sale y la fecha del pago.</summary>
public sealed record Perceptor193(PerceptorInformativa Socio, decimal Base, decimal Porcentaje, decimal Retencion, string Origen, DateTime? Fecha = null);

public sealed record Declaracion193(int Ejercicio, Declarante303 Declarante, IReadOnlyList<Perceptor193> Perceptores, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    public decimal TotalBase => Perceptores.Sum(p => p.Base);
    public decimal TotalRetenciones => Perceptores.Sum(p => p.Retencion);
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Registros del 193 con el diseño de la AEAT (sources/aeat/DR193.pdf, ejercicio 2025): tipo 1 del declarante y un tipo 2 por socio con
/// dividendos (clave de percepción «A», naturaleza 02).
/// <list type="bullet">
/// <item>La sociedad paga sus propios dividendos: naturaleza del declarante «S» (no es entidad financiera, emisora con pagos por cuenta
/// ajena ni perceptora de gastos de administración); por eso en el tipo 2 van a ceros o en blanco el mediador, la clave y el código
/// emisor, el pago, el tipo de código, la cuenta de valores, las penalizaciones, el ISIN, las fechas del préstamo, las compensaciones y
/// las garantías, y en el tipo 1 los ingresados (175-189) y los gastos (220-234).</item>
/// <item>Tipo 2: percepción dineraria (122 = 1), importe íntegro (123-135) = base (152-164), % de retención (165-168, dos enteros y dos
/// decimales), retenciones (169-181), número de orden (315-321), fecha de devengo = fecha del pago del dividendo (331-338) y clave de
/// mercado «D» (otros: participaciones de una SL).</item>
/// </list>
/// </summary>
public static class Modelo193
{
    public const string Modelo = "193";

    public static (string Tipo1, IReadOnlyList<string> Tipo2) Registros(Declaracion193 d, ContactoInformativa contacto, string numero, string? anterior = null, bool sustitutiva = false)
    {
        var t1 = Tipo1Informativa.Base(Modelo, d.Ejercicio, d.Declarante, contacto, numero, anterior, sustitutiva)
            .Num(136, 144, d.Perceptores.Count).Importe(145, 159, d.TotalBase).Importe(160, 174, d.TotalRetenciones).Importe(175, 189, 0m)
            .Importe(220, 234, 0m).Texto(235, "S");
        var t2 = d.Perceptores.Select((p, i) => new RegistroInformativo()
            .Texto(1, "2").Texto(2, Modelo).Num(5, 8, d.Ejercicio).Nif(9, d.Declarante.Nif).Nif(18, p.Socio.Nif).An(36, 75, p.Socio.Nombre)
            .Num(77, 78, int.TryParse(p.Socio.Provincia, out var prov) ? prov : 0).Num(79, 79, 0).Texto(92, "A").Num(93, 94, 2).Num(95, 95, 0)
            .Num(118, 121, 0).Num(122, 122, 1).Importe(123, 135, p.Base).Importe(139, 151, 0m).Importe(152, 164, p.Base)
            .Num(165, 168, (long)decimal.Round(p.Porcentaje * 100m, 0, MidpointRounding.AwayFromZero)).Importe(169, 181, p.Retencion).Importe(182, 192, 0m)
            .Texto(208, "S").Num(209, 216, 0).Num(217, 224, 0).Importe(225, 236, 0m).Importe(237, 248, 0m).Num(249, 313, 0).Num(314, 314, 0)
            .Num(315, 321, i + 1).Num(331, 338, p.Fecha is DateTime f ? long.Parse(f.ToString("ddMMyyyy", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : 0)
            .Texto(339, "D").ToString()).ToList();
        return (t1.ToString(), t2);
    }
}

/// <summary>
/// Modelo 193, resumen anual de retenciones del capital mobiliario (contabilidad/modelos-tributarios/modelo-193): el cálculo, su estado
/// y la presentación por TGVI Online con la pieza común.
/// <list type="bullet">
/// <item>Dividendos repartidos en el año desde el programa (RepartosDividendos y sus socios, por la fecha del reparto): bruto y retención
/// de cada socio.</item>
/// <item>Sin repartos en el programa (los migrados del ERP son solo asientos): lo abonado en el año en la subcuenta de retención de cada
/// socio (SociosSociedad.IdIdentidadRetencion) y la base = retención × 100 / porcentaje de retención de dividendos del ejercicio
/// (19 % si no está informado), redondeada a céntimos; la fecha, la del último abono.</item>
/// </list>
/// </summary>
public sealed class Servicio193(IBaseDatos bd, ServicioTgvi? tgvi = null, IReloj? reloj = null) : ServicioInformativaIva(bd, tgvi!, reloj)
{
    public const string Modelo = Modelo193.Modelo;

    /// <summary>Plazo del 193: del 1 al 31 de enero del año siguiente.</summary>
    public static (DateTime Desde, DateTime Hasta) Plazo(int ejercicio) => (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 31));

    public async Task<Declaracion193> CalcularAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var (r, avisos) = await PerceptoresAsync(ctx, new DateTime(ctx.Ejercicio, 1, 1), new DateTime(ctx.Ejercicio + 1, 1, 1), c);
        var errores = new List<string>();
        foreach (var p in r)
        {
            if (!Modelo347.EsNifEspanol(p.Socio.Nif)) errores.Add($"{p.Socio.Nombre} no tiene un NIF español válido ({p.Socio.Nif}): corrígelo en su ficha.");
            if (p.Socio.Provincia is null) errores.Add($"{p.Socio.Nombre} no tiene código postal en su ficha: el 193 pide su provincia.");
        }
        return new Declaracion193(ctx.Ejercicio, await DeclaranteAsync(ctx), r, errores, avisos);
    }

    /// <summary>El 193 del ejercicio con su estado en la pantalla de modelos.</summary>
    public async Task<PeriodoInformativa> EjercicioAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var d = await CalcularAsync(ctx, c);
        var (desde, hasta) = Plazo(ctx.Ejercicio);
        return await PeriodoAsync(ctx, Modelo, "0A", desde, hasta, d.Perceptores.Count, d.TotalRetenciones, d.Errores, d.Avisos, c);
    }

    /// <summary>La declaración y sus registros para presentarla por TGVI Online; con <paramref name="sustitutiva"/>, sustitutiva de la última presentada.</summary>
    public async Task<Resultado<(Declaracion193 Declaracion, DeclaracionInformativa Tgvi)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, bool sustitutiva, string? contacto = null,
        CancellationToken c = default)
    {
        var registro = $"Modelo 193 {ctx.Ejercicio}";
        var d = await CalcularAsync(ctx, c);
        if (d.Perceptores.Count == 0)
            return Resultado<(Declaracion193, DeclaracionInformativa)>.Fallo(new Error("Modelo193.SinDividendos", $"No hay dividendos con retención en {ctx.Ejercicio}.", registro,
                "El 193 resume las retenciones de los dividendos del año; sin repartos no se presenta."));
        if (!d.Valida)
            return Resultado<(Declaracion193, DeclaracionInformativa)>.Fallo(new Error("Modelo193.Errores", $"El 193 de {ctx.Ejercicio} no se puede presentar todavía.", registro,
                string.Join(" ", d.Errores), "Completa los datos que se indican y vuelve a intentarlo."));
        var persona = await ContactoAsync(ctx, contacto);
        if (persona.TelefonoNumerico == 0) return Resultado<(Declaracion193, DeclaracionInformativa)>.Fallo(SinTelefono(registro));
        var anterior = sustitutiva ? await AnteriorAsync(ctx, Modelo, "0A", c) : null;
        var numero = await Tgvi.NumeroJustificanteAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, "0A", c);
        var (t1, t2) = Modelo193.Registros(d, persona, numero, anterior?.Justificante, sustitutiva);
        return Resultado<(Declaracion193, DeclaracionInformativa)>.Correcto((d, new DeclaracionInformativa(Modelo, ctx.Ejercicio, "0A", d.Declarante.Nif, d.Declarante.RazonSocial,
            t1, t2, numero, d.TotalRetenciones, [("Perceptores", d.Perceptores.Count.ToString(CultureInfo.InvariantCulture)), ("Base", Euros(d.TotalBase)),
                ("Retenciones", Euros(d.TotalRetenciones))], anterior?.Justificante)));
    }

    /// <summary>
    /// Dividendos de cada socio con fecha (del reparto o del asiento) en [<paramref name="desde"/>, <paramref name="hasta"/>): los del 193
    /// del año y los del 123 del trimestre.
    /// </summary>
    internal Task<(List<Perceptor193> Perceptores, List<string> Avisos)> PerceptoresAsync(ContextoSociedadEjercicio ctx, DateTime desde, DateTime hasta,
        CancellationToken c = default) => Bd.EjecutarAsync(async s =>
    {
        var esquema = s.LeerEsquema();
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
                    P: new PerceptorInformativa(Modelo200.Nif(f.Texto("NIF")), (nombre ?? "").Trim(), cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) ? cp[..2] : null));
            }, ctx.IdSociedad);
        var r = new List<Perceptor193>();
        if (esquema.ExisteTabla("RepartosDividendosSocios"))
        {
            var repartos = await s.ConsultarAsync(
                "SELECT rs.IdSocio, rs.Bruto, rs.Retencion, r.PorcentajeRetencion, r.Fecha FROM (RepartosDividendosSocios AS rs INNER JOIN RepartosDividendos AS r ON rs.IdRepartoDividendos = r.IdRepartoDividendos) " +
                "INNER JOIN SociedadesEjercicios AS se ON r.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ? AND r.Fecha >= ? AND r.Fecha < ?",
                f => (Socio: f.Entero("IdSocio"), Bruto: f.DecimalNulo("Bruto") ?? 0m, Ret: f.DecimalNulo("Retencion") ?? 0m, Pct: f.DecimalNulo("PorcentajeRetencion") ?? 0m,
                    Fecha: f.Fecha("Fecha")),
                ctx.IdSociedad, desde, hasta);
            foreach (var g in repartos.GroupBy(x => x.Socio))
            {
                var socio = socios.FirstOrDefault(x => x.Id == g.Key).P ?? new PerceptorInformativa("", $"Socio {g.Key}", null);
                r.Add(new Perceptor193(socio, g.Sum(x => x.Bruto), g.First().Pct, g.Sum(x => x.Ret), "reparto de dividendos del programa", g.Max(x => x.Fecha)));
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
                var abonos = await s.ConsultarAsync(
                    "SELECT a.FechaContable, p.ImporteHaber FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 " +
                    "AND a.TipoAsiento = 0 AND p.CuentaContable = ? AND a.FechaContable >= ? AND a.FechaContable < ? AND p.ImporteHaber <> 0",
                    f => (Fecha: f.Fecha("FechaContable"), Importe: f.DecimalNulo("ImporteHaber") ?? 0m), ctx.IdSociedadEjercicio, cuenta.Trim(), desde, hasta);
                var abonado = abonos.Sum(a => a.Importe);
                if (abonado == 0m) continue;
                r.Add(new Perceptor193(so.P, decimal.Round(abonado * 100m / porcentaje, 2, MidpointRounding.AwayFromZero), porcentaje, abonado,
                    $"subcuenta de retención {cuenta.Trim()}", abonos.Max(a => a.Fecha)));
            }
            if (r.Count > 0)
                avisos.Add($"El reparto no está grabado en el programa: la base sale de la retención abonada en la subcuenta de cada socio al {porcentaje:0.##} %. Comprueba el bruto con el acta del reparto.");
        }
        return (r, avisos);
    }, c);
}
