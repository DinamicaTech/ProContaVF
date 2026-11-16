---
title: Compensar la comisión de las plataformas
status: draft
depends_on: [facturacion/reservas, compras/factura-compra, contabilidad/plantillas-asiento]
---
## Summary
Tarea pendiente: al registrar la factura de comisión de una plataforma (Airbnb, Booking), compensar su cuenta de proveedor (410) contra su cuenta de cliente (430), para que la 430 quede con lo que de verdad ingresa el banco.
**Aceptación:** tras registrar la factura de comisión de un mes, la 410 de la plataforma queda a cero y la 430 de la plataforma cuadra con los ingresos bancarios de ese mes; el asiento de compensación cuadra (debe = haber) y no se genera dos veces.

## Decisions
- 2026-11-14 14:00 · Creado como borrador desde facturacion/reservas, con el OK del propietario.

## Requirements
- 2026-11-12 14:40 · Q2. a. La plataforma es la que realmente nos paga, aunque no el 100% de la factura ya que se queda su comisión (registradas como facturas de compra). Esto contablemente se ha de cuadrar ya que por un lado, la plataforma actúa como 430 y por otro lado como 410, el ingreso bancario no coincide con el importe de la factura emitida y la factura de compra no tiene movimiento bancario de pago.
- 2026-11-14 14:00 · draft: Al registrar la factura de comisión de una plataforma, compensar su 410 contra su 430 con las plantillas de asiento.
