---
title: Crear el ejercicio siguiente
status: draft
depends_on: [contabilidad/procesos, organizacion/sociedades-ejercicios, contabilidad/cuenta-contable]
threads: []
---
## Summary
Proceso que crea el ejercicio siguiente de una sociedad: su fila en SociedadesEjercicios con los parámetros copiados del actual, el plan de cuentas y las subcuentas, y el trimestre activo en el 1.º. Es la condición previa de la apertura y del cierre.
**Aceptación:** el ejercicio nuevo tiene las mismas cuentas y parámetros que el anterior y su trimestre activo es el 1.º; repetirlo no duplica nada.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Lo hace contabilidad/procesos, como ya decía organizacion/sociedades-ejercicios («la apertura y el cierre los hará contabilidad/procesos»).

## Requirements
- 2026-11-13 12:05 · draft: Crear el ejercicio siguiente de una sociedad (parámetros, plan de cuentas y subcuentas, trimestre activo 1.º) como condición previa del cierre y la apertura.
