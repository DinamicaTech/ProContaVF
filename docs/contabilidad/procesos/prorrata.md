---
title: Prorrata de IVA
status: draft
depends_on: [contabilidad/procesos, facturacion/factura-venta/contabilizacion]
threads: []
---
## Summary
Cálculo de la prorrata de IVA: el sistema propone la legal (operaciones con derecho a deducción / total de operaciones, sin IVA, a partir del libro de IVA repercutido por actividad), el operador puede fijar la que marca el gestor y se genera un Excel con el desglose para contrastarlo. Al cierre del ejercicio, regularización de la prorrata definitiva (4729).
**Aceptación:** la propuesta coincide con un cálculo hecho a mano sobre datos de prueba, el Excel muestra cada base que entra en el numerador y el denominador, fijar la del gestor no se pisa al recalcular y la regularización del 4.º trimestre ajusta la diferencia entre la provisional y la definitiva.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Reglas de cálculo en las decisiones y requisitos del nodo padre (contabilidad/procesos 2026-10-29 17:50 y requisito derivado 2026-11-11 11:35): el derecho a deducir lo da cada línea del libro, no el indicador Deducible de la actividad; los ingresos financieros sin factura y las ventas de inmovilizado se añaden o excluyen aparte.

## Requirements
- 2026-11-13 12:05 · draft: Calcular la prorrata de IVA (la legal propuesta por defecto, la del gestor si se fija, con desglose en Excel) y regularizar la prorrata definitiva (4729) al cierre del ejercicio.
