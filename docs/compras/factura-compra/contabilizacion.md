---
title: Contabilización de compras
status: draft
depends_on: [compras/factura-compra, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Genera los asientos de las facturas de compra con el motor de contabilidad/asientos.
**Aceptación:** compra registrada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 18:14 · Un descuadre por redondeo de hasta 0,05 € (propuesta pendiente de validar) se ajusta con un apunte en la cuenta de diferencias de redondeo de la sociedad (6690000 en el prototipo); un descuadre mayor bloquea la contabilización. Nunca se graba un asiento descuadrado.
- 2026-10-29 18:14 · La factura rectificativa se contabiliza en el trimestre de IVA activo: su fecha contable es la mayor entre la fecha de la factura original y el primer día del trimestre activo.
- 2026-10-29 18:14 · La autoliquidación del IVA (inversión del sujeto pasivo e intracomunitarias) usa el tipo real de la factura, no un 21 % fijo. Las cuentas de IVA, retención, suplidos y redondeo son parámetros de la sociedad.
- 2026-10-29 18:53 · Propuesta pendiente de validar: la fecha contable de una factura es la mayor entre su fecha y el primer día del trimestre de IVA activo (sustituye la regla antigua del 2 de enero para facturas del ejercicio anterior); una factura de un ejercicio posterior al de trabajo se bloquea.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-07: contabilización de compra, contrato incompleto
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de compra registradas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · question: P-08 ¿de dónde sale la cuenta de gasto? (a) la del proveedor, con la última usada como propuesta revisable ✅ (b) reglas por concepto
- 2026-10-29 18:14 · * A veces un asiento queda descuadrado por tema de redondeos (principalmente con apuntes de IVA), si es el caso, hay que ajustarlo con un apunte de diferencias de redondeo o similar
- 2026-10-29 18:14 · * Ok, rectificativas al trimestre activo
