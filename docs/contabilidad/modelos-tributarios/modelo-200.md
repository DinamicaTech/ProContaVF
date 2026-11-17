---
title: Modelo 200
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/procesos/impuesto-sociedades, contabilidad/balances]
threads:
  - 2026-11-17 13:50 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
---
## Summary
Declaración anual del Impuesto sobre Sociedades: fichero con el diseño de registro del ejercicio a partir del balance, la cuenta de pérdidas y ganancias y el cálculo del impuesto (contabilidad/procesos/impuesto-sociedades), con su PDF oficial y su presentación con la pieza común (`presentacion`); el mismo fichero se puede importar en Sociedades WEB para completarlo allí. Hoy el programa no calcula el 200 completo, solo la cuota que se contabiliza.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba pasa la validación de la AEAT (ServaliDos) sin errores y su PDF muestra en cada casilla el importe que da el programa; en el entorno de pruebas de la AEAT se presenta y queda registrado con su CSV y justificante; la cuota del 200 coincide con la contabilizada en la 6300 por el proceso del Impuesto sobre Sociedades.

## Decisions
- 2026-11-17 13:50 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT», con el árbol acordado con el propietario (2026-11-17 12:48 y 13:46): un nodo por modelo, con la pieza común en contabilidad/modelos-tributarios/presentacion. El PDF del modelo es el que devuelve la AEAT y lo que sale a ingresar se domicilia (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 13:50).
- 2026-11-17 13:50 · El servicio de presentación directa admite el modelo 200 (lista de modelos de la especificación versión 30); como el 200 tiene muchas páginas que el programa no calcula, la primera entrega puede limitarse a generar el fichero para importarlo en Sociedades WEB y presentar desde allí.

## Requirements
- 2026-11-17 13:50 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo con la pieza común.
- 2026-11-17 13:50 · draft: Generar el modelo 200 de cada ejercicio desde el balance, la cuenta de pérdidas y ganancias y el cálculo del Impuesto sobre Sociedades: fichero con el diseño de registro del ejercicio (importable en Sociedades WEB), PDF oficial de la AEAT y presentación directa con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.
