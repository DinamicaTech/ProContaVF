---
title: Modelo 303
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/procesos/liquidacion-iva, contabilidad/procesos/prorrata, organizacion/sociedades-ejercicios]
threads:
  - 2026-11-17 13:50 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
  - 2026-11-17 15:10 · Presentar el modelo 303 | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7j1fQT4w8ZKAHTqs4f37LC
---
## Summary
Autoliquidación trimestral del IVA: las casillas del modelo a partir del cálculo del 303 que ya hace el nodo padre (`Calculo303`), el fichero con el diseño de registro de cada ejercicio, su PDF oficial y su presentación con la pieza común (`presentacion`), con domiciliación si sale a ingresar. Se lleva del nodo padre el borrador con casillas que seguía pendiente.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba pasa la validación de la AEAT (ServaliDos) sin errores y su PDF muestra en cada casilla el importe que da el programa; en el entorno de pruebas de la AEAT se presenta y queda registrado con su CSV y justificante; en la copia de pruebas, el 1T, 2T y 3T de 2026 dan el mismo resultado que la liquidación de IVA (1.330,59, 3.026,65 y 3.875,21 €); al presentar, lo liquidado del trimestre pasa a ser lo presentado.

## Decisions
- 2026-11-17 13:50 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT», con el árbol acordado con el propietario (2026-11-17 12:48 y 13:46): un nodo por modelo, con la pieza común en contabilidad/modelos-tributarios/presentacion. El PDF del modelo es el que devuelve la AEAT y lo que sale a ingresar se domicilia (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 13:50).
- 2026-11-17 13:50 · Recibe del nodo padre la parte pendiente del borrador del 303 (casillas de modificación de bases y cuotas, exentas y no sujetas, avisos de facturas de otro trimestre y bloqueo si hay facturas con asiento y sin registro, línea «Derived from facturacion/factura-venta/contabilizacion» del 2026-11-11 11:35, que se queda en el padre como origen del cálculo).

## Requirements
- 2026-11-17 13:50 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo con la pieza común.
- 2026-11-17 13:50 · draft: Generar el modelo 303 de cada trimestre: casillas desde el cálculo del 303, fichero con el diseño de registro del ejercicio, PDF oficial de la AEAT y presentación directa con domiciliación, con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.
2026-11-17 14:38 · answer: importación con DUA (10,92 € en el 1T de 2026) → solo se deduce (casillas 32 y 33); solo se devenga, en la casilla 77, con el pago diferido del IVA de importación («1- Ok»).
2026-11-17 14:38 · answer: 4T con resultado negativo → compensar, no pedir devolución («Compensar, pedir devolución probablemente genera una inspección de IVA»).
2026-11-17 14:38 · answer: complementarias → se incluyen ya (marca, justificante anterior y casilla 70) («Ok»); sacar el PDF borrador con ServaliDos no presenta nada.
2026-11-17 14:38 · answer: redondeo → la prorrata se redondea por casilla, como la AEAT, aunque mueva algún céntimo respecto a la liquidación («Ok»).
