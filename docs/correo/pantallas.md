---
title: Pantallas de correo
status: draft
depends_on: [correo/envio, plataforma/contenedor, plataforma/ui-base]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-10 19:40 · Desarrollar envío y recepción de correo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GT4mx65rhF9fy8aRE77SaGH
---
## Summary
Compositor individual modal (Para, CC, CCO, asunto, cuerpo y adjuntos) y registro de correo enviado, desde el menú o como resumen modal filtrado por lote, con su detalle de solo lectura, los adjuntos tal como se enviaron y las acciones en el menú contextual. Los correos recibidos se añaden al registro con la recepción.
**Aceptación:** diez mensajes no abren diez modales; cancelar no envía.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-10 19:40 · Una sola factura (o un tercero) abre el compositor; un proceso de más de un correo (varias facturas, programadas) crea un lote sin abrir el compositor por mensaje y, al terminar, muestra su resumen: el registro de correo filtrado por ese lote, en ventana modal.
- 2026-11-10 19:40 · Registro de correo (menú Correo › Correo enviado): tabla filtrable de los mensajes con su estado, detalle de solo lectura con remitente, Para, CC y CCO, asunto, cuerpo, adjuntos (se abren tal como se enviaron), Message-ID, intentos y origen. Las acciones (reintentar, reenviar, dar por enviado o no enviado un incierto, cancelar pendientes, abrir adjunto, abrir origen) van en el menú contextual, según la regla «Menú contextual»; la maqueta de lotes las tenía en botones y se adapta.
- 2026-11-10 19:40 · Fuera de esta versión, con el OK del propietario: pausa de lotes, borradores guardados entre sesiones y avisos de entrega o rebote. El compositor solo conserva el borrador mientras está abierto.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 22 RF-COR-01..03: compositor y registro de lotes
- 2026-10-28 12:28 · From sources/maquetas/Correo/ y sources/maquetas/CorreoLotes/: maquetas
- 2026-10-28 12:28 · draft: Construir el compositor y el seguimiento de lotes según las maquetas.
- 2026-11-10 19:26 · hay que poder consultar los emails enviados y poder acceder a los adjuntos si los hubiesen
- 2026-11-10 19:26 · Ok al envio 'ciego' de lotes de facturas con un resumen al final de los envíos
- 2026-11-10 19:26 · En contextual
