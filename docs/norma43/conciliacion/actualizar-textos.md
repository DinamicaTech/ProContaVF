---
title: Actualizar los textos de los asientos de banco
status: draft
depends_on: [norma43/conciliacion, facturacion/factura-venta, compras/factura-compra, terceros/tercero, contabilidad/asientos]
---
## Summary
Que ventas, compras y terceros pidan a la conciliación que regenere los textos (descripción, concepto y documento) de los asientos de banco de las líneas enlazadas cuando cambie algo que sale en ellos, también en un trimestre cerrado.
**Aceptación:** cambiar la descripción abreviada o el número de una factura enlazada, o la subcuenta o el nombre del tercero, actualiza los asientos de banco de sus líneas con `ServicioConciliacionN43.ActualizarTextosAsync` (mismo asiento, con registro de cambios); en un trimestre cerrado se hace igual (sin efecto fiscal; la subcuenta del tercero solo si el asiento no lleva retención) y en un ejercicio cerrado se avisa sin cambiar nada.

## Decisions
- 2026-11-13 10:50 · Creado como borrador desde norma43/conciliacion. `ActualizarTextosAsync` ya existe; el motor admite en un trimestre cerrado los cambios de textos y el de subcuenta 4XX de un tercero sin 4751 (contabilidad/asientos 2026-11-13 10:50). Cambiar la subcuenta del asiento de banco (no solo los textos) necesitará volver a contabilizar la línea o una variante de la operación que también cambie la cuenta.

## Requirements
- 2026-11-13 10:42 · Esto lleva al punto que si modificamos la descripción abreviada de una factura de venta, también deberíamos actualizar el asiento (solo descripción), al igual que si modificamos el número de factura o, incluso, la subcuenta del proveedor (aunque sea un trimestre cerrado)
- 2026-11-13 10:50 · draft: Que ventas, compras y terceros llamen a ServicioConciliacionN43.ActualizarTextosAsync al cambiar la descripción abreviada o el número de una factura enlazada a una línea N43, o la subcuenta del tercero, para regenerar los textos (y, si cambia, la subcuenta) de los asientos de banco, también en un trimestre cerrado.
