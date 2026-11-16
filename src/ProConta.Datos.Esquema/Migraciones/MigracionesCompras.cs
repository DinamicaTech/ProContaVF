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

    /// <summary>Producto por defecto del proveedor (compras/pantallas 2026-11-06 13:36): el de su última factura guardada.</summary>
    public static Migracion ProductoDefectoProveedor { get; } = new(
        "20261106_1345_PRODUCTO_DEFECTO_PROVEEDOR", 261106004,
        "Tipo de producto por defecto del proveedor (1 servicios, 2 bienes).",
        "compras/factura-compra",
        new AnadirColumna("CuentasProveedoresSociedad", "TipoProductoDefecto", "BYTE"));

    /// <summary>
    /// Textos clave de compras y cuenta de gasto y centro de coste por defecto del proveedor (compras/pantallas
    /// 2026-11-06 17:02): cada empresa guarda textos que, si aparecen en el contenido o en la ruta del PDF, asignan una
    /// cuenta de gasto (por su identidad contable, que vale en todos los ejercicios), un centro de coste o los dos. Si no
    /// coincide ninguno, se usan los del proveedor, que se aprenden al guardar su factura. La migración del ERP trae
    /// PalabrasClaveTipoGasto y PalabrasClaveSubDepartamento.
    /// </summary>
    public static Migracion TextosClaveCompra { get; } = new(
        "20261106_2000_TEXTOS_CLAVE_COMPRA", 261106006,
        "Textos clave de compras por empresa y cuenta de gasto y centro de coste por defecto del proveedor.",
        "compras/factura-compra",
        new CrearTabla("TextosClaveCompra",
            "CREATE TABLE TextosClaveCompra (IdTextoClave COUNTER CONSTRAINT PK_TextosClaveCompra PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_TCC_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Texto TEXT(100) NOT NULL, " +
            "IdIdentidadContable LONG CONSTRAINT FK_TCC_Identidad REFERENCES IdentidadesContables (IdIdentidadContable), " +
            "IdCentroCoste LONG CONSTRAINT FK_TCC_CentroCoste REFERENCES CentrosCoste (IdCentroCoste))"),
        new CrearIndice("TextosClaveCompra", "UX_TCC_SociedadTexto",
            "CREATE UNIQUE INDEX UX_TCC_SociedadTexto ON TextosClaveCompra (IdSociedad, Texto)"),
        new AnadirColumna("CuentasProveedoresSociedad", "IdIdentidadGastoDefecto", "LONG"),
        new AnadirColumna("CuentasProveedoresSociedad", "IdCentroCosteDefecto", "LONG"));

    /// <summary>
    /// Exportación de compras a la contabilidad (compras/factura-compra/contabilizacion, entrega 3): en la factura, el
    /// asiento vigente, el que anula su contra-asiento y el número de lote; en el ejercicio, el tipo de IVA general, la
    /// cuenta de IVA de importación y las de diferencias de redondeo. Solo estructura, sin datos.
    /// </summary>
    public static Migracion ExportacionCompras { get; } = new(
        "20261107_0100_EXPORTACION_COMPRAS", 261107001,
        "Exportación de facturas de compra: asiento y lote de la factura; tipo de IVA general y cuentas de importación y redondeo del ejercicio.",
        "compras/factura-compra",
        new AnadirColumna("FacturasCompra", "IdAsientoContable", "LONG"),
        new AnadirColumna("FacturasCompra", "IdAsientoAnulado", "LONG"),
        new AnadirColumna("FacturasCompra", "GeneracionExportacion", "LONG"),
        new AnadirColumna("SociedadesEjercicios", "TipoIVAGeneral", "SINGLE"),
        new AnadirColumna("SociedadesEjercicios", "CuentaIVAImportacion", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaRedondeoGastos", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaRedondeoIngresos", "TEXT(10)"));

    /// <summary>
    /// Cambios del 7 de noviembre (propietario 2026-11-07 11:59): cuentas de IVA repercutido de ventas por tipo (las
    /// CuentaTipoIVA quedan para el soportado de compras), origen del documento de compra (carpeta o email), concepto
    /// leído de la factura y su versión abreviada, y NIF del proveedor en las plantillas de reconocimiento (pasan a ir por
    /// proveedor, no por carpeta). Solo estructura, sin datos.
    /// </summary>
    public static Migracion ComprasVentasNoviembre { get; } = new(
        "20261107_1300_COMPRAS_VENTAS_NOVIEMBRE", 261107002,
        "IVA repercutido de ventas por tipo, origen y concepto de las facturas de compra y plantillas de reconocimiento por NIF.",
        "compras/factura-compra",
        new AnadirColumna("SociedadesEjercicios", "CuentaIVARepercutido0", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaIVARepercutido1", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaIVARepercutido2", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaIVARepercutido3", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaIVARepercutido4", "TEXT(10)"),
        new AnadirColumna("FacturasCompra", "OrigenDocumento", "TEXT(10)"),
        new AnadirColumna("FacturasCompra", "ConceptoFactura", "TEXT(255)"),
        new AnadirColumna("FacturasCompra", "ConceptoAbreviado", "TEXT(60)"),
        new AnadirColumna("PlantillasReconocimientoCompra", "NIF", "TEXT(20)"),
        new CrearIndice("PlantillasReconocimientoCompra", "IX_PRC_SociedadNif",
            "CREATE INDEX IX_PRC_SociedadNif ON PlantillasReconocimientoCompra (IdSociedad, NIF)"));

    /// <summary>
    /// Concepto abreviado por defecto del proveedor (propietario 2026-11-07 19:21): el que se escribe a mano en una factura
    /// sin concepto leído; gana al abreviado del concepto leído en las facturas siguientes. Solo estructura, sin datos.
    /// </summary>
    public static Migracion ConceptoAbreviadoProveedor { get; } = new(
        "20261107_1925_CONCEPTO_ABREVIADO_PROVEEDOR", 261107003,
        "Concepto abreviado por defecto del proveedor.",
        "compras/factura-compra",
        new AnadirColumna("CuentasProveedoresSociedad", "ConceptoAbreviadoDefecto", "TEXT(60)"));

    /// <summary>
    /// Bien de inversión en el libro de IVA soportado (propietario 2026-11-07 19:55): la compra con cuenta de gasto del grupo 2
    /// (inmovilizado) va en las casillas de bienes de inversión del 303 y se regulariza. Solo estructura, sin datos.
    /// </summary>
    public static Migracion BienInversionIvaSoportado { get; } = new(
        "20261107_2000_BIEN_INVERSION_IVA_SOPORTADO", 261107004,
        "Marca de bien de inversión en el libro de IVA soportado.",
        "contabilidad/procesos",
        new AnadirColumna("IVASoportado", "BienInversion", "YESNO"));

    /// <summary>
    /// Marca VIES del proveedor (propietario 2026-11-07 20:25): «VIES», «NO VIES» o vacía (sin verificar; se consulta al abrir
    /// una factura suya), y cuándo se comprobó. Solo estructura, sin datos.
    /// </summary>
    /// <summary>
    /// Carpeta de facturas de compra de cada ejercicio (compras/factura-compra 2026-11-11 17:33): relativa a la carpeta de
    /// documentos de la empresa (Sociedades.RutaDocumentos), por defecto «&lt;año&gt;\Proveedores», que es donde compras busca los PDF
    /// nuevos y contra la que resuelve las rutas guardadas sin unidad. Se rellena así en los ejercicios que ya existen.
    /// </summary>
    public static Migracion CarpetaFacturasCompra { get; } = new(
        "20261111_1740_CARPETA_FACTURAS_COMPRA", 261111014,
        "Carpeta de facturas de compra de cada ejercicio, relativa a la carpeta de documentos de la empresa.",
        "compras/factura-compra",
        new AnadirColumna("SociedadesEjercicios", "CarpetaFacturasCompra", "TEXT(255)"),
        new PasoPersonalizado("Rellenar la carpeta de facturas de compra de los ejercicios con «<año>\\Proveedores»",
            "UPDATE SociedadesEjercicios SET CarpetaFacturasCompra = Ejercicio & '\\Proveedores' WHERE CarpetaFacturasCompra IS NULL AND Ejercicio IS NOT NULL",
            // Hecho si algún ejercicio la tiene (o no hay ninguno): los ejercicios que se crean después pueden dejarla vacía, que
            // vale «<año>\Proveedores», y no por eso la migración está a medias.
            async (s, e) => e.ExisteColumna("SociedadesEjercicios", "CarpetaFacturasCompra") && (
                System.Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM SociedadesEjercicios WHERE CarpetaFacturasCompra IS NOT NULL"), System.Globalization.CultureInfo.InvariantCulture) > 0 ||
                System.Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM SociedadesEjercicios WHERE Ejercicio IS NOT NULL"), System.Globalization.CultureInfo.InvariantCulture) == 0),
            (s, _) => s.EjecutarAsync("UPDATE SociedadesEjercicios SET CarpetaFacturasCompra = Ejercicio & '\\Proveedores' WHERE CarpetaFacturasCompra IS NULL AND Ejercicio IS NOT NULL")));

    public static Migracion ViesProveedor { get; } = new(
        "20261107_2030_VIES_PROVEEDOR", 261107005,
        "Marca VIES del proveedor y fecha de la comprobación.",
        "terceros/roles",
        new AnadirColumna("Proveedores", "EstadoVies", "TEXT(10)"),
        new AnadirColumna("Proveedores", "FechaVies", "DATETIME"));

    private static PasoPersonalizado Relacion(string tabla, string columna, string nombre)
    {
        var sql = $"ALTER TABLE {tabla} ADD CONSTRAINT {nombre} FOREIGN KEY ({columna}) REFERENCES FormasPago (Id)";
        return new PasoPersonalizado($"Relacionar {tabla}.{columna} con FormasPago", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, tabla, nombre)),
            (s, _) => s.EjecutarAsync(sql));
    }
}
