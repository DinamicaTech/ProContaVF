using System.ComponentModel;

namespace ProConta.UI.Base.Contenedor;

/// <summary>
/// Pantalla que vive en una pestaña del área de trabajo del contenedor (plataforma/contenedor). El contenedor pinta su
/// título con el punto de cambios pendientes o el triángulo de guardado fallido, guarda la activa con Ctrl+G o Ctrl+S
/// y, al cerrar la pestaña o salir con cambios, ofrece guardar, descartar o seguir editando.
/// </summary>
public interface IPestanaTrabajo : INotifyPropertyChanged
{
    string Titulo { get; }

    /// <summary>Tooltip de la pestaña (qué es y, si hay cambios, cuántos).</summary>
    string Ayuda { get; }

    /// <summary>Clave del icono de la biblioteca (por ejemplo «user»), sin el prefijo «Icono.».</summary>
    string Icono { get; }

    bool TieneCambios { get; }

    /// <summary>El último intento de guardar falló (triángulo en la pestaña).</summary>
    bool GuardadoFallido { get; }

    /// <summary>Contenido de la pestaña (un elemento WPF). Se crea una vez y se conserva mientras la pestaña está abierta.</summary>
    object Vista { get; }

    /// <summary>Cambios pendientes en frases cortas, para el aviso de cerrar o salir.</summary>
    IReadOnlyList<string> DescribirCambios();

    /// <summary>Guarda los cambios; false si no se han podido guardar (la pantalla ya ha explicado por qué).</summary>
    Task<bool> GuardarAsync();

    /// <summary>Descarta los cambios pendientes.</summary>
    void Descartar();

    /// <summary>La pestaña se ha cerrado: libera lo que tenga abierto.</summary>
    void AlCerrar();
}

/// <summary>Área de trabajo del contenedor, tal como la ven los módulos que abren pestañas.</summary>
public interface IAnfitrionPestanas
{
    IReadOnlyList<IPestanaTrabajo> Pestanas { get; }

    IPestanaTrabajo? Activa { get; }

    /// <summary>Abre una pestaña (o la pone en el sitio de <paramref name="sustituir"/>, que se cierra sin preguntar) y la activa.</summary>
    void Abrir(IPestanaTrabajo pestana, IPestanaTrabajo? sustituir = null);

    void Activar(IPestanaTrabajo pestana);

    /// <summary>Cierra la pestaña sin preguntar (quien llama ya ha resuelto sus cambios).</summary>
    void Cerrar(IPestanaTrabajo pestana);

    /// <summary>Aviso breve en la barra de estado, que no bloquea.</summary>
    void Avisar(string texto);
}
