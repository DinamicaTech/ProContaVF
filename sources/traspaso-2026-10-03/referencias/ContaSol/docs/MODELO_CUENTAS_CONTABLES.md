# Motor de cuentas contables

## Decisiones de identidad

`IdentidadesContables` conserva la continuidad funcional de una cuenta entre
ejercicios. Es una pieza técnica que no se muestra al operador.

`CuentasContables` es la cuenta que el operador utiliza: pertenece a un
ejercicio y contiene código, nombre, descripción, padre, estados y estadísticas.
Una identidad puede tener varias cuentas, incluso dentro del mismo ejercicio,
pero cada código es único dentro de éste.

Ejemplo:

| Identidad | Ejercicio | Código | Nombre |
|---:|---:|---|---|
| 875 | 2025 | 4000001 | Proveedor Hermanos Pérez |
| 875 | 2026 | 4100001 | Acreedor Hermanos Pérez, S.L. |

Los apuntes referenciarán únicamente `IdCuentaContable`. No conservarán una
copia redundante del código o del nombre.

La tabla plana anterior (`Codigo`, `Descripcion`) no contiene sociedad ni
ejercicio, por lo que no puede asignarse automáticamente sin inventar su
propietario. La migración la conserva íntegra como `CuentasContablesLegacy`
antes de crear el nuevo maestro. Su reconciliación se realizará junto con la
migración de `F_MAE`, cuando sí exista contexto de sociedad.

## Reserva histórica

`CodigosContablesReservados` garantiza que un código utilizado por una
identidad no pueda asignarse posteriormente a otra identidad de la misma
sociedad. La misma identidad sí puede reutilizar su código en ejercicios
sucesivos.

Un cambio de código dentro de un ejercicio actualiza la única fila de
`CuentasContables`; las claves externas permanecen estables. Tanto el código
anterior como el nuevo quedan reservados para la identidad.

## Jerarquía y uso

`IdCuentaPadre` pertenece a `CuentasContables`, porque la jerarquía puede cambiar
entre ejercicios. El código hijo debe comenzar por el código del padre, pero no
es necesario materializar todos los prefijos intermedios.

`PermiteMovimientos` es explícito: una cuenta puede conservar movimientos y
tener hijos posteriores. `Bloqueada` impide nuevos apuntes; `Activa` gobierna su
visibilidad ordinaria y produce una advertencia, no una pérdida histórica.

## Estadísticas reconstruibles

Cada cuenta conserva como caché:

- `SaldoInicial`: asiento de apertura contabilizado.
- `SaldoActual`: apertura, ordinarios y regularización; excluye cierre.
- `NumeroApuntes`: apuntes contabilizados, excluido el asiento de cierre.

El asiento es siempre la fuente de verdad. El repositorio ofrece
`RecalcularEstadisticas`, pensado para importaciones y reparaciones.

## Catálogos y operaciones posteriores

`PlanesContables` y `CuentasPlanContable` son plantillas para crear sociedades;
no gobiernan el plan personalizado después de copiarlo.

`TiposCuentaContable` aporta prefijo, longitud y naturaleza predeterminados al
asistente. `CompatibilidadesFusionCuenta` limita las fusiones admitidas y
`FusionesIdentidadesContables` deja trazabilidad de cada absorción.

`RevisionesCuentasContables` registra conciliación contable por cuenta y fecha
de corte, incluyendo estado, comentarios, saldo, operador y momento de revisión.

## Interfaz de búsqueda y alta

La cinta principal incorpora **Cuentas contables** dentro de Maestros. El
selector trabaja con el ejercicio activo y ofrece:

- búsqueda incremental por código, nombre y descripción;
- filtros para cuentas imputables, inactivas y bloqueadas;
- saldo actual y número de apuntes en el propio listado;
- selección por doble clic;
- alta de una subcuenta sin abandonar el proceso invocador.

El asistente de alta permite elegir tipo y cuenta padre, propone el siguiente
código histórico libre y deja editar código, nombre, descripción e
imputabilidad antes de crear. La propuesta no reserva el número: la restricción
única de Access vuelve a comprobarlo dentro de la transacción de alta.

## Tipos de asiento

El catálogo estable contiene `ORDINARIO`, `APERTURA`, `REGULARIZACION` y
`CIERRE`. Durante la migración de ContaSol se interpreta `F_APU.ASIAPU` así:

| ASIAPU | Tipo |
|---:|---|
| 1 | Apertura |
| 10000 | Regularización |
| 20000 | Cierre |
| cualquier otro | Ordinario |
