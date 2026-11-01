---
title: Perfiles, notas e historial
depends_on: [terceros/tercero, seguridad/permisos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 10:46 · Desarrollar la pantalla de Terceros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GVR2rh2hJF6h9BMYjKSu8jn
---
## Summary
Perfiles de clasificación N:M independientes de roles, notas y notas internas, e historial de cambios por operador. Código: `CatalogoPerfilesAccess` (listar y dar de alta perfiles) y las asignaciones, que forman parte de la ficha del tercero y se guardan con ella.
**Aceptación:** repetir el estado actual no genera movimientos duplicados.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Los perfiles se guardan con el Guardar/Descartar de la ficha, como el resto de la ficha (resuelve RF-TER-13).
- 2026-11-01 10:46 · Asignaciones guardadas con la ficha: retirar pone Activo a falso y la fecha de retirada; reasignar reactiva la misma fila; repetir el estado actual no escribe nada ni deja historial; un perfil inactivo conserva sus asignaciones pero no admite nuevas. Alta de perfiles con nombre único desde la ficha («Nuevo perfil…»). La pestaña Perfiles tiene dos listas con filtro, selección múltiple, doble clic, Intro, flechas y arrastre.
- 2026-11-01 10:46 · Notas y Notas internas son dos campos de la pestaña Notas, sin capa de seguridad: las ven y editan todos los operadores (decisión del propietario, terceros/pantallas 2026-11-01 09:55). El historial se ve en una tabla con Exportar a Excel.
- 2026-11-01 15:48 · Validado por el propietario (pantalla de Terceros y revisión 1).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7.2 RF-TER-12..14: perfiles, notas, historial
- 2026-10-28 12:28 · draft: Implementar perfiles, notas e historial del tercero.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-TER-13 ¿los perfiles se guardan al instante o con el Guardar de la ficha? (a) con el Guardar/Descartar de la ficha, para unificar ✅ (b) al instante, como el prototipo
- 2026-10-31 16:54 · 32. a
- 2026-10-31 16:54 · answer: RF-TER-13 guardado de perfiles → (a) con el Guardar/Descartar de la ficha
- 2026-11-01 10:46 · Derived from terceros/pantallas: perfiles, notas e historial en la ficha.

## Hand-over
- 2026-11-01 10:46 · Hecho: perfiles, notas e historial en la ficha, con pruebas. Pendiente: validación del propietario (el arrastre de perfiles solo se comprueba a mano).
