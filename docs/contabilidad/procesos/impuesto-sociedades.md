---
title: Impuesto sobre Sociedades
status: draft
depends_on: [contabilidad/procesos]
threads: []
---
## Summary
Proceso de cierre que propone la cuota del Impuesto sobre Sociedades a partir del resultado antes de impuestos (grupos 6 y 7) y del tipo anual configurado, y contabiliza el importe que confirma o escribe el operador (6300 contra 4752, menos las retenciones y pagos a cuenta de la 473). Se ejecuta antes de la regularización. No es el cálculo completo del modelo 200.
**Aceptación:** la cuota contabilizada es la confirmada; repetirlo regenera el lote; la regularización posterior incluye la 6300.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Va antes de la regularización: en 2025 la 6300000 quedó fuera de ella (hallazgo al validar balances, 2026-11-11).

## Requirements
- 2026-11-13 12:05 · draft: Proponer la cuota del Impuesto sobre Sociedades con el resultado y el tipo anual, y contabilizar el importe confirmado (6300 contra 4752 y 473) antes de la regularización.
