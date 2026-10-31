---
title: Entrega y calidad
status: draft
depends_on: [architecture]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Estrategia de desarrollo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Solución .NET, compilación x64, pruebas automáticas ejecutadas en local (sin BD por cada hilo antes de su PR; contra Access en el equipo del propietario antes de validar), versionado de entregas visible (InformationalVersion), esquema de pruebas separado de producción y copias de seguridad coherentes de BD y documentos.
**Aceptación:** un comando local compila la solución x64 y ejecuta las pruebas, con y sin BD de pruebas; la entrega muestra su versión.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 08:37 · Pruebas en local, sin compilación en GitHub (ver architecture 2026-10-31 08:37): cada hilo ejecuta las pruebas sin BD antes de abrir su PR y las pruebas contra Access se ejecutan en el equipo del propietario, contra la BD de pruebas, antes de pedir la validación. Sustituye la parte "ejecución de pruebas en cada PR" de su línea draft. Es el primer incremento del esqueleto técnico.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-04..07: pruebas sin efectos externos, backups con ensayo de restauración, entregas versionadas, compilación completa
- 2026-10-28 12:28 · draft: Preparar la solución, la compilación x64, la ejecución de pruebas en cada PR y el versionado de entregas.
- 2026-10-28 12:28 · question: P-14 ¿cómo se instala en el Windows Server? (a) carpeta copiada con un script y ACE x64 instalado aparte ✅ (b) instalador MSI
- 2026-10-31 08:37 · Derived from architecture: la solución, la compilación x64 y la ejecución local de las pruebas (sin BD y contra la BD de pruebas) forman el primer incremento del esqueleto técnico; no se configura compilación en GitHub.
