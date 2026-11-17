---
title: Modelos presentados
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, documentos]
threads:
  - 2026-11-17 13:50 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
---
## Summary
Pantalla con todos los modelos presentados de la sociedad: los que presenta el programa (con su respuesta guardada) y los que devuelve la consulta de la AEAT (ConsultaExt), también los que presentó la gestoría. Se ven en forma de tabla o en PDF, con su CSV, justificante, expediente, fecha, forma de pago e importe, y se abre el PDF justificante.
Después de la Versión Mínima Viable.
**Aceptación:** con la copia de pruebas y el entorno de pruebas de la AEAT, un 303 presentado desde el programa sale en la lista con su CSV, justificante y PDF; la consulta a la AEAT añade los presentados fuera del programa sin duplicar los que ya están; la lista se puede ver en tabla y abrir cada PDF.

## Decisions
- 2026-11-17 13:50 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT», con el árbol acordado con el propietario (2026-11-17 12:48 y 13:46): un nodo por modelo, con la pieza común en contabilidad/modelos-tributarios/presentacion. El PDF del modelo es el que devuelve la AEAT y lo que sale a ingresar se domicilia (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 13:50).

## Requirements
- 2026-11-17 12:48 · y supongo que otro nodo específico que muestre todos los modelos presentados con opción de visualizarlos (en forma tabular o en PDF), y consultar su CSV y justificante.
- 2026-11-17 13:50 · Derived from contabilidad/modelos-tributarios/presentacion: lista de lo presentado con la consulta de la AEAT y la respuesta guardada de cada presentación.
- 2026-11-17 13:50 · draft: Construir la pantalla de modelos presentados de cada sociedad: lista de lo presentado desde el programa y de lo que devuelve la consulta de la AEAT (ConsultaExt), en tabla o en PDF, con CSV, justificante, expediente, fecha, forma de pago e importe, y apertura del PDF justificante, después de la Versión Mínima Viable.
