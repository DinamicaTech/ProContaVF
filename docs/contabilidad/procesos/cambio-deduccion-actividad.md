---
title: Cambio de deducción de una actividad desde una fecha
status: draft
depends_on: [contabilidad/procesos, contabilidad/procesos/prorrata, contabilidad/modelos-tributarios, informes/informe-gestoria]
threads:
  - 2026-11-15 19:36 · Mejorar el informe trimestral para la gestoría | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GDKdsJYPwYjUXMKLnUhFQKN
---
## Summary
Pendiente: que la deducción de una actividad de IVA (Actividades.Deducible) pueda cambiar a partir de una fecha, para que cada línea de IVA se clasifique con la deducción vigente en su fecha. Caso que lo origina: desde el 1/12/2026 el alquiler de pisos lleva IVA al 10 % y sus compras pasan a ser 100 % deducibles; las de antes siguen sin deducir. El tipo de IVA de las ventas se cambia a mano y no se modeliza aquí.
**Aceptación:** con la actividad en «no deducible hasta el 30/11/2026 y 100 % deducible desde el 01/12/2026», el 303 del 4T deduce entera la cuota de diciembre de esa actividad y nada de octubre y noviembre; las bases repercutidas de diciembre cuentan con derecho a deducción en la prorrata real; las compras comunes siguen en prorrata y los bienes de inversión anteriores no se regularizan.

## Decisions
- 2026-11-15 19:36 · Creado como borrador desde informes/informe-gestoria (hilo «Mejorar el informe trimestral para la gestoría»), pendiente del visto bueno del propietario a la ruta y a la línea «draft:». Propuesta recomendada: un cambio de deducción con fecha de efecto en la actividad, en lugar de una actividad nueva con la asignación de centros fechada (más trabajo). Lo usarán LectorActividadesIva, la clasificación de las líneas de IVA del informe para la gestoría y la prorrata.

## Requirements
- 2026-11-15 19:24 · Cómo gestionamos que a partir del 1/12/2026, el alquiler de pisos lleva 10% de IVA y por tanto, pasa a ser 100% deducible para facturas de compra relacionadas a esa actividad a partir de esa fecha ?
- 2026-11-15 19:36 · Sí, me temo que en algún sitio habrá que modelizar la fecha de corte, podían haber esperado un mes para aplicarlo y no complicarnos la vida a todos.
- 2026-11-15 19:36 · Mi idea es modificar el tipo de IVA de venta manualmente el 1/12/2026 al 10%, creo que no necesitamos tenerlo modelizado. Con la prorrata por actividad no puedo hacer lo mismo ya que de alguna forma necesitas poder discriminar por fechas.
- 2026-11-15 19:36 · Las compras comunes van a prorrata como hasta ahora.
- 2026-11-15 19:36 · Y los bienes de inversión, al ser anteriores a esta fecha, tampoco creo que deban modificarse
- 2026-11-15 19:36 · draft: Permitir que la deducción de una actividad de IVA cambie desde una fecha (alquiler de pisos al 10 % y 100 % deducible desde el 1/12/2026), clasificando cada línea de IVA por su fecha en el 303, la prorrata y el informe para la gestoría.
- 2026-11-15 19:36 · question: ¿Qué actividad cambia el 1/12/2026? (a) Alquiler turístico (b) Alquiler Viviendas (c) las dos. Sin recomendación: depende de a qué alquileres se aplica el 10 %.
- 2026-11-15 19:36 · question: ¿Qué fecha decide la deducción de una compra de finales de noviembre contabilizada en diciembre? (a) la de la factura (fecha de operación o expedición) (b) la fecha contable del asiento. Recomendada (a): el derecho a deducir nace con la operación, y así una factura de noviembre registrada tarde no se deduce entera.
