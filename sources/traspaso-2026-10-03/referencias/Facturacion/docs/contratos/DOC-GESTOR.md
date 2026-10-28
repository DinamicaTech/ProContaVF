# DOC-GESTOR — contrato funcional y técnico inicial

Fecha: 01/10/2026. Versión de contrato: 0.3, con decisiones de rutas y exclusión de migración documental. Propietario funcional: DOC-GESTOR.

## 1. Estado, autoridad y localización

**Publicado para contraste de dependencias. No hay implementación DOC-GESTOR, API compilada, migración aplicada ni integración acreditada por esta entrega.** La tabla base se llama `Documentos` por decisión explícita del usuario y existe en `C:/Docs/VeriFactu/ProcontaEstructura.accdb`, junto con `DocumentosFacturas` y `DocumentosPersonas`. No se afirma su existencia en ProcontaVF. El inventario de §2.1 es evidencia de metadatos; las ampliaciones, servicios y permisos desde §4 siguen siendo propuestas.

Encargo recibido de Auditor: centralizar documentos de Terceros, Asientos, Contratos, Facturas de venta, FacturasCompra y futuras entidades; relaciones N:M; almacenamiento externo a Access; pestaña Documentos reutilizable; preparación de correo mediante Gestor Correo. El usuario autoriza este módulo y el plan WPF. Esto prevalece para este alcance sobre la indicación histórica WinForms/Krypton de AGENTS.md y sobre «no abrir aún otro módulo documental» del mapa anterior. No cambia esas instrucciones para otros módulos.

- Ficha canónica: `C:/Docs/VeriFactu/ProConta.Facturacion/docs/contratos/DOC-GESTOR.md`.
- Gobierno: [directorio](../DIRECTORIO_CONTRATOS.md), [mapa](../MAPA_PROYECTO.md), [registro](../REGISTRO_HILOS.json). Auditor mantiene sus entradas; esta entrega no las modifica.
- Repositorio documental inspeccionado: rama `codex/integracion-proconta-20260822`, HEAD `35554e9f5d97163c303628ec795265af4dcbe333`.
- Worktree futuro requerido: `C:/Docs/VeriFactu/ProConta.GestorDocumental`, inexistente en la comprobación inicial. Rama propia pendiente de acordar/verificar. No se crea en esta entrega ni se implementará en la raíz del repositorio principal.
- Revisión cruzada, aprobación del diseño físico, implementación, integración y aceptación visual: pendientes, por separado.

## 2. Evidencia local revisada

Lectura selectiva de archivos y Git, sin abrir Access ni ejecutar aplicaciones. Los contratos vecinos son evidencia documental de sus propietarios; no se han repetido sus pruebas ni certificado todo su código. Corte no atómico: hay desarrollos concurrentes.

| Evidencia | Hallazgo y consecuencia |
|---|---|
| [GeneradorPdfFactura.vb](../../src/ProConta.Facturacion.Integraciones/Documentos/GeneradorPdfFactura.vb), `Generar` y `GenerarTemporal` | Existe generación persistente bajo una raíz configurable, predeterminada junto al ejecutable; utiliza sociedad/ejercicio, año y número de factura y puede reemplazar el archivo. La generación temporal devuelve una ruta a cargo del llamador. Ninguna de esas rutas es una identidad documental estable. |
| [RepositorioDocumentosFacturaAccess.vb](../../src/ProConta.Facturacion.Persistencia.Access/Documentos/RepositorioDocumentosFacturaAccess.vb) | `GuardarRuta` escribe `Facturas.PDFFactura`. Ese repositorio sirve al render de facturas, no acredita el servicio transversal propuesto. |
| [RepositorioProcesamientoFacturaCompraAccess.vb](../../src/ProConta.Facturacion.Persistencia.Access/FacturasCompra/RepositorioProcesamientoFacturaCompraAccess.vb), `ResolverRutaPdf` | Resuelve ruta guardada y alternativas históricas de Synology. Se documenta como comportamiento existente; la conversión de esas rutas queda fuera del alcance actual. |
| [DAT-TERCEROS](C:/Docs/VeriFactu/ProConta.Terceros/docs/CONTRATO_PUBLICO.md) y [modelo](C:/Docs/VeriFactu/ProConta.Terceros/docs/TERCEROS_CONTRATO.md) | `IdTercero` estable; Cliente/Proveedor/Contacto son funciones distintas. El servicio actual no autoriza por Seguridad ni recibe sociedad/ejercicio. La ficha puede alojar componentes de otros propietarios. |
| [SEG-ACCESO vigente](C:/Docs/VeriFactu/ProConta.Seguridad/docs/INTEGRACION_ACCESO_REAL.md) | Emite sesión/contexto validado y ofrece `Revalidar`; no se ha localizado en esta revisión una política implementada de acceso a documentos concretos. |
| [CFG-GLOBAL](C:/Docs/VeriFactu/ProConta.ConfiguracionGlobal/docs/CONTRATO_PUBLICO.md) | Resuelve directorio de datos y Access por puesto/proceso. No acredita aún catálogo de almacenes documentales ni cambio coordinado de raíz. |
| [Gestor Correo](C:/Docs/VeriFactu/ProConta.GestorCorreo/docs/GESTOR_CORREO.md) | Posee cuentas, secretos y transporte. Su documento conserva preparación e historial de facturas en Facturación: hay que contrastar su evolución al envío/seguimiento central del encargo actual. No existe aquí evidencia de un compositor neutral de borradores con referencias documentales. |
| [Norma 43](C:/Docs/VeriFactu/ProConta.Norma43/docs/MOTOR_NORMA43.md) | Documenta idempotencia por sociedad, formato y SHA-256; esa regla es de lotes bancarios, no una deduplicación global trasladable a documentos. |
| [UI-PRINCIPAL](UI-PRINCIPAL.md) y [UI-BASE](UI-BASE.md) | Publican alojamiento WPF y recursos comunes. No acreditan integración de esta pestaña. |
| [Sociedades](C:/Docs/VeriFactu/ProConta.ActualizadorEsquema/docs/actualizador-esquema/SOCIEDADES_Y_EJERCICIOS.md) y [Compras](C:/Docs/VeriFactu/ProConta.ActualizadorEsquema/docs/actualizador-esquema/FACTURAS_COMPRA.md) | Sociedad permanente `TEXT(3)` y ejercicio `LONG` son identidades diferentes. El manifiesto de Compras usa `FacturasCompra.IdRegistro` como PK, no `IdFacturaCompra`. |

No se ha hecho inventario exhaustivo de documentos históricos. `Apuntes.Documento` y `LineasExtractoBancario.Documento` no se interpretarán automáticamente como rutas por su nombre.

## 2.1. Esquema heredado y reutilización de campos — revisión v0.3

Fuente de estructura: `C:/Docs/VeriFactu/ProcontaEstructura.accdb`, según instrucción explícita del usuario y extracción de Auditor sobre copia privada. DOC-GESTOR ha revisado todas las columnas y colecciones del [JSON de metadatos](../../tmp/maqueta-configuracion/documentos-esquema.json), no las bases ni valores de negocio. SHA-256 del JSON: `658331631D39F4BAC7A61DBEAB6B273B769ABA255476FE5F12DBC6EDE30D2E94`; identifica el JSON, no la base original. La evidencia está en una carpeta temporal; este inventario queda en el contrato y Auditor decidirá su archivo duradero. No se deduce que Estructura contenga datos históricos ni que las tres tablas estén instaladas en ProcontaVF.

La extracción contiene `Columns`, `Primary_Keys`, `Foreign_Keys` e `Indexes`. Las tres últimas colecciones están vacías en las tres tablas. Esto no acredita PK, FK, unicidad, autonumeración ni ausencia definitiva de restricciones fuera del alcance de la extracción. ActualizadorEsquema deberá contrastar relaciones/índices mediante inspección adecuada sobre copia autorizada antes de diseñar la evolución; no se ha realizado esa comprobación adicional aquí.

`Documentos` tiene 34 columnas. `Id` es LONG nullable según ACE, sin default informado: no se afirma autonumérico ni único. Solo los cuatro booleanos figuran no nullable; todos los demás campos son nullable. Ninguna columna informa default. `DocumentosFacturas` contiene `IdDocumento` e `IdFactura`, ambos LONG nullable; `DocumentosPersonas`, `IdDocumento` e `IdPersona`, también LONG nullable. La forma sugiere vínculos N:M, pero no garantiza extremos válidos ni parejas únicas. No se examinaron filas, duplicados, nulos o huérfanos.

### Reutilización de campos, sin renombrados ni cargas ejecutadas

| Campos de Documentos / tipo observado | Tratamiento propuesto |
|---|---|
| `Id` LONG | Conservar nombre y tipo como punto de partida para nuevas altas. Validar PK, nulabilidad y generación de claves. No usar ruta ni huella como identidad. No se migran IDs históricos. |
| `Nombre`, `Descripcion` TEXT(255) | Útiles primera fase; conservar ambos. Nombre se proyecta como título de API; no asumir que es el nombre físico del fichero. |
| `Fichero`, `RutaIniOriginal` TEXT(255), `Extension` TEXT(50) | El usuario confirma que Fichero contiene hoy ruta absoluta. En nuevas incorporaciones Fichero guardará exclusivamente ruta relativa a la raíz configurable (§5). No convertir rutas existentes. RutaIniOriginal conserva su definición pendiente y no sustituye al resolvedor; Extension conserva su campo. |
| `Tamaño` LONG | Confirmar unidad, negativos y límites antes de mapear a bytes. El tamaño verificado futuro en versión requiere capacidad suficiente; no sobrescribir el dato original sin evidencia. |
| `FechaDocumento` DATETIME | Fecha funcional útil primera fase; distinta del instante de incorporación. Conservar precisión y nulabilidad. |
| `UsuarioCreacion`, `UsuarioModificacion` LONG | Para nuevas altas/cambios, vincular actor real autenticado con Seguridad mediante diseño físico acordado. No mapear usuarios históricos ni modificar sus valores. |
| `FechaCreacion`, `HoraCreacion`, `FechaModificacion`, `HoraModificacion` DATETIME separados | Conservar pares. Combinar solo tras confirmar semántica, zona horaria y tratamiento de pares incompletos/horas ambiguas. No etiquetar UTC por deducción. La incorporación UTC nueva no sustituye esas fechas. |
| `AutoGenerado` Boolean no nullable | Conservar indicador, separado del modo de incorporación: un documento importado puede ser autogenerado. No derivar Manual de False sin confirmar semántica. |
| `IdEstado`, `IdSubTipoDocumento` LONG | Conservar referencias; catálogos y correspondencias pendientes. No mapear IdEstado a Activo/Retirado ni subtipo a permisos por su número. |
| `PalabrasClaveLibres` texto largo | Metadato pasivo preservado; búsqueda opcional posterior. No interpretarlo como comandos ni catálogo normalizado disponible. |
| `Principal`, `Compilado`, `Publicado` Boolean no nullable | Preservar legado; semántica pendiente. Principal no identifica automáticamente versión oficial; Publicado no concede acceso público. Sin motores de compilación/publicación en fase 1. |
| `PDFIndexado` TEXT(255), `TextoOCR` texto largo | Preservación histórica; confirmar si PDFIndexado es ruta u otra referencia. No generar OCR, indexar ni crear derivados por la existencia de estos campos. |
| `EstadoFirma`, `TipoAdquisicionFirma` byte; `ParametrosFirma` texto largo | Fuera del incremento inicial. Conservar sin interpretar valores ni validar firmas por esos indicadores. No ejecutar parámetros. |
| `FechaFirma`, `HoraFirma`, `FechaRechazoFirma`, `HoraRechazoFirma`, `FechaRevocacionFirma`, `HoraRevocacionFirma` DATETIME separados | Preservar hechos históricos declarados, pendientes de semántica/zona y evidencia de firma. No crear flujos de firma/rechazo/revocación. |
| `ParametrosEnvioEmail` texto largo | Dato histórico opaco; no cargar como configuración ni generar destinatarios/envíos. Adopción futura requiere análisis autorizado y contrato de Gestor Correo. |
| `ParametrosPostProcesoSQL` texto largo | Dato histórico para analizar, **nunca instrucciones a ejecutar**. No invocarlo en altas, importación, consulta, migración o recuperación. Ninguna API propuesta admite SQL documental arbitrario. |

El texto largo se reconoce por tipo, indicador largo y longitud publicada 0; no significa capacidad nula. Los campos fuera de fase 1 se conservan como referencia estructural y permanecen intactos en origen. No se copian valores ni se prepara una carga histórica. ActualizadorEsquema decidirá su tratamiento estructural sin activar firma, OCR, publicación o parametrizaciones.

### Decisión de alcance: sin migración documental histórica

Decisión explícita del usuario, recibida por Auditor el 01/10/2026: **no es necesario migrar los documentos actuales ni sus vínculos**. Quedan retirados la conversión de rutas antiguas, copia/importación del archivo documental histórico y mapas Personas → Terceros / Facturas antiguas → destino para este módulo. Sustituye las propuestas v0.1/v0.2 incompatibles. No autoriza borrar tablas, filas ni archivos originales; tampoco cancela otras migraciones ERP.

El esquema heredado se conserva como referencia para reutilizar nombres/campos y diseñar cambios estructurales reproducibles con ActualizadorEsquema. Los vínculos nuevos se crearán contra entidades del modelo vigente, con claves validadas y FKs reales; no se cargarán ni emparejarán IDs históricos. La existencia de DocumentosPersonas no obliga a mantener Personas como identidad funcional nueva: el vínculo a Terceros y su nombre físico siguen pendientes de diseño, sin migración de filas.

La extracción no muestra sociedad, huella, raíz lógica, clave idempotente, versiones ni auditoría separada. Son ampliaciones propuestas para nuevas incorporaciones; no se rellenarán a partir de documentos históricos. La sociedad de las altas se obtiene del contexto autorizado y se valida contra los registros nuevos vinculados.

Preguntas estructurales pendientes: generación y restricciones de Documentos.Id; claves de entidades vigentes; reutilización de catálogos estado/subtipo; unidad y capacidad de Tamaño; semántica de fechas/horas y campos fuera de fase 1. No se necesitan mapas ni inventarios históricos para iniciar este módulo. Fichero tiene ya semántica decidida: ruta relativa nueva según §5.

## 3. Fronteras de responsabilidad

| Propietario | Responsabilidad |
|---|---|
| DOC-GESTOR | Identidad, versiones, metadatos, almacenamiento, integridad del contenido, vínculos, consulta documental autorizada, apertura/obtención de adjuntos y auditoría de sus operaciones. Único escritor funcional objetivo; inventariar y adaptar escritores heredados antes de atribuirle exclusividad efectiva. |
| Terceros | Identidad, funciones, contactos/destinatarios y alojamiento de la pestaña. No escribe tablas documentales por SQL. |
| Facturación / Reporting | Facturación solicita/genera el PDF con su renderizador y decide su significado, revisión y momento de emisión. DOC-GESTOR conserva el resultado y lo vincula. No recalcula ni reemite facturas. |
| Compras, Asientos, Contratos | Identidad y semántica del registro, estado, pertenencia a sociedad/ejercicio y relaciones autorizadas con Terceros; proporcionan adaptadores de consulta. |
| SEG-ACCESO | Autenticación y autoridad de permisos. DOC-GESTOR aplica sus decisiones en cada servicio, no solo en botones. |
| CFG-GLOBAL | Resolución/configuración de raíces y despliegue por puesto; contrato documental pendiente. |
| Gestor Correo | Composición de correo, envío y seguimiento objetivo. Recibe adjuntos de versión concreta; no adquiere propiedad del archivo original. Transición del historial existente pendiente de acuerdo. |
| ActualizadorEsquema | Diseño físico contrastado, versión compatible y migraciones reproducibles. Ninguna pantalla ejecuta DDL. |
| MigradorERP / ContaSol | Conservan sus otras migraciones ERP. Este módulo no les encarga migrar documentos/vínculos ni crear mapas documentales históricos. |

Sin edición de contenido, OCR, firma digital, retención legal automatizada ni gestor de carpetas general en el primer incremento. La retención se definirá con los responsables funcionales; aquí no se fijan plazos fiscales.

## 4. Evolución propuesta del legado e invariantes

Se parte de las tres tablas de §2.1, no de un esquema independiente. Se retira la propuesta v0.1 de sustituir Documentos.Id por una PK UUID y de crear DocumentosFacturasVenta en paralelo. Se propone conservar Documentos.Id LONG como identidad local estable, tras validar unicidad, nulabilidad y generación de altas. IdDocumento en API/tablas hijas representa Documentos.Id, sin renombrado implícito. Identificador global adicional solo si una necesidad concreta lo justifica. El modelo de destino busca una maestra común capaz de aplicar FKs reales. **Antes de DDL se verificará la coexistencia física de todas las tablas destino en la misma Access.** Si algún módulo utiliza otra base, no se presentará una FK entre bases como integridad garantizada: se resolverá la ubicación con ActualizadorEsquema antes de habilitar ese vínculo.

| Tabla heredada / ampliación | Campos y restricciones objetivo, aún no garantizados |
|---|---|
| `Documentos` — existente en Estructura | Reutilizar Id LONG, Nombre, Descripcion, FechaDocumento y Fichero según §2.1; Fichero será relativo para nuevas altas. Titulo de API se proyecta desde Nombre, sin duplicarlo. Proponer IdSociedad, origen/instante/operador de incorporación y revisión de concurrencia como ampliaciones. No reinterpretar IdEstado/IdSubTipoDocumento sin catálogo; retirada nueva con semántica diferenciada hasta acordarlo. PK/FK y nulabilidad de destino pendientes. |
| `VersionesDocumento` — ampliación | `IdVersionDocumento` UUID PK propuesto; `IdDocumento` LONG FK a `Documentos.Id`; `NumeroVersion` positivo, único por documento; `NombreOriginal`; `TipoContenido`; `Extension`; `TamanoBytes`; `HuellaSha256`; `FechaIncorporacionUtc`; `IdOperadorIncorporacion` FK; `IdAlmacen` FK; `RutaRelativa`; `ModoAlmacenamiento` (Gestionado en fase 1); `EstadoContenido` (Pendiente, Disponible, Incidencia, PurgaPendiente, Purgado). Contenido confirmado inmutable. |
| `AlmacenesDocumentales` | `IdAlmacen` estable PK; código lógico único y estado habilitado. CFG resuelve su raíz por instalación/puesto. No persiste una ruta absoluta por documento gestionado. |
| `OperacionesDocumentales` | UUID de operación; sociedad; productor; clave idempotente; huella de solicitud; documento/versión resultantes mediante FKs; fase, fechas, operador y diagnóstico saneado. Unicidad de `(IdSociedad, Productor, ClaveIdempotencia)`. Registro durable de recuperación. |
| `EventosDocumento` | Identidad de evento; documento y versión opcional mediante FKs; actor, instante UTC, operación, motivo y correlación. Altas, versiones, vínculos/desvínculos, retirada, purga y recuperación auditables. Eventos de intentos sin documento se correlacionan con operación/Seguridad. |
| Vínculo nuevo a Terceros, con DocumentosPersonas como referencia estructural | IdDocumento LONG FK a Documentos.Id e IdTercero FK a Terceros vigente. Nombre DocumentosTerceros propuesto, por acordar con ActualizadorEsquema. Sin traducir ni cargar IdPersona histórico; no duplicar relaciones activas. Pareja única, propósito y auditoría propuestos. |
| `DocumentosAsientos` | `IdDocumento` FK, clave real de Asientos FK, propósito y auditoría. Pareja única. |
| `DocumentosContratos` | `IdDocumento` FK, clave real de Contratos FK, propósito y auditoría. Pareja única. No habilitar hasta confirmar tabla/PK y propietario. |
| `DocumentosFacturas` — nombre heredado reutilizable | IdDocumento LONG FK a Documentos.Id e IdFactura referenciando la PK validada de Facturas de venta vigente. Solo vínculos nuevos; no traducir ni cargar filas históricas. Pareja única, propósito y auditoría propuestos. |
| `DocumentosFacturasCompra` | `IdDocumento` FK, `IdRegistroFacturaCompra` FK propuesta a `FacturasCompra.IdRegistro`, propósito y auditoría. Pareja única. |

Los vínculos heredados no tienen UUID en la extracción. Su identidad inicial será la pareja validada de extremos; identificador adicional y revisión para concurrencia/auditoría se evaluarán como ampliaciones. Todas las tablas se indexarán por entidad destino y documento. Las FKs restringen borrados; no hay cascadas que borren silenciosamente el archivo o su historial. La baja de una entidad con vínculos requiere coordinación de su propietario.

Los tipos físicos de ampliaciones son pendientes: UUID de versiones/operaciones como GUID nativo ACE o texto de 36 (no sustitución de Documentos.Id LONG), tamaños de texto, representación exacta de bytes enteros de 64 bits y límites/índices deben probarse en ACE x64 por ActualizadorEsquema. SHA-256 será hexadecimal canónico de 64 caracteres, no índice único; Los nuevos instantes UTC se persistirán en DATETIME con convención explícita y conversión local en UI; las fechas heredadas conservan el tratamiento pendiente de §2.1. Las claves externas conservan el tipo de su tabla real, incluido operador LONG y sociedad TEXT(3).

Reglas propuestas:

1. Un documento pertenece a una sociedad permanente, puede abarcar varios ejercicios de esa sociedad y enlazar N registros. No se comparte entre sociedades en el primer incremento. Un contrato común requerirá registros documentales independientes; una futura excepción necesita autorización explícita, modelo y permisos propios.
2. Terceros es identidad global, pero el vínculo y la consulta documental quedan en la sociedad autorizada del documento. No hay vínculos adicionales a Clientes o Proveedores. La relación funcional se resuelve mediante su `IdTercero`.
3. No hay tabla de vínculos `TipoEntidad + Id`. Una nueva entidad añade tabla con FK, adaptador tipado y migración; el DTO de presentación puede indicar tipo y texto de procedencia sin sustituir integridad persistente.
4. El vínculo apunta al documento lógico. La consulta ordinaria presenta la última versión disponible y permite historial autorizado. Adjuntos y documentos emitidos usan `IdVersionDocumento` explícito para no cambiar de contenido después de seleccionarlos.
5. Añadir una versión es deliberado, con motivo y revisión esperada. Para una factura emitida, conserva el PDF original; una nueva representación autorizada queda como versión diferenciada. Una rectificativa es otra factura y otro documento. Nunca se regeneran silenciosamente bytes ausentes como si fueran el original.
6. No hay deduplicación automática por nombre, número de factura ni huella. La misma huella puede avisar dentro de la sociedad; el usuario puede vincular el documento existente si tiene permiso. Entre empresas no se busca ni se revela coincidencia.

## 5. Archivos, concurrencia y consistencia

**Propuesta para nuevas incorporaciones: copia gestionada.** El archivo elegido por el usuario se copia al almacén; su original no se mueve ni borra. Esto no incluye copia/importación del archivo documental histórico. Referencia externa queda fuera del primer incremento y no puede introducir una ruta absoluta en Fichero.

**Decisión del usuario:** las nuevas escrituras de `Documentos.Fichero` guardan una ruta RELATIVA a una carpeta documental configurable. Valor predeterminado: `Path.Combine(AppContext.BaseDirectory, "Documentos")`, equivalente a la carpeta base de la aplicación más Documentos, nunca al directorio de trabajo. La raíz configurada es una ubicación absoluta resuelta por el servicio central de almacenamiento; no se repite dentro de Fichero. No se convierten valores absolutos históricos.

El servicio propuesto `ResolverRutaDocumentoAsync` recibe contexto autorizado e identidad documental, lee Fichero/versión en persistencia y obtiene la raíz configurada. Combina y normaliza, valida pertenencia al directorio raíz con límite de segmento (no solo prefijo textual) y rechaza rutas absolutas, UNC en Fichero, rutas de unidad relativas, segmentos de escape, enlaces/reparse points que salgan del almacén y nombres inválidos. No expone una API pública que acepte rutas arbitrarias como permiso. La ruta absoluta resultante queda dentro del adaptador de almacenamiento; las UI y Correo consumen recursos autorizados. Aplicar la misma validación al leer y al escribir.

Ruta relativa orientativa de nueva incorporación: `<sociedad>/<IdDocumento>/<IdVersionDocumento>.<extension validada>`. La organización de subcarpetas es propuesta, no una decisión ya aceptada. Validar capacidad de Fichero TEXT(255) sin truncar; cualquier ampliación necesaria corresponde a ActualizadorEsquema.

Para compatibilizar Fichero con versiones, se propone que `Documentos.Fichero` refleje la ruta relativa de la versión vigente confirmada, mientras `VersionesDocumento.RutaRelativa` conserva cada versión inmutable. Actualizar esa selección y Fichero juntos en transacción corta al confirmar versión; no mantener dos escritores independientes. Un adjunto o emitido conserva su IdVersionDocumento y nunca depende de que cambie Fichero. Esta semántica de versiones queda pendiente de contraste; la obligación de ruta relativa ya está decidida.

Si la carpeta predeterminada no permite escribir, detener la incorporación, explicar la ubicación y el permiso que falta y ofrecer elegir una carpeta accesible. No redirigir silenciosamente a temporal, perfil de usuario u otra raíz. La selección se valida antes de guardarla; cancelar mantiene configuración y datos anteriores.

**Cambiar ubicación y trasladar son operaciones distintas.** Seleccionar una ubicación existente configura dónde buscar los documentos y requiere comprobar que corresponde al repositorio esperado; no copia, mueve ni borra archivos. Si faltan documentos o no se puede acreditar coherencia, informar y conservar la configuración anterior hasta resolverlo. Un eventual traslado será una operación separada, expresamente solicitada y diseñada con inventario, copia verificada, coordinación y conmutación; no se ejecuta al editar la raíz ni forma parte de esta tarea.

**Pendiente de CFG-GLOBAL/Auditor:** mecanismo de persistencia, distribución y verificación de configuración entre equipos para que el mismo Fichero no apunte a repositorios distintos. Una ruta predeterminada local no garantiza esa coherencia. Acordar identificación del repositorio y validación de selección; no se impone ahora UNC, sincronización, configuración central ni una política por puesto. IdAlmacen/catálogo son propuestas sujetas a este acuerdo.

Access y archivos no comparten transacción. Protocolo propuesto:

1. Revalidar contexto, permisos, entidad y compatibilidad de esquema. Reservar operación con clave única y huella de petición; la concurrencia se resuelve por índice, no por «consultar y luego insertar» sin restricción.
2. Copiar a staging de ese almacén, calcular tamaño/SHA-256 sobre los bytes copiados y cerrar el archivo. No mantener transacción ACE durante lectura/copias largas. Límites de tamaño/extensiones se configuran; verificar contenido antes de abrirlo y no ejecutar adjuntos automáticamente.
3. Transacción corta: crear documento/versión Pendiente, vínculos y evento de operación. Revalidar entidad/sociedad; las FKs protegen borrado concurrente. Una versión Pendiente nunca se entrega para abrir o enviar.
4. Publicar staging al destino exclusivo de esa versión sin reemplazo. Si ya existe por reintento, verificar huella y tamaño. La atomicidad del renombrado en el almacenamiento elegido deberá comprobarse; no se presume para proveedores de sincronización.
5. Transacción corta: marcar Disponible y completar operación. Si falla, la operación durable permite recuperar; no se responde como Disponible antes de confirmar.

Recuperador: operación sin fila de versión → revisar/eliminar staging vencido con exclusión; versión Pendiente y staging válido → publicar; destino válido y BD pendiente → completar; bytes ausentes o distintos → Incidencia, sin fabricar contenido. La limpieza solo afecta temporales identificados propios, con exclusión de operaciones activas; nunca borra archivos desconocidos por antigüedad. Un fallo después del commit puede devolver OperacionPendiente y su ID para consultar/reintentar.

Registro automático: Facturación proporciona una clave estable por sociedad, factura, clase de salida y revisión funcional. Igual clave + igual huella de petición y bytes → misma versión y mismos vínculos; igual clave + contenido/objetivo diferente → ConflictoIdempotencia. Si el render produce bytes variables, se consulta antes la operación existente; no se relaja la igualdad para ocultar cambios. Una regeneración explícita requiere nueva revisión autorizada. DOC-GESTOR no participa en la transacción de emisión fiscal: Facturación debe registrar una tarea/outbox durable recuperable para incorporar el PDF después, evitando que el fallo documental reemita una factura. Esa adaptación está pendiente.

La revisión esperada de metadatos/vínculos detecta ediciones concurrentes; el índice `(IdDocumento, NumeroVersion)` evita versiones duplicadas. Un reintento tras cancelar consulta primero la operación: cancelar no equivale a revertir una publicación ya confirmada.

Ausencia, acceso denegado del sistema de archivos, raíz desconectada y huella distinta son estados distintos. El listado conserva metadatos autorizados, muestra incidencia y ofrece reintentar o reparar con permiso; no elimina el registro. Restaurar contenido requiere la huella original; bytes diferentes constituyen nueva versión.

Copias de seguridad incluyen BD, almacenes y registro de operaciones en un punto coherente, con pausa/coordinación de publicaciones o reconciliación documentada. Restauración se prueba conjuntamente. No se considera suficiente copiar solo Access.

## 6. Consulta agregada y autorización

`ConsultarDocumentosTerceroAsync` parte de vínculos directos y de adaptadores de negocio explícitos: Tercero → Cliente → Facturas; Tercero → Proveedor → FacturasCompra; Contratos y Asientos solo con una relación validada por sus propietarios. No deduce pertenencia por NIF, nombres, texto de apuntes o importes. No crea automáticamente un vínculo directo redundante para cada factura.

Primero se autoriza contexto y operación; después cada documento y cada procedencia. Solo se agregan relaciones visibles. Se agrupa por `IdDocumento`, mostrando una fila y la lista de procedencias autorizadas («adjuntado al tercero», «factura …», etc.). Tener acceso a un tercero o a una ruta de relación no concede acceso a documentos restringidos. Un documento con dos rutas visibles aparece una vez; no se filtran a la UI títulos, recuentos ni IDs de rutas prohibidas. Una relación directa no elude una restricción del documento.

Ámbito predeterminado propuesto: sociedad actual; documentos directos permanentes y registros del ejercicio actual. Incluir otros ejercicios es filtro explícito sujeto a autorización por cada ejercicio, sin modificar el contexto global. Búsqueda/paginación/total se aplican tras autorización y agrupación, con orden estable por fecha e ID. Un adaptador obligatorio no disponible produce resultado incompleto identificado por módulo, nunca «sin documentos» como si fuera total; un módulo no autorizado no se revela mediante ese aviso.

Acciones de permiso propuestas, pendientes de SEG: consultar, incorporar, vincular, desvincular, abrir/exportar, preparar adjuntos, añadir versión, reparar, retirar y purgar. Comprobar acceso a ambos extremos al vincular y autoridad sobre el vínculo al desvincular. Las categorías restringidas se autorizan explícitamente. Mientras no exista política acordada, denegar esas capacidades; no reutilizar automáticamente la política permisiva de reports.

Desvincular elimina solo la relación elegida y audita motivo; no afecta al archivo ni a otros enlaces. Un vínculo indirecto se administra en el registro propietario si está autorizado: no se oculta simulando un desvínculo directo. Retirar documento y purgar bytes son operaciones distintas; requieren permiso específico, comprobación de referencias, restricciones del originador, retención y adjuntos reservados. Los emitidos no se purgan por la simple ausencia de vínculos. Primer incremento sin purga física; conservar historial y documentos sin vínculos para revisión autorizada.

Las autorizaciones de aplicación no protegen por sí solas archivos accesibles directamente en una carpeta compartida. SEG/infraestructura deben acordar ACLs o acceso mediante servicio si se requiere aislamiento por documento. No prometer confidencialidad por registro con una carpeta legible por todos los operadores.

## 7. API propuesta v0.3 (no disponible en código)

Servicios de aplicación VB.NET/.NET 10/x64, independientes de WPF; DTO sin controles, DataRow ni conexiones. Todos los métodos reciben contexto validado emitido por Seguridad, correlación y CancellationToken. El servicio obtiene actor desde la sesión, no de un texto libre. Firma conceptual: `MetodoAsync(contexto, solicitud, cancelacion) -> Task(Of ResultadoDocumental(Of T))`; nombres finales de tipos pendientes de contraste.

| Método | Solicitud → resultado y efectos |
|---|---|
| `IncorporarArchivoAsync` | Fuente legible, título/clase, destino tipado y clave idempotente → documento/versión/operación. Copia gestionada y vínculo confirmado o estado pendiente recuperable. |
| `RegistrarGeneradoAsync` | Bytes/flujo del productor, metadatos, referencia funcional tipada, revisión y clave estable → misma identidad al reintentar; no renderiza ni envía. |
| `AgregarVersionAsync` | Documento, contenido, motivo y revisión esperada → versión nueva inmutable; verifica restricciones del originador. |
| `VincularTerceroAsync`, `VincularAsientoAsync`, `VincularContratoAsync`, `VincularFacturaVentaAsync`, `VincularFacturaCompraAsync` | Documento + clave tipada de destino + propósito → vínculo existente o creado. Validan sociedad y permiso; no aceptan nombres de tabla libres. |
| `DesvincularTerceroAsync` y equivalentes tipados | Pareja tipada de extremos del vínculo, revisión esperada y motivo → confirmación con evento. No borra documento. |
| `ConsultarDocumentosTerceroAsync` / `ConsultarDocumentosEntidadAsync` | Tercero o destino tipado, filtros, cursor y tamaño → página de `DocumentoResumen` con versión visible, estado, procedencias autorizadas y acciones permitidas orientativas. No concede permisos para acciones posteriores. |
| `ObtenerDetalleAsync` | Documento → metadatos e historial de versiones autorizado, sin rutas físicas públicas. |
| `AbrirContenidoAsync` | Versión exacta → recurso de lectura con disposición explícita; verifica permiso, disponibilidad e integridad antes de ceder contenido. La UI decide visor seguro. |
| `PrepararAdjuntosAsync` | Lista explícita de versiones y contexto de correo → colección de referencias/recursos reservados, nombres, tipo, tamaño, huella y vencimiento. No envía ni encola envío. |
| `ConsultarOperacionAsync` | ID/clave de operación propia autorizada → estado y resultado para recuperación/reintento. |
| `RetirarDocumentoAsync` | Documento, revisión y motivo → retirada auditada si cumple reglas. No purga bytes. |

`DestinoDocumental` será una unión cerrada de DTO tipados; el adaptador correspondiente conoce la tabla concreta. `DocumentoResumen` contiene IdDocumento, IdVersionDocumento, título/nombre, clase/origen, tamaño, fecha/operador visibles, estado y colección de procedencias. Las respuestas no exponen rutas UNC ni existencia de registros prohibidos.

`ResultadoDocumental` distingue éxito, pendiente y error; códigos propuestos: ContextoNoValido, NoDisponibleONoAutorizado, EsquemaIncompatible, EntidadNoValida, SociedadNoCoincide, ArchivoAusente, AlmacenNoDisponible, AccesoArchivoDenegado, ContenidoAlterado, LimiteExcedido, ConflictoIdempotencia, ConflictoConcurrencia y OperacionPendiente. Mensaje funcional y acción de recuperación; detalle técnico saneado en log con correlación. No transformar un error de permisos o raíz en lista vacía.

Ejemplos de consumo: adjuntar contrato escaneado a un Tercero mediante IncorporarArchivo, obtener un único documento y añadir después vínculo al Contrato; registrar PDF emitido contra FacturaVenta y verlo indirectamente desde Tercero; reintentar esa misma clave y recuperar la misma versión; seleccionar dos versiones para un borrador de correo sin invocar SMTP.

## 8. Entrega a Gestor Correo

Flujo objetivo: selección explícita en Documentos → revalidar permiso de preparar/exportar → resolver versiones → abrir borrador editable de Gestor Correo. Destinatario sugerido por Terceros según uso; usuario puede elegir otro, con las mismas reglas de exportación. No inferir dirección única ni enviar al abrir la ficha.

Gestor Correo conserva borrador, destinatarios, envío y seguimiento; DOC-GESTOR conserva identidad/contenido. Correo debe materializar una copia durable verificada o mantener una reserva de versión hasta terminar/cancelar el trabajo; una ruta temporal con vida de ventana no sirve para reintentos. Acordar confirmación de recepción, liberación, vencimiento y tratamiento de borradores abandonados. Al enviar se revalida autorización vigente según política acordada y la integridad del adjunto; un borrador no es permiso perpetuo.

Seguimiento retorna el identificador del mensaje de Correo y resultado propio de transporte, sin convertir aceptación SMTP en lectura o entrega al destinatario. Las referencias a versiones se acuerdan con el propietario del historial. DOC-GESTOR no guarda contraseñas ni usa MailKit directamente. Esta entrega no crea borradores reales ni envía mensajes.

## 9. Plan WPF y pruebas de aceptación

1. **Contrastar contratos:** resolver P1–P6 de §10. Incluir reutilización estructural y exclusión de migración documental de §2.1. No desarrollar una solución completa ni aplicar DDL antes. Auditor registra acuerdos y asigna consumidores.
2. **Preparar implementación:** crear/verificar worktree permanente indicado en §1, rama propia y acceso; respetar instrucciones y submódulos. Separar dominio/aplicación, adaptador Access, almacenamiento y UI. No duplicar infraestructura común.
3. **Incremento mínimo:** almacenamiento gestionado, alta manual, tabla de vínculo a Terceros, consulta/apertura y desvínculo con permisos. Migración exclusiva de ActualizadorEsquema probada en base descartable.
4. **Componente `ContenidoDocumentosWpf` propuesto:** UserControl y ViewModel alojables en pestaña, sin ventana ni selector propio de sociedad/ejercicio. Recibe servicios, contexto y entidad del host. Tabla con nombre, clase, origen, fecha, tamaño, estado y procedencias; búsqueda y paginación; selección múltiple explícita para adjuntos. Panel de detalle/versiones. Acciones Incorporar, Abrir, Preparar correo y Desvincular según capacidades. Recursos UI-BASE, teclado, foco, ToolTips descriptivos, DPI y mensajes centrales. Líneas bancarias sirve como referencia de alojamiento, no como código documental ya reutilizado.
5. **Ciclo de vida:** cambio de tercero/contexto cancela consultas y descarta respuestas obsoletas; resolver operaciones pendientes antes de cierre con contrato del host. No mantener ACE abierto desde ViewModel. Liberar flujos, previews y reservas al finalizar su uso; el borrador durable de Correo conserva su propia copia/reserva.
6. **Segundo incremento:** registro PDF idempotente, adaptadores de consulta indirecta y transferencia a borrador Correo; después Asientos, Contratos, Compras. No incluye importaciones documentales históricas. Habilitar cada uno tras confirmar claves y permisos.

Pruebas futuras necesarias: N:M sin duplicados; documento con caminos visibles y ocultos sin fuga; aislamiento entre sociedades y ejercicios; rechazo de huérfanos por FKs; registro simultáneo/reintentos y conflicto de bytes; fallo inducido en cada fase BD/archivo y recuperación; ruta ausente/SMB desconectado/huella cambiada; escape de raíz; raíz predeterminada sin escritura sin redirección silenciosa; seleccionar ubicación sin mover archivos; coherencia de repositorio entre equipos según acuerdo futuro; desvincular sin borrar otros enlaces; versión emitida inmutable; cambio de contexto con consulta pendiente; adjunto que sobrevive al cierre de la pestaña y no cambia al agregar versión; compatibilidad de migración estructural y restauración conjunta. UI se validará manualmente por el usuario; compilación o capturas no sustituyen esa aceptación.

## 10. Decisiones pendientes para coordinación de Auditor

| ID / prioridad | Responsables | Decisión o evidencia requerida |
|---|---|---|
| P1 — antes de esquema | ActualizadorEsquema + propietarios | Contrastar restricciones y generación de Documentos.Id y de los vínculos heredados; ratificar ubicación física común, PK/tipos de Facturas, Asientos y Contratos, y FKs/índices/tipos ACE del modelo; comprobar sociedad de cada extremo. No aplicar a `C:/Docs/VeriFactu/ProcontaVF.accdb`. |
| P2 — antes de acceso funcional | SEG-ACCESO | Permisos por acción/documento/clase, lectura de otros ejercicios, reglas para vínculos múltiples, revalidación en Correo y protección real de archivos compartidos. Ratificar aislamiento por sociedad propuesto. |
| P3 — antes de persistir archivos | CFG-GLOBAL + infraestructura | Cómo persistir y distribuir configuración coherente entre equipos para que un mismo Fichero resuelva el mismo repositorio; pendiente, sin imponer UNC ni política central/local. Validación de escritura, identificación de ubicación existente, límites, publicación, recuperación y backup. Traslado como operación separada futura. |
| P4 — antes de integrar PDF | Facturación / Reporting | Punto durable posterior a emisión, clave idempotente/revisión, selección de versión oficial y retirada progresiva de `PDFFactura` como autoridad. No mantener dos escritores independientes. |
| P5 — antes de correo | Gestor Correo + Facturación + Terceros | Puerto de borrador, destinatario por uso, posesión durable de adjuntos, referencia a versiones y transición del historial/seguimiento actualmente documentado en Facturación. |
| P6 — antes de agregar consultas | Terceros + Asientos + Contratos + Compras | APIs tipadas de relación/permiso; ejercicio predeterminado y filtros; relación histórica de tercero que no debe cambiar al editar una ficha; dueño y clave de Contratos. |
| P7 — alcance resuelto | Usuario / Auditor | Excluida migración de documentos y vínculos actuales, conversión/copia histórica y mapas Personas/Facturas antiguos. Preservar originales. Las otras migraciones ERP continúan con su alcance propio. |
| P8 — antes de eliminación física | Dueños funcionales + Seguridad | Retención, bloqueos de documentos emitidos, reservas de correo, revisión de documentos sin vínculos y autorización de purga. Sin purga en el primer incremento. |

No habrá transición ni conversión de rutas documentales históricas en este módulo. Las nuevas incorporaciones usan Fichero relativo y el servicio central. La adaptación de consumidores para documentos nuevos se coordina con sus propietarios, sin reescribir ni borrar documentos anteriores.

## 11. Alcance real de esta entrega

Solo se crea y revisa esta ficha; v0.3 incorpora la decisión de ruta relativa y retira la migración documental histórica. Se revisaron AGENTS.md, status/diff e historial antes de escribir; los cambios ajenos presentes se preservan. Git requirió `safe.directory` limitado a cada comando por diferencia de propietario; no se modificó configuración global. No se hicieron commits, pruebas de negocio, compilaciones, cambios de código, migraciones, escrituras en bases, envíos ni contactos con otros hilos. La revisión final comprueba el archivo documental y sus enlaces locales, no la implementación futura.
