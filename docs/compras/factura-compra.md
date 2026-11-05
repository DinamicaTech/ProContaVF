---
title: Factura de compra
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 07:46 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-11-05 11:24 · Construir facturas de compra | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEbtcX2Z7baM2yhwHY2wJXj
---
## Summary
Ente FacturaCompra con sus desgloses: inserción atómica, MD5 compatible del PDF para duplicados, configuración por sociedad y destino por sociedad/ejercicio.
**Aceptación:** PDF duplicado no crea segunda compra; un corte no deja desgloses huérfanos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 07:46 · La factura de compra guarda su moneda y sus importes originales (los euros se calculan con el cambio del día de la factura) y, si es una rectificativa o un abono del proveedor, la factura a la que rectifica.
- 2026-11-05 11:24 · Estructura nueva de FacturasCompra: sociedad-ejercicio explícito (IdSociedadEjercicio), desgloses de IVA en una tabla hija (tipo, base, cuota, confianza y evidencia) en lugar de las columnas fijas BI0-BI4, moneda e importe original con el cambio aplicado y su fecha, factura rectificada, confianza, «requiere revisión» y log del reconocimiento (JSON versionado), además de la configuración del reconocimiento por sociedad. La migración del esquema solo crea la estructura; el traslado de las facturas antiguas al formato nuevo va en la migración del ERP (implantacion/migracion-erp), como el resto de correcciones de datos.
- 2026-11-05 11:24 · Contabilizar no registra el pago: el pago al proveedor se concilia después con el banco (norma43). La forma de pago del proveedor se guarda como dato de la factura.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-05: duplicados, persistencia atómica, configuración
- 2026-10-28 12:28 · draft: Implementar el ente FacturaCompra y su persistencia con control de duplicados.
- 2026-10-31 07:30 · Y en el caso de facturas de importación, se puede indicar la moneda y hago conversión a euros aplicando el tipo de cambio de la moneda en la fecha de la factura. En la contabilidad siempre se trabaja en euros.
- 2026-10-31 07:41 · 4. Ver relacionadas lo que hará es mostrar la factura original (en el caso de estar sobre una rectificativa) o viceversa
- 2026-11-05 11:18 · P5. Sí, cada sociedad una carpeta raíz dentro de la carpeta datos que es donde estará la BD compartida
- 2026-11-05 11:18 · answer: sociedad de cada PDF → cada sociedad tiene su carpeta raíz dentro de la carpeta de datos de la BD compartida
- 2026-11-05 11:18 · P8 a
- 2026-11-05 11:18 · answer: facturas antiguas → la migración del esquema solo crea la estructura nueva; su traslado va en la migración del ERP
