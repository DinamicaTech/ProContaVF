namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de terceros (docs/datos/esquema.md, Requirements).</summary>
public static class MigracionesTerceros
{
    /// <summary>Versión de fila de la ficha del tercero, para la concurrencia optimista (las filas existentes empiezan en 0).</summary>
    public static Migracion VersionTercero { get; } = new(
        "20261031_2005_TERCEROS_VERSION", 261031003,
        "Terceros.Version (versión de fila para concurrencia optimista).",
        "terceros/tercero",
        new AnadirColumnaObligatoria("Terceros", "Version", "LONG", "0"));
}
