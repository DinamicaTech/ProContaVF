---
title: Motor de informes
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Catálogo de informes, proveedores de datos y plantillas versionadas; salidas HTML, PDF y XLSX sin Excel COM; modelo estable para el PDF de factura.
**Aceptación:** el informe usa la plantilla publicada exacta; la exportación conserva decimales y fechas.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 24 RF-REP-01..03: catálogo, plantillas, salidas
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Reporting/docs/REPORTING.md: motor anterior (FastReport en el disco del propietario)
- 2026-10-28 12:28 · draft: Implementar el motor de informes con plantillas versionadas.
- 2026-10-28 12:28 · question: ¿motor de plantillas? (a) FastReport, como el proyecto anterior ✅ (b) otro
