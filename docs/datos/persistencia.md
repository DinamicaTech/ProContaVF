---
title: Capa de persistencia
status: draft
depends_on: [datos/esquema, nucleo]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Capa común sobre ACE/OLE DB x64: conexiones y transacciones cortas, consultas parametrizadas por posición, concurrencia optimista y unicidad en BD, traducción de fallos técnicos (tabla ausente, lectura fallida) a errores explícitos, nunca a «vacío» o «sin permiso». El dominio no conoce OLE DB.
**Aceptación:** pruebas sobre copia: transacción revertida no deja filas, conflicto de versión detectado, tabla ausente da error explícito.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Concurrencia optimista con versión de fila en las fichas editables y numeración dentro de transacción (confirma architecture 2026-10-28 12:45).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-01: capas separadas, consultas parametrizadas, conexiones cortas fuera de HTTP/SMTP/OCR
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-03: concurrencia optimista en fichas y unicidad en BD
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 4 RF-CFG-04: tabla ausente o fallo de lectura no equivale a catálogo vacío
- 2026-10-28 12:28 · draft: Crear la capa de persistencia común sobre ACE/OLE DB con pruebas de integración sobre una copia de la maestra.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿política de concurrencia? (a) optimista con versión de fila en las fichas editables y transacción en numeración ✅ (b) bloqueo pesimista al abrir ficha
- 2026-10-31 16:54 · 10. a
- 2026-10-31 16:54 · answer: política de concurrencia → (a) optimista con versión de fila en las fichas editables y transacción en numeración
