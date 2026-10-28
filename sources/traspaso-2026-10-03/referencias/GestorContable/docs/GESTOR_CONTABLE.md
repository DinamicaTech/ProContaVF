# Gestor Contable

El Gestor coordina los contratos públicos de Asientos, Cuentas y Seguridad. No accede a las tablas de esos motores salvo mediante sus interfaces o servicios publicados.

## Procesos y permisos

Los códigos estables son `IVA.LIQUIDAR`, `IVA.REGULARIZAR_PRORRATA`, `IS.REGISTRAR`, `EJERCICIO.CERRAR` y `EJERCICIO.REABRIR`. Seguridad los presenta como filas de una matriz cuyas columnas son los grupos de operadores. Aplica una política permisiva: una casilla sin restricción guardada está habilitada y cualquier denegación explícita de uno de los grupos del operador prevalece.

## Lotes regenerables

Todos los asientos creados usan `GESTOR_CONTABLE` como motor propietario y una clave estable de período o ejercicio. Una nueva ejecución elimina y vuelve a crear exclusivamente ese lote. Los editores manuales rechazan estos asientos. El mayor publica los metadatos de propiedad para que las propuestas excluyan los lotes del propio Gestor y nunca se alimenten de una ejecución anterior.

Antes de escribir, el Gestor abre una previsualización modal reutilizando el visor común de Asientos. Cuando una operación genera varios asientos, como el cierre, se muestra una pestaña para cada uno. La confirmación aplica el mismo plan conservado en memoria y no vuelve a calcularlo; cancelar descarta la propuesta sin cambios. La reapertura no crea asientos y muestra únicamente el alcance de los lotes automáticos que eliminará.

## IVA y prorrata

La liquidación parte del mayor de las cuentas de IVA configuradas y exige que todo el período pertenezca al ejercicio. La clasificación se conserva por sociedad, ejercicio y centro de coste: `0` deducible al 100 %, `1` sujeto a la prorrata provisional única del ejercicio y `2` no deducible. La ausencia de una asignación explícita se considera deducción total. Los cálculos y la regularización definitiva no modifican la prorrata provisional almacenada para ese ejercicio.

La propuesta de prorrata definitiva divide los ingresos configurados con derecho a deducción entre los ingresos totales configurados y redondea el porcentaje hacia arriba a un entero. El operador confirma o modifica ese porcentaje antes de generar la corrección. La regularización específica plurianual de bienes de inversión queda fuera de esta versión.

## Impuesto sobre Sociedades

La aplicación puede proponer una cuota a partir del resultado de las cuentas 6 y 7 y del tipo anual. La cifra que se contabiliza es siempre el importe que el operador haya confirmado o escrito manualmente.

## Cierre y reapertura

El cierre genera regularización, cierre y apertura del ejercicio siguiente dentro de una única llamada transaccional de Asientos; al final marca `SociedadesEjercicios.EjercicioCerrado`. La reapertura elimina esos tres lotes automáticos y retira el indicador dentro de la misma transacción. Los asientos manuales y los lotes de otros procesos no se eliminan.

## Configuración

La configuración central publica dos mantenimientos en la categoría Contabilidad: parámetros anuales del Gestor y tratamiento de IVA por centro. Los códigos de cuenta deben existir, estar activos, no bloqueados y ser imputables en el ejercicio que recibe cada asiento. Para cerrar es obligatorio que exista el ejercicio siguiente y que sus cuentas de apertura estén preparadas.

Las pantallas usan controles interactivos Krypton. Los `TableLayoutPanel` y `FlowLayoutPanel` empleados son exclusivamente contenedores de distribución sin equivalente Krypton y no introducen apariencia ni comportamiento de negocio propios.
