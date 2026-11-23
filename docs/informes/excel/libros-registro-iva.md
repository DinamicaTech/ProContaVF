---
title: Libros registro de IVA
status: draft
depends_on: [informes/excel, informes/reporting, facturacion/factura-venta/contabilizacion, compras/factura-compra/contabilizacion, contabilidad/modelos-tributarios]
threads:
  - 2026-11-23 13:55 · Libros registro de IVA | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G89D89wjxGRBqXapkzMB5Co
---
## Summary
Libros registro de IVA de facturas expedidas y de facturas recibidas por periodo, obligatorios, en Excel y en PDF desde el menú Informes, a partir de los registros de IVA repercutido y soportado que ya graban ventas y compras al contabilizar.
Hecho, pendiente de validar: Informes › «Libros registro de IVA…» pide periodo (trimestre, mes o fechas del ejercicio de trabajo), libros (expedidas, recibidas o los dos) y formato, y guarda el fichero por el gestor documental como sumas y saldos. Los libros salen de las mismas líneas de IVA que el 303 (`LectorLineasIva`), así que cuadran con él por construcción; cálculo en `src/ProConta.Contabilidad/Iva/LibrosRegistroIva.cs`, salidas en `src/ProConta.Informes/InformeLibrosIva.cs`, diálogo en `src/ProConta.Contabilidad.UI/Iva/DialogoLibrosIva.cs`.
**Aceptación:** en la copia de pruebas, las bases y cuotas de cada libro por trimestre coinciden con las casillas del 303 del mismo trimestre; cada factura sale una vez con su número, fecha, NIF, base, tipo y cuota.

## Decisions
- 2026-11-18 12:20 · Creado como borrador desde el hilo «Registrar las funcionalidades que faltan» (revisión de funcionalidades antes del arranque), con el visto bueno del propietario (2026-11-18 12:18).
- 2026-11-23 15:27 · Criterios (propietario 2026-11-23 15:27): el periodo va por la fecha contable del asiento, como el 303; solo entran asientos contabilizados y nunca el de cierre.
- 2026-11-23 15:27 · Fuente de los datos: las líneas de IVA del 303 (`LectorLineasIva` y `ClasificadorLineasIva`, contabilidad/modelos-tributarios), no una consulta propia. La operación de las recibidas (nacional, intracomunitaria, importación, ISP) sale del asiento y no del código de tipo de operación, que lo importado de ContaSol trae cruzado con Compras; así el libro y el 303 no pueden discrepar.
- 2026-11-23 15:27 · Forma de los libros: una fila por factura y tipo de IVA, numeradas por factura dentro del periodo (una factura es un registro de IVA: mismo asiento, número y tercero), por fecha contable y asiento. Expedidas: fechas contable y de expedición, número, cliente, NIF, base, tipo, cuota, rectificativa y clave (exención E1-E6 o calificación S1/S2/N1/N2). Recibidas: lo mismo con el proveedor, la operación, bien de inversión, cuota soportada, cuota autorrepercutida, deducción (100 %, prorrata, no deducible, sin centro) y el aviso de la línea.
- 2026-11-23 15:27 · Resumen: totales por tipo de cada libro y el cuadre con el 303 de las facturas del periodo (devengado, deducible, informativo y resultado), con la prorrata provisional del ejercicio y sin la regularización anual de la prorrata (casilla 44), que no sale de ninguna factura. El cuadre lleva siempre los dos libros, aunque se pida uno.
- 2026-11-23 15:27 · Salidas, como sumas y saldos: PDF A4 apaisado por el motor de informes (HTML › WebView2), con los dos libros y una página de resumen; Excel (ClosedXML) con una hoja por libro (tabla con filtros y fila de totales) y la hoja «Resumen». Se guardan por el gestor documental (tipo INFORME_CONTABLE, «<año>\Informes», vinculado al ejercicio) y se abren. Periodo propuesto: el último trimestre terminado.
- 2026-11-23 15:27 · Comprobado en una copia de ProConta-pruebas-main-02 (ProConta-libros-iva-01.accdb): en los cuatro trimestres de 2026 el devengado, el deducible y el resultado de los libros coinciden al céntimo con el 303 (1T 745,74 €, 2T 2.447,43 €, 3T 3.320,66 €, 4T −1,35 €), sin filas repetidas ni facturas sin número. 19 recibidas del 2T no tienen NIF en su registro de IVA (dato de origen, se ven en el libro).

## Requirements
- 2026-11-18 12:16 · Ok, nuevo nodo en draft, lo activo más tarde
- 2026-11-18 12:18 · draft: Libros registro de IVA de facturas expedidas y de facturas recibidas por periodo, en Excel y en PDF desde el menú Informes, a partir de los registros de IVA que ya graban ventas y compras.
- 2026-11-23 15:27 · * Fecha contable del asiento
- 2026-11-23 15:27 · * Ok, sin el de cierre
- 2026-11-23 15:27 · Adelante con del desarrollo
