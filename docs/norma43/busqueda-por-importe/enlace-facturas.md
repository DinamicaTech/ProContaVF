---
title: Enlace de las líneas N43 con sus facturas
status: draft
depends_on: [norma43/busqueda-por-importe, norma43/conciliacion, implantacion/migracion-erp, facturacion/descripcion-abreviada]
---
## Summary
Guardar qué factura (de venta o de compra) paga o cobra cada línea N43, migrando el enlace que ya tiene el ERP y creándolo al elegir un candidato en la búsqueda por importe, para que la conciliación ponga en los apuntes de banco el número de la factura (documento) y su descripción abreviada (concepto).
**Aceptación:** las líneas migradas del ERP con N43Lineas.NumeroFactura quedan enlazadas a su factura; elegir un candidato en la búsqueda por importe crea el enlace; una línea puede tener varias facturas; la conciliación las recibe por `IFacturasLineaN43` (número y descripción abreviada en ventas, ConceptoAbreviado en compras) y actualiza los textos de su asiento si ya estaba contabilizada.

## Decisions
- 2026-11-13 10:50 · Creado como borrador desde norma43/conciliacion. En el ERP, N43Lineas.NumeroFactura está informado en 1.913 de las 6.736 líneas y no se migró: en ProContaVF la línea solo guarda Documento, que coincide con el número de factura en las remesas pero no en el resto. La conciliación ya tiene el punto de extensión `IFacturasLineaN43` (hoy `SinFacturasEnlazadas`) y `ActualizarTextosAsync`.

## Requirements
- 2026-11-13 10:29 · En el caso de transferencia de juan diaz, si tenemos el IdFacturaVenta, podemos concatenar la descripción de la factura al final de los dos apuntes.
- 2026-11-13 10:50 · draft: Migrar N43Lineas.NumeroFactura del ERP como enlace línea N43 ↔ factura (venta o compra, una o varias por línea), crear ese enlace al elegir un candidato en la búsqueda por importe e implementar IFacturasLineaN43 para la conciliación (número y descripción abreviada en ventas, ConceptoAbreviado en compras), actualizando los textos del asiento de la línea al enlazar.
