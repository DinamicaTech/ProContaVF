---
title: Tesorería
status: idea
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Cobros, pagos, vencimientos, remesas y SEPA: fuera del trabajo actual, pendiente de delimitar propietarios y contratos. Si se incorpora, se hará con tablas nuevas y limpias: la migración del ERP no importa Pagos ni las remesas SEPA.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-01 16:05 · [replaced by 2026-11-10 13:06] Las remesas SEPA y sus recibos tienen ya nodo propio, facturacion/remesas-sepa (importación desde el ERP), y los vencimientos de las facturas de venta (`Pagos` del ERP) son de facturacion/factura-venta. Esta idea conserva el resto de la tesorería (cobros, pagos y previsión).
- 2026-11-10 13:06 · Vuelven aquí las remesas SEPA y los vencimientos: se borra el nodo facturacion/remesas-sepa, y la migración del ERP no importa `Pagos`, los fraccionamientos, `Recibos`, `RemesasRecibos`, `DatosBancarios` ni `DatosBancariosGestion` (implantacion/migracion-erp 2026-11-10 13:06). Los requisitos del nodo borrado se copian abajo con sus fechas.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 19 RF-N43-05 y 29 P-10: tesorería no delimitada
- 2026-10-28 12:39 · A7) b (de momento no trabajaremos tesorería)
- 2026-10-28 12:28 · idea: Gestionar cobros, pagos, remesas y vencimientos (tesorería completa), separada de Norma 43.
- 2026-11-01 16:01 · 2. Mejor crear un nodo e importarlos, luego ya afinaremos si generamos remesas SEPA o no.
- 2026-11-10 13:06 · Punto B: a) Pasar remesas a creative.
