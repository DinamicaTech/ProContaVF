# ProConta · Facturas emitidas y programadas (maqueta)

Abre `proconta-facturas.html` con doble clic en Chrome o Edge. Funciona sin servidor ni conexión; con conexión carga las fuentes de Google Fonts. Los datos son ficticios (Ribera Gestión · 2026, hoy = 01/10/2026) y los cambios duran mientras la página está abierta.

La **barra superior gris con borde amarillo** es solo de demostración. Permite cambiar:
- el contexto (Facturación o Terceros › Ventas);
- el tercero de ejemplo;
- el estado de los datos (normal, cargando, error, sin registros o sin permiso);
- el operador (completo o solo consulta);
- el ancho de la ventana (1440, 1280, 1100 o 960 px).

Parámetros de URL: `?contexto=terceros` y `?datos=loading|error|empty|denied`.

Casos para probar:
- **PR-0008 (Academia Nexo):** programación sin facturas generadas.
- **PR-0003:** filtro de programación combinado con otros filtros.
- **F 2026/0055:** error de comunicación con VeriFactu.
- **F 2026/0053:** factura rechazada por VeriFactu.
- **Marzo de PR-0001:** factura rectificada por R 2026/0001.
- **Borradores B-0006 y B-0007:** borradores pendientes de emitir.

## Decisiones funcionales supuestas (para revisar)

**Estructura**
1. Los componentes compartidos son los listados de programadas y emitidas, el detalle y las acciones colectivas. Solo la pantalla completa tiene:
   - el filtro de cliente;
   - «Generar programadas»;
   - «Enviar pendientes a VeriFactu»;
   - «Exportar a contabilidad».
2. En Terceros, el tercero es un filtro fijo con candado. «Limpiar filtros» no lo quita.
3. «Abrir en Facturación» pasa a la pantalla completa con el cliente como filtro, que se puede quitar. Conserva la programación filtrada, el registro activo y el detalle, los filtros y la selección. Desde allí, «Volver a…» regresa a la ficha.
4. En la pantalla completa, el filtro de cliente oculta la columna Cliente. «Limpiar filtros» de las emitidas no quita el cliente, pero sí la programación.

**Programadas**
5. Un clic en una programación la aplica como filtro de las emitidas. Volver a pulsarla no lo quita: se quita con «Ver todas» del aviso o con la tecla F. Intro o doble clic abren el detalle.
6. El total de una programación es fijo: cada factura generada lleva sus líneas tal cual, salvo que se modifiquen manualmente (revisión 3).
7. «Generadas» cuenta solo las facturas de la sociedad y el ejercicio activos.
8. Orden por defecto: activas, pausadas y finalizadas; dentro de cada grupo, por próxima fecha.
9. Las líneas de una programación añaden el periodo facturado al primer concepto (por ejemplo, «octubre 2026»).
10. Una programación finalizada no se edita. Editar solo afecta a las facturas futuras, y la primera emisión no se cambia.
11. Comportamientos marcados como **Propuesta** en el detalle:
    - generar como borrador y emitir en lote;
    - no recuperar los periodos de una pausa;
    - finalización automática al acabar la vigencia.

**Emitidas**
12. Se muestran dos indicadores separados:
    - **Estado de la factura** (Borrador, Emitida o Rectificada), como píldora rellena.
    - **Situación VeriFactu** (Pendiente, Enviando, Aceptada, Aceptada con errores, Rechazada, Error de comunicación o No aplica), como etiqueta con contorno e icono.
13. Los borradores aparecen en el mismo listado, sin número («Borrador B-0007») y con VeriFactu «No aplica». Sus acciones son ver, editar, emitir y eliminar. Una factura emitida no se edita; solo sus notas internas.
14. Rectificar:
    - solo está disponible con VeriFactu aceptado (también con errores);
    - crea un borrador R1 enlazado.
    - **Propuesta:** la original pasa a «Rectificada» al emitir la rectificativa, no al crear el borrador.
15. Una factura rechazada por VeriFactu se subsana, no se rectifica (**Propuesta**; el flujo de subsanación está por definir).
16. Los datos del destinatario son los de emisión. Los cambios posteriores en la ficha no modifican la factura.

**Activo, selección y ámbito**
17. Hay tres marcas visuales distintas:
    - **Registro activo** (el que se ve en el detalle): barra naranja.
    - **Seleccionadas** (casillas): fondo azul.
    - **Programación aplicada como filtro:** embudo y fondo crema.
18. Con el detalle abierto, el detalle sigue a la fila al moverse con el teclado o al hacer clic.
19. Las acciones colectivas actúan sobre «Seleccionadas (n)» o «Todo el listado filtrado (n)», a elegir explícitamente.
    - Cada botón muestra cuántas facturas le aplican.
    - El diálogo de confirmación indica el ámbito y los filtros, las facturas que se omiten (por ejemplo, borradores o clientes sin email) y la lista afectada.
20. Si cambia el filtro, la selección se conserva. Las seleccionadas que quedan fuera del filtro no se incluyen en las acciones, y se avisa de ello con la opción «Quitarlas».

**Detalle y navegación**
21. El detalle se muestra en un panel lateral de 452 px. Por debajo de 1240 px de ventana se superpone a los listados.
22. Pestañas del detalle de una emitida: Resumen, Líneas, VeriFactu (registro, huella, CSV, QR e historial) y Documentos y envíos.
23. El detalle de una programada tiene, en una sola página: Destinatario, Conceptos, Condiciones, Datos de facturación y Comportamiento por definir.
24. Las acciones no disponibles se muestran atenuadas y siguen siendo enfocables. Al pulsarlas, explican el motivo (permisos, estado, VeriFactu, falta de email…).

**Teclado:**
- ↑ ↓, Inicio, Fin, AvPág y RePág: moverse por la lista.
- Intro: abrir el detalle.
- Espacio: marcar la fila (en emitidas y en programadas).
- F: aplicar o quitar el filtro de programación.
- → ← (o + −): mostrar u ocultar las líneas.
- Ctrl+G: guardar las líneas en edición.
- Ctrl+A: seleccionar todo el listado.
- Mayús+F10 o clic derecho: menú de acciones.
- Ctrl+F: buscar.
- Alt+P: plegar las programadas.
- Esc: cerrar el menú, el diálogo o el detalle.

**Simulado:** correos, PDF, envíos a VeriFactu, emisión, rectificación, pausa y edición se identifican con la etiqueta «Simulado». No se envía nada. «Exportar listado» descarga de verdad un CSV del listado filtrado.

## Revisión 2 · selección de programadas y líneas desplegables

**Generar solo las programaciones seleccionadas**
25. Las programadas tienen casillas (y Espacio), igual que las emitidas.
    - El clic en la fila sigue aplicando el filtro de emitidas. Con el teclado, el filtro se aplica con F.
    - «Generar facturas (n)» actúa solo sobre las marcadas que están en el listado.
    - La herramienta general «Generar programadas…» actúa sobre todas las activas.
26. El diálogo de generación permite elegir hasta qué fecha generar:
    - lista, por programación, los periodos que se van a generar;
    - indica las que se omiten y por qué (pausada, finalizada, nada pendiente hasta esa fecha);
    - si no hay nada pendiente, ofrece usar la próxima fecha de emisión.
    - **Propuesta:** se generan como borradores con la fecha de cada periodo, y la próxima emisión avanza.

**Líneas: expand en ambas tablas**
27. El chevrón (o → / ←, + / −) despliega las líneas bajo la fila, sin salir del listado. Se pueden desplegar varias filas a la vez.
    - El bloque desplegado queda fijo a la izquierda aunque la tabla tenga desplazamiento horizontal.
    - Al desplegar una programada, la tabla de programadas gana altura.
28. La edición de líneas se hace en el propio expand, y solo en un registro a la vez.
    - Se puede añadir, modificar y eliminar líneas. Los importes y totales se recalculan al escribir.
    - Guardar y Descartar van al pie del bloque, con el número de cambios. Ctrl+G guarda.
    - La fila editada lleva un punto naranja y se mantiene visible aunque los filtros la excluyan.
    - El detalle lateral no edita: su botón «Editar líneas en el listado» abre el expand (una sola forma de hacerlo).
29. Qué se puede editar:
    - Programaciones no finalizadas: los cambios afectan a las próximas emisiones, no a las facturas ya generadas.
    - Borradores.
    - Las facturas emitidas y las rectificativas se muestran con candado y su motivo.
30. Validación al guardar:
    - concepto obligatorio;
    - cantidad numérica y distinta de 0 (positiva en programaciones);
    - precio numérico;
    - al menos una línea.

    Los campos con error se marcan y reciben el foco. Se admite coma decimal y separador de miles.
31. Si hay cambios sin guardar, se pide guardar, descartar o seguir editando al:
    - plegar el registro o editar otro;
    - cambiar de contexto o de tercero;
    - «Abrir en Facturación»;
    - emitir el borrador.
32. Esc dentro de las líneas vuelve a la fila sin descartar nada.

**Por qué el expand y no otra pantalla:** consultar las líneas es frecuente y breve, y el expand lo resuelve sin perder el contexto del listado ni el filtro. Para editar, limitarlo a un registro con su propia barra de Guardar/Descartar evita tener cambios pendientes repartidos por varias filas. Si en WPF las líneas llegan a tener muchas columnas (descuentos, cuentas, centros de coste), conviene llevar la edición a una ventana de factura y dejar el expand solo para consulta.

## Revisión 3 · campos de líneas y de los listados

33. **Líneas**, con las mismas columnas que el ERP actual: #, Descripción, Precio neto, Cant., Importe neto, IVA, Dto., Precio bruto, Importe bruto y Notas.
    - Precio bruto e importe bruto se calculan (neto + IVA).
    - En edición se pueden modificar la descripción, el precio neto, la cantidad, el IVA, el descuento y las notas.
34. **Descuento: a confirmar.** Siguiendo la captura del ERP (precio neto 1.900 € × 2 = 3.800 € con −7,38 %), el descuento es informativo: indica la rebaja sobre tarifa y el precio neto ya la incluye, así que no se vuelve a aplicar al importe.
35. **Fila de totales** en el pie de la misma tabla, alineada con las columnas.
    - La fila se llama «Total» e incluye la suma de cantidades, el importe neto y el importe bruto, sin desglose de IVA.
    - Con retención se añaden dos filas:
      - «Retención 19 %», con el importe negativo en la columna Importe bruto;
      - «Total a cobrar», con el total final.
    - El porcentaje de retención se edita en la propia fila de retención; 0 equivale a sin retención.
36. **Listado de emitidas.** Nuevas columnas:
    - Notas, antes de Base;
    - Ret., con el porcentaje de retención, después de Base;
    - Fecha envío, antes de VeriFactu.

    **A confirmar:** la fecha de envío es la del último envío por correo al cliente (no la de VeriFactu). Las facturas no enviadas muestran «—».
37. **Listado de programadas.**
    - Columna Notas, antes de Periodicidad.
    - «Importe previsto» pasa a llamarse «Total». Ya no lleva «≈», porque el importe es fijo.
38. **Notas.** Las notas de la programación se copian en las facturas que genera (propuesta). El detalle de la factura y el de la programación muestran sus notas.

## Pendiente de decidir
- Si las emitidas incluyen el estado de cobro (no se ha añadido para no mezclarlo con el estado de la factura).
- La anulación de registros en VeriFactu (no se ha incluido).
- Si las acciones colectivas deben limitarse al listado visible o permitir una selección que quede oculta por el filtro.

## Revisión de validación 1 (2026-10-29)
- Separador arrastrable entre programadas y emitidas para repartir la altura (también con ↑ ↓ desde el teclado; doble clic vuelve a la altura inicial).
- Todos los controles llevan tooltip descriptivo.
- Sin palabras enteras en mayúsculas: encabezados de tabla y de sección, etiquetas «Propuesta» y «Simulado» y el documento de la factura.
- Campos de fecha en formato dd/mm/aaaa, con error de campo (borde rojo, icono y mensaje en el tooltip) si la fecha no es válida.
- Textos largos de las tablas: una línea con «…» y el texto completo en el tooltip.
- Modo `?embebido=1` (con `operador`, `sociedad` y `ejercicio`), que muestra solo el área de trabajo, sin barra de demostración ni marco de ventana.
