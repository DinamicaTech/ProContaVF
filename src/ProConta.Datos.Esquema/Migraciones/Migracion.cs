using System.Security.Cryptography;
using System.Text;
using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>
/// Cambio de estructura versionado. Su identificador lleva la fecha y hora en que se diseñó
/// (<c>yyyyMMdd_HHmm_NOMBRE</c>) y su orden es <c>yyMMddNNN</c>, como las migraciones heredadas.
/// Cada paso comprueba si ya está hecho, así que repetir una migración es inocuo.
/// </summary>
public sealed class Migracion
{
    public Migracion(string id, int orden, string descripcion, string nodoSolicitante, params PasoMigracion[] pasos)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{8}_\d{4}_[A-Z0-9_]+$"))
            throw new ArgumentException($"El identificador «{id}» no sigue el formato yyyyMMdd_HHmm_NOMBRE.", nameof(id));
        if (pasos.Length == 0) throw new ArgumentException("Una migración necesita al menos un paso.", nameof(pasos));
        Id = id;
        Orden = orden;
        Descripcion = descripcion;
        NodoSolicitante = nodoSolicitante;
        Pasos = pasos;
        Hash = CalcularHash();
    }

    public string Id { get; }
    public int Orden { get; }
    public string Descripcion { get; }

    /// <summary>Nodo de docs que pidió el cambio con su línea «Derived from».</summary>
    public string NodoSolicitante { get; }

    public IReadOnlyList<PasoMigracion> Pasos { get; }

    /// <summary>
    /// Comprobación previa opcional: se ejecuta para todas las pendientes antes de hacer la copia y de
    /// cambiar nada; si devuelve un error, la actualización no empieza.
    /// </summary>
    public Func<ISesionDatos, ContextoMigracion, Task<Nucleo.Resultados.Error?>>? Precondicion { get; init; }

    /// <summary>SHA-256 de la definición: identifica la migración aplicada aunque cambie su texto descriptivo.</summary>
    public string Hash { get; }

    private string CalcularHash()
    {
        var texto = new StringBuilder(Id).Append('\n');
        foreach (var paso in Pasos) texto.Append(paso.Firma).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())));
    }

    public override string ToString() => Id;
}

/// <summary>
/// Datos que recibe una migración al aplicarse: parámetros de la instalación (por ejemplo, el
/// fichero de origen de un catálogo que estaba vinculado), indicados al ejecutar el ActualizadorEsquema.
/// </summary>
public sealed record ContextoMigracion(IReadOnlyDictionary<string, string> Parametros)
{
    public static readonly ContextoMigracion Vacio = new(new Dictionary<string, string>());

    public string? Parametro(string nombre) => Parametros.TryGetValue(nombre, out var valor) && !string.IsNullOrWhiteSpace(valor) ? valor : null;
}

/// <summary>Paso idempotente de una migración.</summary>
public abstract class PasoMigracion
{
    /// <summary>Texto descriptivo para el registro de la migración.</summary>
    public abstract string Descripcion { get; }

    /// <summary>Texto estable que define el paso; entra en el hash de la migración.</summary>
    public abstract string Firma { get; }

    public abstract Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema);

    public abstract Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto);
}

/// <summary>Crea una tabla si no existe. <c>sql</c> es el CREATE TABLE completo.</summary>
public sealed class CrearTabla(string tabla, string sql) : PasoMigracion
{
    public override string Descripcion => $"Crear la tabla {tabla}";
    public override string Firma => sql;
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.Tabla(tabla) is { EsVinculada: false });
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => sesion.EjecutarAsync(sql);
}

/// <summary>Añade una columna si no existe (<c>definicion</c>: «TEXT(50) NOT NULL», «YESNO»…).</summary>
public sealed class AnadirColumna(string tabla, string columna, string definicion) : PasoMigracion
{
    public override string Descripcion => $"Añadir {tabla}.{columna}";
    public override string Firma => $"ALTER TABLE [{tabla}] ADD COLUMN [{columna}] {definicion}";
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.ExisteColumna(tabla, columna));
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => sesion.EjecutarAsync(Firma);
}

/// <summary>
/// Añade una columna obligatoria con valor por defecto a una tabla con filas: Access deja a null las
/// filas existentes aunque la columna sea NOT NULL, así que se añade, se rellena y después se exige.
/// Si la columna ya existe pero admite nulos (intento anterior a medias), se completa.
/// </summary>
public sealed class AnadirColumnaObligatoria(string tabla, string columna, string tipo, string valorPorDefecto) : PasoMigracion
{
    public override string Descripcion => $"Añadir {tabla}.{columna} obligatoria (por defecto {valorPorDefecto})";
    public override string Firma => $"ALTER TABLE [{tabla}] ADD COLUMN [{columna}] {tipo} DEFAULT {valorPorDefecto}; UPDATE [{tabla}] SET [{columna}] = {valorPorDefecto} WHERE [{columna}] IS NULL; ALTER TABLE [{tabla}] ALTER COLUMN [{columna}] {tipo} NOT NULL DEFAULT {valorPorDefecto}";
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.Tabla(tabla)?.Columna(columna) is { AdmiteNulos: false });
    public override async Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto)
    {
        if (!sesion.LeerEsquema().ExisteColumna(tabla, columna))
            await sesion.EjecutarAsync($"ALTER TABLE [{tabla}] ADD COLUMN [{columna}] {tipo} DEFAULT {valorPorDefecto}");
        await sesion.EjecutarAsync($"UPDATE [{tabla}] SET [{columna}] = {valorPorDefecto} WHERE [{columna}] IS NULL");
        await sesion.EjecutarAsync($"ALTER TABLE [{tabla}] ALTER COLUMN [{columna}] {tipo} NOT NULL DEFAULT {valorPorDefecto}");
    }
}

/// <summary>Crea un índice si no existe. <c>sql</c> es el CREATE [UNIQUE] INDEX completo.</summary>
public sealed class CrearIndice(string tabla, string indice, string sql) : PasoMigracion
{
    public override string Descripcion => $"Crear el índice {indice} de {tabla}";
    public override string Firma => sql;
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.ExisteIndice(tabla, indice));
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => sesion.EjecutarAsync(sql);
}

/// <summary>Quita el vínculo de una tabla vinculada (no toca la BD de destino del vínculo).</summary>
public sealed class QuitarVinculo(string tabla) : PasoMigracion
{
    public override string Descripcion => $"Quitar el vínculo de la tabla {tabla}";
    public override string Firma => $"DROP LINK [{tabla}]";
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.Tabla(tabla) is not { EsVinculada: true });
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => sesion.EjecutarAsync($"DROP TABLE [{tabla}]");
}

/// <summary>Paso de datos o de DDL particular, con su propia comprobación de «ya hecho».</summary>
public sealed class PasoPersonalizado(
    string descripcion,
    string firma,
    Func<ISesionDatos, EsquemaFisico, Task<bool>> yaAplicado,
    Func<ISesionDatos, ContextoMigracion, Task> aplicar) : PasoMigracion
{
    public override string Descripcion => descripcion;
    public override string Firma => firma;
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) => yaAplicado(sesion, esquema);
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => aplicar(sesion, contexto);
}
