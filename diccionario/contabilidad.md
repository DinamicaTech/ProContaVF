<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · contabilidad

## Apuntes

- Dueño funcional: `contabilidad/asientos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdApunte | COUNTER | no |  |  |
| IdAsiento | LONG | no |  |  |
| Orden | SHORT | no |  |  |
| CuentaContable | TEXT(20) | no |  |  |
| ImporteDebe | CURRENCY | sí |  | Importe positivo o nulo; exactamente un lado del apunte debe estar informado. |
| ImporteHaber | CURRENCY | sí |  | Importe positivo o nulo; exactamente un lado del apunte debe estar informado. |
| Descripcion | TEXT(255) | sí |  |  |
| NumeroDocumento | TEXT(100) | sí |  |  |
| Documento | LONGTEXT | sí |  |  |
| IdCliente | LONG | sí |  |  |
| IdProveedor | LONG | sí |  |  |
| FechaVencimiento | DATETIME | sí |  |  |
| IdCentroCoste | LONG | sí |  | Único nodo analítico; su ruta determina todos los niveles superiores. |
| TipoApunte | BYTE | no |  | Distingue movimientos ordinarios y ajustes técnicos de redondeo. |
| GeneradoAutomaticamente | YESNO | no |  |  |
| MotivoGeneracion | TEXT(255) | sí |  |  |
| DiferenciaOrigen | CURRENCY | sí |  | Descuadre previo que justificó un ajuste automático. |

Índices:
- FK_Apuntes_Asientos: IdAsiento
- FK_Apuntes_CentrosCoste: IdCentroCoste
- IX_Apuntes_CentroCoste: IdCentroCoste
- IX_Apuntes_Cliente: IdCliente
- IX_Apuntes_CuentaAsiento: CuentaContable, IdAsiento
- IX_Apuntes_Proveedor: IdProveedor
- IX_Apuntes_Vencimiento: FechaVencimiento
- PK_Apuntes (clave primaria): IdApunte
- UX_Apuntes_AsientoOrden (único): IdAsiento, Orden

Relaciones:
- FK_Apuntes_Asientos: (IdAsiento) → Asientos (IdAsiento); borrado CASCADE, actualización NO ACTION
- FK_Apuntes_CentrosCoste: (IdCentroCoste) → CentrosCoste (IdCentroCoste); borrado NO ACTION, actualización NO ACTION

## Asientos

- Dueño funcional: `contabilidad/asientos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdAsiento | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| FechaContable | DATETIME | no |  | Fecha efectiva que determina ejercicio, períodos y obligaciones afectadas. |
| Descripcion | TEXT(255) | no |  |  |
| TipoAsiento | BYTE | no |  |  |
| TipoAsientoManual | BYTE | sí |  |  |
| Notas | LONGTEXT | sí |  |  |
| EstadoAsiento | BYTE | no |  |  |
| OperadorCreacion | TEXT(100) | no |  |  |
| FechaHoraCreacion | DATETIME | no |  |  |
| OperadorModificacion | TEXT(100) | sí |  |  |
| FechaHoraModificacion | DATETIME | sí |  |  |
| OperadorContabilizacion | TEXT(100) | sí |  |  |
| FechaHoraContabilizacion | DATETIME | sí |  |  |
| TotalDebe | CURRENCY | no |  | Instantánea de control recalculada desde los apuntes. |
| TotalHaber | CURRENCY | no |  | Instantánea de control recalculada desde los apuntes. |
| IdLoteProceso | LONG | sí |  |  |
| Version | LONG | no |  | Número de concurrencia optimista incrementado en cada revisión. |

Índices:
- FK_Asientos_SociedadesEjercicios: IdSociedadEjercicio
- IX_Asientos_Diario: IdSociedadEjercicio, FechaContable, IdAsiento
- IX_Asientos_Estado: IdSociedadEjercicio, EstadoAsiento
- IX_Asientos_Lote: IdLoteProceso
- PK_Asientos (clave primaria): IdAsiento

Relaciones:
- FK_Asientos_SociedadesEjercicios: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## CentrosCoste

- Dueño funcional: `contabilidad/centros-coste`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCentroCoste | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| IdCentroCostePadre | LONG | sí |  | Nodo superior; nulo para una raíz. |
| Codigo | TEXT(30) | no |  |  |
| Nombre | TEXT(150) | no |  |  |
| Descripcion | LONGTEXT | sí |  |  |
| EsImputable | YESNO | no |  | Indica si el nodo puede asignarse directamente a un apunte. |
| EstaActivo | YESNO | no |  |  |
| Orden | SHORT | no |  |  |
| Nivel | BYTE | no |  | Profundidad derivada; cero identifica una raíz. |
| RutaJerarquica | TEXT(255) | no |  | Ruta materializada de identidades para consultar descendientes sin recursividad. |
| RutaOrden | TEXT(255) | no |  | Ruta materializada de órdenes para mostrar el árbol con ORDER BY. |
| IdCentroCosteRaiz | LONG | sí |  |  |

Índices:
- FK_CentrosCoste_Padre: IdCentroCostePadre
- FK_CentrosCoste_Sociedades: IdSociedad
- IX_CentrosCoste_Raiz: IdCentroCosteRaiz
- IX_CentrosCoste_RutaOrden: IdSociedad, RutaOrden
- IX_CentrosCoste_SociedadPadre: IdSociedad, IdCentroCostePadre, Orden
- PK_CentrosCoste (clave primaria): IdCentroCoste
- UX_CentrosCoste_Ruta (único): IdSociedad, RutaJerarquica

Relaciones:
- FK_CentrosCoste_Padre: (IdCentroCostePadre) → CentrosCoste (IdCentroCoste); borrado NO ACTION, actualización NO ACTION
- FK_CentrosCoste_Sociedades: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## CodigosContablesReservados

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCodigoContableReservado | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| Codigo | TEXT(30) | no |  | Código que no puede asignarse históricamente a otra identidad de la sociedad. |
| IdIdentidadContable | LONG | no |  |  |
| FechaPrimeraAsignacion | DATETIME | no |  |  |

Índices:
- PK_CodigosContablesReservados (clave primaria): IdCodigoContableReservado
- UX_CodigosReservados_SociedadCodigo (único): IdSociedad, Codigo

## ConfiguracionesContables

- Dueño funcional: `contabilidad/procesos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdSociedad | TEXT(3) | no |  |  |
| ToleranciaRedondeo | CURRENCY | no |  | Diferencia máxima en euros que CAsiento puede cuadrar automáticamente. |
| CuentaGastosRedondeo | TEXT(20) | no |  | Cuenta utilizada en el Debe cuando el Haber supera al Debe dentro de la tolerancia. |
| CuentaIngresosRedondeo | TEXT(20) | no |  | Cuenta utilizada en el Haber cuando el Debe supera al Haber dentro de la tolerancia. |
| PermitirAjusteAutomatico | YESNO | no |  |  |
| PoliticaModificacionAsientos | BYTE | no |  | Política de edición: estricta o flexible con auditoría, según el catálogo de dominio. |

Índices:
- FK_ConfiguracionesContables_Sociedades: IdSociedad
- PK_ConfiguracionesContables (clave primaria): IdSociedad

Relaciones:
- FK_ConfiguracionesContables_Sociedades: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## ConfiguracionesContablesCuentaBancaria

- Dueño funcional: `contabilidad/procesos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracionCuentaBancaria | COUNTER | no |  |  |
| IdCuentaBancaria | LONG | no |  |  |
| IdIdentidadContableBanco | LONG | no |  |  |
| IdIdentidadCuentaPuente | LONG | sí |  |  |
| IdIdentidadSplit | LONG | sí |  |  |
| IdIdentidadGastosDivisa | LONG | sí |  |  |
| FechaDesde | DATETIME | no |  |  |
| FechaHasta | DATETIME | sí |  |  |

Índices:
- FK_ConfigBanco_Cuenta: IdCuentaBancaria
- FK_ConfigBanco_Identidad: IdIdentidadContableBanco
- PK_ConfigCuentaBancaria (clave primaria): IdConfiguracionCuentaBancaria

Relaciones:
- FK_ConfigBanco_Cuenta: (IdCuentaBancaria) → CuentasBancarias (IdCuentaBancaria); borrado NO ACTION, actualización NO ACTION
- FK_ConfigBanco_Identidad: (IdIdentidadContableBanco) → IdentidadesContables (IdIdentidadContable); borrado NO ACTION, actualización NO ACTION

## CuentasContables

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaContable | COUNTER | no |  |  |
| IdIdentidadContable | LONG | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| IdCuentaPadre | LONG | sí |  | Cuenta superior del mismo ejercicio; no exige niveles intermedios. |
| Codigo | TEXT(30) | no |  | Número visible único dentro del ejercicio. |
| Nombre | TEXT(150) | no |  |  |
| Descripcion | LONGTEXT | sí |  |  |
| EstaActiva | YESNO | no |  |  |
| EstaBloqueada | YESNO | no |  |  |
| EsImputable | YESNO | no |  |  |
| IdCentroCostePredeterminado | LONG | sí |  |  |
| SaldoInicial | CURRENCY | no |  | Caché reconstruible desde el asiento de apertura. |
| SaldoActual | CURRENCY | no |  | Caché reconstruible que excluye el asiento de cierre. |
| NumeroApuntes | LONG | no |  | Caché reconstruible para selección y diagnóstico. |

Índices:
- IX_Cuentas_IdentidadEjercicio: IdIdentidadContable, IdSociedadEjercicio
- IX_Cuentas_Padre: IdCuentaPadre
- PK_CuentasContables (clave primaria): IdCuentaContable
- UX_Cuentas_EjercicioCodigo (único): IdSociedadEjercicio, Codigo

## CuentasContablesLegacy

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| Codigo | TEXT(10) | sí |  |  |
| Descripcion | TEXT(255) | sí |  |  |

## CuentasPlanContable

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaPlanContable | COUNTER | no |  |  |
| IdPlanContable | LONG | no |  |  |
| IdCuentaPadre | LONG | sí |  |  |
| IdTipoCuentaContable | LONG | sí |  |  |
| Codigo | TEXT(30) | no |  |  |
| Nombre | TEXT(150) | no |  |  |
| Descripcion | LONGTEXT | sí |  |  |
| Naturaleza | BYTE | no |  |  |
| EsImputable | YESNO | no |  |  |

Índices:
- PK_CuentasPlanContable (clave primaria): IdCuentaPlanContable
- UX_CuentasPlan_Codigo (único): IdPlanContable, Codigo

## F_DEP

- Dueño funcional: `contabilidad/centros-coste`
- Tabla **vinculada** a otro fichero: su estructura no se lee desde la maestra.

## IdentidadesContables

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdIdentidadContable | COUNTER | no |  | Identidad estable que enlaza cuentas equivalentes entre ejercicios. |
| IdSociedad | TEXT(3) | no |  |  |
| IdTipoCuentaContable | LONG | sí |  |  |
| Naturaleza | BYTE | no |  |  |
| FechaAlta | DATETIME | no |  |  |
| FechaBaja | DATETIME | sí |  |  |

Índices:
- PK_IdentidadesContables (clave primaria): IdIdentidadContable

## IVARepercutido

- Dueño funcional: `contabilidad/procesos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdIVARepercutido | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| IdCliente | LONG | sí |  |  |
| IdAsiento | LONG | sí |  |  |
| FechaExpedicion | DATETIME | no |  |  |
| FechaOperacion | DATETIME | no |  |  |
| FechaHoraRegistroContable | DATETIME | no |  |  |
| IdPeriodoIVA | LONG | no |  |  |
| NumeroFactura | TEXT(100) | sí |  |  |
| NombreCliente | TEXT(150) | sí |  |  |
| IdentificacionFiscalCliente | TEXT(30) | sí |  |  |
| PaisCliente | TEXT(3) | sí |  |  |
| IdTipoOperacion | LONG | no |  |  |
| ImporteTotalFactura | CURRENCY | no |  |  |
| BaseImponibleTotal | CURRENCY | no |  |  |
| CuotaRepercutidaTotal | CURRENCY | no |  |  |
| RecargoEquivalenciaTotal | CURRENCY | no |  |  |
| IncluidoModelo347 | YESNO | no |  |  |
| Estado | TEXT(20) | no |  |  |
| Descripcion | LONGTEXT | sí |  |  |
| OperadorCreacion | TEXT(100) | sí |  |  |
| FechaHoraCreacion | DATETIME | no |  |  |

Índices:
- FK_IVAR_Asiento: IdAsiento
- FK_IVAR_Cliente: IdCliente
- FK_IVAR_Ejercicio: IdSociedadEjercicio
- FK_IVAR_Periodo: IdPeriodoIVA
- FK_IVAR_Tipo: IdTipoOperacion
- PK_IVARepercutido (clave primaria): IdIVARepercutido

Relaciones:
- FK_IVAR_Asiento: (IdAsiento) → Asientos (IdAsiento); borrado NO ACTION, actualización NO ACTION
- FK_IVAR_Cliente: (IdCliente) → Clientes (IdCliente); borrado NO ACTION, actualización NO ACTION
- FK_IVAR_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_IVAR_Periodo: (IdPeriodoIVA) → PeriodosIVA (IdPeriodoIVA); borrado NO ACTION, actualización NO ACTION
- FK_IVAR_Tipo: (IdTipoOperacion) → TiposOperacion (IdTipoOperacion); borrado NO ACTION, actualización NO ACTION

## IVASoportado

- Dueño funcional: `contabilidad/procesos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdIVASoportado | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| IdProveedor | LONG | sí |  |  |
| IdAsiento | LONG | sí |  |  |
| FechaExpedicion | DATETIME | no |  |  |
| FechaOperacion | DATETIME | no |  |  |
| FechaHoraRegistroContable | DATETIME | no |  |  |
| IdPeriodoIVA | LONG | no |  |  |
| NumeroFactura | TEXT(100) | sí |  |  |
| NombreProveedor | TEXT(150) | sí |  |  |
| IdentificacionFiscalProveedor | TEXT(30) | sí |  |  |
| PaisProveedor | TEXT(3) | sí |  |  |
| IdTipoOperacion | LONG | no |  |  |
| TipoBienServicio | TEXT(20) | sí |  |  |
| InversionSujetoPasivo | YESNO | no |  |  |
| ImporteTotalFactura | CURRENCY | no |  |  |
| BaseImponibleTotal | CURRENCY | no |  |  |
| CuotaSoportadaTotal | CURRENCY | no |  |  |
| CuotaDevengadaAutorrepercutidaTotal | CURRENCY | no |  |  |
| CuotaDeducibleTotal | CURRENCY | no |  |  |
| IncluidoModelo347 | YESNO | no |  |  |
| Estado | TEXT(20) | no |  |  |
| Descripcion | LONGTEXT | sí |  |  |
| OperadorCreacion | TEXT(100) | sí |  |  |
| FechaHoraCreacion | DATETIME | no |  |  |

Índices:
- FK_IVAS_Asiento: IdAsiento
- FK_IVAS_Ejercicio: IdSociedadEjercicio
- FK_IVAS_Periodo: IdPeriodoIVA
- FK_IVAS_Proveedor: IdProveedor
- FK_IVAS_Tipo: IdTipoOperacion
- PK_IVASoportado (clave primaria): IdIVASoportado

Relaciones:
- FK_IVAS_Asiento: (IdAsiento) → Asientos (IdAsiento); borrado NO ACTION, actualización NO ACTION
- FK_IVAS_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_IVAS_Periodo: (IdPeriodoIVA) → PeriodosIVA (IdPeriodoIVA); borrado NO ACTION, actualización NO ACTION
- FK_IVAS_Proveedor: (IdProveedor) → Proveedores (IdProveedor); borrado NO ACTION, actualización NO ACTION
- FK_IVAS_Tipo: (IdTipoOperacion) → TiposOperacion (IdTipoOperacion); borrado NO ACTION, actualización NO ACTION

## ModelosBalance

- Dueño funcional: `contabilidad/balances`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdModeloBalance | COUNTER | no |  |  |
| Codigo | TEXT(40) | no |  | Código estable y versionado del modelo normativo. |
| Nombre | TEXT(150) | no |  |  |
| TipoBalance | BYTE | no |  | 1: Pérdidas y ganancias; 2: Balance de situación. |
| VersionNormativa | TEXT(30) | no |  |  |
| FechaVigenciaDesde | DATETIME | no |  |  |
| FechaVigenciaHasta | DATETIME | sí |  |  |
| FuenteNormativa | TEXT(255) | no |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_ModelosBalance (clave primaria): IdModeloBalance
- UX_ModelosBalance_Codigo (único): Codigo

## NodosBalance

- Dueño funcional: `contabilidad/balances`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdNodoBalance | COUNTER | no |  |  |
| IdModeloBalance | LONG | no |  |  |
| IdNodoPadre | LONG | sí |  | Nodo superior; nulo únicamente para las raíces. |
| Codigo | TEXT(60) | no |  |  |
| Nombre | TEXT(255) | no |  |  |
| TipoNodo | BYTE | no |  |  |
| Orden | LONG | no |  | Orden oficial de presentación dentro del modelo. |
| Nivel | BYTE | no |  |  |
| Multiplicador | SHORT | no |  |  |
| MostrarSiEsCero | YESNO | no |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- FK_NodosBalance_Modelo: IdModeloBalance
- FK_NodosBalance_Padre: IdNodoPadre
- IX_NodosBalance_PadreOrden: IdModeloBalance, IdNodoPadre, Orden
- PK_NodosBalance (clave primaria): IdNodoBalance
- UX_NodosBalance_Codigo (único): IdModeloBalance, Codigo

Relaciones:
- FK_NodosBalance_Modelo: (IdModeloBalance) → ModelosBalance (IdModeloBalance); borrado NO ACTION, actualización NO ACTION
- FK_NodosBalance_Padre: (IdNodoPadre) → NodosBalance (IdNodoBalance); borrado NO ACTION, actualización NO ACTION

## PeriodosIVA

- Dueño funcional: `contabilidad/procesos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPeriodoIVA | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| Numero | BYTE | no |  |  |
| FechaInicio | DATETIME | no |  |  |
| FechaFin | DATETIME | no |  |  |
| Estado | TEXT(20) | no |  |  |

Índices:
- FK_PeriodosIVA_Ejercicio: IdSociedadEjercicio
- PK_PeriodosIVA (clave primaria): IdPeriodoIVA
- UX_PeriodosIVA_EjercicioNumero (único): IdSociedadEjercicio, Numero

Relaciones:
- FK_PeriodosIVA_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## PlanesContables

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPlanContable | COUNTER | no |  |  |
| Codigo | TEXT(30) | no |  |  |
| Nombre | TEXT(150) | no |  |  |
| VersionPlan | TEXT(30) | sí |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_PlanesContables (clave primaria): IdPlanContable
- UX_PlanesContables_Codigo (único): Codigo

## PresentacionesTributarias

- Dueño funcional: `contabilidad/modelos-tributarios`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPresentacionTributaria | COUNTER | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| TipoPresentacion | TEXT(20) | no |  | Modelo u obligación que puede condicionar modificaciones contables. |
| Periodicidad | BYTE | no |  |  |
| FechaDesde | DATETIME | no |  | Primer día contable protegido por la presentación. |
| FechaHasta | DATETIME | no |  | Último día contable protegido por la presentación. |
| EstadoPresentacion | BYTE | no |  |  |
| NumeroJustificante | TEXT(100) | sí |  |  |
| FechaHoraPresentacion | DATETIME | sí |  |  |
| Operador | TEXT(100) | sí |  |  |
| IdPresentacionRectificada | LONG | sí |  |  |
| Observaciones | LONGTEXT | sí |  |  |

Índices:
- FK_Presentaciones_Ejercicios: IdSociedadEjercicio
- FK_Presentaciones_Rectificada: IdPresentacionRectificada
- IX_Presentaciones_Periodo: IdSociedadEjercicio, FechaDesde, FechaHasta
- IX_Presentaciones_Tipo: TipoPresentacion, EstadoPresentacion
- PK_PresentacionesTributarias (clave primaria): IdPresentacionTributaria

Relaciones:
- FK_Presentaciones_Ejercicios: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_Presentaciones_Rectificada: (IdPresentacionRectificada) → PresentacionesTributarias (IdPresentacionTributaria); borrado NO ACTION, actualización NO ACTION

## ReglasCuentasBalance

- Dueño funcional: `contabilidad/balances`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdReglaCuentaBalance | COUNTER | no |  |  |
| IdNodoBalance | LONG | no |  |  |
| TipoCoincidencia | BYTE | no |  |  |
| CuentaDesde | TEXT(20) | sí |  |  |
| CuentaHasta | TEXT(20) | sí |  |  |
| PrefijoCuenta | TEXT(20) | sí |  | Prefijo que clasifica automáticamente nuevas subcuentas. |
| EsInclusion | YESNO | no |  |  |
| Multiplicador | SHORT | no |  | Signo aplicado al saldo Debe menos Haber. |
| Prioridad | SHORT | no |  | Gana la regla aplicable con mayor prioridad y prefijo más específico. |
| Descripcion | TEXT(255) | sí |  |  |

Índices:
- FK_ReglasBalance_Nodo: IdNodoBalance
- IX_ReglasBalance_Nodo: IdNodoBalance
- IX_ReglasBalance_Prefijo: PrefijoCuenta, Prioridad
- PK_ReglasCuentasBalance (clave primaria): IdReglaCuentaBalance

Relaciones:
- FK_ReglasBalance_Nodo: (IdNodoBalance) → NodosBalance (IdNodoBalance); borrado NO ACTION, actualización NO ACTION

## RelacionesAsientos

- Dueño funcional: `contabilidad/asientos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRelacionAsiento | COUNTER | no |  |  |
| IdAsientoOrigen | LONG | no |  |  |
| IdAsientoDestino | LONG | no |  |  |
| TipoRelacion | TEXT(30) | no |  |  |
| Motivo | LONGTEXT | sí |  |  |

Índices:
- FK_RelAsiento_Destino: IdAsientoDestino
- FK_RelAsiento_Origen: IdAsientoOrigen
- PK_RelacionesAsientos (clave primaria): IdRelacionAsiento

Relaciones:
- FK_RelAsiento_Destino: (IdAsientoDestino) → Asientos (IdAsiento); borrado NO ACTION, actualización NO ACTION
- FK_RelAsiento_Origen: (IdAsientoOrigen) → Asientos (IdAsiento); borrado NO ACTION, actualización NO ACTION

## RelacionesCalculoBalance

- Dueño funcional: `contabilidad/balances`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRelacionCalculoBalance | COUNTER | no |  |  |
| IdNodoResultado | LONG | no |  |  |
| IdNodoComponente | LONG | no |  |  |
| Multiplicador | SHORT | no |  |  |
| Orden | SHORT | no |  |  |

Índices:
- FK_RelacionesBalance_Componente: IdNodoComponente
- FK_RelacionesBalance_Resultado: IdNodoResultado
- PK_RelacionesCalculoBalance (clave primaria): IdRelacionCalculoBalance
- UX_RelacionesBalance (único): IdNodoResultado, IdNodoComponente

Relaciones:
- FK_RelacionesBalance_Componente: (IdNodoComponente) → NodosBalance (IdNodoBalance); borrado NO ACTION, actualización NO ACTION
- FK_RelacionesBalance_Resultado: (IdNodoResultado) → NodosBalance (IdNodoBalance); borrado NO ACTION, actualización NO ACTION

## RevisionesAsientos

- Dueño funcional: `contabilidad/asientos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRevisionAsiento | COUNTER | no |  |  |
| IdAsiento | LONG | no |  |  |
| NumeroRevision | LONG | no |  |  |
| TipoOperacion | BYTE | no |  |  |
| Motivo | LONGTEXT | no |  |  |
| Operador | TEXT(100) | no |  |  |
| FechaHora | DATETIME | no |  |  |
| IdLoteProceso | LONG | sí |  |  |
| AdvertenciasAceptadas | LONGTEXT | sí |  |  |
| ContenidoAnterior | LONGTEXT | sí |  | Instantánea JSON anterior; nula en la creación. |
| ContenidoPosterior | LONGTEXT | no |  | Instantánea JSON resultante de la operación. |

Índices:
- FK_RevisionesAsientos_Asientos: IdAsiento
- PK_RevisionesAsientos (clave primaria): IdRevisionAsiento
- UX_RevisionesAsientos_Numero (único): IdAsiento, NumeroRevision

Relaciones:
- FK_RevisionesAsientos_Asientos: (IdAsiento) → Asientos (IdAsiento); borrado NO ACTION, actualización NO ACTION

## TiposCuentaContable

- Dueño funcional: `contabilidad/cuenta-contable`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTipoCuentaContable | COUNTER | no |  |  |
| Codigo | TEXT(30) | no |  |  |
| Nombre | TEXT(100) | no |  |  |
| Prefijo | TEXT(20) | sí |  |  |
| LongitudPredeterminada | BYTE | sí |  |  |
| Naturaleza | BYTE | no |  |  |
| EsImputable | YESNO | no |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_TiposCuentaContable (clave primaria): IdTipoCuentaContable
- UX_TiposCuentaContable_Codigo (único): Codigo

