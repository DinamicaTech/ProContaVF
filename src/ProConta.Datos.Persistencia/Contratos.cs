namespace ProConta.Datos.Persistencia;

/// <summary>Cómo se abre la BD.</summary>
public enum ModoApertura
{
    /// <summary>Uso normal: varias sesiones de Escritorio remoto a la vez.</summary>
    Compartido,
    /// <summary>Solo lectura (diagnóstico, diccionario).</summary>
    SoloLectura,
    /// <summary>Uso exclusivo: lo exige el ActualizadorEsquema para aplicar DDL.</summary>
    Exclusivo
}

/// <summary>
/// Punto de entrada a la BD. Cada llamada abre una conexión corta y la cierra al terminar;
/// nunca se mantiene abierta durante HTTP, SMTP, OCR o copias de ficheros (RF-TEC-01).
/// </summary>
public interface IBaseDatos
{
    /// <summary>Ruta completa del fichero accdb.</summary>
    string Ruta { get; }

    /// <summary>Ejecuta un trabajo sin transacción (lecturas o escrituras sueltas).</summary>
    Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default);

    /// <summary>
    /// Ejecuta un trabajo en una transacción corta. Se confirma si termina bien; se revierte si
    /// lanza una excepción o si devuelve un <see cref="Nucleo.Resultados.Resultado"/> fallido.
    /// </summary>
    Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default);
}

/// <summary>
/// Sesión abierta sobre la BD, con o sin transacción. Las consultas usan parámetros por
/// posición (marcadores «?») en el orden de <c>parametros</c>.
/// </summary>
public interface ISesionDatos
{
    bool EnTransaccion { get; }

    Task<IReadOnlyList<T>> ConsultarAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] parametros);

    /// <summary>Primera fila o el valor por defecto si no hay ninguna.</summary>
    Task<T?> ConsultarPrimeroAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] parametros);

    Task<object?> EscalarAsync(string sql, params object?[] parametros);

    /// <summary>Ejecuta una orden y devuelve las filas afectadas.</summary>
    Task<int> EjecutarAsync(string sql, params object?[] parametros);

    /// <summary>
    /// Actualiza o borra una ficha con concurrencia optimista: la orden debe filtrar por la
    /// versión leída. Si no afecta a ninguna fila, lanza <see cref="ConflictoConcurrenciaException"/>.
    /// </summary>
    Task ActualizarConVersionAsync(string sql, string registroAfectado, params object?[] parametros);

    /// <summary>Último autonumérico generado en esta conexión (SELECT @@IDENTITY).</summary>
    Task<int> UltimoIdAsync();

    /// <summary>
    /// Siguiente número de una serie (MAX + 1) dentro de la transacción en curso. La unicidad
    /// la garantiza un índice único de la BD; si otra sesión gana la carrera, la inserción
    /// falla con <see cref="UnicidadException"/> y el llamador repite la transacción.
    /// </summary>
    Task<int> SiguienteNumeroAsync(string tabla, string campo, string? filtro = null, params object?[] parametros);

    /// <summary>Estructura física de la BD (tablas, columnas, índices y relaciones).</summary>
    EsquemaFisico LeerEsquema();
}

/// <summary>Fila de un resultado, sin exponer tipos de OLE DB al resto del código.</summary>
public interface IFilaDatos
{
    bool EsNulo(string campo);
    string? Texto(string campo);
    int Entero(string campo);
    int? EnteroNulo(string campo);
    long EnteroLargo(string campo);
    decimal Decimal(string campo);
    decimal? DecimalNulo(string campo);
    double Doble(string campo);
    DateTime Fecha(string campo);
    DateTime? FechaNula(string campo);
    bool Logico(string campo);
    byte[]? Binario(string campo);
    object? Valor(string campo);
}
