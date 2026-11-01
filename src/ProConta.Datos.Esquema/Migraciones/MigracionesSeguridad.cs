namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de seguridad (docs/datos/esquema.md, Requirements).</summary>
public static class MigracionesSeguridad
{
    /// <summary>
    /// Parámetro general «Mostrar la lista de operadores en el acceso», guardado en negativo
    /// (Configuracion.OcultarListaOperadoresAcceso, por defecto no): la lista se muestra salvo que se oculte.
    /// </summary>
    public static Migracion ListaOperadoresAcceso { get; } = new(
        "20261101_0925_LISTA_OPERADORES_ACCESO", 261101001,
        "Configuracion.OcultarListaOperadoresAcceso (sí/no, por defecto no: el acceso muestra la lista de operadores).",
        "seguridad/acceso",
        new AnadirColumna("Configuracion", "OcultarListaOperadoresAcceso", "YESNO NOT NULL DEFAULT No"));
}
