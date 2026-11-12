---
title: Comandos de informe por correo
status: draft
depends_on: [correo/recepcion, informes/reporting, informes/excel]
threads: []
---
## Summary
Pedir por correo un informe del catálogo (por ejemplo, el mayor de una cuenta) y recibirlo como adjunto en la respuesta. La recepción ya guarda estos mensajes «en espera» (correo/recepcion 2026-11-11 11:00); falta interpretar el comando y generar el informe sin pantalla con el catálogo de informes/reporting.
**Aceptación:** un remitente autorizado para pedir informes envía un comando con un mayor y recibe la respuesta con el Excel o el PDF adjunto, igual que el generado desde el menú Informes con los mismos parámetros; un comando que no se entiende se responde diciendo por qué y cómo escribirlo; repetir el sondeo no duplica la respuesta.

## Decisions
- 2026-11-12 15:19 · Creado como borrador desde informes (hilo «Diseñar el módulo de informes»), con el OK del propietario. Los informes pedidos por correo salen del catálogo (proveedor de filas y salidas Excel o PDF), no de la impresión de pantalla con FlowDocument, que necesita la ventana y el diálogo de impresión.

## Requirements
- 2026-11-12 13:11 · Ok con FlowDocument, lo único que no tengo claro si nos sirve para pedir un mayor mediante un comando de email
- 2026-11-12 15:19 · draft: Pedir por correo informes del catálogo (por ejemplo, un mayor) y responder al remitente con el informe adjunto en Excel o PDF, generado sin pantalla.
