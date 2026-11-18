---
title: Modelo 180
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/modelos-tributarios/modelo-115]
threads:
  - 2026-11-17 14:05 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
  - 2026-11-18 12:40 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
---
## Summary
Resumen anual de las retenciones de arrendamientos de inmuebles urbanos: un registro por arrendador con su inmueble y las retenciones del año, que cuadra con los cuatro 115; fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común (`presentacion`).
Hecho el cálculo, pendiente de validar: `Modelo180` y `ServicioResumenRetenciones` (desde las mismas percepciones que el 115, así que cuadra con sus cuatro trimestres). El inmueble sale del activo cuyo centro de coste llevan las facturas de alquiler con retención, con su situación, referencia catastral y dirección de «Inmuebles para los modelos (180 y 347)» (esquema 72). En la copia no hay alquileres con retención: con uno inventado se presentó en el entorno de pruebas de TGVI (CSV QZUTWH3QCZKM2QTS).
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba se valida en TGVI Online del entorno de pruebas de la AEAT sin registros erróneos, su vista previa en PDF muestra los importes que da el programa y se presenta y queda registrado con su CSV; el total retenido coincide con la suma de los cuatro 115 del año.

## Decisions
- 2026-11-17 14:05 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT» a petición del propietario (2026-11-17 13:48), al confirmar que las informativas se pueden presentar desde un programa con TGVI Online (`sources/aeat/Especificaciones_TGVI_Online.pdf`, versión 2.8). Como declaración informativa no lleva pago; lo que valida la AEAT sin errores se presenta, y los registros erróneos quedan para una complementaria (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 14:05).

- 2026-11-18 12:40 · Hecho en la rama claude/project-thread-v1z0ue (desarrollo desatendido autorizado por el propietario; decisiones fiscales prudentes, pendientes de su validación), sin menú ni pantalla y sin cambio de esquema; se presenta con la pieza común de TGVI Online (`ServicioTgvi`, contabilidad/modelos-tributarios/presentacion 2026-11-18 12:40).
- 2026-11-18 12:40 · [replaced by 2026-11-18 13:45] Un registro por arrendador con la base, las retenciones del año y el porcentaje; total = suma de las casillas 03 de los cuatro 115 (prueba con datos inventados). Bloqueos: NIF español y provincia del arrendador y los datos del inmueble (situación, referencia catastral y dirección), que las facturas de alquiler no tienen. Diseño `sources/aeat/DR180e2023.pdf` (vigente según el propietario); el fichero queda para cuando el inmueble esté ligado a la factura.
- 2026-11-18 13:45 · Un registro por arrendador con la base, las retenciones del año y el porcentaje; total = suma de las casillas 03 de los cuatro 115. Diseño `sources/aeat/DR180e2023.pdf` (vigente según el propietario). Bloqueos: NIF español del arrendador y su inmueble; sin código postal solo avisa (provincia «00»). Inmueble de cada arrendador (esquema 72): el activo de categoría inmueble con el centro de coste de sus facturas de alquiler con retención (TipoRetencion 2). Factura sin centro o con un centro sin inmueble: bloqueo que pide poner el centro del inmueble y, si no está en Activos, darlo de alta como activo ajeno con ese centro. Un arrendador con varios inmuebles: bloqueo (el programa aún no reparte un arrendador entre inmuebles). Inmueble sin situación, sin referencia catastral con las situaciones 1 a 3 o sin vía, municipio, código de municipio o provincia: bloqueo que manda a «Inmuebles para los modelos (180 y 347)».
- 2026-11-18 13:45 · Registro (diseño de 2023): situación en la 114, referencia catastral 115-134 y dirección 135-327; el número de casa va a cinco cifras con ceros a la izquierda (la AEAT rechaza blancos, error E020190).
- 2026-11-18 13:45 · TGVI (entorno de pruebas, copia ProConta-modelos-72 con un alquiler y un inmueble inventados y la Agencia Tributaria como arrendador): 1 registro correcto y presentado, CSV QZUTWH3QCZKM2QTS, número 1802500900001. Aviso de la AEAT 21102B por el porcentaje (15 %): la factura de prueba era de un profesional cambiada a alquiler.
- 2026-11-18 13:45 · Pruebas: `DatosModelosTests` (posiciones del inmueble y bloqueos sin inmueble o incompleto).

## Requirements
- 2026-11-17 14:05 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo por TGVI Online con la pieza común.
- 2026-11-17 14:05 · draft: Generar el modelo 180 de cada ejercicio desde las facturas de alquiler recibidas con retención: arrendador, inmueble y retenciones, cuadrado con los cuatro 115, fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.

## Hand-over
- 2026-11-18 12:40 · Pendiente: ligar el inmueble a las facturas de alquiler y generar el fichero; validación del propietario.
