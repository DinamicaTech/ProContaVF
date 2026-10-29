---
title: Movimientos similares
status: draft
depends_on: [norma43/pantallas]
threads:
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Para una línea N43, lista las líneas con cuenta de destino cuyo concepto se parece más y permite asignar esa cuenta a la selección. Sustituye a `frmMovimientosN43Similares` de ProConta antiguo.
**Aceptación:** la lista sale ordenada por porcentaje (como número), sin la propia línea; asignar una cuenta la aplica a las líneas seleccionadas; ver asiento e ir a la línea funcionan desde cada resultado; el detalle de la línea muestra un avance con los mejores resultados.

## Decisions
- 2026-10-29 17:18 · Creado como hijo de `norma43` a petición del propietario. Referencia funcional: `frmMovimientosN43Similares` (exportado solo en el clon local del propietario), que usa la similitud Ratcliff/Obershelp por caracteres, muestra hasta 50 líneas desde el 5 % y no abre si ninguna llega al 50 %.
- 2026-10-29 17:18 · Se conserva el cálculo nuevo del prototipo, como pidió el propietario si es parecido o mejor: palabras significativas del concepto (sin números ni palabras de menos de 3 letras) comparadas por coincidencia de conjunto (Jaccard), y una palabra cortada por el banco cuenta como igual a la completa si una empieza por la otra (al menos 4 letras). Es mejor que el antiguo con referencias, fechas y números variables, y equivalente con conceptos truncados.
- 2026-10-29 17:18 · Lista: hasta 50 líneas desde el 20 %, ordenadas por porcentaje; se excluyen la propia línea, las remesas y las líneas generadas. Acciones por resultado: asignar su cuenta a la selección, copiar la cuenta, ver el asiento e ir a la línea. Se abre desde el menú contextual de `norma43/pantallas` («Movimientos similares…») y desde el enlace «Ver todos» del avance del detalle, que muestra los 3 mejores.
- 2026-10-29 17:38 · El propietario valida el diseño y su prototipo en la pantalla de líneas N43.

## Requirements
- 2026-10-29 17:08 · No veo las opciones de 'Buscar por importe' (sería un contextual que hace lo que hay en el formulario frmN43BusquedaPorImporte) ni la de 'Movimientos similares', que ejecuta el formulario frmMovimientosN43Similares. Ambas funcionalidades se merecen un nodo propio por debajo de Norma43
- 2026-10-29 17:18 · Si tu cálculo de movimientos similares es parecido o mejor al antiguo, conservamos el nuevo
