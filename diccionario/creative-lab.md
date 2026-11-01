<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · creative-lab

## Activos

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdActivo | COUNTER | no |  |  |
| IdSociedad | TEXT(10) | no |  |  |
| IdTipoActivo | LONG | no |  |  |
| Codigo | TEXT(40) | no |  |  |
| Nombre | TEXT(255) | no |  |  |
| AliasActivo | TEXT(100) | sí |  |  |
| Descripcion | LONGTEXT | sí |  |  |
| FechaAdquisicion | DATETIME | sí |  |  |
| FechaAlta | DATETIME | no |  |  |
| FechaBaja | DATETIME | sí |  |  |
| Estado | BYTE | no |  |  |
| EsAmortizable | YESNO | no |  |  |
| CosteAdquisicionActual | CURRENCY | no |  | Caché reconstruible desde operaciones y movimientos capitalizables; no es la fuente primaria. |
| AmortizacionAcumulada | CURRENCY | no |  | Caché del saldo oficial de la identidad contable con rol AMORTIZACION_ACUMULADA. |
| DeterioroAcumulado | CURRENCY | no |  |  |
| ValorContable | CURRENCY | no |  | Coste menos amortización y deterioro; valor materializado para consulta y reporting. |
| ValorActual | CURRENCY | sí |  |  |
| ResultadoNoRealizado | CURRENCY | sí |  |  |
| FechaUltimaValoracion | DATETIME | sí |  |  |
| FechaHoraUltimoCalculo | DATETIME | sí |  |  |
| EstadoCalculo | BYTE | no |  |  |
| IdDepartamento | LONG | sí |  |  |
| IdSubDepartamento | LONG | sí |  |  |
| IdFacturaCompra | LONG | sí |  |  |
| ReferenciaMigracion | TEXT(100) | sí |  |  |
| Version | LONG | no |  | Control de concurrencia optimista; aumenta en cada modificación. |

Índices:
- IX_Activos_SociedadEstado: IdSociedad, Estado
- PK_Activos (clave primaria): IdActivo
- UX_Activos_SociedadCodigo (único): IdSociedad, Codigo

## ActivosInversiones

- Dueño funcional: `creative-lab/activos`

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

- Dueño funcional: `creative-lab/activos`

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

## CuentasActivo

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaActivo | COUNTER | no |  |  |
| IdActivo | LONG | no |  |  |
| IdPlantillaCuentaActivo | LONG | no |  |  |
| IdIdentidadContable | LONG | no |  |  |
| FechaHoraCreacion | DATETIME | no |  |  |

Índices:
- PK_CuentasActivo (clave primaria): IdCuentaActivo
- UX_CuentasActivo_Plantilla (único): IdActivo, IdPlantillaCuentaActivo

## DetallesLoteAmortizacionActivo

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDetalleLoteAmortizacion | COUNTER | no |  |  |
| IdLoteAmortizacion | LONG | no |  |  |
| IdActivo | LONG | no |  |  |
| Importe | CURRENCY | no |  |  |
| IdAsiento | LONG | sí |  |  |
| Estado | TEXT(20) | no |  |  |
| Incidencia | LONGTEXT | sí |  |  |

Índices:
- PK_DetallesLoteAmortizacion (clave primaria): IdDetalleLoteAmortizacion
- UX_DetallesLoteAmortizacion_Activo (único): IdLoteAmortizacion, IdActivo

## ImportacionesActivos

- Dueño funcional: `creative-lab/activos`

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

## Inmuebles

- Dueño funcional: `creative-lab/activos`
- Tabla **vinculada** a otro fichero: su estructura no se lee desde la maestra.

## InstrumentosFinancieros

- Dueño funcional: `creative-lab/activos`

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

## LotesAmortizacionActivo

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLoteAmortizacion | COUNTER | no |  |  |
| IdSociedad | TEXT(10) | no |  |  |
| FechaDesde | DATETIME | no |  |  |
| FechaHasta | DATETIME | no |  |  |
| Estado | TEXT(20) | no |  |  |
| IdLoteProceso | TEXT(100) | no |  |  |
| FechaHoraCreacion | DATETIME | no |  |  |

Índices:
- PK_LotesAmortizacionActivo (clave primaria): IdLoteAmortizacion
- UX_LotesAmortizacionActivo_Proceso (único): IdLoteProceso

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

- Dueño funcional: `creative-lab/activos`

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

## PlanesAmortizacionActivo

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPlanAmortizacion | COUNTER | no |  |  |
| IdActivo | LONG | no |  |  |
| FechaInicio | DATETIME | no |  |  |
| BaseAmortizable | CURRENCY | no |  |  |
| ValorResidual | CURRENCY | no |  |  |
| VidaUtilMeses | LONG | no |  |  |
| Metodo | TEXT(20) | no |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_PlanesAmortizacionActivo (clave primaria): IdPlanAmortizacion

## PlantillasCuentasActivo

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPlantillaCuentaActivo | COUNTER | no |  |  |
| IdTipoActivo | LONG | no |  |  |
| RolCuenta | TEXT(40) | no |  | Finalidad semántica de la cuenta; evita deducir funciones a partir del prefijo contable. |
| PrefijoCuenta | TEXT(20) | no |  |  |
| PlantillaNombre | TEXT(255) | no |  |  |
| Orden | SHORT | no |  |  |
| EsObligatoria | YESNO | no |  |  |
| EstaActiva | YESNO | no |  |  |

Índices:
- PK_PlantillasCuentasActivo (clave primaria): IdPlantillaCuentaActivo
- UX_PlantillasCuentasActivo_Rol (único): IdTipoActivo, RolCuenta

## TiposActivo

- Dueño funcional: `creative-lab/activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTipoActivo | COUNTER | no |  |  |
| Codigo | TEXT(30) | no |  |  |
| Nombre | TEXT(100) | no |  |  |
| Categoria | TEXT(30) | no |  |  |
| EsAmortizablePredeterminado | YESNO | no |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_TiposActivo (clave primaria): IdTipoActivo
- UX_TiposActivo_Codigo (único): Codigo

