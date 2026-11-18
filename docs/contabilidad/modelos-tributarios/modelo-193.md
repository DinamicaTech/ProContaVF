---
title: Modelo 193
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/procesos/aplicacion-resultado]
threads:
  - 2026-11-18 12:40 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
---
## Summary
Resumen anual de las retenciones del capital mobiliario por dividendos: un registro por perceptor (socio) con los dividendos íntegros y la retención del año, desde el reparto de dividendos; fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común (`presentacion`).
Hecho el cálculo, pendiente de validar: `Servicio193` (`src/ProConta.Contabilidad/Modelos/Modelo193.cs`). Sin fichero: el diseño de registro del 193 no está en el proyecto (pendiente del OK del propietario para descargarlo de la Sede). En una copia de ProConta-dividendos-01 el 2025 da los 2 perceptores de la gestoría con 11.728,40 € de retención cada uno.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba se valida en TGVI Online del entorno de pruebas de la AEAT sin registros erróneos y se presenta y queda registrado con su CSV; el total retenido coincide con las retenciones del reparto de dividendos del año.

## Decisions
- 2026-11-18 12:40 · Creado como borrador desde el hilo «Completar los modelos tributarios y su pantalla», aprobado por el propietario el 2026-11-18 a las 06:06 (relayado por el hilo coordinador).
- 2026-11-18 12:40 · Cálculo: los repartos del año grabados en el programa (RepartosDividendos y sus socios, por la fecha del reparto), con el bruto y la retención de cada socio; si no hay (los repartos migrados del ERP son solo asientos), lo abonado en el año en la subcuenta de retención de cada socio (SociosSociedad.IdIdentidadRetencion) y la base = retención × 100 / porcentaje de retención de dividendos del ejercicio (19 % si está vacío), con un aviso para comprobarla con el acta. Identificación del socio de su ficha de tercero; bloquean el NIF no español y la falta de código postal.
- 2026-11-18 12:40 · Contraste con el 193 de 2025 de la gestoría (2 perceptores; 123.456,80 € de base y 23.456,80 € de retenciones): en una copia de ProConta-dividendos-01 (la de pruebas general no tiene socios) el programa da los mismos 2 perceptores y 11.728,40 € de retención cada uno; la base sale 61.728,42 € en vez de 61.728,40 € porque se reconstruye desde la retención (el reparto de 2025 no está grabado en el programa).

## Requirements
- 2026-11-18 12:40 · draft: Generar el modelo 193 de cada ejercicio desde el reparto de dividendos: un registro por perceptor, fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común contabilidad/modelos-tributarios/presentacion

## Hand-over
- 2026-11-18 12:40 · Pendiente: el diseño de registro (dr193…) y con él el fichero y la presentación por TGVI; la base exacta cuando el reparto esté grabado en el programa; validación del propietario.
