---
title: Facturas recurrentes de proveedores pendientes de recibir
status: draft
depends_on: [compras/factura-compra, terceros/tercero, pantalla-de-inicio]
threads:
  - 2026-11-17 10:35 · Proyectar facturas de proveedores pendientes | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GLgEfj973aef2DkS5nYqTVW
---
## Summary
Pantalla informativa de Compras que lista las facturas de proveedores recurrentes (mensuales, bimestrales o trimestrales) que deberían haber llegado y no están registradas, con el importe estimado de la última factura. Sustituye a frmFacturasPendientes del ERP. No genera asientos: es distinta del proceso de cierre contabilidad/procesos/facturas-pendientes-recibir (4009).
**Aceptación:** con facturas de prueba de un proveedor mensual, uno bimestral y uno trimestral, la lista muestra solo los periodos que faltan, con el importe y la fecha de la última factura; un proveedor excluido en su ficha no aparece; «No llegará» quita ese periodo y no vuelve al refrescar; abrir la pantalla no escribe ningún asiento.

## Decisions
- 2026-11-17 10:35 · Creado en el hilo «Proyectar facturas de proveedores pendientes» a partir del análisis de frmFacturasPendientes del ERP (propietario 2026-11-17 10:28, opción 2: va en compras, no en contabilidad/procesos, porque controla facturas de compra que no han llegado y no genera asientos).
- 2026-11-17 10:35 · Proceso antiguo (frmFacturasPendientes, en el front-end ProcontaEstructurav2.accdb): lee FacturasCompra de los últimos 12 meses de la sociedad activa, unida a F_PRO por la cuenta del proveedor; un proveedor es recurrente con factura en al menos 3 meses seguidos; si su última factura tiene 30 días o más, saca una fila por cada mes desde el que cortó la racha hasta el actual, con el total de la última factura como importe estimado y la fecha de esa factura; doble clic abre el PDF de la última factura; botones Refrescar y Salir. Defectos que no se reproducen: la racha se cuenta desde la primera fila leída de cada proveedor, que no siempre es su factura más reciente, y solo entiende proveedores mensuales.
- 2026-11-17 10:35 · Detección: con las facturas de compra registradas de los últimos 12 meses de la sociedad de trabajo, se deduce la periodicidad de cada proveedor (mensual, bimestral o trimestral) a partir del intervalo entre sus facturas, contado desde la más reciente; es recurrente si tiene al menos 3 facturas seguidas con esa periodicidad. Falta una factura cuando ha pasado su periodo más un margen (30 días, como el ERP) desde la última; se lista una fila por cada periodo que falta hasta hoy.
- 2026-11-17 10:35 · Importe estimado: el total de la última factura del proveedor, solo informativo (propietario 2026-11-17 10:28, P2).
- 2026-11-17 10:35 · Solo es una pantalla para revisar: no genera asientos ni se enlaza con la 4009 del cierre (propietario 2026-11-17 10:28, P3).
- 2026-11-17 10:35 · Pantalla: columnas proveedor, periodicidad, periodo esperado, importe estimado y fecha de la última factura; selección múltiple con casilla. Botones de cabecera «Refrescar» y exportar; menú contextual sobre las filas: «Abrir la última factura» (su PDF), «Registrar la factura» (abre el alta de factura de compra con el proveedor) y «No llegará», que descarta ese periodo de ese proveedor de forma persistente (se puede deshacer).
- 2026-11-17 10:35 · La ficha del proveedor (terceros/tercero) puede excluirlo del control o fijar su periodicidad, que manda sobre la deducida. Inicio (pantalla-de-inicio) muestra en la salud un aviso con el número de facturas recurrentes sin recibir, que abre esta pantalla.

## Requirements
- 2026-11-16 19:15 · El ERP actual tiene un proceso que muestra facturas recurrentes (normalmente mensuales) no recibidas. Está en el formulario frmFacturasPendientes. ¿Puedes analizarlo?
- 2026-11-17 10:28 · Opción 2 (compras/facturas-recurrentes-pendientes)
- 2026-11-17 10:28 · Ok al desglose
- 2026-11-17 10:28 · P1. Ok
- 2026-11-17 10:28 · answer: ¿detectar también bimestrales y trimestrales? → sí
- 2026-11-17 10:28 · P2. Creo que el más acertado sería el de la última factura, igualmente, es simplemente informativo
- 2026-11-17 10:28 · answer: ¿importe estimado de la última factura o media de las tres últimas? → el de la última factura
- 2026-11-17 10:28 · P3. No, solo es una pantalla para revisar facturas pendientes, no ha de generar asientos
- 2026-11-17 10:28 · answer: ¿enlazar con el asiento de la 4009 del cierre? → no, no genera asientos
- 2026-11-17 10:35 · draft: Implementar la pantalla de facturas recurrentes de proveedores pendientes de recibir según las Decisions de este nodo, con la exclusión y la periodicidad fija en la ficha del proveedor y el aviso en la salud de Inicio.
