using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Amplía una columna de texto si es más corta que la longitud indicada (no la estrecha nunca).</summary>
public sealed class AmpliarTexto(string tabla, string columna, int longitud) : PasoMigracion
{
    public override string Descripcion => $"Ampliar {tabla}.{columna} a {longitud} caracteres";
    public override string Firma => $"ALTER TABLE [{tabla}] ALTER COLUMN [{columna}] TEXT({longitud})";
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.Tabla(tabla)?.Columna(columna) is { Longitud: int l } && l >= longitud);
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => sesion.EjecutarAsync(Firma);
}

/// <summary>Migraciones pedidas por los nodos de facturación, terceros/roles y seguridad/permisos para la facturación de venta.</summary>
public static class MigracionesFacturacion
{
    /// <summary>
    /// Conceptos por sociedad (facturacion/conceptos 2026-10-31 16:54): Conceptos.IdSociedad (la sociedad de su
    /// ejercicio) y Conceptos.IdIdentidadContable (la identidad de su cuenta de venta en ese ejercicio, que da la
    /// continuidad entre ejercicios aunque cambie el código). IdSociedadEjercicio se conserva como el ejercicio en
    /// que se creó el concepto y CuentaContable (7 caracteres) queda como dato heredado: el programa ya no la usa.
    /// Se conservan los IdConcepto, así que las líneas antiguas siguen apuntando bien.
    /// </summary>
    public static Migracion ConceptosSociedad { get; } = new(
        "20261101_1630_CONCEPTOS_SOCIEDAD", 261101004,
        "Conceptos por sociedad con la cuenta de venta como identidad contable.",
        "facturacion/conceptos",
        new AnadirColumna("Conceptos", "IdSociedad", "TEXT(3)"),
        new AnadirColumna("Conceptos", "IdIdentidadContable", "LONG"),
        new PasoPersonalizado(
            "Poner a cada concepto la sociedad de su ejercicio",
            "UPDATE Conceptos INNER JOIN SociedadesEjercicios ON Conceptos.IdSociedadEjercicio = SociedadesEjercicios.IdSociedadEjercicio SET Conceptos.IdSociedad = SociedadesEjercicios.IdSociedad WHERE Conceptos.IdSociedad IS NULL",
            async (s, _) => Convert.ToInt32(await s.EscalarAsync(
                "SELECT COUNT(*) FROM Conceptos AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE c.IdSociedad IS NULL AND se.IdSociedad IS NOT NULL")) == 0,
            (s, _) => s.EjecutarAsync(
                "UPDATE Conceptos INNER JOIN SociedadesEjercicios ON Conceptos.IdSociedadEjercicio = SociedadesEjercicios.IdSociedadEjercicio SET Conceptos.IdSociedad = SociedadesEjercicios.IdSociedad WHERE Conceptos.IdSociedad IS NULL")),
        new PasoPersonalizado(
            "Poner a cada concepto la identidad contable de su cuenta de venta",
            "UPDATE Conceptos SET IdIdentidadContable = (identidad de la cuenta CuentaContable en su ejercicio) WHERE IdIdentidadContable IS NULL",
            async (s, _) => (await PendientesIdentidadAsync(s)).Count == 0,
            async (s, _) =>
            {
                foreach (var (idConcepto, identidad) in await PendientesIdentidadAsync(s))
                    await s.EjecutarAsync("UPDATE Conceptos SET IdIdentidadContable = ? WHERE IdConcepto = ?", identidad, idConcepto);
            }),
        new CrearIndice("Conceptos", "IX_Conceptos_Sociedad", "CREATE INDEX IX_Conceptos_Sociedad ON Conceptos (IdSociedad)"));

    /// <summary>Conceptos sin identidad cuya cuenta existe en su ejercicio, con la identidad que les corresponde.</summary>
    private static Task<IReadOnlyList<(int IdConcepto, int Identidad)>> PendientesIdentidadAsync(ISesionDatos s) =>
        s.ConsultarAsync(
            "SELECT c.IdConcepto, cc.IdIdentidadContable FROM Conceptos AS c INNER JOIN CuentasContables AS cc " +
            "ON (c.IdSociedadEjercicio = cc.IdSociedadEjercicio AND c.CuentaContable = cc.Codigo) WHERE c.IdIdentidadContable IS NULL",
            f => (f.Entero("IdConcepto"), f.Entero("IdIdentidadContable")));

    /// <summary>Conceptos con el mismo nombre en una misma sociedad (venían de ejercicios distintos), para decidir si se unen.</summary>
    public static Task<IReadOnlyList<(string IdSociedad, string Nombre, int Veces)>> ConceptosRepetidosAsync(ISesionDatos s) =>
        s.ConsultarAsync(
            "SELECT IdSociedad, Nombre, COUNT(*) AS Veces FROM Conceptos GROUP BY IdSociedad, Nombre HAVING COUNT(*) > 1 ORDER BY IdSociedad, Nombre",
            f => (f.Texto("IdSociedad") ?? "", f.Texto("Nombre") ?? "", f.Entero("Veces")));

    /// <summary>
    /// Factura de venta (facturacion/factura-venta): instantánea del domicilio fiscal y el email del cliente al validar
    /// (definición de la referencia 0.24, con el email a 254 como Terceros.Email), marca de validación con el paso
    /// provisional «sin VeriFactu» y la cuenta de las líneas a 30 caracteres, como los códigos de cuenta.
    /// </summary>
    public static Migracion FacturasInstantanea { get; } = new(
        "20261101_1631_FACTURAS_INSTANTANEA", 261101005,
        "Domicilio y email del cliente congelados en Facturas, marca «sin VeriFactu», versión de fila y cuenta de las líneas a 30 caracteres.",
        "facturacion/factura-venta",
        new AnadirColumna("Facturas", "DireccionCliente", "TEXT(255)"),
        new AnadirColumna("Facturas", "CodigoPostalCliente", "TEXT(20)"),
        new AnadirColumna("Facturas", "PoblacionCliente", "TEXT(100)"),
        new AnadirColumna("Facturas", "ProvinciaCliente", "TEXT(100)"),
        new AnadirColumna("Facturas", "EmailCliente", "TEXT(254)"),
        new AnadirColumnaObligatoria("Facturas", "SinVeriFactu", "YESNO", "False"),
        new AnadirColumnaObligatoria("Facturas", "Version", "LONG", "0"),
        new AmpliarTexto("LineasFactura", "CuentaContable", 30));

    /// <summary>
    /// Cuenta de retención del cliente por sociedad (terceros/roles 2026-11-01 16:18): CuentasClientesSociedad pasa a
    /// tener una clase (CLIENTE o RETENCION), como las cuentas del proveedor; las filas existentes son CLIENTE y el
    /// índice único pasa a ser por cliente, sociedad y clase.
    /// </summary>
    public static Migracion CuentaRetencionCliente { get; } = new(
        "20261101_1632_CUENTA_RETENCION_CLIENTE", 261101006,
        "Clase de cuenta (CLIENTE o RETENCION) en CuentasClientesSociedad para la cuenta de retención del cliente.",
        "terceros/roles",
        new AnadirColumnaObligatoria("CuentasClientesSociedad", "Clase", "TEXT(10)", "'CLIENTE'"),
        new PasoPersonalizado(
            "Quitar el índice único UX_CCS_ClienteSociedad (cliente y sociedad)",
            "DROP INDEX UX_CCS_ClienteSociedad ON CuentasClientesSociedad",
            (_, e) => Task.FromResult(!e.ExisteIndice("CuentasClientesSociedad", "UX_CCS_ClienteSociedad")),
            (s, _) => s.EjecutarAsync("DROP INDEX UX_CCS_ClienteSociedad ON CuentasClientesSociedad")),
        new CrearIndice("CuentasClientesSociedad", "UX_CCS_ClienteSociedadClase",
            "CREATE UNIQUE INDEX UX_CCS_ClienteSociedadClase ON CuentasClientesSociedad (IdCliente, IdSociedad, Clase)"))
    {
        // El índice por cliente y sociedad de 20261101_1000_CUENTAS_ROLES_SOCIEDAD se sustituye por este.
        Retira = [("20261101_1000_CUENTAS_ROLES_SOCIEDAD", "Crear el índice UX_CCS_ClienteSociedad de CuentasClientesSociedad")],
    };

    /// <summary>
    /// Permisos de acciones por grupo (seguridad/permisos, regla común): una acción sin filas la puede hacer cualquier
    /// operador; con alguna fila, solo los grupos que la tienen. Primeras acciones: las de facturas y el mantenimiento.
    /// </summary>
    public static Migracion PermisosAcciones { get; } = new(
        "20261101_1633_PERMISOS_ACCIONES", 261101007,
        "Tabla PermisosAccionesGrupo (acción y grupo) para autorizar acciones en los servicios.",
        "seguridad/permisos",
        new CrearTabla("PermisosAccionesGrupo",
            "CREATE TABLE PermisosAccionesGrupo (IdPermisoAccion COUNTER CONSTRAINT PK_PermisosAccionesGrupo PRIMARY KEY, " +
            "CodigoAccion TEXT(60) NOT NULL, " +
            "IdGrupoOperadores LONG NOT NULL CONSTRAINT FK_PermisosAcciones_Grupo REFERENCES GruposOperadores (IdGrupoOperadores))"),
        new CrearIndice("PermisosAccionesGrupo", "UX_PermisosAcciones_AccionGrupo",
            "CREATE UNIQUE INDEX UX_PermisosAcciones_AccionGrupo ON PermisosAccionesGrupo (CodigoAccion, IdGrupoOperadores)"));

    /// <summary>Motivo de la rectificación en la rectificativa (facturacion/factura-venta 2026-11-02).</summary>
    public static Migracion MotivoRectificacion { get; } = new(
        "20261102_1300_MOTIVO_RECTIFICACION", 261102001,
        "Motivo de la rectificación en Facturas (obligatorio en las rectificativas nuevas).",
        "facturacion/factura-venta",
        new AnadirColumna("Facturas", "MotivoRectificacion", "TEXT(255)"));

    /// <summary>
    /// Facturación programada (facturacion/programadas 2026-11-04): periodo de la primera factura (avanza en cada
    /// generada), número de repeticiones como final alternativo a FechaFin, facturas ya generadas, qué se genera
    /// (BORRADOR o VALIDAR), versión de fila, más cadencias (BIMESTRAL, CUATRIMESTRAL) y la cuenta de las líneas a 30
    /// caracteres, como en LineasFactura. Solo añade y amplía columnas: no cambia los datos que ya hay.
    /// </summary>
    public static Migracion FacturasProgramadas { get; } = new(
        "20261104_1820_FACTURAS_PROGRAMADAS", 261104001,
        "Programación de las facturas programadas: periodo, repeticiones, facturas generadas, qué se genera, versión y más cadencias.",
        "facturacion/programadas",
        new AnadirColumna("FacturasProgramadas", "PeriodoDesde", "DATETIME"),
        new AnadirColumna("FacturasProgramadas", "PeriodoHasta", "DATETIME"),
        new AnadirColumna("FacturasProgramadas", "NumeroRepeticiones", "LONG"),
        new AnadirColumnaObligatoria("FacturasProgramadas", "FacturasGeneradas", "LONG", "0"),
        new AnadirColumnaObligatoria("FacturasProgramadas", "AlGenerar", "TEXT(10)", "'BORRADOR'"),
        new AnadirColumnaObligatoria("FacturasProgramadas", "Version", "LONG", "0"),
        new PasoPersonalizado(
            "Rehacer FacturasProgramadas.Periodicidad a 15 caracteres sin la regla que solo admitía cinco cadencias",
            "FacturasProgramadas.Periodicidad TEXT(15) NOT NULL DEFAULT 'MENSUAL' (copia los valores por PeriodicidadAnterior)",
            (_, e) => Task.FromResult(e.Tabla("FacturasProgramadas")?.Columna("Periodicidad") is { Longitud: >= 15 } && !e.ExisteColumna("FacturasProgramadas", "PeriodicidadAnterior")),
            RehacerPeriodicidadAsync),
        new AmpliarTexto("LineasFacturasProgramadas", "CuentaContable", 30));

    /// <summary>
    /// La columna heredada tiene una regla de validación de Access que solo admite MENSUAL, TRIMESTRAL, SEMESTRAL, ANUAL y
    /// PUNTUAL, y el SQL de Access no puede quitarla: se rehace la columna (los valores se conservan tal cual) para
    /// admitir también BIMESTRAL y CUATRIMESTRAL. Se puede repetir si se interrumpe: los valores esperan en
    /// PeriodicidadAnterior hasta el final.
    /// </summary>
    private static async Task RehacerPeriodicidadAsync(ISesionDatos s, ContextoMigracion _)
    {
        const string tabla = "FacturasProgramadas";
        var esquema = s.LeerEsquema();
        if (!esquema.ExisteColumna(tabla, "PeriodicidadAnterior"))
        {
            await s.EjecutarAsync($"ALTER TABLE [{tabla}] ADD COLUMN [PeriodicidadAnterior] TEXT(15)");
            await s.EjecutarAsync($"UPDATE [{tabla}] SET [PeriodicidadAnterior] = [Periodicidad]");
        }
        if (s.LeerEsquema().ExisteColumna(tabla, "Periodicidad"))
            await s.EjecutarAsync($"ALTER TABLE [{tabla}] DROP COLUMN [Periodicidad]");
        await s.EjecutarAsync($"ALTER TABLE [{tabla}] ADD COLUMN [Periodicidad] TEXT(15) DEFAULT 'MENSUAL'");
        await s.EjecutarAsync($"UPDATE [{tabla}] SET [Periodicidad] = IIf([PeriodicidadAnterior] Is Null, 'MENSUAL', [PeriodicidadAnterior])");
        await s.EjecutarAsync($"ALTER TABLE [{tabla}] ALTER COLUMN [Periodicidad] TEXT(15) NOT NULL DEFAULT 'MENSUAL'");
        await s.EjecutarAsync($"ALTER TABLE [{tabla}] DROP COLUMN [PeriodicidadAnterior]");
    }

    /// <summary>
    /// Facturar sin VeriFactu y PDF (facturacion/verifactu, facturacion/factura-venta 2026-11-05): parámetro general
    /// Configuracion.VeriFactuActivo, por defecto no; domicilio fiscal y carpeta de documentos de cada empresa, y domicilio
    /// del emisor congelado en la factura al validar. Solo añade columnas.
    /// </summary>
    public static Migracion FacturasSinVeriFactu { get; } = new(
        "20261105_1100_FACTURAS_SIN_VERIFACTU", 261105001,
        "Parámetro para activar VeriFactu, domicilio fiscal y carpeta de documentos de la empresa, y domicilio del emisor en la factura.",
        "facturacion/verifactu",
        new AnadirColumna("Configuracion", "VeriFactuActivo", "YESNO NOT NULL DEFAULT No"),
        new AnadirColumna("Sociedades", "DireccionFiscal", "TEXT(255)"),
        new AnadirColumna("Sociedades", "CodigoPostalFiscal", "TEXT(20)"),
        new AnadirColumna("Sociedades", "PoblacionFiscal", "TEXT(100)"),
        new AnadirColumna("Sociedades", "ProvinciaFiscal", "TEXT(100)"),
        new AnadirColumna("Sociedades", "RutaDocumentos", "TEXT(255)"),
        new AnadirColumna("Facturas", "DireccionEmisor", "TEXT(255)"),
        new AnadirColumna("Facturas", "CodigoPostalEmisor", "TEXT(20)"),
        new AnadirColumna("Facturas", "PoblacionEmisor", "TEXT(100)"),
        new AnadirColumna("Facturas", "ProvinciaEmisor", "TEXT(100)"));

    /// <summary>
    /// Deshacer la exportación contable de las ventas (facturacion/factura-venta/contabilizacion 2026-11-08): en la factura,
    /// el asiento que anula su contra-asiento y el número de lote, como en compras. Solo añade columnas.
    /// </summary>
    public static Migracion ExportacionVentas { get; } = new(
        "20261108_1200_EXPORTACION_VENTAS", 261108002,
        "Deshacer la exportación contable de las facturas de venta: asiento anulado y número de lote de la factura.",
        "facturacion/factura-venta",
        new AnadirColumna("Facturas", "IdAsientoAnulado", "LONG"),
        new AnadirColumna("Facturas", "GeneracionExportacion", "LONG"));

    /// <summary>
    /// Libro de IVA repercutido de las ventas (facturacion/factura-venta/contabilizacion 2026-11-11): cada registro enlaza
    /// con su factura, también los del ERP, que no tienen asiento de ProContaVF. Solo añade la columna y su índice.
    /// </summary>
    public static Migracion LibroIvaRepercutido { get; } = new(
        "20261111_1300_LIBRO_IVA_REPERCUTIDO", 261111002,
        "Enlace de cada registro del libro de IVA repercutido con su factura de venta.",
        "facturacion/factura-venta",
        new AnadirColumna("IVARepercutido", "IdFactura", "LONG"),
        new CrearIndice("IVARepercutido", "IX_IVAR_Factura", "CREATE INDEX IX_IVAR_Factura ON IVARepercutido (IdFactura)"));

    /// <summary>
    /// Datos de pago y de presentación de la factura (facturacion/factura-venta 2026-11-13 12:13): el logo y el teléfono de la
    /// empresa, la cuenta bancaria en la que se cobra cada forma de pago en cada empresa (la que sale en las transferencias) y el
    /// IBAN de cada cliente (el que sale enmascarado en los recibos domiciliados). Solo añade columnas.
    /// </summary>
    public static Migracion DatosPagoFactura { get; } = new(
        "20261113_2345_DATOS_PAGO_FACTURA", 261113030,
        "Logo y teléfono de la empresa, cuenta bancaria de cada forma de pago en cada empresa e IBAN del cliente, para la factura.",
        "facturacion/factura-venta",
        new AnadirColumna("Sociedades", "Telefono", "TEXT(30)"),
        new AnadirColumna("Sociedades", "Logo", "TEXT(255)"),
        new AnadirColumna("FormasPagoSociedad", "IdCuentaBancaria", "LONG"),
        new AnadirColumna("Clientes", "IBAN", "TEXT(34)"))
    {
        Descripciones =
        [
            new("Sociedades", "Telefono", "Teléfono de la empresa; sale en sus facturas junto al domicilio del emisor."),
            new("Sociedades", "Logo", "Fichero JPG o PNG del logo que sale en las facturas; si la ruta es relativa, dentro de RutaDocumentos. Vacío: sale el nombre de la empresa."),
            new("FormasPagoSociedad", "IdCuentaBancaria", "Cuenta bancaria de la empresa (CuentasBancarias) en la que se cobra esta forma de pago; su IBAN sale en las facturas, por ejemplo en las transferencias."),
            new("Clientes", "IBAN", "IBAN del cliente, sin espacios, para los recibos domiciliados; en la factura sale enmascarado (solo los cuatro últimos dígitos)."),
        ],
    };
}
