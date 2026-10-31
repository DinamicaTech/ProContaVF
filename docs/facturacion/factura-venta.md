---
title: Factura de venta
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero, facturacion/conceptos, facturacion/verifactu]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Ente FacturaVenta: borrador sin número, cálculo de líneas y totales, validación, numeración transaccional, instantánea de emisor y destinatario, estados, inmutabilidad tras emitir, duplicado y rectificativa 1:1.
**Aceptación:** la de spec § 10.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Se ratifica la rectificativa completa 1:1 y el retorno a borrador tras consultar la AEAT, como dice RF-FAC-09 (resuelve P-05).

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-01..09: borrador, cálculo, suplidos, validaciones, instantánea, estados, inmutabilidad, duplicar, rectificar
- 2026-10-28 12:28 · draft: Implementar el ente FacturaVenta con su cálculo, numeración, emisión y rectificación.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-05 ¿se ratifica la rectificativa completa 1:1 y el retorno a borrador tras consultar la AEAT? (a) sí, como dice la spec ✅ (b) revisar antes
- 2026-10-31 16:54 · 15. a
- 2026-10-31 16:54 · answer: P-05 rectificativa 1:1 y retorno a borrador → (a) sí, como dice la spec
