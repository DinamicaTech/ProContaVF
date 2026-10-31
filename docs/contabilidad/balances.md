---
title: Balances
status: draft
depends_on: [contabilidad/asientos, informes/reporting]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Balance de sumas y saldos, de situación, pérdidas y ganancias y comparativos, con trazabilidad al origen; los presenta informes/reporting.
**Aceptación:** cada cifra enlaza a su origen; los ejemplos esperados cuadran.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Formatos de referencia de los balances: BalancePyG2026_Nuevo.xlsx y BalanceSituacion2026_Nuevo.xlsx de la carpeta ProConta (resuelve P-09).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-BAL-01: balances por contrato y ejemplos esperados
- 2026-10-28 12:28 · From ProConta/BalancePyG2026_Nuevo.xlsx y BalanceSituacion2026_Nuevo.xlsx (disco del propietario): formatos de referencia
- 2026-10-28 12:28 · draft: Implementar los balances con sus ejemplos esperados.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-09 ¿los formatos de balance son los xlsx _Nuevo de la carpeta ProConta? (a) sí ✅ (b) otros
- 2026-10-31 16:54 · 4. a
- 2026-10-31 16:54 · answer: P-09 formatos de balance → (a) los xlsx _Nuevo de la carpeta ProConta
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 4: exportaciones Balance PyG, PyG detallado y Balance de situación del menú antiguo.
