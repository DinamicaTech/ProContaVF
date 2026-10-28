---
title: Perfiles, notas e historial
status: draft
depends_on: [terceros/tercero, seguridad/permisos]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Perfiles de clasificación N:M independientes de roles, notas y notas internas, e historial de cambios por operador.
**Aceptación:** repetir el estado actual no genera movimientos duplicados.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7.2 RF-TER-12..14: perfiles, notas, historial
- 2026-10-28 12:28 · draft: Implementar perfiles, notas e historial del tercero.
- 2026-10-28 12:28 · question: RF-TER-13 ¿los perfiles se guardan al instante o con el Guardar de la ficha? (a) con el Guardar/Descartar de la ficha, para unificar ✅ (b) al instante, como el prototipo
