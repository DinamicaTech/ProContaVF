using System.Globalization;
using System.Net;
using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Dividendos;

/// <summary>Un socio en el acta: sus participaciones, su numeración, su % y lo que le toca del reparto.</summary>
public sealed record SocioActa(string Nombre, int? Participaciones, string? Numeracion, decimal Participacion, decimal Bruto, decimal Retencion, decimal Neto, string? CargoJunta);

/// <summary>El administrador de la sociedad, con los datos de su ficha (rol Empleado marcado como administrador).</summary>
public sealed record AdministradorActa(string Nombre, string Cargo, string? Nacionalidad, string? Profesion, string? EstadoCivil, string? Direccion, string? Poblacion, string? Nif);

/// <summary>Todo lo que lleva el acta de la junta que aprueba un reparto de dividendos.</summary>
public sealed record DatosActa(int IdReparto, string Sociedad, string Cif, decimal? ValorNominal, DateTime Fecha, string Hora, string Origen, decimal PorcentajeRetencion,
    IReadOnlyList<SocioActa> Socios, AdministradorActa? Administrador, string Presidente, string Secretario, IReadOnlyList<string> Avisos)
{
    public decimal Bruto => Socios.Sum(s => s.Bruto);
    public decimal Retencion => Socios.Sum(s => s.Retencion);
    public decimal Neto => Socios.Sum(s => s.Neto);
}

/// <summary>
/// Acta de la Junta General y Universal de Socios que aprueba un reparto de dividendos (contabilidad/procesos/aplicacion-resultado 2026-11-17),
/// como la que se usaba hasta ahora en Word: asistentes con su capital y participaciones, administrador, presidente y secretario, orden del día,
/// el acuerdo del reparto con su importe en letras y la tabla por socio, la delegación de facultades y las firmas. Se genera en HTML y la
/// interfaz lo pasa a PDF con el motor de informes.
/// </summary>
public static class ActaDividendos
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Hora en el formato del acta: «11:00» → «11,00».</summary>
    public static string Hora(string? hora)
    {
        var t = (hora ?? "").Trim().Replace('.', ':').Replace(',', ':');
        if (TimeSpan.TryParseExact(t, ["h\\:mm", "hh\\:mm", "%h"], CultureInfo.InvariantCulture, out var h)) return $"{h.Hours},{h.Minutes:00}";
        return t.Length == 0 ? "11,00" : hora!.Trim();
    }

    /// <summary>¿La hora escrita es válida (hh:mm)?</summary>
    public static bool HoraValida(string? hora) =>
        TimeSpan.TryParseExact((hora ?? "").Trim().Replace('.', ':').Replace(',', ':'), ["h\\:mm", "hh\\:mm", "%h"], CultureInfo.InvariantCulture, out var h) && h < TimeSpan.FromDays(1);

    /// <summary>Nombre del fichero: «ActaDividendos<sociedad><aaaammdd>.pdf», como el que se usaba.</summary>
    public static string NombreFichero(string sociedad, DateTime fecha)
    {
        var limpio = new string((sociedad ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Where(char.IsLetterOrDigit).ToArray() ?? []);
        var nombre = $"ActaDividendos{(limpio.Length > 0 ? char.ToUpper(limpio[0], Es) + limpio[1..].ToLower(Es) : "")}{fecha:yyyyMMdd}.pdf";
        return string.Concat(nombre.Split(Path.GetInvalidFileNameChars()));
    }

    /// <summary>Importe en euros como sale en el acta: «61.728,40».</summary>
    private static string N(decimal v) => v.ToString("#,##0.00", Es);
    private static string Entero(decimal v) => v.ToString("#,##0.##", Es);
    private static string H(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string M(string? s) => H((s ?? "").Trim().ToUpper(Es));

    public static string Html(DatosActa d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var sb = new StringBuilder();
        var fecha = d.Fecha.ToString("d 'de' MMMM 'de' yyyy", Es);
        var cargo = d.Administrador?.Cargo is { Length: > 0 } c ? c : "Administrador único";
        sb.Append("""
            <!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><title>Acta</title>
            <style type="text/css" media="print">@page { size: 210mm 297mm; margin: 22mm 22mm 20mm 22mm; } body { margin: 0; }</style>
            <style>
            body { font-family: "Times New Roman", Times, serif; font-size: 11.5pt; line-height: 1.45; color: #000; }
            h1 { font-size: 13pt; text-align: center; margin: 0; } h2 { font-size: 12pt; text-align: center; margin: 2pt 0 0 0; }
            .cif { text-align: center; margin: 2pt 0 18pt 0; } p { text-align: justify; margin: 0 0 9pt 0; }
            .titulo { text-align: center; font-weight: bold; text-decoration: underline; margin: 14pt 0 8pt 0; }
            .seccion { font-weight: bold; margin: 12pt 0 6pt 0; }
            table.socio { border-collapse: collapse; margin: 0 0 10pt 18pt; } table.socio td { padding: 1pt 10pt 1pt 0; vertical-align: top; }
            table.socio td.e { font-weight: bold; width: 62mm; }
            table.reparto { border-collapse: collapse; width: 100%; margin: 8pt 0 12pt 0; font-size: 10.5pt; }
            table.reparto th, table.reparto td { border: 0.6pt solid #000; padding: 3pt 5pt; } table.reparto th { background: #eee; }
            table.reparto td.n { text-align: right; white-space: nowrap; } table.reparto tr.t td { font-weight: bold; }
            .punto { font-weight: bold; margin: 8pt 0 2pt 0; }
            table.firmas { width: 100%; margin-top: 36pt; } table.firmas td { width: 50%; text-align: center; vertical-align: top; padding-top: 40pt; }
            </style></head><body>
            """);
        sb.Append($"<h1>ACTA DE JUNTA GENERAL Y UNIVERSAL DE SOCIOS DE LA COMPAÑÍA</h1><h2>\"{M(d.Sociedad)}\"</h2><div class=\"cif\">(CIF.: {H(FormatoCif(d.Cif))})</div>");
        sb.Append($"<p>A las {H(Hora(d.Hora))} horas del día {H(fecha)}, se reúnen en el domicilio social de la compañía, los socios propietarios del capital social de la compañía " +
            $"mercantil \"{M(d.Sociedad)}\", y al amparo de lo previsto en el artículo 178 de la vigente Ley de Sociedades de Capital, acuerdan por unanimidad constituirse en " +
            "JUNTA GENERAL Y UNIVERSAL DE SOCIOS, con la asistencia de los siguientes:</p>");
        sb.Append("<div class=\"titulo\">LISTA DE ASISTENTES</div><div class=\"seccion\">A.- SOCIOS ASISTENTES:</div>");
        foreach (var s in d.Socios)
        {
            sb.Append("<table class=\"socio\">");
            Fila(sb, "SOCIO", M(s.Nombre));
            if (d.ValorNominal is decimal vn && s.Participaciones is int np) Fila(sb, "CAPITAL SUSCRITO Y DESEMBOLSADO", $"{H(Entero(vn * np))} €");
            if (s.Participaciones is int p) Fila(sb, "NÚMERO DE PARTICIPACIONES", H(p.ToString("#,##0", Es)));
            if (!string.IsNullOrWhiteSpace(s.Numeracion))
                Fila(sb, "NUMERACIÓN DE LAS PARTICIPACIONES", string.Join("<br>", s.Numeracion.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(H)));
            Fila(sb, "PORCENTAJE DE CAPITAL", $"{H(s.Participacion.ToString("0.00", Es))} %");
            sb.Append("</table>");
        }
        sb.Append($"<div class=\"seccion\">B.- {M(cargo)}:</div>");
        sb.Append($"<p>{Comparecencia(d.Administrador)}</p>");
        sb.Append($"<p>Se designa para que actúen de presidente y secretario de esta Junta General y Universal de Socios a {M(d.Presidente)} y {M(d.Secretario)}, respectivamente, " +
            "quienes presentes en este acto aceptan en lo menester dichos cargos, indicando que no les afecta incompatibilidad alguna para ello ni en especial las derivadas de la Ley 5/2006 de 10 de abril.</p>");
        sb.Append("<p>A continuación, y, por unanimidad de todos los socios asistentes, se acepta el siguiente:</p>");
        sb.Append("<div class=\"titulo\">ORDEN DEL DÍA</div>");
        sb.Append($"<div class=\"punto\">PRIMERO</div><p>Reparto de dividendos con cargo a {H(d.Origen)}.</p>");
        sb.Append("<div class=\"punto\">SEGUNDO</div><p>Delegación de facultades para la elevación a público de los acuerdos adoptados.</p>");
        sb.Append("<p>El Presidente declara válidamente constituida la presente JUNTA GENERAL Y UNIVERSAL DE SOCIOS, por concurrir el CIEN POR CIEN (100%) del capital social y la " +
            "totalidad de los socios, todos con derecho a voto.</p>");
        sb.Append("<p>Abierta la sesión, el Presidente decide entrar seguidamente en la deliberación y votación de los temas comprendidos en el ORDEN DEL DÍA, y después de la debida " +
            "consideración y amplio debate, por UNANIMIDAD de todos los socios se toman los siguientes:</p>");
        sb.Append("<div class=\"titulo\">ACUERDOS</div>");
        sb.Append($"<div class=\"punto\">PRIMERO. – REPARTO DE DIVIDENDOS CON CARGO A {M(d.Origen)}.</div>");
        var r = d.PorcentajeRetencion.ToString("0.##", Es);
        sb.Append($"<p>Se aprueba por unanimidad realizar una distribución de dividendos de {H(N(d.Bruto))} euros ({H(ImporteEnLetras.Euros(d.Bruto))}) con cargo al saldo de " +
            $"{H(d.Origen)} de la sociedad. Esta distribución se realiza en la proporción que corresponde a cada socio, sujeta a retención del {H(r)}% en concepto de IRPF.</p>");
        sb.Append("<p>En resumen, la distribución queda de la siguiente forma:</p>");
        sb.Append($"<table class=\"reparto\"><tr><th>Socio</th><th>Grado particip.</th><th>Número</th><th>Valor nominal</th><th>Dividendos</th><th>Retención IRPF ({H(r)}%)</th><th>Neto a distribuir</th></tr>");
        foreach (var s in d.Socios)
            sb.Append($"<tr><td>{M(s.Nombre)}</td><td class=\"n\">{H(s.Participacion.ToString("0.##", Es))}%</td><td class=\"n\">{H(s.Participaciones?.ToString("#,##0", Es))}</td>" +
                $"<td class=\"n\">{H(d.ValorNominal is decimal v && s.Participaciones is int n ? Entero(v * n) : "")}</td><td class=\"n\">{H(N(s.Bruto))}</td>" +
                $"<td class=\"n\">{H(N(-s.Retencion))}</td><td class=\"n\">{H(N(s.Neto))}</td></tr>");
        var totalPart = d.Socios.Sum(s => s.Participaciones ?? 0);
        sb.Append($"<tr class=\"t\"><td>Total</td><td class=\"n\">{H(d.Socios.Sum(s => s.Participacion).ToString("0.##", Es))}%</td><td class=\"n\">{H(totalPart.ToString("#,##0", Es))}</td>" +
            $"<td class=\"n\">{H(d.ValorNominal is decimal vt ? Entero(vt * totalPart) : "")}</td><td class=\"n\">{H(N(d.Bruto))}</td><td class=\"n\">{H(N(-d.Retencion))}</td>" +
            $"<td class=\"n\">{H(N(d.Neto))}</td></tr></table>");
        sb.Append("<div class=\"punto\">SEGUNDO.- DELEGACIÓN DE FACULTADES PARA LA ELEVACIÓN A PÚBLICO DE LOS ACUERDOS ADOPTADOS</div>");
        sb.Append("<p>Puestos a votación los Acuerdos Transcritos, correspondientes a la totalidad de los asuntos del ORDEN DEL DÍA de esta Junta se aprueba el contenido de cada uno " +
            $"con el voto favorable de todos los asistentes y acuerdan expresamente facultar al {H(cargo)} de la compañía, {M(d.Administrador?.Nombre ?? d.Presidente)}, para que pueda " +
            "comparecer ante Notario y para elevar a público las decisiones de este acta que así lo requieran, otorgando una o más escrituras públicas comprensivas de las mismas, " +
            "incluso de subsanación y rectificación, quedando igualmente facultado para introducir en las mismas las modificaciones que, en su caso, exija la calificación verbal o " +
            "escrita de los Registradores Mercantiles, realizando al efecto cuantas gestiones fueren necesarias para lograr la inscripción de las mismas en los Registros públicos " +
            "correspondientes, en la medida en que ello fuera necesario. Asimismo, si fuere menester, facultarle para que proceda a la publicación de las presentes decisiones, con " +
            "el fin de dar cumplimiento a lo dispuesto en la legislación vigente.</p>");
        sb.Append("<p>Y no habiendo más asuntos que tratar, se levanta la sesión en el día de la fecha, y de ella la presente Acta que, leída, es aprobada por unanimidad, y firmada " +
            "por el Secretario con el Visto Bueno del Sr. Presidente de la Junta.</p>");
        sb.Append($"<table class=\"firmas\"><tr><td>VºBº EL PRESIDENTE<br><br><br><br>{M(d.Presidente)}</td><td>EL SECRETARIO<br><br><br><br>{M(d.Secretario)}</td></tr></table>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void Fila(StringBuilder sb, string etiqueta, string valorHtml) => sb.Append($"<tr><td class=\"e\">{H(etiqueta)}</td><td>{valorHtml}</td></tr>");

    private static string Comparecencia(AdministradorActa? a)
    {
        if (a is null) return "[Falta el administrador: márcalo en la ficha de su tercero, Roles › Empleado › «Administrador de la sociedad»]";
        var partes = new List<string> { M(a.Nombre) };
        if (!string.IsNullOrWhiteSpace(a.Nacionalidad)) partes.Add($"de nacionalidad {H(a.Nacionalidad.Trim())}");
        partes.Add("mayor de edad");
        if (!string.IsNullOrWhiteSpace(a.Profesion)) partes.Add(H(a.Profesion.Trim()));
        if (!string.IsNullOrWhiteSpace(a.EstadoCivil)) partes.Add(H(a.EstadoCivil.Trim()));
        if (!string.IsNullOrWhiteSpace(a.Poblacion)) partes.Add($"con vecindad en {H(a.Poblacion.Trim())}");
        var texto = string.Join(", ", partes);
        if (!string.IsNullOrWhiteSpace(a.Direccion)) texto += $", con domicilio en {H(a.Direccion.Trim())}";
        if (!string.IsNullOrWhiteSpace(a.Nif)) texto += $" y con DNI núm. {H(a.Nif.Trim())}";
        return texto + ".";
    }

    /// <summary>«B62344502» → «B-62344502», como en el acta.</summary>
    private static string FormatoCif(string cif)
    {
        var c = (cif ?? "").Trim().ToUpperInvariant();
        return c.Length == 9 && char.IsLetter(c[0]) && c[1] != '-' ? $"{c[0]}-{c[1..]}" : c;
    }
}

/// <summary>
/// Lee de la BD lo que necesita el acta de un reparto (contabilidad/procesos/aplicacion-resultado 2026-11-17): la empresa y su valor nominal,
/// los socios activos con sus participaciones, su numeración y su cargo en la junta, el administrador (rol Empleado marcado como administrador,
/// con sus datos personales) y el reparto. Presidente y secretario son los socios con ese cargo; si falta alguno, el administrador preside y otro
/// socio es secretario, con un aviso. Avisa también de lo que falta en las fichas para que el acta salga completa.
/// </summary>
public sealed class ServicioActaDividendos(IBaseDatos bd, ServicioDividendos dividendos)
{
    public async Task<Resultado<DatosActa>> DatosAsync(ContextoSociedadEjercicio ctx, int idReparto, string? hora)
    {
        var registro = $"Acta del reparto de dividendos {idReparto}";
        var reparto = (await dividendos.RepartosAsync(ctx)).FirstOrDefault(r => r.IdReparto == idReparto);
        if (reparto is null)
            return Resultado<DatosActa>.Fallo(new Error("Acta.SinReparto", "El reparto ya no existe.", registro, "Se ha deshecho desde otro puesto.", "Actualiza la lista (F5)."));
        return Resultado<DatosActa>.Correcto(await bd.EjecutarAsync(async s =>
        {
            var avisos = new List<string>();
            var esquema = s.LeerEsquema();
            var conNominal = esquema.ExisteColumna("Sociedades", "ValorNominalParticipacion");
            var soc = await s.ConsultarPrimeroAsync(
                $"SELECT Nombre, CIF{(conNominal ? ", ValorNominalParticipacion" : "")} FROM Sociedades WHERE IdSociedad = ?",
                f => (Nombre: (f.Texto("Nombre") ?? "").Trim(), Cif: (f.Texto("CIF") ?? "").Trim(), Nominal: conNominal ? f.DecimalNulo("ValorNominalParticipacion") : null), ctx.IdSociedad);
            if (soc.Nominal is null) avisos.Add("Falta el valor nominal de la participación (Parámetros y tablas › Empresas): el acta sale sin el capital de cada socio.");

            var socios = await s.ConsultarAsync(
                "SELECT so.IdSocio, t.Nombre, ss.Participacion, ss.Participaciones, ss.NumeracionParticipaciones, ss.CargoJunta " +
                "FROM (SociosSociedad AS ss INNER JOIN Socios AS so ON ss.IdSocio = so.IdSocio) INNER JOIN Terceros AS t ON so.IdTercero = t.IdTercero " +
                "WHERE ss.IdSociedad = ? AND ss.Activo = True ORDER BY t.Nombre",
                f => (Id: f.Entero("IdSocio"), Nombre: (f.Texto("Nombre") ?? "").Trim(), Pct: f.DecimalNulo("Participacion") ?? 0m, N: f.EnteroNulo("Participaciones"),
                    Num: f.Texto("NumeracionParticipaciones"), Cargo: f.Texto("CargoJunta")?.Trim().ToUpperInvariant()), ctx.IdSociedad);
            var lista = new List<SocioActa>();
            foreach (var x in socios)
            {
                var r = reparto.Socios.FirstOrDefault(g => g.IdSocio == x.Id);
                lista.Add(new SocioActa(x.Nombre, x.N, x.Num, r?.Participacion ?? x.Pct, r?.Bruto ?? 0m, r?.Retencion ?? 0m, r?.Neto ?? 0m, x.Cargo));
                if (x.N is null) avisos.Add($"{x.Nombre} no tiene número de participaciones (ficha › Roles › Socio).");
            }
            foreach (var g in reparto.Socios.Where(g => socios.All(x => x.Id != g.IdSocio)))
                lista.Add(new SocioActa(g.Nombre, null, null, g.Participacion ?? 0m, g.Bruto, g.Retencion, g.Neto, null));

            var admin = await s.ConsultarPrimeroAsync(
                "SELECT t.Nombre, t.NIF, t.Direccion, t.Poblacion, es.CargoAdministrador, em.Nacionalidad, em.Profesion, em.EstadoCivil " +
                "FROM (EmpleadosSociedad AS es INNER JOIN Empleados AS em ON es.IdEmpleado = em.IdEmpleado) INNER JOIN Terceros AS t ON em.IdTercero = t.IdTercero " +
                "WHERE es.IdSociedad = ? AND es.Activo = True AND es.Administrador = True ORDER BY t.Nombre",
                f => new AdministradorActa((f.Texto("Nombre") ?? "").Trim(), string.IsNullOrWhiteSpace(f.Texto("CargoAdministrador")) ? "Administrador único" : f.Texto("CargoAdministrador")!.Trim(),
                    f.Texto("Nacionalidad"), f.Texto("Profesion"), f.Texto("EstadoCivil"), f.Texto("Direccion"), f.Texto("Poblacion"), f.Texto("NIF")), ctx.IdSociedad);
            if (admin is null)
                avisos.Add("No hay administrador: marca «Administrador de la sociedad» en la ficha de su tercero (Roles › Empleado).");
            else if (string.IsNullOrWhiteSpace(admin.Nacionalidad) || string.IsNullOrWhiteSpace(admin.Profesion) || string.IsNullOrWhiteSpace(admin.EstadoCivil))
                avisos.Add($"Faltan datos del administrador ({admin.Nombre}): nacionalidad, profesión o estado civil (ficha › Roles › Empleado).");

            var presidente = lista.FirstOrDefault(x => x.CargoJunta == "PRESIDENTE")?.Nombre;
            var secretario = lista.FirstOrDefault(x => x.CargoJunta == "SECRETARIO")?.Nombre;
            if (presidente is null)
            {
                presidente = admin?.Nombre ?? lista.FirstOrDefault()?.Nombre ?? "";
                avisos.Add($"Ningún socio es presidente de la junta: preside {presidente}. Elígelo en la ficha del socio (Roles › Socio › Cargo en la junta).");
            }
            if (secretario is null)
            {
                var p = presidente;
                secretario = lista.FirstOrDefault(x => !string.Equals(x.Nombre, p, StringComparison.OrdinalIgnoreCase))?.Nombre ?? "";
                avisos.Add($"Ningún socio es secretario de la junta: actúa {secretario}. Elígelo en la ficha del socio (Roles › Socio › Cargo en la junta).");
            }
            var origen = await OrigenAsync(s, ctx, reparto.CuentaReservas);
            return new DatosActa(idReparto, soc.Nombre, soc.Cif, soc.Nominal, reparto.Fecha, string.IsNullOrWhiteSpace(hora) ? "11:00" : hora.Trim(), origen, reparto.PorcentajeRetencion,
                lista, admin, presidente, secretario, avisos);
        }));
    }

    /// <summary>
    /// Ruta del PDF: carpeta de documentos de la empresa (Sociedades.RutaDocumentos; relativa, junto a la BD) \ ejercicio \ «Actas» \
    /// «ActaDividendos<empresa><aaaammdd>.pdf».
    /// </summary>
    public async Task<string> RutaAsync(ContextoSociedadEjercicio ctx, DatosActa d)
    {
        var ruta = await bd.EjecutarAsync(s => s.EscalarAsync("SELECT RutaDocumentos FROM Sociedades WHERE IdSociedad = ?", ctx.IdSociedad));
        var raiz = Presentacion.ServicioPresentacionAeat.CarpetaEmpresa(bd.Ruta, ruta as string);
        return Path.Combine(raiz, d.Fecha.Year.ToString(CultureInfo.InvariantCulture), "Actas", ActaDividendos.NombreFichero(d.Sociedad, d.Fecha));
    }

    /// <summary>
    /// Guarda el PDF del acta por el gestor documental (documentos 2026-11-18, parte b) en <see cref="RutaAsync"/>, como documento de tipo «Acta»
    /// vinculado al ejercicio y a la empresa; si ya había un acta con ese nombre (la junta de ese día), la nueva es una versión suya. Devuelve la ruta
    /// del fichero guardado.
    /// </summary>
    public async Task<string> GuardarAsync(ContextoSociedadEjercicio ctx, DatosActa d, byte[] pdf, string operador)
    {
        var ruta = await RutaAsync(ctx, d);
        var anio = ctx.Ejercicio.ToString(CultureInfo.InvariantCulture);
        var r = await new ProConta.Documentos.ServicioDocumentos(bd, operador).GuardarAsync(new ProConta.Documentos.SolicitudDocumento(
            ctx.IdSociedad, "ACTA", $"Acta de la junta del reparto de dividendos {d.Fecha:dd-MM-yyyy}", Path.GetDirectoryName(ruta)!, Path.GetFileName(ruta), d.Fecha.Date,
            ctx.Ejercicio, "DIVIDENDOS",
            [new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, ctx.IdSociedadEjercicio, null, $"Ejercicio {anio}"),
             ProConta.Documentos.ServicioDocumentos.Empresa(ctx.IdSociedad, d.Sociedad)],
            Motivo: "Acta generada", VersionSiMismoNombre: true), pdf);
        if (!r.EsCorrecto) throw new IOException(r.Error!.TextoOperador());
        return r.Valor.RutaCompleta;
    }

    /// <summary>De dónde sale el reparto, en el texto del acta: el resultado del ejercicio anterior (129), reservas voluntarias (113) o el nombre de la cuenta.</summary>
    private static async Task<string> OrigenAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, string cuenta)
    {
        if (cuenta.StartsWith("129", StringComparison.Ordinal)) return $"resultado del ejercicio {ctx.Ejercicio - 1}";
        if (cuenta.StartsWith("113", StringComparison.Ordinal)) return "reservas voluntarias";
        var nombre = await s.EscalarAsync("SELECT Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", ctx.IdSociedadEjercicio, cuenta);
        return nombre is string n && n.Trim().Length > 0 ? n.Trim().ToLower(CultureInfo.GetCultureInfo("es-ES")) : $"la cuenta {cuenta}";
    }
}
