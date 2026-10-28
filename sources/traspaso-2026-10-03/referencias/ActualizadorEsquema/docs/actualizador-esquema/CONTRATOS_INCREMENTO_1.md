# Contratos para la futura base maestra: incremento 1

Estado: decisiones de integración para el esquema inicial nuevo. El contrato compartido legible por herramientas está en `database/contracts/base-compartida.v1.json`. No altera migraciones publicadas ni bases existentes. Fuentes: [inventario y propuesta](../../../ProConta.Facturacion/docs/actualizador-esquema/INVENTARIO_Y_PROPUESTA.md), revisión de los worktrees de cada módulo y consultas de coordinación del 28-09-2026. Las definiciones completas de columnas, defaults, descripciones e índices se incorporarán al manifiesto operativo en el incremento 2.

## Identidades compartidas

| Identidad | Contrato maestro | Adaptación necesaria |
|---|---|---|
| Sociedad permanente | `Sociedades.IdSociedad TEXT(3)`; todos los campos que referencian esta PK usan `TEXT(3)` | Activos declara `IdSociedad TEXT(10)` en `Activos`, `LotesAmortizacionActivo` e `ImportacionesActivos`. Normalizar su DDL y su importador, sin truncar códigos de origen. |
| Sociedad y ejercicio | `SociedadesEjercicios.IdSociedadEjercicio LONG`; `SociedadesEjercicios.IdSociedad TEXT(3)` referencia sociedad permanente | Los consumidores anuales usan `IdSociedadEjercicio`; no deducirlo del código permanente ni de un `IdSociedad LONG` heredado. |
| Cuenta contable | `CuentasContables.Codigo TEXT(10)` y toda referencia textual al código con longitud máxima 10 | Decisión explícita del usuario. Los `IdCuentaContable` e `IdIdentidadContable` siguen siendo `LONG`; no son códigos textuales. Rechazar con diagnóstico cualquier valor importado que exceda 10 caracteres; nunca truncarlo. |

Regla cerrada por el usuario: **todo campo `IdSociedad` de la base maestra representa la sociedad permanente y es `TEXT(3)`**. Cuando una columna heredada llamada `IdSociedad` contenga una clave numérica anual, el esquema nuevo la denominará `IdSociedadEjercicio LONG`; no se conservará el nombre ambiguo. Esta regla alcanza también las tres tablas de Activos que hoy declaran `TEXT(10)`.

`Sociedades.IdSociedad TEXT(3)` es la clave primaria física. El NIF es obligatorio, único e inmutable: un NIF distinto identifica otra sociedad, no un cambio del NIF de la existente. `SociedadesEjercicios.RazonSocial` conserva el nombre histórico de cada ejercicio. Al cambiar la razón social se actualiza el ejercicio en curso y los posteriores, sin reescribir los anteriores. `Sociedades.RazonSocial` representa la denominación vigente. La base observada llama `CIF` al dato fiscal y `Nombre` a la denominación en ambas tablas; el manifiesto nuevo deberá resolver estos nombres de forma explícita.

El [contrato de sociedades y ejercicios](SOCIEDADES_Y_EJERCICIOS.md) distribuye los 35 y 42 campos observados y propone los índices. El usuario confirmó que la sociedad `003`, que duplica el NIF de `002`, es de pruebas; se excluye de la carga empresarial de esta instalación sin modificar el origen. La maestra nueva empieza sin sociedades.

El código de cuenta se conserva como texto para mantener ceros iniciales y prefijos. El contrato de unicidad de `CuentasContables` sigue siendo `(IdSociedadEjercicio, Codigo)`. `Apuntes.CuentaContable` conserva la instantánea textual y los identificadores de cuenta e identidad permiten su vinculación estable. Las referencias numéricas y la instantánea textual deberán concordar con el asiento y su ejercicio; el validador del esquema y los importadores deben comprobarlo.

### Campos de código de cuenta que deben medir como máximo 10

| Área | Campos observados que hay que alinear |
|---|---|
| Cuentas | `CuentasPlanContable.Codigo`, `CuentasContables.Codigo`, `CodigosContablesReservados.Codigo`, `TiposCuentaContable.Prefijo` |
| Diario, clientes y facturas | `Apuntes.CuentaContable`, `Clientes.CuentaContable`, `Conceptos.CuentaContable`, `LineasFactura.CuentaContable`, `LineasFacturasProgramadas.CuentaContable` |
| Política contable | `ConfiguracionesContables.CuentaGastosRedondeo`, `CuentaIngresosRedondeo` |
| GestorContable | Los ocho campos `Cuenta*` de `ConfiguracionGestorContable`; cada elemento de `CuentasIngresosConDerecho` y `CuentasIngresosTotales` se valida a 10, aunque esas listas permanezcan `LONGTEXT` |
| Reporting y Activos | `ReglasCuentasBalance.CuentaDesde`, `CuentaHasta`, `PrefijoCuenta`; `PlantillasCuentasActivo.PrefijoCuenta` |
| Compras | `FacturasCompra.CuentaContableProveedor` ya es `TEXT(10)` en la base inspeccionada |
| Sociedades y ejercicios | Todos los campos `Cuenta*` que expresen cuentas contables; los dos `CuentaSuplido*` heredados están en `TEXT(255)` y requieren clasificación antes de conservarlos |

`PlanesContables.Codigo`, `TiposCuentaContable.Codigo`, `CentrosCoste.Codigo`, los códigos de proceso, `CuentasBancarias.Cuenta` y `PlantillasApuntesNomina.FormulaCuenta` son identificadores o expresiones distintos del código de cuenta final. No se reducen por coincidencia de nombre; la evaluación de `FormulaCuenta` sí debe producir un código de hasta 10 caracteres. Las migraciones antiguas quedan como evidencia histórica: el nuevo manifiesto creará las longitudes acordadas y los cambios en bases con datos requerirán migraciones nuevas con preflight de valores.

Comprobación sobre **otra copia temporal** de la captura de pruebas del inventario (SHA-256 `4A99674A9FC6DC4C9BD7D34ED4CB53DD0BE67A2DB25EFA9B868D29C1D7DF25A6`, origen capturado el 28-09-2026 a las 09:09 UTC): se consultaron exclusivamente `Max(Len(campo))` y el número de filas con longitud mayor que 10. Los 16 campos de código/prefijo de la tabla anterior dieron **cero valores de más de 10**. Los máximos fueron 10 en `CuentasContables.Codigo` y `CodigosContablesReservados.Codigo`, 7 en `Apuntes.CuentaContable` y 7 o menos en los demás campos poblados. En los campos heredados `Sociedades[Ejercicios].CuentaSuplido` y `CuentaSuplidoIVA` tampoco hubo valores de más de 10; deberán clasificarse antes de conservarlos en la maestra. No se consultaron ni publicaron códigos individuales. La copia temporal de esta comprobación se eliminó. Este resultado no sustituye el preflight de una copia actual cuando se prepare la transición de datos.

## Propiedad de los cuatro vínculos heredados

| Vínculo de la base instalada | Destino en base nueva | Responsable |
|---|---|---|
| `F_DEP` | `CentrosCoste` local, nodos superiores | Importador ContaSol. MigradorERP consume correspondencias, no el vínculo. |
| `F_CDP` | `CentrosCoste` local, nodos hijos | Importador ContaSol. El antiguo script ERP de Conceptos que usa `CODCDP`/`DEPCDP` debe leer la correspondencia nueva. |
| `FormasPago` | Catálogo local con `Codigo BYTE` como PK, `Descripcion TEXT(50)` obligatoria y `Activa YESNO` obligatoria; sin filas iniciales | MigradorERP importa las filas activas e inactivas. Facturación consume el catálogo local y señala las inactivas al mostrarlas. |
| `Inmuebles` | Tabla externa vinculada desde la base Access instalada; sus filas se trasladarán al modelo local de Activos mediante importación explícita | Importador de Activos coordinado con MigradorERP. El importador actual cubre solo una parte de los campos heredados. Los datos catastrales y de alquiler se trasladarán cuando exista un campo de destino adecuado, sin volcarlos indiscriminadamente en `Descripcion`. |

La maestra nueva no contendrá tablas vinculadas a archivos ERP. Esta decisión no elimina vínculos de la base instalada.

El `FormasPago` del ERP tiene 64 columnas en las dos copias inspeccionadas. El código consumidor actual solo consulta `Codigo`, `Descripcion` y `Activa`, y los campos de factura, programación y contrato que lo guardan son `BYTE`. `Sociedades` y `SociedadesEjercicios` conservan hoy `FormaPagoDefecto LONG`; en la base nueva se alineará a `BYTE` tras un preflight de rango y referencias. Los otros 61 campos describen comportamientos de cobro/exportación del ERP antiguo y no entran automáticamente en el catálogo nuevo. Se mantendrán las formas de pago inactivas para que el histórico siga siendo legible. En la copia de inventario se contaron 16 formas, todas con código y descripción, dos inactivas y ningún código duplicado. La captura de pruebas utiliza además el código `1` en facturas y como valor predeterminado de sociedades, pero ese código no figura ni en la copia de inventario ni en la segunda copia del ERP enlazado. El original ERP cambió de huella después de estas comprobaciones, así que se repetirá el contraste sobre una copia del corte. La transición debe resolver o informar la referencia antes de activar FKs; no sembrará una fila ficticia. El origen continúa disponible para una integración funcional posterior si se requiere alguna de esas capacidades.

Activos ya distingue tipos mediante `TiposActivo` (`INMUEBLE`, `VEHICULO`, `TECNICO`, `INVERSION`) y tiene el detalle específico `ActivosInversiones`. El DDL actual no incluye un detalle equivalente para inmuebles ni campos catastrales en `Activos`. `Contratos` cubre los contratos de alquiler, pero no sustituye una ficha catastral del inmueble. El [diseño del detalle `ActivosInmuebles`](ACTIVOS_INMUEBLES.md) mapea las 21 columnas de `Inmuebles`, separa datos operativos de instantáneas legacy y propone una tabla uno a uno para los inmuebles. La decisión del usuario es trasladar los datos históricos que tengan un campo adecuado; el esquema actual aún no puede contenerlos todos.

## Dependencias y datos iniciales por módulo

| Módulo | Contrato confirmado para incorporar al esquema inicial | Datos iniciales |
|---|---|---|
| CuentasContables (`7a63dab`) | Núcleo de cuentas, `PlanesContablesEjercicios`, `FusionesCuentasContables`, `RevisionesCuentasContables`, `AuditoriaCuentasContables`, `Apuntes.IdCuentaContable` e `IdIdentidadContable` | Solo catálogos técnicos de planes/tipos; no cuentas reales de sociedad. |
| Asientos (`8a015ba`) | `SociedadesEjercicios.EjercicioCerrado`, propiedad automática en `Asientos`, `Apuntes.GeneradoAutomaticamente`, `AuditoriaLotesAsientos` | Sin asientos ni lotes ficticios. |
| GestorContable (`231fbbd`) | `ConfiguracionGestorContable`, `TratamientosIvaCentrosCoste`; depende de Cuentas, CentrosCoste y Asientos automáticos | No insertar configuración anual universal: los valores por defecto son de aplicación y las cuentas operativas deben existir. |
| GestorCorreo (`9ff2fa5`) | `CuentasCorreo`, `CuentasCorreoSociedadesEjercicios`, `DiagnosticosCuentasCorreo`; conservar `ConfiguracionMailDispatcher` mientras exista su FK de procesamientos | Tablas vacías. La conversión de configuraciones instaladas es independiente; nunca sembrar direcciones, cuentas ni secretos. |
| Seguridad (`270450d`) | `RestriccionesProcesosContablesGrupo` por grupo y código de proceso; requiere grupos y códigos publicados por GestorContable | Sin operadores o permisos de instalación ficticios. |
| Nóminas (`b8c9a1a` más cambios locales) | Diez tablas declaradas por `MigracionNominas`; requiere sociedades, ejercicios, centros, identidades y asientos | No aplicar a todas las instalaciones la excepción de configuración de sociedad `002`. `FormulaCuenta` es una expresión, cuyo resultado cumple el límite de 10. |
| Norma43 (`1e6d995` más cambios locales) | Motor bancario y tres columnas anulables de reversión en `LotesImportacionBancaria` | Sin cuentas bancarias ni lotes. El cambio de reversión local usa `260907001`, colisiona y recibirá otra identidad en el catálogo central. |
| FacturasCompra (contrato de `a458966`) | `ConfiguracionReconocimientoFacturasCompra` y `LOGImportacion`, `RequiereRevisionImportacion`, `ConfianzaImportacion` en `FacturasCompra`; separar sociedad permanente de ejercicio según el apartado siguiente | Configuración vacía, reconocimiento desactivado hasta configuración. |

### Resolución de Compras: sociedad frente a ejercicio

El `FacturasCompra.IdSociedad LONG` heredado identifica **SociedadesEjercicios.IdSociedadEjercicio**, no la sociedad permanente. El VBA anterior lo compara con la sociedad anual seleccionada y la migración de normalización trasladó el antiguo `Sociedades.IdSociedad` numérico a `SociedadesEjercicios.IdSociedadEjercicio`, pero omitió `FacturasCompra` en la lista de campos que se renombraban. Para la base nueva se denominará `FacturasCompra.IdSociedadEjercicio LONG` con FK al ejercicio. La configuración de reconocimiento será por sociedad permanente: `ConfiguracionReconocimientoFacturasCompra.IdSociedad TEXT(3)`, único y con FK a `Sociedades`.

El procesador de compras tendrá que recibir ambos identificadores del contexto activo. La lectura `SELECT TOP 1` de la primera configuración activa no es válida para más de una sociedad y se sustituirá por una consulta filtrada por `IdSociedad`. La transición de datos preservará el `IdSociedad LONG` antiguo hasta comprobar correspondencias y huérfanos; no renombrará ni rellenará columnas en la base original. En otra copia temporal de la base de pruebas se contaron 2.036 facturas de compra: 2.021 enlazan con `SociedadesEjercicios.IdSociedadEjercicio` y **15 son huérfanas**. Tres llevan clave anual 0, pero indican sociedad `002` y ejercicio `2021`: existe un ejercicio candidato, que se deberá contrastar antes de asignar. Las otras doce llevan clave anual 7 y sociedad `999`, inexistente en las sociedades de pruebas; no se les asignará ejercicio por inferencia. El cambio de nombre no podrá aplicarse a esos registros sin una resolución explícita; el importador deberá detenerse con un informe de incidencias. La copia temporal se eliminó.

El catálogo central nuevo usa una versión inicial independiente y no marca ejecutadas migraciones de ramas. Los IDs/órdenes `260821001`, `260821002`, `260822003` y `260907001`–`003` colisionan entre ramas. Los recursos que entren en la base inicial serán nuevos e inmutables; la transición de bases existentes tendrá una correspondencia explícita por estado físico y procedencia, sin adoptar versiones solo por nombre.

## Verificaciones aún necesarias para cerrar este incremento

1. Integrar en el manifiesto físico el contrato de `Sociedades` y `SociedadesEjercicios`, incluidos los renombrados de `Nombre`/`CIF`, las descripciones DAO y los índices únicos; aplicar el perfil de exclusión de la sociedad de pruebas `003` al importar esta instalación.
2. Repetir el preflight de longitud sobre una copia actual antes de cualquier transición de datos y revisar las listas `LONGTEXT` token a token; la captura histórica no autoriza estrechar campos de una base existente sin diagnóstico.
3. Resolver las 15 referencias anuales huérfanas de `FacturasCompra.IdSociedad` de la captura de pruebas y repetir el diagnóstico sobre una copia vigente antes de migrar datos; adaptar el servicio y la configuración al par de identidades resuelto.
4. Integrar el catálogo local `FormasPago`, el diseño `ActivosInmuebles` y sus correspondencias con Contratos, para no depender de vínculos.
5. Verificar defaults, validaciones, índices, relaciones y descripciones DAO de cada módulo antes de generar el DDL operativo. Las migraciones compiladas actuales suelen comprobar existencia, no equivalencia física completa.

### Cobertura DAO de la referencia instalada

La utilidad `tools/ActualizadorEsquema.Inspeccion/InspeccionarPropiedadesDao.ps1` leyó exclusivamente una copia temporal de la captura del inventario. Extrajo propiedades de **95 tablas locales y 1.148 campos** sin abrir vínculos ni consultar filas. En esa referencia, 437 campos tienen descripción DAO, 101 valor predeterminado, 44 regla de validación y 23 permiten cadena de longitud cero. `Sociedades` tiene descripción en sus 35 campos; `SociedadesEjercicios` no tiene ninguna en sus 42. El JSON completo queda en `work/dao-propiedades.json`, excluido de Git. Estas cifras son evidencia del estado instalado, no una aprobación automática de todas sus propiedades. El manifiesto operativo deberá declarar y verificar cada propiedad requerida tras la revisión por módulo.
