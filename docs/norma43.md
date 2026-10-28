---
title: Norma 43
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Importación y consulta de extractos bancarios Norma 43: lectura, validación, persistencia idempotente por contenido, consulta y reversión auditada. No es la tesorería completa.
**Aceptación:** reimportar el mismo archivo no duplica; revertir algo contabilizado se bloquea.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 19 RF-N43-01, 02, 04: motor, reversión, selección y límites de consulta
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Norma43/docs/: motor anterior
- 2026-10-28 12:39 · A7) b (de momento no trabajaremos tesorería)
- 2026-10-28 12:28 · draft: Implementar la importación Norma 43 con idempotencia y reversión.
