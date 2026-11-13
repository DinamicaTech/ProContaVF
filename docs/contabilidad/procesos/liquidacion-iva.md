---
title: Liquidación de IVA
status: draft
depends_on: [contabilidad/procesos, contabilidad/asientos, organizacion/sociedades-ejercicios, contabilidad/procesos/prorrata]
threads: []
---
## Summary
Liquidación trimestral del IVA en un solo asiento, con fecha el último día del trimestre: deja a cero las 477x y 472x de cada actividad, lleva a la 6341 la parte del IVA soportado por prorrata que no se deduce y el neto a 4750 (a ingresar) o 4700 (a compensar o devolver). Al confirmarla se activa el trimestre siguiente.
**Aceptación:** con repercutido 10.000, soportado 100 % deducible 2.000, soportado por prorrata 1.000 y prorrata del 80 %, el asiento es 477 D 10.000, 4725 H 2.000, 4727 H 1.000, 6341 D 200 y 4750 H 7.200; repetirla regenera el mismo lote sin duplicar, y al confirmar el trimestre activo pasa al siguiente.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Un solo asiento por trimestre (opción A del propietario, 2026-11-13 12:00), no los dos pasos del sistema antiguo (saldar contra la 6341000 y liquidar después): la 6341 solo recoge el IVA no deducible y el asiento se lee como el 303. Clasificación del IVA soportado por la actividad del centro de coste (Actividades), nunca por el nombre del centro; el intracomunitario y la inversión del sujeto pasivo se autorrepercuten y se deducen.
- 2026-11-13 12:05 · Al confirmar la liquidación se activa el trimestre siguiente (SociedadesEjercicios.TrimestreActivo), que bloquea en el motor los asientos con cuentas fiscales del trimestre liquidado (propietario 2026-11-13 11:52, P2).

## Requirements
- 2026-11-13 11:52 · answer: ¿Liquidar un trimestre activa el siguiente? → P2. Sí
- 2026-11-13 12:00 · answer: ¿Un asiento o dos pasos como el sistema antiguo? → Ok a la opción A
- 2026-11-13 12:05 · draft: Liquidar el IVA de un trimestre en un solo asiento (472x y 477x por actividad a cero, IVA no deducible por prorrata a la 6341 y neto a 4750 o 4700), como propuesta confirmada en un lote regenerable, y activar el trimestre siguiente al confirmar.
