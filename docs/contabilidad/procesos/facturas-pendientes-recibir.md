---
title: Facturas pendientes de recibir
status: draft
depends_on: [contabilidad/procesos, compras/factura-compra, contabilidad/procesos/cierre-ejercicio]
threads: []
---
## Summary
Proceso de cierre para los proveedores y acreedores cuadrados con saldo deudor (pagado sin factura recibida): propone el gasto por la base supuesta (saldo / (1 + tipo de IVA configurable)) en la cuenta de gasto por defecto del proveedor contra la 4009 (facturas pendientes de recibir), y lo revierte en la apertura del ejercicio siguiente para que la factura real se contabilice con normalidad.
**Aceptación:** repetirlo no duplica; la apertura del ejercicio siguiente lleva su reversión; el proveedor no se realimenta en una segunda ejecución.

## Decisions
- 2026-11-13 12:05 · Creado como borrador desde contabilidad/procesos (hilo «Definir los procesos contables»), al acordar con el propietario la lista de procesos contables. Usa el marco común del nodo padre: propuesta revisable en el visor de asientos, confirmación de exactamente lo propuesto, lote propio regenerable (repetir no duplica), permisos y cuentas configuradas por sociedad y ejercicio.
- 2026-11-13 12:05 · Se rehace (propietario 2026-11-13 11:52, P3 A): no se reproduce el proceso antiguo (21 % fijo, dos asientos por la 4109000 que se anulan, IVA supuesto que queda en el proveedor y sin reversión).

## Requirements
- 2026-11-13 11:52 · answer: ¿Rehacer o quitar las facturas pendientes de recibir? → P3. A
- 2026-11-13 12:05 · draft: Proponer a 31/12, para los proveedores y acreedores cuadrados con saldo deudor, el gasto por la base supuesta (tipo de IVA configurable) en la cuenta de gasto del proveedor contra la 4009, y revertirlo en la apertura del ejercicio siguiente.
