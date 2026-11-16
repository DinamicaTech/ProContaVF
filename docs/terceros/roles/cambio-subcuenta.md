---
title: Cambiar la subcuenta del tercero
status: draft
depends_on: [terceros/roles, contabilidad/asientos, facturacion/factura-venta, compras/factura-compra, norma43/conciliacion]
---
## Summary
Proceso para pasar a otra subcuenta todo lo de un tercero (facturas de venta y compra y sus cobros y pagos del banco), para que facturas y movimientos sigan en la misma subcuenta.
**Aceptación:** al cambiar la subcuenta de un rol del tercero, los apuntes de sus facturas y de los asientos de banco enlazados de los ejercicios abiertos pasan a la nueva (con registro de cambios), también en un trimestre cerrado si el asiento no lleva retención; los saldos de la subcuenta antigua y la nueva cuadran con sus facturas.

## Decisions
- 2026-11-16 19:30 · Creado como borrador desde norma43/conciliacion/actualizar-textos (propietario 2026-11-16 19:16: «Proceso completo aparte»).

## Requirements
- 2026-11-16 19:30 · draft: Proceso «Cambiar la subcuenta del tercero» que pase a la subcuenta nueva, en los ejercicios abiertos, los apuntes de sus facturas de venta y compra y los de los asientos de banco enlazados (con sus imputaciones N43), también en un trimestre cerrado si el asiento no lleva retención, para que facturas y cobros o pagos sigan en la misma subcuenta.
