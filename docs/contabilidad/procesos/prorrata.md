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
- 2026-11-15 19:00 · Cumplida la línea «Derived from informes/informe-gestoria» del 2026-11-15 18:10, sin asiento: `Calculo303.cs` da el porcentaje real acumulado (bases repercutidas de actividades 100 % deducibles entre el total, redondeado al entero superior tras quitar el ruido decimal; sin ventas, 0 %), el volumen de operaciones por actividad y trimestre, la cuota sujeta a prorrata, lo deducido con la provisional, lo deducible con la real y la regularización estimada. El proceso contable y el asiento de regularización siguen pendientes.

## Requirements
- 2026-11-13 12:05 · draft: Calcular la prorrata de IVA (la legal propuesta por defecto, la del gestor si se fija, con desglose en Excel) y regularizar la prorrata definitiva (4729) al cierre del ejercicio.
- 2026-11-15 18:10 · Derived from informes/informe-gestoria: porcentaje provisional del ejercicio, porcentaje real acumulado (operaciones con derecho a deducción entre el total, redondeado al entero superior), volumen de operaciones por actividad y trimestre, y regularización estimada por trimestre (cuota sujeta a prorrata por la diferencia de porcentajes), sin generar asiento. Paridad con el libro anterior: 2026 a 3T da 52.289,30 / 263.012,00 = 19,88 % → 20 % y una regularización estimada de 99,44 €.
