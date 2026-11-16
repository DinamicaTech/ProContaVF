using System.Globalization;
using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;

namespace ProConta.Norma43.Similares;

/// <summary>
/// Línea con cuenta con la que se compara (norma43/movimientos-similares): lo justo para el cálculo y para la lista.
/// </summary>
/// <param name="Ejercicio">Año de la fecha contable del movimiento, como en la pantalla de líneas.</param>
/// <param name="IdIdentidadContable">Identidad de su única cuenta de destino viva.</param>
/// <param name="Cuenta">Código de esa cuenta en el ejercicio de la línea (o el último que tuvo).</param>
/// <param name="IdAsiento">Asiento que la contabilizó (de ProContaVF o del ERP); null si no está contabilizada.</param>
public sealed record CandidatoSimilarN43(int IdLinea, DateTime Fecha, int Ejercicio, bool EsCargo, decimal Importe, string Concepto,
    int IdIdentidadContable, string? Cuenta, string? NombreCuenta, int? IdAsiento);

/// <summary>Un movimiento parecido: el candidato y su porcentaje de similitud (0 a 100).</summary>
public sealed record SimilarN43(CandidatoSimilarN43 Linea, int Porcentaje);

/// <summary>
/// Cálculo de «Movimientos similares» (norma43/movimientos-similares), sin BD. Sustituye a la similitud Ratcliff/Obershelp por caracteres
/// de frmMovimientosN43Similares: se comparan las palabras significativas del concepto (sin acentos ni mayúsculas; fuera las de menos de
/// 3 letras y las que llevan cifras, que son referencias, fechas e importes que cambian cada mes) como conjuntos (Jaccard: compartidas
/// entre el total de distintas). Una palabra cortada por el banco cuenta como la completa si una empieza por la otra y las dos tienen al
/// menos 4 letras.
/// </summary>
public static class ReglasSimilaresN43
{
    /// <summary>Porcentaje mínimo de la lista completa.</summary>
    public const int MinimoLista = 20;
    /// <summary>Resultados como mucho en la lista completa.</summary>
    public const int MaximoLista = 50;
    /// <summary>Porcentaje mínimo del avance del detalle.</summary>
    public const int MinimoAvance = 50;
    /// <summary>Resultados del avance del detalle.</summary>
    public const int MaximoAvance = 3;
    /// <summary>Ejercicios en que se busca: el de la línea y los dos anteriores (propietario 2026-11-16 11:17).</summary>
    public const int Ejercicios = 3;

    /// <summary>Palabras significativas de un concepto, sin repetir y en el orden en que salen.</summary>
    public static IReadOnlyList<string> Palabras(string? concepto)
    {
        if (string.IsNullOrWhiteSpace(concepto)) return [];
        var sb = new StringBuilder(concepto.Length);
        // Sin acentos (la ñ queda como n en los dos lados) y lo que no es letra o cifra, como separador.
        foreach (var c in concepto.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !w.Any(char.IsDigit))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Dos palabras valen como la misma si son iguales o, con 4 letras o más las dos, una empieza por la otra (cortada por el banco).</summary>
    public static bool MismaPalabra(string a, string b) =>
        a == b || (a.Length >= 4 && b.Length >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)));

    /// <summary>Similitud entre dos listas de palabras significativas, de 0 a 100 (redondeada al entero).</summary>
    public static int Similitud(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count == 0 || b.Count == 0) return 0;
        var usadas = new bool[b.Count];
        var comunes = 0;
        foreach (var w in a)
            for (var j = 0; j < b.Count; j++)
                if (!usadas[j] && MismaPalabra(w, b[j])) { usadas[j] = true; comunes++; break; }
        var union = a.Count + b.Count - comunes;
        return union == 0 ? 0 : (int)Math.Round(100m * comunes / union, MidpointRounding.AwayFromZero);
    }

    public static int Similitud(string? a, string? b) => Similitud(Palabras(a), Palabras(b));

    /// <summary>Por qué no se buscan similares para esta línea; null si se puede.</summary>
    public static string? Motivo(LineaN43 linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        if (linea.EsRecibo) return "Es un recibo de una remesa o devolución explotada: su cuenta es la del cliente de la factura, no se busca por el concepto.";
        if (linea.Explotada || linea.Especial is ReglasPalabrasClave.EspecialRemesa or ReglasPalabrasClave.EspecialDevolucion)
            return "Es una remesa o una devolución de recibos: se reparte entre sus recibos con «Explotar remesa…» o «Conciliar facturas devueltas…».";
        if (Palabras(linea.Concepto).Count == 0) return "El concepto no tiene palabras con las que comparar (solo números o abreviaturas cortas).";
        return null;
    }

    /// <summary>Años en que se busca para una línea: el suyo y los dos anteriores.</summary>
    public static (int Desde, int Hasta) Rango(int ejercicio) => (ejercicio - Ejercicios + 1, ejercicio);

    /// <summary>
    /// Lista completa: los candidatos desde el mínimo, sin la propia línea ni los de fuera del rango de ejercicios, del más parecido al
    /// menos; a igual porcentaje, el más reciente primero.
    /// </summary>
    public static IReadOnlyList<SimilarN43> Buscar(LineaN43 linea, IEnumerable<CandidatoSimilarN43> candidatos, int minimo = MinimoLista, int maximo = MaximoLista)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ArgumentNullException.ThrowIfNull(candidatos);
        if (Motivo(linea) is not null) return [];
        var palabras = Palabras(linea.Concepto);
        var (desde, hasta) = Rango(linea.Ejercicio);
        return candidatos
            .Where(c => c.IdLinea != linea.Id && c.Ejercicio >= desde && c.Ejercicio <= hasta)
            .Select(c => new SimilarN43(c, Similitud(palabras, Palabras(c.Concepto))))
            .Where(s => s.Porcentaje >= minimo)
            .OrderByDescending(s => s.Porcentaje).ThenByDescending(s => s.Linea.Fecha).ThenByDescending(s => s.Linea.IdLinea)
            .Take(maximo).ToList();
    }

    /// <summary>
    /// Avance del detalle: los mejores desde el 50 % con una cuenta distinta de la que ya tiene la línea, una vez cada cuenta (el más
    /// parecido de cada una), hasta tres.
    /// </summary>
    public static IReadOnlyList<SimilarN43> Avance(LineaN43 linea, IEnumerable<CandidatoSimilarN43> candidatos)
    {
        ArgumentNullException.ThrowIfNull(linea);
        var actuales = linea.Destinos.Select(d => d.IdIdentidadContable).ToHashSet();
        return Buscar(linea, candidatos, MinimoAvance, int.MaxValue)
            .Where(s => !actuales.Contains(s.Linea.IdIdentidadContable))
            .DistinctBy(s => s.Linea.IdIdentidadContable)
            .Take(MaximoAvance).ToList();
    }
}

/// <summary>
/// Lectura ligera de las líneas con las que se compara (norma43/movimientos-similares): las de la sociedad con una sola cuenta de destino
/// viva, de los años indicados, con su concepto, su cuenta y su asiento. No entran las de lotes revertidos, los recibos de remesas
/// explotadas, las remesas y devoluciones (palabra clave especial 5 o 6, o ya explotadas) ni las repartidas entre varias cuentas.
/// </summary>
public sealed class ConsultaSimilaresN43(IBaseDatos bd)
{
    public Task<IReadOnlyList<CandidatoSimilarN43>> CandidatosAsync(string idSociedad, int desde, int hasta) => bd.EjecutarAsync<IReadOnlyList<CandidatoSimilarN43>>(async s =>
    {
        var inicio = new DateTime(desde, 1, 1);
        var fin = new DateTime(hasta + 1, 1, 1);
        // Solo las líneas con imputación viva; la fecha contable se afina en memoria como en la pantalla.
        var filas = await s.ConsultarAsync(
            "SELECT l.IdLineaExtractoBancario, l.FechaOperacion, l.FechaValor, l.Sentido, l.Importe, l.ConceptoCompleto, i.IdIdentidadContable, i.IdPalabraClaveBancaria " +
            "FROM ((LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria) " +
            "INNER JOIN ImputacionesLineaExtractoBancario AS i ON i.IdLineaExtractoBancario = l.IdLineaExtractoBancario " +
            "WHERE lo.IdSociedad = ? AND lo.Estado <> ? AND e.IdLineaExtractoOrigen IS NULL AND i.Estado <> 'DESCARTADA' " +
            "AND ((l.FechaValor >= ? AND l.FechaValor < ?) OR (l.FechaOperacion >= ? AND l.FechaOperacion < ?))",
            f => (Id: f.Entero("IdLineaExtractoBancario"), Operacion: f.Fecha("FechaOperacion").Date, Valor: f.Fecha("FechaValor").Date,
                Sentido: (ConsultaLineasN43.Limpio(f.Texto("Sentido")) ?? "").ToUpperInvariant(), Importe: f.Decimal("Importe"),
                Concepto: ConsultaLineasN43.Limpio(f.Texto("ConceptoCompleto")), Identidad: f.Entero("IdIdentidadContable"), Palabra: f.EnteroNulo("IdPalabraClaveBancaria")),
            idSociedad, ServicioImportacionN43.EstadoRevertido, inicio, fin, inicio, fin);
        var lineas = filas.GroupBy(f => f.Id)
            .Where(g => g.Count() == 1 && g.First().Concepto is not null)
            .Select(g => g.First())
            .Where(f => ServicioConciliacionN43.FechaMovimiento(f.Operacion, f.Valor).Year is var a && a >= desde && a <= hasta)
            .ToList();
        if (lineas.Count == 0) return [];

        var especiales = (await s.ConsultarAsync("SELECT IdPalabraClaveBancaria FROM PalabrasClaveBancarias WHERE IdSociedad = ? AND Especial IN (?, ?)",
            f => f.Entero("IdPalabraClaveBancaria"), idSociedad, ReglasPalabrasClave.EspecialRemesa, ReglasPalabrasClave.EspecialDevolucion)).ToHashSet();
        lineas = lineas.Where(f => f.Palabra is not int p || !especiales.Contains(p)).ToList();

        var explotadas = new HashSet<int>();
        var asientos = new Dictionary<int, int>();
        foreach (var bloque in lineas.Select(f => f.Id).Chunk(300))
        {
            var lista = ConsultaLineasN43.Lista(bloque);
            foreach (var x in await s.ConsultarAsync(
                         "SELECT e.IdLineaExtractoOrigen FROM ExtractosBancarios AS e INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
                         $"WHERE lo.Estado <> ? AND e.IdLineaExtractoOrigen IN ({lista})", f => f.Entero("IdLineaExtractoOrigen"), ServicioImportacionN43.EstadoRevertido))
                explotadas.Add(x);
            foreach (var c in await s.ConsultarAsync(
                         "SELECT c.IdLineaExtractoBancario, c.IdAsiento FROM ContabilizacionesLineaExtracto AS c INNER JOIN Asientos AS a ON c.IdAsiento = a.IdAsiento " +
                         $"WHERE c.IdLineaExtractoBancario IN ({lista}) ORDER BY c.IdContabilizacionLineaExtracto",
                         f => (Linea: f.Entero("IdLineaExtractoBancario"), Asiento: f.Entero("IdAsiento"))))
                asientos[c.Linea] = c.Asiento;
        }
        lineas = lineas.Where(f => !explotadas.Contains(f.Id)).ToList();

        // Código y nombre de cada cuenta en el ejercicio de la línea (o el último que tuvo).
        var ejercicios = (await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdSociedadEjercicio"), Anio: ConsultaLineasN43.Anio(f.Texto("Ejercicio"))), idSociedad)).ToDictionary(x => x.Id, x => x.Anio);
        var codigos = new Dictionary<(int Anio, int Identidad), (string Codigo, string Nombre)>();
        var ultimos = new Dictionary<int, (string Codigo, string Nombre)>();
        foreach (var bloque in lineas.Select(f => f.Identidad).Distinct().Chunk(300))
            foreach (var c in await s.ConsultarAsync(
                         $"SELECT IdSociedadEjercicio, IdIdentidadContable, Codigo, Nombre FROM CuentasContables WHERE IdIdentidadContable IN ({ConsultaLineasN43.Lista(bloque)}) ORDER BY IdSociedadEjercicio",
                         f => (Ej: f.Entero("IdSociedadEjercicio"), Id: f.Entero("IdIdentidadContable"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim())))
            {
                ultimos[c.Id] = (c.Codigo, c.Nombre);
                if (ejercicios.TryGetValue(c.Ej, out var anio)) codigos[(anio, c.Id)] = (c.Codigo, c.Nombre);
            }

        return lineas.Select(f =>
        {
            var fecha = ServicioConciliacionN43.FechaMovimiento(f.Operacion, f.Valor);
            var c = codigos.TryGetValue((fecha.Year, f.Identidad), out var x) ? x : ultimos.TryGetValue(f.Identidad, out var u) ? u : ((string Codigo, string Nombre)?)null;
            return new CandidatoSimilarN43(f.Id, f.Operacion, fecha.Year, f.Sentido == ServicioConciliacionN43.Cargo, f.Importe, f.Concepto!, f.Identidad, c?.Codigo, c?.Nombre,
                asientos.TryGetValue(f.Id, out var a) ? a : null);
        }).ToList();
    });
}
