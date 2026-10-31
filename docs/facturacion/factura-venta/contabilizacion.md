---
title: Contabilización de ventas
status: draft
depends_on: [facturacion/factura-venta, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Genera los asientos de las facturas de venta con el motor de contabilidad/asientos; la lógica de contabilización vive aquí, no en contabilidad.
**Aceptación:** factura validada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · La factura de venta se contabiliza al validarla, en un lote regenerable que no duplica; el asiento se fecha el día en que se genera, como en compras (compras/factura-compra/contabilizacion 2026-10-30 09:58). Las ventas registradas antes como albaranes o similar (facturacion/reservas) se contabilizan al facturarse de forma masiva.

## Requirements
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de venta emitidas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿cuándo se contabiliza? (a) al validar la factura, en lote regenerable ✅ (b) manualmente por lotes
- 2026-10-31 16:54 · 16. a, aunque aparecerá un submodelo de albaranes o algo similar para poder registrar ventas (en forma de albaranes o similar) que se facturarán (y contabilizarán) posteriormente de forma masiva. Aplicable, principalmente, a las reservas de los pisos turísticos
- 2026-10-31 16:54 · answer: cuándo se contabiliza → (a) al validar la factura, en lote regenerable; las ventas registradas como albaranes o similar se facturarán y contabilizarán después de forma masiva (facturacion/reservas)
