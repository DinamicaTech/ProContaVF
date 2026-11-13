using System.Globalization;
using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Cuaderno43;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Importacion;

/// <summary>Qué pasa (o pasaría) al importar un fichero.</summary>
public enum VeredictoImportacion
{
    /// <summary>Se importa (o se ha importado) como lote nuevo.</summary>
    Importable,
    /// <summary>Su contenido ya está importado (mismo hash o los mismos movimientos): no se hace nada.</summary>
    YaImportado,
    /// <summary>Es el contenido de un lote revertido: se reactiva ese lote, sin duplicarlo.</summary>
    Reactivable,
    /// <summary>Trae movimientos ya importados mezclados con otros nuevos: se rechaza entero.</summary>
    Solape,
    /// <summary>Alguna cuenta bancaria no existe en la sociedad: hay que darla de alta para seguir.</summary>
    CuentasDesconocidas,
    /// <summary>Alguna cuenta bancaria es de otra sociedad: se rechaza.</summary>
    OtraSociedad,
    /// <summary>El fichero no se puede leer o no cuadra: se rechaza con sus incidencias.</summary>
    Invalido,
}

/// <summary>Cuenta bancaria de un extracto que no está dada de alta en la sociedad.</summary>
public sealed record CuentaDesconocida(string Entidad, string Oficina, string Cuenta, string Divisa, string? NombreEnFichero, string? IbanPropuesto)
{
    public string Ccc => $"{Entidad} {Oficina} {Cuenta}";
}

/// <summary>Alta de una cuenta bancaria desde el aviso de la importación; la 572 es opcional (identidad contable).</summary>
public sealed record AltaCuentaBancaria(string Entidad, string Oficina, string Cuenta, string Divisa, string Nombre, string? Iban, int? IdIdentidadContableBanco);

/// <summary>Coincidencia de un extracto del fichero con movimientos ya importados.</summary>
public sealed record SolapeN43(string Ccc, DateTime Desde, DateTime Hasta, int Coinciden, int Nuevos, IReadOnlyList<(int IdLote, string Nombre)> Lotes)
{
    public string Texto => $"{Ccc} ({Desde:dd/MM/yyyy}–{Hasta:dd/MM/yyyy}): {Coinciden} movimientos ya importados" +
        (Nuevos > 0 ? $" y {Nuevos} nuevos" : "") + (Lotes.Count == 0 ? "" : $", en {string.Join(", ", Lotes.Select(l => $"«{l.Nombre}» (lote {l.IdLote})"))}");
}

/// <summary>Análisis de un fichero antes de importarlo.</summary>
public sealed record AnalisisN43(
    VeredictoImportacion Veredicto, FicheroN43 Fichero, string Hash, int? IdLoteExistente, IReadOnlyList<CuentaDesconocida> Desconocidas,
    IReadOnlyList<SolapeN43> Solapes, IReadOnlyList<string> Rechazos)
{
    /// <summary>Explicación para el operador: por qué y qué hacer.</summary>
    public string Explicacion => Veredicto switch
    {
        VeredictoImportacion.Importable =>
            $"{Fichero.Extractos.Count} extracto(s) y {Fichero.NumeroMovimientos} movimiento(s) listos para importar" +
            (Fichero.Advertencias.Any() ? $", con {Fichero.Advertencias.Count()} advertencia(s)." : "."),
        VeredictoImportacion.YaImportado => IdLoteExistente is int l
            ? $"Este fichero ya está importado (lote {l}): no se ha hecho nada."
            : "Todos sus movimientos ya están importados: " + string.Join("; ", Solapes.Select(s => s.Texto)) + ". No se ha hecho nada.",
        VeredictoImportacion.Reactivable => $"Este fichero es el del lote {IdLoteExistente}, que se revirtió: se vuelve a activar ese mismo lote.",
        VeredictoImportacion.Solape => "El fichero trae movimientos que ya están importados junto a otros nuevos, así que no se importa nada: " +
            string.Join("; ", Solapes.Where(s => s.Coinciden > 0).Select(s => s.Texto)) +
            ". Descarga del banco solo el periodo que falta (a partir del día siguiente al último movimiento importado) o revierte antes el lote que se solapa.",
        VeredictoImportacion.CuentasDesconocidas => "Estas cuentas bancarias no están dadas de alta en la sociedad: " +
            string.Join(", ", Desconocidas.Select(d => d.Ccc)) + ". Dalas de alta para continuar con la importación.",
        VeredictoImportacion.OtraSociedad => string.Join(" ", Rechazos),
        _ => "El fichero no se puede importar: " + string.Join(" ", Fichero.Errores.Take(10).Select(e => e.Texto)) +
            (Fichero.Errores.Count() > 10 ? $" (y {Fichero.Errores.Count() - 10} errores más)." : "") +
            " Descárgalo de nuevo del banco en formato Norma 43 (cuaderno 43) y vuelve a importarlo.",
    };
}

/// <summary>Resultado de una importación.</summary>
public sealed record ResultadoImportacionN43(VeredictoImportacion Veredicto, int? IdLote, AnalisisN43 Analisis)
{
    /// <summary>True si al terminar el contenido del fichero está en la BD (importado ahora, reactivado o ya estaba).</summary>
    public bool Hecho => Veredicto is VeredictoImportacion.Importable or VeredictoImportacion.Reactivable or VeredictoImportacion.YaImportado;
    public string Explicacion => Veredicto switch
    {
        VeredictoImportacion.Importable => $"Importado el lote {IdLote}: {Analisis.Fichero.Extractos.Count} extracto(s) y {Analisis.Fichero.NumeroMovimientos} movimiento(s)" +
            (Analisis.Fichero.Advertencias.Any() ? $", con {Analisis.Fichero.Advertencias.Count()} advertencia(s) guardadas en el lote." : "."),
        VeredictoImportacion.Reactivable => $"Reactivado el lote {IdLote}, que se había revertido: no se ha duplicado nada.",
        _ => Analisis.Explicacion,
    };
}

/// <summary>
/// Importación y reversión de extractos Norma 43 (norma43 2026-11-12). Importar es idempotente: por sociedad, formato y SHA-256
/// del contenido, y además por contenido (los mismos movimientos de la misma cuenta, contando repeticiones), así que volver a
/// descargar el fichero o bajar uno con un rango mayor no duplica: si todo está ya importado no se hace nada, y si se mezclan
/// movimientos importados con otros nuevos se rechaza el fichero entero. El lote completo se escribe en una transacción.
/// Revertir no borra filas: marca el lote con fecha, operador y motivo, y se bloquea si el lote es de otra sociedad o tiene
/// imputaciones vivas o asientos.
/// </summary>
public sealed class ServicioImportacionN43
{
    public const string Formato = "NORMA43";
    public const string VersionLector = "CUADERNO43";
    public const string EstadoImportado = "IMPORTADO";
    public const string EstadoRevertido = "REVERTIDO";

    private readonly IBaseDatos _bd;
    private readonly IReloj _reloj;

    public ServicioImportacionN43(IBaseDatos bd, IReloj? reloj = null)
    {
        _bd = bd;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    public static string Huella(byte[] contenido) => Convert.ToHexString(SHA256.HashData(contenido));

    /// <summary>Lee el fichero y dice qué pasaría al importarlo, sin escribir nada.</summary>
    public Task<AnalisisN43> AnalizarAsync(ContextoSociedadEjercicio contexto, byte[] contenido, IReadOnlyList<AltaCuentaBancaria>? altas = null) =>
        _bd.EjecutarAsync(async s => (await EvaluarAsync(s, contexto, contenido, altas)).Analisis);

    /// <summary>
    /// Importa el fichero. <paramref name="altas"/> son las cuentas desconocidas que el operador ha decidido dar de alta para
    /// seguir; se crean en la misma transacción que el lote.
    /// </summary>
    public Task<ResultadoImportacionN43> ImportarAsync(ContextoSociedadEjercicio contexto, byte[] contenido, string nombreFichero, string? rutaOrigen, Actor actor,
        IReadOnlyList<AltaCuentaBancaria>? altas = null, CancellationToken cancelacion = default) =>
        _bd.EnTransaccionAsync(async s =>
        {
            var (analisis, cuentas) = await EvaluarAsync(s, contexto, contenido, altas);
            switch (analisis.Veredicto)
            {
                case VeredictoImportacion.Reactivable:
                    await s.EjecutarAsync("UPDATE LotesImportacionBancaria SET Estado = ?, FechaHoraReversion = NULL, OperadorReversion = NULL, MotivoReversion = NULL " +
                        "WHERE IdLoteImportacionBancaria = ?", EstadoImportado, analisis.IdLoteExistente!.Value);
                    return new ResultadoImportacionN43(analisis.Veredicto, analisis.IdLoteExistente, analisis);
                case VeredictoImportacion.Importable:
                    foreach (var alta in altas ?? [])
                    {
                        var clave = Clave(alta.Entidad, alta.Oficina, alta.Cuenta, alta.Divisa);
                        if (cuentas.ContainsKey(clave) && cuentas[clave] is not 0) continue;
                        if (!analisis.Fichero.Extractos.Any(e => Clave(e) == clave)) continue;
                        cuentas[clave] = await AltaCuentaAsync(s, contexto.IdSociedad, alta);
                    }
                    var id = await EscribirLoteAsync(s, contexto, contenido, nombreFichero, rutaOrigen, analisis, cuentas, cancelacion);
                    return new ResultadoImportacionN43(analisis.Veredicto, id, analisis);
                default:
                    return new ResultadoImportacionN43(analisis.Veredicto, analisis.IdLoteExistente, analisis);
            }
        }, cancelacion);

    /// <summary>Revierte un lote: lo marca como revertido con fecha, operador y motivo; no borra nada.</summary>
    public Task<Resultado> RevertirAsync(ContextoSociedadEjercicio contexto, int idLote, Actor actor, string motivo) =>
        _bd.EnTransaccionAsync(async s =>
        {
            var registro = $"Lote {idLote}";
            if (string.IsNullOrWhiteSpace(motivo))
                return Resultado.Fallo(new Error("N43.MotivoObligatorio", "Para revertir un lote hay que explicar el motivo.", registro, AccionCorrectora: "Escribe el motivo y vuelve a revertir."));
            var lote = await s.ConsultarPrimeroAsync("SELECT IdSociedad, Estado, NombreFichero FROM LotesImportacionBancaria WHERE IdLoteImportacionBancaria = ?",
                f => (Sociedad: f.Texto("IdSociedad"), Estado: f.Texto("Estado"), Nombre: f.Texto("NombreFichero")), idLote);
            if (lote.Sociedad is null)
                return Resultado.Fallo(new Error("N43.LoteInexistente", "El lote no existe.", registro, AccionCorrectora: "Actualiza la pantalla (F5): puede que otra sesión lo haya cambiado."));
            if (!string.Equals(lote.Sociedad, contexto.IdSociedad, StringComparison.OrdinalIgnoreCase))
                return Resultado.Fallo(new Error("N43.OtraSociedad", $"El lote es de la sociedad {lote.Sociedad}, no de la sociedad activa {contexto.IdSociedad}.", registro,
                    AccionCorrectora: $"Entra en la sociedad {lote.Sociedad} para revertirlo."));
            if (lote.Estado == EstadoRevertido)
                return Resultado.Fallo(new Error("N43.YaRevertido", "El lote ya está revertido.", registro, AccionCorrectora: "No hace falta hacer nada; para recuperarlo, vuelve a importar el mismo fichero."));
            const string lineas = "SELECT l.IdLineaExtractoBancario FROM LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario " +
                "WHERE e.IdLoteImportacionBancaria = ?";
            var imputadas = Convert.ToInt32(await s.EscalarAsync(
                $"SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE Estado <> 'DESCARTADA' AND IdLineaExtractoBancario IN ({lineas})", idLote), CultureInfo.InvariantCulture);
            var contabilizadas = Convert.ToInt32(await s.EscalarAsync(
                $"SELECT COUNT(*) FROM ContabilizacionesLineaExtracto WHERE IdLineaExtractoBancario IN ({lineas})", idLote), CultureInfo.InvariantCulture);
            if (contabilizadas > 0)
                return Resultado.Fallo(new Error("N43.ConAsientos", $"No se puede revertir: {contabilizadas} línea(s) del lote ya tienen asiento.", registro,
                    Causa: "Revertir dejaría asientos sin el movimiento bancario que los justifica.",
                    AccionCorrectora: "Borra antes los asientos de esas líneas desde la pantalla de líneas bancarias y vuelve a revertir."));
            if (imputadas > 0)
                return Resultado.Fallo(new Error("N43.ConImputaciones", $"No se puede revertir: {imputadas} línea(s) del lote tienen una cuenta asignada.", registro,
                    Causa: "Revertir perdería de vista el trabajo de imputación hecho sobre esas líneas.",
                    AccionCorrectora: "Quita antes la cuenta asignada a esas líneas (o descártala) y vuelve a revertir."));
            await s.EjecutarAsync("UPDATE LotesImportacionBancaria SET Estado = ?, FechaHoraReversion = ?, OperadorReversion = ?, MotivoReversion = ? WHERE IdLoteImportacionBancaria = ?",
                EstadoRevertido, SinMilisegundos(_reloj.Ahora), Recortar(actor.ToString(), 100), motivo.Trim(), idLote);
            return Resultado.Correcto();
        });

    // ---------- evaluación ----------

    private async Task<(AnalisisN43 Analisis, Dictionary<string, int> Cuentas)> EvaluarAsync(ISesionDatos s, ContextoSociedadEjercicio contexto, byte[] contenido,
        IReadOnlyList<AltaCuentaBancaria>? altas)
    {
        var fichero = LectorCuaderno43.Leer(contenido);
        var hash = Huella(contenido);
        var cuentas = new Dictionary<string, int>();
        AnalisisN43 Analisis(VeredictoImportacion v, int? lote = null, IReadOnlyList<CuentaDesconocida>? d = null, IReadOnlyList<SolapeN43>? so = null, IReadOnlyList<string>? r = null) =>
            new(v, fichero, hash, lote, d ?? [], so ?? [], r ?? []);
        if (!fichero.EsValido) return (Analisis(VeredictoImportacion.Invalido), cuentas);

        var existente = await s.ConsultarPrimeroAsync(
            "SELECT IdLoteImportacionBancaria, Estado FROM LotesImportacionBancaria WHERE IdSociedad = ? AND Formato = ? AND HashSha256Contenido = ?",
            f => (Id: f.Entero("IdLoteImportacionBancaria"), Estado: f.Texto("Estado")), contexto.IdSociedad, Formato, hash);
        if (existente.Id > 0 && existente.Estado != EstadoRevertido) return (Analisis(VeredictoImportacion.YaImportado, existente.Id), cuentas);

        // Cuentas bancarias: de la sociedad activa, de otra (se rechaza) o desconocidas (alta para seguir).
        var desconocidas = new List<CuentaDesconocida>();
        var rechazos = new List<string>();
        foreach (var e in fichero.Extractos.GroupBy(Clave).Select(g => g.First()))
        {
            var filas = await s.ConsultarAsync("SELECT IdCuentaBancaria, IdSociedad FROM CuentasBancarias WHERE EntidadOriginal = ? AND Oficina = ? AND Cuenta = ? AND Divisa = ?",
                f => (Id: f.Entero("IdCuentaBancaria"), Sociedad: f.Texto("IdSociedad")), e.Entidad, e.Oficina, e.Cuenta, e.Divisa);
            var propia = filas.FirstOrDefault(f => string.Equals(f.Sociedad, contexto.IdSociedad, StringComparison.OrdinalIgnoreCase));
            if (propia.Id > 0) { cuentas[Clave(e)] = propia.Id; continue; }
            if (filas.Count > 0)
            {
                rechazos.Add($"La cuenta {e.Ccc} es de la sociedad {filas[0].Sociedad}, no de {contexto.IdSociedad}: entra en esa sociedad para importar el fichero.");
                continue;
            }
            if (altas?.Any(a => Clave(a.Entidad, a.Oficina, a.Cuenta, a.Divisa) == Clave(e)) == true) { cuentas[Clave(e)] = 0; continue; }
            desconocidas.Add(new CuentaDesconocida(e.Entidad, e.Oficina, e.Cuenta, e.Divisa, e.Nombre, LectorCuaderno43.Iban(e.Entidad, e.Oficina, e.Cuenta)));
        }
        if (rechazos.Count > 0) return (Analisis(VeredictoImportacion.OtraSociedad, r: rechazos), cuentas);
        if (desconocidas.Count > 0) return (Analisis(VeredictoImportacion.CuentasDesconocidas, d: desconocidas), cuentas);

        // Solapes por contenido con lo ya importado (sin contar lotes revertidos ni el propio lote revertido que se reactivaría).
        var excluir = existente.Id > 0 ? existente.Id : -1;
        var solapes = new List<SolapeN43>();
        foreach (var e in fichero.Extractos)
            solapes.Add(cuentas[Clave(e)] is int idCuenta and > 0 ? await SolapeAsync(s, idCuenta, e, excluir) : new SolapeN43(e.Ccc, e.FechaInicio, e.FechaFin, 0, e.Movimientos.Count, []));
        var conCoincidencias = solapes.Count(x => x.Coinciden > 0);
        if (conCoincidencias > 0)
        {
            var veredicto = solapes.All(x => x.Nuevos == 0) ? VeredictoImportacion.YaImportado : VeredictoImportacion.Solape;
            return (Analisis(veredicto, so: solapes), cuentas);
        }
        return (Analisis(existente.Id > 0 ? VeredictoImportacion.Reactivable : VeredictoImportacion.Importable, existente.Id > 0 ? existente.Id : null, so: solapes), cuentas);
    }

    /// <summary>
    /// Compara los movimientos del extracto con los ya importados de la cuenta en las mismas fechas, contando repeticiones: dos
    /// movimientos idénticos del mismo día solo están importados si en la BD hay al menos dos. Un extracto sin movimientos
    /// coincide si ya hay uno de la cuenta con las mismas fechas y el mismo saldo final.
    /// </summary>
    private static async Task<SolapeN43> SolapeAsync(ISesionDatos s, int idCuenta, ExtractoN43 e, int excluirLote)
    {
        const string desdeLotes = "(LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria";
        if (e.Movimientos.Count == 0)
        {
            var iguales = await s.ConsultarAsync(
                "SELECT lo.IdLoteImportacionBancaria, lo.NombreFichero FROM ExtractosBancarios AS e INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
                "WHERE e.IdCuentaBancaria = ? AND e.FechaInicio = ? AND e.FechaFin = ? AND e.SaldoFinal = ? AND lo.Estado <> ? AND lo.IdLoteImportacionBancaria <> ?",
                f => (f.Entero("IdLoteImportacionBancaria"), f.Texto("NombreFichero") ?? ""), idCuenta, e.FechaInicio, e.FechaFin, e.SaldoFinal ?? 0m, EstadoRevertido, excluirLote);
            return new SolapeN43(e.Ccc, e.FechaInicio, e.FechaFin, iguales.Count > 0 ? 1 : 0, iguales.Count > 0 ? 0 : 1, iguales.Take(1).ToList());
        }
        var desde = e.Movimientos.Min(m => m.FechaOperacion);
        var hasta = e.Movimientos.Max(m => m.FechaOperacion);
        var valorDesde = e.Movimientos.Min(m => m.FechaValor);
        var valorHasta = e.Movimientos.Max(m => m.FechaValor);
        var importadas = await s.ConsultarAsync(
            "SELECT l.FechaOperacion, l.FechaValor, l.Sentido, l.Importe, l.ConceptoComun, l.ConceptoPropio, l.Documento, l.Referencia1, l.Referencia2, " +
            $"lo.IdLoteImportacionBancaria, lo.NombreFichero FROM {desdeLotes} " +
            "WHERE e.IdCuentaBancaria = ? AND ((l.FechaOperacion >= ? AND l.FechaOperacion <= ?) OR (l.FechaValor >= ? AND l.FechaValor <= ?)) " +
            "AND lo.Estado <> ? AND lo.IdLoteImportacionBancaria <> ?",
            f => (Clave: ClaveMovimiento(f.Fecha("FechaValor"), f.Texto("Sentido"), f.Decimal("Importe"), f.Texto("ConceptoComun"),
                    f.Texto("ConceptoPropio"), f.Texto("Documento"), f.Texto("Referencia1"), f.Texto("Referencia2")),
                Lote: (f.Entero("IdLoteImportacionBancaria"), f.Texto("NombreFichero") ?? "")),
            idCuenta, desde, hasta, valorDesde, valorHasta, EstadoRevertido, excluirLote);
        var enBd = importadas.GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.ToList());
        var coinciden = 0;
        var lotes = new HashSet<(int, string)>();
        foreach (var g in e.Movimientos.GroupBy(ClaveMovimiento))
        {
            if (!enBd.TryGetValue(g.Key, out var bd)) continue;
            coinciden += Math.Min(g.Count(), bd.Count);
            foreach (var x in bd) lotes.Add(x.Lote);
        }
        return new SolapeN43(e.Ccc, desde, hasta, coinciden, e.Movimientos.Count - coinciden, lotes.OrderBy(l => l.Item1).ToList());
    }

    internal static string ClaveMovimiento(MovimientoN43 m) =>
        ClaveMovimiento(m.FechaValor, m.Sentido, m.Importe, m.ConceptoComun, m.ConceptoPropio, m.Documento, m.Referencia1, m.Referencia2);

    /// <summary>
    /// Clave de contenido de un movimiento: fecha valor, sentido, importe, conceptos y referencias, sin espacios ni ceros a la
    /// izquierda. No usa la fecha de operación porque la migración del ERP guardó la fecha valor como fecha de operación en las
    /// líneas en que no coinciden (comprobado con extractos reales el 2026-11-12), ni el concepto completo, que el ERP guardaba
    /// a su manera.
    /// </summary>
    internal static string ClaveMovimiento(DateTime valor, string? sentido, decimal importe, string? comun, string? propio, string? documento, string? ref1, string? ref2)
    {
        static string N(string? t) => (t ?? "").Trim().TrimStart('0');
        return string.Join("|", valor.ToString("yyyyMMdd", CultureInfo.InvariantCulture), (sentido ?? "").Trim(),
            importe.ToString("0.00", CultureInfo.InvariantCulture), N(comun), N(propio), N(documento), N(ref1), N(ref2));
    }

    private static string Clave(ExtractoN43 e) => Clave(e.Entidad, e.Oficina, e.Cuenta, e.Divisa);
    private static string Clave(string entidad, string oficina, string cuenta, string divisa) => $"{entidad.Trim()}|{oficina.Trim()}|{cuenta.Trim()}|{divisa.Trim()}";

    // ---------- escritura ----------

    private static async Task<int> AltaCuentaAsync(ISesionDatos s, string sociedad, AltaCuentaBancaria a)
    {
        await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, IdEntidadFinanciera, EntidadOriginal, Oficina, Cuenta, Iban, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            sociedad, null, a.Entidad.Trim(), a.Oficina.Trim(), a.Cuenta.Trim(), string.IsNullOrWhiteSpace(a.Iban) ? null : a.Iban.Replace(" ", "").ToUpperInvariant(),
            a.Divisa.Trim(), Recortar(string.IsNullOrWhiteSpace(a.Nombre) ? $"{a.Entidad} {a.Oficina} {a.Cuenta}" : a.Nombre.Trim(), 150), true);
        var id = await s.UltimoIdAsync();
        if (a.IdIdentidadContableBanco is int banco)
            await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, IdIdentidadCuentaPuente, IdIdentidadSplit, IdIdentidadGastosDivisa, FechaDesde) " +
                "VALUES (?, ?, ?, ?, ?, ?)", id, banco, null, null, null, new DateTime(2000, 1, 1));
        return id;
    }

    private async Task<int> EscribirLoteAsync(ISesionDatos s, ContextoSociedadEjercicio contexto, byte[] contenido, string nombre, string? ruta, AnalisisN43 a,
        Dictionary<string, int> cuentas, CancellationToken cancelacion)
    {
        var f = a.Fichero;
        var ahora = SinMilisegundos(_reloj.Ahora);
        await s.EjecutarAsync(
            "INSERT INTO LotesImportacionBancaria (IdSociedad, Formato, VersionEsquema, NombreFichero, RutaOrigen, HashSha256Contenido, TamanoBytes, Codificacion, Estado, " +
            "FechaHoraInicio, FechaHoraFin, NumeroRegistrosDeclarado, NumeroRegistrosLeido, NumeroExtractos, NumeroMovimientos, NumeroErrores, NumeroAdvertencias) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            contexto.IdSociedad, Formato, VersionLector, Recortar(Path.GetFileName(nombre), 255), ruta, a.Hash, contenido.Length, f.Codificacion, EstadoImportado,
            ahora, ahora, f.RegistrosDeclarados, f.RegistrosLeidos, f.Extractos.Count, f.NumeroMovimientos, 0, f.Advertencias.Count());
        var idLote = await s.UltimoIdAsync();
        foreach (var e in f.Extractos)
        {
            cancelacion.ThrowIfCancellationRequested();
            await s.EjecutarAsync(
                "INSERT INTO ExtractosBancarios (IdLoteImportacionBancaria, OrdinalExtracto, IdCuentaBancaria, Entidad, Oficina, Cuenta, Divisa, FechaInicio, FechaFin, SentidoSaldoInicial, " +
                "SaldoInicial, Modalidad, Nombre, NumeroApuntesDebeDeclarado, TotalDebeDeclarado, NumeroApuntesHaberDeclarado, TotalHaberDeclarado, NumeroApuntesDebeCalculado, " +
                "TotalDebeCalculado, NumeroApuntesHaberCalculado, TotalHaberCalculado, CodigoSeguridad, SaldoFinal, EstadoCuadre, TextoCabeceraOriginal, TextoCierreOriginal) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                idLote, e.Ordinal, cuentas[Clave(e)], e.Entidad, e.Oficina, e.Cuenta, e.Divisa, e.FechaInicio, e.FechaFin, e.SentidoSaldoInicial, e.SaldoInicial, e.Modalidad,
                e.Nombre, e.NumeroDebeDeclarado ?? 0, e.TotalDebeDeclarado ?? 0m, e.NumeroHaberDeclarado ?? 0, e.TotalHaberDeclarado ?? 0m, e.NumeroDebeCalculado,
                e.TotalDebeCalculado, e.NumeroHaberCalculado, e.TotalHaberCalculado, null, e.SaldoFinal ?? 0m, "CUADRADO", e.TextoCabecera, e.TextoCierre!);
            var idExtracto = await s.UltimoIdAsync();
            foreach (var m in e.Movimientos)
            {
                await s.EjecutarAsync(
                    "INSERT INTO LineasExtractoBancario (IdExtractoBancario, OrdinalMovimiento, NumeroLineaFisica, OficinaOrigen, FechaOperacion, FechaValor, ConceptoComun, ConceptoPropio, " +
                    "Sentido, Importe, Documento, Referencia1, Referencia2, ConceptoCompleto, EntidadOficinaDestino, TextoOriginal) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    idExtracto, m.Ordinal, m.Linea, m.OficinaOrigen, m.FechaOperacion, m.FechaValor, m.ConceptoComun, m.ConceptoPropio, m.Sentido, m.Importe, m.Documento,
                    m.Referencia1, m.Referencia2, m.ConceptoCompleto, null, m.Texto);
                if (m.Complementos.Count == 0) continue;
                var idLinea = await s.UltimoIdAsync();
                foreach (var c in m.Complementos)
                    await s.EjecutarAsync(
                        "INSERT INTO ComplementosLineaExtractoBancario (IdLineaExtractoBancario, OrdinalComplemento, NumeroLineaFisica, TipoRegistro, CodigoDato, Concepto1, Concepto2, Divisa, Importe, TextoOriginal) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", idLinea, c.Ordinal, c.Linea, c.Tipo, c.CodigoDato, c.Concepto1, c.Concepto2, c.Divisa, c.Importe, c.Texto);
            }
        }
        foreach (var i in f.Advertencias)
            await s.EjecutarAsync("INSERT INTO IncidenciasImportacionBancaria (IdLoteImportacionBancaria, NumeroLineaFisica, Codigo, Severidad, Mensaje, RegistroOriginal, EstadoResolucion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?)", idLote, i.Linea, i.Codigo, "ADVERTENCIA", i.Mensaje, i.Registro, "INFORMADA");
        return idLote;
    }

    internal static string Recortar(string t, int largo) => t.Length <= largo ? t : t[..largo];
    private static DateTime SinMilisegundos(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Kind);
}
