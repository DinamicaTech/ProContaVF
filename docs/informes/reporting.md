---
title: Motor de informes
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Catálogo de informes, proveedores de datos y plantillas versionadas con FastReport en su edición de código abierto; salidas HTML, PDF y XLSX sin Excel COM; modelo estable para el PDF de factura.
**Aceptación:** el informe usa la plantilla publicada exacta; la exportación conserva decimales y fechas.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Motor de plantillas: la edición gratuita de código abierto de FastReport (FastReport.OpenSource, licencia MIT), compatible con la licencia MIT de ProContaVF (architecture 2026-10-31 16:54); no se usa la edición comercial.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 24 RF-REP-01..03: catálogo, plantillas, salidas
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Reporting/docs/REPORTING.md: motor anterior (FastReport en el disco del propietario)
- 2026-10-28 12:28 · draft: Implementar el motor de informes con plantillas versionadas.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿motor de plantillas? (a) FastReport, como el proyecto anterior ✅ (b) otro
- 2026-10-31 16:54 · 22. a) Basta la edición gratuita, la idea es que ProContaVF sea licencia MIT
- 2026-10-31 16:54 · answer: motor de plantillas → (a) FastReport; basta la edición gratuita, porque ProContaVF será licencia MIT
