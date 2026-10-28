# MigradorERP: inventario, cobertura y plan de traslado

Primera entrega: 28/09/2026. Rama de trabajo: `feature/migracion-proconta`, base `792a9ae`. Estado: análisis y diseño; no hay todavía un ejecutor integral ni una migración final ensayada.

## Objetivo y límites

Delimitación confirmada: las tablas `F_???` (prefijo `F_` seguido de exactamente tres caracteres), excepto `F_NOM`, corresponden a Importador ContaSol. El ERP tiene una única base; ContaSol tiene una por ejercicio. `F_NOM_ALIAS`, `F_APUMANUAL`, `F_Anulacion_Asientos` y `F_CLIAGRUPADOS` no quedan excluidas por este patrón. MigradorERP no carga las tablas de ContaSol: consume sus correspondencias para enlazar los datos ERP. Esta delimitación prevalece sobre las propuestas históricas de carga contable recogidas abajo, que quedan pendientes de reformulación.

MigradorERP trasladará la única instalación del ERP actual, representada por `ProcontaEstructura.accdb` (originariamente `deporwin.mdb`), al nuevo ProConta. Se mantendrá como herramienta de transición hasta completar el corte final y verificar sus resultados. El uso puntual no impide repetir ensayos sobre destinos nuevos.

Importador ContaSol sigue siendo una capacidad independiente de implantación para nuevos usuarios. ActualizadorEsquema prepara la estructura del destino y sus catálogos técnicos; MigradorERP carga los datos empresariales compatibles con esa estructura. Ninguno de estos procesos debe confundirse con la importación operativa posterior de ficheros de nóminas, Norma 43 o facturas. El histórico de esos módulos ya existente en el ERP sí requiere una decisión de traslado.

El usuario confirma que no hay producción y que existe una BD de pruebas. Es viable definir un esquema inicial limpio sin conservar todas las iteraciones de desarrollo como camino de actualización. Ello no autoriza a sustituir la base de pruebas ni descartar su configuración útil. Esta entrega no modifica código, esquemas ni datos del producto.

## Evidencias y límites de la inspección

- Worktree propio: `C:/Docs/VeriFactu/ProConta.Migracion`; rama verificada, sin cambios al comenzar. La carpeta figura como raíz de trabajo con escritura.
- Se han revisado los cuatro scripts de importación de `database/seeds/`, los scripts de preparación/ensayo, el importador legacy de Activos, el importador ContaSol y contratos/esquemas relevantes de las ramas actuales.
- Se creó una copia nueva de `C:/Docs/VeriFactu/ProcontaEstructura.accdb` en `work/migrador-erp/20260928-inventario/estructura.accdb`. El origen no tenía archivo de bloqueo. Los SHA-256 de copia y origen coincidieron antes de la lectura y volvieron a coincidir después; no quedó bloqueo en el origen ni en la copia.
- La consulta ACE/OLE DB se limitó a metadatos, con `Mode=Read`: nombres/tipos de objetos, columnas y claves primarias declaradas. No se consultaron registros empresariales ni se ejecutaron consultas guardadas o macros.
- Se enumeraron 135 objetos de tipo `TABLE` y 114 de tipo `VIEW`, y se guardaron los metadatos en JSON dentro de `work/`, excluido de Git. El número de vistas expuesto por OLE DB no representa todas las consultas de acción, formularios o módulos de Access.
- El proveedor no respondió dentro del entorno restringido y se canceló esa ejecución. Fuera de él devolvió y guardó los metadatos, pero ambos procesos terminaron con el código anómalo `21508178`. Los JSON se volvieron a leer y validar; esta observación no es una prueba satisfactoria de un extractor estable. Debe resolverse el cierre del proveedor antes de automatizar el perfilado.
- No se ha abierto `ProcontaVF.accdb`: se observó su archivo de bloqueo. No se ha realizado el perfilado de filas, la conciliación de importes ni la comprobación de disponibilidad de documentos físicos.
- Los análisis históricos de `docs/MIGRACION_PROCONTA.md` y `docs/PROPUESTA_*` son antecedentes, no una especificación vigente automática. Contienen decisiones sobre ejercicio en curso, tablas descartadas y sociedades que hay que cotejar con el alcance actual y el esquema canónico.

El inventario exhaustivo de las 135 tablas se encuentra en [MIGRADOR_ERP_TABLAS.md](C:/Docs/VeriFactu/ProConta.Migracion/docs/MIGRADOR_ERP_TABLAS.md). La clasificación es provisional; no constituye autorización de descarte.

## Orígenes y autoridad de los datos

| Origen localizado | Uso propuesto | Comprobación pendiente |
|---|---|---|
| `C:/Docs/VeriFactu/ProcontaEstructura.accdb` | Origen principal inspeccionado mediante copia; incluye tablas ERP y `F_???`. | Confirmar que será la extracción completa y vigente en el corte; resolver procedencia y ejercicio de tablas materializadas. |
| `C:/Docs/VeriFactu/CopiaProcontaEstructura.accdb` y copias de `work/databases/` | Referencias históricas de ensayo. | No fusionarlas ni tomarlas como datos nuevos por el nombre; conservar fecha/huella y función de cada copia. |
| `ProcontaVF.accdb` de desarrollo | Posible origen complementario de configuración válida que solo existe en el sistema nuevo. | Seleccionar expresamente sociedades, parámetros, plantillas y maestros útiles, separando datos sintéticos; no copiarla entera como base de producción. |
| Archivos referenciados por documentos, facturas, remesas y extractos | Contenido documental que no queda cubierto por trasladar las filas. | Resolver raíces autorizadas, existencia, integridad y destino; no incluir rutas personales ni documentos en Git. |
| Bases ContaSol por ejercicio | Ruta independiente de Importador ContaSol. | La carga corresponde exclusivamente a Importador ContaSol; acordar correspondencias para enlazar las tablas `F_???` del ERP. Evitar duplicidad por ambos caminos. |
| Libro de inversiones utilizado por Activos | Complemento opcional, separado del origen ERP. | Confirmar si forma parte del corte de esta instalación y qué posiciones tienen una fuente única. |

Un registro no debe ganar prioridad porque su archivo tenga una fecha más reciente. La autoridad se fija por familia de datos, sociedad y ejercicio. La BD de pruebas puede mezclar datos reales, configuración nueva y registros alterados para ensayos.

## Importadores existentes y grado de reutilización

| Pieza | Cobertura observada | Condiciones que impiden usarla sin adaptación |
|---|---|---|
| `database/seeds/20260810_importar_clientes_personas.ps1` | `Personas` activas → `Clientes`; conserva ID y varios datos identificativos. | Sustituye el correo por uno de pruebas; exige un recuento fijo; no aporta mapa de identidad, deduplicación ni cobertura de inactivos referenciados, domicilios o vínculos. |
| `database/seeds/20260810_importar_conceptos.ps1` | `ConceptosCaja` + `Agrupaciones` + `F_CDP` → `Conceptos`. | Solo activos; sociedad/ejercicio predeterminado, recuento fijo y referencias contables/analíticas antiguas. Rechaza colisiones en vez de reanudar. |
| `database/seeds/20260810_importar_facturas_programadas.ps1` | Cabeceras y líneas con cliente encontrado → `FacturasProgramadas` y `LineasFacturasProgramadas`. | Selección implícita por `INNER JOIN`, recuento fijo, fechas centinela y periodicidades distintas de anual/mensual convertidas en mensual. IDs de línea calculados como `Id*100+NumLinea`. Mantiene `Activa` del origen. |
| `database/seeds/20260810_importar_facturas_historicas.ps1` | `FacturasVenta`/`LineasFacturaVenta` → `Facturas`/`LineasFactura`; crea clientes y conceptos auxiliares. | Número convertido desde ID aunque el origen tiene `Numero`, `CodigoSerie` y `NumeroFacturaManual`; emisor, serie, ejercicio, país y clasificación fijados; fechas centinela, cliente auxiliar, recuentos fijos y correo de pruebas. IDs de línea `Id*10000+NumLinea`. No incorpora la cadena documental/rectificativa completa. |
| `ImportadorActivosAccess.ImportarModeloAntiguo` en Activos | `Inmuebles` y `CuentasInmuebles` → activos y asociaciones contables; copia de origen, transacción, referencia `INMUEBLES:<id>` y registro de importación. | Excluye tipo 6 y SEPA; solo lee una selección de campos. No traslada, por ejemplo, todos los datos catastrales y de alquiler presentes. Pasa referencias de factura/analítica sin un mapa global. Resuelve las cuentas en el último ejercicio disponible. Omite por referencia existente, sin demostrar equivalencia si cambió el origen. |
| `ImportadorActivosAccess.ImportarCarteraExcel` | Posiciones de inversión, instrumento y operación inicial. | Rango concreto de una hoja. Es carga adicional, no cobertura del legado completo de Activos. |
| `ImportadorContaSolAccess` | Empresa/ejercicio, centros, cuentas, terceros, asientos, bancos e IVA en su ámbito. | Su propietario es Importador ContaSol. Consumir sus correspondencias; MigradorERP no ejecuta sus fases sobre `F_???` del ERP sin resolver procedencia y compatibilidad, ni registrar una carga ERP como ContaSol. |
| `tools/PrepararBasesWorktree.ps1` | Copia bases de referencia al worktree y escribe configuración local. | Prepara el entorno; no migra ni reconstruye el destino. El modo de sobrescritura no es una reconciliación. |
| `tools/ProConta.Facturacion.CompararNorma43` y documentos de resultados históricos | Caracterización del parser con ficheros y datos legacy. | No demuestra traslado de reglas, imputaciones o vínculos contables. Sus informes históricos contienen valores de negocio; no copiarlos a la nueva documentación versionada. |

Los scripts parciales usan DAO COM, aunque la nueva implementación debe utilizar VB.NET/.NET 10 x64 y ACE/OLE DB. Son especificación de transformaciones realizadas, no una dependencia técnica que deba perpetuarse. Sus transacciones por bloque no convierten el conjunto en una migración integral recuperable.

### Piezas de ensayo que no deben formar parte de la carga empresarial

`20260810_preparar_bateria_20_5.ps1` cambia clientes, series, fechas e importes de programaciones para pruebas y limpia copias de correo. `20260810_configuracion_verifactu_dinamica.sql` configura un entorno de pruebas concreto. Deben mantenerse identificados como utilidades de ensayo; MigradorERP no los ejecutará. Tampoco se trasladarán automáticamente resultados de reconocimiento, cachés OCR o estados de envíos de prueba.

### Transformaciones que necesitan una regla explícita

1. **Inactivos referenciados:** conservar los necesarios para reconstruir el histórico, aunque no se ofrezcan para nuevas operaciones. Un filtro `Activo=True` no define el alcance de la migración.
2. **Número y serie de factura:** separar la identidad técnica de la identificación documental del origen. No sustituirla por el ID sin demostrar equivalencia.
3. **Importes históricos:** preservar bases, cuotas, retenciones, suplidos y totales entregados; comparar con el cálculo del modelo nuevo sin corregir silenciosamente diferencias históricas.
4. **Datos ausentes:** generar incidencias o representación histórica explícita, no fechas inventadas, emisores fijos o clasificaciones fiscales deducidas únicamente de un tipo cero.
5. **Programaciones:** verificar periodicidad, última generación y próxima fecha; conservar la intención de activación, pero impedir su ejecución automática durante carga y validación.
6. **Identidades:** sustituir fórmulas aritméticas de IDs por mapas o demostrar que los IDs conservados son únicos y compatibles con sus rangos. Validar claves compuestas antes de leer dependientes.
7. **Metadatos antiguos:** conservar datos de negocio no representados mediante archivo controlado o ampliación acordada; una columna que no lee un importador no queda automáticamente descartada.

## Mapa funcional origen → destino

Las claves de esta tabla son candidatas semánticas. La inspección de metadatos no confirma su unicidad ni la integridad referencial de todas las tablas. En varias tablas centrales el proveedor no devolvió PK declarada; se exige perfilado antes de aprobar el mapa.

| Bloque y claves de origen | Destino y propietario | Transformación / laguna |
|---|---|---|
| `Sociedades.IdSociedad`, `Codigo`, `Ejercicio`; referencias desde `Sistema` | `Sociedades` + `SociedadesEjercicios`, configuración | Separar entidad permanente y ejercicio; resolver referencias del ERP y de configuración nueva. No aceptar la antigua propuesta de ignorar `Sociedades` sin una fuente alternativa verificada. |
| `F_DEP`/`F_CDP`, `Departamentos`, departamentos usados por ERP | `CentrosCoste` y estructura analítica, Cuentas/Configuración | Mapear sociedad y jerarquía; validar diferencias entre código, identificador y denominación. |
| `F_MAE.CODMAE`, `CuentasContables`, historial de cambios | Identidades permanentes + cuentas por ejercicio, Cuentas Contables | Conservar códigos como instantáneas donde proceda; resolver identidad/cuenta anual y recodificaciones. No unificar por código entre sociedades. |
| `Personas.IdPersona`, `F_CLI.CODCLI`, `F_PRO.CODPRO`, `DatosContacto`, domicilios | Clientes/proveedores y relaciones contables | Deduplicación con evidencias; NIF normalizado ayuda, pero no demuestra por sí solo equivalencia en todos los casos. Documentar campos y roles no necesarios para ProConta. |
| `ConceptosCaja.Id`, `Agrupaciones.IdAgrupacion` | Conceptos y configuración contable, Facturación | Reutilizar lógica seleccionada del script, incluyendo inactivos referenciados; resolver IVA, cuenta y analítica contra destino canónico. |
| `FacturasProgramadas.Id` + `LineasFacturaProgramada.(Id,NumLinea)` | Programaciones/líneas, Facturación | Mapear sociedades, clientes, conceptos y series. Validar periodicidades y fechas; no generar facturas al importar. |
| `FacturasVenta.Id` + `LineasFacturaVenta.(Id,NumLinea)` | Histórico de facturas/líneas, Facturación | Conservar identificación documental, importes, rectificación y enlaces a programación. Hace falta contrato de importación histórica que no ejecute emisión, VeriFactu, correo ni regeneración sustitutiva de originales. |
| `FacturasCompra.IdRegistro`, referencias IVA/asiento/documento | Facturas de compra y desgloses, Compras/Contabilidad | Traslado del dato ya registrado, sin volver a reconocer PDF ni llamar a AEAT/OCR. Mapear proveedores, importación/transporte/suplido, analítica y rectificaciones. No se ha localizado un migrador integral de este bloque. |
| `F_APU.(DIAAPU,ASIAPU,ORDAPU)` con sociedad/ejercicio; `F_OAS`, `F_APUMANUAL` | Asientos/Apuntes/revisiones, Asientos | Clave provisional completa hasta demostrar qué componentes pueden omitirse. Agrupar y conservar Debe/Haber; reconciliar fechas, estado y cabecera. No repetir contabilización desde facturas o nóminas cuyo asiento ya se ha trasladado. |
| `F_IVS.CODIVS`, `F_IVR.CODIVR` con ámbito | Libros/desgloses IVA, Contabilidad/Modelos | Convertir columnas repetidas a desgloses sin recalcular el histórico; resolver relaciones a asiento y documento. Diferenciar propuesta antigua `DocumentosFiscales` de tablas realmente aprobadas. |
| `F_NOM`, alias/campos y `PlantillasApuntes` | Empleados, nóminas, importaciones y vínculos, Nóminas | Determinar clave estable de nómina y origen de empleados; preservar el asiento existente y no crear uno nuevo por defecto. No confundir con importación futura de Excel. |
| `N43LotesImportacion.IdLote`, `N43Cabeceras.IdRegistro`, `N43Lineas.(IdRegistro,NumLinea)` | Lotes/extractos/líneas, reglas y vínculos, Norma 43 | Reimportar el fichero no recupera por sí mismo conciliación, clasificación, rectificaciones ni referencias del ERP. Definir mapa incluso si faltan originales. |
| `Pagos.Codigo`, `Recibos.Codigo`, `RemesasRecibos.Codigo` y datos bancarios | Tesorería/cobros/remesas; propietario por cerrar | Conservar estados, devoluciones, mandatos y relaciones. No se ha acreditado un destino completo y un ejecutor actual para este bloque. |
| `Inmuebles.Id`, `CuentasInmuebles.(IdInmueble,IdPlantilla)` | Activos/CuentasActivo | Reutilización parcial del importador; aplicar mapas globales de factura, sociedad y analítica. Los descartes previos de tipo 6/SEPA se contrastarán con la cobertura general. |
| `Documentos.Id`, `DocumentosFacturas`, `DocumentosPersonas`; referencias de archivo en otras tablas | Almacenamiento documental y enlaces, contrato por cerrar | Resolver si `Fichero` es contenido o referencia según tipo y uso; copiar originales con verificación, conservar vínculos y registrar ausencias. No inferir equivalencia por nombre de archivo. |
| `Operadores.IdOperador`, permisos y configuración legacy | Operadores/grupos de Seguridad y configuración por módulo | Mapear identidades/auditoría; no trasladar contraseñas legacy directamente. La provisión de credenciales corresponde a Seguridad/Gestor Correo. |
| Esquemas de balances, plantillas, categorías IVA y reglas | Reporting, Modelos, Nóminas y configuración | Separar catálogo del producto de configuración de la instalación; traducir solo formatos admitidos. No ejecutar fórmulas o SQL legacy durante la carga sin adaptación controlada. |
| Tablas auxiliares, copias y resultados calculados | Archivo/reconstrucción/decisión | No cargarlas como hechos nuevos. Comparar para demostrar si contienen información exclusiva antes de excluirlas. |

La cobertura real acreditada hoy es parcial: facturación mediante scripts de ensayo e importación legacy de Activos. El resto tiene modelos, importadores de otros orígenes o propuestas, pero no una cadena ERP completa verificada.

## Dependencias del destino actual

| Referencia inspeccionada | Estado observado | Requisito para MigradorERP |
|---|---|---|
| Facturación `773d3e4` | Base principal; cambios documentales de auditoría pendientes. | Contrato explícito de histórico y de programaciones; sociedad/ejercicio, series y numeración inicial. |
| Cuentas `7a63dab`; GestorContable `231fbbd` | Limpios. GestorContable contiene evolución de referencias contables. | Fijar identidad permanente, cuenta anual e IDs de apunte. No adoptar el antiguo maestro único por sociedad como esquema actual. |
| Asientos `8a015ba` | Limpio. API de lotes y cierre. | Definir entrada histórica compatible con estado, fechas, cierres y auditoría; el API operativo no implica autorización para reinterpretar asientos importados. |
| Nóminas `b8c9a1a` + cambios locales | Ciclo contable nuevo aún sin commit. | Necesita `Sociedades`, `SociedadesEjercicios`, `CentrosCoste`, `IdentidadesContables`, `Asientos` y mapas de empleados/nóminas. Fijar un commit antes de ejecutar una carga reproducible. |
| Norma43 `1e6d995` + cambios locales | Reversión/estado y UI posteriores al HEAD. | Fijar contrato de lote, líneas e imputaciones, separado de parseo de ficheros. |
| Activos `e952318` | Limpio; importador legacy seleccionado. | Cuentas/identidades preparadas, facturas de compra y analítica mapeadas; completar campos no cubiertos. |
| Seguridad `270450d`; Gestor Correo `9ff2fa5` | Limpios; correo unificado y credenciales opacas. | Identidades sin transportar secretos; cuentas importadas sin activar comunicaciones. |
| Configuración global `a3d7bb5` | Limpio. `IUbicacionDatosAplicacion`. | Recibir rutas explícitas y separar origen, destino y carpeta de ensayo. Modo desatendido sin UI. |
| FacturasCompra `adf0d88` | Limpio en su rama; documentación de reconocimiento parcial. | Contrato de carga histórica y relaciones contables; no utilizar reconocimiento como migrador. |
| ContaSol `792a9ae` + cambios locales | Importador con esquema de asiento distinto del motor actual. | Reutilizar conocimiento de mapeo con contrato vigente, manteniendo propietario e historial independientes. |

`MigracionBaseActual.Aplicar` de Facturación solo exige que existan tablas/campos: no construye la base inicial. Es la dependencia principal con ActualizadorEsquema. Las divergencias de IDs/órdenes entre ramas se resolverán al definir el esquema canónico; MigradorERP no parcheará historiales para simular compatibilidad.

## Diseño de la ejecución

### Estrategia propuesta para los primeros ensayos

Usar un destino nuevo por ejecución y conservar intacto el anterior hasta aceptar la reconciliación. Es más verificable que actualizar incrementalmente una BD de pruebas heterogénea. La reanudación dentro de una ejecución se permite solo para fases confirmadas contra el mismo origen y plan; si cambia la huella de origen o el contrato, iniciar otro ensayo.

El manifiesto privado del ensayo incluirá versión/commit del ejecutor y de los adaptadores, versión de esquema, identidad de cada origen, SHA-256, alcance de sociedades/ejercicios, decisiones de mapeo, fases, incidencias y resultados. No se versionarán rutas sensibles, registros, importes de empresas ni documentos; Git conserva código, reglas y pruebas sintéticas.

Se necesita un mapa durable de identidad con al menos origen, tabla/entidad, sociedad/ejercicio cuando corresponda, clave original y clave de destino. La huella identifica una extracción, no sustituye la identidad empresarial ni demuestra que otro fichero no contenga los mismos hechos. Los nombres de tablas de control se acordarán con ActualizadorEsquema; esta entrega no crea un nuevo esquema paralelo.

### Orden de carga

1. **Preparación:** origen congelado, copia verificada, destino inequívocamente distinto y nuevo, manifiesto, alcance y capacidad de disco. Rechazar originales abiertos/cambiantes y destinos inesperados.
2. **Estructura:** ActualizadorEsquema crea el esquema canónico y catálogos técnicos. Verificar su contrato antes de insertar datos de empresa.
3. **Contexto y maestros:** sociedades/ejercicios, analítica, identidades/cuentas, terceros, empleados y catálogos empresariales. Resolver colisiones y mapas.
4. **Documentos y configuración funcional:** conceptos, series, programaciones y documentos de compra/venta con sus importes originales. Preparar los archivos en una zona de ensayo. Ninguna programación o comunicación se ejecuta durante la carga.
5. **Enlaces contables e IVA:** consumir las correspondencias de Importador ContaSol y resolver las referencias ERP. La carga de asientos y libros corresponde a ese hilo. Los módulos productores no generan de nuevo asientos ya importados.
6. **Relaciones de negocio:** pagos/remesas/mandatos, vínculos bancarios, nóminas-asiento, activos/cuentas/documentos y rectificaciones. Resolver ciclos en una fase de enlaces después de crear las identidades.
7. **Derivados:** reconstruir saldos y cachés admitidas, contrastándolos con hechos y controles de origen. No recalcular documentos históricos para sobrescribirlos.
8. **Validación y cierre:** controles estructurales, funcionales y documentales, revisión de incidencias y aceptación del ensayo. La activación de programaciones/correo y el corte definitivo son pasos explícitos posteriores.

### Transacciones, repetición y recuperación

- Cada unidad coherente —por ejemplo factura con líneas o asiento con apuntes y mapa— se confirma en una transacción corta. Los mapas, vínculos y marcas de fase no pueden indicar éxito antes de persistir la unidad.
- Lectura de archivos y comunicaciones no se realizan dentro de una transacción Access. La copia documental requiere estado de preparación y verificación: Access y el sistema de archivos no forman una transacción única.
- La repetición sobre el mismo ensayo consulta mapas y comprueba equivalencia del contenido; no basta con omitir una referencia existente. Una diferencia produce incidencia, no sobrescritura silenciosa.
- Una fase fallida no habilita las dependientes. La reanudación valida huellas y contratos; no continúa sobre datos de origen cambiados.
- El primer mecanismo de recuperación será descartar únicamente el destino de ensayo identificado y reconstruir otro, sin borrar bases del usuario. La recuperación definitiva se documentará y probará antes del corte.

## Ensayo y reconciliación exigidos

| Control | Criterio de aceptación |
|---|---|
| Cobertura | Cada tabla/archivo del alcance tiene tratamiento: cargado, reconstruido, archivado, excluido con motivo aceptado o incidencia pendiente. No hay omisiones implícitas por joins. |
| Identidad | Claves de origen únicas dentro de su ámbito; cada registro elegible tiene una correspondencia y toda referencia tiene destino o una incidencia explícita. |
| Recuentos | Leídos = elegibles + excluidos/incidentados según categorías disjuntas. Elegibles y resultados se reconcilian por entidad y sociedad/ejercicio. No se usan números fijos históricos como contrato. |
| Facturas | Número/serie, fechas, datos congelados, líneas, bases, cuotas, retenciones y totales conservados. Toda diferencia de cálculo se explica; ninguna emisión o comunicación externa. |
| Programaciones | Periodicidad y próxima fecha coherentes con última generación; no se duplica un periodo ya facturado y no se activa un lote por cargarlo. |
| Asientos | Debe/Haber, fechas, cuentas y estado conciliados por asiento y ejercicio. Los descuadres se conservan e identifican conforme a una regla aprobada, sin apuntes artificiales de ajuste. |
| IVA | Bases/cuotas/retenciones reconciliadas por libro, periodo y tipo con trazabilidad a documento/asiento; no duplicar el mismo hecho desde varias fuentes. |
| Nóminas y banca | Relaciones preservadas; no volver a contabilizar hechos migrados. Cuadres y estados documentados aun cuando falten ficheros originales. |
| Activos | Coste, amortización histórica y cuentas/vínculos conciliados; no perder datos catastrales/de alquiler por la cobertura parcial del importador actual. |
| Documentos | Contenido original verificado por huella, ruta de destino válida y enlaces completos; ausencias clasificadas, nunca sustituidas por documentos regenerados sin decisión. |
| Repetición | Segunda ejecución sobre destino nuevo produce el mismo resultado funcional. Reanudar una fase confirmada no duplica entidades ni enlaces. |
| Fallos | Interrupción entre cabecera/líneas/mapa, documento/archivo y fases; la reanudación no deja éxito falso ni referencias rotas. |
| Aislamiento | Ninguna escritura en el origen; hashes verificados; no correos, envíos fiscales ni tareas automáticas; logs públicos sin datos empresariales ni credenciales. |

Los fixtures automatizados deben ser sintéticos: claves repetidas entre ejercicios, inactivos referenciados, datos faltantes, periodicidad distinta de mensual/anual, numeración distinta del ID, rectificaciones, factura sin PDF, doble fuente del mismo asiento, error transaccional y reinicio. Los informes detallados con datos reales permanecen en el área privada de ensayo.

## Decisiones pendientes y siguiente incremento

Antes de la implementación integral deben cerrarse estas decisiones:

1. **Alcance temporal y fuentes:** el análisis antiguo proponía solo el ejercicio en curso para parte de la contabilidad y ContaSol para anteriores. La propiedad de las tablas ya está fijada por la regla exacta indicada al inicio; queda concretar el alcance temporal ERP y los enlaces por sociedad/ejercicio.
2. **Configuración de desarrollo válida:** identificar qué datos de la BD de pruebas deben sobrevivir al esquema inicial limpio y cuáles son sintéticos.
3. **Histórico y operativa futura:** representación de facturas antiguas, numeración de continuación, rectificaciones, pendientes de cobro y programaciones activables tras el corte.
4. **Ámbitos sin destino cerrado:** tesorería/remesas, documentos y datos legacy de actividad deportiva/acceso, reservas u otros usos que no se trasladarán automáticamente al producto contable.
5. **Esquema de destino:** un manifiesto único de ActualizadorEsquema y commits consumibles de los módulos. Ninguna de las ramas inspeccionadas es por sí sola ese contrato completo.

Se puede avanzar ya en un primer extractor de perfilado sin mutaciones: resolver el cierre de ACE, validar claves candidatas y relaciones, producir recuentos y diagnósticos locales saneados, y convertir este inventario provisional en cobertura aceptada. Después, implementar una primera carga vertical de maestros + una factura histórica sobre una base nueva creada por ActualizadorEsquema, con datos sintéticos y sin servicios externos.

No se ha compilado la solución ni ejecutado pruebas funcionales en esta entrega porque solo se añade documentación. La consulta de metadatos y la verificación de huellas son evidencias de inspección; no se presentan como validación de un migrador terminado.
