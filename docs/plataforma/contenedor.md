---
title: Contenedor principal
status: draft
depends_on: [plataforma/ui-base, seguridad/acceso]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Aplicación principal (UI-PRINCIPAL): cabecera con empresa, ejercicio y operador, navegación lateral plegable, búsqueda de pantallas y acciones, área de trabajo con pestañas y gestión de cambios pendientes al cambiar de ficha o contexto.
**Aceptación:** la de spec § 6: comparación con la maqueta incluyendo vacíos, errores, campos largos, listas grandes y cambios pendientes.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 16:58 · El separador entre el menú lateral y la pantalla sustituye a «Ampliar área de trabajo»: se arrastra para cambiar el ancho (por debajo de 150 px queda la franja de iconos y, hasta el borde, el menú se oculta), doble clic o Intro lo oculta o lo muestra, y oculto deja una franja visible con el asa para recuperarlo. La barra de estado ya no se oculta. Atajo global provisional Ctrl+Mayús+M, pendiente del catálogo de `plataforma/atajos-de-teclado`.
- 2026-10-30 15:03 · Doble pantalla: una pantalla puede desacoplar un panel auxiliar a una ventana propia del programa, que el operador lleva a la segunda pantalla (a pantalla completa si quiere); el panel sigue sincronizado con la pantalla de origen y su hueco lo aprovechan los demás paneles; un botón lo vuelve a acoplar. Primer caso: el visor de PDF de `compras/pantallas`. Los separadores arrastrables entre paneles guardan su posición por operador y vuelven a la de fábrica con doble clic.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 6 RF-UI-01, 04, 05, 07: contenedor, avisos técnicos discretos, ciclo de vida de fichas y rendimiento
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/contratos/UI-PRINCIPAL.md y ACUERDOS_CONTENEDOR_WPF.md: contrato anterior
- 2026-10-28 12:28 · From sources/maquetas/Contenedor/: maqueta del contenedor
- 2026-10-28 12:28 · draft: Construir el contenedor WPF según la maqueta del contenedor.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final del contenedor (lienzo «ProConta · Contenedor principal») antes de construir su UI WPF.
- 2026-10-29 13:04 · Veo la opción de 'Ampliar área de trabajo'. Quizá es más sencillo un slider para encoger la parte los menús de la izquierda y una hotkey global para ocultar/mostrar el menú (nunca se ocultará del todo, siempre quedará el slider para poderlo desocultar sin saber la hotkey).
- 2026-10-29 16:37 · Igual que en la pantalla anterior, el 'Ampliar área de trabajo' un slider entre los menús y el formulario
- 2026-10-30 14:59 · La idea será poder hacer un undock del visualizador de PDF para poderlo mover a la segunda pantalla a full screen y así ganar todo ese espacio como más anchura de la lista de ficheros.
- 2026-10-31 11:20 · Derived from (root): la navegación del contenedor da acceso a todas las funciones del menú principal antiguo que se conservan (sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 2, con el reparto por nodo de la decisión de la raíz de 2026-10-29 17:50); los procesos que generan asientos se lanzan como acciones con permiso propio.
