---
title: Acceso y sesión
status: draft
depends_on: [seguridad/permisos, organizacion/sociedades-ejercicios, datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Autenticación en cuatro fases (usuario, contraseña, empresa, ejercicio) y sesión validada con FechaHoraAutenticacion; solo muestra las empresas autorizadas tras autenticar y revalida la sesión en cada operación.
**Aceptación:** la de spec § 5.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-01, 02, 05, 06: cuatro fases, preferencias de empresa/ejercicio, sesión sin credenciales, hash PBKDF2
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Seguridad/docs/: contrato público e integración del acceso anteriores
- 2026-10-28 12:28 · draft: Implementar la autenticación en cuatro fases y la sesión validada.
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 1, 6.4: el acceso antiguo obligaba a identificarse sin poder cancelar; el nuevo permite salir de la identificación.
