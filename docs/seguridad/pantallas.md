---
title: Pantallas de seguridad
status: draft
depends_on: [seguridad/acceso, plataforma/ui-base, seguridad/permisos, terceros/roles, terceros/pantallas, datos/esquema]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:30 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-11-24 11:20 · Gestionar operadores como rol de terceros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPG4YWMLdxtTEKh34oaV7be
---
## Summary
Pantalla de acceso compacta en cuatro fases y mantenimiento de operadores y grupos. La pantalla de acceso y el asistente del primer administrador están en `src/ProConta.App` (VentanaAcceso, VentanaPrimerAdministrador). Los operadores se mantienen como el rol «Operador ProConta» de un tercero persona física, en la pestaña Roles de su ficha (solo para administradores): activo, grupos y contraseña; el nombre de acceso es su nombre de pila, con apellidos si coincide con otro (`ServicioOperadores` en src/ProConta.Seguridad, `IOperadoresFicha` en src/ProConta.Terceros.UI, `OperadoresFichaTercero` en src/ProConta.App). Pendiente: el icono por fase del acceso; el mantenimiento de grupos y de sus permisos va en el borrador seguridad/pantallas/grupos-y-permisos.
**Aceptación:** todas las fases utilizables por teclado; comparación con la maqueta.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 19:30 · Pantalla de acceso con el aspecto de la propuesta «Dividida» de la maqueta (960×640, franja azul con «Vas a trabajar en» sociedad, ejercicio y operador, y la versión), sin marco, movible desde las zonas libres, con «Salir» visible. El orden de los campos sigue RF-SEG-01 y seguridad/acceso, no el de la maqueta (que pedía primero la sociedad): Operador y Contraseña y solo entonces Sociedad (las autorizadas, con la última elegida propuesta) y Ejercicio (los cerrados marcados «cerrado»), «Entrar». «Entrar con otro operador» vuelve a la identificación. Intro avanza, Esc sale.
- 2026-10-31 19:30 · [replaced by seguridad/acceso 2026-11-01 09:30] El operador se escribe (no se elige de una lista), para no mostrar los nombres de los operadores antes de autenticar. Los errores aparecen en un recuadro con mensaje, causa y qué hacer, y nunca dicen qué dato ha fallado.
- 2026-11-01 09:30 · La pantalla de acceso sigue el flujo y el parámetro de la lista de operadores de seguridad/acceso (decisiones 2026-11-01 09:30): botón «Entrar» desde el principio, bloque de sociedad y ejercicio oculto hasta identificarse y foco en «Entrar» al aparecer.
- 2026-11-14 20:06 · Asistente del primer administrador: el campo se llama «Contraseña (opcional)» y su ayuda dice que puede quedar vacía y que cualquier contraseña vale; ya no se avisa de un mínimo de caracteres (criterio de seguridad/permisos 2026-11-14 20:06).
- 2026-11-24 11:20 · Mantenimiento de operadores como rol de terceros (propietario 2026-11-24 10:07 y 10:43): cada operador es el rol «Operador ProConta» de un tercero persona física con nombre. OperadoresSeguridad deja de guardar nombre y correo (salen del tercero) y apunta a su tercero (IdTercero, obligatorio y único; esquema 82 en datos/esquema). El rol está en la pestaña Roles de la ficha (terceros/pantallas): Activar/Desactivar, IdOperador, nombre de operador (solo lectura), «Contraseña…» (al momento, vacía la quita) y los grupos activos como casillas; el rol y los grupos se guardan con la ficha, justo después del tercero, y se comprueban antes de guardar nada. Quitar el rol desactiva el operador, nunca lo borra.
- 2026-11-24 11:20 · Quién lo gestiona: solo quien tiene la acción SEGURIDAD.OPERADORES; mientras no tenga grupos asignados, solo los miembros activos del grupo ADMINISTRADORES (seguridad/permisos). Los demás no ven el rol en la ficha. Nadie se quita a sí mismo el rol ni el grupo ADMINISTRADORES, y no se puede dejar ProContaVF sin ningún administrador activo. La contraseña la cambia quien gestiona operadores, o cada operador la suya.
- 2026-11-24 11:20 · Asistente del primer administrador: pide nombre, primer y segundo apellido (opcionales) y contraseña (opcional); crea el tercero persona física, le da el rol de operador y lo pone en ADMINISTRADORES, en una transacción.
- 2026-11-24 11:20 · Nombre de acceso (seguridad/acceso): el nombre de pila del tercero; si otro operador, activo o no, se llama igual (sin distinguir mayúsculas), se añade el primer apellido y, si siguen coincidiendo, el segundo; si aún coinciden, el número del tercero entre paréntesis. Se compara con todos los operadores para que el nombre de uno no cambie al activar o desactivar a otro.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-ACC-01: login compacto sin marco, movible, opción Salir, icono por fase
- 2026-10-28 12:28 · From sources/maquetas/acceso-proconta.html: maqueta de Acceso
- 2026-10-28 12:28 · draft: Construir la pantalla de acceso según la maqueta y el mantenimiento de operadores.
- 2026-10-31 17:53 · Derived from architecture: la pantalla de acceso del contenedor y el asistente del primer administrador.
- 2026-11-14 20:06 · Derived from seguridad/permisos: la contraseña es opcional y sin requisitos de longitud ni complejidad, así que las pantallas no piden un mínimo de caracteres.
- 2026-11-24 10:07 · 1. Sí, la idea es que solo alguien del grupo administradores puede asignar/desasignar este rol (mientras no entremos más a fondo con la matriz de permisos)
- 2026-11-24 10:07 · Otra opción es crear un tercero con el rol de operador administrador al crear el primer operador en un Proconta vacío

## Hand-over
- 2026-11-01 09:30 · Hecho: pantalla de acceso con el flujo acordado el 01/11 y asistente del primer administrador. Pendiente: mantenimiento de operadores y grupos; icono por fase.
- 2026-11-24 11:20 · Hecho: operadores como rol de terceros (rama claude/project-thread-8hkcxo), con pruebas. Pendiente: validación del propietario; icono por fase; grupos y permisos en el borrador seguridad/pantallas/grupos-y-permisos.
