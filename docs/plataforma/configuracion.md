---
title: Configuración global
status: draft
depends_on: [datos/esquema]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Resolutor único de la ubicación de datos y de la ruta exacta de la BD (CFG-GLOBAL), con la configuración local guardada en el perfil del usuario de Windows, fuera de Access; comprueba versión y compatibilidad del esquema al arrancar.
**Aceptación:** la de spec § 4: selección cancelada no altera nada, ruta inválida informa ubicación y acción, el arranque no crea ni migra otra BD.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 4 RF-CFG-01..05: resolutor único, precedencia de fuentes, configuración fuera de Access, comprobación de esquema al arrancar
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/ConfiguracionGlobal/docs/CONTRATO_PUBLICO.md y CONFIGURACION_DATOS.md: contrato anterior
- 2026-10-28 12:28 · draft: Implementar la resolución de la ruta de datos y la comprobación de compatibilidad del esquema al arrancar.
