---
title: Motor de asientos
status: draft
depends_on: [contabilidad/cuenta-contable, contabilidad/centros-coste]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Motor común que valida y guarda asientos como agregado atómico con cuadre, auditoría y protección de ejercicios cerrados; centraliza toda modificación de apuntes con un registro de cambios agrupado por IdLote; lo usan directamente ventas, compras, nóminas y la conciliación bancaria.
**Aceptación:** asiento desequilibrado no persiste; un automático rechaza edición manual.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 10:20 · Toda modificación de apuntes (desde el Mayor, al modificar un asiento o en cambios de cuenta masivos) pasa por este motor, que guarda un registro de cambios con el valor anterior y el nuevo; un IdLote vincula los cambios de varios apuntes hechos en una misma operación.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-ASI-01..03: agregado atómico, cuadre, automáticos regenerables por su lote
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Asientos/ (código en el disco del propietario): motor anterior
- 2026-10-28 12:28 · draft: Implementar el motor de asientos y publicar su contrato para las áreas que generan asientos.
- 2026-10-29 10:12 · Sí, de hecho la modificación de apuntes se centralizará en la clase que gestiona apuntes (creo que no hay un nodo específico para ella, lo crearé si es el caso) que mantendrá el log de cambios con un IdLote para vincular cambios de varios apuntes (por ejemplo, al modificar un asiento o con cambios de cuenta masivos)
