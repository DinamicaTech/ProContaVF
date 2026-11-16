---
title: Enlace de las líneas N43 con sus facturas
depends_on: [norma43/busqueda-por-importe, norma43/conciliacion, implantacion/migracion-erp, facturacion/descripcion-abreviada]
threads:
  - 2026-11-16 17:05 · Descripción abreviada de las facturas | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGZLepDu59ZV3krPJEfEMnf
---
## Summary
Guardar qué factura (de venta o de compra) paga o cobra cada línea N43, migrando el enlace que ya tiene el ERP y creándolo al elegir un candidato en la búsqueda por importe, para que la conciliación ponga en los apuntes de banco el número de la factura (documento) y su descripción abreviada (concepto).
**Aceptación:** las líneas migradas del ERP con N43Lineas.NumeroFactura quedan enlazadas a su factura; elegir un candidato en la búsqueda por importe crea el enlace; una línea puede tener varias facturas; la conciliación las recibe por `IFacturasLineaN43` (número y descripción abreviada en ventas, ConceptoAbreviado en compras) y actualiza los textos de su asiento si ya estaba contabilizada.
Hecho desde facturacion/descripcion-abreviada: `FacturasEnlazadasN43` (src/ProConta.Norma43/Conciliacion/FacturasLineaN43.cs), que usa por defecto `ServicioConciliacionN43`; migración de N43Lineas.NumeroFactura en `EjecucionErp.EnlacesNumeroFacturaAsync`; textos del asiento al enlazar en `LineasN43VM.BuscarPorImporteAsync`.

## Decisions
- 2026-11-13 10:50 · Creado como borrador desde norma43/conciliacion. En el ERP, N43Lineas.NumeroFactura está informado en 1.913 de las 6.736 líneas y no se migró: en ProContaVF la línea solo guarda Documento, que coincide con el número de factura en las remesas pero no en el resto. La conciliación ya tiene el punto de extensión `IFacturasLineaN43` (hoy `SinFacturasEnlazadas`) y `ActualizarTextosAsync`.
- 2026-11-16 11:30 · El enlace ya existe (norma43/busqueda-por-importe 2026-11-16 11:30): la línea se guarda en la factura, FacturasCompra.LineaN43 (migrado del ERP para las compras) y Facturas.LineaN43 (esquema 57), y lo crea la búsqueda por importe. Queda en este borrador: migrar N43Lineas.NumeroFactura del ERP a Facturas.LineaN43 para las ventas, implementar `IFacturasLineaN43` leyendo esas dos columnas y actualizar los textos del asiento al enlazar.
- 2026-11-16 17:05 · Lo hace facturacion/descripcion-abreviada en el mismo hilo, a petición del propietario (2026-11-16 16:45), porque la migración definitiva del ERP es inminente.
- 2026-11-16 17:05 · `FacturasEnlazadasN43` lee, por cada línea, las compras con FacturasCompra.LineaN43 (número y ConceptoAbreviado) y las ventas con número que cobra o devuelve: Facturas.LineaN43 y LineaN43Devolucion y los reintentos de ReintentosSEPA (número con el prefijo de su serie y DescripcionAbreviada). Los borradores no cuentan. Es la fuente por defecto de `ServicioConciliacionN43`.
- 2026-11-16 17:05 · Migración (implantacion/migracion-erp 2026-11-16 17:05): N43Lineas.NumeroFactura de los abonos es el número de la factura de venta (FacturasVenta.Id) y pasa a Facturas.LineaN43; en los cargos es el número de la factura del proveedor y pasa a FacturasCompra.LineaN43 cuando ese número es único entre las compras del ERP. Nunca pisa un enlace que ya exista (FacturasCompra.LineaN43 del ERP, recibos de las remesas): si una factura sale en varias líneas, se queda con la primera. Se ignoran las líneas marcadas para eliminar y el valor «Vinculada».
- 2026-11-16 17:05 · Al enlazar o desenlazar facturas con «Buscar por importe», las líneas ya contabilizadas por ProContaVF actualizan los textos de su asiento (`ActualizarTextosAsync`, motivo «Enlazar facturas con el movimiento bancario»); las no contabilizadas los toman al contabilizarse y las del ERP no se tocan. Cambiar después la descripción abreviada de una factura no actualiza el asiento por sí solo: se hace con «Actualizar textos del asiento» en Líneas N43.
- 2026-11-16 17:19 · Validado por el propietario (PR #92, desde facturacion/descripcion-abreviada): el nodo deja de ser borrador.

## Requirements
- 2026-11-13 10:29 · En el caso de transferencia de juan diaz, si tenemos el IdFacturaVenta, podemos concatenar la descripción de la factura al final de los dos apuntes.
- 2026-11-13 10:50 · draft: Migrar N43Lineas.NumeroFactura del ERP como enlace línea N43 ↔ factura (venta o compra, una o varias por línea), crear ese enlace al elegir un candidato en la búsqueda por importe e implementar IFacturasLineaN43 para la conciliación (número y descripción abreviada en ventas, ConceptoAbreviado en compras), actualizando los textos del asiento de la línea al enlazar.
- 2026-11-16 16:45 · Porqué dejar en el borrador el segundo punto ? Justo estamos a punto de hacer la migración definitiva del ERP, por lo que o se hace ahora o ya no se hará ...
