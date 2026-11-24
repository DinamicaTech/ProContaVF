using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.UI.Logica;

/// <summary>Grupo de operadores que se puede marcar en el rol «Operador ProConta».</summary>
public sealed record GrupoOperadorFicha(int IdGrupo, string Nombre, string? Descripcion);

/// <summary>Rol «Operador ProConta» de un tercero tal como está guardado: su nombre de acceso, si está activo, si tiene contraseña y sus grupos.</summary>
public sealed record EstadoOperadorFicha(int IdOperador, string NombreAcceso, bool Activo, bool TieneContrasena, IReadOnlyList<int> Grupos);

/// <summary>
/// El rol «Operador ProConta» en la ficha del tercero (seguridad/pantallas 2026-11-24). Los operadores son de seguridad: el contenedor
/// conecta este contrato con su servicio y con el operador de la sesión. Sin él, la ficha no muestra el rol.
/// </summary>
public interface IOperadoresFicha
{
    /// <summary>El operador de la sesión puede dar y quitar este rol (de momento, los administradores).</summary>
    Task<bool> PuedeGestionarAsync();

    /// <summary>Rol del tercero, o null si nunca ha sido operador.</summary>
    Task<EstadoOperadorFicha?> ObtenerAsync(int idTercero);

    /// <summary>Grupos activos que se pueden marcar.</summary>
    Task<IReadOnlyList<GrupoOperadorFicha>> GruposAsync();

    /// <summary>
    /// Da, cambia o quita el rol con sus grupos. Con <paramref name="soloComprobar"/> solo dice si se puede (antes de guardar la ficha;
    /// un tercero nuevo llega con IdTercero 0).
    /// </summary>
    Task<Resultado> GuardarAsync(int idTercero, bool activo, IReadOnlyCollection<int> grupos, bool soloComprobar);

    /// <summary>
    /// Elige un operador existente y lo pasa a este tercero (persona física guardada que nunca ha sido operador), con su número, contraseña
    /// y grupos. Devuelve el aviso que se muestra, o null si se cancela.
    /// </summary>
    Task<string?> PasarOperadorAsync(int idTercero, string nombreTercero);

    /// <summary>Pide la contraseña nueva del operador y la guarda; devuelve el aviso que se muestra, o null si se cancela.</summary>
    Task<string?> CambiarContrasenaAsync(int idOperador, string nombreAcceso);
}
