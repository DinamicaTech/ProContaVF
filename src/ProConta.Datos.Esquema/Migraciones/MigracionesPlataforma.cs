namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de plataforma (docs/plataforma.md).</summary>
public static class MigracionesPlataforma
{
    /// <summary>
    /// Entorno de pruebas (plataforma/entorno-pruebas 2026-11-16): la marca que dice que esta BD es una copia de pruebas
    /// viaja dentro del propio fichero, no en su ruta. La pone solo el refresco de la copia de pruebas, junto con la fecha
    /// de la copia y la BD de producción de la que sale; una BD de producción nunca la tiene.
    /// </summary>
    public static Migracion EntornoPruebas { get; } = new(
        "20261116_1400_ENTORNO_PRUEBAS", 261116014,
        "Entorno de pruebas: marca de BD de pruebas con la fecha y el origen de la copia de producción.",
        "plataforma/entorno-pruebas",
        new AnadirColumnaObligatoria("Configuracion", "EntornoPruebas", "YESNO", "No"),
        new AnadirColumna("Configuracion", "CopiaProduccionFecha", "DATETIME"),
        new AnadirColumna("Configuracion", "CopiaProduccionOrigen", "TEXT(255)"))
    {
        Descripciones =
        [
            new("Configuracion", "EntornoPruebas", "Sí si esta BD es la copia de pruebas (la pone el refresco desde producción). En producción, siempre No."),
            new("Configuracion", "CopiaProduccionFecha", "Fecha y hora en que se copió de producción esta BD de pruebas. Nulo en producción."),
            new("Configuracion", "CopiaProduccionOrigen", "Ruta de la BD de producción de la que sale esta copia de pruebas. Nulo en producción."),
        ],
    };
}
