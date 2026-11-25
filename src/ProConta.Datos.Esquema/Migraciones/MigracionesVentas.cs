using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por facturacion/reservas (ventas por facturar).</summary>
public static class MigracionesVentas
{
    /// <summary>
    /// Ventas por facturar (facturacion/reservas 2026-11-14): orígenes de venta por sociedad (con
    /// los textos con que llegan en las importaciones), las reglas de venta por origen y activo (concepto, IVA y cliente por defecto), las ventas sin factura (reservas de Avaibook, altas manuales y
    /// reservas del ERP ya facturadas), los alojamientos de Avaibook con su activo, y la conexión con Avaibook por
    /// sociedad (token cifrado). Solo crea tablas. No llevan relación con Sociedades: Access no admite más índices en esa tabla.
    /// </summary>
    public static Migracion VentasPorFacturar { get; } = new(
        "20261114_1200_VENTAS_POR_FACTURAR", 261114010,
        "Ventas por facturar: orígenes y reglas de venta, ventas, alojamientos de Avaibook y conexión con Avaibook.",
        "facturacion/reservas",
        new CrearTabla("OrigenesVenta",
            "CREATE TABLE OrigenesVenta (IdOrigenVenta COUNTER CONSTRAINT PK_OrigenesVenta PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " +
            "Codigo TEXT(20) NOT NULL, Nombre TEXT(60) NOT NULL, " +
            "TextosImportacion TEXT(255), EsDirecta YESNO NOT NULL, Activo YESNO NOT NULL)"),
        new CrearIndice("OrigenesVenta", "UX_OrigenesVenta_SociedadCodigo",
            "CREATE UNIQUE INDEX UX_OrigenesVenta_SociedadCodigo ON OrigenesVenta (IdSociedad, Codigo)"),
        new CrearTabla("VentasPorFacturar",
            "CREATE TABLE VentasPorFacturar (IdVentaPorFacturar COUNTER CONSTRAINT PK_VentasPorFacturar PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " +
            "IdOrigenVenta LONG NOT NULL CONSTRAINT FK_VentasPorFacturar_Origen REFERENCES OrigenesVenta (IdOrigenVenta), " +
            "IdCliente LONG CONSTRAINT FK_VentasPorFacturar_Cliente REFERENCES Clientes (IdCliente), " +
            "IdActivo LONG CONSTRAINT FK_VentasPorFacturar_Activo REFERENCES Activos (IdActivo), " +
            "IdConcepto LONG, FechaReferencia DATETIME NOT NULL, FechaFin DATETIME, Cantidad DECIMAL(12,4) NOT NULL, " +
            "Importe CURRENCY NOT NULL, Comision CURRENCY, TipoIva DECIMAL(8,4), CausaExencion TEXT(2), " +
            "Descripcion TEXT(255), Referencia TEXT(60), Titular TEXT(150), " +
            "Anulada YESNO NOT NULL, MotivoAnulacion TEXT(255), FacturadaFuera YESNO NOT NULL, IdFactura LONG, " +
            "Fuente TEXT(12) NOT NULL, IdExterno TEXT(80), Huella TEXT(64), Incidencia TEXT(255), Notas LONGTEXT, " +
            "FechaAlta DATETIME NOT NULL, OperadorAlta TEXT(100), FechaModificacion DATETIME, Version LONG NOT NULL)"),
        new CrearIndice("VentasPorFacturar", "IX_VentasPorFacturar_Externo",
            "CREATE INDEX IX_VentasPorFacturar_Externo ON VentasPorFacturar (IdSociedad, Fuente, IdExterno)"),
        new CrearIndice("VentasPorFacturar", "IX_VentasPorFacturar_Fecha",
            "CREATE INDEX IX_VentasPorFacturar_Fecha ON VentasPorFacturar (IdSociedad, FechaReferencia)"),
        new CrearIndice("VentasPorFacturar", "IX_VentasPorFacturar_Factura",
            "CREATE INDEX IX_VentasPorFacturar_Factura ON VentasPorFacturar (IdFactura)"),
        new CrearTabla("AlojamientosAvaibook",
            "CREATE TABLE AlojamientosAvaibook (IdAlojamientoAvaibook COUNTER CONSTRAINT PK_AlojamientosAvaibook PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " +
            "IdExterno TEXT(40) NOT NULL, Nombre TEXT(255), " +
            "IdActivo LONG CONSTRAINT FK_AlojamientosAvaibook_Activo REFERENCES Activos (IdActivo), Activo YESNO NOT NULL)"),
        new CrearIndice("AlojamientosAvaibook", "UX_AlojamientosAvaibook_SociedadExterno",
            "CREATE UNIQUE INDEX UX_AlojamientosAvaibook_SociedadExterno ON AlojamientosAvaibook (IdSociedad, IdExterno)"),
        new CrearTabla("ReglasVenta",
            "CREATE TABLE ReglasVenta (IdReglaVenta COUNTER CONSTRAINT PK_ReglasVenta PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " +
            "IdOrigenVenta LONG NOT NULL CONSTRAINT FK_ReglasVenta_Origen REFERENCES OrigenesVenta (IdOrigenVenta), " +
            "IdActivo LONG CONSTRAINT FK_ReglasVenta_Activo REFERENCES Activos (IdActivo), " +
            "IdConcepto LONG, CausaExencion TEXT(2), TipoIva DECIMAL(8,4), " +
            "IdCliente LONG CONSTRAINT FK_ReglasVenta_Cliente REFERENCES Clientes (IdCliente), Activa YESNO NOT NULL)"),
        new CrearIndice("ReglasVenta", "IX_ReglasVenta_OrigenActivo",
            "CREATE INDEX IX_ReglasVenta_OrigenActivo ON ReglasVenta (IdSociedad, IdOrigenVenta, IdActivo)"),
        new CrearTabla("ConexionesAvaibook",
            "CREATE TABLE ConexionesAvaibook (IdSociedad TEXT(3) NOT NULL CONSTRAINT PK_ConexionesAvaibook PRIMARY KEY, " +
            "Activa YESNO NOT NULL, Direccion TEXT(255) NOT NULL, SecretoProtegido LONGTEXT, ImportarDesde DATETIME, " +
            "UltimaImportacion DATETIME, OperadorUltimaImportacion TEXT(100), ResumenUltimaImportacion LONGTEXT)"))
    {
        Descripciones = Descripciones,
    };

    /// <summary>
    /// Cómo se facturan las ventas de cada regla (facturacion/reservas 2026-11-14 17:15): cada cuánto se agrupan en una factura
    /// (NO, SEMANAL, QUINCENAL, MENSUAL, TRIMESTRAL o ANUAL), si se separa una factura por activo y si «Facturar todo» deja
    /// borradores o facturas validadas y contabilizadas. Solo añade columnas, con lo que se hacía hasta ahora por defecto.
    /// </summary>
    public static Migracion ReglasVentaAgrupacion { get; } = new(
        "20261114_1715_REGLAS_VENTA_AGRUPACION", 261114020,
        "Reglas de venta: agrupación de las ventas en facturas, separación por activo y borrador o factura validada.",
        "facturacion/reservas",
        new AnadirColumnaObligatoria("ReglasVenta", "Agrupar", "TEXT(12)", "'MENSUAL'"),
        // Access deja a «No» las filas existentes al añadir un YESNO (nunca a null), así que el «Sí» por defecto se pone a mano en el mismo paso.
        new PasoPersonalizado(
            "Añadir ReglasVenta.SepararPorActivo (sí por defecto, también en las reglas que ya hay)",
            "ALTER TABLE [ReglasVenta] ADD COLUMN [SepararPorActivo] YESNO NOT NULL DEFAULT Yes; UPDATE [ReglasVenta] SET [SepararPorActivo] = True",
            (_, e) => Task.FromResult(e.ExisteColumna("ReglasVenta", "SepararPorActivo")),
            async (s, _) =>
            {
                await s.EjecutarAsync("ALTER TABLE [ReglasVenta] ADD COLUMN [SepararPorActivo] YESNO NOT NULL DEFAULT Yes");
                await s.EjecutarAsync("UPDATE [ReglasVenta] SET [SepararPorActivo] = True");
            }),
        new AnadirColumnaObligatoria("ReglasVenta", "AlFacturar", "TEXT(10)", "'BORRADOR'"))
    {
        Descripciones =
        [
            new("ReglasVenta", "Agrupar", "Cada cuánto se agrupan sus ventas en una factura: NO (una por venta), SEMANAL, QUINCENAL, MENSUAL, TRIMESTRAL o ANUAL, por la fecha de referencia."),
            new("ReglasVenta", "SepararPorActivo", "Si se hace una factura por activo relacionado; si no, una por origen y periodo con todos sus activos."),
            new("ReglasVenta", "AlFacturar", "Qué crea «Facturar todo»: BORRADOR, o VALIDAR (factura numerada, validada y contabilizada)."),
        ],
    };

    internal static IReadOnlyList<DescripcionEsquema> Descripciones =>
    [
        new("OrigenesVenta", null, "Origen de las ventas por facturar de una sociedad (Airbnb, Booking, directa…) y cómo se reconoce al importar."),
        new("OrigenesVenta", "IdOrigenVenta", "Identificador del origen."),
        new("OrigenesVenta", "IdSociedad", "Sociedad del origen."),
        new("OrigenesVenta", "Codigo", "Código corto del origen, único en la sociedad (AIRBNB, BOOKING, DIRECTA)."),
        new("OrigenesVenta", "Nombre", "Nombre del origen en las pantallas y en la descripción de la factura."),
        new("OrigenesVenta", "TextosImportacion", "Textos con los que llega en una importación (partnerName de Avaibook), separados por punto y coma."),
        new("OrigenesVenta", "EsDirecta", "Origen de las reservas que no vienen de un portal (alta propia en Avaibook, motor de reservas)."),
        new("OrigenesVenta", "Activo", "Solo los activos se ofrecen al dar de alta ventas."),
        new("VentasPorFacturar", null, "Venta registrada antes de facturarla (una reserva, un servicio): se factura después con «Facturar ventas…»."),
        new("VentasPorFacturar", "IdVentaPorFacturar", "Identificador de la venta."),
        new("VentasPorFacturar", "IdSociedad", "Sociedad de la venta."),
        new("VentasPorFacturar", "IdOrigenVenta", "Origen de la venta; con su activo decide la regla de venta (concepto, IVA y cliente por defecto)."),
        new("VentasPorFacturar", "IdCliente", "Cliente propio de la venta (el huésped que pide factura); vacío, el de su regla de venta."),
        new("VentasPorFacturar", "IdActivo", "Activo relacionado (el piso de la reserva); agrupa las facturas y da el centro de coste de la línea."),
        new("VentasPorFacturar", "IdConcepto", "Concepto propio de la venta; vacío, el de su regla de venta."),
        new("VentasPorFacturar", "FechaReferencia", "Fecha que decide el mes en que se factura: la de entrada del huésped."),
        new("VentasPorFacturar", "FechaFin", "Fecha final (la de salida del huésped)."),
        new("VentasPorFacturar", "Cantidad", "Cantidad de la línea (noches)."),
        new("VentasPorFacturar", "Importe", "Importe total que se factura: lo que paga el huésped, comisión incluida."),
        new("VentasPorFacturar", "Comision", "Comisión de la plataforma; no entra en la factura emitida."),
        new("VentasPorFacturar", "TipoIva", "IVA propio de la venta (%); vacío (y sin causa de exención), el de su regla de venta."),
        new("VentasPorFacturar", "CausaExencion", "Causa de exención propia de la venta (E1…E6); vacía (y sin IVA), la de su regla de venta."),
        new("VentasPorFacturar", "Descripcion", "Descripción de la línea de factura."),
        new("VentasPorFacturar", "Referencia", "Referencia externa (código de la reserva en la plataforma)."),
        new("VentasPorFacturar", "Titular", "Nombre del huésped o titular. Dato personal: no sale en informes ni diagnósticos."),
        new("VentasPorFacturar", "Anulada", "Venta anulada (reserva cancelada): no se factura."),
        new("VentasPorFacturar", "MotivoAnulacion", "Por qué se anuló."),
        new("VentasPorFacturar", "FacturadaFuera", "Facturada fuera de ProContaVF (en el ERP): no se vuelve a facturar."),
        new("VentasPorFacturar", "IdFactura", "Factura (borrador o validada) en la que está; vacío, pendiente."),
        new("VentasPorFacturar", "Fuente", "De dónde viene: MANUAL, AVAIBOOK o ERP."),
        new("VentasPorFacturar", "IdExterno", "Identificador en la fuente (externalId de Avaibook, Id del ERP); reconoce la venta al reimportar."),
        new("VentasPorFacturar", "Huella", "Huella de los datos importados, para saber si han cambiado en la fuente."),
        new("VentasPorFacturar", "Incidencia", "Último cambio de la fuente que no se ha podido aplicar (venta ya facturada)."),
        new("VentasPorFacturar", "Notas", "Notas internas."),
        new("VentasPorFacturar", "FechaAlta", "Fecha y hora del alta."),
        new("VentasPorFacturar", "OperadorAlta", "Operador o proceso que la dio de alta."),
        new("VentasPorFacturar", "FechaModificacion", "Fecha y hora del último cambio."),
        new("VentasPorFacturar", "Version", "Versión de la fila (concurrencia optimista)."),
        new("AlojamientosAvaibook", null, "Alojamiento de Avaibook de una sociedad y el activo al que corresponden sus reservas."),
        new("AlojamientosAvaibook", "IdAlojamientoAvaibook", "Identificador interno."),
        new("AlojamientosAvaibook", "IdSociedad", "Sociedad."),
        new("AlojamientosAvaibook", "IdExterno", "Identificador del alojamiento en Avaibook (accommodationId)."),
        new("AlojamientosAvaibook", "Nombre", "Nombre del alojamiento en Avaibook."),
        new("AlojamientosAvaibook", "IdActivo", "Activo relacionado de sus reservas; vacío, sus reservas no se importan."),
        new("AlojamientosAvaibook", "Activo", "Si se importan sus reservas."),
        new("ReglasVenta", null, "Regla de venta: lo que se propone al facturar las ventas de un origen y un activo (concepto, IVA y cliente)."),
        new("ReglasVenta", "IdReglaVenta", "Identificador de la regla."),
        new("ReglasVenta", "IdSociedad", "Sociedad."),
        new("ReglasVenta", "IdOrigenVenta", "Origen de las ventas a las que se aplica."),
        new("ReglasVenta", "IdActivo", "Activo relacionado de las ventas a las que se aplica; vacío, todos los del origen que no tengan regla propia."),
        new("ReglasVenta", "IdConcepto", "Concepto de la línea de factura (nombre y cuenta de venta)."),
        new("ReglasVenta", "CausaExencion", "Causa de exención (E1…E6); vacía, sujeta con el IVA de TipoIva."),
        new("ReglasVenta", "TipoIva", "IVA de la línea (%) cuando no está exenta; vacío, el del concepto."),
        new("ReglasVenta", "IdCliente", "Cliente al que se facturan esas ventas (la plataforma) si la venta no lleva el suyo."),
        new("ReglasVenta", "Activa", "Si la regla se aplica."),
        new("ConexionesAvaibook", null, "Conexión de una sociedad con la API de propietario de Avaibook."),
        new("ConexionesAvaibook", "IdSociedad", "Sociedad."),
        new("ConexionesAvaibook", "Activa", "Si se puede importar."),
        new("ConexionesAvaibook", "Direccion", "Dirección de la API (https://api.avaibook.com/api/owner/)."),
        new("ConexionesAvaibook", "SecretoProtegido", "Token de la API, cifrado."),
        new("ConexionesAvaibook", "ImportarDesde", "Primera fecha de entrada que se importa (las anteriores vienen del ERP)."),
        new("ConexionesAvaibook", "UltimaImportacion", "Fecha y hora de la última importación."),
        new("ConexionesAvaibook", "OperadorUltimaImportacion", "Operador de la última importación."),
        new("ConexionesAvaibook", "ResumenUltimaImportacion", "Resumen de la última importación, sin datos personales."),
    ];

    /// <summary>Clave de proceso de la plantilla de sistema de la compensación (la misma que PlantillaCompensacionComision.ClaveProceso).</summary>
    public const string ClaveCompensacion = "comision-plataforma";

    public const string NombreCompensacion = "Compensación de comisión de plataforma";

    private const string DescripcionCompensacion =
        "Compensa la comisión de una plataforma (Airbnb, Booking…) que la plataforma descuenta de lo que paga: la cuenta de acreedor (410) de la " +
        "factura de comisión al Debe contra la cuenta de cliente (430) del mismo tercero al Haber, por lo que la factura llevó a la 410. Así la 430 " +
        "queda con lo que ingresa el banco. Es una plantilla de sistema: la ejecuta la exportación de las facturas de compra y no se usa a mano.";

    private const string EjemploCompensacion =
        "{\"ejercicio\":2026,\"cuentas\":{\"4100001\":\"Plataforma Ejemplo Ltd.\",\"4300001\":\"Plataforma Ejemplo Ltd.\"}," +
        "\"registros\":[{\"clave\":\"1\",\"fecha\":\"2026-03-31\",\"campos\":{\"CuentaProveedor\":\"4100001\",\"CuentaCliente\":\"4300001\"," +
        "\"Importe\":152.40,\"Concepto\":\"Compensación comisión Plataforma Ejemplo Ltd. fra. EJ-0001\"}}]}";

    /// <summary>Líneas de la plantilla de compensación: (orden, cuenta, lado, importe, concepto).</summary>
    private static readonly (short Orden, string Cuenta, string Lado, string Importe, string Concepto)[] LineasCompensacion =
    [
        (1, "CuentaProveedor", "D", "Importe", "Concepto"),
        (2, "CuentaCliente", "H", "Importe", "Concepto"),
    ];

    /// <summary>
    /// Compensación de la comisión de las plataformas (facturacion/reservas/compensacion-comision 2026-11-16): la plantilla de sistema
    /// «Compensación de comisión de plataforma», que da el asiento que lleva la 410 de cada factura de comisión contra la 430 del mismo tercero.
    /// Solo añade la plantilla: no cambia ninguna tabla.
    /// </summary>
    public static Migracion CompensacionComision { get; } = new(
        "20261116_1700_COMPENSACION_COMISION", 261116017,
        "Ventas por facturar: plantilla de sistema de la compensación de la comisión de las plataformas (410 contra 430).",
        "facturacion/reservas/compensacion-comision",
        new PasoPersonalizado(
            "Dar de alta la plantilla de sistema «" + NombreCompensacion + "»",
            "INSERT PlantillasAsiento (" + NombreCompensacion + ", SISTEMA, PROCESO " + ClaveCompensacion + ", FECHA_ORIGEN) + PlantillasAsientoLineas " +
            string.Join("; ", LineasCompensacion.Select(l => $"{l.Orden} {l.Cuenta} {l.Lado} {l.Importe} {l.Concepto}")) + "; ejemplo " + EjemploCompensacion,
            async (s, e) => e.Tabla("PlantillasAsiento") is not null && await IdPlantillaCompensacionAsync(s) is not null,
            async (s, _) => await CargarPlantillaCompensacionAsync(s)));

    private static async Task<int?> IdPlantillaCompensacionAsync(ISesionDatos s) =>
        await s.EscalarAsync("SELECT TOP 1 IdPlantillaAsiento FROM PlantillasAsiento WHERE ClaveProceso = ? AND IdSociedad IS NULL", ClaveCompensacion) is { } v and not DBNull
            ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : null;

    private static async Task CargarPlantillaCompensacionAsync(ISesionDatos s)
    {
        if (await IdPlantillaCompensacionAsync(s) is not null) return;
        await s.EjecutarAsync(
            "INSERT INTO PlantillasAsiento (Nombre, Descripcion, Tipo, IdSociedad, Origen, ClaveProceso, ReglaFecha, AgruparPorCuenta, Activa, MotivoPropuesto, Ejemplo, Orden, Clase) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            NombreCompensacion, DescripcionCompensacion, "SISTEMA", DBNull.Value, "PROCESO", ClaveCompensacion, "FECHA_ORIGEN", false, true, DBNull.Value, EjemploCompensacion, (short)102, "PROCESO");
        var id = await s.UltimoIdAsync();
        foreach (var l in LineasCompensacion)
            await s.EjecutarAsync(
                "INSERT INTO PlantillasAsientoLineas (IdPlantillaAsiento, NumeroAsiento, Orden, PorCadaApunte, FormulaCuenta, Lado, FormulaImporte, FormulaConcepto, FormulaCentro, Condicion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                id, (short)1, l.Orden, false, l.Cuenta, l.Lado, l.Importe, l.Concepto, DBNull.Value, DBNull.Value);
    }

    /// <summary>
    /// Factura nominal al huésped (facturacion/reservas 2026-11-25 14:13): la regla de venta puede pedir una factura por reserva
    /// a nombre del huésped (FacturaAlHuesped), que se da de alta como cliente con la 430 del cliente de la regla, e indicar el
    /// acreedor de la plataforma (IdAcreedor) para compensar su comisión aunque cliente y acreedor sean terceros distintos.
    /// Las ventas guardan el email y el teléfono del huésped que da Avaibook. Solo añade columnas.
    /// </summary>
    public static Migracion FacturaNominalHuesped { get; } = new(
        "20261125_1500_FACTURA_NOMINAL_HUESPED", 261125015,
        "Ventas por facturar: factura nominal al huésped, acreedor de la plataforma en la regla y contacto del huésped.",
        "facturacion/reservas",
        new AnadirColumnaObligatoria("ReglasVenta", "FacturaAlHuesped", "YESNO", "No"),
        new AnadirColumna("ReglasVenta", "IdAcreedor", "LONG"),
        new PasoPersonalizado(
            "Relacionar ReglasVenta (FK_ReglasVenta_Acreedor)",
            "ALTER TABLE ReglasVenta ADD CONSTRAINT FK_ReglasVenta_Acreedor FOREIGN KEY (IdAcreedor) REFERENCES Proveedores (IdProveedor)",
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, "ReglasVenta", "FK_ReglasVenta_Acreedor")),
            (s, _) => s.EjecutarAsync("ALTER TABLE ReglasVenta ADD CONSTRAINT FK_ReglasVenta_Acreedor FOREIGN KEY (IdAcreedor) REFERENCES Proveedores (IdProveedor)")),
        new AnadirColumna("VentasPorFacturar", "EmailTitular", "TEXT(255)"),
        new AnadirColumna("VentasPorFacturar", "TelefonoTitular", "TEXT(50)"))
    {
        Descripciones =
        [
            new("ReglasVenta", "FacturaAlHuesped", "Si cada venta se factura a nombre de su huésped (una factura por venta): el huésped se da de alta como cliente con la 430 del cliente de la regla, que comparten todos los huéspedes del origen."),
            new("ReglasVenta", "IdAcreedor", "Acreedor (proveedor) de la plataforma del origen, cuya comisión se compensa contra la 430 del cliente de la regla; vacío si es el mismo tercero que el cliente."),
            new("VentasPorFacturar", "EmailTitular", "Email del huésped que da Avaibook (dato personal): reconoce al huésped que repite al darlo de alta como cliente."),
            new("VentasPorFacturar", "TelefonoTitular", "Teléfono del huésped que da Avaibook (dato personal), que pasa a su ficha de cliente."),
        ],
    };}
