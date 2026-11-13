---
title: Recalcular saldos
status: draft
depends_on: [contabilidad/procesos, contabilidad/asientos]
threads: []
---
## Summary
Proceso visible que recalcula la caché de saldos de las subcuentas del ejercicio desde los apuntes (`IMotorAsientos.RecalcularSaldosAsync`, ya hecho), como punto de sincronismo. El motor ya la mantiene al grabar cada asiento; esto la rehace si alguien ha tocado la BD por fuera. No genera asientos.
**Aceptación:** tras descuadrar a mano el saldo de una subcuenta en una copia, el proceso lo deja igual a la suma de sus apuntes.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Visible en la pantalla de procesos, no oculto (propietario 2026-11-13 12:00): no puede hacer daño. Sustituye a «Regenera saldos» del sistema antiguo, que el reparto inicial mandaba a contabilidad/centros-coste.

## Requirements
- 2026-11-13 12:00 · 12. Ok, sí, al menos tener un punto de sincronismo. No hace falta que esté oculto, tampoco es un proceso que pueda generar 'daños'
- 2026-11-13 12:05 · draft: Recalcular desde los apuntes la caché de saldos de las subcuentas del ejercicio, desde la pantalla de procesos contables.
