---
title: Contenedor principal
depends_on: [plataforma/ui-base, seguridad/acceso]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-10-31 19:30 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Aplicación principal (UI-PRINCIPAL): cabecera con empresa, ejercicio y operador, navegación lateral plegable, búsqueda de pantallas y acciones, área de trabajo con pestañas y gestión de cambios pendientes al cambiar de ficha o contexto. Primera versión en `src/ProConta.App` (ejecutable ProContaVF.exe; `ejecutar.cmd` en la raíz): arranque con resolución de la BD y comprobación del esquema, asistente del primer administrador, pantalla de acceso y ventana principal con la cabecera, la navegación (solo Inicio activo), la pestaña Inicio y la barra de estado con la BD, la versión del esquema y la versión del programa.
**Aceptación:** la de spec § 6: comparación con la maqueta incluyendo vacíos, errores, campos largos, listas grandes y cambios pendientes.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 16:58 · El separador entre el menú lateral y la pantalla sustituye a «Ampliar área de trabajo»: se arrastra para cambiar el ancho (por debajo de 150 px queda la franja de iconos y, hasta el borde, el menú se oculta), doble clic o Intro lo oculta o lo muestra, y oculto deja una franja visible con el asa para recuperarlo. La barra de estado ya no se oculta. Atajo global provisional Ctrl+Mayús+M, pendiente del catálogo de `plataforma/atajos-de-teclado`.
- 2026-10-30 15:03 · Doble pantalla: una pantalla puede desacoplar un panel auxiliar a una ventana propia del programa, que el operador lleva a la segunda pantalla (a pantalla completa si quiere); el panel sigue sincronizado con la pantalla de origen y su hueco lo aprovechan los demás paneles; un botón lo vuelve a acoplar. Primer caso: el visor de PDF de `compras/pantallas`. Los separadores arrastrables entre paneles guardan su posición por operador y vuelven a la de fábrica con doble clic.
- 2026-10-31 19:30 · Arranque de ProContaVF.exe: resuelve la BD con plataforma/configuracion (argumento `--bd`, variable PROCONTA_BD o configuración del puesto); si no hay o no vale, explica el motivo y ofrece «Elegir base de datos…» (cancelar no cambia nada y la elegida se guarda en el perfil del usuario); comprueba que el esquema es el del programa y, si no, explica qué hacer y sale sin migrar nada; si la BD no tiene operadores, abre el asistente del primer administrador; después, la pantalla de acceso y la ventana principal.
- 2026-10-31 19:30 · Ventana principal: cabecera azul marino con la marca, la sociedad y el ejercicio de trabajo y el operador; navegación lateral con las áreas del menú (las que aún no tienen pantallas se ven atenuadas con un tooltip que lo explica); separador arrastrable entre menú y área de trabajo, que con doble clic o Ctrl+Mayús+M oculta o muestra el menú y queda siempre visible; pestaña Inicio con el saludo, el contexto (avisa si el ejercicio está cerrado) y lo que contiene la versión; barra de estado siempre visible con la BD conectada (ruta completa en el tooltip), la versión del esquema y la versión del programa (InformationalVersion).
- 2026-10-31 19:30 · Al salir se cierra la sesión y el proceso termina con `BaseDatosAccess.TerminarProceso`, para evitar el fallo del ACE de Office al descargarse (datos/persistencia).
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).

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
- 2026-10-31 17:53 · Derived from architecture: el contenedor WPF ejecutable con la pantalla de acceso, que muestra su versión; es el punto de validación de los desarrollos base.

## Hand-over
- 2026-10-31 19:30 · Hecho: ejecutable con arranque, primer administrador, acceso y ventana principal; revisado renderizando las ventanas fuera de pantalla contra una copia de la maestra en la versión 6. Pendiente (próximas entregas): búsqueda «Ir a», pestañas de pantallas con cambios pendientes, cambio de sociedad o ejercicio sin volver a entrar, franja de iconos por debajo de 150 px y posición del separador guardada por operador, barra de título propia de la maqueta, doble pantalla y el resto del catálogo de atajos.
