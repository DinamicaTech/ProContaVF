---
title: Conceptos
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 16:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
---
## Summary
Catálogo de conceptos facturables (tipos, actividad, precios, tratamiento fiscal) para preparar líneas; cambiar un concepto no reescribe líneas históricas.
**Aceptación:** duplicar, consultar uso y eliminar según referencias.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · El catálogo de conceptos es por sociedad y continuo entre ejercicios (resuelve RF-CAT-02).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 9 RF-CAT-01, 02: catálogo de conceptos
- 2026-10-28 12:28 · draft: Implementar el catálogo de conceptos.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-CAT-02 ¿ámbito del catálogo? (a) por sociedad ✅ (b) global (c) por ejercicio
- 2026-10-31 16:54 · 12. a
- 2026-10-31 16:54 · answer: RF-CAT-02 ámbito del catálogo → (a) por sociedad
- 2026-11-01 16:18 · 3. Ok
- 2026-11-01 16:18 · answer: conceptos por sociedad → (a) migración: cada concepto pasa a su sociedad y la cuenta de venta se guarda como identidad contable; se conservan los IdConcepto y los repetidos en varios ejercicios se cuentan antes de migrar para decidir si se unen
- 2026-11-01 16:20 · Derived from plataforma/mantenimiento: proveedor de Conceptos y Tipos de concepto con Duplicar, Ver uso y baja según uso.
