---
title: Núcleo común
status: draft
depends_on: []
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Tipos compartidos por todas las áreas: NIF y su validación, importes Decimal con redondeo monetario, contexto sociedad/ejercicio, resultado con errores recuperables, historial y auditoría (actor, instante, operación). No contiene reglas de negocio de ningún área.
**Aceptación:** la biblioteca compila, la usan al menos dos áreas y sus pruebas cubren validación de NIF y redondeo.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-02: convenciones de código (adaptadas a C# en architecture)
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-02: Decimal y redondeo AwayFromZero a dos decimales, base del tipo Importe
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 6 RF-UI-06 y 27 RF-TEC-04: errores con registro afectado, causa y acción correctora; logs con correlación/actor/contexto sin secretos
- 2026-10-28 12:28 · draft: Crear en la solución nueva la biblioteca común con los tipos compartidos (NIF, Importe, ContextoSociedadEjercicio, Resultado/Error, Auditoría) y sus pruebas unitarias.
