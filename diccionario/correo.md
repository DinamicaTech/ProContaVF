<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · correo

## AdjuntosMailDispatcher

- Dueño funcional: `correo`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdAdjuntoMail | COUNTER | no |  |  |
| IdProcesamientoMail | LONG | no |  |  |
| Ordinal | LONG | no |  |  |
| NombreOriginal | TEXT(255) | no |  |  |
| HashContenido | TEXT(64) | no |  |  |
| TipoDetectado | TEXT(40) | sí |  |  |
| Estado | TEXT(40) | no |  |  |
| RutaMaterializada | LONGTEXT | sí |  |  |
| Detalle | LONGTEXT | sí |  |  |

Índices:
- FK_AdjuntoMail_Proceso: IdProcesamientoMail
- PK_AdjuntosMail (clave primaria): IdAdjuntoMail
- UX_AdjuntosMail_Ordinal (único): IdProcesamientoMail, Ordinal

Relaciones:
- FK_AdjuntoMail_Proceso: (IdProcesamientoMail) → ProcesamientosMailDispatcher (IdProcesamientoMail); borrado NO ACTION, actualización NO ACTION

## AdjuntosMensajeCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdAdjuntoMensaje | COUNTER | no |  |  |
| IdMensajeCorreo | LONG | no |  |  |
| Ordinal | SHORT | no |  |  |
| Nombre | TEXT(255) | no |  |  |
| TipoContenido | TEXT(100) | no |  |  |
| TamanoBytes | LONG | no |  |  |
| Huella | TEXT(64) | no |  |  |
| RutaRelativa | TEXT(255) | no |  |  |

Índices:
- FK_AdjuntosMensaje_Mensaje: IdMensajeCorreo
- PK_AdjuntosMensajeCorreo (clave primaria): IdAdjuntoMensaje
- UX_AdjuntosMensaje_Ordinal (único): IdMensajeCorreo, Ordinal

Relaciones:
- FK_AdjuntosMensaje_Mensaje: (IdMensajeCorreo) → MensajesCorreo (IdMensajeCorreo); borrado NO ACTION, actualización NO ACTION

## ConfiguracionCorreo

- Dueño funcional: `correo`
- Configuración del motor de envío de correo para cada empresa.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracionCorreo | COUNTER | no |  | Identificador autonumérico interno de la configuración de correo. |
| IdSociedad | TEXT(3) | no |  | Empresa a la que pertenece la cuenta remitente. Solo puede existir una configuración por empresa. |
| Activo | YESNO | no | False | Indica si el envío de correo está habilitado para esta empresa. |
| MotorCorreo | TEXT(15) | no | "CDO" | Motor utilizado para enviar: inicialmente CDO; permite sustituirlo en el futuro. |
| ServidorSMTP | TEXT(255) | no | "email.dinamica.tech" | Nombre DNS o dirección del servidor SMTP. |
| PuertoSMTP | SHORT | no | 587 | Puerto TCP del servidor SMTP; actualmente 587. |
| TipoSeguridad | TEXT(10) | no | "STARTTLS" | Seguridad de transporte: NINGUNA, STARTTLS o SSL. |
| TipoAutenticacion | TEXT(10) | no | "BASICA" | Autenticación SMTP: ANONIMA, BASICA o NTLM. |
| UsuarioSMTP | TEXT(255) | sí |  | Nombre de usuario utilizado para autenticarse en SMTP. |
| PasswordProtegida | LONGTEXT | sí |  | Contraseña SMTP cifrada mediante DPAPI. Nunca debe contener texto claro. |
| Remitente | TEXT(255) | sí |  | Dirección de correo utilizada en el encabezado From. |
| NombreRemitente | TEXT(120) | sí |  | Nombre visible asociado a la dirección remitente. |
| ResponderA | TEXT(255) | sí |  | Dirección Reply-To opcional. |
| CCPredeterminado | TEXT(255) | sí |  | Destinatarios en copia aplicados de forma predeterminada. |
| CCOPredeterminado | TEXT(255) | sí |  | Destinatarios en copia oculta aplicados de forma predeterminada. |
| AsuntoPlantilla | TEXT(255) | sí |  | Plantilla predeterminada del asunto; puede contener variables como {NumeroFactura} o {NombreEmpresa}. |
| CuerpoPlantilla | LONGTEXT | sí |  | Plantilla predeterminada del cuerpo del mensaje. |
| FormatoCuerpo | TEXT(5) | no | "HTML" | Formato del mensaje: TEXTO o HTML. |
| TimeoutSegundos | SHORT | sí | 60 | Tiempo máximo de espera de la operación SMTP, en segundos. |
| FechaModificacion | DATETIME | sí | Now() | Fecha y hora de la última modificación. |
| OperadorModificacion | TEXT(30) | sí |  | Usuario u operador que realizó la última modificación. |

Índices:
- FK_Sociedades_ConfiguracionCorreo: IdSociedad
- PK_ConfiguracionCorreo (clave primaria): IdConfiguracionCorreo
- UX_ConfiguracionCorreo_IdEmpresa (único): IdSociedad

Relaciones:
- FK_Sociedades_ConfiguracionCorreo: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## ConfiguracionMailDispatcher

- Dueño funcional: `correo`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracionMail | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| Activo | YESNO | no |  |  |
| DireccionReceptora | TEXT(254) | no |  |  |
| NombreVisible | TEXT(150) | sí |  |  |
| ServidorPop3 | TEXT(255) | no |  |  |
| PuertoPop3 | LONG | no |  |  |
| TlsPop3 | TEXT(30) | no |  |  |
| UsuarioPop3 | TEXT(254) | no |  |  |
| ServidorSmtp | TEXT(255) | no |  |  |
| PuertoSmtp | LONG | no |  |  |
| TlsSmtp | TEXT(30) | no |  |  |
| AutenticarSmtp | YESNO | no |  |  |
| UsuarioSmtp | TEXT(254) | sí |  |  |
| ReferenciaSecreto | TEXT(255) | no |  |  |
| DireccionRemitente | TEXT(254) | no |  |  |
| DireccionRespuesta | TEXT(254) | sí |  |  |
| IntervaloMinutos | LONG | no |  |  |
| EliminarTrasProcesar | YESNO | no |  |  |
| TiempoEsperaSegundos | LONG | no |  |  |
| TamanoMaximoMensajeBytes | LONG | no |  |  |
| NumeroMaximoAdjuntos | LONG | no |  |  |
| TamanoMaximoAdjuntoBytes | LONG | no |  |  |

Índices:
- FK_ConfigMail_SociedadEjercicio: IdSociedadEjercicio
- PK_ConfiguracionMail (clave primaria): IdConfiguracionMail
- UX_ConfigMail_DireccionActiva (único): DireccionReceptora
- UX_ConfigMail_SociedadEjercicio (único): IdSociedadEjercicio

Relaciones:
- FK_ConfigMail_SociedadEjercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## CuentasCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaCorreo | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| Activa | YESNO | no |  |  |
| Direccion | TEXT(254) | no |  |  |
| NombreVisible | TEXT(150) | sí |  |  |
| Usuario | TEXT(254) | sí |  |  |
| SecretoProtegido | LONGTEXT | sí |  |  |
| ResponderA | TEXT(254) | sí |  |  |
| ServidorSmtp | TEXT(255) | sí |  |  |
| PuertoSmtp | LONG | no |  |  |
| SeguridadSmtp | TEXT(15) | no |  |  |
| ServidorPop3 | TEXT(255) | sí |  |  |
| PuertoPop3 | LONG | no |  |  |
| SeguridadPop3 | TEXT(15) | no |  |  |
| EliminarTrasProcesar | YESNO | no |  |  |
| IntervaloRecepcionMinutos | LONG | no |  |  |
| TiempoEsperaSegundos | LONG | no |  |  |
| Version | LONG | no |  |  |
| FechaModificacion | DATETIME | sí |  |  |
| OperadorModificacion | TEXT(30) | sí |  |  |

Índices:
- FK_CuentasCorreo_SociedadEjercicio: IdSociedadEjercicio
- PK_CuentasCorreo (clave primaria): IdCuentaCorreo
- UX_CuentasCorreo_SociedadEjercicio (único): IdSociedadEjercicio

Relaciones:
- FK_CuentasCorreo_SociedadEjercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## DestinatariosMensajeCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDestinatarioMensaje | COUNTER | no |  |  |
| IdMensajeCorreo | LONG | no |  |  |
| Tipo | TEXT(4) | no |  |  |
| Direccion | TEXT(254) | no |  |  |
| Nombre | TEXT(150) | sí |  |  |
| Resultado | TEXT(15) | no |  |  |
| RespuestaServidor | TEXT(255) | sí |  |  |

Índices:
- FK_DestinatariosMensaje_Mensaje: IdMensajeCorreo
- IX_DestinatariosMensaje_Mensaje: IdMensajeCorreo
- PK_DestinatariosMensajeCorreo (clave primaria): IdDestinatarioMensaje

Relaciones:
- FK_DestinatariosMensaje_Mensaje: (IdMensajeCorreo) → MensajesCorreo (IdMensajeCorreo); borrado NO ACTION, actualización NO ACTION

## EnviosCorreo

- Dueño funcional: `correo`
- Historial general de intentos de envío de correo realizados por ProConta.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdEnvioCorreo | COUNTER | no |  | Identificador autonumérico del intento de envío. |
| IdSociedad | TEXT(3) | no |  | Empresa remitente del correo. |
| TipoCorreo | TEXT(20) | no | "FACTURA" | Finalidad del mensaje, definida en TiposCorreo. |
| IdFactura | LONG | sí |  | Factura relacionada cuando TipoCorreo=FACTURA; puede quedar vacío para otros mensajes. |
| NumeroIntento | SHORT | no | 1 | Número correlativo de intento para la factura o comunicación. |
| FechaHoraPreparacion | DATETIME | sí | Now() | Fecha y hora en que se preparó el mensaje. |
| FechaHoraEnvio | DATETIME | sí |  | Fecha y hora en que el motor confirmó el envío al servidor SMTP. |
| Remitente | TEXT(255) | sí |  | Dirección remitente congelada para este intento. |
| Destinatario | TEXT(255) | no |  | Destinatario o destinatarios principales congelados para este intento. |
| CC | TEXT(255) | sí |  | Destinatarios en copia congelados. |
| CCO | TEXT(255) | sí |  | Destinatarios en copia oculta congelados. |
| Asunto | TEXT(255) | no |  | Asunto definitivo enviado. |
| Cuerpo | LONGTEXT | sí |  | Cuerpo definitivo enviado. |
| FormatoCuerpo | TEXT(5) | no | "HTML" | Formato del cuerpo: TEXTO o HTML. |
| ArchivoAdjunto | TEXT(255) | sí |  | Ruta del archivo adjunto principal utilizado en el envío. |
| Estado | TEXT(10) | no | "PENDIENTE" | Estado del intento: PENDIENTE, ENVIANDO, ENVIADO, ERROR o CANCELADO. |
| MotorCorreo | TEXT(15) | sí | "CDO" | Motor que realizó el intento, inicialmente CDO. |
| IdMensajeServidor | TEXT(255) | sí |  | Identificador del mensaje si el motor o servidor SMTP lo proporciona. |
| DescripcionError | LONGTEXT | sí |  | Detalle completo del error producido durante la preparación o el envío. |
| Operador | TEXT(30) | sí |  | Usuario u operador que inició el envío. |
| IdPlantillaCorreo | LONG | sí |  | Plantilla especifica utilizada para generar el mensaje; queda vacio si se usaron los valores predeterminados o un contenido proporcionado por el emisor. |
| IdContrato | LONG | sí |  | Contrato al que corresponde el correo, cuando se trate de un aviso contractual. |

Índices:
- FK_EnviosCorreo_Contratos: IdContrato
- FK_EnviosCorreo_Facturas: IdFactura
- FK_EnviosCorreo_PlantillasCorreo: IdPlantillaCorreo
- FK_EnviosCorreo_TiposCorreo: TipoCorreo
- FK_Sociedades_EnviosCorreo: IdSociedad
- IX_EnviosCorreo_Estado: Estado
- IX_EnviosCorreo_FechaHoraEnvio: FechaHoraEnvio
- IX_EnviosCorreo_IdEmpresa: IdSociedad
- IX_EnviosCorreo_IdFactura: IdFactura
- IX_EnviosCorreo_IdPlantillaCorreo: IdPlantillaCorreo
- PK_EnviosCorreo (clave primaria): IdEnvioCorreo
- UX_EnviosCorreo_FacturaIntento (único): IdFactura, NumeroIntento

Relaciones:
- FK_EnviosCorreo_Contratos: (IdContrato) → Contratos (IdContrato); borrado NO ACTION, actualización NO ACTION
- FK_EnviosCorreo_Facturas: (IdFactura) → Facturas (IdFactura); borrado NO ACTION, actualización NO ACTION
- FK_EnviosCorreo_PlantillasCorreo: (IdPlantillaCorreo) → PlantillasCorreo (IdPlantillaCorreo); borrado NO ACTION, actualización NO ACTION
- FK_EnviosCorreo_TiposCorreo: (TipoCorreo) → TiposCorreo (TipoCorreo); borrado NO ACTION, actualización NO ACTION
- FK_Sociedades_EnviosCorreo: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## IntentosMensajeCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdIntentoMensaje | COUNTER | no |  |  |
| IdMensajeCorreo | LONG | no |  |  |
| Numero | LONG | no |  |  |
| Inicio | DATETIME | no |  |  |
| Fin | DATETIME | sí |  |  |
| Resultado | TEXT(20) | no |  |  |
| Fase | TEXT(20) | sí |  |  |
| RespuestaServidor | TEXT(255) | sí |  |  |
| Detalle | LONGTEXT | sí |  |  |
| Operador | TEXT(30) | sí |  |  |

Índices:
- FK_IntentosMensaje_Mensaje: IdMensajeCorreo
- PK_IntentosMensajeCorreo (clave primaria): IdIntentoMensaje
- UX_IntentosMensaje_Numero (único): IdMensajeCorreo, Numero

Relaciones:
- FK_IntentosMensaje_Mensaje: (IdMensajeCorreo) → MensajesCorreo (IdMensajeCorreo); borrado NO ACTION, actualización NO ACTION

## LotesCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLoteCorreo | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| Descripcion | TEXT(255) | no |  |  |
| Origen | TEXT(30) | no |  |  |
| FechaCreacion | DATETIME | no |  |  |
| Operador | TEXT(30) | no |  |  |

Índices:
- FK_LotesCorreo_SociedadEjercicio: IdSociedadEjercicio
- PK_LotesCorreo (clave primaria): IdLoteCorreo

Relaciones:
- FK_LotesCorreo_SociedadEjercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## MensajesCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdMensajeCorreo | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| IdCuentaCorreo | LONG | sí |  |  |
| IdLoteCorreo | LONG | sí |  |  |
| TipoCorreo | TEXT(20) | no |  |  |
| TipoOrigen | TEXT(20) | sí |  |  |
| IdOrigen | LONG | sí |  |  |
| ReferenciaOrigen | TEXT(150) | sí |  |  |
| MessageId | TEXT(255) | no |  |  |
| Remitente | TEXT(254) | no |  |  |
| NombreRemitente | TEXT(150) | sí |  |  |
| ResponderA | TEXT(254) | sí |  |  |
| Asunto | TEXT(255) | no |  |  |
| CuerpoHtml | LONGTEXT | sí |  |  |
| CuerpoTexto | LONGTEXT | sí |  |  |
| Estado | TEXT(20) | no |  |  |
| NumeroIntentos | LONG | no |  |  |
| ProximoIntento | DATETIME | sí |  |  |
| FechaCreacion | DATETIME | no |  |  |
| FechaAceptacion | DATETIME | sí |  |  |
| Operador | TEXT(30) | no |  |  |
| IdMensajeOriginal | LONG | sí |  |  |
| DesviadoA | TEXT(254) | sí |  |  |
| UltimoError | LONGTEXT | sí |  |  |
| ResueltoPor | TEXT(30) | sí |  |  |
| FechaResolucion | DATETIME | sí |  |  |
| MotivoResolucion | TEXT(255) | sí |  |  |
| Version | LONG | no |  |  |

Índices:
- FK_MensajesCorreo_Cuenta: IdCuentaCorreo
- FK_MensajesCorreo_Lote: IdLoteCorreo
- FK_MensajesCorreo_SociedadEjercicio: IdSociedadEjercicio
- FK_MensajesCorreo_Tipo: TipoCorreo
- IX_MensajesCorreo_Cola: Estado, ProximoIntento
- IX_MensajesCorreo_Origen: TipoOrigen, IdOrigen
- IX_MensajesCorreo_SociedadEjercicio: IdSociedadEjercicio, FechaCreacion
- PK_MensajesCorreo (clave primaria): IdMensajeCorreo
- UX_MensajesCorreo_MessageId (único): MessageId

Relaciones:
- FK_MensajesCorreo_Cuenta: (IdCuentaCorreo) → CuentasCorreo (IdCuentaCorreo); borrado NO ACTION, actualización NO ACTION
- FK_MensajesCorreo_Lote: (IdLoteCorreo) → LotesCorreo (IdLoteCorreo); borrado NO ACTION, actualización NO ACTION
- FK_MensajesCorreo_SociedadEjercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_MensajesCorreo_Tipo: (TipoCorreo) → TiposCorreo (TipoCorreo); borrado NO ACTION, actualización NO ACTION

## PlantillasCorreo

- Dueño funcional: `correo`
- Plantillas especificas de asunto y cuerpo por empresa y tipo de correo.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPlantillaCorreo | COUNTER | no |  | Identificador autonumerico interno de la plantilla. |
| IdSociedad | TEXT(3) | no |  | Empresa propietaria de la plantilla. |
| TipoCorreo | TEXT(20) | no |  | Finalidad del mensaje a la que se aplica la plantilla. |
| Asunto | TEXT(255) | no |  | Plantilla del asunto. Puede contener variables entre llaves reconocidas por el emisor. |
| Cuerpo | LONGTEXT | sí |  | Plantilla del cuerpo. Puede contener variables entre llaves reconocidas por el emisor. |
| FormatoCuerpo | TEXT(5) | no | "HTML" | Formato del cuerpo: TEXTO o HTML. |
| Activo | YESNO | no | True | Indica si la plantilla se utiliza. Si esta inactiva se usan los valores predeterminados de ConfiguracionCorreo. |
| FechaModificacion | DATETIME | sí | Now() | Fecha y hora de la ultima modificacion de la plantilla. |
| OperadorModificacion | TEXT(30) | sí |  | Usuario u operador que realizo la ultima modificacion. |

Índices:
- FK_PlantillasCorreo_TiposCorreo: TipoCorreo
- FK_Sociedades_PlantillasCorreo: IdSociedad
- IX_PlantillasCorreo_TipoCorreo: TipoCorreo
- PK_PlantillasCorreo (clave primaria): IdPlantillaCorreo
- UX_PlantillasCorreo_EmpresaTipo (único): IdSociedad, TipoCorreo

Relaciones:
- FK_PlantillasCorreo_TiposCorreo: (TipoCorreo) → TiposCorreo (TipoCorreo); borrado NO ACTION, actualización NO ACTION
- FK_Sociedades_PlantillasCorreo: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## ProcesamientosMailDispatcher

- Dueño funcional: `correo`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdProcesamientoMail | COUNTER | no |  |  |
| ClaveIdempotencia | TEXT(255) | no |  |  |
| UidlPop3 | TEXT(255) | sí |  |  |
| MessageId | TEXT(255) | sí |  |  |
| HashContenido | TEXT(64) | no |  |  |
| IdConfiguracionMail | LONG | no |  |  |
| Estado | TEXT(40) | no |  |  |
| EsErrorTransitorio | YESNO | no |  |  |
| FechaHoraInicio | DATETIME | no |  |  |
| FechaHoraFin | DATETIME | sí |  |  |
| Remitente | TEXT(254) | sí |  |  |
| Asunto | LONGTEXT | sí |  |  |
| Detalle | LONGTEXT | sí |  |  |

Índices:
- FK_ProcesamientoMail_Config: IdConfiguracionMail
- PK_ProcesamientosMail (clave primaria): IdProcesamientoMail
- UX_ProcesamientosMail_Clave (único): ClaveIdempotencia
- UX_ProcesamientosMail_Uidl (único): IdConfiguracionMail, UidlPop3

Relaciones:
- FK_ProcesamientoMail_Config: (IdConfiguracionMail) → ConfiguracionMailDispatcher (IdConfiguracionMail); borrado NO ACTION, actualización NO ACTION

## TiposCorreo

- Dueño funcional: `correo`
- Catalogo de finalidades admitidas para los mensajes enviados por ProConta.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| TipoCorreo | TEXT(20) | no |  | Codigo estable del tipo de mensaje: FACTURA, CONTRATO, INCREMENTO IPC, AVISO u OTRO. |
| Descripcion | TEXT(120) | no |  | Descripcion funcional del tipo de correo. |
| Activo | YESNO | no | True | Indica si el tipo se puede seleccionar para nuevos mensajes. |

Índices:
- PK_TiposCorreo (clave primaria): TipoCorreo

## TurnosCorreo

- Dueño funcional: `correo/envio`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| Clave | TEXT(30) | no |  |  |
| Propietario | TEXT(100) | no |  |  |
| CaducaEn | DATETIME | no |  |  |

Índices:
- PK_TurnosCorreo (clave primaria): Clave

