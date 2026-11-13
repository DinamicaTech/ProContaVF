using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Norma43.Asignacion;

/// <summary>Lo que el motor decide para una línea.</summary>
public sealed record AsignacionLineaN43(int IdLinea, EleccionPalabra Eleccion)
{
    /// <summary>True si la línea recibe cuenta: hay palabra clave y tiene cuenta.</summary>
    public bool RecibeCuenta => Eleccion.Palabra is { IdIdentidadContable: not null };
}

/// <summary>Resumen de una pasada del motor.</summary>
public sealed record ResultadoAsignacionN43(IReadOnlyList<AsignacionLineaN43> Lineas)
{
    public static readonly ResultadoAsignacionN43 Vacio = new([]);

    /// <summary>Líneas sin cuenta que se han revisado.</summary>
    public int Revisadas => Lineas.Count;
    /// <summary>Líneas que reciben (o recibirían, al simular) la cuenta de su palabra clave.</summary>
    public int Asignadas => Lineas.Count(l => l.RecibeCuenta);
    public int SinPalabra => Lineas.Count(l => l.Eleccion.Resultado == ResultadoBusqueda.SinPalabra);
    /// <summary>Líneas en que encajan varias palabras clave igual de concretas con cuentas distintas: se dejan para el operador.</summary>
    public int Ambiguas => Lineas.Count(l => l.Eleccion.Resultado == ResultadoBusqueda.Ambigua);
    /// <summary>Líneas cuya palabra clave aún no tiene cuenta.</summary>
    public int PalabraSinCuenta => Lineas.Count(l => l.Eleccion.Palabra is { IdIdentidadContable: null });
    /// <summary>Líneas de remesas de recibos (incluidas en las asignadas si su palabra tiene cuenta): quedan pendientes de explotar (norma43/remesas).</summary>
    public int Remesas => Lineas.Count(l => l.Eleccion.Palabra is { EsRemesa: true });

    /// <summary>Resumen para el operador.</summary>
    public string Texto => Revisadas == 0
        ? "No había líneas sin cuenta."
        : $"{Asignadas} de {Revisadas} línea(s) sin cuenta la han recibido por palabra clave" +
          (Ambiguas > 0 ? $"; {Ambiguas} encajan con varias palabras clave de cuentas distintas y se dejan sin asignar" : "") +
          (PalabraSinCuenta > 0 ? $"; {PalabraSinCuenta} tienen una palabra clave sin cuenta" : "") +
          (Remesas > 0 ? $"; {Remesas} son remesas de recibos, pendientes de explotar" : "") + ".";
}

/// <summary>
/// Motor de asignación automática de cuentas a las líneas de extractos Norma 43 con las palabras clave bancarias
/// (norma43/asignacion-cuentas). Revisa las líneas de la sociedad (o de un lote) que no tienen imputación viva ni asiento y
/// que no son de lotes revertidos, elige la palabra clave más concreta (<see cref="ReglasPalabrasClave"/>) y, si tiene cuenta
/// (también las de remesa, como hacía el ERP), escribe la imputación de contrapartida con la palabra clave que la asignó. Nunca toca una línea que ya
/// tiene cuenta (puesta a mano, migrada o asignada antes), así que se puede repetir sin efectos.
/// </summary>
public sealed class AsignadorCuentasN43
{
    public const string Origen = "PALABRA_CLAVE";
    public const string Rol = "CONTRAPARTIDA";
    public const string Estado = "CONFIRMADA";

    private readonly IBaseDatos _bd;

    public AsignadorCuentasN43(IBaseDatos bd) => _bd = bd;

    /// <summary>Asigna cuenta a las líneas sin cuenta de la sociedad, o solo a las del lote indicado.</summary>
    public Task<ResultadoAsignacionN43> AsignarAsync(ContextoSociedadEjercicio contexto, int? idLote = null, CancellationToken cancelacion = default) =>
        _bd.EnTransaccionAsync(s => AsignarAsync(s, contexto.IdSociedad, idLote, escribir: true, incluirAsignadas: false, cancelacion), cancelacion);

    /// <summary>
    /// Dice qué haría el motor sin escribir nada. Con <paramref name="incluirAsignadas"/> también evalúa las líneas que ya
    /// tienen cuenta (sirve para comparar el motor con lo que asignó el ERP).
    /// </summary>
    public Task<ResultadoAsignacionN43> SimularAsync(ContextoSociedadEjercicio contexto, int? idLote = null, bool incluirAsignadas = false, CancellationToken cancelacion = default) =>
        _bd.EjecutarAsync(s => AsignarAsync(s, contexto.IdSociedad, idLote, escribir: false, incluirAsignadas, cancelacion), cancelacion);

    /// <summary>Pasada del motor dentro de una sesión ya abierta (la importación la lanza en su propia transacción).</summary>
    internal static async Task<ResultadoAsignacionN43> AsignarAsync(ISesionDatos s, string idSociedad, int? idLote, bool escribir, bool incluirAsignadas,
        CancellationToken cancelacion = default)
    {
        var palabras = await PalabrasAsync(s, idSociedad);

        var sql = "SELECT l.IdLineaExtractoBancario, e.Entidad, l.Sentido, l.Importe, l.ConceptoCompleto, l.Documento, l.Referencia1, l.Referencia2 " +
            "FROM (LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
            "WHERE lo.IdSociedad = ? AND lo.Estado <> ?";
        var parametros = new List<object?> { idSociedad, Importacion.ServicioImportacionN43.EstadoRevertido };
        if (idLote is int lote) { sql += " AND lo.IdLoteImportacionBancaria = ?"; parametros.Add(lote); }
        var lineas = await s.ConsultarAsync(sql + " ORDER BY l.IdLineaExtractoBancario", f => new LineaN43ParaAsignar(
            f.Entero("IdLineaExtractoBancario"), f.Texto("Entidad") ?? "", f.Texto("Sentido") ?? "", f.Decimal("Importe"), f.Texto("ConceptoCompleto"),
            f.Texto("Documento"), f.Texto("Referencia1"), f.Texto("Referencia2")), [.. parametros]);
        if (lineas.Count == 0) return ResultadoAsignacionN43.Vacio;

        if (!incluirAsignadas)
        {
            // Una línea con cuenta (imputación no descartada) o con asiento no se toca: la reasignación nunca pisa nada.
            var conCuenta = await IdsAsync(s, "SELECT DISTINCT IdLineaExtractoBancario FROM ImputacionesLineaExtractoBancario WHERE Estado <> 'DESCARTADA'");
            var conAsiento = await IdsAsync(s, "SELECT DISTINCT IdLineaExtractoBancario FROM ContabilizacionesLineaExtracto WHERE Estado <> 'ANULADA'");
            lineas = lineas.Where(l => !conCuenta.Contains(l.IdLinea) && !conAsiento.Contains(l.IdLinea)).ToList();
        }

        var resultado = new List<AsignacionLineaN43>(lineas.Count);
        foreach (var l in lineas)
        {
            cancelacion.ThrowIfCancellationRequested();
            var a = new AsignacionLineaN43(l.IdLinea, ReglasPalabrasClave.Elegir(l, palabras));
            resultado.Add(a);
            if (!escribir || !a.RecibeCuenta) continue;
            var p = a.Eleccion.Palabra!;
            await s.EjecutarAsync(
                "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Confianza, Estado, Concepto, IdCentroCoste, IdPalabraClaveBancaria) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                l.IdLinea, p.IdIdentidadContable!.Value, Rol, ImporteFirmado(l), Origen, null, Estado, l.ConceptoCompleto, null, p.Id);
        }
        return new ResultadoAsignacionN43(resultado);
    }

    /// <summary>Importe de la contrapartida con signo: positivo en los cargos (D) y negativo en los abonos (H), como la migración del ERP.</summary>
    internal static decimal ImporteFirmado(LineaN43ParaAsignar l) => string.Equals(l.Sentido.Trim(), "D", StringComparison.OrdinalIgnoreCase) ? l.Importe : -l.Importe;

    private static Task<IReadOnlyList<PalabraClaveN43>> PalabrasAsync(ISesionDatos s, string idSociedad) =>
        s.ConsultarAsync("SELECT IdPalabraClaveBancaria, Entidad, Sentido, PalabraClave, PalabraClave2, IdIdentidadContable, Especial FROM PalabrasClaveBancarias " +
            "WHERE IdSociedad = ? AND Activa = True ORDER BY IdPalabraClaveBancaria",
            f => new PalabraClaveN43(f.Entero("IdPalabraClaveBancaria"), f.Texto("Entidad"), f.Texto("Sentido"), f.Texto("PalabraClave") ?? "", f.Texto("PalabraClave2"),
                f.EnteroNulo("IdIdentidadContable"), f.EnteroNulo("Especial")), idSociedad);

    private static async Task<HashSet<int>> IdsAsync(ISesionDatos s, string sql) =>
        [.. await s.ConsultarAsync(sql, f => f.Entero("IdLineaExtractoBancario"))];
}
