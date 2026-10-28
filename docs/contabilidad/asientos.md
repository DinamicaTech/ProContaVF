---
title: Motor de asientos
status: draft
depends_on: [contabilidad/cuenta-contable, contabilidad/centros-coste]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Motor común que valida y guarda asientos como agregado atómico con cuadre, auditoría y protección de ejercicios cerrados; lo usan directamente ventas, compras, nóminas y la conciliación bancaria.
**Aceptación:** asiento desequilibrado no persiste; un automático rechaza edición manual.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-ASI-01..03: agregado atómico, cuadre, automáticos regenerables por su lote
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Asientos/ (código en el disco del propietario): motor anterior
- 2026-10-28 12:28 · draft: Implementar el motor de asientos y publicar su contrato para las áreas que generan asientos.
