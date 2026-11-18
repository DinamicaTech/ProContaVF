---
title: Modelo 349
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/modelos-tributarios/modelo-303]
threads:
  - 2026-11-17 14:05 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
  - 2026-11-18 12:40 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
---
## Summary
Declaración recapitulativa de operaciones intracomunitarias, del periodo que corresponda (trimestral o mensual): adquisiciones y prestaciones intracomunitarias por operador con su NIF-IVA (verificado con el VIES), a partir de los libros de IVA, cuadrando con las casillas intracomunitarias del 303; fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común (`presentacion`). En la sociedad, el caso típico son las comisiones de Airbnb y Booking.
Hecho, pendiente de validar con la pantalla única «Modelos tributarios»: `Modelo349` y `Servicio349` (`src/ProConta.Contabilidad/Modelos`). En la copia de pruebas el 2T de 2026 da lo mismo que la gestoría (3 operadores, 6.207,11 € = casilla 10 del 303) y en el entorno de pruebas de TGVI se validó y presentó (CSV AKXGZUYXAN5E8SFS).
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba se valida en TGVI Online del entorno de pruebas de la AEAT sin registros erróneos, su vista previa en PDF muestra los importes que da el programa y se presenta y queda registrado con su CSV; el total de adquisiciones de servicios intracomunitarios del periodo coincide con las casillas de adquisiciones intracomunitarias del 303 del mismo periodo.

## Decisions
- 2026-11-17 14:05 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT» a petición del propietario (2026-11-17 13:48), al confirmar que las informativas se pueden presentar desde un programa con TGVI Online (`sources/aeat/Especificaciones_TGVI_Online.pdf`, versión 2.8). Como declaración informativa no lleva pago; lo que valida la AEAT sin errores se presenta, y los registros erróneos quedan para una complementaria (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 14:05).

- 2026-11-18 12:40 · Hecho en la rama claude/project-thread-v1z0ue (desarrollo desatendido autorizado por el propietario; decisiones fiscales prudentes, pendientes de su validación), sin menú ni pantalla y sin cambio de esquema; se presenta con la pieza común de TGVI Online (`ServicioTgvi`, contabilidad/modelos-tributarios/presentacion 2026-11-18 12:40).
- 2026-11-18 12:40 · Operadores: de las líneas de IVA del trimestre, las adquisiciones intracomunitarias de bienes (clave A: IVA en la cuenta «Intracomunitario soportado · bienes» del ejercicio) y de servicios (I), las entregas intracomunitarias exentas (E5, clave E) y las prestaciones de servicios no sujetas por localización a un cliente de la UE (N2, clave S); un registro por NIF comunitario y clave con la suma de sus bases. Cuadre con el 303 del trimestre: A + I = casilla 10 y E = casilla 59 (aviso si no).
- 2026-11-18 12:40 · País del operador: el prefijo del NIF si es de la UE; si no, el país de la ficha del tercero buscada por el número del NIF (en la copia Airbnb figura como «9827384L» en el libro y como «IE9827384L» con país IE en su ficha) y, en último lugar, el del libro de IVA, que en datos migrados a veces es «ES» y no vale. Sin país de la UE la declaración no se genera.
- 2026-11-18 12:40 · Sin registros de rectificaciones: un operador con la suma negativa (abonos de otro periodo) impide generar el 349 y se explica que hay que presentarlo desde la Sede. Plazo: el mes siguiente al trimestre (4T, hasta el 30 de enero). Teléfono de contacto obligatorio (la AEAT rechaza el tipo 1 sin él: error 10703; en ProConta-modelos-01 se puso uno inventado).
- 2026-11-18 12:40 · Contraste: el 2T de 2026 de la gestoría y el programa coinciden operador a operador (IE clave I 4.344,74; NL clave I 1.810,49; DE clave A 51,88). El 3T de 2026 da 5 operadores y 670,55 €.
- 2026-11-18 12:40 · TGVI (entorno de pruebas, que solo admite datos de prueba): se enviaron los importes del 2T con los NIF comunitarios y nombres de los operadores inventados: 3 registros correctos, vista previa en PDF y presentación con CSV AKXGZUYXAN5E8SFS, expediente 2026349009620900200013, número 3492602900001 y PDF archivado.

## Requirements
- 2026-11-17 14:05 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo por TGVI Online con la pieza común.
- 2026-11-17 14:05 · draft: Generar el modelo 349 de cada periodo desde los libros de IVA: operadores intracomunitarios con su NIF-IVA y su importe por clave de operación, cuadrado con el 303, fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.

## Hand-over
- 2026-11-18 12:40 · Pendiente: los registros de rectificaciones; validación del propietario.
