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

## Requirements
- 2026-11-13 12:00 · 4. Tienes razón, estaba pensando en centimillos de un asiento, no del saldo de una subcuenta. Sí, lo seguimos necesitando
- 2026-11-13 12:05 · draft: Llevar a 31/12 a las cuentas de redondeo del ejercicio los céntimos residuales del saldo de las subcuentas (dentro de un umbral configurable), como propuesta confirmada en un lote regenerable.
