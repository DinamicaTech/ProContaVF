---
title: Motor de asientos
depends_on: [contabilidad/cuenta-contable, contabilidad/centros-coste]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:15 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Motor común que valida y guarda asientos como agregado atómico con cuadre, auditoría y protección de ejercicios cerrados; centraliza toda modificación de apuntes con un registro de cambios agrupado por IdLote; lo usan directamente ventas, compras, nóminas y la conciliación bancaria. Contrato `IMotorAsientos` (src/ProConta.Contabilidad/Asientos): obtener, guardar y borrar asientos manuales, reemplazar y borrar los asientos de un lote automático, cambiar de cuenta varios apuntes y consultar el registro de cambios; `ReglasAsiento` y `PoliticaCuadre` validan y cuadran sin BD.
**Aceptación:** asiento desequilibrado no persiste; un automático rechaza edición manual.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 10:20 · Toda modificación de apuntes (desde el Mayor, al modificar un asiento o en cambios de cuenta masivos) pasa por este motor, que guarda un registro de cambios con el valor anterior y el nuevo; un IdLote vincula los cambios de varios apuntes hechos en una misma operación.
- 2026-10-31 11:20 · Referencia funcional de la rectificación de asientos: la rutina común que usaban el Mayor y los asientos manuales de ProConta antiguo, en `sources/proconta-antiguo/asientos-tipo/ANALISIS-FUNCIONAL.md` § 4 (fecha, ejercicio de destino, punteo, motivo como nota y en el registro de cambios); la ofrece contabilidad/plantillas-asiento sobre este motor, y sus defectos (§ 6) no se reproducen.
- 2026-10-31 19:15 · Agregado: cabecera y apuntes se validan y guardan en una transacción. Reglas: descripción obligatoria (hasta 255), fecha dentro del año del ejercicio, al menos dos apuntes, cada apunte con cuenta e importe positivo en un solo lado; cada cuenta se resuelve por código exacto en el ejercicio y debe admitir apuntes; el centro de coste, si lo hay, debe ser de la sociedad, activo e imputable. El ejercicio cerrado se comprueba dentro de la misma transacción.
- 2026-10-31 19:15 · Cuadre: por defecto Debe y Haber deben coincidir al céntimo. Solo con una `PoliticaCuadre` configurada (tolerancia de hasta 1 € y cuentas de gastos e ingresos de redondeo) se añade un apunte visible de tipo AjusteRedondeo con su motivo; nunca una cuenta puente ni un ajuste sin configurar.
- 2026-10-31 19:15 · Asientos automáticos: llevan la identidad de su lote (motor propietario, proceso y clave de ejecución, columnas nuevas de Asientos). `ReemplazarLoteAsync` borra los del lote y guarda los nuevos en una transacción (regenerar no duplica; si uno falla, no se guarda ninguno y queda lo anterior). La vía manual (guardar, borrar, cambiar cuenta) rechaza los automáticos.
- 2026-10-31 19:15 · Auditoría: cada alta, modificación o lote escribe una revisión en RevisionesAsientos con la instantánea JSON anterior y la resultante; modificar o borrar exige motivo. Cada cambio de apunte (ALTA, BAJA o MODIFICACION de cada campo, comparando por orden) va a la tabla nueva RegistroCambiosApuntes con valor anterior y nuevo, operador y motivo, y un IdLote común a toda la operación (por ejemplo, un cambio de cuenta de varios apuntes). Al borrar un asiento se borran sus revisiones y relaciones, y la traza queda en RegistroCambiosApuntes.
- 2026-10-31 19:15 · Concurrencia optimista con Asientos.Version. La caché de saldos (SaldoActual y NumeroApuntes) de las cuentas afectadas se recalcula en la misma transacción. EstadoAsiento se guarda siempre como contabilizado: no hay borradores descuadrados.
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).
- 2026-11-07 00:19 · El motor admite un trabajo del área dueña del lote dentro de la misma transacción (TrabajoLote: antes de borrar los asientos del lote y después de grabar los nuevos), para que compras grabe el libro de IVA y la marca de la factura con el asiento; si ese trabajo falla, no se guarda nada.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-ASI-01..03: agregado atómico, cuadre, automáticos regenerables por su lote
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Asientos/ (código en el disco del propietario): motor anterior
- 2026-10-28 12:28 · draft: Implementar el motor de asientos y publicar su contrato para las áreas que generan asientos.
- 2026-10-29 10:12 · Sí, de hecho la modificación de apuntes se centralizará en la clase que gestiona apuntes (creo que no hay un nodo específico para ella, lo crearé si es el caso) que mantendrá el log de cambios con un IdLote para vincular cambios de varios apuntes (por ejemplo, al modificar un asiento o con cambios de cuenta masivos)
- 2026-10-31 11:20 · Derived from contabilidad/plantillas-asiento: grabar los asientos que proponen las plantillas (anular con su IVA, cambiar de cuenta, contra-apunte, ajustes) con su motivo en el registro de cambios por IdLote, vinculado al asiento original.
- 2026-10-31 17:53 · Derived from architecture: el motor de asientos con su contrato (agregado atómico con cuadre, ejercicio cerrado, automáticos por lote, registro de cambios por IdLote) para las áreas que generan asientos.

## Hand-over
- 2026-10-31 19:15 · Hecho: motor, migración y 10 pruebas (reglas y cuadre sin BD; desequilibrado no persiste, alta y modificación con motivo, revisión, registro y saldos, conflicto de versión, ejercicio cerrado, cuentas y centros no válidos, lote regenerado sin duplicar y atómico, automático rechaza edición manual, cambio de cuenta masivo con un IdLote, borrado). Pendiente: anulación y rectificación (las ofrecerá contabilidad/plantillas-asiento sobre este motor), relaciones entre asientos (RelacionesAsientos), periodos protegidos además del ejercicio cerrado, la vía interna de cierre y reapertura (contabilidad/procesos) y la consulta del mayor (contabilidad/mayor).
