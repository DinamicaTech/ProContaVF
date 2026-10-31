---
title: Operadores y permisos
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Operadores, grupos (N:M) y permisos por ámbito: ámbito sin configurar permite a todo operador válido; ámbito configurado aplica sus asignaciones. Los servicios comprueban la autorización, no solo la pantalla.
**Aceptación:** grupo sin filas no hereda acceso en un ámbito configurado; tabla ausente no equivale a ámbito sin configurar.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-03, 04, 07: operadores y grupos, política de ámbitos, puntos pendientes
- 2026-10-28 12:28 · draft: Implementar operadores, grupos y la política de permisos por ámbitos.
- 2026-10-28 12:28 · question: P-03 ¿arranque sin operadores? (a) asistente que crea el primer administrador si no hay ninguno ✅ (b) operador por defecto en la maestra
- 2026-10-28 12:28 · question: P-03 ¿bloqueo por intentos, expiración y segundo factor? (a) fuera del primer incremento ✅ (b) bloqueo por intentos ya
- 2026-10-29 17:45 · Derived from (root): los procesos que generan asientos o borran datos (amortizaciones, IVA, apertura y cierre, dividendos, resetear la demo…) exigen un permiso propio, y ningún atajo de teclado da privilegios sin autenticarse (sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 1, 6.3).
