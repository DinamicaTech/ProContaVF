---
title: Roles del tercero
status: draft
depends_on: [terceros/tercero, contabilidad/cuenta-contable, organizacion/formas-pago]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Roles opcionales de una identidad (Cliente, Proveedor, Contacto…) con IDs funcionales propios y cuentas contables por rol y sociedad/ejercicio.
**Aceptación:** añadir un rol no duplica la identidad; las cuentas cambian con el contexto.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-03, 04: roles idempotentes y cuentas por rol y sociedad/ejercicio
- 2026-10-28 12:28 · draft: Implementar los roles del tercero y sus cuentas por sociedad y ejercicio.
- 2026-10-28 12:28 · question: ¿Asociación y Particular son roles o tipos de persona? (a) tipos/clasificación: Particular es persona física y Asociación entidad sin personalidad, y los roles siguen siendo Cliente/Proveedor/Contacto ✅ (b) roles nuevos
- 2026-10-28 12:28 · question: P-04 ¿cómo se representa la pareja Proveedor/Acreedor? (a) dos cuentas opcionales del rol Proveedor ✅ (b) dos roles distintos
