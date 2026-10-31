---
title: Contabilización de ventas
status: draft
depends_on: [facturacion/factura-venta, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Genera los asientos de las facturas de venta con el motor de contabilidad/asientos; la lógica de contabilización vive aquí, no en contabilidad.
**Aceptación:** factura validada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de venta emitidas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · question: ¿cuándo se contabiliza? (a) al validar la factura, en lote regenerable ✅ (b) manualmente por lotes
