---
title: Base visual WPF
status: draft
depends_on: [plataforma/contenedor, terceros/pantallas, norma43/pantallas, facturacion/pantallas, compras/pantallas, plataforma/mantenimiento, contabilidad/pantallas, contabilidad/mayor, contabilidad/mayor/prototipo, contabilidad/asientos]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Estilos, recursos y componentes WPF compartidos: tipografía, colores, separaciones, pestañas, listas, estados, botones, foco, validaciones y ayudas. Fija el ancho de campo según contenido y las pautas de accesibilidad.
Dirige la validación de los prototipos HTML de las pantallas principales (contenedor, Terceros, líneas Norma 43, Facturas emitidas, importación de facturas de compra, mantenimiento 1:N con configuración y Mayor), de la que sale la biblioteca WPF.
**Aceptación:** cada pantalla principal tiene su prototipo HTML validado por el propietario; una pantalla de muestra con todos los componentes WPF comparada con las maquetas a igual escala.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 10:20 · Los prototipos se hacen y se validan en HTML; WPF solo para la versión definitiva. Motivo: se cambian en minutos, el propietario los ve y comenta en el lienzo sin compilar y se pueden capturar a 1440×900 y 1280×800 desde cualquier hilo; WPF solo compila en el equipo del propietario. Los prototipos se limitan a patrones que WPF resuelve sin trucos.
- 2026-10-29 10:20 · Prototipos de referencia: el lienzo de diseño «ProConta · Contenedor principal» (cuenta del propietario; 27 pantallas, igual a `sources/maquetas/Contenedor/`) y las maquetas de `sources/maquetas/` (Facturas, Configuración, Correo, CorreoLotes, Acceso). Las pantallas nuevas (importación de facturas de compra, Mayor) se añaden al mismo lienzo.
- 2026-10-29 10:20 · Orden de validación: contenedor con Terceros, Facturas emitidas (listado y detalle), líneas Norma 43, mantenimiento 1:N con configuración, importación de facturas de compra y Mayor. Cada nodo de pantalla construye su UI WPF solo después de validar su prototipo.
- 2026-10-29 10:20 · Punto de partida de la biblioteca: los tokens de las maquetas HTML (IBM Plex Sans, IBM Plex Mono y Bricolage Grotesque; paleta azul marino #14213A y acento #F4A259) y el prototipo WPF anterior `ProConta/Claude outputs/TercerosWPF` del disco del propietario (Themes/Tokens.xaml, Icons.xaml y Controls.xaml), que solo se consulta.
- 2026-10-29 10:30 · Reglas visuales comunes: todo control lleva un tooltip descriptivo; cada campo mide lo que su contenido; los textos no se recortan si hay espacio para mostrarlos completos; no se escriben palabras enteras en mayúsculas, ni por estilo (encabezados de columna o de sección), salvo siglas como NIF o CIF.

- 2026-10-29 10:30 · Errores de validación de campo: el campo se marca con borde rojo e icono y el mensaje completo va en su tooltip (al pasar el ratón o al enfocarlo) y en el resumen superior con enlace a cada campo; ningún campo cambia de tamaño ni lleva texto de error debajo. Los cuadros de mensaje se reservan para errores que bloquean y piden una decisión (por ejemplo, un guardado fallido) y la barra de estado para avisos que no bloquean.
- 2026-10-29 10:58 · Validado por el propietario el prototipo del contenedor con Terceros (lienzo «ProConta · Contenedor principal», versión 16, igual a `sources/maquetas/Contenedor/`), con la ronda 1 de cambios aplicada; la pestaña Contabilidad de la ficha queda pendiente del prototipo del Mayor.
- 2026-10-29 11:38 · Resolución de diseño: 2560×1440 (los equipos del propietario trabajan con dos pantallas de esa resolución). A resoluciones menores las pantallas solo deben seguir siendo funcionales. Excepción a la regla de no recortar textos: en las columnas de texto largo de una tabla se muestra el inicio del texto con «…» y el texto completo en el tooltip.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 6 RF-UI-02, 03, 06: look and feel de las maquetas con estilos compartidos, campos del ancho de su contenido, accesibilidad y mensajes
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/contratos/UI-BASE.md: contrato anterior
- 2026-10-28 12:28 · From sources/maquetas/: maquetas HTML aprobadas
- 2026-10-28 12:28 · draft: Crear la biblioteca de estilos y componentes WPF compartidos a partir de las maquetas.
- 2026-10-29 09:33 · Confirma que tengas acceso a este repositorio ya confirma si nos es más útil trabajar prototipos en HTML que en WPF.
- 2026-10-29 09:33 · El objetivo es validar los prototipos finales de las pantallas principales (contenedor principal, terceros, Lineas N43, Facturas emitidas (listado y detalle de factura), Importación de Facturas de compra, Pantalla genérica de mantenimiento de tablas 1:N + configuración y Mayor contable) y a partir de esta validación, crear la biblioteca de estilos y componentes WPF compartidos e ir implementando la UI de los diversos nodos.
- 2026-10-29 10:21 · Sí, la idea es precisamente mejorar el Proconta actual
- 2026-10-29 10:21 · Ajustarlos al tamaño real
- 2026-10-29 10:21 · No tengo claro que el campo CP deba contener un mensaje de error pasivo. Tenemos otros mecanismos como Msgbox para errores inmediatos, Status bar para avisos o incluso un pequeño label debajo del campo. Si hemos de adaptar todos los campos para contener descriptivos de error quedará algo raro
- 2026-10-29 10:21 · Ok, los mostraremos sin recortar
- 2026-10-29 10:21 · Normalmente intentaremos evitar palabras completas en mayúsculas (excepto casos como NIF, CIF, etc.)
- 2026-10-29 10:23 · answer: ¿cómo se muestran los errores de validación de los campos? → borde rojo y tooltip
- 2026-10-29 11:38 · La resolución de las pantallas (al menos las nuestras) que usarán ProContaVF es de 2560 x 1440, no vamos a condicionar el diseño de pantalla por resoluciones inferiores mientras sigan siendo funcionales.
- 2026-10-29 11:38 · Sí, en las columnas con texto largo, mostramos el inicio del texto con ... y el texto completo en ToolTip.

## Hand-over
- 2026-10-29 10:20 · Hecho: acceso al lienzo confirmado, decisión HTML frente a WPF, desglose acordado y requisitos derivados en cada nodo de pantalla. Mayor antiguo exportado y analizado (`sources/proconta-antiguo/mayor/ANALISIS-FUNCIONAL.md`). Pendiente: validar los prototipos en el orden acordado, crear los de importación de compras y Mayor, y después la biblioteca WPF con su pantalla de muestra.
