---
title: Remesas SEPA
status: draft
depends_on: [facturacion/factura-venta, terceros/tercero]
threads:
  - 2026-11-01 16:05 · Analizar la migración del ERP | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GHRgCCJAyW6rPfAPdHnyMXG
---
## Summary
Recibos y remesas SEPA de las facturas de venta. Primero se importan del ERP (`Recibos`, `RemesasRecibos`, con sus mandatos y datos bancarios del deudor) en tablas propias; después se decidirá si ProContaVF genera remesas SEPA.
**Aceptación:** cada recibo y remesa del ERP queda importado con su estado, su factura enlazada por mapa y sus totales cuadrados con el origen; importar no envía ni genera ninguna remesa.

## Decisions
- 2026-11-01 16:05 · Creado desde implantacion/migracion-erp: dueño de `Recibos` y `RemesasRecibos` del ERP (y, a confirmar en el perfilado, de `DatosBancarios`, `InmueblesRecibosSEPA` e `InmueblesTitularesSEPA`). Sus tablas se pedirán a datos/esquema cuando se implemente. Toma de creative-lab/tesoreria solo la parte de remesas SEPA; el resto de la tesorería sigue como idea.

## Requirements
- 2026-11-01 16:01 · 2. Mejor crear un nodo e importarlos, luego ya afinaremos si generamos remesas SEPA o no.
- 2026-11-01 16:01 · draft: Importar del ERP los recibos y las remesas SEPA (Recibos, RemesasRecibos) en tablas propias de este nodo, enlazados a sus facturas de venta, sin generar ni enviar remesas.
