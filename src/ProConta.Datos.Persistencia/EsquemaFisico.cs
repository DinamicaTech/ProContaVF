namespace ProConta.Datos.Persistencia;

/// <summary>Estructura física leída de la BD; la usan el ActualizadorEsquema y el diccionario de datos.</summary>
public sealed record EsquemaFisico(IReadOnlyList<TablaFisica> Tablas)
{
    public TablaFisica? Tabla(string nombre) =>
        Tablas.FirstOrDefault(t => string.Equals(t.Nombre, nombre, StringComparison.OrdinalIgnoreCase));

    public bool ExisteTabla(string nombre) => Tabla(nombre) is not null;

    public bool ExisteColumna(string tabla, string columna) => Tabla(tabla)?.Columna(columna) is not null;

    public bool ExisteIndice(string tabla, string indice) =>
        Tabla(tabla)?.Indices.Any(i => string.Equals(i.Nombre, indice, StringComparison.OrdinalIgnoreCase)) == true;
}

/// <param name="EsVinculada">true si es una tabla vinculada a otro fichero.</param>
public sealed record TablaFisica(
    string Nombre,
    bool EsVinculada,
    string? Descripcion,
    IReadOnlyList<ColumnaFisica> Columnas,
    IReadOnlyList<IndiceFisico> Indices,
    IReadOnlyList<RelacionFisica> Relaciones)
{
    public ColumnaFisica? Columna(string nombre) =>
        Columnas.FirstOrDefault(c => string.Equals(c.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Tipo">Tipo Access legible (TEXT(100), LONG, CURRENCY…).</param>
public sealed record ColumnaFisica(string Nombre, int Posicion, string Tipo, int? Longitud, bool AdmiteNulos, string? PorDefecto, string? Descripcion);

public sealed record IndiceFisico(string Nombre, bool EsClavePrimaria, bool EsUnico, IReadOnlyList<string> Columnas);

/// <summary>Relación (clave ajena) de esta tabla hacia otra.</summary>
public sealed record RelacionFisica(string Nombre, IReadOnlyList<string> Columnas, string TablaReferenciada, IReadOnlyList<string> ColumnasReferenciadas, string ReglaBorrado, string ReglaActualizacion);

/// <summary>
/// Memoria de la estructura de la BD mientras dura un ámbito (contabilidad/modelos-tributarios 2026-11-18): leer la estructura de Access
/// cuesta unos 0,4 s y la pantalla de modelos tributarios la consultaba unas 80 veces al abrir. Dentro de un ámbito abierto con
/// <see cref="Abrir"/>, <c>LeerEsquema</c> fuera de transacción la lee una vez por fichero; fuera de un ámbito la lee siempre (así una
/// migración o una prueba que cambia la estructura la ve al momento).
/// </summary>
public static class MemoriaEsquema
{
    private static readonly AsyncLocal<System.Collections.Concurrent.ConcurrentDictionary<string, EsquemaFisico>?> Actual = new();

    public static IDisposable Abrir()
    {
        var anterior = Actual.Value;
        Actual.Value = new System.Collections.Concurrent.ConcurrentDictionary<string, EsquemaFisico>(StringComparer.OrdinalIgnoreCase);
        return new Cierre(anterior);
    }

    // Memoria de todo el proceso (registro-errores 2026-11-28): ProContaVF la enciende al arrancar, después de actualizar el
    // esquema; desde entonces la estructura de cada fichero se lee una sola vez por sesión del programa. Así las consultas, que
    // ahora van por turno, no esperan las lecturas de estructura repetidas (unos 0,4 s cada una).
    private static System.Collections.Concurrent.ConcurrentDictionary<string, EsquemaFisico>? _proceso;

    /// <summary>
    /// Recuerda la estructura de cada BD hasta que se cierra el programa (o hasta <see cref="Olvidar"/>). Solo para quien ya ha
    /// actualizado el esquema y no lo cambia después (ProContaVF tras el arranque); las migraciones y las pruebas no la encienden.
    /// </summary>
    public static void RecordarEnElProceso() =>
        _proceso ??= new System.Collections.Concurrent.ConcurrentDictionary<string, EsquemaFisico>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Olvida la estructura recordada en el proceso (se vuelve a leer la próxima vez).</summary>
    public static void Olvidar() => _proceso?.Clear();

    /// <summary>Apaga la memoria del proceso: fuera de un ámbito se vuelve a leer siempre (las pruebas la dejan así al terminar).</summary>
    public static void DejarDeRecordar() => _proceso = null;

    public static EsquemaFisico Obtener(string clave, Func<EsquemaFisico> leer) =>
        (Actual.Value ?? _proceso) is { } d ? d.GetOrAdd(clave, _ => leer()) : leer();

    private sealed class Cierre(System.Collections.Concurrent.ConcurrentDictionary<string, EsquemaFisico>? anterior) : IDisposable
    {
        public void Dispose() => Actual.Value = anterior;
    }
}
