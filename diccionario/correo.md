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

## ConfiguracionCorreo

- Dueño funcional: `correo`
- ConfiguraciÃ³n del motor de envÃ­o de correo para cada empresa.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracionCorreo | COUNTER | no |  | Identificador autonumÃ©rico interno de la configuraciÃ³n de correo. |
| IdSociedad | TEXT(3) | no |  | Empresa a la que pertenece la cuenta remitente. Solo puede existir una configuraciÃ³n por empresa. |
| Activo | YESNO | no | False | Indica si el envÃ­o de correo estÃ¡ habilitado para esta empresa. |
| MotorCorreo | TEXT(15) | no | "CDO" | Motor utilizado para enviar: inicialmente CDO; permite sustituirlo en el futuro. |
| ServidorSMTP | TEXT(255) | no | "email.dinamica.tech" | Nombre DNS o direcciÃ³n del servidor SMTP. |
| PuertoSMTP | SHORT | no | 587 | Puerto TCP del servidor SMTP; actualmente 587. |
| TipoSeguridad | TEXT(10) | no | "STARTTLS" | Seguridad de transporte: NINGUNA, STARTTLS o SSL. |
| TipoAutenticacion | TEXT(10) | no | "BASICA" | AutenticaciÃ³n SMTP: ANONIMA, BASICA o NTLM. |
| UsuarioSMTP | TEXT(255) | sí |  | Nombre de usuario utilizado para autenticarse en SMTP. |
| PasswordProtegida | LONGTEXT | sí |  | ContraseÃ±a SMTP cifrada mediante DPAPI. Nunca debe contener texto claro. |
| Remitente | TEXT(255) | sí |  | DirecciÃ³n de correo utilizada en el encabezado From. |
| NombreRemitente | TEXT(120) | sí |  | Nombre visible asociado a la direcciÃ³n remitente. |
| ResponderA | TEXT(255) | sí |  | DirecciÃ³n Reply-To opcional. |
| CCPredeterminado | TEXT(255) | sí |  | Destinatarios en copia aplicados de forma predeterminada. |
| CCOPredeterminado | TEXT(255) | sí |  | Destinatarios en copia oculta aplicados de forma predeterminada. |
| AsuntoPlantilla | TEXT(255) | sí |  | Plantilla predeterminada del asunto; puede contener variables como {NumeroFactura} o {NombreEmpresa}. |
| CuerpoPlantilla | LONGTEXT | sí |  | Plantilla predeterminada del cuerpo del mensaje. |
| FormatoCuerpo | TEXT(5) | no | "HTML" | Formato del mensaje: TEXTO o HTML. |
| TimeoutSegundos | SHORT | sí | 60 | Tiempo mÃ¡ximo de espera de la operaciÃ³n SMTP, en segundos. |
| FechaModificacion | DATETIME | sí | Now() | Fecha y hora de la Ãºltima modificaciÃ³n. |
| OperadorModificacion | TEXT(30) | sí |  | Usuario u operador que realizÃ³ la Ãºltima modificaciÃ³n. |

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

## EnviosCorreo

- Dueño funcional: `correo`
- Historial general de intentos de envÃ­o de correo realizados por ProConta.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdEnvioCorreo | COUNTER | no |  | Identificador autonumÃ©rico del intento de envÃ­o. |
| IdSociedad | TEXT(3) | no |  | Empresa remitente del correo. |
| TipoCorreo | TEXT(20) | no | "FACTURA" | Finalidad del mensaje, definida en TiposCorreo. |
| IdFactura | LONG | sí |  | Factura relacionada cuando TipoCorreo=FACTURA; puede quedar vacÃ­o para otros mensajes. |
| NumeroIntento | SHORT | no | 1 | NÃºmero correlativo de intento para la factura o comunicaciÃ³n. |
| FechaHoraPreparacion | DATETIME | sí | Now() | Fecha y hora en que se preparÃ³ el mensaje. |
| FechaHoraEnvio | DATETIME | sí |  | Fecha y hora en que el motor confirmÃ³ el envÃ­o al servidor SMTP. |
| Remitente | TEXT(255) | sí |  | DirecciÃ³n remitente congelada para este intento. |
| Destinatario | TEXT(255) | no |  | Destinatario o destinatarios principales congelados para este intento. |
| CC | TEXT(255) | sí |  | Destinatarios en copia congelados. |
| CCO | TEXT(255) | sí |  | Destinatarios en copia oculta congelados. |
| Asunto | TEXT(255) | no |  | Asunto definitivo enviado. |
| Cuerpo | LONGTEXT | sí |  | Cuerpo definitivo enviado. |
| FormatoCuerpo | TEXT(5) | no | "HTML" | Formato del cuerpo: TEXTO o HTML. |
| ArchivoAdjunto | TEXT(255) | sí |  | Ruta del archivo adjunto principal utilizado en el envÃ­o. |
| Estado | TEXT(10) | no | "PENDIENTE" | Estado del intento: PENDIENTE, ENVIANDO, ENVIADO, ERROR o CANCELADO. |
| MotorCorreo | TEXT(15) | sí | "CDO" | Motor que realizÃ³ el intento, inicialmente CDO. |
| IdMensajeServidor | TEXT(255) | sí |  | Identificador del mensaje si el motor o servidor SMTP lo proporciona. |
| DescripcionError | LONGTEXT | sí |  | Detalle completo del error producido durante la preparaciÃ³n o el envÃ­o. |
| Operador | TEXT(30) | sí |  | Usuario u operador que iniciÃ³ el envÃ­o. |
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

