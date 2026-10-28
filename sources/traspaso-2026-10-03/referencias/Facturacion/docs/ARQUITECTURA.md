# Arquitectura

## Objetivo

La aplicacion debe ofrecer una interfaz de gestion rica sin trasladar las reglas
fiscales a formularios ni acoplar el dominio a Microsoft Access o VeriFactu.

## Vista general

```text
WinForms
   |
   v
Servicios de aplicacion
   |
   v
Dominio de facturacion
   |
   +--------------------+-------------------+
   |                    |                   |
   v                    v                   v
Persistencia Access   VeriFactu        Correo / PDF /
                                      Contabilidad
```

La direccion de las dependencias debe apuntar hacia el dominio. El dominio no
debe conocer formularios, controles, OLE DB ni clases concretas de VeriFactu.

## Capas previstas

### Interfaz WinForms

Responsabilidades:

- Formularios, cuadros de dialogo y navegacion.
- Grids, filtros, seleccion y menus contextuales.
- Validacion inmediata de la entrada del usuario.
- Presentacion de errores y progreso.
- Invocacion de casos de uso.

No debe calcular impuestos, asignar numeros fiscales ni ejecutar directamente
SQL de actualizacion.

### Aplicacion

Coordina casos de uso completos, por ejemplo:

- Crear y guardar un borrador.
- Validar y emitir una factura.
- Generar facturas programadas.
- Enviar una factura a VeriFactu.
- Generar el PDF y enviarlo por correo.
- Exportar un lote a contabilidad.

Esta capa controla transacciones y combina dominio, repositorios e
integraciones.

### Dominio

Contiene las entidades y reglas independientes de infraestructura:

- `Factura` y `LineaFactura`.
- `FacturaProgramada` y sus lineas.
- Contratos, series y conceptos cuando participen en reglas de negocio.
- Estados, validaciones, calculos y rectificaciones.

Una factura emitida es inmutable desde el punto de vista fiscal. Los errores de
comunicacion con la AEAT no revierten su emision.

`Factura` agrega sus `LineaFactura`: las líneas calculan cuotas desde la base
imponible y la cabecera publica únicamente totales derivados. La interfaz puede
asignar una base o un importe con impuestos, pero nunca una cuota o un total de
factura. `ValidarParaEmision` reúne errores y advertencias antes de que la capa
de aplicación reserve número.

El guardado de contenido fiscal pasa exclusivamente por
`IRepositorioEdicionFactura.GuardarBorrador`. La condición
`EstadoFactura = Borrador` forma parte del propio `UPDATE` y la sustitución de
líneas comparte su transacción. Las rutas operativas de PDF, correo,
contabilidad, historial y respuestas VeriFactu permanecen separadas para poder
actualizar esos datos sin reabrir el contenido fiscal.

### Persistencia Access

Responsabilidades:

- Conexiones ACE/OLE DB.
- Repositorios y materializacion de entidades.
- Consultas SQL parametrizadas.
- Transacciones cortas.
- Control de concurrencia y numeracion.
- Versionado y actualización idempotente del esquema antes de abrir la interfaz.

No se expondran conexiones, comandos, lectores ni filas de datos fuera de esta
capa.

### Integraciones

Cada integracion externa se ocultara detras de una interfaz propia:

- VeriFactu.
- Correo SMTP.
- Generacion de PDF.
- Exportacion contable.

El adaptador de VeriFactu traducira entre el modelo de ProConta y las clases de
la biblioteca externa. Ninguna otra capa dependera directamente de esas clases.
El mismo adaptador decide si corresponde un alta ordinaria o un reenvío técnico
del XML original y reconoce como éxito los duplicados cuyo registro previo sigue
aceptado en la AEAT.

## Solucion actual

```text
ProConta.Facturacion.sln
|-- ProConta.Facturacion.App
|-- ProConta.Facturacion.Aplicacion
|-- ProConta.Facturacion.Dominio
|-- ProConta.Facturacion.Persistencia.Access
|-- ProConta.Facturacion.Integraciones
`-- ProConta.Facturacion.Tests
```

`Aplicacion` contiene la orquestacion de casos de uso y sus contratos de salida.
Las implementaciones de esos contratos pertenecen a Access o Integraciones. De
este modo, el flujo completo puede probarse sin abrir una base de datos ni
realizar envios fiscales, generar archivos o mandar correos reales.

`ServicioProcesamientoFacturas` ofrece las operaciones masivas sobre facturas
ya existentes: validación, regeneración del PDF y envío por correo. Cada nivel
reutiliza el anterior cuando falta un requisito: el PDF valida primero una
factura pendiente y el correo genera primero un documento inexistente. Los
errores se aíslan por factura y la cancelación explícita sí detiene la colección.

`ServicioCreacionFacturasRectificativas` crea una rectificativa en borrador por
cada factura validada seleccionada. La persistencia Access reserva la numeración,
copia la cabecera, invierte cantidades e importes de las líneas y enlaza ambos
documentos dentro de una transacción independiente por factura. En esa misma
transacción la original queda en `RectificacionPendiente`; el servicio de
eliminación restaura `Validada` si se descarta el borrador.

`ServicioSolicitudEdicionFactura` es la puerta de entrada al editor. Si la
factura está pendiente de respuesta consulta los registros propios de la AEAT y
devuelve una decisión con título, explicación y acción sugerida para la interfaz.
La indisponibilidad fiscal bloquea la edición sin convertir una incertidumbre en
un borrador modificable.

## Acceso a datos

- Se conserva la sintaxis SQL de Access.
- Los parametros OLE DB se añaden en el mismo orden que sus marcadores `?`.
- Las consultas complejas que resulte util inspeccionar desde Access pueden
  mantenerse como consultas guardadas.
- Las operaciones simples pueden residir como SQL parametrizado en los
  repositorios.
- Las conexiones no se mantienen abiertas durante la interaccion del usuario.

## Convivencia con ProConta actual

La nueva aplicacion podra ejecutarse de forma independiente o iniciarse desde
Access con argumentos como sociedad, ejercicio, usuario e identificador de
factura. Ambos sistemas conviviran durante la transicion sobre las bases de datos
existentes.

## Actualización del esquema

`ActualizadorEsquemaAccess` compara el catálogo compilado con
`VersionesEsquema`, impide abrir una base creada por una versión futura y
verifica que no haya cambiado la huella de una migración ya aplicada. Cada
intento queda auditado en `LogMigracionesEsquema` y dispone de una copia previa.
Las migraciones son clases de infraestructura porque Access puede requerir más
que SQL: comprobaciones condicionales, relaciones, propiedades y datos de
catálogo.

## Ciclo manual de factura

`ServicioFacturas` es la fachada única que utilizarán los formularios para el
ciclo manual. Publica `CrearBorrador`, `CargarFactura`, `GuardarBorrador`,
`ValidarAsync`, `EliminarBorrador` y `CrearRectificativa`. La interfaz no debe
conocer ni recibir repositorios, el gestor interno `Facturas` ni los servicios
especializados que coordinan VeriFactu.

Guardar un borrador recalcula y persiste sus importes, pero permite que siga
incompleto mientras el usuario trabaja: todavía puede no tener cliente o
líneas. La validación fiscal completa se ejecuta al llamar a `ValidarAsync`.
Solo esta operación conserva el sufijo `Async`, porque puede realizar una
comunicación real con VeriFactu.

La validación guarda primero el contenido y, dentro de una transacción corta,
vuelve a comprobar fecha y serie, incrementa el contador, asigna
`NumeroFactura` y `FechaFactura` y cambia a `PendienteValidar`. Solo entonces se
realiza el intento en VeriFactu.
