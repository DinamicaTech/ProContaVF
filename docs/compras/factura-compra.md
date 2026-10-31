---
title: Factura de compra
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Ente FacturaCompra con sus desgloses: inserción atómica, MD5 compatible del PDF para duplicados, configuración por sociedad y destino por sociedad/ejercicio.
**Aceptación:** PDF duplicado no crea segunda compra; un corte no deja desgloses huérfanos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 07:46 · La factura de compra guarda su moneda y sus importes originales (los euros se calculan con el cambio del día de la factura) y, si es una rectificativa o un abono del proveedor, la factura a la que rectifica.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-05: duplicados, persistencia atómica, configuración
- 2026-10-28 12:28 · draft: Implementar el ente FacturaCompra y su persistencia con control de duplicados.
2026-10-31 07:30 · Y en el caso de facturas de importación, se puede indicar la moneda y hago conversión a euros aplicando el tipo de cambio de la moneda en la fecha de la factura. En la contabilidad siempre se trabaja en euros.
2026-10-31 07:41 · 4. Ver relacionadas lo que hará es mostrar la factura original (en el caso de estar sobre una rectificativa) o viceversa
