---
title: Sincronización con ContaSol
status: idea
depends_on: [implantacion/importador-contasol, contabilidad/asientos]
threads:
  - 2026-11-13 11:40 · Valorar sincronización con ContaSol | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GJHcdShM1PjQsJavgQf28rc
---
## Summary
Durante la transición, mantener ContaSol al día y en modo solo lectura, para que la gestoría siga haciendo sus consultas e informes allí hasta que confíe en ProContaVF. Sería en un solo sentido: lo que se crea, modifica o borra en ProContaVF (apuntes, subcuentas, libros de IVA…) se escribe en las BD de ContaSol (una por ejercicio), como ya hacía ProConta antiguo con las tablas adjuntas. Se puede desactivar en un momento dado y, una vez desactivada, no se puede reactivar si ya hay cambios contables en ProContaVF.
**Valoración (2026-11-13):** viable, porque va en un solo sentido y ContaSol queda solo de lectura. Se apoya en lo que ya existe: la correspondencia entre modelos del importador (recorrida al revés), los mapas de identidad «DIA:ASI» por ejercicio, el registro de cambios de apuntes por IdLote y la actualización masiva de F_SAL de ProContaEstructura.accdb. Riesgos: bloqueos si ContaSol tiene el fichero abierto, cambios de esquema en futuras versiones de ContaSol y la información de ProContaVF que ContaSol no puede guardar (centros de más de dos niveles, terceros separados de la cuenta, datos de VeriFactu).

## Decisions
- 2026-11-13 11:40 · Creado como idea en el laboratorio, a petición del propietario, a raíz de la conciliación N43 (no está previsto exportar nada a ContaSol).
- 2026-11-13 11:45 · [replaced by 2026-11-13 11:41] Respuestas del propietario: volcado a petición por ejercicio (no incremental), que vacía y vuelve a llenar en la BD de ContaSol de ese ejercicio F_APU, F_OAS, F_MAE, F_IVS, F_IVR, F_SAL, F_CLI, F_PRO, F_DEP y F_CDP (sin terceros ni centros quedaría inconsistente) y no toca el resto de tablas de ContaSol; la BD de un ejercicio nuevo la crea el propietario desde ContaSol y ProContaVF la rellena; el solo lectura se garantiza por acuerdo con la gestoría (ya es así: solo el propietario modifica la contabilidad). Pendiente de decidir: los clientes y proveedores sin actividad en cinco años que no se importaron a ProContaVF desaparecerían de ContaSol con el volcado.
- 2026-11-13 11:41 · Volcado a petición por ejercicio (no incremental) a la BD de ContaSol de ese ejercicio: vacía y vuelve a llenar F_APU, F_OAS, F_MAE, F_IVS, F_IVR, F_SAL, F_DEP y F_CDP; en F_CLI y F_PRO añade y actualiza, pero no borra los clientes y proveedores que no están en ProContaVF (los que no se importaron por no tener actividad en cinco años); no toca el resto de tablas de ContaSol (empresa, configuración). La BD de un ejercicio nuevo la crea el propietario desde ContaSol y ProContaVF la rellena. El solo lectura de ContaSol se garantiza por acuerdo con la gestoría (ya es así: solo el propietario modifica la contabilidad).
- 2026-11-13 11:40 · Alcance acordado en el debate: solo de ProContaVF a ContaSol; desde la puesta en producción, ContaSol solo se usa para leer y ProContaVF es el único sistema en el que se escribe. El ERP no entra: se migra y deja de usarse. F_SAL no se recalcula en cada asiento, sino a petición (salvo que se afine para actualizar solo los registros afectados).

## Debate
- **Correspondencia de modelos.** Es la del importador al revés (implantacion/importador-contasol): asiento ↔ F_APU + F_OAS con la clave «DIA:ASI» de MapasIdentidadContaSol; cuenta ↔ F_MAE; registros de IVA ↔ F_IVS/F_IVR enlazados por CIVAPU/TIVAPU; centros ↔ F_DEP/F_CDP («DEP/SUB»); terceros ↔ F_CLI/F_PRO. Un asiento nuevo de ProContaVF necesita un número de ContaSol libre en su ejercicio (1 = apertura, 10000 = regularización y 20000 = cierre están reservados) y un registro en el mapa.
- **Qué cambió.** El registro de cambios de apuntes (contabilidad/asientos, IdLote) dice qué asientos se crearon, modificaron o borraron; para cuentas, terceros e IVA hace falta una marca equivalente o comparar con el mapa.
- **Dos formas de hacerlo.** (a) Incremental: cada cambio se escribe en ContaSol al grabarlo, o en una cola que se vacía a petición. (b) Volcado por ejercicio: a petición se reescriben las tablas del ejercicio en ContaSol desde ProContaVF; es repetible, no necesita guardar estado y es más sencillo, pero tarda más por ejecución. En ambos casos F_SAL se recalcula al final, a petición.
- **Desactivación.** Un parámetro general (activo/inactivo) con fecha; si hay cambios contables posteriores a la desactivación, no se puede reactivar (ContaSol ya no estaría al día).
- **Coste estimado.** El volcado por ejercicio (elegido), 2 o 3 PR; el incremental, bastante más (cola, reintentos, conflictos de bloqueo).

## Requirements
- 2026-11-13 11:16 · idea: Valora la posibilidad de sincronizar la base de datos de ContaSol con ProcontaVF. La idea sería actualizar en ContaSol los apuntes, subcuentas, libro de IVA, etc. que se generen, borren o modifiquen desde ProcontaVF. ProcontEstructuda.accdb es lo que hace, lo único que no tiene un modelo de datos intermedio, usa directamente el de ContaSol (tablas adjuntas) como modelo de trabajo.
- 2026-11-13 11:34 · Sí, la idea es que la gestoría pueda usar ContaSol para sus consultas e informes hasta que se haya acostumbrado (y se fíe) de ProContaVF.
- 2026-11-13 11:34 · Sería unidireccional ya que en el momento de poner ProContVF en producción, ContaSol sería solo de lectura.
- 2026-11-13 11:34 · La actualización masiva de F_SAL lo tienes en ProContaEstructura.accdb, lo único que habría que lanzarla a petición ya que sería costoso actualizarlo con cada escritura de asiento (a no ser que hilemos fino y se actualicen solo los registros afectados)
- 2026-11-13 11:34 · Debería poderse desactivar la sincronización a partir de un momento dado (ya no se podría reactivar si se ha hecho algún cambio contable en ProcontaVF)
- 2026-11-13 11:40 · [answered 2026-11-13 11:39] question: ¿Incremental o volcado por ejercicio? A) volcado a petición de un ejercicio entero (más simple y repetible; recomendado); B) incremental con cola que se vacía a petición junto con F_SAL; C) incremental al grabar cada asiento. Recomendación: A, y pasar a B solo si el volcado resulta lento.
- 2026-11-13 11:40 · [answered 2026-11-13 11:39] question: ¿Qué tablas se mantienen? A) F_APU, F_OAS, F_MAE, F_IVS, F_IVR y F_SAL (lo que consulta la gestoría; recomendado); B) además F_CLI/F_PRO y F_DEP/F_CDP.
- 2026-11-13 11:40 · [answered 2026-11-13 11:39] question: Ejercicio nuevo (2027): ContaSol necesita su BD del ejercicio. A) la crea el propietario desde ContaSol y ProContaVF la rellena (recomendado); B) ProContaVF la crea copiando la estructura del ejercicio anterior.
- 2026-11-13 11:40 · [answered 2026-11-13 11:39] question: ¿Cómo se garantiza que ContaSol es solo de lectura? A) por acuerdo con la gestoría (sin control técnico); B) usuarios de ContaSol con permiso de solo consulta (recomendado, si ContaSol lo permite); C) el volcado detecta y avisa de cambios hechos en ContaSol.
- 2026-11-13 11:39 · 1. A) (entiendo que sería como regenerar toda la BD de ContaSol desde cero y volcar la de ProContaVF, no?)
- 2026-11-13 11:39 · 2. B (sin F_CLI, F_PRO, F_DEP y F_CDP quedaría inconsistente)
- 2026-11-13 11:39 · 3. A
- 2026-11-13 11:39 · 4. A (de hecho, es como se está trabajando actualmente, solo yo modifico la contabilidad, la gestoría solo consulta)
- 2026-11-13 11:45 · answer: ¿Incremental o volcado? → A, volcado a petición de un ejercicio entero.
- 2026-11-13 11:45 · answer: ¿Qué tablas? → B, también F_CLI, F_PRO, F_DEP y F_CDP.
- 2026-11-13 11:45 · answer: Ejercicio nuevo → A, la crea el propietario desde ContaSol y ProContaVF la rellena.
- 2026-11-13 11:45 · answer: Solo lectura → A, por acuerdo con la gestoría.
- 2026-11-13 11:45 · [answered 2026-11-13 11:40] question: Clientes y proveedores de ContaSol sin actividad en cinco años (no se importaron a ProContaVF): A) el volcado no borra de F_CLI/F_PRO los que no están en ProContaVF, solo añade y actualiza (recomendado: ContaSol conserva su ficha y no se pierde nada); B) se borran para que ContaSol sea copia exacta de ProContaVF.
- 2026-11-13 11:40 · A
- 2026-11-13 11:41 · answer: Clientes y proveedores sin actividad → A, el volcado no los borra de ContaSol.
