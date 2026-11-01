---
title: Pantallas de terceros
status: draft
depends_on: [terceros/tercero, terceros/roles, terceros/contactos, terceros/perfiles-notas, plataforma/contenedor, contabilidad/mayor]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-11-01 09:55 · Desarrollar la pantalla de Terceros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GVR2rh2hJF6h9BMYjKSu8jn
---
## Summary
Buscador multicriterio y ficha de terceros en pestañas del contenedor (Roles, Contactos, Notas, Historial, Ventas, Compras, Mayor, Documentos), que alojan los componentes de sus áreas dueñas.
**Aceptación:** la de spec § 7; guardar un tercero no modifica una factura emitida.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 10:45 · La pestaña Contabilidad de la ficha muestra el Mayor «vitaminado» de contabilidad/mayor embebido, filtrado por las cuentas del tercero; el mayor simple del prototipo actual se sustituye cuando se valide el del Mayor.
- 2026-10-29 10:45 · Los cambios sin guardar de la ficha se indican solo en la barra de acciones del pie, junto a Guardar, y con el punto de la pestaña; la cabecera no repite el rótulo.
- 2026-10-31 09:40 · La pestaña Contabilidad de la ficha muestra ya el Mayor del prototipo (lienzo versión 28), limitado a las cuentas del tercero (todas juntas o una a una); lo que sale de ellas (contrapartida, búsqueda en todas las cuentas, «Abrir en el Mayor») abre una pestaña del Mayor. Corregido en el lienzo un fallo que mostraba la pantalla de facturas encima de la ficha.
- 2026-11-01 09:55 · Las pestañas Ventas, Compras, Contabilidad y Documentos se muestran como huecos explícitos que dicen qué mostrarán y con qué desarrollo llegan, hasta que sus áreas (facturación, compras, contabilidad/mayor, documentos) las rellenen; no se leen sus tablas desde Terceros (RF-TER-15).
- 2026-11-01 09:55 · Roles: se construye con los campos que ya tiene la BD más la cuenta de acreedor del proveedor; los valores por defecto del proveedor (forma de pago, días, retención, tipo de operación, IBAN, cuenta de gasto) llegan con el corte de compras, los datos comerciales del cliente (días de pago, descuento, riesgo, IBAN) con el de facturación y el idioma y canal del contacto con el de correo. Hasta entonces esos campos no aparecen en la ficha.
- 2026-11-01 09:55 · Las Notas internas no tienen de momento capa de seguridad: las ven y editan todos los operadores (sustituye, por ahora, la capacidad específica de RF-TER-14).
- 2026-11-01 09:55 · El botón «Enviar email» junto a cada email queda desactivado, con un tooltip que lo explica, hasta que exista el compositor de correo.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-05, 09, 11, 15: comportamiento del domicilio, pastilla de destinatarios, búsqueda modal, pestaña Ventas
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 3 RF-ARQ-03: la pestaña aloja componentes del propietario
- 2026-10-28 12:28 · From sources/maquetas/Contenedor/ (ficha vigente) y ProConta/Claude outputs/TercerosWPF (prototipo WPF en el disco del propietario)
- 2026-10-28 12:28 · draft: Construir el buscador y la ficha de terceros según la maqueta.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final del buscador y la ficha de Terceros (lienzo del contenedor) antes de construir su UI WPF.
- 2026-10-29 10:39 · La pestaña de contabilidad será la misma 'vitaminada' que estamos diseñando
- 2026-10-29 10:39 · El label '1 cambio sin guardar' en la parte superior derecha quizá es redundante, ya lo tenemos abajo al lado del botón validar
- 2026-11-01 09:36 · Ya tenemos desarrollado el 'framework' del proyecto. Empezamos los desarrollos específicos. El primero será Terceros ya que es bastante autónomo (excepto pestañas como Contabilidad) y alimenta datos para otros núcleos funcionales.
- 2026-11-01 09:36 · Primero revisa si he de resolver alguna pregunta y, en caso contrario, inicia el desarrollo de toda la pantalla.
- 2026-11-01 09:48 · c)   De momento no montamos una capa de seguridad en las notas.
