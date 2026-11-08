---
title: Contabilización de ventas
depends_on: [facturacion/factura-venta, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 17:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
---
## Summary
Genera los asientos de las facturas de venta con el motor de contabilidad/asientos; la lógica de contabilización vive aquí, no en contabilidad. Código: `ContabilizadorVentas` y `AsientoVenta` (src/ProConta.Facturacion/Facturas/ContabilizacionVentas.cs).
**Aceptación:** factura validada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · La factura de venta se contabiliza al validarla, en un lote regenerable que no duplica; el asiento se fecha el día en que se genera, como en compras (compras/factura-compra/contabilizacion 2026-10-30 09:58). Las ventas registradas antes como albaranes o similar (facturacion/reservas) se contabilizan al facturarse de forma masiva.
- 2026-11-01 18:15 · Asiento de una factura de venta, un lote por factura (motor «facturacion/factura-venta», proceso VENTA, clave IdFactura) que se sustituye al regenerar: Debe la cuenta del cliente por el total a cobrar y su cuenta de retención por la retención; Haber el ingreso de cada cuenta de las líneas no suplidas (agrupado por cuenta y centro de coste), el IVA de cada tipo con la cuenta del ejercicio para ese tipo y los suplidos a la cuenta de su línea o, si no tiene, a CuentaSuplido del ejercicio. El IdAsientoContable queda en la factura.
- 2026-11-01 18:15 · Rectificativa: el motor de asientos no admite importes negativos, así que cada importe negativo se apunta en positivo en el lado contrario (el cliente al Haber, el ingreso y el IVA al Debe) y el asiento es de tipo rectificación. Precisa la respuesta del 2026-11-01 17:18, que decía «importes en negativo»: el efecto contable es el mismo.
- 2026-11-01 18:15 · Fecha del asiento: el día en que se genera si cae en el ejercicio de la factura; si no, la fecha de la factura.
- 2026-11-01 18:15 · Antes de validar se comprueban todas las cuentas del asiento en el ejercicio (existen y admiten apuntes). Si al cliente le falta la cuenta del cliente o la de retención (cuando hay retención), el error ofrece asignarla o crearla con su nombre (430 o «Retención IRPF <cliente>» en 473, código siguiente libre propuesto); si la cuenta del rol es de otro ejercicio, ofrece darla de alta en este con el mismo código. El recargo de equivalencia aún no se contabiliza y se rechaza con un error que lo explica.
- 2026-11-04 17:57 · Validado por el propietario (facturación de venta, PR #14).
- 2026-11-07 12:49 · Fecha del asiento (propietario 2026-11-07 11:59): la de la factura, o el día 1 del trimestre activo del ejercicio si es anterior, como en compras (`TrimestreContable`). Sustituye a «el día en que se genera». Pendiente: que ventas respete el trimestre cerrado al regenerar y que lleve libro de IVA repercutido.
- 2026-11-07 13:00 · IVA repercutido (propietario 2026-11-07 11:59: «Ventas debería usar una 477, no una 472»): la cuenta de cada tipo sale de CuentaIVARepercutido0..4 del ejercicio o, vacía, de la 477 con la terminación de la CuentaTipoIVA. Si no existe, la validación no lo cuenta como error y se crea al contabilizar, con el nombre «IVA repercutido del N %».
- 2026-11-07 21:46 · Derived from contabilidad/asientos: el trimestre cerrado lo protege ya el motor de asientos; si algún día ventas regenera el asiento de una factura de un trimestre cerrado, el motor lo rechaza y habrá que hacer contra-asiento, como en compras. Queda pendiente el libro de IVA repercutido.

## Requirements
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de venta emitidas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿cuándo se contabiliza? (a) al validar la factura, en lote regenerable ✅ (b) manualmente por lotes
- 2026-10-31 16:54 · 16. a, aunque aparecerá un submodelo de albaranes o algo similar para poder registrar ventas (en forma de albaranes o similar) que se facturarán (y contabilizarán) posteriormente de forma masiva. Aplicable, principalmente, a las reservas de los pisos turísticos
- 2026-10-31 16:54 · answer: cuándo se contabiliza → (a) al validar la factura, en lote regenerable; las ventas registradas como albaranes o similar se facturarán y contabilizarán después de forma masiva (facturacion/reservas)
- 2026-11-01 17:18 · 4. La cuenta de retención sería diferente para cada cliente.
- 2026-11-01 17:18 · Realmente debería ser una subcuenta por cliente, no por inmueble, pero como apenas hay rotación, pues ya no sirve aunque no sea lo ideal.
- 2026-11-01 17:18 · Me parece perfecto
- 2026-11-01 17:18 · answer: cuentas del asiento → Debe la cuenta del cliente en la sociedad y su cuenta de retención (por cliente y sociedad, en terceros/roles); Haber el ingreso por la cuenta de cada línea, el IVA por las cuentas del ejercicio según el tipo y los suplidos a CuentaSuplido; la rectificativa con importes en negativo
- 2026-11-04 17:57 · Ok, validado
- 2026-11-08 10:41 · Derived from contabilidad/procesos (propietario 2026-11-08 09:40): IVA repercutido por actividad. Cada línea de la factura de venta tiene su centro de coste; su actividad (Actividades, la del centro o la heredada) da la 477: la serie de la actividad con el 2 cambiado por el 7 (4728 → 4778) y la terminación de la 477 general del tipo (4770021 → 4778021). Si una factura mezcla actividades, el IVA de cada tipo se reparte por actividad. Sin actividad o sin serie, la 477 general como hasta ahora. La subcuenta que no exista se crea al contabilizar. Es la base para calcular la prorrata por actividad. El propietario revisará también la descripción de los apuntes de ventas.

## Hand-over
- 2026-11-01 18:15 · Hecho con pruebas sin BD (lados, rectificativa, suplidos) y contra Access. Pendiente: ejecutar las pruebas Access en el equipo del propietario.
