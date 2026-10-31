---
title: Contabilización de compras
status: draft
depends_on: [compras/factura-compra, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Genera los asientos de las facturas de compra con el motor de contabilidad/asientos.
**Aceptación:** compra registrada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-07: contabilización de compra, contrato incompleto
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de compra registradas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · question: P-08 ¿de dónde sale la cuenta de gasto? (a) la del proveedor, con la última usada como propuesta revisable ✅ (b) reglas por concepto
