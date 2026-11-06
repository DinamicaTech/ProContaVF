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

    /// <summary>
    /// Centro de coste de la factura de compra (compras/pantallas, entrega 2): IdCentroCoste apunta a CentrosCoste. La
    /// columna heredada IdSubDepartamento guarda el código del ERP y la convierte la migración del ERP, como en las líneas
    /// de las facturas de venta.
    /// </summary>
    public static Migracion FacturasCompraCentroCoste { get; } = new(
        "20261105_1830_FACTURAS_COMPRA_CENTRO_COSTE", 261105003,
        "Facturas de compra: centro de coste.",
        "compras/factura-compra",
        new AnadirColumna("FacturasCompra", "IdCentroCoste", "LONG"),
        new CrearIndice("FacturasCompra", "IX_FacturasCompra_CentroCoste",
            "CREATE INDEX IX_FacturasCompra_CentroCoste ON FacturasCompra (IdCentroCoste)"));

    /// <summary>
    /// Plantillas de reconocimiento por proveedor (compras/reconocimiento 2026-11-05 18:42): para cada empresa, carpeta del
    /// proveedor y campo (nif, numero, fecha, total), el rótulo que precede al valor, su posición y cuántas palabras ocupa.
    /// Se aprenden al guardar una factura revisada; un proveedor puede tener varias por campo, una por formato de factura.
    /// </summary>
    public static Migracion PlantillasReconocimientoCompra { get; } = new(
        "20261105_1900_PLANTILLAS_RECONOCIMIENTO_COMPRA", 261105004,
        "Facturas de compra: plantillas de reconocimiento aprendidas por proveedor.",
        "compras/reconocimiento",
        new CrearTabla("PlantillasReconocimientoCompra",
            "CREATE TABLE PlantillasReconocimientoCompra (IdPlantilla COUNTER CONSTRAINT PK_PlantillasReconocimientoCompra PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_PRC_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Carpeta TEXT(255) NOT NULL, Campo TEXT(10) NOT NULL, Pagina SHORT NOT NULL, Ancla TEXT(120) NOT NULL, Posicion TEXT(10) NOT NULL, " +
            "Palabras SHORT NOT NULL, X DOUBLE NOT NULL, Y DOUBLE NOT NULL, DesplazamientoX DOUBLE NOT NULL, Aciertos LONG NOT NULL, " +
            "FechaAlta DATETIME NOT NULL, FechaUso DATETIME NOT NULL)"),
        new CrearIndice("PlantillasReconocimientoCompra", "IX_PRC_SociedadCarpeta",
            "CREATE INDEX IX_PRC_SociedadCarpeta ON PlantillasReconocimientoCompra (IdSociedad, Carpeta)"));
    /// <summary>
    /// Forma de pago de la factura de compra y la del proveedor por defecto (compras/pantallas 2026-11-06): la factura
    /// apunta a FormasPago (su subcuenta sale de FormasPagoSociedad) y la cuenta del proveedor en la empresa guarda la
    /// última que se le asignó, que se propone en sus facturas nuevas. CuentaFormaPago queda como dato heredado del ERP.
    /// </summary>
    public static Migracion FormaPagoCompras { get; } = new(
        "20261106_1310_FORMA_PAGO_COMPRAS", 261106002,
        "Facturas de compra: forma de pago y forma de pago por defecto del proveedor.",
        "compras/factura-compra",
        new AnadirColumna("FacturasCompra", "IdFormaPago", "BYTE"),
        Relacion("FacturasCompra", "IdFormaPago", "FK_FacturasCompra_FormaPago"),
        new AnadirColumna("CuentasProveedoresSociedad", "IdFormaPagoDefecto", "BYTE"),
        Relacion("CuentasProveedoresSociedad", "IdFormaPagoDefecto", "FK_CPS_FormaPagoDefecto"));

    private static PasoPersonalizado Relacion(string tabla, string columna, string nombre)
    {
        var sql = $"ALTER TABLE {tabla} ADD CONSTRAINT {nombre} FOREIGN KEY ({columna}) REFERENCES FormasPago (Id)";
        return new PasoPersonalizado($"Relacionar {tabla}.{columna} con FormasPago", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, tabla, nombre)),
            (s, _) => s.EjecutarAsync(sql));
    }
}
