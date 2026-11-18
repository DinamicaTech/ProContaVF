using System.Globalization;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Un periodo de una declaración informativa en la pantalla de modelos (trimestre del 349 o el año «0A» de las anuales).</summary>
public sealed record PeriodoInformativa(string Periodo, DateTime Desde, DateTime Hasta, Estado303 Estado, int Registros, decimal Total, PresentacionGuardada? Ultima,
    IReadOnlyList<PresentacionGuardada> Presentaciones, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public bool Terminado => Estado != Estado303.EnCurso;
    public bool SePuedePresentar => Terminado && Bloqueos.Count == 0;
}

/// <summary>
/// Lecturas comunes de las informativas que salen de los libros de IVA (349 y 347): las líneas de IVA y el 303 del ejercicio, el país
/// del tercero de cada registro, la cuenta de IVA de las intracomunitarias de bienes, los registros que no van al 347 y el código postal
/// de cada tercero; y el estado de los periodos con lo presentado por TGVI Online.
/// </summary>
public abstract class ServicioInformativaIva(IBaseDatos bd, ServicioTgvi tgvi, IReloj? reloj)
{
    protected IBaseDatos Bd => bd;
    protected ServicioTgvi Tgvi => tgvi;
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly ServicioPresentacionAeat _directa = new(bd, new ClienteAeatHttp(), null, reloj);
    public DateTime Hoy => _reloj.Ahora.Date;

    protected async Task<(IReadOnlyList<LineaIva> Lineas, Resultado303 R303)> IvaAsync(ContextoSociedadEjercicio ctx, CancellationToken c)
    {
        var (lineas, _, _, iva) = await new LectorLineasIva(bd).CalcularAsync(ctx, c);
        return (lineas, iva.Modelo303);
    }

    protected Task<Declarante303> DeclaranteAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync("SELECT Nombre, CIF FROM Sociedades WHERE IdSociedad = ?",
            f => new Declarante303((f.Texto("CIF") ?? "").Trim().ToUpperInvariant(), (f.Texto("Nombre") ?? "").Trim(), null), ctx.IdSociedad) ?? new Declarante303("", "", null));

    /// <summary>Contacto del tipo 1: el teléfono de la sociedad y el nombre que se indique (si no, la razón social).</summary>
    protected Task<ContactoInformativa> ContactoAsync(ContextoSociedadEjercicio ctx, string? nombre) => bd.EjecutarAsync(async s =>
    {
        var conTelefono = s.LeerEsquema().ExisteColumna("Sociedades", "Telefono");
        var f = await s.ConsultarPrimeroAsync($"SELECT Nombre{(conTelefono ? ", Telefono" : "")} FROM Sociedades WHERE IdSociedad = ?",
            r => (Nombre: (r.Texto("Nombre") ?? "").Trim(), Telefono: conTelefono ? r.Texto("Telefono") : null), ctx.IdSociedad);
        return new ContactoInformativa(f.Telefono ?? "", string.IsNullOrWhiteSpace(nombre) ? f.Nombre : nombre);
    });

    /// <summary>País de cada asiento con registro de IVA (el del proveedor o el del cliente), para los NIF sin prefijo.</summary>
    protected Task<IReadOnlyDictionary<int, string>> PaisesAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyDictionary<int, string>>(async s =>
    {
        var sop = await s.ConsultarAsync("SELECT IdAsiento, PaisProveedor AS Pais FROM IVASoportado WHERE IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdAsiento"), Pais: (f.Texto("Pais") ?? "").Trim()), ctx.IdSociedadEjercicio);
        var rep = await s.ConsultarAsync("SELECT IdAsiento, PaisCliente AS Pais FROM IVARepercutido WHERE IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdAsiento"), Pais: (f.Texto("Pais") ?? "").Trim()), ctx.IdSociedadEjercicio);
        return sop.Concat(rep).Where(x => x.Pais.Length > 0).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Pais);
    });

    /// <summary>País de la UE de cada tercero con NIF comunitario en su ficha, por el número sin el prefijo (y por el NIF completo).</summary>
    protected Task<IReadOnlyDictionary<string, string>> PaisesTercerosAsync() => bd.EjecutarAsync<IReadOnlyDictionary<string, string>>(async s =>
    {
        var t = await s.ConsultarAsync("SELECT NIF, Pais FROM Terceros WHERE NIF IS NOT NULL", f => (Nif: RegistroInformativo.NormalizarNif(f.Texto("NIF")), Pais: (f.Texto("Pais") ?? "").Trim().ToUpperInvariant()));
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (nif, pais) in t)
        {
            var (p, numero) = Modelo349.NifComunitario(nif, pais);
            if (p is null || p == "ES" || !Modelo349.PaisesUe.Contains(p)) continue;
            r.TryAdd(numero, p);
            r.TryAdd(nif, p);
        }
        return r;
    });

    protected Task<IReadOnlySet<string>> CuentasBienesIntraAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlySet<string>>(async s =>
    {
        var cols = new[] { "CuentaIVAIntracomunitarioSoportadoBienes", "CuentaIVAIntracomunitarioRepercutidoBienes" }.Where(x => s.LeerEsquema().ExisteColumna("SociedadesEjercicios", x)).ToList();
        if (cols.Count == 0) return new HashSet<string>();
        var fila = await s.ConsultarPrimeroAsync($"SELECT {string.Join(", ", cols)} FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => cols.Select(x => f.Texto(x)?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList(), ctx.IdSociedadEjercicio);
        return new HashSet<string>(fila ?? [], StringComparer.Ordinal);
    });

    /// <summary>Estado de un periodo de una informativa con lo presentado desde el programa en el entorno de la BD.</summary>
    protected async Task<PeriodoInformativa> PeriodoAsync(ContextoSociedadEjercicio ctx, string modelo, string periodo, DateTime desde, DateTime hasta, int registros, decimal total,
        IEnumerable<string> bloqueos, IEnumerable<string> avisos, CancellationToken c)
    {
        var entorno = await tgvi.EntornoAsync(c);
        var del = (await _directa.PresentadasAsync(ctx.IdSociedad, modelo, ctx.Ejercicio, c))
            .Where(p => p.Periodo == periodo && p.Entorno == ServiciosAeat.Codigo(entorno)).OrderBy(p => p.FechaHora).ThenBy(p => p.Id).ToList();
        var ultima = del.LastOrDefault();
        var b = new List<string>();
        var a = new List<string>(avisos);
        if (Hoy < desde) b.Add($"El {modelo} {(periodo == "0A" ? "" : "del " + periodo + " ")}de {ctx.Ejercicio} se presenta del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}.");
        else b.AddRange(bloqueos);
        if (Hoy > hasta && ultima is null) a.Add($"El plazo terminó el {hasta:dd/MM/yyyy}: si no lo presentó la gestoría, preséntalo cuanto antes.");
        var estado = Hoy < desde ? Estado303.EnCurso
            : ultima is null ? Estado303.SinPresentar
            : ultima.Resultado != total ? Estado303.Desactualizado
            : ultima.ComplementariaDe is not null ? Estado303.Complementaria
            : Estado303.Presentado;
        if (estado == Estado303.Desactualizado)
            a.Add($"Se presentó con un total de {Euros(ultima!.Resultado)} y ahora da {Euros(total)}: si la diferencia es correcta, presenta una sustitutiva (o complementaria con lo que faltó).");
        return new PeriodoInformativa(periodo, desde, hasta, estado, registros, total, ultima, del, b, a);
    }

    protected async Task<PresentacionGuardada?> AnteriorAsync(ContextoSociedadEjercicio ctx, string modelo, string periodo, CancellationToken c)
    {
        var entorno = await tgvi.EntornoAsync(c);
        return (await _directa.PresentadasAsync(ctx.IdSociedad, modelo, ctx.Ejercicio, c))
            .Where(p => p.Periodo == periodo && p.Entorno == ServiciosAeat.Codigo(entorno)).OrderBy(p => p.FechaHora).ThenBy(p => p.Id).LastOrDefault();
    }

    /// <summary>Las informativas piden el teléfono de contacto (la AEAT rechaza el tipo 1 sin él, error 10703).</summary>
    protected static Error SinTelefono(string registro) => new("Informativa.SinTelefono", "Falta el teléfono de contacto de la sociedad.", registro,
        "Las declaraciones informativas llevan el teléfono de la persona con quien relacionarse y la AEAT las rechaza sin él.",
        "Escríbelo en Configuración › Parámetros y tablas › Empresas («Teléfono»).");

    protected static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}

/// <summary>
/// Modelo 349 de cada trimestre (contabilidad/modelos-tributarios/modelo-349) con el mismo contrato que los demás servicios de modelos:
/// estado de los trimestres, la declaración (<see cref="Modelo349"/>) y sus registros para TGVI Online (<see cref="ServicioTgvi"/>).
/// Plazo: el mes siguiente al trimestre (en el 4T, hasta el 30 de enero).
/// </summary>
public sealed class Servicio349(IBaseDatos bd, ServicioTgvi tgvi, IReloj? reloj = null) : ServicioInformativaIva(bd, tgvi, reloj)
{
    public const string Modelo = Modelo349.Modelo;

    public static (DateTime Desde, DateTime Hasta) Plazo(int ejercicio, int trimestre) => trimestre == 4
        ? (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 30))
        : (new DateTime(ejercicio, 3 * trimestre + 1, 1), new DateTime(ejercicio, 3 * trimestre + 1, 20));

    public async Task<Declaracion349> CalcularAsync(ContextoSociedadEjercicio ctx, int trimestre, CancellationToken c = default)
    {
        var (lineas, r303) = await IvaAsync(ctx, c);
        var paises = await PaisesAsync(ctx);
        var fichas = await PaisesTercerosAsync();
        var bienes = await CuentasBienesIntraAsync(ctx);
        // El país de la ficha del tercero (por el número de su NIF comunitario) antes que el del libro de IVA, que en los datos migrados a veces es «ES».
        return Modelo349.Calcular(lineas, r303, ctx.Ejercicio, trimestre, await DeclaranteAsync(ctx), l => l.CuentaIva is { } cta && bienes.Contains(cta.Trim()),
            l => fichas.GetValueOrDefault(RegistroInformativo.NormalizarNif(l.Nif)) ?? paises.GetValueOrDefault(l.IdAsiento));
    }

    public async Task<IReadOnlyList<PeriodoInformativa>> TrimestresAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var r = new List<PeriodoInformativa>();
        for (var t = 1; t <= 4; t++)
        {
            var d = await CalcularAsync(ctx, t, c);
            var (desde, hasta) = Plazo(ctx.Ejercicio, t);
            var avisos = new List<string>(d.Avisos);
            if (d.Operadores.Count == 0) avisos.Add($"El {t}T no tiene operaciones intracomunitarias: no hay que presentar el 349.");
            r.Add(await PeriodoAsync(ctx, Modelo, d.Periodo, desde, hasta, d.Operadores.Count, d.Total, d.Errores, avisos, c));
        }
        return r;
    }

    /// <summary>La declaración del trimestre lista para TGVI Online; con <paramref name="sustitutiva"/>, sustituye a la última presentada desde el programa.</summary>
    public async Task<Resultado<(Declaracion349 Declaracion, DeclaracionInformativa Tgvi)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, int trimestre, bool sustitutiva,
        string? contacto = null, CancellationToken c = default)
    {
        var registro = $"Modelo 349 {trimestre}T {ctx.Ejercicio}";
        var d = await CalcularAsync(ctx, trimestre, c);
        if (d.Operadores.Count == 0)
            return Resultado<(Declaracion349, DeclaracionInformativa)>.Fallo(new Error("Modelo349.Vacio", $"El {trimestre}T no tiene operaciones intracomunitarias.", registro, null, "No hay que presentar el 349."));
        if (!d.Valida)
            return Resultado<(Declaracion349, DeclaracionInformativa)>.Fallo(new Error("Modelo349.Errores", $"El 349 del {trimestre}T tiene errores que impiden presentarlo.", registro,
                string.Join(" ", d.Errores), "Corrige lo que se indica y vuelve a intentarlo."));
        PresentacionGuardada? anterior = null;
        if (sustitutiva && (anterior = await AnteriorAsync(ctx, Modelo, d.Periodo, c)) is null)
            return Resultado<(Declaracion349, DeclaracionInformativa)>.Fallo(new Error("Modelo349.SinAnterior", $"No consta presentado el 349 del {trimestre}T desde el programa.", registro));
        var persona = await ContactoAsync(ctx, contacto);
        if (persona.TelefonoNumerico == 0)
            return Resultado<(Declaracion349, DeclaracionInformativa)>.Fallo(SinTelefono(registro));
        var numero = await Tgvi.NumeroJustificanteAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, d.Periodo, c);
        var (t1, t2) = Modelo349.Registros(d, persona, numero, anterior?.Justificante, sustitutiva);
        var resumen = new List<(string, string)> { ("Operadores intracomunitarios", d.Operadores.Count.ToString(CultureInfo.InvariantCulture)), ("Importe de las operaciones", Euros(d.Total)) };
        resumen.AddRange(d.Operadores.Select(o => ($"{o.Pais}{o.Numero} {o.Nombre} ({o.Clave})", Euros(o.Base))));
        return Resultado<(Declaracion349, DeclaracionInformativa)>.Correcto((d, new DeclaracionInformativa(Modelo, ctx.Ejercicio, d.Periodo, d.Declarante.Nif, d.Declarante.RazonSocial,
            t1, t2, numero, d.Total, resumen, anterior?.Justificante)));
    }
}

/// <summary>
/// Modelo 347 de cada ejercicio (contabilidad/modelos-tributarios/modelo-347): el estado del año, la declaración (<see cref="Modelo347"/>)
/// y sus registros para TGVI Online. Plazo: febrero del año siguiente. No van las facturas de compra con retención ni los registros del
/// libro marcados «no incluir en el 347».
/// </summary>
public sealed class Servicio347(IBaseDatos bd, ServicioTgvi tgvi, IReloj? reloj = null) : ServicioInformativaIva(bd, tgvi, reloj)
{
    public const string Modelo = Modelo347.Modelo;

    public static (DateTime Desde, DateTime Hasta) Plazo(int ejercicio) => (new DateTime(ejercicio + 1, 2, 1), new DateTime(ejercicio + 1, 3, 2).AddDays(-1));

    public async Task<Declaracion347> CalcularAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var (lineas, _) = await IvaAsync(ctx, c);
        var (excluidos, cps) = await Bd.EjecutarAsync(async s =>
        {
            var esquema = s.LeerEsquema();
            var ex = new HashSet<int>();
            if (esquema.ExisteColumna("IVASoportado", "IncluidoModelo347"))
            {
                ex.UnionWith(await s.ConsultarAsync("SELECT IdAsiento FROM IVASoportado WHERE IdSociedadEjercicio = ? AND IncluidoModelo347 = False", f => f.Entero("IdAsiento"), ctx.IdSociedadEjercicio));
                ex.UnionWith(await s.ConsultarAsync("SELECT IdAsiento FROM IVARepercutido WHERE IdSociedadEjercicio = ? AND IncluidoModelo347 = False", f => f.Entero("IdAsiento"), ctx.IdSociedadEjercicio));
            }
            if (esquema.ExisteColumna("FacturasCompra", "IdAsientoContable"))
                ex.UnionWith(await s.ConsultarAsync(
                    "SELECT IdAsientoContable FROM FacturasCompra WHERE IdSociedadEjercicio = ? AND IdAsientoContable IS NOT NULL AND (ImporteRetencion <> 0 OR PorcentajeRetencion <> 0)",
                    f => f.Entero("IdAsientoContable"), ctx.IdSociedadEjercicio));
            var t = await s.ConsultarAsync("SELECT NIF, CodigoPostal FROM Terceros WHERE NIF IS NOT NULL", f => (Nif: Modelo347.Nif(f.Texto("NIF")), Cp: f.Texto("CodigoPostal")?.Trim()));
            return (ex, t.Where(x => !string.IsNullOrEmpty(x.Cp)).GroupBy(x => x.Nif).ToDictionary(g => g.Key, g => g.First().Cp!));
        }, c);
        return Modelo347.Calcular(lineas, ctx.Ejercicio, await DeclaranteAsync(ctx), l => excluidos.Contains(l.IdAsiento), nif => cps.GetValueOrDefault(nif));
    }

    public async Task<PeriodoInformativa> EjercicioAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var d = await CalcularAsync(ctx, c);
        var (desde, hasta) = Plazo(ctx.Ejercicio);
        return await PeriodoAsync(ctx, Modelo, "0A", desde, hasta, d.Declarados.Count, d.Total, d.Errores, d.Avisos, c);
    }

    public async Task<Resultado<(Declaracion347 Declaracion, DeclaracionInformativa Tgvi)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, bool sustitutiva, string? contacto = null,
        CancellationToken c = default)
    {
        var registro = $"Modelo 347 {ctx.Ejercicio}";
        var d = await CalcularAsync(ctx, c);
        if (d.Declarados.Count == 0)
            return Resultado<(Declaracion347, DeclaracionInformativa)>.Fallo(new Error("Modelo347.Vacio", "Ningún tercero pasa de 3.005,06 € en el año.", registro, null, "No hay que presentar el 347."));
        if (!d.Valida)
            return Resultado<(Declaracion347, DeclaracionInformativa)>.Fallo(new Error("Modelo347.Errores", $"El 347 de {ctx.Ejercicio} tiene errores que impiden presentarlo.", registro,
                string.Join(" ", d.Errores), "Corrige lo que se indica y vuelve a intentarlo."));
        PresentacionGuardada? anterior = null;
        if (sustitutiva && (anterior = await AnteriorAsync(ctx, Modelo, "0A", c)) is null)
            return Resultado<(Declaracion347, DeclaracionInformativa)>.Fallo(new Error("Modelo347.SinAnterior", $"No consta presentado el 347 de {ctx.Ejercicio} desde el programa.", registro));
        var persona = await ContactoAsync(ctx, contacto);
        if (persona.TelefonoNumerico == 0)
            return Resultado<(Declaracion347, DeclaracionInformativa)>.Fallo(SinTelefono(registro));
        var numero = await Tgvi.NumeroJustificanteAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, "0A", c);
        var (t1, t2) = Modelo347.Registros(d, persona, numero, anterior?.Justificante, sustitutiva);
        var resumen = new List<(string, string)>
        {
            ("Declarados", d.Declarados.Count.ToString(CultureInfo.InvariantCulture)),
            ("Compras (A)", Euros(d.Declarados.Where(x => x.Clave == 'A').Sum(x => x.Total))),
            ("Ventas (B)", Euros(d.Declarados.Where(x => x.Clave == 'B').Sum(x => x.Total))),
        };
        return Resultado<(Declaracion347, DeclaracionInformativa)>.Correcto((d, new DeclaracionInformativa(Modelo, ctx.Ejercicio, "0A", d.Declarante.Nif, d.Declarante.RazonSocial,
            t1, t2, numero, d.Total, resumen, anterior?.Justificante)));
    }
}
