---
title: Conciliar los cobros de las plataformas
status: draft
depends_on: [norma43/conciliacion, norma43/pantallas, norma43/remesas, facturacion/reservas, facturacion/reservas/compensacion-comision]
---
## Summary
Pendiente. Desde la pantalla de líneas N43, conciliar de forma automática los cobros de Airbnb y Booking con las facturas nominales de cada reserva (validadas, no borradores), teniendo en cuenta los pagos fraccionados (varias líneas para una reserva) y los agrupados (una línea para varias reservas). La N43 no lleva el código de la reserva: el cruce es por importe y fechas.
Lo comprobado el 2026-11-25 en una copia de validación (cobros de enero a octubre de 2026 contra las ventas por facturar): Booking paga el total menos la comisión y menos el «cargo por servicio de los pagos», un 1,3 % del total que Booking factura junto con la comisión en la misma factura de compra (dos líneas); cruzan las 21 transferencias, de una o dos reservas, pagadas entre 2 y 12 días después de la salida, y la línea trae «ID.<código del alojamiento>». Airbnb paga el total menos la comisión entre 2 y 4 días después de la entrada; cruzan 52 de 79 transferencias (48 de una reserva y 4 de dos) y casi todas las demás son reservas pagadas en varios trozos (por ejemplo, 7 transferencias que suman una reserva) o reservas modificadas.
**Aceptación:** con datos de prueba, un cobro de Booking de dos reservas y una reserva de Airbnb pagada en tres transferencias quedan conciliados contra sus facturas; la diferencia con el total de cada factura (comisión y cargo de pago) cuadra con la compensación de la factura de compra de la plataforma; lo que no cruza queda para conciliar a mano, con su motivo.

## Decisions
- 2026-11-25 14:18 · Creado como borrador desde facturacion/reservas (propietario, Q3: «Ok con el desglose»), para un hilo propio después de la factura nominal al huésped.

## Requirements
- 2026-11-25 13:53 · ¿Puedes revisar si hay alguna forma fácil de casar las reservas con las líneas N43?
- 2026-11-25 14:13 · Opción desde la pantalla de N43 de conciliar automáticamente los movimientos de AirBnB y Booking con las facturas generadas (definitivas, no borrador), teniendo en cuenta pagos fraccionados.
- 2026-11-25 14:18 · Q2. Te adjunto una factura de este año, hay dos líneas: Cargo por servicio de los pagos EUR 6,88 Reservas EUR 529,20 EUR 79,38 Por lo que se cobra todo junto pero se detalla en la factura
- 2026-11-25 14:18 · draft: En la pantalla de líneas N43, añadir una acción de cabecera que concilie automáticamente los cobros de Airbnb y Booking con las facturas nominales validadas de sus reservas (factura por reserva, 430 compartida por plataforma), por importe y fechas: Airbnb, total menos comisión, 2 a 4 días después de la entrada y a veces en varias transferencias; Booking, total menos comisión menos el 1,3 % de cargo por servicio de los pagos, 2 a 12 días después de la salida y a veces agrupando varias reservas; lo que no cruce queda para conciliar a mano con su motivo.
