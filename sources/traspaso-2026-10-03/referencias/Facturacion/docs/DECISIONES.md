# Decisiones del proyecto

## 2026-08-09 — Aplicacion de escritorio .NET

La nueva facturacion se desarrollara como una aplicacion .NET de escritorio en
lugar de implementar la mayor parte de su funcionalidad en VBA.

Motivos principales:

- Interfaz mas rica y controlable.
- Grids, menus contextuales y eventos sin las limitaciones de Access.
- Mejor capacidad de prueba, refactorizacion y mantenimiento.
- Integracion mas natural con certificados, XML, SMTP y PDF.

Access y el ProConta existente continuaran conviviendo con la nueva aplicacion.

## 2026-08-09 — VB.NET

Se utilizara VB.NET porque es el lenguaje en el que el responsable del proyecto
tiene mayor experiencia y productividad.

Se acepta que C# dispone de mas ejemplos y recibe mas novedades de sintaxis. El
escenario elegido, WinForms y bibliotecas empresariales, forma parte de los usos
que Microsoft continua soportando especificamente para Visual Basic.

El proyecto utilizara tipado estricto mediante `Option Strict On`.

## 2026-08-09 — WinForms

Se utilizara WinForms por su productividad para aplicaciones empresariales y su
soporte de formularios, grids, eventos y controles personalizados.

No se adopta WPF inicialmente porque su complejidad adicional no aporta una
ventaja suficiente para los requisitos actuales.

## 2026-08-09 — .NET 10 LTS y x64

La solucion tendra como destino .NET 10 LTS para Windows y se compilara para
`x64`.

La arquitectura coincide con Access, ACE y el entorno VeriFactu ya probado. No
se utilizara `Any CPU`.

## 2026-08-09 — Access como base de datos

Se mantiene Microsoft Access porque:

- El volumen de datos no justifica SQL Server.
- Su diseñador de tablas y consultas facilita el mantenimiento.
- El responsable del proyecto domina su dialecto SQL.
- No requiere administrar un servidor adicional.

La aplicacion accedera mediante ACE/OLE DB. Se revisara esta decision solamente
si aparecen limites reales de volumen, concurrencia o disponibilidad.

## 2026-08-09 — Integracion .NET nativa con VeriFactu

La aplicacion referenciara la implementacion .NET del repositorio VeriFactu, que
actualmente ofrece un destino `net8.0` compatible con la aplicacion .NET 10.

No se utilizara el componente COM desde la nueva aplicacion. La capa COM queda
reservada para el prototipo VBA existente.

VeriFactu se encapsulara detras de una interfaz y un adaptador propios para que
el dominio no dependa de su API concreta.

## 2026-08-09 — Factura como unico informe propio

La aplicacion necesita inicialmente un unico documento formal: la factura en
PDF. Se generara desde .NET.

El resto del analisis y explotacion de datos se realizara principalmente mediante
Excel y consultas vinculadas a la base.

## 2026-08-09 — Desarrollo iterativo

No se realizara un analisis funcional exhaustivo de toda la aplicacion antes de
programar. Se construiran entregas verticales pequeñas y verificables.

Las reglas estructurales y fiscales importantes deben estabilizarse antes de
implementar las operaciones que dependan de ellas.

## 2026-08-09 — Idioma y convenciones del codigo

Los identificadores propios del dominio se escribiran en español siguiendo las
convenciones de .NET. Se utilizara `PascalCase` en la API publica,
`_camelCase` para campos privados y `camelCase` para parametros y variables
locales.

No se utilizaran prefijos de tipo ni `cls`. Se permiten prefijos cortos en los
controles WinForms porque facilitan la lectura de formularios y manejadores de
eventos.

Los acronimos se trataran como palabras normales en codigo (`Nif`, `Iva`,
`Pdf`, `Smtp`, `Aeat` y `Qr`). Los nombres existentes de la base de datos se
conservaran y se mapearan desde la capa de persistencia.

## 2026-08-09 — Documentacion de funciones y procedimientos

Toda `Function` y todo `Sub`, incluidos los procedimientos privados,
constructores y manejadores de eventos, estaran precedidos por comentarios XML.

La documentacion indicara como minimo:

- El objeto o finalidad del procedimiento.
- Los parametros de entrada y de entrada/salida.
- El retorno directo de la funcion.
- Las salidas indirectas mediante `ByRef`, propiedades, variables globales o
  efectos externos.

Se utilizan comentarios XML para que el contrato aparezca tambien en IntelliSense
y pueda formar parte de la documentacion generada del proyecto.

## 2026-08-09 — Voz de ProConta y mensajes orientados a soluciones

ProConta conservara una voz propia: cercana, conversacional, en primera persona,
dirigida al usuario de tu y con humor ocasional. Esta personalidad se considera
parte de la experiencia del producto porque facilita el vinculo emocional y
favorece su uso.

No se permitiran mensajes que se limiten a comunicar un fracaso. Todo error o
validacion debe aportar el maximo contexto disponible, explicar la causa y
proponer una solucion. Cuando la aplicacion pueda ejecutar el siguiente paso,
debe ofrecerlo directamente.

Los detalles tecnicos se registraran separadamente. La interfaz mostrara una
explicacion comprensible y podra ofrecer detalles adicionales para soporte.

La presentacion de mensajes se centralizara en un servicio propio; los
formularios y servicios de aplicacion no llamaran directamente a
`MessageBox.Show`.

## 2026-08-09 — Versionado de VeriFactu

El repositorio oficial de VeriFactu se incorpora como submodulo Git en
`externals/VeriFactu` y queda fijado a una revision concreta. La aplicacion lo
consume mediante `ProjectReference` desde la capa de integraciones.

Las actualizaciones se realizaran de forma explicita, revisando los cambios y
ejecutando todas las pruebas antes de aceptar una nueva revision. La dependencia
`System.Security.Cryptography.Xml` se fuerza actualmente a una version corregida
desde `Directory.Build.targets` para evitar conservar vulnerabilidades conocidas
de la version declarada por la revision externa.

## 2026-08-09 — Generacion de facturas programadas

Cada identificador seleccionado se procesa de forma independiente dentro de un
lote. El periodo facturado se obtiene de `FacturasProgramadas.FechaInicio` y
`FechaFin`; `ProximaFechaEmision` solo establece desde qué fecha puede generarse.
La factura conserva su procedencia mediante `FechaProgramadaOrigen`,
`IdFacturaProgramada` e `IdLoteFacturaProgramada`.

Todas las facturas se crean inicialmente pendientes de validar. Cuando se pide
validacion automatica se realiza un intento en VeriFactu. Un rechazo o un error
deja esa factura pendiente y evita generar su PDF y correo, pero no detiene las
demas facturas del lote. Sin validacion automatica, el proceso de esa factura
termina inmediatamente despues de crearla.

Los errores de PDF o correo tampoco revierten una validacion fiscal ya aceptada.
El resultado conserva la etapa alcanzada para permitir procesos de reintento
especificos en una entrega posterior.

## 2026-08-11 — Validación fiscal centralizada e idempotente

- Toda validación —generación programada, proceso manual, PDF o correo— utiliza
  `ValidadorFacturasVeriFactu`; ningún consumidor reimplementa los reintentos.
- Los intentos se clasifican como `ACEPTADO`, `ACEPTADO_DUPLICADO`, `RECHAZADO`,
  `ERROR_PREVIO_ENVIO` o `COMUNICACION_DUDOSA` en `VeriFactuEnvios.EstadoEnvio`.
- Tras una comunicación dudosa se utiliza `InvoiceRetrySend`, que recupera y
  remite el registro técnico original con la misma huella, encadenamiento y
  fecha de generación; no se genera un alta nueva.
- Si la AEAT rechaza el reenvío por duplicado, solo se considera éxito cuando
  `EstadoRegistroDuplicado` es `Correcta` o `AceptadaConErrores`. Un registro
  anterior `Anulada` o de estado desconocido permanece pendiente.
- `VeriFactuRespuestas.EsDuplicado` queda informado y la respuesta SOAP completa
  conserva el identificador y estado de la petición anterior para auditoría.

## 2026-08-09 — Creacion transaccional desde Access

La creacion desde una programacion reserva `SeriesFacturacion.UltimoNumero`,
inserta la cabecera y sus lineas y avanza `ProximaFechaEmision` dentro de una
unica transaccion nativa de ACE. No se solicita aislamiento `Serializable`
porque el proveedor de Access no lo admite; la primera escritura sobre la serie
mantiene el bloqueo necesario durante la reserva y las claves unicas actuan como
ultima barrera frente a duplicados.

Al generar una factura se avanzan `FechaInicio`, `FechaFin` y
`ProximaFechaEmision` por el número de meses de su periodicidad. Si `FechaFin`
está vacía, se materializa el final inclusivo del periodo completo. Se admiten
MENSUAL, BIMESTRAL, TRIMESTRAL, CUATRIMESTRAL, SEMESTRAL y ANUAL.

`DiaEmision` conserva el anclaje elegido para `ProximaFechaEmision`: del 1 al 28
es un día fijo y del 29 al 31 mantiene el desplazamiento respecto al final del
mes. Así, 31/01 pasa a 28/02 y vuelve a 31/03; 29/01 pasa a 26/02 y vuelve a
29/03. La fecha y hora real de ejecución se registra en
`FechaUltimaGeneracion`; no debe confundirse con el día de anclaje.

## 2026-08-13 — Duplicación operativa

- Cualquier factura puede duplicarse, cualquiera que sea su estado. La copia
  siempre nace como `Borrador`, conserva el contenido comercial y todas sus
  líneas, pero no hereda número, fechas de emisión, vínculos de rectificación,
  exportación contable, PDF, envíos ni procedencia programada.
- `DuplicarFactura` exige el `IdSociedadEjercicio` actual, igual que la creación
  manual. Siempre resuelve la serie normal predeterminada de ese ejercicio y
  congela su nombre y NIF de emisor; no arrastra esos valores del ejercicio
  histórico. Si el origen es rectificativo, el duplicado se convierte además en
  factura normal.
- `FechaOperacion` queda vacía en el duplicado. El período facturado sí se
  conserva como punto de partida editable.
- Una factura programada duplicada conserva cliente, contrato, fechas,
  periodicidad, preferencias y líneas. Reinicia `FechaUltimaGeneracion`, alta e
  historial y queda inactiva para que nadie la genere antes de revisarla.
- `FacturaProgramada` y `LineaFacturaProgramada` son agregados de dominio
  materializables. Su ciclo editable pertenece a `ServicioFacturasProgramadas`,
  separado del servicio de facturas emitibles.

Los importes se redondean por linea a dos decimales y alejandose de cero en los
puntos medios. La base descuenta primero el porcentaje y despues el importe fijo.
Los suplidos no generan IVA ni retencion; se suman al total. La retencion se
calcula sobre la suma de bases no suplidas. La cuota de recargo de equivalencia
se agrega temporalmente al campo de cabecera `IVA`, ya que el modelo actual no
dispone de otro campo agregado para esa cuota.

Los lotes programados solo pueden contener facturas de una sociedad. Se crean en
estado `PROCESANDO` y terminan como `COMPLETADO` o `ERROR`, respetando la regla
de validacion de la tabla. Los fallos individuales se guardan con programacion,
cliente y explicacion en `DescripcionError`; las facturas creadas pendientes de
validacion no se consideran por si mismas un error del lote.

## 2026-08-10 — Licencia de VeriFactu

Durante esta fase ProConta Facturación no se utilizará ni distribuirá como un
producto comercial. El proyecto se mantiene bajo las condiciones aplicables de
la librería VeriFactu. Si en el futuro se decide comercializarlo, se solicitará
previamente una licencia comercial a su titular.

## 2026-08-10 — Identidad del sistema informático

- Productor: Dinámica Ingeniería Suministros y Servicios, S.L.
- NIF del productor: B62344502.
- Nombre del sistema: ProConta.
- Identificador del sistema: PC.
- Número de instalación inicial: 1.

## 2026-08-10 — Cadena fiscal y primera prueba externa

La cadena fiscal de la librería VeriFactu se conserva en su ubicación estable
de `ProgramData`. No se traslada al directorio temporal de cada ejecución porque
forma parte del estado persistente por emisor y la propia librería bloquea su
cambio cuando ya contiene registros.

La factura ficticia `TEST-2026-0001`, por 1,21 euros y sin valor comercial, fue
aceptada en el entorno de pruebas con CSV `A-YERM3YV5J83MXL`. Se generó el PDF
con su QR y se envió a `ronald@dinamica.tech`. El primer intento SMTP quedó
auditado como error por usar el puerto 465; el segundo se completó después de
restaurar la configuración prevista de puerto 587 con STARTTLS. No se repitió
el envío fiscal.

## 2026-08-10 — Creación de facturas rectificativas

- Cada sociedad debe tener exactamente una serie con `EsRectificativa=True`.
- Una factura emitida solo puede tener una rectificativa y el vínculo se guarda
  en `Rectificativa.IdFacturaRectificada`.
- Las líneas conservan precio y porcentajes, pero invierten `Cantidad` y todos
  los importes derivados para que cabecera y desglose sean coherentes.
- La rectificativa genérica es `R4` de tipo `I`; si la original es simplificada
  (`F2`), se utiliza `R5`.
- La nueva factura nace en Borrador y la original pasa a
  `RectificacionPendiente` mientras se revisa. La original pasa a Rectificada
  solo cuando VeriFactu acepta la nueva. Si se elimina el borrador, la original
  vuelve atómicamente a Validada.
- El motivo se antepone a `NotasInternas`; si está vacío se registra como
  `(no especificado)`.

## 2026-08-11 — Estados y edición segura

- El ciclo utiliza cinco estados: `Borrador`, `PendienteValidar`, `Validada`,
  `RectificacionPendiente` y `Rectificada`. Se descarta un estado Error: el
  detalle técnico pertenece a los intentos y envíos, no al estado fiscal.
- La versión inicial no utiliza la anulación registral de la AEAT. La anulación
  económica se representa mediante una rectificativa completa con signo
  contrario, manteniendo coherencia fiscal y contable.
- Antes de devolver una factura `PendienteValidar` a Borrador, se consulta el
  servicio de registros propios de la AEAT por NIF emisor, número y fecha.
- Si la AEAT confirma la factura, se consolida `Validada` y se ofrece crear una
  rectificativa. Si la consulta no responde, se conserva `PendienteValidar` y se
  pide reintentar; nunca se abre a edición ante una respuesta incierta.
- Si la consulta encuentra el registro expresamente rechazado, se devuelve a
  Borrador y se presenta el motivo fiscal para que el usuario pueda corregirlo.
- Una ausencia en AEAT solo permite volver a Borrador cuando han transcurrido
  al menos cinco minutos desde el último intento, para evitar una decisión
  prematura por latencia del servicio.

## 2026-08-10 - Actualizacion anual por IPC o IRAV

- El proceso es manual y revisa contratos con factura programada activa cuyo
  proximo aniversario cae en los siguientes 45 dias.
- El valor oficial se materializa en `Contratos`; cada tipo de indice se
  consulta una sola vez por ejecucion y se rechaza si su mes dista mas de dos
  meses naturales de la fecha actual.
- La fecha efectiva es el dia 1 del mes del aniversario. La notificacion y la
  aplicacion sobre la factura programada son idempotentes para esa anualidad.
- El incremento se aplica a todas las lineas activas de la programacion dentro
  de la misma transaccion que crea la primera factura del nuevo periodo.
- Los fallos se aislan por contrato y no detienen la revision de los demas.

## 2026-08-10 - Motor de informes FastReport

- Las facturas PDF se generan con `FastReport.OpenSource` y el complemento MIT
  `FastReport.OpenSource.Export.PdfSimple`, ambos en version 2026.2.3.
- La presentacion reside en `Plantillas/Factura.frx` y puede modificarse con
  FastReport Designer Community Edition sin recompilar la aplicacion.
- Las plantillas no acceden directamente a Access. Reciben un objeto estable
  `ReportFactura` con emisor, cliente, lineas, importes, URL y QR VeriFactu.
- El exportador PDFSimple cubre la factura actual. Cifrado, firma PDF y PDF/A
  requeririan evaluar el exportador comercial.

## 2026-08-11 — Recuperación operativa y entrega de documentos

- Una factura validada sin PDF y un PDF sin correo enviado son situaciones
  normales. El PDF se genera después de obtener el QR y solo se entrega
  automáticamente cuando `FacturasProgramadas.EnviarPorEmail=True`.
- Los procesos manuales de envío siempre representan una solicitud expresa y
  no consultan esa preferencia de la programación original.
- Los fallos SMTP se conservan por intento en `EnviosCorreo` y, al terminar la
  selección, se envía a `Configuracion.EmailIncidencias` un resumen por empresa.
  El propio aviso se audita como `INCIDENCIA`, pero nunca genera avisos
  recursivos si también falla.
- Los lotes interrumpidos no se reanudan como una unidad: cada factura termina
  sus operaciones atómicas y los elementos aún no procesados entrarán en una
  nueva selección. La futura interfaz refrescará los datos al finalizar.
- Esta nomenclatura quedó reemplazada el 13 de agosto: los contratos pertenecen
  permanentemente a `Sociedades` mediante `IdSociedad` y la instancia anual se
  denomina `SociedadesEjercicios`.

## 2026-08-11 — Migración integral aplazada

- `ProcontaVF.accdb` se toma como punto de partida válido y no se reconstruirá
  ahora todo el modelo mediante una secuencia histórica de migraciones.
- Se mantienen los scripts parciales existentes porque siguen siendo útiles
  para documentar y repetir cambios concretos.
- La migración integral del modelo inicial se reactivará únicamente si
  necesitamos volver a importar todos los datos desde el ERP o preparar una
  instalación nueva desde cero. El versionado incremental posterior sí queda
  activo.

## 2026-08-11 — Versionado incremental del esquema

- Aunque no reconstruimos todo el modelo histórico, desde la base adoptada se
  versionará cada cambio estructural nuevo mediante `IMigracionEsquema`.
- Las modificaciones manuales en desarrollo son válidas, pero se reproducen
  después en una migración idempotente; nadie inserta directamente una versión
  en el historial.
- Una migración publicada es inmutable. Su hash detecta cambios accidentales y
  una aplicación antigua no abre una base que contenga migraciones desconocidas.
- Antes de cualquier cambio se exige acceso exclusivo y se crea automáticamente
  una copia. Los fallos quedan registrados por intento y no permiten continuar
  hacia la interfaz.

## 2026-08-11 — Concurrencia en facturas programadas

- La reserva de número, la cabecera, las líneas y el avance de la programación
  permanecen dentro de una única transacción de Access.
- Cada lote congela `FechaGeneracionHasta` al iniciarse. El creador vuelve a
  comprobarla dentro de su transacción y rechaza una programación que otro
  proceso haya avanzado más allá de ese límite.
- El proceso perdedor no reserva número ni deja cabeceras o líneas parciales;
  solicita refrescar la selección antes de generar deliberadamente otro periodo.
- La prueba con dos procesos reales cubre tanto la misma programación como dos
  programaciones distintas que comparten serie.

## 2026-08-11 — Inmutabilidad fiscal en persistencia

- La interfaz no es una frontera suficiente para proteger una factura emitida.
  El repositorio condiciona el guardado y la eliminación al estado Borrador en
  la propia sentencia SQL.
- Cabecera y líneas se sustituyen dentro de una sola transacción. Si el estado
  cambió desde que se abrió el editor, no queda ningún cambio parcial.
- `FacturaNoEditableException` comunica la factura y su estado persistido; para
  una validada propone explícitamente crear una rectificativa.
- PDF, correo, exportación contable, historial y metadatos VeriFactu son datos
  operativos posteriores y se actualizan mediante repositorios independientes.

## 2026-08-11 — Modelo de importes y validación de factura

- La base imponible de línea es el importe maestro. También se admite introducir
  el importe con impuestos; el modelo calcula entonces su base implícita.
- Importes y cuotas se redondean por línea a dos decimales, alejando de cero las
  mitades. Los totales de cabecera son siempre propiedades calculadas.
- El repositorio de edición recibe `Factura`, no un DTO con totales asignables,
  y persiste los valores derivados de sus líneas.
- Una factura a cero genera una advertencia confirmable, no un error. Cliente,
  descripción y al menos una línea sí son requisitos bloqueantes de emisión.
- El siguiente número de una serie no se reserva si su fecha es anterior a la
  fecha máxima ya numerada de esa serie.
- Después de emitir solo quedan editables notas internas y datos operativos o
  analíticos que no cambian el registro fiscal ni el documento entregado.

## 2026-08-12 — Ciclo de una factura manual

- El borrador se materializa desde el primer momento y recibe `IdFactura`, pero
  no `NumeroFactura`.
- La sociedad anual es el único dato exigido por la API. De ella se congelan
  NIF, nombre emisor y forma de pago predeterminada.
- `SeriesFacturacion.EsPredeterminada` identifica la serie normal inicial. Si
  solo existe una serie normal puede utilizarse sin marca; cualquier otra
  ambigüedad detiene la creación con una explicación de configuración.
- `Facturas.IdCliente` y `Facturas.FechaFactura` admiten `Null`. Un borrador sin
  cliente ni fecha fiscal se conserva así también físicamente, sin registros
  técnicos ni valores centinela. La fecha definitiva se asigna al validar.
- Cada guardado vuelve a leer y congelar el cliente incluso cuando su
  identificador no cambia.
- `IdSerie` puede cambiar solo a otra serie normal de la misma sociedad;
  `IdSociedadEjercicio` queda fijado al crear.
- Número, fecha definitiva y paso a `PendienteValidar` se confirman
  atómicamente antes del intento en VeriFactu. Un fallo conserva esos valores y
  deja la factura recuperable como pendiente.
- La misma regla se aplica a rectificativas: su creación asigna la serie y el
  vínculo con la original, pero no consume un número hasta que se validan.

## 2026-08-13 — Normalización de sociedades y ejercicios

- `Sociedades` representa la entidad jurídica permanente y utiliza
  `IdSociedad` de tres caracteres.
- `SociedadesEjercicios` representa la configuración anual y utiliza
  `IdSociedadEjercicio` numérico, además de `IdSociedad` como clave externa.
- Facturas, programaciones, series, conceptos y lotes dependen de
  `IdSociedadEjercicio`.
- Contratos, configuración de correo, plantillas, envíos de correo y
  configuración VeriFactu dependen directamente de `IdSociedad`.
- La antigua tabla vinculada `Sociedades` se materializa localmente durante la
  migración para no alterar la base de estructura de ProConta.

## 2026-08-13 — Ciclo editable de una factura programada

- `ServicioFacturasProgramadas` crea, carga, duplica y borra programaciones; el
  formulario no accede directamente al repositorio.
- `CrearFactura(IdSociedadEjercicio)` materializa inmediatamente una cabecera
  mensual, inactiva, sin cliente y sin líneas. `IdCliente` es `Null`, nunca cero.
- La cabecera se guarda con `FacturaProgramada.Guardar`; sus líneas se crean y
  borran desde el agregado y se guardan con `LineaFacturaProgramada.Guardar`.
- `Revisar()` devuelve una sola incidencia cada vez. Al asignar `Activa=True`,
  el propio objeto exige `Ok` antes de cambiar el atributo.
- Las comprobaciones locales cubren fechas, periodicidad, día de emisión,
  retención y líneas. El repositorio comprueba además ejercicio, cliente y serie.
- El repositorio impide que una operación deje sin líneas activas una
  programación que sigue activa. El borrado de cabecera exige desactivarla antes.

## 2026-08-13 — Revisión previa de una factura

- `Factura.Revisar()` devuelve únicamente la primera incidencia que debe
  corregirse o confirmarse, siguiendo el mismo patrón de `FacturaProgramada`.
- La fecha fiscal no forma parte de la revisión del borrador. Se asigna al
  validar y Access comprueba su correlación con la serie al reservar el número.
- `Factura.ValidarAsync()` ejecuta primero `Revisar()`. Si encuentra un error no
  guarda, no numera y no inicia comunicaciones con VeriFactu.
- Una advertencia, como el importe cero, se devuelve para confirmación. Solo se
  continúa cuando la llamada de validación indica que ha sido aceptada.
- Se elimina la antigua API pública `ValidarParaEmision`; no existen dos caminos
  distintos para evaluar las mismas reglas.

## 2026-08-13 — Consultas para las pantallas de facturación

- La interfaz no recibe SQL ni materializa agregados `Factura`: utiliza
  `ServicioConsultaFacturacion`, que devuelve filas ligeras y desconectadas.
- El límite inicial es de 1.000 filas y el resultado indica si existen más para
  que la pantalla pueda pedir al usuario que afine los filtros.
- Los filtros son tipados, parametrizados, acumulables y mantienen el orden de
  aplicación. Alt+clic alterna un filtro de celda y Escape retira el último.
- Una selección de varios años o meses constituye un solo filtro compuesto: sus
  períodos se combinan con `OR` y con los demás filtros mediante `AND`.
- Cliente y concepto se filtran por identificador. El concepto se resuelve con
  `EXISTS` sobre las líneas, sin duplicar cabeceras.
- Cada fila incluye una clave `EstadoVisualFacturacion`. El color se aplica en
  memoria durante el formateo del grid, sin nuevas consultas por fila.
- `SociedadesEjercicios.Activa` identifica un ejercicio vigente por sociedad,
  no una sociedad global. El servicio devuelve todos los contextos activos; la
  sesión de interfaz conserva el seleccionado y puede cambiarlo sin modificar
  la base de datos.

## 2026-08-13 — Primera carcasa gráfica

- La interfaz utiliza Krypton Standard Toolkit 105 sobre WinForms y .NET 10.
  `KryptonRibbon`, `KryptonNavigator` y `KryptonDataGridView` proporcionan cinta,
  documentos con pestañas y listas sin introducir reglas de negocio visuales.
- `GestorApariencia` es el único punto que traduce un tema de ProConta a la
  paleta global de Krypton. Los controles heredan la paleta y no fijan estilos
  independientes salvo los colores semánticos de estados.
- El contexto de sesión es una combinación `Sociedad/Ejercicio`. Al iniciar se
  toma el primer contexto activo disponible; el selector ofrece también todos
  los ejercicios históricos y cambiarlo no altera `Activa` en Access.
- La pantalla de facturas ordena y filtra siempre mediante
  `ServicioConsultaFacturacion`. La grid no ejecuta SQL ni carga agregados de
  dominio completos.
- Un doble clic publica el identificador de factura y abre una pestaña propia.
  El contenido de cabecera y líneas se incorporará en el siguiente desarrollo.
- La lista admite hasta 5.000 facturas por consulta. La tecla `F` aplica un
  filtro tipado sobre cualquiera de las fechas de `Facturas`: primero se elige
  el campo y después uno o varios de los últimos nueve años y doce meses.
  `Ctrl+clic` alterna períodos, `Mayús+clic` selecciona rangos y `(todos)`
  elimina únicamente el filtro del campo de fecha elegido.
- La versión funcional se declara en el proyecto de la aplicación y aparece en
  el caption principal. Cada entrega compilada debe identificar expresamente
  esa versión para poder cotejarla con el ejecutable abierto.
- `ServicioBusquedaClientes` y sus diálogos son independientes de Facturas para
  poder reutilizarlos en contratos, programaciones y futuros mantenimientos. La
  búsqueda parcial parametrizada cubre código, teléfono, NIF, nombre y razón
  social. Un único resultado se resuelve directamente; cero resultados abre la
  lista completa y varios resultados abren solo las coincidencias. En la lista
  de facturas, `F3` sustituye únicamente el filtro de cliente activo.

## 2026-08-13 — Operaciones contextuales y documentos proforma

- La grid ofrece Ver, Imprimir, VeriFactu, Enviar, Duplicar, Borrar y
  Rectificar. Ver exige una sola factura; las demás operaciones admiten
  selección múltiple y Borrar solo se habilita cuando todas son borradores.
- Duplicar sustituye los filtros por las copias creadas. Rectificar muestra
  conjuntamente originales y rectificativas mediante un filtro tipado de
  identificadores, sin exponer SQL a la interfaz.
- Ver e Imprimir renderizan archivos temporales y nunca escriben
  `PDFFactura`. Las operaciones largas informan por factura en una ventana
  flotante que permanece disponible al terminar.
- Una factura sin aceptación VeriFactu utiliza `FacturaProforma.frx`: omite el
  número y el QR y declara expresamente que carece de validez fiscal. Una
  factura aceptada utiliza `Factura.frx` con sus datos fiscales y QR.
- El formulario depende de `IRenderizadorDocumentoFactura`, no de FastReport.
  La elección de plantilla y el motor de renderizado quedan fuera de la capa
  gráfica.

## 2026-08-13 — Alta guiada y correo revisable

- `Nueva factura` es la primera acción contextual. Materializa inmediatamente
  un borrador del contexto actual, filtra y selecciona su fila y abre su pestaña
  antes de solicitar cliente y descripción opcionales.
- Cancelar el buscador o la descripción no elimina el borrador. Si se elige
  cliente, `GuardarBorrador` vuelve a leer su ficha y congela los datos fiscales.
- `ServicioFacturas.PrepararCorreosAsync` concentra validación, PDF y plantilla.
  El formulario no interpreta estados fiscales: solo presenta un resultado por
  factura.
- Antes de SMTP se abre un editor modal con Para, CC, CCO, asunto, cuerpo y el
  PDF adjunto de solo lectura. Confirmar conserva los cambios en la auditoría;
  cancelar omite solo ese mensaje y mantiene el PDF preparado.
- Usuario, contraseña y configuración SMTP nunca se exponen en el formulario.
- Rectificar exige una confirmación previa que explica la inversión de líneas,
  el borrador resultante y el estado transitorio de la factura original.

## 2026-08-16 — MailKit y recepción eliminable de facturas de proveedor

- El prototipo CDO ya había sido sustituido por `System.Net.Mail.SmtpClient`,
  pero este último tampoco será el transporte definitivo.
- SMTP se centraliza en un adaptador MailKit/MimeKit compartido por facturas,
  avisos de incremento e incidencias.
- Cada mensaje recibe antes del envío un `Message-ID` MIME que se conserva en
  `EnviosCorreo`, además de cabeceras `X-ProConta-*` para correlación.
- La recepción futura utilizará IMAP y sondeo periódico. POP3 e IMAP IDLE no
  forman parte de la primera versión.
- Una recepción se identifica mediante cuenta, carpeta, UIDVALIDITY y UID; el
  `Message-ID` no se considera suficiente para garantizar idempotencia.
- El mensaje original se conserva localmente como `.eml` antes de eliminarlo
  del servidor.
- El borrado IMAP solo se ejecutará después de guardar el PDF, registrar la
  factura, confirmar el asiento y entregar el correo de confirmación.
- Un proceso interrumpido reanuda el estado pendiente sin duplicar factura ni
  asiento. La definición completa está en `docs/RECEPCION_CORREO.md`.
