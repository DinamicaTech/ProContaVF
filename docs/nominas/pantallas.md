---
title: Pantallas de nóminas
status: draft
depends_on: [nominas, plataforma/contenedor]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Importación, mapeo de columnas, resolución de empleados y revisión de la contabilización de nóminas.
**Aceptación:** empleado ambiguo exige resolver antes de confirmar.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: la lista «Resumen Nóminas» de `frmNominas` (importar, exportar a contabilidad, deshacer, refrescar, doble clic a los asientos del mes), en `sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md` § 1-3 y 6.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 18 RF-NOM-01: mapeo y resolución con selección manual
- 2026-10-28 12:28 · draft: Construir la pantalla de importación de nóminas.
- 2026-10-29 17:45 · Derived from nominas: la pantalla parte del fichero Excel (o del recibido por correo), no del portapapeles, y ofrece lo que hoy hace «Resumen Nóminas» (sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md § 1-3, 6).
