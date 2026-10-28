---
title: Geografía
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Países, provincias, poblaciones y códigos postales, con la resolución automática CP → provincia → población que usan los domicilios.
**Aceptación:** un CP con una sola provincia la selecciona; con ambigüedad se elige; sin correspondencia no se inventa.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-05: orden y autocompletado del domicilio; catálogo geográfico pendiente
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 29 P-02: propietario de la geografía
- 2026-10-28 12:28 · draft: Definir e implementar el catálogo geográfico y su búsqueda por código postal.
- 2026-10-28 12:28 · question: ¿de dónde sale el catálogo? (a) INE (provincias y municipios) más el callejero de códigos postales disponible en la BD actual ✅ (b) solo los datos de la BD actual
