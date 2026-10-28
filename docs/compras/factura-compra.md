---
title: Factura de compra
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Ente FacturaCompra con sus desgloses: inserción atómica, MD5 compatible del PDF para duplicados, configuración por sociedad y destino por sociedad/ejercicio.
**Aceptación:** PDF duplicado no crea segunda compra; un corte no deja desgloses huérfanos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-05: duplicados, persistencia atómica, configuración
- 2026-10-28 12:28 · draft: Implementar el ente FacturaCompra y su persistencia con control de duplicados.
