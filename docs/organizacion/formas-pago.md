---
title: Formas de pago
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Catálogo de formas de pago, del que cuelgan las cuentas de proveedor/acreedor de terceros/roles. Tabla propia y muy simplificada: Id (código conservado del ERP), Descripcion y SEPA (domiciliación SEPA). Contrato `ICatalogoFormasPago` (src/ProConta.Organizacion/FormasPago): listar, obtener, alta, modificación y baja restringida por uso.
**Aceptación:** alta y baja restringida por uso desde plataforma/mantenimiento.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 18:13 · FormasPago deja de ser una tabla vinculada al ERP y pasa a tabla propia con solo Id, Descripcion y SEPA (sí/no, domiciliación SEPA), conservando los códigos del ERP; F_CDP, F_DEP e Inmuebles siguen vinculadas.
- 2026-10-31 19:55 · Migración 20261031_1945_FORMAS_PAGO_PROPIA: copia las filas del vínculo (o, si su fichero no está, del accdb indicado con `--origen-formas-pago`), quita el vínculo y crea la tabla con Id BYTE (el tipo de Facturas.FormaDePago y Contratos.FormaDePago). SEPA se propone a partir de la descripción (contiene «domicili»): en la maestra quedan como SEPA 110 «Recibo domiciliado» y 125 «Domiciliación proveedor».
- 2026-10-31 19:55 · Las relaciones antiguas de Facturas.FormaDePago y Contratos.FormaDePago hacia el vínculo se quitan y no se recrean: el código 1, que usan 2 facturas y la forma de pago por defecto de todas las sociedades y ejercicios, no existe en el catálogo, y no se inventan filas.
- 2026-10-31 19:55 · Alta con el siguiente código libre (hasta 255) dentro de transacción; descripción obligatoria, de hasta 100 caracteres y sin repetir. La baja se rechaza, diciendo dónde, si la usan facturas, contratos o la forma de pago por defecto de sociedades o ejercicios.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 29 P-02: propietario de formas de pago
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-04: cuentas de proveedor y acreedor bajo forma de pago
- 2026-10-28 12:28 · draft: Definir e implementar el catálogo de formas de pago y su proveedor de mantenimiento.
- 2026-10-31 18:13 · FormasPago deberá materializarse como tabla propia, pero muy simplificada (Id, Descripcion, SEPA) ya que de momento no tengo previsto incorporarle más funcionalidad.
- 2026-10-31 17:53 · Derived from architecture: el catálogo de formas de pago como tabla propia con su contrato de alta, modificación y baja restringida por uso.

## Hand-over
- 2026-10-31 19:55 · Hecho: migración, contrato y pruebas (migración con códigos conservados, SEPA, sin origen no cambia nada; alta, repetida, modificación, baja en uso y libre). Pendiente: que el propietario valide qué formas de pago son SEPA y qué hacer con el código 1 sin forma de pago (darlo de alta o corregir esos registros) antes de recrear las relaciones; el proveedor declarativo para plataforma/mantenimiento.
