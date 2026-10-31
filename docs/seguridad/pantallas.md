---
title: Pantallas de seguridad
status: draft
depends_on: [seguridad/acceso, plataforma/ui-base]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:30 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Pantalla de acceso compacta en cuatro fases y mantenimiento de operadores y grupos. La pantalla de acceso y el asistente del primer administrador están en `src/ProConta.App` (VentanaAcceso, VentanaPrimerAdministrador); el mantenimiento de operadores y grupos está pendiente.
**Aceptación:** todas las fases utilizables por teclado; comparación con la maqueta.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 19:30 · Pantalla de acceso con el aspecto de la propuesta «Dividida» de la maqueta (960×640, franja azul con «Vas a trabajar en» sociedad, ejercicio y operador, y la versión), sin marco, movible desde las zonas libres, con «Salir» visible. El orden de los campos sigue RF-SEG-01 y seguridad/acceso, no el de la maqueta (que pedía primero la sociedad): Operador y Contraseña, «Continuar», y solo entonces Sociedad (las autorizadas, con la última elegida propuesta) y Ejercicio (los cerrados marcados «cerrado»), «Entrar». «Entrar con otro operador» vuelve a la identificación. Intro avanza, Esc sale.
- 2026-10-31 19:30 · El operador se escribe (no se elige de una lista), para no mostrar los nombres de los operadores antes de autenticar. Los errores aparecen en un recuadro con mensaje, causa y qué hacer, y nunca dicen qué dato ha fallado.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-ACC-01: login compacto sin marco, movible, opción Salir, icono por fase
- 2026-10-28 12:28 · From sources/maquetas/acceso-proconta.html: maqueta de Acceso
- 2026-10-28 12:28 · draft: Construir la pantalla de acceso según la maqueta y el mantenimiento de operadores.
- 2026-10-31 17:53 · Derived from architecture: la pantalla de acceso del contenedor y el asistente del primer administrador.

## Hand-over
- 2026-10-31 19:30 · Hecho: pantalla de acceso y asistente del primer administrador. Pendiente: que el propietario confirme el orden de los campos y escribir el operador frente a elegirlo de una lista; mantenimiento de operadores y grupos; icono por fase.
