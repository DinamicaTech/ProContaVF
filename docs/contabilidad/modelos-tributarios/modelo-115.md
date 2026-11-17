---
title: Modelo 115
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, compras/factura-compra]
threads:
  - 2026-11-17 13:50 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
---
## Summary
Retenciones de arrendamientos de inmuebles urbanos, trimestral: las retenciones de las facturas de alquiler recibidas del trimestre, fichero con el diseño de registro del ejercicio, PDF oficial y presentación con la pieza común (`presentacion`), con domiciliación.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba pasa la validación de la AEAT (ServaliDos) sin errores y su PDF muestra en cada casilla el importe que da el programa; en el entorno de pruebas de la AEAT se presenta y queda registrado con su CSV y justificante; el total retenido coincide con lo que el aviso S4 espera en la 4751 de arrendamientos del trimestre.

## Decisions
- 2026-11-17 13:50 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT», con el árbol acordado con el propietario (2026-11-17 12:48 y 13:46): un nodo por modelo, con la pieza común en contabilidad/modelos-tributarios/presentacion. El PDF del modelo es el que devuelve la AEAT y lo que sale a ingresar se domicilia (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 13:50).

## Requirements
- 2026-11-17 13:50 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo con la pieza común.
- 2026-11-17 13:50 · draft: Generar el modelo 115 de cada trimestre desde las facturas de alquiler recibidas con retención: fichero con el diseño de registro del ejercicio, PDF oficial de la AEAT y presentación directa con domiciliación, con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.
