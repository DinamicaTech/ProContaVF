---
title: Pantallas de contabilidad
status: draft
depends_on: [contabilidad/asientos, contabilidad/mayor, plataforma/contenedor]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Editor y visor común de asientos (también para propuestas de procesos), consulta del mayor y de balances.
**Aceptación:** cancelar la vista previa deja la BD intacta.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 16 RF-GES-02: editor/visor común, modal y con pestañas
- 2026-10-28 12:28 · draft: Construir el editor de asientos y la consulta del mayor.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: la consulta del Mayor se construye en WPF solo cuando su prototipo HTML esté validado (lo prepara contabilidad/mayor).
