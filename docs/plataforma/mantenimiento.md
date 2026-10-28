---
title: Mantenimiento de parámetros
status: draft
depends_on: [plataforma/ui-base, seguridad/permisos]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Pantalla genérica y extensible (CFG-MANT) para parámetros únicos y colecciones 1:N, agrupados por categorías, alimentada por proveedores que publica cada área dueña de sus datos; no adquiere la propiedad de las tablas.
**Aceptación:** los ejemplos de aceptación de spec § 8.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 8 RF-MAN-01..05: pantalla por proveedores, editores, servicios tipados para datos con invariantes, catálogo conocido
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/WpfConfiguracion/docs/: contrato e integración anteriores
- 2026-10-28 12:28 · From sources/maquetas/Configuracion/: maqueta de Configuración
- 2026-10-28 12:28 · draft: Construir el mantenimiento genérico con los proveedores del catálogo conocido.
- 2026-10-28 12:28 · question: RF-MAN-05 ¿ámbito de cada proveedor (global, sociedad o ejercicio)? (a) lo declara cada proveedor y el servicio lo aplica ✅ (b) todos por sociedad
