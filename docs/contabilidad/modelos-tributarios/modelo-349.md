---
title: Modelo 349
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/modelos-tributarios/modelo-303]
threads:
  - 2026-11-17 14:05 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
---
## Summary
Declaración recapitulativa de operaciones intracomunitarias, del periodo que corresponda (trimestral o mensual): adquisiciones y prestaciones intracomunitarias por operador con su NIF-IVA (verificado con el VIES), a partir de los libros de IVA, cuadrando con las casillas intracomunitarias del 303; fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común (`presentacion`). En la sociedad, el caso típico son las comisiones de Airbnb y Booking.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba se valida en TGVI Online del entorno de pruebas de la AEAT sin registros erróneos, su vista previa en PDF muestra los importes que da el programa y se presenta y queda registrado con su CSV; el total de adquisiciones de servicios intracomunitarios del periodo coincide con las casillas de adquisiciones intracomunitarias del 303 del mismo periodo.

## Decisions
- 2026-11-17 14:05 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT» a petición del propietario (2026-11-17 13:48), al confirmar que las informativas se pueden presentar desde un programa con TGVI Online (`sources/aeat/Especificaciones_TGVI_Online.pdf`, versión 2.8). Como declaración informativa no lleva pago; lo que valida la AEAT sin errores se presenta, y los registros erróneos quedan para una complementaria (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 14:05).

## Requirements
- 2026-11-17 14:05 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo por TGVI Online con la pieza común.
- 2026-11-17 14:05 · draft: Generar el modelo 349 de cada periodo desde los libros de IVA: operadores intracomunitarios con su NIF-IVA y su importe por clave de operación, cuadrado con el 303, fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.
