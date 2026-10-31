namespace ProConta.UI.Base.Preferencias;

/// <summary>
/// Preferencias de presentación del operador (columnas, paneles, anchos), por clave.
/// La biblioteca solo conoce esta interfaz: de momento se guardan en un fichero local por operador
/// (<see cref="PreferenciasJson"/>); cuando exista su tabla irán a la base de datos (cambio de datos/esquema).
/// </summary>
public interface IPreferenciasOperador
{
    /// <summary>Lee una preferencia; devuelve default si no existe o no se puede leer.</summary>
    T? Leer<T>(string clave);

    /// <summary>Guarda una preferencia.</summary>
    void Guardar<T>(string clave, T valor);

    /// <summary>Borra una preferencia (vuelve a la de fábrica).</summary>
    void Borrar(string clave);
}

/// <summary>Punto de acceso a las preferencias del operador activo. La aplicación lo fija al iniciar sesión.</summary>
public static class PreferenciasOperador
{
    /// <summary>Preferencias del operador activo; sin sesión, en memoria (no se guardan).</summary>
    public static IPreferenciasOperador Actual { get; set; } = new PreferenciasEnMemoria();
}
