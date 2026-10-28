---
title: Nóminas
status: draft
depends_on: [contabilidad/asientos, datos/esquema, datos/persistencia]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Importación de nóminas desde Excel (detección de cabecera, mapeo y alias por sociedad, resolución de empleados) y su contabilización con plantillas mediante el motor de asientos; reversión y reimportación controladas.
**Aceptación:** la de spec § 18.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 18 RF-NOM-01..05: importación, idempotencia, plantillas, reversión
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Nominas/docs/: interfaz anterior
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Implementar la importación y contabilización de nóminas.
- 2026-10-28 12:28 · question: P-05/A08 ¿libros de ejemplo y asientos esperados? (a) los de ProConta/Nominas/2025 y 2026 de tu disco ✅
