---
title: Céntimos residuales
status: draft
depends_on: [contabilidad/procesos]
threads: []
---
## Summary
Proceso de cierre que lleva a la cuenta de diferencias de redondeo los céntimos que quedan en el saldo de las subcuentas (por ejemplo, la 430 de un cliente con 0,01 € tras cobrar 120,99 € de una factura de 121,00 €), con fecha 31/12. Sustituye a «Mata centimillos» del sistema antiguo, sin sus defectos: calcula desde los apuntes y no duplica al repetirse.
**Aceptación:** las subcuentas con saldo distinto de cero dentro del umbral quedan a cero contra las cuentas de redondeo del ejercicio; repetirlo no genera asientos nuevos ni duplica el lote.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Se mantiene (propietario 2026-11-13 12:00): el motor cuadra cada asiento, pero eso no deja a cero el saldo de las subcuentas. Umbral configurable (el antiguo usaba ±0,07 €); cuentas de redondeo = las del ejercicio (Parámetros › Ejercicios, vacías 6590000 y 7590000).
- 2026-11-15 17:20 · Cumplida la línea «Derived from pantalla-de-inicio» del 2026-11-15 17:20: la salud de Inicio avisa (gravedad «aviso») de las subcuentas 40, 41 y 43 con saldo a la fecha de corte distinto de cero y menor de 1 € en valor absoluto, con su número, su total y el Mayor ya con ellas (pantalla-de-inicio 2026-11-15 17:20). Solo es el aviso: el proceso de este nodo sigue por hacer, y cuando exista, el aviso podrá abrirlo.

## Requirements
- 2026-11-13 12:00 · 4. Tienes razón, estaba pensando en centimillos de un asiento, no del saldo de una subcuenta. Sí, lo seguimos necesitando
- 2026-11-13 12:05 · draft: Llevar a 31/12 a las cuentas de redondeo del ejercicio los céntimos residuales del saldo de las subcuentas (dentro de un umbral configurable), como propuesta confirmada en un lote regenerable.
- 2026-11-15 17:20 · Derived from pantalla-de-inicio: aviso en la salud de Inicio de las subcuentas de clientes y proveedores (40, 41 y 43) con céntimos residuales (saldo a la fecha de corte distinto de cero y menor de 1 € en valor absoluto), con su número, su importe y el Mayor de esas subcuentas; el proceso que los salda sigue siendo de este nodo.
