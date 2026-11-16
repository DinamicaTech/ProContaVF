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

    /// <summary>
    /// Búsqueda por importe (norma43/busqueda-por-importe 2026-11-16): la factura de venta guarda el movimiento bancario con que se
    /// cobró, como ya hace FacturasCompra.LineaN43 con el pago. Es la marca «conciliada», distinta de la de exportada (su asiento 430).
    /// </summary>
    public static Migracion BusquedaPorImporte { get; } = new(
        "20261116_1100_BUSQUEDA_IMPORTE_N43", 261116011,
        "Norma 43: movimiento bancario enlazado a cada factura de venta (búsqueda por importe).",
        "norma43/busqueda-por-importe",
        new AnadirColumna("Facturas", "LineaN43", "LONG"),
        new CrearIndice("Facturas", "IX_Facturas_LineaN43", "CREATE INDEX IX_Facturas_LineaN43 ON Facturas (LineaN43)"))
    {
        Descripciones =
        [
            new("Facturas", "LineaN43", "Movimiento bancario (LineasExtractoBancario) con que se cobró la factura, enlazado con «Buscar por importe»: la factura está conciliada. Nulo si no."),
        ],
    };

    /// <summary>
    /// Explosión de remesas y devoluciones de recibos (norma43/remesas 2026-11-16): el extracto ficticio que desglosa una remesa
    /// (o una devolución) guarda la línea de la que sale; la factura guarda la devolución de su primer recibo, y los cobros y
    /// devoluciones de los recibos que se vuelven a girar van a ReintentosSEPA (intento 2, 3…).
    /// </summary>
    public static Migracion Remesas { get; } = new(
        "20261116_1300_REMESAS_N43", 261116013,
        "Norma 43: explosión de remesas y devoluciones de recibos, con los reintentos de cobro.",
        "norma43/remesas",
        new AnadirColumna("ExtractosBancarios", "IdLineaExtractoOrigen", "LONG"),
        new CrearIndice("ExtractosBancarios", "IX_Extractos_LineaOrigen", "CREATE INDEX IX_Extractos_LineaOrigen ON ExtractosBancarios (IdLineaExtractoOrigen)"),
        new AnadirColumna("Facturas", "LineaN43Devolucion", "LONG"),
        new CrearIndice("Facturas", "IX_Facturas_LineaN43Devolucion", "CREATE INDEX IX_Facturas_LineaN43Devolucion ON Facturas (LineaN43Devolucion)"),
        new CrearTabla("ReintentosSEPA",
            "CREATE TABLE ReintentosSEPA (IdReintentoSEPA COUNTER CONSTRAINT PK_ReintentosSEPA PRIMARY KEY, " +
            "IdFactura LONG NOT NULL CONSTRAINT FK_ReintentosSEPA_Factura REFERENCES Facturas (IdFactura), Intento SHORT NOT NULL, " +
            "LineaN43 LONG NOT NULL, LineaN43Devolucion LONG)"),
        new CrearIndice("ReintentosSEPA", "IX_ReintentosSEPA_Factura", "CREATE UNIQUE INDEX IX_ReintentosSEPA_Factura ON ReintentosSEPA (IdFactura, Intento)"))
    {
        Descripciones =
        [
            new("ExtractosBancarios", "IdLineaExtractoOrigen", "Línea (remesa o devolución de recibos) que desglosa este extracto ficticio, una línea por recibo. Nulo en los extractos del banco."),
            new("Facturas", "LineaN43Devolucion", "Movimiento bancario con que se devolvió el recibo de Facturas.LineaN43. Nulo si no se devolvió."),
            new("ReintentosSEPA", "Intento", "Número del intento de cobro: 2 para el primer reintento (el primero es Facturas.LineaN43)."),
            new("ReintentosSEPA", "LineaN43", "Movimiento bancario del nuevo cobro de la factura."),
            new("ReintentosSEPA", "LineaN43Devolucion", "Movimiento bancario con que se devolvió ese cobro. Nulo si no se devolvió."),
        ],
    };
}
