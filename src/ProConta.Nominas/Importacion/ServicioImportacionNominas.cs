using System.Globalization;
using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Nominas.Lectura;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Roles;

namespace ProConta.Nominas.Importacion;

/// <summary>Qué pasa (o pasaría) al importar un fichero de nóminas.</summary>
public enum VeredictoNominas
{
    /// <summary>Se importa (o se ha importado).</summary>
    Importable,
    /// <summary>Este mismo fichero ya está importado: no se hace nada.</summary>
    YaImportado,
    /// <summary>Ya hay una importación vigente del mismo periodo y tipo de paga con otro contenido: hay que reemplazarla.</summary>
    OtraVersion,
    /// <summary>Hay columnas sin alias: hay que decir a qué campo corresponde cada una (o ignorarla).</summary>
    CabecerasSinAlias,
    /// <summary>El NIF de la empresa del fichero no es el de la sociedad de trabajo.</summary>
    OtraSociedad,
    /// <summary>La fecha de la nómina no es del ejercicio de trabajo.</summary>
    OtroEjercicio,
    /// <summary>El fichero no se puede leer o no cuadra con su total.</summary>
    Invalido,
}

/// <summary>Origen de una importación.</summary>
public enum OrigenNominas { Fichero, Correo, Migracion }

/// <summary>Alias que el operador decide para una cabecera sin alias: el campo, o null para ignorar la columna.</summary>
public sealed record AliasNuevo(string Cabecera, string? Campo);

/// <summary>Trabajador del fichero y cómo se ha reconocido.</summary>
public sealed record TrabajadorFichero(FilaNomina Fila, ResolucionTrabajador Resolucion);

/// <summary>Análisis de un fichero antes de importarlo.</summary>
public sealed record AnalisisNominas(VeredictoNominas Veredicto, FicheroNominas Fichero, string Hash, int? IdImportacionExistente,
    IReadOnlyList<TrabajadorFichero> Trabajadores, string? Detalle = null)
{
    public IReadOnlyList<TrabajadorFichero> SinResolver => Trabajadores.Where(t => !t.Resolucion.Resuelto).ToList();

    /// <summary>Trabajadores reconocidos que están de baja en la sociedad (rol Empleado desactivado): se importan, con aviso.</summary>
    public IReadOnlyList<string> DeBaja { get; init; } = [];

    /// <summary>Aviso de los trabajadores de baja, o vacío.</summary>
    public string AvisoDeBaja => DeBaja.Count == 0 ? "" :
        $" Aviso: {string.Join(", ", DeBaja)} {(DeBaja.Count == 1 ? "está" : "están")} de baja en la sociedad; comprueba que sea un finiquito o un atraso (si vuelve a trabajar, reactiva su rol Empleado en la ficha).";

    /// <summary>Explicación para el operador: qué pasa y qué hacer.</summary>
    public string Explicacion => Veredicto switch
    {
        VeredictoNominas.Importable => $"{Describir(Fichero)}: {Fichero.Filas.Count} nómina(s) listas para importar" +
            (SinResolver.Count > 0 ? $"; {SinResolver.Count} trabajador(es) sin reconocer, que habrá que resolver antes de contabilizar." : ".") +
            (Fichero.Advertencias.Count > 0 ? " Advertencias: " + string.Join(" ", Fichero.Advertencias) : "") + AvisoDeBaja,
        VeredictoNominas.YaImportado => $"Este fichero ya está importado (importación {IdImportacionExistente}): no se ha hecho nada.",
        VeredictoNominas.CabecerasSinAlias => "No sé qué es " + string.Join(", ", Fichero.SinAlias.Select(c => $"«{c.Cabecera}»" + (c.Ejemplo is null ? "" : $" (p. ej. {c.Ejemplo})"))) +
            ". Di a qué campo corresponde cada columna, o que se ignore; se recordará para la sociedad.",
        _ => Detalle ?? ("El fichero no se puede importar: " + string.Join(" ", Fichero.Errores.Take(10))),
    };

    public static string Describir(FicheroNominas f) =>
        (f.Mes is DateTime m ? m.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-ES")) : "Periodo desconocido") +
        (f.TipoPaga == TipoPaga.Extra ? " (paga extra)" : " (paga mensual)");
}

/// <summary>Resultado de una importación.</summary>
public sealed record ResultadoImportacionNominas(VeredictoNominas Veredicto, int? IdImportacion, AnalisisNominas Analisis, string? Estado)
{
    public bool Hecho => Veredicto is VeredictoNominas.Importable or VeredictoNominas.YaImportado;

    public string Explicacion => Veredicto == VeredictoNominas.Importable
        ? $"Importadas {Analisis.Fichero.Filas.Count} nómina(s) de {AnalisisNominas.Describir(Analisis.Fichero)} (importación {IdImportacion})." +
          (Estado == ServicioImportacionNominas.EstadoIncidencias ? $" Quedan {Analisis.SinResolver.Count} trabajador(es) por resolver antes de contabilizar." : "") +
          Analisis.AvisoDeBaja
        : Analisis.Explicacion;
}

/// <summary>
/// Importación de los Excel de nóminas de la gestoría (nominas 2026-11-13, RF-NOM-01 y 02). Lee el fichero con los alias de la
/// sociedad, comprueba que es de la sociedad (NIF) y del ejercicio, que no está ya importado (SHA-256) y que no hay otra versión
/// vigente del mismo periodo y paga; reconoce a cada trabajador por su código o NIF de empleado y guarda la importación y sus
/// nóminas en una transacción. Los trabajadores sin reconocer dejan la importación «con incidencias» hasta resolverlos. No
/// contabiliza: eso lo hace la plantilla de sistema «Nómina» (siguiente entrega).
/// </summary>
public sealed class ServicioImportacionNominas(IBaseDatos bd, IReloj? reloj = null)
{
    public const string EstadoPendiente = "PENDIENTE", EstadoIncidencias = "INCIDENCIAS", EstadoContabilizada = "CONTABILIZADA",
        EstadoReemplazada = "REEMPLAZADA", EstadoDeshecha = "DESHECHA";

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>
    /// Huella del contenido de la hoja, no de los bytes del fichero: el mismo Excel guardado otra vez (con otra fecha
    /// de guardado o en otro formato) da la misma huella y sale como «ya importado». Si no se puede leer, la de los bytes.
    /// </summary>
    public static string Huella(byte[] contenido)
    {
        var (filas, error) = LectorExcelNominas.LeerCeldas(contenido);
        if (error is not null) return Convert.ToHexString(SHA256.HashData(contenido));
        var sb = new System.Text.StringBuilder();
        var lineas = filas.Select(f =>
        {
            var celdas = f.Select(Celda).ToList();
            while (celdas.Count > 0 && celdas[^1].Length == 0) celdas.RemoveAt(celdas.Count - 1);
            return string.Join("\u001F", celdas);
        }).ToList();
        while (lineas.Count > 0 && lineas[^1].Length == 0) lineas.RemoveAt(lineas.Count - 1);
        foreach (var l in lineas) sb.Append(l).Append('\u001E');
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));

        static string Celda(object? v) => v switch
        {
            null => "",
            string t => t.Trim(),
            DateTime d => d.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            double n => Math.Round((decimal)n, 6).ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => v.ToString()?.Trim() ?? "",
        };
    }

    /// <summary>Lee el fichero y dice qué pasaría al importarlo, sin escribir nada.</summary>
    public Task<AnalisisNominas> AnalizarAsync(ContextoSociedadEjercicio ctx, byte[] contenido, IReadOnlyList<AliasNuevo>? alias = null) =>
        bd.EjecutarAsync(s => EvaluarAsync(s, ctx, contenido, alias));

    /// <summary>
    /// Importa el fichero. <paramref name="alias"/>: lo que el operador ha decidido para las cabeceras sin alias (se guardan para
    /// la sociedad en la misma transacción). <paramref name="reemplazar"/>: la importación vigente del mismo periodo y paga a la
    /// que sustituye, si el operador lo ha confirmado; solo si aún no está contabilizada (la contabilizada se reemplaza desde la
    /// contabilización, que anula sus asientos).
    /// </summary>
    public Task<Resultado<ResultadoImportacionNominas>> ImportarAsync(ContextoSociedadEjercicio ctx, byte[] contenido, string nombreFichero, OrigenNominas origen,
        Actor actor, IReadOnlyList<AliasNuevo>? alias = null, int? reemplazar = null, int? idAdjuntoRecibido = null, string? motivo = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            foreach (var a in alias ?? [])
            {
                var error = await GuardarAliasAsync(s, ctx.IdSociedad, a);
                if (error is not null) return Resultado<ResultadoImportacionNominas>.Fallo(error);
            }
            var analisis = await EvaluarAsync(s, ctx, contenido, null);
            if (analisis.Veredicto == VeredictoNominas.OtraVersion && reemplazar is int sustituida && sustituida == analisis.IdImportacionExistente)
            {
                var estado = await s.ConsultarPrimeroAsync("SELECT Estado FROM ImportacionesNomina WHERE IdImportacionNomina = ?", f => f.Texto("Estado"), sustituida);
                if (estado == EstadoContabilizada)
                    return Resultado<ResultadoImportacionNominas>.Fallo(new Error("Nominas.ReemplazarContabilizada",
                        "La versión anterior ya está contabilizada: no se puede reemplazar solo con la importación.", $"Importación {sustituida}",
                        "Sus asientos tienen que anularse a la vez que se contabiliza la nueva versión.", "Usa «Reemplazar con otro fichero…» en la pantalla de nóminas."));
                if (string.IsNullOrWhiteSpace(motivo))
                    return Resultado<ResultadoImportacionNominas>.Fallo(new Error("Nominas.MotivoObligatorio", "Para reemplazar una importación hay que explicar el motivo.",
                        $"Importación {sustituida}", null, "Escribe el motivo (p. ej. «la gestoría corrige la baja de …») y vuelve a importar."));
                await s.EjecutarAsync("UPDATE ImportacionesNomina SET Estado = ?, FechaHoraEstado = ?, OperadorEstado = ?, MotivoEstado = ? WHERE IdImportacionNomina = ?",
                    EstadoReemplazada, Ahora(), Recortar(actor.ToString(), 100), motivo.Trim(), sustituida);
                analisis = analisis with { Veredicto = VeredictoNominas.Importable };
            }
            else reemplazar = null;
            if (analisis.Veredicto != VeredictoNominas.Importable)
                return Resultado<ResultadoImportacionNominas>.Correcto(new(analisis.Veredicto, analisis.IdImportacionExistente, analisis, null));
            var (id, estadoNuevo) = await EscribirAsync(s, ctx, contenido, nombreFichero, origen, actor, analisis, reemplazar, idAdjuntoRecibido);
            return Resultado<ResultadoImportacionNominas>.Correcto(new(VeredictoNominas.Importable, id, analisis, estadoNuevo));
        });

    /// <summary>
    /// Asigna el empleado a las nóminas de un trabajador sin reconocer de la importación (las de ese código, o la de ese ordinal si
    /// el fichero no trae código) y pone la importación «pendiente» si ya no queda ninguno.
    /// </summary>
    public Task<Resultado> ResolverTrabajadorAsync(ContextoSociedadEjercicio ctx, int idImportacion, int ordinal, int idEmpleado) =>
        bd.EnTransaccionAsync(async s =>
        {
            var registro = $"Importación {idImportacion}";
            var imp = await s.ConsultarPrimeroAsync("SELECT IdSociedadEjercicio, Estado FROM ImportacionesNomina WHERE IdImportacionNomina = ?",
                f => (Ejercicio: (int?)f.Entero("IdSociedadEjercicio"), Estado: f.Texto("Estado")), idImportacion);
            if (imp.Ejercicio != ctx.IdSociedadEjercicio)
                return Resultado.Fallo(new Error("Nominas.ImportacionInexistente", "La importación no existe en la sociedad y el ejercicio de trabajo.", registro, null,
                    "Actualiza la pantalla (F5) o cambia a la sociedad y el ejercicio de la importación."));
            if (imp.Estado is EstadoContabilizada or EstadoReemplazada or EstadoDeshecha)
                return Resultado.Fallo(new Error("Nominas.ImportacionCerrada", $"La importación está {imp.Estado!.ToLowerInvariant()}: sus trabajadores ya no se cambian.", registro));
            var empleadoAqui = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM EmpleadosSociedad WHERE IdEmpleado = ? AND IdSociedad = ?", idEmpleado, ctx.IdSociedad),
                CultureInfo.InvariantCulture);
            if (empleadoAqui == 0)
                return Resultado.Fallo(new Error("Nominas.NoEsEmpleado", "Ese tercero no es empleado de la sociedad.", registro, null,
                    "Dale antes el rol Empleado en la sociedad (o usa «Alta como empleado»)."));
            var codigo = await s.ConsultarPrimeroAsync("SELECT CodigoTrabajador FROM Nominas WHERE IdImportacionNomina = ? AND Ordinal = ?", f => f.Texto("CodigoTrabajador"),
                idImportacion, ordinal);
            if (codigo is null)
                await s.EjecutarAsync("UPDATE Nominas SET IdEmpleado = ? WHERE IdImportacionNomina = ? AND Ordinal = ?", idEmpleado, idImportacion, ordinal);
            else
                await s.EjecutarAsync("UPDATE Nominas SET IdEmpleado = ? WHERE IdImportacionNomina = ? AND CodigoTrabajador = ?", idEmpleado, idImportacion, codigo);
            await RecalcularEstadoAsync(s, idImportacion);
            return Resultado.Correcto();
        });

    /// <summary>Alias guardados de la sociedad: cabecera normalizada → campo (null = ignorar).</summary>
    public static async Task<Dictionary<string, string?>> AliasAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync("SELECT Cabecera, Campo FROM AliasCamposNomina WHERE IdSociedad = ?", f => (C: f.Texto("Cabecera") ?? "", V: f.Texto("Campo")), idSociedad))
        .GroupBy(a => CamposNomina.NormalizarCabecera(a.C)).ToDictionary(g => g.Key, g => g.First().V);

    // ---------- evaluación ----------

    private static async Task<AnalisisNominas> EvaluarAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, byte[] contenido, IReadOnlyList<AliasNuevo>? alias)
    {
        var guardados = await AliasAsync(s, ctx.IdSociedad);
        foreach (var a in alias ?? []) guardados[CamposNomina.NormalizarCabecera(a.Cabecera)] = a.Campo;
        var fichero = LectorExcelNominas.Leer(contenido, guardados);
        var hash = Huella(contenido);
        AnalisisNominas A(VeredictoNominas v, int? existente = null, string? detalle = null, IReadOnlyList<TrabajadorFichero>? t = null) =>
            new(v, fichero, hash, existente, t ?? [], detalle);

        var ya = await s.ConsultarPrimeroAsync("SELECT IdImportacionNomina FROM ImportacionesNomina WHERE IdSociedadEjercicio = ? AND Huella = ?",
            f => (int?)f.Entero("IdImportacionNomina"), ctx.IdSociedadEjercicio, hash);
        if (ya is int idYa) return A(VeredictoNominas.YaImportado, idYa);
        if (fichero.Errores.Count > 0 || fichero.Filas.Count == 0) return A(VeredictoNominas.Invalido);
        if (fichero.SinAlias.Count > 0) return A(VeredictoNominas.CabecerasSinAlias);

        if (fichero.NifEmpresa is { Length: > 0 } nifFichero)
        {
            var sociedades = await s.ConsultarAsync("SELECT IdSociedad, Nombre, CIF FROM Sociedades",
                f => (Id: f.Texto("IdSociedad")?.Trim() ?? "", Nombre: f.Texto("Nombre"), Nif: Nif.Normalizar(f.Texto("CIF") ?? "")));
            var propia = sociedades.FirstOrDefault(x => string.Equals(x.Id, ctx.IdSociedad.Trim(), StringComparison.OrdinalIgnoreCase));
            var nif = Nif.Normalizar(nifFichero);
            if (propia.Nif.Length > 0 && propia.Nif != nif)
            {
                var duena = sociedades.FirstOrDefault(x => x.Nif == nif);
                return A(VeredictoNominas.OtraSociedad, detalle: $"El fichero es de la empresa con NIF {nif} ({fichero.NombreEmpresa}), no de {propia.Nombre} (NIF {propia.Nif}). " +
                    (duena.Id is { Length: > 0 } ? $"Cambia a la sociedad {duena.Id} ({duena.Nombre}) y vuelve a importarlo." : "Comprueba que la gestoría ha enviado el fichero correcto."));
            }
        }
        if (fichero.FechaNomina is DateTime fecha && fecha.Year != ctx.Ejercicio)
            return A(VeredictoNominas.OtroEjercicio, detalle: $"Las nóminas son del {fecha:dd/MM/yyyy} y el ejercicio de trabajo es {ctx.Ejercicio}. " +
                $"Cambia al ejercicio {fecha.Year} y vuelve a importarlo.");

        var trabajadores = new List<TrabajadorFichero>();
        foreach (var f in fichero.Filas)
            trabajadores.Add(new(f, await ServicioEmpleados.BuscarAsync(s, ctx.IdSociedad, f.CodigoTrabajador, f.Nif, f.Nombre)));
        var deBaja = new List<string>();
        foreach (var t in trabajadores.Where(t => t.Resolucion.IdEmpleado is not null))
            if (await s.ConsultarPrimeroAsync("SELECT Activo FROM EmpleadosSociedad WHERE IdEmpleado = ? AND IdSociedad = ?", f => (bool?)f.Logico("Activo"),
                    t.Resolucion.IdEmpleado, ctx.IdSociedad) == false)
                deBaja.Add(t.Fila.Nombre);

        if (fichero.Mes is DateTime mes)
        {
            var vigente = await s.ConsultarPrimeroAsync(
                "SELECT IdImportacionNomina, Estado, NombreFichero FROM ImportacionesNomina WHERE IdSociedadEjercicio = ? AND TipoPaga = ? AND PeriodoDesde >= ? AND PeriodoDesde < ? " +
                "AND Estado NOT IN (?, ?) ORDER BY IdImportacionNomina DESC",
                f => (Id: (int?)f.Entero("IdImportacionNomina"), Estado: f.Texto("Estado"), Nombre: f.Texto("NombreFichero")),
                ctx.IdSociedadEjercicio, TextoTipo(fichero.TipoPaga), mes, mes.AddMonths(1), EstadoReemplazada, EstadoDeshecha);
            if (vigente.Id is int otra)
                return A(VeredictoNominas.OtraVersion, otra, $"Ya hay nóminas de {AnalisisNominas.Describir(fichero)} importadas de «{vigente.Nombre}» (importación {otra}) " +
                    "y este fichero trae otro contenido. Si es una corrección de la gestoría, reemplaza la versión anterior; si no, no hace falta importarlo.", trabajadores);
        }
        return A(VeredictoNominas.Importable, t: trabajadores) with { DeBaja = deBaja };
    }

    private async Task<(int Id, string Estado)> EscribirAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, byte[] contenido, string nombreFichero, OrigenNominas origen,
        Actor actor, AnalisisNominas a, int? sustituida, int? idAdjunto)
    {
        var f = a.Fichero;
        var incidencias = Incidencias(a.Trabajadores.Where(t => !t.Resolucion.Resuelto).Select(t => t.Fila), f.Advertencias);
        var estado = a.SinResolver.Count > 0 ? EstadoIncidencias : EstadoPendiente;
        await s.EjecutarAsync(
            "INSERT INTO ImportacionesNomina (IdSociedadEjercicio, NombreFichero, Huella, Origen, IdAdjuntoRecibido, CodigoEmpresaGestoria, NifEmpresa, TextoPaga, TipoPaga, " +
            "PeriodoDesde, PeriodoHasta, FechaNomina, Estado, IdImportacionSustituida, Incidencias, FechaHoraImportacion, Operador) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ctx.IdSociedadEjercicio, Recortar(Path.GetFileName(nombreFichero), 255), a.Hash, origen.ToString().ToUpperInvariant(), idAdjunto,
            Recortar(f.CodigoEmpresa, 20), Recortar(f.NifEmpresa, 20), Recortar(f.TextoPaga, 100), TextoTipo(f.TipoPaga),
            f.PeriodoDesde, f.PeriodoHasta, f.FechaNomina ?? f.PeriodoHasta, estado, sustituida, incidencias, Ahora(), Recortar(actor.ToString(), 100));
        var id = await s.UltimoIdAsync();
        var importes = CamposNomina.Importes.Select(c => c.Nombre).ToList();
        var sql = "INSERT INTO Nominas (IdImportacionNomina, Ordinal, FilaOrigen, IdEmpleado, CodigoTrabajador, NombreOrigen, NifOrigen, TipoPaga, FechaNomina, " +
            string.Join(", ", importes) + ") VALUES (" + string.Join(", ", Enumerable.Repeat("?", 9 + importes.Count)) + ")";
        foreach (var t in a.Trabajadores)
        {
            var fila = t.Fila;
            object?[] valores = [id, fila.Ordinal, fila.Fila, t.Resolucion.IdEmpleado, Recortar(fila.CodigoTrabajador, 30), Recortar(fila.Nombre, 150),
                Recortar(fila.Nif, 20), TextoTipo(fila.TipoPaga), fila.FechaNomina, .. importes.Select(c => fila.Importes.TryGetValue(c, out var v) ? (object?)v : null)];
            await s.EjecutarAsync(sql, valores);
        }
        return (id, estado);
    }

    private static async Task RecalcularEstadoAsync(ISesionDatos s, int idImportacion)
    {
        var sinResolver = await s.ConsultarAsync("SELECT Ordinal, FilaOrigen, CodigoTrabajador, NombreOrigen FROM Nominas WHERE IdImportacionNomina = ? AND IdEmpleado IS NULL ORDER BY Ordinal",
            f => (Fila: f.EnteroNulo("FilaOrigen"), Codigo: f.Texto("CodigoTrabajador"), Nombre: f.Texto("NombreOrigen") ?? ""), idImportacion);
        var texto = sinResolver.Count == 0 ? null : string.Join("\n", sinResolver.Select(x => TextoSinResolver(x.Nombre, x.Codigo)));
        await s.EjecutarAsync("UPDATE ImportacionesNomina SET Estado = ?, Incidencias = ? WHERE IdImportacionNomina = ? AND Estado IN (?, ?)",
            sinResolver.Count == 0 ? EstadoPendiente : EstadoIncidencias, texto, idImportacion, EstadoPendiente, EstadoIncidencias);
    }

    private static string? Incidencias(IEnumerable<FilaNomina> sinResolver, IEnumerable<IncidenciaNominas> advertencias)
    {
        var lineas = sinResolver.Select(f => TextoSinResolver(f.Nombre, f.CodigoTrabajador)).Concat(advertencias.Select(a => a.ToString())).ToList();
        return lineas.Count == 0 ? null : string.Join("\n", lineas);
    }

    private static string TextoSinResolver(string nombre, string? codigo) =>
        $"Trabajador sin resolver: «{nombre}»" + (codigo is null ? "" : $" (código {codigo})") + ". Asígnalo a un empleado o dalo de alta.";

    private static async Task<Error?> GuardarAliasAsync(ISesionDatos s, string idSociedad, AliasNuevo a)
    {
        var cabecera = CamposNomina.NormalizarCabecera(a.Cabecera);
        if (cabecera.Length == 0) return null;
        string? campo = null;
        if (!string.IsNullOrWhiteSpace(a.Campo))
        {
            if (CamposNomina.Buscar(a.Campo) is not { } def)
                return new Error("Nominas.CampoInexistente", $"El campo «{a.Campo}» no existe.", $"Cabecera {a.Cabecera}", null, "Elige uno de la lista o ignora la columna.");
            campo = def.Nombre;
        }
        if (await s.EjecutarAsync("UPDATE AliasCamposNomina SET Campo = ? WHERE IdSociedad = ? AND Cabecera = ?", campo, idSociedad, Recortar(cabecera, 100)) == 0)
            await s.EjecutarAsync("INSERT INTO AliasCamposNomina (IdSociedad, Cabecera, Campo) VALUES (?, ?, ?)", idSociedad, Recortar(cabecera, 100), campo);
        return null;
    }

    public static string TextoTipo(TipoPaga t) => t == TipoPaga.Extra ? "EXTRA" : "MENSUAL";

    private DateTime Ahora() => new(_reloj.Ahora.Year, _reloj.Ahora.Month, _reloj.Ahora.Day, _reloj.Ahora.Hour, _reloj.Ahora.Minute, _reloj.Ahora.Second);

    private static string? Recortar(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
