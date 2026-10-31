---
title: Centros de coste
status: draft
depends_on: [contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Centros de coste jerárquicos por contexto y revisiones de cuenta a fecha de corte; saldos como cachés reconstruibles.
**Aceptación:** la caché reconstruida coincide con los apuntes.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-CUE-03: revisiones, cachés y centros de coste
- 2026-10-28 12:28 · draft: Implementar centros de coste y revisiones de cuenta.
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 0, 3.12: «Regenera saldos» recalcula los saldos por periodo (0 apertura, 1-12 meses, 13 regularización, 14 cierre) a partir de los apuntes; es la reconstrucción de la caché de saldos.
