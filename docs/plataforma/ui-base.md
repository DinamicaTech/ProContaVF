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
- 2026-10-29 12:32 · Los monitores de 2560×1440 trabajan con la escala de Windows al 100 %: el área útil de diseño es de 2560×1440 píxeles reales.
- 2026-10-29 16:58 · Todo importe se muestra en euros con separador de miles y dos decimales (1.234,56 €), en rejillas, totales, diálogos y tooltips; los campos de entrada mantienen el número sin símbolo. Una sola moneda: las cuentas bancarias están en euros o ya convertidas.
- 2026-10-29 17:18 · [replaced by 2026-10-31 09:58] Las columnas de todas las rejillas se redimensionan arrastrando el borde de su cabecera y con doble clic se ajustan al contenido; los anchos se guardan por operador y pantalla, y el menú contextual de la cabecera ofrece «Restablecer columnas» para volver a los de fábrica. La rejilla se desplaza en horizontal cuando no cabe.
- 2026-10-31 09:58 · Criterio global de todas las tablas y rejillas: las columnas se redimensionan arrastrando el borde de su cabecera y con doble clic se ajustan al contenido; se ordena por cualquier columna con clic en su título (ascendente, descendente y vuelta al orden de fábrica); se mueven arrastrando el título o con Alt+← →; el menú contextual de la cabecera permite mover u ocultar una columna, elegir las columnas visibles (algunas vienen ocultas de fábrica) y «Restablecer columnas» (orden, visibles y anchos de fábrica). Todo se guarda por operador y pantalla. Toda tabla tiene «Exportar a Excel» con todas las filas de la vista y las mismas columnas, en el mismo orden. La rejilla se desplaza en horizontal cuando no cabe. Ordenar o mover columnas no recalcula ningún dato: un saldo acumulado conserva en cada fila el valor calculado por fecha. Se aplica en el componente de tabla de la biblioteca WPF; los prototipos ya validados no se retocan (el Mayor lo muestra completo).
- 2026-10-31 10:14 · Validado el prototipo del Mayor (contabilidad/mayor/prototipo, lienzo versión 29): con él quedan validados todos los prototipos HTML de las pantallas principales. Siguiente paso: la biblioteca de estilos y componentes WPF con su pantalla de muestra.
- 2026-10-31 10:16 · Plan de la biblioteca WPF acordado: proyecto `src/ProConta.UI.Base` (WPF, .NET 10, x64) con los tokens de las maquetas (colores, tipografía y tamaños), iconos y estilos de todos los controles, y los componentes comunes (tabla con el criterio global de columnas y «Exportar a Excel», separador arrastrable, estados vacío, error y carga, diálogos, menús y tooltips), más una aplicación de muestra con todos ellos para compararla con las maquetas a 2560×1440. Dependencias aprobadas: ClosedXML (MIT) para exportar a Excel y las fuentes IBM Plex Sans, IBM Plex Mono y Bricolage Grotesque (OFL) incluidas en el programa. Las preferencias de cada operador (columnas, paneles) se guardan a través de una interfaz; de momento en un fichero local y, cuando exista su tabla, en la base de datos, cambio que hace datos/esquema. Se compila y se prueba en el equipo del propietario con una sesión remota; su prototipo TercerosWPF solo se consulta. Los prototipos validados se fusionan antes de empezar y la biblioteca va en una rama nueva.
- 2026-10-31 09:58 · Todo saldo contable se muestra con signo, deudor en positivo y acreedor en negativo, sin sufijo D/H; la ayuda dice «Saldo deudor» o «Saldo acreedor». Color según el grupo del PGC de la cuenta: grupos 4 y 5, saldo deudor en verde (a favor de la sociedad: el banco tiene dinero, el cliente debe) y acreedor en rojo (deuda con proveedores o Hacienda); grupos 6 a 9, al revés (gasto en rojo, ingreso en verde); grupos 1, 2 y 3, en negro. Si se mezclan cuentas de varios grupos, en negro.

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
- 2026-10-29 12:32 · answer: ¿qué escala de Windows usan los monitores de 2560×1440? → 100 %
- 2026-10-29 16:37 · Siempre aplicar currency a importes.
- 2026-10-29 16:37 · En un principio, las cuentas bancarias serán siempre en euros o se habrán convertido a euros, trabajar en contabilidad con dos monedas a la vez puede ser complicado.
- 2026-10-29 17:18 · Sí, el comportamiento es global y persistente por operador con una opción de resetear configuración (puede ser un contextual en la cabecera) por si el operador hace un estropicio
- 2026-10-31 09:35 · El Saldo, en lugar de un sufijo 'D' o 'H': en Rojo si es 'D' o en Verde si es 'H' (la idea es que verde es bueno para la sociedad (saldo a su favor) y rojo malo (saldo en contra), no tengo claro si siempre 'D' sería rojo (quizá depende de la cuenta contable).
- 2026-10-31 09:35 · Asumo que se puede ordenar por columnas, redimensionar y mover, como en todas las tablas
- 2026-10-31 09:45 · El signo siempre, claro. Podemos usar color negro para el grupo 1. En 6 y 7 sigue teniendo sentido el color, no ?
- 2026-10-31 09:45 · Ok a aplicarlo como regla generar a todas las tablas, añade también la exportación a Excel tal y como lo tienes en esta. Al reordenar no recalculamos el saldo (no sé si tendría sentido)
- 2026-10-31 09:48 · Todas las tablas: Ok, si, no hace falta rehacer nada, solo marcarlo como criterio global

## Hand-over
- 2026-10-29 10:20 · Hecho: acceso al lienzo confirmado, decisión HTML frente a WPF, desglose acordado y requisitos derivados en cada nodo de pantalla. Mayor antiguo exportado y analizado (`sources/proconta-antiguo/mayor/ANALISIS-FUNCIONAL.md`). Pendiente: validar los prototipos en el orden acordado, crear los de importación de compras y Mayor, y después la biblioteca WPF con su pantalla de muestra.
- 2026-10-31 10:16 · 1. Ok 2. Ok 3. Ok a ClosedXML y fuentes 4. Ok 5. Ok
