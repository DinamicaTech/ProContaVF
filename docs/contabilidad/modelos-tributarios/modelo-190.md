---
title: Modelo 190
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/modelos-tributarios/modelo-111, nominas]
threads:
  - 2026-11-17 14:05 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
  - 2026-11-18 12:40 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
---
## Summary
Resumen anual de las retenciones del trabajo y de actividades profesionales: un registro por perceptor (empleados de las nóminas y profesionales de las facturas recibidas) con sus percepciones y retenciones del año, que cuadra con los cuatro 111; fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común (`presentacion`).
Hecho el cálculo, pendiente de validar: `Modelo190` y `ServicioResumenRetenciones` (desde las mismas percepciones que el 111, así que cuadra con sus cuatro trimestres). No se ofrece presentar: el 190 pide datos personales de los trabajadores (año de nacimiento, situación familiar, descendientes) que el programa no guarda.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba se valida en TGVI Online del entorno de pruebas de la AEAT sin registros erróneos, su vista previa en PDF muestra los importes que da el programa y se presenta y queda registrado con su CSV; el total retenido coincide con la suma de los cuatro 111 del año.

## Decisions
- 2026-11-17 14:05 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT» a petición del propietario (2026-11-17 13:48), al confirmar que las informativas se pueden presentar desde un programa con TGVI Online (`sources/aeat/Especificaciones_TGVI_Online.pdf`, versión 2.8). Como declaración informativa no lleva pago; lo que valida la AEAT sin errores se presenta, y los registros erróneos quedan para una complementaria (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 14:05).

- 2026-11-18 12:40 · Hecho en la rama claude/project-thread-v1z0ue (desarrollo desatendido autorizado por el propietario; decisiones fiscales prudentes, pendientes de su validación), sin menú ni pantalla y sin cambio de esquema; se presenta con la pieza común de TGVI Online (`ServicioTgvi`, contabilidad/modelos-tributarios/presentacion 2026-11-18 12:40).
- 2026-11-18 12:40 · Percepciones: un registro por perceptor y clave con la suma del año: empleados clave A, administradores clave E con la subclave que se indique (la gestoría usa E.04 para el administrador), profesionales clave G.01; dinerario, retenciones, especie (cuota de autónomo) e ingresos a cuenta como en el 111. NIF, nombre y provincia de la ficha del tercero del empleado o del proveedor. Total = suma de las casillas 28 de los cuatro 111 (prueba).
- 2026-11-18 12:40 · Bloqueos: datos personales de las claves A y E (no están en el programa; harían falta en la ficha del empleado, con cambio de esquema), provincia (código postal) y NIF español del perceptor. El fichero se genera con los datos adicionales a cero para cuando estén.
- 2026-11-18 12:40 · Contraste con el 190 de 2025 de la gestoría (7 percepciones, 12.006,18 €): el programa da 5 percepciones y 11.841,97 €. Diferencia de 164,21 €: la paga extra de julio de 2025, que no está en la copia (administrador: 314,91 € de bruto y 110,22 € de retención; una empleada: 1.080 € y 54,00 €); es la misma diferencia del 3T del 111. Profesionales: mismo total (3.197,61 €), pero la gestoría asigna una factura de 309,66 € a otro profesional (por eso su 3T del 111 tenía 1 perceptor); el programa la asigna al proveedor de la ficha. La gestoría incluye además dos registros de clave L.99 (rentas exentas) que el programa no tiene. Especie de los empleados: igual (16.938,96 €, la cuota de autónomo).

## Requirements
- 2026-11-17 14:05 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo por TGVI Online con la pieza común.
- 2026-11-17 14:05 · draft: Generar el modelo 190 de cada ejercicio desde las nóminas y las facturas de profesionales con retención: un registro por perceptor con percepciones y retenciones, cuadrado con los cuatro 111, fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.

## Hand-over
- 2026-11-18 12:40 · Pendiente: guardar los datos personales de los trabajadores (cambio de esquema) y el código postal de los perceptores; presentar por TGVI cuando estén; validación del propietario.
