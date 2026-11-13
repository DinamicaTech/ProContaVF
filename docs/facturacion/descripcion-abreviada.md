---
title: Descripción abreviada de las facturas
status: draft
depends_on: [facturacion/factura-venta, facturacion/programadas, implantacion/migracion-erp, norma43/conciliacion]
---
## Summary
Campo corto DescripcionAbreviada en las facturas de venta y en las facturas programadas (que lo pasan a la factura que generan), para el concepto de los apuntes de banco de la conciliación N43 («Transferencia de Juan Diaz · Alquiler mensual StaMaria 03/2026»). La carga inicial sale de las Notas del ERP.
**Aceptación:** la factura de venta y la programada tienen DescripcionAbreviada editable en sus pantallas; la programada la pasa (con su periodo) a la factura que genera; la migración del ERP la carga desde FacturasVenta.Notas y FacturasProgramadas.Notas; la conciliación N43 la añade al concepto de los apuntes de las líneas enlazadas.

## Decisions
- 2026-11-13 10:50 · Creado como borrador desde norma43/conciliacion. En el ERP, FacturasVenta.Notas está informado en 1.566 de 2.160 facturas (p. ej. «Período: 01/09/2026 - 30/09/2026»); en ProContaVF la tabla Facturas no tiene Notas (no se migró), así que la carga inicial tiene que salir del ERP en implantacion/migracion-erp. Compras aún no tiene descripción abreviada: FacturasCompra.ConceptoAbreviado existe pero está vacío (su valor por defecto sería CuentasProveedoresSociedad.ConceptoAbreviadoDefecto).

## Requirements
- 2026-11-13 10:42 · 'Alquiler mensual StaMaria 03/2026', el IdFactura ya lo tenemos en una columna aparte. Seria FacturasVenta.DescripcionAbreviada ..... vaya, me acabo de inventar un campo que afecta de lleno a facturas de venta. El campo 'Descripcion' actual es demasiado largo, por lo que añadiremos un 'DescripcionAbreviada' en facturas programadas y en facturas venta. De hecho, actualmente ya lo tenemos en FacturasVenta.Notas (aparece, por ejemplo 'Período: 01/11/2025 - 30/11/2025' que es justo lo que necesitamos), solo que este es un campo Memo no especializado en la descripción corta, pero nos puede servir para la carga inicial de datos (FacturasProgramadas.DescripcionAbreviada=FacturasProgramadas.Notas) y (FacturasVenta.DescripcionAbreviada=FacturasVenta.Notas)
- 2026-11-13 10:42 · Si, compras aún no tiene descripcion abreviada.
- 2026-11-13 10:50 · draft: Añadir DescripcionAbreviada a las facturas de venta y a las facturas programadas (que la pasan a la factura que generan), editable en sus pantallas, con la carga inicial desde FacturasVenta.Notas y FacturasProgramadas.Notas del ERP en implantacion/migracion-erp, para que la conciliación N43 la añada al concepto de los apuntes de banco.
