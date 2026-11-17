---
title: Reiniciar el cuadre del Mayor
status: draft
depends_on: [contabilidad/mayor]
threads:
  - 2026-11-17 19:20 · Reiniciar el cuadre del Mayor | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWxUTkA96csbwAnHBi8zRre
---
## Summary
Proceso contable que reinicia el cuadre de las subcuentas del Mayor en el ejercicio de trabajo: las que tienen movimientos y saldo dentro del umbral (Configuración general, `UmbralCuadreMayor`, vacío = 0,05 €) quedan marcadas como cuadradas con su saldo actual y todas las demás quedan sin la casilla «Cuadrada», también las cuadradas a mano con saldo; las notas nunca se borran y no se hacen asientos. Pantalla Contabilidad › «Reiniciar el cuadre del Mayor» (antes de «Fin de ejercicio»), con la tabla de lo que cambia antes de grabar. Lógica en `CuadresMayor.ProponerReinicio` y `SesionMayor.ProponerReinicioCuadreAsync` / `GuardarReinicioCuadreAsync` (`src/ProConta.Contabilidad/Mayor`); pantalla en `src/ProConta.Contabilidad.UI/ReinicioCuadre`.
**Aceptación:** en una copia de pruebas, la pantalla enseña las subcuentas que se marcan, se desmarcan o se actualizan; al reiniciar, el Mayor muestra en verde las de saldo dentro del umbral, sin la casilla las demás y con sus notas intactas; repetirlo sin cambios no propone nada.

## Decisions
- 2026-11-12 10:15 · Creado como borrador desde contabilidad/mayor (hilo «Terminar el Mayor reutilizable»), a petición del propietario.
- 2026-11-17 19:20 · Comportamiento (propietario 2026-11-17 19:17, opción «como el antiguo, con umbral»): sin opción de «quitar antes las casillas»; reiniciar siempre deja cuadradas las subcuentas del ejercicio de trabajo con movimientos y saldo de hasta ±umbral y sin la casilla todas las demás. Las notas se conservan (las heredadas del ejercicio anterior se copian al de trabajo al marcar). Una subcuenta ya cuadrada con el mismo saldo y dentro del umbral no se toca; si su saldo cambió sin salir del umbral, se vuelve a marcar con el actual («Se actualiza»).
- 2026-11-17 19:20 · Pantalla Contabilidad › «Reiniciar el cuadre del Mayor», hija de Contabilidad tras «Reparto de dividendos» y antes de «Fin de ejercicio» (no es un paso del cierre). Cabecera: ejercicio de trabajo, umbral vigente, «Cambiar el umbral…» (abre Configuración general) y «Actualizar» (F5). Cuerpo: tabla de las subcuentas que cambian (subcuenta, nombre, saldo, cambio y si tiene notas) con el recuento; clic derecho, Mayor de la subcuenta. Pie: «Reiniciar el cuadre», que pide confirmación y graba exactamente lo de la tabla en una transacción; después se refresca el Mayor abierto.
- 2026-11-17 19:20 · Sin asientos, sin lote, sin deshacer, sin cambio de esquema y sin permiso propio (como marcar el cuadre en el Mayor). Se quita `SesionMayor.ReiniciarCuadreAsync` y `CuadresMayor.Reiniciar`, que nadie usaba, en favor de la propuesta y su grabación.

## Requirements
- 2026-11-12 09:57 · Reiniciar cuadre, viendo que afecta a todos las subcuentas, irá en procesos contables, por lo que lo quitamos del mayor.
- 2026-11-12 10:14 · Ok a lo de apuntarlo en procesos.
- 2026-11-12 10:15 · draft: Reiniciar el cuadre del Mayor desde los procesos contables: marcar como cuadradas las subcuentas del ejercicio de trabajo con saldo dentro del umbral y, si se pide, quitar antes la casilla de todas, sin borrar notas.
- 2026-11-17 19:17 · Se desmarca el flag de los no cuadrados y se marca los que cuadran con un pequeño margen de error.

## Hand-over
2026-11-17 20:10 · Hecho y fusionado (PR #111): lógica, pantalla, menú y pruebas sin BD (`MayorCuadreTests`, pasan); la solución compila. Falta, junto con otros hilos: las pruebas contra Access y probar la pantalla con una copia de C:\Docs\ProContaVF-validacion; cuando el propietario lo valide, el nodo deja de ser borrador.
