<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · activos

## Activos

- Dueño funcional: `activos`

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
| IdCentroCoste | LONG | sí |  |  |
| EsAjeno | YESNO | no | False |  |

Índices:
- FK_Activos_CentroCoste: IdCentroCoste
- IX_Activos_CentroCoste: IdCentroCoste
- IX_Activos_SociedadEstado: IdSociedad, Estado
- PK_Activos (clave primaria): IdActivo
- UX_Activos_SociedadCodigo (único): IdSociedad, Codigo

Relaciones:
- FK_Activos_CentroCoste: (IdCentroCoste) → CentrosCoste (IdCentroCoste); borrado NO ACTION, actualización NO ACTION

## ActivosInmuebles

- Dueño funcional: `activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdActivo | LONG | no |  |  |
| ReferenciaCatastral | TEXT(30) | sí |  |  |
| ValorCatastralSuelo | CURRENCY | sí |  |  |
| ValorCatastralConstruccion | CURRENCY | sí |  |  |
| ValorCatastralTotal | CURRENCY | sí |  |  |
| PorcentajeConstruccion | DECIMAL | sí |  |  |
| ValorCompra | CURRENCY | sí |  |  |
| CosteCompra | CURRENCY | sí |  |  |
| PrecioAlquiler | CURRENCY | sí |  |  |
| FechaInicioAlquiler | DATETIME | sí |  |  |
| TributoMetropolitano | CURRENCY | sí |  |  |

Índices:
- FK_ActivosInmuebles_Activo: IdActivo
- IX_ActivosInmuebles_ReferenciaCatastral: ReferenciaCatastral
- PK_ActivosInmuebles (clave primaria): IdActivo

Relaciones:
- FK_ActivosInmuebles_Activo: (IdActivo) → Activos (IdActivo); borrado NO ACTION, actualización NO ACTION

## CuentasActivo

- Dueño funcional: `activos`

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

- Dueño funcional: `activos`

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

## Inmuebles

- Dueño funcional: `activos`
- Tabla **vinculada** a otro fichero: su estructura no se lee desde la maestra.

## LotesAmortizacionActivo

- Dueño funcional: `activos`

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

## PlanesAmortizacionActivo

- Dueño funcional: `activos`

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
| PorcentajeAnual | DECIMAL | sí |  |  |

Índices:
- IX_PlanesAmortizacionActivo_Activo: IdActivo
- PK_PlanesAmortizacionActivo (clave primaria): IdPlanAmortizacion

## PlantillasCuentasActivo

- Dueño funcional: `activos`

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

## PlantillasSubcuentasActivo

- Dueño funcional: `activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPlantillaSubcuentaActivo | COUNTER | no |  |  |
| IdTipoActivo | LONG | no |  |  |
| IdTipoSubcuentaActivo | LONG | no |  |  |
| Prefijo | TEXT(7) | no |  |  |
| Orden | SHORT | no |  |  |
| EstaActiva | YESNO | no |  |  |

Índices:
- FK_PlantillasSubcuentasActivo_Tipo: IdTipoActivo
- FK_PlantillasSubcuentasActivo_TipoSubcuenta: IdTipoSubcuentaActivo
- IX_PlantillasSubcuentasActivo_TipoSubcuenta: IdTipoSubcuentaActivo
- PK_PlantillasSubcuentasActivo (clave primaria): IdPlantillaSubcuentaActivo
- UX_PlantillasSubcuentasActivo_Linea (único): IdTipoActivo, IdTipoSubcuentaActivo

Relaciones:
- FK_PlantillasSubcuentasActivo_Tipo: (IdTipoActivo) → TiposActivo (IdTipoActivo); borrado NO ACTION, actualización NO ACTION
- FK_PlantillasSubcuentasActivo_TipoSubcuenta: (IdTipoSubcuentaActivo) → TiposSubcuentaActivo (IdTipoSubcuentaActivo); borrado NO ACTION, actualización NO ACTION

## SubcuentasActivo

- Dueño funcional: `activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdSubcuentaActivo | COUNTER | no |  |  |
| IdActivo | LONG | no |  |  |
| IdPlantillaSubcuentaActivo | LONG | no |  |  |
| IdIdentidadContable | LONG | no |  |  |
| FechaHoraCreacion | DATETIME | no |  |  |

Índices:
- FK_SubcuentasActivo_Activo: IdActivo
- FK_SubcuentasActivo_Identidad: IdIdentidadContable
- FK_SubcuentasActivo_Plantilla: IdPlantillaSubcuentaActivo
- IX_SubcuentasActivo_Identidad: IdIdentidadContable
- IX_SubcuentasActivo_PlantillaLinea: IdPlantillaSubcuentaActivo
- PK_SubcuentasActivo (clave primaria): IdSubcuentaActivo
- UX_SubcuentasActivo_Plantilla (único): IdActivo, IdPlantillaSubcuentaActivo

Relaciones:
- FK_SubcuentasActivo_Activo: (IdActivo) → Activos (IdActivo); borrado NO ACTION, actualización NO ACTION
- FK_SubcuentasActivo_Identidad: (IdIdentidadContable) → IdentidadesContables (IdIdentidadContable); borrado NO ACTION, actualización NO ACTION
- FK_SubcuentasActivo_Plantilla: (IdPlantillaSubcuentaActivo) → PlantillasSubcuentasActivo (IdPlantillaSubcuentaActivo); borrado NO ACTION, actualización NO ACTION

## TiposActivo

- Dueño funcional: `activos`

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

## TiposSubcuentaActivo

- Dueño funcional: `activos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTipoSubcuentaActivo | COUNTER | no |  |  |
| Nombre | TEXT(255) | no |  |  |
| Descripcion | TEXT(255) | sí |  |  |
| Rol | TEXT(30) | no |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_TiposSubcuentaActivo (clave primaria): IdTipoSubcuentaActivo
- UX_TiposSubcuentaActivo_Nombre (único): Nombre

