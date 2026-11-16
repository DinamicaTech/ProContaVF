namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por pantalla-de-inicio.</summary>
public static class MigracionesInicio
{
    /// <summary>
    /// Histórico de la salud (pantalla-de-inicio 2026-11-15 16:37): el recuento diario de cada elemento de «Salud de la contabilidad»
    /// (por sociedad, ejercicio de trabajo, día y clave), para mostrar en cada aviso la variación desde el lunes. Una fila por día y
    /// elemento: la última revisión del día sustituye a las anteriores. Solo crea la tabla; es lo único que Inicio escribe. Sin
    /// relación con Sociedades: Access no admite más índices en esa tabla.
    /// </summary>
    public static Migracion HistoricoSalud { get; } = new(
        "20261115_1640_HISTORICO_SALUD", 261115010,
        "Histórico diario del recuento de la salud de la contabilidad, para la tendencia «desde el lunes» de Inicio.",
        "pantalla-de-inicio",
        new CrearTabla("HistoricoSalud",
            "CREATE TABLE HistoricoSalud (IdHistoricoSalud COUNTER CONSTRAINT PK_HistoricoSalud PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, Ejercicio SHORT NOT NULL, Fecha DATETIME NOT NULL, " +
            "ClaveRevision TEXT(60) NOT NULL, ClaveElemento TEXT(120) NOT NULL, Elementos LONG NOT NULL, Importe CURRENCY, " +
            "FechaHoraRegistro DATETIME NOT NULL)"),
        new CrearIndice("HistoricoSalud", "UX_HistoricoSalud_Dia",
            "CREATE UNIQUE INDEX UX_HistoricoSalud_Dia ON HistoricoSalud (IdSociedad, Ejercicio, Fecha, ClaveElemento)"))
    {
        Descripciones =
        [
            new("HistoricoSalud", null, "Recuento diario de la salud de la contabilidad de Inicio: una fila por día y elemento (la última revisión del día sustituye). Sirve para la tendencia «desde el lunes»."),
            new("HistoricoSalud", "IdHistoricoSalud", "Identificador de la fila."),
            new("HistoricoSalud", "IdSociedad", "Sociedad revisada."),
            new("HistoricoSalud", "Ejercicio", "Ejercicio de trabajo con el que se hizo la revisión."),
            new("HistoricoSalud", "Fecha", "Día de la revisión (sin hora)."),
            new("HistoricoSalud", "ClaveRevision", "Revisión que encontró el elemento (por ejemplo, bancos-sin-asiento)."),
            new("HistoricoSalud", "ClaveElemento", "Clave del elemento: la revisión con su detalle (por ejemplo, el banco o el trimestre)."),
            new("HistoricoSalud", "Elementos", "Número de cosas que cuenta el elemento ese día (movimientos, facturas, subcuentas…); 1 si no cuenta nada."),
            new("HistoricoSalud", "Importe", "Importe asociado al elemento, si lo tiene."),
            new("HistoricoSalud", "FechaHoraRegistro", "Cuándo se guardó la fila (la última revisión del día)."),
        ],
    };
}
