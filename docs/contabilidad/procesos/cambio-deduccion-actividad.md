---
title: Cambio de deducción de una actividad desde una fecha
depends_on: [contabilidad/procesos, contabilidad/procesos/prorrata, contabilidad/modelos-tributarios, informes/informe-gestoria, compras/factura-compra/contabilizacion, pantalla-de-inicio, datos/esquema]
threads:
  - 2026-11-15 19:36 · Mejorar el informe trimestral para la gestoría | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GDKdsJYPwYjUXMKLnUhFQKN
  - 2026-11-15 19:56 · Deducir el IVA del turístico desde el 1/12 | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GKmgLfNWCToTD6PKQqXqGVC
---
## Summary
La deducción de una actividad de IVA puede cambiar a partir de una fecha, y cada línea de IVA se clasifica con la deducción vigente en la fecha de su factura. Caso que lo origina: desde el 1/12/2026 el alquiler turístico lleva IVA al 10 % y sus compras con fecha de factura desde ese día pasan a ser 100 % deducibles; las anteriores siguen sin deducir. El tipo de IVA de las ventas se cambia a mano y no se modeliza aquí.
Hecho y validado por el propietario el 2026-11-15: esquema 55 con la tabla ActividadesDeduccion; Catálogos › «Cambios de deducción de las actividades» y el calendario en la ficha de Actividades (IVA); la propuesta de asiento de compras, el 303, la prorrata, el informe para la gestoría y la estimación de IVA de Inicio usan la deducción vigente en la fecha de la factura (en Inicio, la del apunte). Para usarlo hay que dar de alta el cambio del turístico (desde el 01/12/2026, 100 % deducible).
**Aceptación:** con la actividad en «no deducible hasta el 30/11/2026 y 100 % deducible desde el 01/12/2026», el 303 del 4T deduce entera la cuota de diciembre de esa actividad y nada de octubre y noviembre; las bases repercutidas de diciembre cuentan con derecho a deducción en la prorrata real; las compras comunes siguen en prorrata y los bienes de inversión anteriores no se regularizan.

## Decisions
- 2026-11-15 19:36 · Creado como borrador desde informes/informe-gestoria (hilo «Mejorar el informe trimestral para la gestoría»), aceptado por el propietario al validar la PR #79 (19:43). Propuesta recomendada: un cambio de deducción con fecha de efecto en la actividad, en lugar de una actividad nueva con la asignación de centros fechada (más trabajo). Lo usarán LectorActividadesIva, la clasificación de las líneas de IVA del informe para la gestoría y la prorrata.
- 2026-11-15 20:40 · Implementado como se acordó con el propietario (19:56): tabla ActividadesDeduccion (migración 20261115_2000_DEDUCCION_ACTIVIDAD_FECHAS, esquema 55), con Actividades.Deducible como la deducción anterior al primer cambio. La fecha que decide es la de expedición de la factura del libro de IVA (en la propuesta de compras, la fecha de la factura); si falta, la contable. El trimestre de cada línea sigue siendo el de la fecha contable. No se recalculan las facturas ya contabilizadas: una compra de fecha posterior al cambio contabilizada antes de darlo de alta se descontabiliza y se vuelve a contabilizar. Pantalla: lista aparte en Catálogos porque la pantalla genérica no admite tablas dentro de una ficha.
- 2026-11-15 20:40 · Cómo comprobarlo: pruebas sin BD `DeduccionPorFechasTests` (caso de aceptación del 4T, prorrata y fecha de factura frente a la del asiento) y `CalculoInicioTests`; contra Access, `CambiosDeduccionAccessTests` y `ContabilizacionComprasAccessTests` (la propuesta deduce o no según la fecha de la factura).
- 2026-11-15 20:58 · Validado por el propietario: el nodo pasa a estable y se fusiona la PR #80.

## Requirements
- 2026-11-15 19:24 · Cómo gestionamos que a partir del 1/12/2026, el alquiler de pisos lleva 10% de IVA y por tanto, pasa a ser 100% deducible para facturas de compra relacionadas a esa actividad a partir de esa fecha ?
- 2026-11-15 19:36 · Sí, me temo que en algún sitio habrá que modelizar la fecha de corte, podían haber esperado un mes para aplicarlo y no complicarnos la vida a todos.
- 2026-11-15 19:36 · Mi idea es modificar el tipo de IVA de venta manualmente el 1/12/2026 al 10%, creo que no necesitamos tenerlo modelizado. Con la prorrata por actividad no puedo hacer lo mismo ya que de alguna forma necesitas poder discriminar por fechas.
- 2026-11-15 19:36 · Las compras comunes van a prorrata como hasta ahora.
- 2026-11-15 19:36 · Y los bienes de inversión, al ser anteriores a esta fecha, tampoco creo que deban modificarse
- 2026-11-15 19:36 · draft: Permitir que la deducción de una actividad de IVA cambie desde una fecha (alquiler de pisos al 10 % y 100 % deducible desde el 1/12/2026), clasificando cada línea de IVA por su fecha en el 303, la prorrata y el informe para la gestoría.
- 2026-11-15 19:36 · [answered 2026-11-15 19:43] question: ¿Qué actividad cambia el 1/12/2026? (a) Alquiler turístico (b) Alquiler Viviendas (c) las dos. Sin recomendación: depende de a qué alquileres se aplica el 10 %.
- 2026-11-15 19:36 · [answered 2026-11-15 19:43] question: ¿Qué fecha decide la deducción de una compra de finales de noviembre contabilizada en diciembre? (a) la de la factura (fecha de operación o expedición) (b) la fecha contable del asiento. Recomendada (a): el derecho a deducir nace con la operación, y así una factura de noviembre registrada tarde no se deduce entera.
- 2026-11-15 19:43 · 1- Solo alquiler turístico
- 2026-11-15 19:43 · 2- Fecha factura
- 2026-11-15 19:43 · answer: ¿Qué actividad cambia el 1/12/2026? → solo Alquiler turístico
- 2026-11-15 19:43 · answer: ¿Qué fecha decide la deducción de una compra? → la fecha de la factura
- 2026-11-15 19:56 · Ok a todo
- 2026-11-15 20:58 · Ok, valida y fusiona PR.
