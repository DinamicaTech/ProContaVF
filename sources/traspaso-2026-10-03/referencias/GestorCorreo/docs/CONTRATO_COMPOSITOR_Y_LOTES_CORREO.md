# Contrato de preparación del compositor y seguimiento de lotes de correo

## Estado y alcance

Este documento publica el resultado de la revisión funcional y técnica de las
maquetas del compositor individual y del seguimiento de lotes. Es un contrato
de preparación para acordar capacidades, responsabilidades y evidencias antes
de implementar las pantallas o modificar el esquema.

No autoriza reglas nuevas de negocio, migraciones de base de datos ni envíos
reales. Las decisiones que las notas o las maquetas presentan como propuestas
continúan pendientes hasta que el usuario las confirme y el módulo propietario
publique el contrato correspondiente.

Fuentes revisadas:

- compositor individual: `https://www.dinamica.tech/Correo/new_proconta-correo`;
- seguimiento de lotes: `https://www.dinamica.tech/CorreoLotes/new_seguimiento-lotes-correo`;
- notas del compositor: `C:\docs\VeriFactu\Claude outputs\Correo\proconta-correo.md`;
- notas de lotes: `C:\docs\VeriFactu\Claude outputs\CorreoLotes\seguimiento-lotes-correo.md`;
- código vigente de Gestor Correo, Facturación y Mail Dispatcher;
- acuerdos de correo
  `C:\Docs\VeriFactu\ProConta.Facturacion\docs\ACUERDOS_CORREO_20261001.md`;
- contrato de Gestión documental
  `C:\Docs\VeriFactu\ProConta.Facturacion\docs\contratos\DOC-GESTOR.md`;
- mapa central
  `C:\Docs\VeriFactu\ProConta.Facturacion\docs\MAPA_PROYECTO.md` y registro de
  hilos de Facturación.

Las maquetas son una referencia visual y de interacción. Los datos, reglas y
resultados simulados que contienen no acreditan capacidades del motor.

## Contrato visual e interacción

### Compositor individual

El compositor se abre modal respecto de la ventana que lo origina, conserva su
propietario, puede moverse y redimensionarse y no utiliza `TopMost`. Debe tener
un diseño compacto cuando disminuya el ancho, sin ocultar destinatarios,
adjuntos, validaciones ni el estado de envío.

La cabecera identifica la sociedad, el ejercicio, el documento de origen y la
cuenta remitente. Este contexto se fija al preparar el mensaje: un cambio
posterior de sociedad activa en la aplicación no puede alterar el borrador ni
el envío. Las cuentas elegibles se limitan a las admitidas por el contexto de
origen y por el contrato de configuración.

El cuerpo funcional comprende:

- remitente y, si se admite elección, las cuentas disponibles en el contexto;
- destinatarios Para, CC y CCO como direcciones independientes, con validación
  localizada y CC/CCO desplegables;
- asunto y editor de cuerpo;
- plantilla y formato cuando el módulo originador los aporte;
- previsualización cuando exista un render contractual;
- lista de adjuntos con identidad, tamaño, disponibilidad y procedencia;
- acciones cuyo texto describa el resultado real, no un «Sí» o «No» ambiguo;
- ayuda accesible y detallada para cada control visual.

La versión final debe construirse de forma declarativa en XAML, con estilos
mantenibles y los recursos compartidos que publique UI-BASE. No se copiarán
reglas de demostración ni se reconstruirá la maqueta con controles estándar sin
estilo. Esta revisión no autoriza cambios en el shell ni en UI-BASE.

El estado visible debe diferenciar al menos preparación, transmisión en curso,
aceptación acreditada por el servicio, rechazo acreditado e incertidumbre. En
estado incierto se bloquean la edición y un reenvío ciego; se conserva el
`Message-ID` y se ofrecen únicamente comprobaciones o resoluciones que el motor
pueda justificar.

### Registro consultable y seguimiento de procesos

Existe un único componente de consulta para el registro de correos salientes y
entrantes, accesible mediante una opción de menú. Reutiliza los registros y
servicios de los motores propietarios; no mantiene un historial paralelo por
módulo.

Los correos entrantes y salientes comparten navegación y presentación de la
consulta, pero conservan estados y acciones propios. La reutilización visual no
habilita acciones de reintento o reenvío sobre mensajes entrantes.

Un proceso automático puede mostrar al finalizar una ventana de resumen y
seguimiento modal respecto de la ventana de origen. Esa ventana es independiente
de las pestañas o ventanas child del contenedor, reutiliza el mismo componente
de consulta y aplica un filtro por el proceso o lote concreto.

Cualquier proceso que implique más de un email omite el compositor individual,
incluso cuando los mensajes correspondan a facturas distintas. Prepara las
instantáneas mediante los contratos del proceso, las entrega a la coordinación
saliente y abre o enlaza el seguimiento; los errores se registran y gestionan
sin abrir un editor por destinatario.

La vista se centra en una tabla filtrable con resumen, progreso y selección
explícita. Toda acción indica si afecta a un mensaje, a la selección visible o
al lote completo. El detalle es de solo lectura y conserva:

- sociedad, ejercicio, remitente y cuenta usados;
- destinatarios Para, CC y CCO;
- asunto y contenido históricos, no regenerados con la plantilla actual;
- adjuntos históricos y su identidad;
- referencia al origen;
- `Message-ID` y demás identificadores disponibles;
- eventos, intentos y evidencias de proveedor en líneas separadas.

La aceptación de transporte y la entrega al destinatario son dimensiones
distintas. Un mensaje aceptado puede seguir sin aviso de entrega; entregado o
rebotado solo se muestra cuando una fuente contractual proporciona la evidencia.
La finalización del proceso originador, el encolado, la aceptación del correo y
la entrega son hitos independientes. Cerrar la ventana de seguimiento no
cancela por sí mismo los envíos.

Pausa, persistencia durable de borradores, resolución manual y avisos de entrega
aparecen en las maquetas, pero no forman parte de la capacidad actual. El
compositor WPF sí puede devolver un borrador al invocador y reabrir esa
instantánea; no afirma guardarla entre sesiones. Las demás capacidades solo se
habilitarán tras aprobar sus reglas, persistencia, permisos y fuentes de
evidencia.

## Mapa de capacidades

| Capacidad | Situación actual | Brecha o condición |
| --- | --- | --- |
| Cuentas por sociedad/ejercicio, credenciales protegidas, TLS y SMTP | Existente en Gestor Correo | Mantener esta propiedad; no duplicarla en las vistas ni en Dispatcher. |
| Mensaje neutral con Para, CC, CCO, texto/HTML, cabeceras y varios adjuntos | Existente en el contrato de Gestor Correo | Facturación interactiva solo materializa hoy un PDF. |
| `Message-ID` generado antes de llamar a MailKit | Parcial | La identidad se pierde del resultado cuando la llamada termina con excepción. |
| Resultado SMTP | Parcial | Solo devuelve `Message-ID` y respuesta, o lanza excepción; no expresa certeza, fase ni resultado por destinatario. |
| Historial saliente de facturas | Parcial | `EnviosCorreo` guarda intento y contenido básico, pero no lote, eventos, evidencias, resolución ni varios adjuntos históricos. |
| Compositor de factura | Parcial | Existe un formulario WinForms/Krypton básico; carece del contrato visual y de estados descrito aquí. |
| Registro consultable de correos entrantes y salientes | Parcial | Existen registros separados, pero no el componente común accesible desde menú ni el filtro reutilizable por proceso/lote. |
| Fijación inmutable del contexto de origen | Parcial | Se transportan identificadores de sociedad/ejercicio, pero no existe una instantánea completa versionada. |
| Cola saliente durable y coordinación por lotes | No existente | Mail Dispatcher actual coordina correo entrante; debe ampliarse con un contrato saliente reutilizable, no clonarse en Gestor Correo. |
| Seguimiento de lote, filtros, detalle e historial de eventos | No existente | Requiere primero modelo, persistencia y consultas del coordinador saliente. |
| Resultado parcial por destinatario | No existente | El resultado binario actual no permite representarlo con seguridad. |
| Estado incierto basado en fase y evidencia | No existente | Hoy cualquier excepción se transforma en fallo aparente. |
| Reintento distinto de reenvío | No existente | Requiere identidad lógica, intentos y vínculo entre mensajes. |
| Cancelación o pausa coordinada | No existente | Cancelar solo puede afectar trabajo que todavía no haya iniciado la transmisión; pausa queda pendiente de contrato. |
| Borradores | Parcial | El compositor WPF devuelve y reabre una instantánea; no existe repositorio durable, caducidad ni recuperación entre sesiones. |
| Confirmaciones de entrega o rebote | No existente | Requieren DSN, webhook o consulta de proveedor con garantías documentadas. |
| Resolución manual de incertidumbre | No existente | Pendientes permisos, auditoría, valores y efecto sobre acciones posteriores. |

El flujo masivo vigente de Facturación abre secuencialmente
`FrmEditarCorreoFactura` para cada factura seleccionada y envía inmediatamente
después de cerrar cada modal. El procesamiento programado también llama al
emisor de forma directa y reduce el resultado a `CorreoEnviado As Boolean`.
Estos flujos no satisfacen el contrato de lote y deberán consumir la futura
coordinación saliente, sin crear una segunda cola local.

## Contrato mínimo del motor saliente

### Identidad e instantánea

Antes de cualquier acceso de red se crea una identidad lógica estable del
mensaje y su `Message-ID`. La instantánea conserva contexto de origen,
remitente, destinatarios, contenido, formato, adjuntos y enlaces funcionales.
Los detalles históricos se leen de esa instantánea; no se recalculan a partir
del registro o plantilla actuales.

`DOC-GESTOR` (`01a0f6ee-afa9-7101-b599-97fa421a3359`) es el propietario de la
identidad, versiones, almacenamiento e integridad documental. Para nuevas
incorporaciones, `Documentos.Fichero` contiene una ruta relativa a una raíz
configurable; el contrato no incluye migración documental legada.

El correo debe referenciar una versión documental concreta y acordar con
DOC-GESTOR cómo conservar o recuperar el contenido exacto de cada adjunto
enviado. Una ruta o referencia mutable no basta para reconstruir el histórico.
La estrategia física, retención y disponibilidad posterior siguen pendientes
de coordinación; Gestor Correo no adquiere la propiedad del archivo original.

### Evidencia de transporte

El resultado de un intento no puede ser un booleano. Debe conservar, cuando
esté disponible:

- identidad del mensaje y del intento;
- fase alcanzada del protocolo;
- instantes de inicio y fin;
- respuesta saneada del servidor;
- evidencia por destinatario;
- clasificación de certeza y causa funcional;
- detalle técnico únicamente en el log protegido.

Los estados agregados propuestos para revisar con el propietario del motor son
`Pendiente`, `EnProceso`, `Aceptado`, `AceptadoParcial`, `FalloConfirmado`,
`Incierto` y `CanceladoAntesDeEnvio`. Son estados de transporte, no de entrega
final. `FalloConfirmado` solo puede representar que no consta ningún
destinatario aceptado y que existe evidencia contractual de no aceptación.

Cada destinatario necesita un resultado independiente: aceptado, rechazado o
sin evidencia. El rechazo de uno no demuestra el rechazo de los demás; por
tanto, una aceptación parcial nunca se reducirá a un fallo total.

Una excepción anterior a cualquier transmisión puede acreditar que no hubo
envío. Una desconexión después de comenzar la entrega del contenido o sin
respuesta concluyente se clasifica como incierta. La ausencia de un mensaje en
Enviados o en una búsqueda del proveedor solo significa «no encontrado»; no
demuestra que no fue aceptado salvo que el contrato de ese proveedor garantice
expresamente esa conclusión.

### Evidencia de entrega

Entrega y transporte usan estados separados. Sin una fuente contractual, el
estado de entrega es `SinAviso`. `Entregado` y `Rebotado` solo se registran con
una notificación o consulta fiable, incluyendo fuente, instante, destinatario y
datos saneados suficientes para auditar la decisión.

### Reintento y reenvío

Un reintento añade un intento al mismo mensaje lógico y solo se ofrece cuando
existe evidencia de fallo confirmado. La política de conservar o renovar el
`Message-ID` en un reintento queda pendiente de decisión explícita y pruebas con
los proveedores admitidos.

Un reenvío crea un mensaje lógico nuevo, enlazado con el anterior, a partir de
la instantánea histórica. Debe advertir del riesgo de duplicado cuando el
mensaje anterior fue aceptado o sigue incierto. No se puede presentar un
reenvío como si fuera otro intento técnico.

Las operaciones sobre selección o lote deben ser atómicas e idempotentes en el
coordinador. La interfaz solicita la acción y vuelve a consultar su resultado;
no modifica estados localmente ni ejecuta bucles SMTP.

## Reparto de responsabilidades y dependencias

### INF-COR — Gestor Correo

- Mantiene cuentas, asignaciones, credenciales, presets, TLS y clientes SMTP.
- Construye el MIME neutral y conserva el `Message-ID` conocido antes de red.
- Publica evidencia de transporte y por destinatario que el adaptador pueda
  demostrar, incluida la fase ambigua; no inventa certeza.
- Traduce capacidades de proveedor sin imponer reglas de lote o de documento.
- Posee la interfaz y las consultas del registro común de correo, reutilizando
  los servicios de procesamiento sin mantener otro historial.
- Implementará las vistas solo cuando existan sus contratos de aplicación y los
  recursos visuales compartidos aprobados.

### INF-DIS — Mail Dispatcher

- Debe publicar una ampliación saliente reutilizable para cola durable,
  idempotencia, adquisición de trabajo, intentos, eventos y coordinación.
- Coordina cancelación previa a transmisión, reintento y reenvío según los
  contratos aprobados.
- No duplica cuentas, credenciales ni conexiones SMTP; consume INF-COR.
- Su implementación actual es de correo entrante y no puede darse por válida
  para estas pantallas sin esa ampliación.

### Módulos originadores

- Preparan destinatarios, plantilla, contenido, adjuntos y referencia de origen.
- Fijan sociedad, ejercicio y reglas funcionales en el momento de preparación.
- Consumen la cola saliente para operaciones masivas y no abren compositores en
  cascada.
- FAC-LIS consume la preparación, coordinación y consulta publicadas por
  INF-COR/INF-DIS; no duplica motor, historial ni seguimiento.

### Dependencias transversales

- UI-BASE: estilos, controles y patrones declarativos compartidos; esta tarea no
  modifica el shell ni sus recursos.
- DOC-GESTOR: identidad, versiones, almacenamiento, integridad y recuperación
  autorizada de adjuntos. Su contrato inicial está publicado; la conservación
  de la versión exacta enviada debe cerrarse entre ambos contratos.
- ActualizadorEsquema: materializa las migraciones reproducibles una vez
  aceptado el modelo; esta revisión no modifica Access.

## Secuencia de desarrollo preparada

1. Aprobar estados, fuentes de evidencia, identidad lógica y conservación del
   contenido exacto de adjuntos con DOC-GESTOR.
2. Extender primero los contratos de resultado de INF-COR y añadir pruebas de
   aceptación parcial, fallo cierto e incertidumbre, sin envío real.
3. Publicar en INF-DIS la coordinación saliente reutilizable y sus pruebas de
   idempotencia, adquisición, cancelación, reintento y reenvío.
4. Diseñar y migrar la persistencia con copia de seguridad y acceso exclusivo,
   sin reutilizar tablas de correo entrante con semántica distinta.
5. Adaptar los módulos originadores, empezando por sustituir el bucle de
   compositores del envío masivo de facturas, sin duplicar el motor en FAC-LIS.
6. Implementar el compositor individual declarativo y el componente común de
   registro/seguimiento, disponible desde menú y como resumen modal filtrado,
   apoyados en contratos reales y no en estados simulados.
7. Compilar la solución, ejecutar pruebas relacionadas y revisar el diff antes
   de preparar cualquier ejecutable manual versionado.

## Decisiones todavía abiertas

- Política del `Message-ID` en reintentos confirmados.
- Fuente y garantías para comprobar aceptación, entrega y rebote por proveedor.
- Política automática de reintentos, esperas y límites.
- Destinatarios de un reenvío y edición permitida de la instantánea.
- Alcance, permisos y auditoría de la resolución manual.
- Semántica, persistencia y recuperación de borradores.
- Semántica de pausa y comportamiento ante trabajo ya adquirido.
- Límites de adjuntos y mecanismo para conservar la versión exacta con
  DOC-GESTOR.
- Capacidad real del proveedor para informar aceptación o rechazo por
  destinatario.

Hasta resolverlas, la interfaz puede representar que una capacidad no está
disponible, pero no simularla ni deducir estados más concluyentes que la
evidencia registrada.

## Incremento WPF invocable desde Terceros

La API neutral, el ejemplo de integración, la semántica exacta de cancelación y
borrador, las limitaciones y los renders están publicados en
[INTEGRACION_COMPOSITOR_WPF.md](INTEGRACION_COMPOSITOR_WPF.md). Este incremento
no conecta transporte ni autoriza a DAT-Terceros a copiar o implementar otro
compositor.
