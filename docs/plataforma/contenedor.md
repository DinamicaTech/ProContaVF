---
title: Contenedor principal
status: draft
depends_on: [plataforma/ui-base, seguridad/acceso]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Aplicación principal (UI-PRINCIPAL): cabecera con empresa, ejercicio y operador, navegación lateral plegable, búsqueda de pantallas y acciones, área de trabajo con pestañas y gestión de cambios pendientes al cambiar de ficha o contexto.
**Aceptación:** la de spec § 6: comparación con la maqueta incluyendo vacíos, errores, campos largos, listas grandes y cambios pendientes.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 6 RF-UI-01, 04, 05, 07: contenedor, avisos técnicos discretos, ciclo de vida de fichas y rendimiento
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/contratos/UI-PRINCIPAL.md y ACUERDOS_CONTENEDOR_WPF.md: contrato anterior
- 2026-10-28 12:28 · From sources/maquetas/Contenedor/: maqueta del contenedor
- 2026-10-28 12:28 · draft: Construir el contenedor WPF según la maqueta del contenedor.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final del contenedor (lienzo «ProConta · Contenedor principal») antes de construir su UI WPF.
- 2026-10-29 13:04 · Veo la opción de 'Ampliar área de trabajo'. Quizá es más sencillo un slider para encoger la parte los menús de la izquierda y una hotkey global para ocultar/mostrar el menú (nunca se ocultará del todo, siempre quedará el slider para poderlo desocultar sin saber la hotkey).
- 2026-10-29 16:37 · Igual que en la pantalla anterior, el 'Ampliar área de trabajo' un slider entre los menús y el formulario
