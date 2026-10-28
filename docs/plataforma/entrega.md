---
title: Entrega y calidad
status: draft
depends_on: [architecture]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Solución .NET, compilación x64, pruebas automáticas en cada pull request y programadas, versionado de entregas visible (InformationalVersion), esquema de pruebas separado de producción y copias de seguridad coherentes de BD y documentos.
**Aceptación:** un PR de prueba ejecuta compilación y pruebas; la entrega muestra su versión.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-04..07: pruebas sin efectos externos, backups con ensayo de restauración, entregas versionadas, compilación completa
- 2026-10-28 12:28 · draft: Preparar la solución, la compilación x64, la ejecución de pruebas en cada PR y el versionado de entregas.
- 2026-10-28 12:28 · question: P-14 ¿cómo se instala en el Windows Server? (a) carpeta copiada con un script y ACE x64 instalado aparte ✅ (b) instalador MSI
