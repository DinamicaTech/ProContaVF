---
title: Geografía
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Países, provincias, poblaciones y códigos postales cargados solo desde la BD actual, lo más simple posible, con la resolución automática CP → provincia → población que usan los domicilios.
**Aceptación:** un CP con una sola provincia la selecciona; con ambigüedad se elige; sin correspondencia no se inventa.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · El catálogo geográfico se carga solo con los datos de la BD actual (sin importar el INE); la dirección física es cada vez más irrelevante, así que se mantiene lo más simple posible.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-05: orden y autocompletado del domicilio; catálogo geográfico pendiente
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 29 P-02: propietario de la geografía
- 2026-10-28 12:28 · draft: Definir e implementar el catálogo geográfico y su búsqueda por código postal.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿de dónde sale el catálogo? (a) INE (provincias y municipios) más el callejero de códigos postales disponible en la BD actual ✅ (b) solo los datos de la BD actual
- 2026-10-31 16:54 · 27. b. La dirección física es cada vez más irrelevante, por lo que cuanto más lo simplifiquemos, mejor
- 2026-10-31 16:54 · answer: origen del catálogo → (b) solo los datos de la BD actual: la dirección física es cada vez más irrelevante, cuanto más simple mejor
