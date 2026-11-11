namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de correo (docs/correo.md).</summary>
public static class MigracionesCorreo
{
    /// <summary>
    /// Correo saliente (correo/envio 2026-11-10 19:40): una cuenta de correo por sociedad y ejercicio (envía por SMTP y
    /// recibe por POP3, con la contraseña cifrada), la cola durable de mensajes con sus destinatarios, adjuntos (copias
    /// inmutables con huella en la carpeta de documentos de la empresa) e intentos, los lotes de los procesos de más de un
    /// correo, el turno que impide que dos sesiones procesen la cola a la vez y el parámetro general de desvío de pruebas.
    /// Las tablas del correo antiguo (ConfiguracionCorreo, EnviosCorreo y las del Mail Dispatcher) no se tocan.
    /// </summary>
    public static Migracion CorreoSaliente { get; } = new(
        "20261110_1940_CORREO_SALIENTE", 261110001,
        "Correo saliente: cuentas de correo por ejercicio, mensajes, destinatarios, adjuntos, intentos, lotes, turno de proceso y desvío de pruebas.",
        "correo/envio",
        new CrearTabla("CuentasCorreo",
            "CREATE TABLE CuentasCorreo (IdCuentaCorreo COUNTER CONSTRAINT PK_CuentasCorreo PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_CuentasCorreo_SociedadEjercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "Activa YESNO NOT NULL, Direccion TEXT(254) NOT NULL, NombreVisible TEXT(150), Usuario TEXT(254), SecretoProtegido LONGTEXT, ResponderA TEXT(254), " +
            "ServidorSmtp TEXT(255), PuertoSmtp LONG NOT NULL, SeguridadSmtp TEXT(15) NOT NULL, " +
            "ServidorPop3 TEXT(255), PuertoPop3 LONG NOT NULL, SeguridadPop3 TEXT(15) NOT NULL, " +
            "EliminarTrasProcesar YESNO NOT NULL, IntervaloRecepcionMinutos LONG NOT NULL, TiempoEsperaSegundos LONG NOT NULL, " +
            "Version LONG NOT NULL, FechaModificacion DATETIME, OperadorModificacion TEXT(30))"),
        new CrearIndice("CuentasCorreo", "UX_CuentasCorreo_SociedadEjercicio",
            "CREATE UNIQUE INDEX UX_CuentasCorreo_SociedadEjercicio ON CuentasCorreo (IdSociedadEjercicio)"),
        new CrearTabla("LotesCorreo",
            "CREATE TABLE LotesCorreo (IdLoteCorreo COUNTER CONSTRAINT PK_LotesCorreo PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_LotesCorreo_SociedadEjercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "Descripcion TEXT(255) NOT NULL, Origen TEXT(30) NOT NULL, FechaCreacion DATETIME NOT NULL, Operador TEXT(30) NOT NULL)"),
        new CrearTabla("MensajesCorreo",
            "CREATE TABLE MensajesCorreo (IdMensajeCorreo COUNTER CONSTRAINT PK_MensajesCorreo PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_MensajesCorreo_SociedadEjercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "IdCuentaCorreo LONG CONSTRAINT FK_MensajesCorreo_Cuenta REFERENCES CuentasCorreo (IdCuentaCorreo), " +
            "IdLoteCorreo LONG CONSTRAINT FK_MensajesCorreo_Lote REFERENCES LotesCorreo (IdLoteCorreo), " +
            "TipoCorreo TEXT(20) NOT NULL CONSTRAINT FK_MensajesCorreo_Tipo REFERENCES TiposCorreo (TipoCorreo), " +
            "TipoOrigen TEXT(20), IdOrigen LONG, ReferenciaOrigen TEXT(150), MessageId TEXT(255) NOT NULL, " +
            "Remitente TEXT(254) NOT NULL, NombreRemitente TEXT(150), ResponderA TEXT(254), Asunto TEXT(255) NOT NULL, CuerpoHtml LONGTEXT, CuerpoTexto LONGTEXT, " +
            "Estado TEXT(20) NOT NULL, NumeroIntentos LONG NOT NULL, ProximoIntento DATETIME, FechaCreacion DATETIME NOT NULL, FechaAceptacion DATETIME, " +
            "Operador TEXT(30) NOT NULL, IdMensajeOriginal LONG, DesviadoA TEXT(254), UltimoError LONGTEXT, " +
            "ResueltoPor TEXT(30), FechaResolucion DATETIME, MotivoResolucion TEXT(255), Version LONG NOT NULL)"),
        new CrearIndice("MensajesCorreo", "UX_MensajesCorreo_MessageId", "CREATE UNIQUE INDEX UX_MensajesCorreo_MessageId ON MensajesCorreo (MessageId)"),
        new CrearIndice("MensajesCorreo", "IX_MensajesCorreo_Cola", "CREATE INDEX IX_MensajesCorreo_Cola ON MensajesCorreo (Estado, ProximoIntento)"),
        new CrearIndice("MensajesCorreo", "IX_MensajesCorreo_Origen", "CREATE INDEX IX_MensajesCorreo_Origen ON MensajesCorreo (TipoOrigen, IdOrigen)"),
        new CrearIndice("MensajesCorreo", "IX_MensajesCorreo_SociedadEjercicio", "CREATE INDEX IX_MensajesCorreo_SociedadEjercicio ON MensajesCorreo (IdSociedadEjercicio, FechaCreacion)"),
        new CrearTabla("DestinatariosMensajeCorreo",
            "CREATE TABLE DestinatariosMensajeCorreo (IdDestinatarioMensaje COUNTER CONSTRAINT PK_DestinatariosMensajeCorreo PRIMARY KEY, " +
            "IdMensajeCorreo LONG NOT NULL CONSTRAINT FK_DestinatariosMensaje_Mensaje REFERENCES MensajesCorreo (IdMensajeCorreo), " +
            "Tipo TEXT(4) NOT NULL, Direccion TEXT(254) NOT NULL, Nombre TEXT(150), Resultado TEXT(15) NOT NULL, RespuestaServidor TEXT(255))"),
        new CrearIndice("DestinatariosMensajeCorreo", "IX_DestinatariosMensaje_Mensaje",
            "CREATE INDEX IX_DestinatariosMensaje_Mensaje ON DestinatariosMensajeCorreo (IdMensajeCorreo)"),
        new CrearTabla("AdjuntosMensajeCorreo",
            "CREATE TABLE AdjuntosMensajeCorreo (IdAdjuntoMensaje COUNTER CONSTRAINT PK_AdjuntosMensajeCorreo PRIMARY KEY, " +
            "IdMensajeCorreo LONG NOT NULL CONSTRAINT FK_AdjuntosMensaje_Mensaje REFERENCES MensajesCorreo (IdMensajeCorreo), " +
            "Ordinal SHORT NOT NULL, Nombre TEXT(255) NOT NULL, TipoContenido TEXT(100) NOT NULL, TamanoBytes LONG NOT NULL, Huella TEXT(64) NOT NULL, " +
            "RutaRelativa TEXT(255) NOT NULL)"),
        new CrearIndice("AdjuntosMensajeCorreo", "UX_AdjuntosMensaje_Ordinal",
            "CREATE UNIQUE INDEX UX_AdjuntosMensaje_Ordinal ON AdjuntosMensajeCorreo (IdMensajeCorreo, Ordinal)"),
        new CrearTabla("IntentosMensajeCorreo",
            "CREATE TABLE IntentosMensajeCorreo (IdIntentoMensaje COUNTER CONSTRAINT PK_IntentosMensajeCorreo PRIMARY KEY, " +
            "IdMensajeCorreo LONG NOT NULL CONSTRAINT FK_IntentosMensaje_Mensaje REFERENCES MensajesCorreo (IdMensajeCorreo), " +
            "Numero LONG NOT NULL, Inicio DATETIME NOT NULL, Fin DATETIME, Resultado TEXT(20) NOT NULL, Fase TEXT(20), RespuestaServidor TEXT(255), " +
            "Detalle LONGTEXT, Operador TEXT(30))"),
        new CrearIndice("IntentosMensajeCorreo", "UX_IntentosMensaje_Numero",
            "CREATE UNIQUE INDEX UX_IntentosMensaje_Numero ON IntentosMensajeCorreo (IdMensajeCorreo, Numero)"),
        new CrearTabla("TurnosCorreo",
            "CREATE TABLE TurnosCorreo (Clave TEXT(30) NOT NULL CONSTRAINT PK_TurnosCorreo PRIMARY KEY, Propietario TEXT(100) NOT NULL, CaducaEn DATETIME NOT NULL)"),
        new AnadirColumna("Configuracion", "CorreoDesviarA", "TEXT(254)"));

    /// <summary>
    /// Correo recibido (correo/recepcion 2026-11-11): cada mensaje del buzón POP3 de la cuenta, identificado por cuenta + UIDL,
    /// con su copia .eml y sus hitos (procesado, respuesta enviada, borrado del servidor) para reanudar sin duplicar; sus
    /// adjuntos con el tipo reconocido, su estado y lo que generaron (factura de compra, asiento), y los remitentes autorizados
    /// de cada ejercicio, con permisos separados para importar y para pedir informes.
    /// </summary>
    public static Migracion CorreoRecepcion { get; } = new(
        "20261111_1100_CORREO_RECEPCION", 261111001,
        "Correo recibido: mensajes del buzón POP3 con sus hitos, adjuntos con su resultado y remitentes autorizados por ejercicio.",
        "correo/recepcion",
        new CrearTabla("MensajesRecibidosCorreo",
            "CREATE TABLE MensajesRecibidosCorreo (IdMensajeRecibido COUNTER CONSTRAINT PK_MensajesRecibidosCorreo PRIMARY KEY, " +
            "IdCuentaCorreo LONG NOT NULL CONSTRAINT FK_MensajesRecibidos_Cuenta REFERENCES CuentasCorreo (IdCuentaCorreo), " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_MensajesRecibidos_SociedadEjercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "Uidl TEXT(255) NOT NULL, MessageId TEXT(255), Remitente TEXT(254), NombreRemitente TEXT(150), Asunto TEXT(255), FechaMensaje DATETIME, " +
            "FechaRecepcion DATETIME NOT NULL, TamanoBytes LONG NOT NULL, Huella TEXT(64) NOT NULL, RutaEml TEXT(255) NOT NULL, Estado TEXT(25) NOT NULL, " +
            "Resumen LONGTEXT, IdMensajeRespuesta LONG CONSTRAINT FK_MensajesRecibidos_Respuesta REFERENCES MensajesCorreo (IdMensajeCorreo), " +
            "EliminadoServidor YESNO NOT NULL, FechaEliminacion DATETIME, FechaProceso DATETIME, Version LONG NOT NULL)"),
        new CrearIndice("MensajesRecibidosCorreo", "UX_MensajesRecibidos_CuentaUidl",
            "CREATE UNIQUE INDEX UX_MensajesRecibidos_CuentaUidl ON MensajesRecibidosCorreo (IdCuentaCorreo, Uidl)"),
        new CrearIndice("MensajesRecibidosCorreo", "IX_MensajesRecibidos_SociedadEjercicio",
            "CREATE INDEX IX_MensajesRecibidos_SociedadEjercicio ON MensajesRecibidosCorreo (IdSociedadEjercicio, FechaRecepcion)"),
        new CrearTabla("AdjuntosRecibidosCorreo",
            "CREATE TABLE AdjuntosRecibidosCorreo (IdAdjuntoRecibido COUNTER CONSTRAINT PK_AdjuntosRecibidosCorreo PRIMARY KEY, " +
            "IdMensajeRecibido LONG NOT NULL CONSTRAINT FK_AdjuntosRecibidos_Mensaje REFERENCES MensajesRecibidosCorreo (IdMensajeRecibido), " +
            "Ordinal SHORT NOT NULL, Nombre TEXT(255) NOT NULL, TamanoBytes LONG NOT NULL, Huella TEXT(64) NOT NULL, RutaRelativa TEXT(255) NOT NULL, " +
            "Tipo TEXT(20) NOT NULL, Estado TEXT(20) NOT NULL, IdFacturaCompra LONG, IdAsiento LONG, Detalle LONGTEXT)"),
        new CrearIndice("AdjuntosRecibidosCorreo", "UX_AdjuntosRecibidos_Ordinal",
            "CREATE UNIQUE INDEX UX_AdjuntosRecibidos_Ordinal ON AdjuntosRecibidosCorreo (IdMensajeRecibido, Ordinal)"),
        new CrearTabla("RemitentesAutorizadosCorreo",
            "CREATE TABLE RemitentesAutorizadosCorreo (IdRemitenteAutorizado COUNTER CONSTRAINT PK_RemitentesAutorizadosCorreo PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_Remitentes_SociedadEjercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "Direccion TEXT(254) NOT NULL, Nombre TEXT(150), PuedeImportar YESNO NOT NULL, PuedePedirInformes YESNO NOT NULL, Activo YESNO NOT NULL)"),
        new CrearIndice("RemitentesAutorizadosCorreo", "UX_Remitentes_EjercicioDireccion",
            "CREATE UNIQUE INDEX UX_Remitentes_EjercicioDireccion ON RemitentesAutorizadosCorreo (IdSociedadEjercicio, Direccion)"));
}
