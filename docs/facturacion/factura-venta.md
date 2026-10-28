---
title: Factura de venta
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero, facturacion/conceptos, facturacion/verifactu]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Ente FacturaVenta: borrador sin número, cálculo de líneas y totales, validación, numeración transaccional, instantánea de emisor y destinatario, estados, inmutabilidad tras emitir, duplicado y rectificativa 1:1.
**Aceptación:** la de spec § 10.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-01..09: borrador, cálculo, suplidos, validaciones, instantánea, estados, inmutabilidad, duplicar, rectificar
- 2026-10-28 12:28 · draft: Implementar el ente FacturaVenta con su cálculo, numeración, emisión y rectificación.
- 2026-10-28 12:28 · question: P-05 ¿se ratifica la rectificativa completa 1:1 y el retorno a borrador tras consultar la AEAT? (a) sí, como dice la spec ✅ (b) revisar antes
