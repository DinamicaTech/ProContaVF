---
title: Atajos de teclado
depends_on: []
status: draft
threads:
  - 2026-10-29 16:34 · Diseñar atajos de teclado | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GV1VwrZgQybpW6Pw4fub1Tr
---
## Summary
Diseña y gestiona los atajos de teclado de todas las operativas que el operador ejecuta a mano (menús, cinta, menús contextuales y botones). Cada módulo publica sus operativas en un catálogo único (id como `LineasN43.ExportarAContabilidad` y texto descriptivo); de ese catálogo salen el atajo, su ejecución y su presentación en menús, cinta, tooltips y búsqueda de acciones.
Primero un catálogo fijo; después, una pantalla de mantenimiento con atajos comunes a todos los operadores.
**Aceptación:** toda operativa publicada se ejecuta con su atajo desde la pantalla donde aplica, el atajo se ve en sus menús, cinta y tooltips sin escribirlo a mano, y no hay dos operativas con la misma tecla en un mismo ámbito.

## Decisions
- 2026-10-29 16:34 · Creado como hijo de `plataforma`. El padre ya es carpeta (`plataforma.md` junto a `plataforma/`), como el resto del árbol, así que no se mueve a `plataforma/README.md`.
- 2026-10-29 16:34 · Catálogo único de operativas: cada módulo registra al arrancar sus operativas con un id único `<Pantalla o área>.<Operativa>` (por ejemplo `LineasN43.ExportarAContabilidad`), un texto descriptivo para el operador (por ejemplo «Exportará las líneas seleccionadas como asientos contables»), su ámbito (global o una pantalla), su tecla por defecto y su condición de ejecución (selección simple, múltiple o ninguna). Menús fijos, menús contextuales, cinta y búsqueda de acciones del contenedor leen ese mismo catálogo; ninguna pantalla define una tecla por su cuenta.
- 2026-10-29 16:34 · Interceptación: un único punto de escucha en la ventana del contenedor. Orden de prioridad: el control con foco conserva sus teclas de edición (Ctrl+C, V, X, Z, A en textos, y las de un editor de texto enriquecido), después la pestaña activa y por último las operativas globales. Si la operativa existe pero no se puede ejecutar en ese momento, la barra de estado dice por qué y cómo conseguirlo (por ejemplo «Selecciona al menos una línea»), en lugar de no hacer nada. En WPF se apoya en comandos con sus gestos de entrada.
- 2026-10-29 16:34 · Presentación: como todo sale del catálogo, cada entrada de menú, menú contextual y botón de cinta muestra su atajo y su tooltip lo incluye («Exportar a contabilidad · Ctrl+E»). Una ayuda de teclado lista los atajos de la pantalla activa y los globales.
- 2026-10-29 16:34 · Conflictos: no se asignan teclas reservadas por Windows o por Escritorio remoto (Alt+Tab, Alt+F4, Ctrl+Alt+Supr, tecla Windows) ni las de edición de texto; una misma tecla no puede estar en una operativa global y en una de pantalla, ni en dos operativas del mismo ámbito. Pantallas distintas sí pueden reutilizar una tecla. Choques ya detectados en las maquetas que hay que resolver en el catálogo inicial: Ctrl+B (negrita en Correo y propuesta para plegar el menú lateral) y Ctrl+K (enlace en Correo y búsqueda en Configuración).
- 2026-10-29 16:34 · Fases: primero un catálogo fijo, cuyas teclas por defecto parten de los atajos provisionales de las maquetas (`sources/maquetas/`: Contenedor, Facturas, Configuración, Correo, CorreoLotes, Líneas N43). Después, la pantalla de mantenimiento de atajos, como proveedor de la pantalla genérica `plataforma/mantenimiento`: lista de operativas con id, texto, tecla por defecto y tecla asignada, captura de la tecla pulsándola, aviso de conflicto con la corrección propuesta y restaurar valores por defecto.
- 2026-10-29 16:34 · Los atajos personalizados son comunes a todos los operadores, no por operador.
- 2026-10-29 16:34 · El id de operativa podrá servir también como identificador de permiso en `seguridad/permisos`, pero en la versión mínima viable no se aplica seguridad a las operativas.
- 2026-10-29 16:45 · Diseño validado por el propietario (catálogo, interceptación, presentación, conflictos y fases). El nodo sigue en borrador por su tarea pendiente: el catálogo fijo inicial y la pantalla de mantenimiento.

## Requirements
- 2026-10-29 16:29 · Todas las operativas de los diferentes módulos ejecutables manualmente por el operador (incluyendo menús y botones 'ribbon') son candidatas a tener un acceso directo de teclado.
- 2026-10-29 16:29 · Este nodo se encargará de diseñar y gestionar las teclas de acceso directo de todas estas operativas.
- 2026-10-29 16:29 · Los nodos funcionales informarán a este nodo de sus operativas publicables (con un descriptor único, por ejemplo, LineasN43.ExportarAContabilidad y un descriptor friendly, por ejemplo, 'Exportará las líneas seleccionadas como asientos contables') para que este las incorpore a la pantalla de mantenimiento de atajos de teclado.
- 2026-10-29 16:29 · Lo que ya no sé es cómo se interceptarán y ejecutarán estos atajos de teclado (e informará del acceso directo en menú contextuales y fijos). Acepto sugerencias.
- 2026-10-29 16:33 · answer: ¿atajos personalizados por operador o comunes? → Atajos comunes
- 2026-10-29 16:33 · answer: ¿el id de operativa sirve también como permiso? → Lo podemos aprovechar, aunque mi idea es no aplicar seguridad en la versión mínima viable.
- 2026-10-29 16:34 · draft: Definir el catálogo fijo inicial de operativas y atajos a partir de las maquetas, resolviendo los choques Ctrl+B y Ctrl+K, y después construir la pantalla de mantenimiento de atajos comunes como proveedor de plataforma/mantenimiento.

## Hand-over
- 2026-10-29 16:34 · Hecho: nodo creado con el diseño de catálogo, interceptación, presentación, conflictos y fases. Diseño validado a las 16:45. Pendiente: añadir las líneas derivadas en `plataforma/contenedor` (punto único de escucha y atajo visible en menús, cinta, menús contextuales y búsqueda de acciones), `plataforma/ui-base` (cómo se muestra el atajo en menús y tooltips) y `plataforma/mantenimiento` (proveedor de atajos) cuando se fusione la rama del hilo «Validar prototipos de pantallas principales»; los nodos de pantalla reciben «publicar sus operativas en el catálogo» cuando construyan su interfaz.
