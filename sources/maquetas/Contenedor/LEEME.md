# ProConta · Contenedor principal (maqueta) con Terceros integrado

Abre `proconta-contenedor.html` con doble clic en Chrome o Edge. No necesita servidor ni conexión; con conexión carga las fuentes de Google Fonts.

## Escenarios
Añade `?escenario=` a la dirección para abrir un estado concreto, por ejemplo `proconta-contenedor.html?escenario=terceros-bus`.

- `terceros-acc`: ficha con tres cambios sin guardar, para ver la barra de acciones. Añade `&acciones=derecha` para ver los botones alineados a la derecha (por defecto, `izquierda`).
- `terceros-doc`: pestaña Documentos con un fichero recién añadido, pendiente de guardar.
- `terceros-bus`: buscador de Terceros con la búsqueda «ferrer» y dos fichas abiertas.
- `terceros-5`: cinco fichas abiertas; al abrir la sexta se cierra la más antigua sin cambios pendientes.
- `terceros`: ficha de Talleres Albora con cambios pendientes.
- `terceros-err`: guardado con errores de validación.
- `factura`: factura abierta desde la ficha de un tercero.
- `close`: cierre de una ficha con cambios.

También funcionan los demás escenarios del contenedor (`inicio`, `ctx-pending`, `tabs`, `n43`…). El botón «Demostración» de la barra de estado permite cambiar de tamaño, de operador y de situación sin recargar la página.

## Terceros en el contenedor
- **Pestaña Terceros:** buscador multicriterio. Un mismo valor se busca en:
  - nombre y apellidos, razón social y nombre comercial;
  - NIF, IdTercero, IdCliente, IdProveedor e IdContacto;
  - teléfonos, emails y domicilios;
  - personas de contacto, perfiles y cuentas contables.

  Si hay varias palabras, deben aparecer todas. Cada resultado indica en qué dato ha coincidido. Se puede filtrar por tipo, por roles y por perfil.
- **Fichas:** doble clic o Intro sobre un resultado abre la ficha en su propia pestaña. La pestaña se titula con el nombre y el primer apellido (particulares) o con la razón social.
- **Máximo 5 fichas:** al abrir la sexta se sustituye, en su misma posición, la ficha abierta hace más tiempo que no tenga cambios pendientes. Si todas tienen cambios, se avisa y no se abre.
- **Cabecera de la ficha:** muestra la identidad y el estado.
  - Los roles activos aparecen como insignias de solo lectura con su Id. Al pulsar una se va a ese rol en la pestaña Roles, que es el único sitio donde se activan o desactivan.
  - A la derecha, el aviso de cambios sin guardar.
- **Guardar y Descartar:** están en una barra fija al pie de la ficha, junto al número de cambios y «Ver cuáles». También se guarda con Ctrl+G (o Ctrl+S).
- **Botón «Nuevo»** del buscador: permite crear un tercero sin rol o un cliente, proveedor o contacto con el rol ya activado.
- **Cambios pendientes:** usan el mecanismo del contenedor. Las pestañas con cambios muestran un punto; las que no se pudieron guardar, un triángulo. Al cerrar una pestaña, cambiar de contexto o salir, se ofrece guardar o descartar.
- **Identidad:** cada control mide lo que necesita su contenido.
  - La etiqueta del campo de identificación cambia según el tipo elegido (NIF, CIF, NIE, NIF-IVA, Nº pasaporte…).
  - Razón social y nombre comercial ocupan media fila cada uno.
- **Domicilio en una línea**, que se rellena en este orden: país, CP, provincia, población y vía y número.
  - En España, el CP fija la provincia. Si solo corresponde a una población, también se elige la población y el cursor pasa a la vía; si no, pasa a la lista de poblaciones de ese CP. Prueba con 46988 (Paterna) o 46470 (Albal, Catarroja o Massanassa).
  - Al elegir una población, el cursor pasa a la vía.
  - Fuera de España, región y población se escriben a mano.
- **Roles** (antes «Funciones»):
  - El cliente tiene una sola cuenta contable, bajo la forma de pago.
  - El proveedor tiene una cuenta de proveedor y otra de acreedor, más la de gasto por defecto.
  - Cada cuenta va en su fila con una descripción, para comprobar que la cuenta es la correcta. Si se indica una cuenta sin descripción, no se guarda.
  - La cuenta es la misma en todas las sociedades.
  - El contacto no tiene cuenta.
  - El mayor de Contabilidad se consulta por cuenta y por sociedad y ejercicio.
- **Ventas:** primero las facturas programadas del cliente y después las emitidas. Al seleccionar una programación, solo se ven las facturas que ha generado; se quita con la «×» del filtro o con «Limpiar filtros».
- **Notas:** pestaña antes de Historial, con «Notas» y «Notas internas». Sus cambios cuentan como pendientes y quedan en el historial.
- **Documentos** (entre Notas e Historial):
  - Los documentos automáticos son las facturas emitidas, las facturas recibidas y los justificantes de pago. Llevan un candado, indican su origen con un enlace a la factura y no se pueden eliminar desde aquí.
  - Los ficheros se añaden arrastrándolos a la zona superior o con «Seleccionar ficheros».
  - Al añadir un fichero se elige su tipo y, si se quiere, una descripción. Altas y bajas quedan pendientes hasta guardar la ficha y se registran en el historial.
  - Se puede buscar por nombre, descripción o número de factura, y filtrar por origen y por tipo. El buscador de Terceros también encuentra los nombres de los documentos.
- **Facturas emitidas:** con doble clic, una factura de la sociedad y ejercicio activos se abre en su pestaña de Facturas, con el botón «Volver a Terceros». Las de otro contexto se muestran en modo consulta.

Los datos son ficticios. Los cambios duran mientras la página está abierta.

## Revisión de validación 1 (29/10/2026)
- Todos los controles del producto tienen un tooltip descriptivo; los campos editables muestran su ayuda o, si tienen un error, el mensaje del error.
- Los errores de validación ya no se escriben debajo del campo: el campo se marca con borde rojo e icono, y el mensaje va en su tooltip y en el resumen superior.
- Razón social, Nombre comercial y CP miden lo que su contenido.
- El nombre del tercero en el buscador y los datos de la cabecera de la ficha se muestran sin recortar.
- Sin palabras enteras en mayúsculas: encabezados de columna y de sección en minúscula inicial, salvo siglas.
- La cabecera de la ficha ya no repite «N cambios sin guardar»: se indica en la barra de acciones del pie y con el punto de la pestaña.

## Facturas emitidas y programadas (29/10/2026)
- La pestaña «Facturas emitidas» del contenedor es ahora la pantalla validada en `sources/maquetas/Facturas/` (ronda 1), reescrita dentro del contenedor: programadas arriba, emitidas abajo, separador arrastrable, detalle lateral, líneas desplegables y editables, menú contextual y diálogos.
- Abre directamente con `?escenario=facturas`. El operador de facturación en consulta ve la pantalla en modo solo lectura.
- Las líneas en edición cuentan como cambios pendientes de la pestaña: el punto de la pestaña, el aviso al cerrar y «Guardar» o «Descartar» del contenedor las tratan como al resto de pantallas.
- El panel de demostración añade el tamaño 2560 × 1440, que es la resolución de referencia.

## Líneas bancarias Norma 43 (29/10/2026)
- La pestaña «Líneas N43» propone la nueva pantalla a partir del análisis de `frmLineasN43` (`sources/proconta-antiguo/norma43/ANALISIS-FUNCIONAL.md`). Abre directamente con `?escenario=n43`.
- Filtros combinados en Y, selector de estado y fichas de filtro removibles; la búsqueda admite prefijos (`n+`, `i+`, `e+`, `a+`, `c+`, `o+`, `f+`).
- Las acciones viven en el menú contextual y dependen de la selección (simple, múltiple o ambas); si una no está disponible, se indica el motivo.
- Diálogos: remesas, explotar remesa con vista previa, dividir en fracciones, asignar subcuenta, palabras clave, traspaso entre bancos, exportar y subcuentas por sociedad y ejercicio.
- A 2560 × 1440 el detalle queda fijo a la derecha; en pantallas pequeñas se abre como panel superpuesto con Intro o doble clic.
- Ronda 2: importes en euros, selector de ejercicio múltiple (los cerrados, solo consulta), columnas de subcuenta de origen y de destino, remesas con subcuenta, fechas, recibos e importe, palabras clave eligiendo el criterio en el concepto (con edición y borrado de las existentes), doble clic para ver el asiento y doble clic en una subcuenta para abrir su mayor provisional.
- En todo el contenedor, «Ampliar área de trabajo» pasa a ser un separador arrastrable entre el menú lateral y la pantalla; oculto deja una franja para recuperarlo (Ctrl+Mayús+M provisional).
- Ronda 3: columnas «Cuenta origen», «Descripción cuenta origen», «Cuenta destino» y «Descripción cuenta destino», redimensionables (arrastre o doble clic en el borde de la cabecera) y guardadas por operador en el navegador; clic derecho en la cabecera: ajustar todas o restablecer. Menú contextual con «Buscar por importe…» (varias líneas) y «Movimientos similares…».

## Facturas de compra: importación desde PDF (29/10/2026)
- La pestaña «Facturas recibidas» propone la nueva pantalla a partir del análisis de `frmEntradaFacturas` (`sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md`). Abre directamente con `?escenario=compras` (2560 × 1440), `?escenario=compras-1440` o, en modo prueba del reconocimiento, `?escenario=compras-prueba`.
- Tres zonas: lista (Pendientes y Contabilizadas, filtros en fichas, columnas redimensionables por operador), ficha de la factura y visor del PDF simulado. Cada campo reconocido muestra su confianza y el motivo, y al pasar por encima se resalta en el visor de dónde sale.
- Seleccionar texto en el visor rellena el campo marcado para captura; no se usa el portapapeles.
- F12 contabiliza (una o varias) enseñando antes el asiento propuesto, los avisos y los bloqueos. Un descuadre de hasta 0,05 € se ajusta con un apunte de redondeo; uno mayor bloquea. Deshacer, rehacer y rectificativa son acciones distintas; la rectificativa va al trimestre activo.
- Al cambiar de factura se guardan sus cambios. Los valores por defecto del proveedor cambiados en la factura pasan a su ficha al guardar o contabilizar, con su historial.
- Casos de ejemplo en la sociedad de trabajo: PDF que falta, PDF escaneado, proveedor desconocido, factura en dólares, pareja de transitario y aduana, inversión del sujeto pasivo, factura del ejercicio anterior, descuadre de 0,01 € y factura dirigida a otra sociedad. Todos los datos son ficticios.
