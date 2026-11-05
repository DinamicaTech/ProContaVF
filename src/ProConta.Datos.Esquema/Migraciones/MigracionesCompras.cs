namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de compras (docs/compras.md).</summary>
public static class MigracionesCompras
{
    /// <summary>
    /// Estructura nueva de las facturas de compra (compras/factura-compra 2026-11-05 11:24): sociedad-ejercicio explícito,
    /// proveedor, desgloses de IVA en la tabla hija DesglosesFacturaCompra (en lugar de las columnas fijas ImporteBI0-4 y
    /// TipoIVA1-4, que quedan como dato heredado), retención, moneda e importe original con el cambio aplicado y su fecha,
    /// factura rectificada, confianza, «requiere revisión» y log del reconocimiento, y la configuración del reconocimiento
    /// por empresa (ConfiguracionReconocimientoFacturasCompra). Solo crea estructura: las facturas antiguas se trasladan
    /// al formato nuevo en la migración del ERP (implantacion/migracion-erp).
    /// </summary>
    public static Migracion FacturasCompraReconocimiento { get; } = new(
        "20261105_1500_FACTURAS_COMPRA_RECONOCIMIENTO", 261105002,
        "Facturas de compra: sociedad-ejercicio, proveedor, desgloses de IVA en tabla hija, moneda, rectificada, reconocimiento y su configuración por empresa.",
        "compras/factura-compra",
        new AnadirColumna("FacturasCompra", "IdSociedadEjercicio", "LONG"),
        new AnadirColumna("FacturasCompra", "IdProveedor", "LONG"),
        new AmpliarTexto("FacturasCompra", "CuentaContableProveedor", 30),
        new AmpliarTexto("FacturasCompra", "Carpeta", 255),
        new AmpliarTexto("FacturasCompra", "NombreFichero", 255),
        new AnadirColumna("FacturasCompra", "ImporteRetencion", "CURRENCY"),
        new AnadirColumna("FacturasCompra", "Divisa", "TEXT(3)"),
        new AnadirColumna("FacturasCompra", "ImporteTotalDivisa", "CURRENCY"),
        new AnadirColumna("FacturasCompra", "TipoCambioAplicado", "DECIMAL(18,6)"),
        new AnadirColumna("FacturasCompra", "FechaTipoCambio", "DATETIME"),
        new AnadirColumna("FacturasCompra", "IdRegistroFacturaRectificada", "LONG"),
        new AnadirColumna("FacturasCompra", "NombreProveedorAeat", "TEXT(150)"),
        new AnadirColumna("FacturasCompra", "ConfianzaImportacion", "DECIMAL(5,4)"),
        new AnadirColumnaObligatoria("FacturasCompra", "RequiereRevisionImportacion", "YESNO", "False"),
        new AnadirColumna("FacturasCompra", "FuenteImportacion", "TEXT(10)"),
        new AnadirColumna("FacturasCompra", "LOGImportacion", "LONGTEXT"),
        new AnadirColumnaObligatoria("FacturasCompra", "Version", "LONG", "0"),
        new CrearIndice("FacturasCompra", "UX_FacturasCompra_IdRegistro",
            "CREATE UNIQUE INDEX UX_FacturasCompra_IdRegistro ON FacturasCompra (IdRegistro)"),
        // No es único: el ERP tiene algún PDF repetido marcado como Duplicada. El control lo hace la inserción.
        new CrearIndice("FacturasCompra", "IX_FacturasCompra_MD5", "CREATE INDEX IX_FacturasCompra_MD5 ON FacturasCompra (MD5)"),
        new CrearIndice("FacturasCompra", "IX_FacturasCompra_SociedadEjercicio",
            "CREATE INDEX IX_FacturasCompra_SociedadEjercicio ON FacturasCompra (IdSociedadEjercicio)"),
        new CrearIndice("FacturasCompra", "IX_FacturasCompra_Proveedor",
            "CREATE INDEX IX_FacturasCompra_Proveedor ON FacturasCompra (IdProveedor)"),
        new CrearTabla("DesglosesFacturaCompra",
            "CREATE TABLE DesglosesFacturaCompra (IdDesgloseFacturaCompra COUNTER CONSTRAINT PK_DesglosesFacturaCompra PRIMARY KEY, " +
            "IdRegistro LONG NOT NULL CONSTRAINT FK_DFC_FacturaCompra REFERENCES FacturasCompra (IdRegistro), " +
            "Orden SHORT NOT NULL, TipoIVA DECIMAL(8,4) NOT NULL, BaseImponible CURRENCY NOT NULL, CuotaIVA CURRENCY NOT NULL, " +
            "Confianza DECIMAL(5,4), Evidencia TEXT(255))"),
        new CrearIndice("DesglosesFacturaCompra", "UX_DFC_FacturaOrden",
            "CREATE UNIQUE INDEX UX_DFC_FacturaOrden ON DesglosesFacturaCompra (IdRegistro, Orden)"),
        new CrearTabla("ConfiguracionReconocimientoFacturasCompra",
            "CREATE TABLE ConfiguracionReconocimientoFacturasCompra (IdConfiguracion COUNTER CONSTRAINT PK_ConfiguracionReconocimientoFC PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_CRFC_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Activo YESNO NOT NULL, SociedadTieneVies YESNO NOT NULL, UsarTextract YESNO NOT NULL, RegionAWS TEXT(30), " +
            "ClaveAccesoAWS TEXT(128), SecretoAWSProtegido LONGTEXT, UmbralAceptacion DECIMAL(5,4) NOT NULL, " +
            "UmbralDiscrepanciaImporte CURRENCY NOT NULL, Version LONG NOT NULL)"),
        new CrearIndice("ConfiguracionReconocimientoFacturasCompra", "UX_CRFC_Sociedad",
            "CREATE UNIQUE INDEX UX_CRFC_Sociedad ON ConfiguracionReconocimientoFacturasCompra (IdSociedad)"));
}
