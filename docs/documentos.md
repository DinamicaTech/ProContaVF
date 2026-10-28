---
title: Gestión documental
status: draft
depends_on: [datos/esquema, datos/persistencia, plataforma/configuracion]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Documentos con vínculos N:M tipados a terceros, asientos, contratos y facturas; fichero con ruta relativa a una raíz configurable (por defecto Documentos junto a la aplicación).
**Aceptación:** un documento con varios vínculos aparece una vez; desvincular no borra el fichero.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 21 RF-DOC-01..07: tabla base, rutas relativas, vínculos, versiones, almacén
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/contratos/DOC-GESTOR.md: contrato inicial
- 2026-10-28 12:28 · draft: Implementar la gestión documental con vínculos y almacén de ficheros.
- 2026-10-28 12:28 · question: P-12 ¿versiones inmutables de documento desde el principio? (a) sí, adjuntos y PDF emitidos referencian la versión exacta ✅ (b) más adelante
