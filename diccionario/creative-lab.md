<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · creative-lab

## ActivosInversiones

- Dueño funcional: `creative-lab/inversiones-financieras`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdActivo | LONG | sí |  |  |
| IdInstrumentoFinanciero | LONG | no |  |  |
| ParticipacionesActuales | DECIMAL | no |  |  |

Índices:
- PK_ActivosInversiones (clave primaria): IdActivo

## ConversacionesIA

- Dueño funcional: `creative-lab/asistente-ia`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConversacion | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| Titulo | TEXT(150) | no |  |  |
| FechaCreacion | DATETIME | no |  |  |
| FechaActualizacion | DATETIME | no |  |  |
| EstaActiva | YESNO | no |  |  |

Índices:
- IX_ConversacionesIA_SociedadFecha: IdSociedad, FechaActualizacion
- PK_ConversacionesIA (clave primaria): IdConversacion

## CotizacionesInstrumentoFinanciero

- Dueño funcional: `creative-lab/inversiones-financieras`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCotizacionInstrumento | COUNTER | no |  |  |
| IdInstrumentoFinanciero | LONG | no |  |  |
| FechaCotizacion | DATETIME | no |  |  |
| Precio | DECIMAL | no |  |  |
| Moneda | TEXT(3) | no |  |  |
| Origen | TEXT(50) | sí |  |  |
| FechaHoraImportacion | DATETIME | no |  |  |

Índices:
- PK_CotizacionesInstrumento (clave primaria): IdCotizacionInstrumento
- UX_CotizacionesInstrumento_Fecha (único): IdInstrumentoFinanciero, FechaCotizacion

## ImportacionesActivos

- Dueño funcional: `creative-lab/inversiones-financieras`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdImportacionActivo | COUNTER | no |  |  |
| TipoOrigen | TEXT(30) | no |  |  |
| RutaOrigen | TEXT(255) | no |  |  |
| IdSociedad | TEXT(10) | no |  |  |
| FechaHora | DATETIME | no |  |  |
| HuellaOrigen | TEXT(64) | no |  |  |
| RegistrosLeidos | LONG | no |  |  |
| RegistrosCreados | LONG | no |  |  |
| RegistrosOmitidos | LONG | no |  |  |
| Observaciones | LONGTEXT | sí |  |  |

Índices:
- PK_ImportacionesActivos (clave primaria): IdImportacionActivo
- UX_ImportacionesActivos_Huella (único): TipoOrigen, IdSociedad, HuellaOrigen

## InstrumentosFinancieros

- Dueño funcional: `creative-lab/inversiones-financieras`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdInstrumentoFinanciero | COUNTER | no |  |  |
| Nombre | TEXT(255) | no |  |  |
| Isin | TEXT(12) | sí |  |  |
| Ticker | TEXT(30) | sí |  |  |
| TipoInstrumento | TEXT(40) | no |  |  |
| MonedaCotizacion | TEXT(3) | no |  |  |
| UrlInformacion | TEXT(255) | sí |  | Página pública de referencia; no garantiza que ofrezca descarga histórica automatizable. |
| RolCartera | TEXT(50) | sí |  |  |
| EsCandidato | YESNO | no |  | El instrumento pertenece al universo propuesto pero no implica que exista una inversión. |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_InstrumentosFinancieros (clave primaria): IdInstrumentoFinanciero
- UX_InstrumentosFinancieros_Isin (único): Isin

## MensajesConversacionIA

- Dueño funcional: `creative-lab/asistente-ia`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdMensaje | COUNTER | no |  |  |
| IdConversacion | LONG | no |  |  |
| OrdenMensaje | LONG | no |  |  |
| Rol | BYTE | no |  |  |
| Contenido | LONGTEXT | no |  |  |
| FechaHora | DATETIME | no |  |  |

Índices:
- PK_MensajesConversacionIA (clave primaria): IdMensaje
- UX_MensajesConversacionIA_Orden (único): IdConversacion, OrdenMensaje

## OperacionesInversion

- Dueño funcional: `creative-lab/inversiones-financieras`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdOperacionInversion | COUNTER | no |  |  |
| IdActivo | LONG | no |  |  |
| Tipo | BYTE | no |  |  |
| FechaOperacion | DATETIME | no |  |  |
| Participaciones | DECIMAL | no |  |  |
| PrecioUnitario | DECIMAL | no |  |  |
| Moneda | TEXT(3) | no |  |  |
| TipoCambioEuro | DECIMAL | no |  |  |
| GastosEur | CURRENCY | no |  |  |
| ImporteTotalEur | CURRENCY | no |  |  |
| ReferenciaExterna | TEXT(100) | sí |  |  |
| Notas | LONGTEXT | sí |  |  |

Índices:
- PK_OperacionesInversion (clave primaria): IdOperacionInversion
- UX_OperacionesInversion_Referencia (único): IdActivo, ReferenciaExterna

