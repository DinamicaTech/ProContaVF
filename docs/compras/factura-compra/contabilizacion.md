---
title: Contabilización de compras
status: draft
depends_on: [compras/factura-compra, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Analizar formularios del ProConta antiguo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G5wuUSovHLCVH22T7EntXcw
---
## Summary
Genera los asientos de las facturas de compra con el motor de contabilidad/asientos.
**Aceptación:** compra registrada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: el asiento y el libro de IVA que genera hoy «Exportar factura» de `frmEntradaFacturas`, con transitario, deshacer, rehacer y rectificativa, en `sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md` § 3-4, y sus defectos (§ 7), que no se reproducen: asiento descuadrado de más de 0,05 € grabado solo con aviso, parada con suplidos, rectificativa fechada en un trimestre de IVA quizá ya presentado, 21 % separado del total en importaciones sin transitario. Los porcentajes de IVA y las cuentas fijas se parametrizan.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-07: contabilización de compra, contrato incompleto
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de compra registradas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · question: P-08 ¿de dónde sale la cuenta de gasto? (a) la del proveedor, con la última usada como propuesta revisable ✅ (b) reglas por concepto
- 2026-10-29 17:45 · Derived from compras/pantallas: generar el asiento contable (con su libro de IVA, analítica y notas) de cada factura revisada, y deshacerlo o rehacerlo, como hace hoy frmEntradaFacturas (sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md § 3-4).
