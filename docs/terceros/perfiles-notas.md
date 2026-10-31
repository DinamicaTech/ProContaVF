---
title: Perfiles, notas e historial
status: draft
depends_on: [terceros/tercero, seguridad/permisos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Perfiles de clasificación N:M independientes de roles, notas y notas internas, e historial de cambios por operador.
**Aceptación:** repetir el estado actual no genera movimientos duplicados.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Los perfiles se guardan con el Guardar/Descartar de la ficha, como el resto de la ficha (resuelve RF-TER-13).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7.2 RF-TER-12..14: perfiles, notas, historial
- 2026-10-28 12:28 · draft: Implementar perfiles, notas e historial del tercero.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-TER-13 ¿los perfiles se guardan al instante o con el Guardar de la ficha? (a) con el Guardar/Descartar de la ficha, para unificar ✅ (b) al instante, como el prototipo
- 2026-10-31 16:54 · 32. a
- 2026-10-31 16:54 · answer: RF-TER-13 guardado de perfiles → (a) con el Guardar/Descartar de la ficha
