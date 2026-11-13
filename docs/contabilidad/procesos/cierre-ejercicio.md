---
title: Cierre y reapertura del ejercicio
status: draft
depends_on: [contabilidad/procesos, contabilidad/procesos/nuevo-ejercicio, organizacion/sociedades-ejercicios]
threads: []
---
## Summary
Cierre completo estándar en una operación atómica: regularización de los grupos 6 y 7 contra la 129 (asiento 10000), cierre de los grupos 1 a 5 (asiento 20000) y apertura del ejercicio siguiente (asiento 1), cuadrados entre sí, y marca del ejercicio cerrado. La reapertura deshace solo esos tres lotes (y la reversión de facturas pendientes de recibir de la apertura) y quita la marca, conservando los demás asientos. El primer cierre que hace ProContaVF es el de 2026; los ejercicios anteriores quedan como vienen de ContaSol.
**Aceptación:** ejecutar dos veces no duplica lote; reabrir conserva asientos ajenos; regularización, cierre y apertura cuadran entre sí; sin ejercicio siguiente no deja cerrar y lo explica.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Primer cierre de ProContaVF: 2026, con la apertura de 2027 (propietario 2026-11-13 11:52, P5); los años anteriores, incluidos los asientos 869/870 de 2019 importados como ordinarios, quedan como vienen de ContaSol.
- 2026-11-13 12:05 · Calcula desde los apuntes, no desde la caché de saldos. Precondición: el ejercicio siguiente existe (contabilidad/procesos/nuevo-ejercicio); orden previo recomendado: amortizaciones, céntimos residuales, facturas pendientes de recibir, regularización de la prorrata e Impuesto sobre Sociedades.

## Requirements
- 2026-11-13 11:52 · answer: ¿Cuál es el primer cierre que hace ProContaVF? → P5. 2026
- 2026-11-13 12:05 · draft: Cerrar el ejercicio (regularización 10000, cierre 20000 y apertura 1 del siguiente, cuadrados, y marca de cerrado) en una operación atómica, y reabrirlo deshaciendo solo esos lotes; primer cierre, el de 2026.
