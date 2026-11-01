using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Persistencia;

/// <summary>
/// Fallo técnico de la BD traducido a un error explícito (RF-CFG-04): nunca se confunde con
/// «sin datos» ni con «sin permiso». Lleva un <see cref="Nucleo.Resultados.Error"/> para el operador.
/// </summary>
public class ExcepcionDatos : Exception
{
    public ExcepcionDatos(Error error, Exception? interna = null) : base(error.ToString(), interna) => Error = error;

    public Error Error { get; }
}

/// <summary>La tabla o consulta pedida no existe en la BD.</summary>
public sealed class TablaAusenteException(Error error, Exception? interna = null) : ExcepcionDatos(error, interna);

/// <summary>Se viola un índice único (registro duplicado).</summary>
public sealed class UnicidadException(Error error, Exception? interna = null) : ExcepcionDatos(error, interna);

/// <summary>Falta el registro relacionado o hay registros que dependen del que se borra.</summary>
public sealed class IntegridadReferencialException(Error error, Exception? interna = null) : ExcepcionDatos(error, interna);

/// <summary>La BD o un registro está bloqueado por otra sesión.</summary>
public sealed class BloqueoException(Error error, Exception? interna = null) : ExcepcionDatos(error, interna);

/// <summary>No se encuentra o no se puede abrir el fichero de la BD.</summary>
public sealed class BaseDatosNoDisponibleException(Error error, Exception? interna = null) : ExcepcionDatos(error, interna);

/// <summary>Otro operador cambió o borró la ficha después de leerla (concurrencia optimista).</summary>
public sealed class ConflictoConcurrenciaException(Error error) : ExcepcionDatos(error);
