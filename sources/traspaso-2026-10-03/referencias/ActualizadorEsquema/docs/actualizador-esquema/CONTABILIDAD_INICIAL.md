# Bloque contable de la maestra nueva

`20260928_003_CONTABILIDAD` añade el esquema de cuentas y diario sobre las migraciones técnica y de identidad. Toma como referencia `MigracionCuentasContables` y `MigracionEvolucionCuentasContables` del worktree de CuentasContables, y `MigracionNucleoContable` y `MigracionMotorAsientosAutomaticos` del worktree de Asientos. Es un manifiesto de **creación nueva**; no se aplica a la base de pruebas con datos. No reescribe las migraciones de esos módulos ni su historia.

## Identidad y códigos

`IdentidadesContables` agrupa la continuidad de una cuenta entre ejercicios de una sociedad permanente (`IdSociedad TEXT(3)`). `CuentasContables` contiene la cuenta concreta de un `IdSociedadEjercicio LONG`. Tiene índices únicos por `(IdSociedadEjercicio, Codigo)` y `(IdIdentidadContable, IdSociedadEjercicio)`. `CodigosContablesReservados` impide reutilizar para otra identidad un código histórico dentro de la sociedad. Planes y tipos son catálogos técnicos; la maestra nueva siembra siete tipos y el plan base PGC Pymes con siete grupos. No siembra cuentas de ninguna empresa.

Todos los campos que **contienen códigos finales de cuenta** en este bloque son `TEXT(10)`: `CuentasPlanContable.Codigo`, `CuentasContables.Codigo`, `CodigosContablesReservados.Codigo`, `TiposCuentaContable.Prefijo` y `Apuntes.CuentaContable`. `PlanesContables.Codigo` y `TiposCuentaContable.Codigo` identifican catálogos y mantienen `TEXT(30)`. Los ID de cuenta e identidad permanecen `LONG`. La instantánea `Apuntes.CuentaContable` debe concordar con `Apuntes.IdCuentaContable`, la identidad y el ejercicio del asiento; las FKs individuales no expresan esta igualdad entre tablas y la validarán el servicio y los importadores. Ninguna carga puede truncar códigos de más de diez caracteres.

## Diario y relaciones pendientes

`Asientos` incorpora desde su creación los campos de propiedad automática (`EsAutomatico`, `MotorPropietario`, `CodigoProceso`, `ClaveEjecucion`). `Apuntes` incorpora `GeneradoAutomaticamente`, `IdCuentaContable` e `IdIdentidadContable`. Los asientos y apuntes empiezan vacíos. `AuditoriaLotesAsientos` conserva evidencia independiente, sin inventar lotes. `SociedadesEjercicios.EjercicioCerrado` ya estaba en el manifiesto anterior.

ACE impone las FKs de identidad, ejercicio, cuenta, asiento, plan, reservas y auditoría cuando ambas tablas ya existen. El manifiesto 004 añadió las FKs de `Apuntes.IdCentroCoste` y `CuentasContables.IdCentroCostePredeterminado`. El 005 añadió la de `Apuntes.IdCliente`. Quedan pendientes `Apuntes.IdProveedor` y `Asientos.IdLoteProceso` hasta crear sus tablas padre. Las FKs por sí solas tampoco garantizan que una identidad y una cuenta pertenezcan a la misma sociedad o ejercicio; el servicio de cuentas y la importación deben comprobarlo en una transacción.

La prueba automatizada crea una ACCDB descartable, comprueba las cinco longitudes `TEXT(10)`, los catálogos sembrados, la unicidad de cuenta por ejercicio y el rechazo de un apunte huérfano. El creador verifica recuentos y ausencia de datos empresariales antes de publicar el archivo nuevo. Las bases originales no se abren ni se modifican durante esta creación.
