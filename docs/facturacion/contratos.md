---
title: Contratos
status: draft
depends_on: [facturacion/programadas, terceros/roles]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Contratos de la sociedad permanente con cliente y facturación programada, continuidad entre ejercicios y revisión anual IPC/IRAV.
**Aceptación:** aviso y aplicación idempotentes por anualidad.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · El inmueble del contrato es opcional, una referencia libre hasta que se formalicen los activos (creative-lab/activos) (resuelve RF-CON-01).
- 2026-10-31 16:54 · Índices IPC/IRAV del INE; los incrementos reales no se activan hasta validar la fórmula con casos del propietario (resuelve P-06).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 9 RF-CON-01..03: contratos y revisión IPC/IRAV
- 2026-10-28 12:28 · draft: Implementar contratos y su revisión anual IPC/IRAV.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-CON-01 ¿el inmueble es obligatorio? Activos ha pasado al laboratorio (creative-lab/activos). (a) opcional, referencia libre hasta que se rediseñen los activos ✅ (b) obligatorio como hoy
- 2026-10-31 16:54 · 13. a
- 2026-10-31 16:54 · answer: RF-CON-01 inmueble obligatorio → (a) opcional, referencia libre hasta que se rediseñen los activos
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-06 ¿fuente oficial y fórmula del IPC/IRAV? (a) INE, validar antes de activar incrementos reales ✅
- 2026-10-31 16:54 · 14. a
- 2026-10-31 16:54 · answer: P-06 fuente IPC/IRAV → (a) INE, validar antes de activar incrementos reales
