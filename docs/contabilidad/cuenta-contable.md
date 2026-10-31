---
title: Cuenta contable
status: draft
depends_on: [datos/esquema, datos/persistencia, organizacion/sociedades-ejercicios]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Ente CuentaContable: identidad permanente y cuenta por ejercicio con código, jerarquía, actividad, bloqueo e imputabilidad; alta, recodificación, fusión y copia entre ejercicios con trazabilidad.
**Aceptación:** un código igual en ejercicios distintos no mezcla cuentas.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-CUE-01, 02: catálogo de cuentas e identidad
- 2026-10-28 12:28 · draft: Implementar el ente CuentaContable y publicar su contrato.
