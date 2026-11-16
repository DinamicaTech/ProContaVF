namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de implantación (docs/implantacion.md).</summary>
public static class MigracionesImplantacion
{
    /// <summary>
    /// Migración del ERP (implantacion/migracion-erp 2026-11-11): una fila por ejecución (MigracionesERP), sus incidencias
    /// (claves foráneas puestas a nulo con su clave original, registros excluidos con su motivo y avisos) y los mapas de
    /// identidad (tabla y clave del ERP → Id de ProContaVF), únicos por ejecución, tipo y clave, como las tablas del
    /// importador de ContaSol. Repetir la migración lee los mapas de la ejecución anterior para no duplicar.
    /// </summary>
    public static Migracion MigracionErp { get; } = new(
        "20261111_1500_MIGRACION_ERP", 261111012,
        "Migración del ERP: ejecuciones, incidencias y mapas de identidad.",
        "implantacion/migracion-erp",
        new CrearTabla("MigracionesERP",
            "CREATE TABLE MigracionesERP (IdMigracionERP COUNTER CONSTRAINT PK_MigracionesERP PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_MigracionesERP_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "RutaOrigen TEXT(255) NOT NULL, HuellaOrigen TEXT(64) NOT NULL, RutaDirecciones TEXT(255), HuellaDirecciones TEXT(64), " +
            "VersionMigrador TEXT(10) NOT NULL, FechaHoraInicio DATETIME NOT NULL, FechaHoraFin DATETIME, Operador TEXT(100), Estado TEXT(20) NOT NULL, Resumen LONGTEXT)"),
        new CrearTabla("IncidenciasMigracionERP",
            "CREATE TABLE IncidenciasMigracionERP (IdIncidenciaMigracionERP COUNTER CONSTRAINT PK_IncidenciasMigracionERP PRIMARY KEY, " +
            "IdMigracionERP LONG NOT NULL CONSTRAINT FK_IncidenciasMigracionERP_Migracion REFERENCES MigracionesERP (IdMigracionERP), " +
            "Fase TEXT(50), TablaOrigen TEXT(100), ClaveOrigen TEXT(150), CampoOrigen TEXT(100), Codigo TEXT(50) NOT NULL, Severidad TEXT(15) NOT NULL, " +
            "Explicacion LONGTEXT NOT NULL, ValorOriginal LONGTEXT, Estado TEXT(15) NOT NULL)"),
        new CrearIndice("IncidenciasMigracionERP", "IX_IncidenciasMigracionERP_Migracion",
            "CREATE INDEX IX_IncidenciasMigracionERP_Migracion ON IncidenciasMigracionERP (IdMigracionERP, Codigo)"),
        new CrearTabla("MapasIdentidadERP",
            "CREATE TABLE MapasIdentidadERP (IdMapaIdentidadERP COUNTER CONSTRAINT PK_MapasIdentidadERP PRIMARY KEY, " +
            "IdMigracionERP LONG NOT NULL CONSTRAINT FK_MapasIdentidadERP_Migracion REFERENCES MigracionesERP (IdMigracionERP), " +
            "TipoEntidad TEXT(30) NOT NULL, ClaveOrigen TEXT(150) NOT NULL, IdDestino LONG NOT NULL)"),
        new CrearIndice("MapasIdentidadERP", "UX_MapasIdentidadERP_Clave",
            "CREATE UNIQUE INDEX UX_MapasIdentidadERP_Clave ON MapasIdentidadERP (IdMigracionERP, TipoEntidad, ClaveOrigen)"));
}

/// <summary>Migraciones pedidas por el nodo norma43 (docs/norma43.md).</summary>
public static class MigracionesNorma43
{
    /// <summary>
    /// Palabras clave de los extractos bancarios (norma43, derivado de implantacion/migracion-erp 2026-11-11): cada regla dice
    /// a qué cuenta va un movimiento de una cuenta bancaria (o de todas) y sentido cuyo concepto contiene la palabra clave y, si
    /// la hay, también la segunda (Y lógico); Especial 5 es una remesa de recibos que se explota. La imputación de una línea
    /// guarda la palabra clave que la asignó.
    /// </summary>
    public static Migracion PalabrasClave { get; } = new(
        "20261111_1510_PALABRAS_CLAVE_N43", 261111013,
        "Norma 43: palabras clave de asignación de cuentas y palabra clave de cada imputación.",
        "norma43",
        new CrearTabla("PalabrasClaveBancarias",
            "CREATE TABLE PalabrasClaveBancarias (IdPalabraClaveBancaria COUNTER CONSTRAINT PK_PalabrasClaveBancarias PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_PalabrasClaveBancarias_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Entidad TEXT(4), Sentido TEXT(1), PalabraClave TEXT(255) NOT NULL, PalabraClave2 TEXT(255), " +
            "IdIdentidadContable LONG CONSTRAINT FK_PalabrasClaveBancarias_Identidad REFERENCES IdentidadesContables (IdIdentidadContable), " +
            "Especial LONG, Activa YESNO NOT NULL)"),
        new CrearIndice("PalabrasClaveBancarias", "IX_PalabrasClaveBancarias_Sociedad",
            "CREATE INDEX IX_PalabrasClaveBancarias_Sociedad ON PalabrasClaveBancarias (IdSociedad, Entidad)"),
        new AnadirColumna("ImputacionesLineaExtractoBancario", "IdPalabraClaveBancaria", "LONG"));

    /// <summary>
    /// Reversión auditada de los lotes de extractos (norma43 2026-11-12): fecha y hora, operador y motivo, nulos mientras el
    /// lote no se haya revertido (definición de la maestra de referencia, manifiesto 20260929_007). El índice por cuenta y
    /// fecha de los extractos acelera la detección de movimientos ya importados.
    /// </summary>
    public static Migracion ReversionLotes { get; } = new(
        "20261112_1700_REVERSION_LOTES_N43", 261112010,
        "Norma 43: reversión auditada de los lotes importados y búsqueda de extractos por cuenta y fecha.",
        "norma43",
        new AnadirColumna("LotesImportacionBancaria", "FechaHoraReversion", "DATETIME"),
        new AnadirColumna("LotesImportacionBancaria", "OperadorReversion", "TEXT(100)"),
        new AnadirColumna("LotesImportacionBancaria", "MotivoReversion", "LONGTEXT"),
        new CrearIndice("ExtractosBancarios", "IX_Extractos_CuentaFechas",
            "CREATE INDEX IX_Extractos_CuentaFechas ON ExtractosBancarios (IdCuentaBancaria, FechaInicio, FechaFin)"));
}
