---
title: Migración del ERP
status: draft
depends_on: [terceros/tercero, facturacion/factura-venta, contabilidad/asientos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Migración única desde ProcontaEstructura.accdb (antes deporwin.mdb) para sustituir la instalación actual, con inventario completo, mapas de identidades y conciliación; nada se reenvía a la AEAT ni se activa al cargar.
**Aceptación:** ensayo repetible en base nueva; original sin cambios; ninguna tabla omitida en silencio.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 26.1 RF-ERP-01..05: migración única, coordinador, clasificación ContaSol, preservación
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Migracion/docs/: migrador anterior y tablas
- 2026-10-28 12:28 · draft: Implementar y ensayar la migración del ERP sobre copias, con conciliación.
- 2026-10-28 12:28 · question: P-13 ¿cobertura temporal? (a) todos los ejercicios con facturas emitidas ✅ (b) solo el ejercicio en curso
