---
title: Reiniciar el cuadre del Mayor
status: draft
depends_on: [contabilidad/mayor]
threads: []
---
## Summary
Proceso contable que reinicia el cuadre de las cuentas del Mayor en el ejercicio de trabajo: marca como cuadradas las subcuentas con movimientos y saldo dentro del umbral y, si se pide, quita antes la casilla «Cuadrada» de todas, sin borrar nunca las notas. Sale del Mayor porque afecta a todas las subcuentas. La lógica ya está hecha y probada (`CuadresMayor.Reiniciar` y `SesionMayor.ReiniciarCuadreAsync`, en `src/ProConta.Contabilidad/Mayor`) y el umbral está en Configuración general (`Configuracion.UmbralCuadreMayor`, vacío = 0,05 €); falta la entrada en los procesos contables.
**Aceptación:** desde los procesos contables, reiniciar el cuadre de un ejercicio marca las cuentas con saldo dentro del umbral, respeta la opción de quitar antes las casillas, conserva las notas y el Mayor lo muestra en verde.

## Decisions
- 2026-11-12 10:15 · Creado como borrador desde contabilidad/mayor (hilo «Terminar el Mayor reutilizable»), a petición del propietario.

## Requirements
- 2026-11-12 09:57 · Reiniciar cuadre, viendo que afecta a todos las subcuentas, irá en procesos contables, por lo que lo quitamos del mayor.
- 2026-11-12 10:14 · Ok a lo de apuntarlo en procesos.
- 2026-11-12 10:15 · draft: Reiniciar el cuadre del Mayor desde los procesos contables: marcar como cuadradas las subcuentas del ejercicio de trabajo con saldo dentro del umbral y, si se pide, quitar antes la casilla de todas, sin borrar notas.
