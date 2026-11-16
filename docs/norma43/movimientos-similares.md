---
title: Movimientos similares
depends_on: [norma43/pantallas]
threads:
  - 2026-10-29 17:19 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-11-16 13:45 · Movimientos similares N43 | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GVKEs9SFdf8iuUnGHTs7M7G
---
## Summary
Para una línea N43, lista las líneas con cuenta de destino cuyo concepto se parece más y permite asignar esa cuenta a la selección. Sustituye a `frmMovimientosN43Similares` de ProConta antiguo.
Hecho en la pantalla de líneas N43 y validado por el propietario (PR #89): «Movimientos similares…» en el menú contextual, debajo de «Asignar cuenta…», con su diálogo, y el avance de 3 resultados con «Usar» y «Ver todos» en el detalle. El cálculo es `ReglasSimilaresN43` y la lectura ligera de candidatos `ConsultaSimilaresN43` (`src/ProConta.Norma43/Similares`). El diálogo es `MovimientosSimilaresN43VM` con `DialogosLineasN43.Similares`. Se busca en el ejercicio de la línea y los dos anteriores. Sin cambios de esquema. Pruebas: `MovimientosSimilaresN43Tests` (sin BD) y `MovimientosSimilaresN43AccessTests`.
**Aceptación:** la lista sale ordenada por porcentaje (como número), sin la propia línea; asignar una cuenta la aplica a las líneas seleccionadas; ver asiento e ir a la línea funcionan desde cada resultado; el detalle de la línea muestra un avance con los mejores resultados.

## Decisions
- 2026-10-29 17:18 · Creado como hijo de `norma43` a petición del propietario. Referencia funcional: `frmMovimientosN43Similares` (`sources/proconta-antiguo/norma43/formulario/frmMovimientosN43Similares.code.vba`). Usa la similitud Ratcliff/Obershelp por caracteres, muestra hasta 50 líneas desde el 5 % y no abre si ninguna llega al 50 %.
- 2026-10-29 17:18 · Se conserva el cálculo nuevo del prototipo, como pidió el propietario si es parecido o mejor: palabras significativas del concepto (sin números ni palabras de menos de 3 letras) comparadas por coincidencia de conjunto (Jaccard), y una palabra cortada por el banco cuenta como igual a la completa si una empieza por la otra (al menos 4 letras). Es mejor que el antiguo con referencias, fechas y números variables, y equivalente con conceptos truncados.
- 2026-10-29 17:18 · [replaced by 2026-11-16 13:45] Lista: hasta 50 líneas desde el 20 %, ordenadas por porcentaje; se excluyen la propia línea, las remesas y las líneas generadas. Acciones por resultado: asignar su cuenta a la selección, copiar la cuenta, ver el asiento e ir a la línea. Se abre desde el menú contextual de `norma43/pantallas` («Movimientos similares…») y desde el enlace «Ver todos» del avance del detalle, que muestra los 3 mejores.
- 2026-10-29 17:38 · El propietario valida el diseño y su prototipo en la pantalla de líneas N43.
- 2026-11-16 13:45 · En el código no había ningún cálculo de similares (solo estaba en el prototipo), así que se programa el del prototipo (decisión del 2026-10-29 17:18). Detalles del cálculo:
  - Las palabras se comparan sin acentos ni mayúsculas, y la ñ cuenta como n.
  - Se descarta toda palabra que lleve alguna cifra, porque son referencias, fechas e importes.
  - Cada palabra cuenta una sola vez.
  - El porcentaje son las palabras compartidas entre el total de palabras distintas, redondeado al entero.
  - A igual porcentaje sale primero la línea más reciente.
- 2026-11-16 13:45 · Con qué se compara (propietario 2026-11-16 12:17): las líneas del ejercicio de la línea y de los dos anteriores (para una de 2026, de 2024 a 2026), aunque esos ejercicios no estén marcados en la pantalla. Se comparan las que tienen una sola cuenta de destino viva. Quedan fuera:
  - las de lotes revertidos;
  - los recibos de remesas o devoluciones explotadas;
  - las remesas y devoluciones, es decir, las que tienen una palabra clave especial 5 o 6 y las ya explotadas;
  - las repartidas entre varias cuentas.

  Esas mismas remesas, devoluciones y recibos, y las líneas sin palabras significativas, no se pueden usar como línea de partida: la opción sale desactivada y explica el motivo. La lectura es ligera (concepto, cuenta y asiento) y se guarda en la pantalla. Se vuelve a leer al actualizar (F5), al cambiar los ejercicios marcados o después de cualquier acción que cambie líneas.
- 2026-11-16 13:45 · Lista completa: hasta 50 líneas desde el 20 %, ordenadas por porcentaje, sin la propia línea y con las exclusiones de la decisión anterior. Cada fila muestra el parecido (en verde y en negrita desde el 50 %), la fecha, el concepto, el importe, la cuenta, su descripción y el asiento. Sus botones:
  - «Asignar»: cierra el diálogo y pone la cuenta con la misma regla que «Asignar cuenta…» (F2), con imputación MANUAL y regenerando el asiento si la línea estaba contabilizada. Si la cuenta no existe en el ejercicio de alguna línea, el aviso lo dice y esa línea no cambia.
  - «Copiar»: copia la cuenta.
  - «Asiento»: abre el asiento de esa línea; también con doble clic en la fila. Se desactiva si la línea no está contabilizada.
  - «Ir a la línea»: cierra el diálogo y selecciona esa línea en la lista. Si su ejercicio no está marcado, lo marca; si los filtros la ocultan, los quita.

  Desde el menú contextual se compara con la línea del detalle si está seleccionada, y si no, con la primera seleccionada; «Asignar» pone la cuenta a todas las seleccionadas. Si la selección no se puede cambiar (ejercicio cerrado, ERP), el diálogo se abre igual con «Asignar» desactivado y el motivo a la vista. Sin resultados, el diálogo lo dice y propone F2 o una palabra clave (Ctrl+K), en lugar de no abrirse como el antiguo.
- 2026-11-16 13:45 · Avance en el detalle de la línea: los 3 mejores desde el 50 %, con una cuenta distinta de la que ya tiene la línea y una sola vez cada cuenta (la del más parecido). Cada uno tiene su botón «Usar», que pone esa cuenta solo a la línea del detalle. El enlace «Ver todos» abre el diálogo completo para esa línea, con «Asignar» solo para ella. Si no hay ninguno, se dice que «Ver todos» muestra desde el 20 %. Si la lectura falla, el avance no se muestra.
- 2026-11-16 13:45 · Comprobado con una copia de la BD del ERP (sociedad 002, 1.204 líneas de 2026):
  - Lectura de candidatos: 4.182 de 2024 a 2026 en 1 s la primera vez; después se reutiliza.
  - Cada búsqueda tarda unos 9 ms.
  - En el 87 % de las líneas que ya tienen cuenta (858 de 977), el movimiento más parecido tiene esa misma cuenta.
  - De las 193 líneas de 2026 sin cuenta, 169 tienen algún similar desde el 50 %.

  Se probó también a dar menos peso a las palabras que salen en muchas líneas, como «RECIBO», «SEPA» o «MANDATO». Acierta lo mismo (854 de 950 desde el 50 %), así que se mantiene el cálculo validado, que es más sencillo de explicar.
- 2026-11-16 14:00 · Validado por el propietario («Ok, perfecto»): el nodo deja de ser borrador y se fusiona la PR #89.

## Requirements
- 2026-10-29 17:08 · No veo las opciones de 'Buscar por importe' (sería un contextual que hace lo que hay en el formulario frmN43BusquedaPorImporte) ni la de 'Movimientos similares', que ejecuta el formulario frmMovimientosN43Similares. Ambas funcionalidades se merecen un nodo propio por debajo de Norma43
- 2026-10-29 17:18 · Si tu cálculo de movimientos similares es parecido o mejor al antiguo, conservamos el nuevo
- 2026-11-16 12:17 · Creo que podemos limitarlo a los últimos tres ejercicios (actual y dos anteriores).
- 2026-11-16 14:00 · Ok, perfecto. Ya puedes fusionar PR
