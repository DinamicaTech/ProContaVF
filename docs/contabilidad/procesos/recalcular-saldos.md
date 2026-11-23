---
title: Recalcular saldos
depends_on: [contabilidad/procesos, contabilidad/asientos]
threads:
  - 2026-11-17 19:20 · Recalcular los saldos de subcuentas | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GAhSzqw4LwtCFPHkPSp8Bn5
---
## Summary
Pantalla Contabilidad › Recalcular saldos: compara, sin grabar, la caché de saldos de las cuentas del ejercicio de trabajo (CuentasContables.SaldoActual y NumeroApuntes) con sus apuntes, lista las que no coinciden y avisa de los códigos con apuntes que no están en el plan del ejercicio; «Recalcular» rehace la caché de todas con `IMotorAsientos.RecalcularSaldosAsync`. Es el punto de sincronismo si alguien ha tocado la BD por fuera. No genera asientos, no pide permiso propio y funciona también en un ejercicio cerrado. Solo lee la caché el conector de Claude; el Mayor, los balances y los procesos calculan desde los apuntes. Hecho y validado (PR #110).
**Aceptación:** tras descuadrar a mano el saldo de una subcuenta en una copia, la pantalla la muestra con su diferencia y, tras «Recalcular», queda igual a la suma de sus apuntes.

## Decisions
- 2026-11-13 12:05 · [replaced by 2026-11-17 19:20] Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Visible en la pantalla de procesos, no oculto (propietario 2026-11-13 12:00): no puede hacer daño. Sustituye a «Regenera saldos» del sistema antiguo, que el reparto inicial mandaba a contabilidad/centros-coste.
- 2026-11-17 19:20 · Pantalla y comportamiento (propietario 2026-11-17 19:11, puntos 1 a 6 del desglose): (1) opción Contabilidad › Recalcular saldos en el menú, detrás del submenú «Fin de ejercicio» y antes de «Asiento manual» (no es un proceso de fin de año); (2) al abrirla compara sin grabar y lista solo las cuentas del ejercicio de trabajo cuya caché no coincide: código, nombre, saldo guardado, saldo según los apuntes (SaldoInicial + Debe − Haber), diferencia y número de apuntes, con el resumen «N cuentas revisadas, M con diferencias»; (3) avisa aparte de los códigos con apuntes en el ejercicio que no están en su plan de cuentas, con su saldo y número de apuntes, y dice cómo arreglarlo (dar de alta la subcuenta); (4) el botón de cabecera «Recalcular» rehace la caché de todas las cuentas del ejercicio y vuelve a comparar; funciona también con el ejercicio cerrado porque no toca asientos; (5) sin permiso propio, sin cambio de esquema y sin asientos; (6) una sola rutina de recálculo, la del motor (`IMotorAsientos.RecalcularSaldosAsync`, por tandas de 150 cuentas): se quita `ICatalogoCuentas.ReconstruirSaldosAsync`, que hacía lo mismo en una sola transacción. El menú contextual de una fila abre el Mayor de la subcuenta. No sigue el marco propuesta → visor de asientos de los procesos porque no genera asientos.
- 2026-11-17 19:20 · Código: `ComparacionSaldos` (sin BD) y `ServicioRecalcularSaldos` en src/ProConta.Contabilidad/Saldos; pantalla en src/ProConta.Contabilidad.UI/Saldos. Los códigos se comparan sin espacios a los lados y, si se repiten, se suman. Pruebas: `RecalcularSaldosTests` (sin BD) y `CuentasAccessTests.RecalcularSaldos_muestra_la_diferencia_y_deja_la_cache_igual_a_los_apuntes` (Access).
- 2026-11-23 14:20 · Validado por el propietario: el nodo deja de ser borrador.

## Requirements
- 2026-11-13 12:00 · 12. Ok, sí, al menos tener un punto de sincronismo. No hace falta que esté oculto, tampoco es un proceso que pueda generar 'daños'
- 2026-11-13 12:05 · draft: Recalcular desde los apuntes la caché de saldos de las subcuentas del ejercicio, desde la pantalla de procesos contables.
- 2026-11-17 19:11 · 1- Ok 2- Ok 3- Ok 4- Ok 5- Ok 6- Ok

## Hand-over
- 2026-11-17 20:10 · Fusionado en main (PR #110) a petición del propietario (2026-11-17 20:04), sin pruebas en su equipo: él probará a la vez varios hilos. Queda en borrador hasta que lo valide. Para probarlo: prueba Access `CuentasAccessTests.RecalcularSaldos_muestra_la_diferencia_y_deja_la_cache_igual_a_los_apuntes` y, en una copia de C:\Docs\ProContaVF-validacion, descuadrar a mano el SaldoActual de una subcuenta, abrir Contabilidad › Recalcular saldos, ver la diferencia y pulsar «Recalcular».
- 2026-11-23 14:20 · Validado por el propietario; nodo estable. No queda nada pendiente.
