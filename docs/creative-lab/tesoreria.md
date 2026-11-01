---
title: Tesorería
status: idea
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Cobros, pagos y previsión de tesorería: fuera del trabajo actual, pendiente de delimitar propietarios y contratos. Las remesas SEPA están en facturacion/remesas-sepa y los vencimientos de venta en facturacion/factura-venta.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-01 16:05 · Las remesas SEPA y sus recibos tienen ya nodo propio, facturacion/remesas-sepa (importación desde el ERP), y los vencimientos de las facturas de venta (`Pagos` del ERP) son de facturacion/factura-venta. Esta idea conserva el resto de la tesorería (cobros, pagos y previsión).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 19 RF-N43-05 y 29 P-10: tesorería no delimitada
- 2026-10-28 12:39 · A7) b (de momento no trabajaremos tesorería)
- 2026-10-28 12:28 · idea: Gestionar cobros, pagos, remesas y vencimientos (tesorería completa), separada de Norma 43.
