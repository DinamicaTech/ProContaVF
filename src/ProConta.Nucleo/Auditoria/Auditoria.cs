namespace ProConta.Nucleo.Auditoria;

/// <summary>Quién hace una operación: el operador autenticado o un proceso del sistema.</summary>
/// <param name="Nombre">Nombre del operador o del proceso (nunca una contraseña ni un dato secreto).</param>
/// <param name="EsProceso">true si es un proceso automático (programadas, importaciones…).</param>
public sealed record Actor(string Nombre, bool EsProceso = false)
{
    public static Actor Proceso(string nombre) => new(nombre, true);
    public override string ToString() => EsProceso ? $"proceso:{Nombre}" : Nombre;
}

/// <summary>Reloj inyectable, para que las pruebas fijen el instante.</summary>
public interface IReloj
{
    DateTime Ahora { get; }
}

/// <summary>Reloj del sistema en hora local (la BD guarda fechas locales).</summary>
public sealed class RelojSistema : IReloj
{
    public static readonly RelojSistema Instancia = new();
    public DateTime Ahora => DateTime.Now;
}

/// <summary>
/// Marca de auditoría de una operación: actor, instante y operación, más el contexto y una
/// correlación para seguir en los registros todo lo que hizo una misma acción (RF-TEC-04).
/// </summary>
public sealed record MarcaAuditoria(
    Actor Actor,
    DateTime Instante,
    string Operacion,
    string? Contexto = null,
    Guid? Correlacion = null)
{
    public static MarcaAuditoria Crear(Actor actor, string operacion, IReloj? reloj = null, string? contexto = null, Guid? correlacion = null) =>
        new(actor, (reloj ?? RelojSistema.Instancia).Ahora, operacion, contexto, correlacion ?? Guid.NewGuid());
}
