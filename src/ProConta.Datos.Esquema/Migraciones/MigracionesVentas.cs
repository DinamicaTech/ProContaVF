namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por facturacion/reservas (ventas por facturar).</summary>
public static class MigracionesVentas
{
    /// <summary>
    /// Ventas por facturar (facturacion/reservas 2026-11-14): orígenes de venta por sociedad (con el cliente al que se factura
    /// y los textos con que llegan en las importaciones), las ventas sin factura (reservas de Avaibook, altas manuales y
    /// reservas del ERP ya facturadas), los alojamientos de Avaibook con su piso y concepto, y la conexión con Avaibook por
    /// sociedad (token cifrado). Solo crea tablas. No llevan relación con Sociedades: Access no admite más índices en esa tabla.
    /// </summary>
    public static Migracion VentasPorFacturar { get; } = new(
        "20261114_1200_VENTAS_POR_FACTURAR", 261114010,
        "Ventas por facturar: orígenes de venta, ventas, alojamientos de Avaibook y conexión con Avaibook.",
        "facturacion/reservas",
        new CrearTabla("OrigenesVenta",
            "CREATE TABLE OrigenesVenta (IdOrigenVenta COUNTER CONSTRAINT PK_OrigenesVenta PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " +
            "Codigo TEXT(20) NOT NULL, Nombre TEXT(60) NOT NULL, " +
            "IdCliente LONG CONSTRAINT FK_OrigenesVenta_Cliente REFERENCES Clientes (IdCliente), " +
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
            "IdActivo LONG CONSTRAINT FK_AlojamientosAvaibook_Activo REFERENCES Activos (IdActivo), " +
            "IdConcepto LONG, CausaExencion TEXT(2), Activo YESNO NOT NULL)"),
        new CrearIndice("AlojamientosAvaibook", "UX_AlojamientosAvaibook_SociedadExterno",
            "CREATE UNIQUE INDEX UX_AlojamientosAvaibook_SociedadExterno ON AlojamientosAvaibook (IdSociedad, IdExterno)"),
        new CrearTabla("ConexionesAvaibook",
            "CREATE TABLE ConexionesAvaibook (IdSociedad TEXT(3) NOT NULL CONSTRAINT PK_ConexionesAvaibook PRIMARY KEY, " +
            "Activa YESNO NOT NULL, Direccion TEXT(255) NOT NULL, SecretoProtegido LONGTEXT, ImportarDesde DATETIME, " +
            "UltimaImportacion DATETIME, OperadorUltimaImportacion TEXT(100), ResumenUltimaImportacion LONGTEXT)"))
    {
        Descripciones = Descripciones,
    };

    internal static IReadOnlyList<DescripcionEsquema> Descripciones =>
    [
        new("OrigenesVenta", null, "Origen de las ventas por facturar de una sociedad (Airbnb, Booking, directa…): a quién se factura y cómo se reconoce al importar."),
        new("OrigenesVenta", "IdOrigenVenta", "Identificador del origen."),
        new("OrigenesVenta", "IdSociedad", "Sociedad del origen."),
        new("OrigenesVenta", "Codigo", "Código corto del origen, único en la sociedad (AIRBNB, BOOKING, DIRECTA)."),
        new("OrigenesVenta", "Nombre", "Nombre del origen en las pantallas y en la descripción de la factura."),
        new("OrigenesVenta", "IdCliente", "Cliente al que se facturan sus ventas (la plataforma); vacío, cada venta lleva su cliente."),
        new("OrigenesVenta", "TextosImportacion", "Textos con los que llega en una importación (partnerName de Avaibook), separados por punto y coma."),
        new("OrigenesVenta", "EsDirecta", "Origen de las reservas que no vienen de un portal (alta propia en Avaibook, motor de reservas)."),
        new("OrigenesVenta", "Activo", "Solo los activos se ofrecen al dar de alta ventas."),
        new("VentasPorFacturar", null, "Venta registrada antes de facturarla (una reserva, un servicio): se factura después con «Facturar ventas…»."),
        new("VentasPorFacturar", "IdVentaPorFacturar", "Identificador de la venta."),
        new("VentasPorFacturar", "IdSociedad", "Sociedad de la venta."),
        new("VentasPorFacturar", "IdOrigenVenta", "Origen de la venta; da el cliente al que se factura si la venta no lleva el suyo."),
        new("VentasPorFacturar", "IdCliente", "Cliente propio de la venta (el huésped que pide factura); vacío, el del origen."),
        new("VentasPorFacturar", "IdActivo", "Piso (activo) de la venta; agrupa las facturas y da el centro de coste de la línea."),
        new("VentasPorFacturar", "IdConcepto", "Concepto de la línea de factura (nombre, cuenta e IVA)."),
        new("VentasPorFacturar", "FechaReferencia", "Fecha que decide el mes en que se factura: la de entrada del huésped."),
        new("VentasPorFacturar", "FechaFin", "Fecha final (la de salida del huésped)."),
        new("VentasPorFacturar", "Cantidad", "Cantidad de la línea (noches)."),
        new("VentasPorFacturar", "Importe", "Importe total que se factura: lo que paga el huésped, comisión incluida."),
        new("VentasPorFacturar", "Comision", "Comisión de la plataforma; no entra en la factura emitida."),
        new("VentasPorFacturar", "TipoIva", "IVA de la línea (%); vacío, el del concepto."),
        new("VentasPorFacturar", "CausaExencion", "Causa de exención de la línea (E1…E6); vacía, sujeta y no exenta."),
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
        new("AlojamientosAvaibook", null, "Alojamiento de Avaibook de una sociedad y el piso (activo) y concepto con que se importan sus reservas."),
        new("AlojamientosAvaibook", "IdAlojamientoAvaibook", "Identificador interno."),
        new("AlojamientosAvaibook", "IdSociedad", "Sociedad."),
        new("AlojamientosAvaibook", "IdExterno", "Identificador del alojamiento en Avaibook (accommodationId)."),
        new("AlojamientosAvaibook", "Nombre", "Nombre del alojamiento en Avaibook."),
        new("AlojamientosAvaibook", "IdActivo", "Piso (activo) de las reservas; vacío, sus reservas no se importan."),
        new("AlojamientosAvaibook", "IdConcepto", "Concepto de la línea de factura de sus reservas."),
        new("AlojamientosAvaibook", "CausaExencion", "Causa de exención de sus reservas (E1 para el alquiler sin servicios de hostelería)."),
        new("AlojamientosAvaibook", "Activo", "Si se importan sus reservas."),
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
}
