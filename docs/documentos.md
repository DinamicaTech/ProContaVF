---
title: Gestión documental
status: draft
depends_on: [datos/esquema, datos/persistencia, plataforma/configuracion]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Motor documental de todos los documentos almacenados en ProContaVF (facturas de compra y venta, contratos…), con versiones inmutables desde el principio y vínculos N:M tipados a terceros, asientos, contratos y facturas; fichero con ruta relativa a una raíz configurable (por defecto Documentos junto a la aplicación).
**Aceptación:** un documento con varios vínculos aparece una vez; desvincular no borra el fichero.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Versiones inmutables de documento desde el principio: los adjuntos enviados y los PDF emitidos referencian la versión exacta (RF-DOC-04). Este nodo es el motor documental de todos los documentos almacenados en ProContaVF (facturas de compra y venta, contratos, etc.) (resuelve P-12).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 21 RF-DOC-01..07: tabla base, rutas relativas, vínculos, versiones, almacén
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/contratos/DOC-GESTOR.md: contrato inicial
- 2026-10-28 12:28 · draft: Implementar la gestión documental con vínculos y almacén de ficheros.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-12 ¿versiones inmutables de documento desde el principio? (a) sí, adjuntos y PDF emitidos referencian la versión exacta ✅ (b) más adelante
- 2026-10-31 16:54 · 11. a, ligado a esto estará el motor documental que gestionará todos los documentos almacenados en ProContaVF (facturas compra, venta, contratos, etc.)
- 2026-10-31 16:54 · answer: P-12 versiones inmutables → (a) sí desde el principio; ligado a ello, el motor documental gestionará todos los documentos almacenados en ProContaVF
