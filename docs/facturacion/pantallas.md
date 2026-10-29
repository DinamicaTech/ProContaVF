---
title: Pantallas de facturación
status: draft
depends_on: [facturacion/factura-venta, facturacion/programadas, plataforma/contenedor]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Listado filtrable de facturas y programaciones con selección explícita, ficha de factura y acciones contextuales.
**Aceptación:** comparación con la maqueta; selección, fila activa y universo filtrado no se confunden.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-10: listado, selección, proforma, contadores
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/WpfFacturas/docs/WPF_FACTURAS.md: pantallas anteriores
- 2026-10-28 12:28 · From sources/maquetas/Facturas/: maqueta de facturas
- 2026-10-28 12:28 · draft: Construir el listado y la ficha de facturas según la maqueta.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final de Facturas emitidas, listado y detalle de factura (sources/maquetas/Facturas/ y lienzo del contenedor), antes de construir su UI WPF.
