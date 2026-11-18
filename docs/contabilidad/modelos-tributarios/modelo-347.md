---
title: Modelo 347
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/asientos, terceros/tercero]
threads:
  - 2026-11-17 14:05 · Analizar los servicios web de la AEAT | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSezTWx3SmyHa8owaSHFbbN
  - 2026-11-18 12:40 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
---
## Summary
Declaración anual de operaciones con terceras personas: clientes y proveedores con los que la sociedad supera 3.005,06 € en el año (sin las operaciones que van al 349 ni las que ya tienen retención), con el importe por trimestre, a partir de los libros de IVA; fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común (`presentacion`).
Hecho, pendiente de validar con la pantalla única «Modelos tributarios»: `Modelo347` y `Servicio347`. No hay 347 de 2025 en la carpeta de la gestoría para contrastar; en el entorno de pruebas de TGVI se validó y presentó uno de prueba (CSV L7QT7ZLTQBS5DHR8). Falta el registro de inmuebles de los arrendamientos de locales.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba se valida en TGVI Online del entorno de pruebas de la AEAT sin registros erróneos, su vista previa en PDF muestra los importes que da el programa y se presenta y queda registrado con su CSV; con la copia de pruebas, cada tercero listado supera el umbral y su importe anual es la suma de sus cuatro trimestres.

## Decisions
- 2026-11-17 14:05 · Creado como borrador desde el hilo «Analizar los servicios web de la AEAT» a petición del propietario (2026-11-17 13:48), al confirmar que las informativas se pueden presentar desde un programa con TGVI Online (`sources/aeat/Especificaciones_TGVI_Online.pdf`, versión 2.8). Como declaración informativa no lleva pago; lo que valida la AEAT sin errores se presenta, y los registros erróneos quedan para una complementaria (ver contabilidad/modelos-tributarios/presentacion 2026-11-17 14:05).

- 2026-11-18 12:40 · Hecho en la rama claude/project-thread-v1z0ue (desarrollo desatendido autorizado por el propietario; decisiones fiscales prudentes, pendientes de su validación), sin menú ni pantalla y sin cambio de esquema; se presenta con la pieza común de TGVI Online (`ServicioTgvi`, contabilidad/modelos-tributarios/presentacion 2026-11-18 12:40).
- 2026-11-18 12:40 · Declarados: de las líneas de IVA del año, por NIF español y clave (A compras, B ventas), las facturas IVA incluido (inversión del sujeto pasivo: la base y la marca), por trimestres, de los que pasan de 3.005,06 € en el año. Fuera: intracomunitarias (van al 349), importaciones, terceros sin NIF español, facturas de compra con retención (van al 190 o al 180) y registros del libro marcados «no incluir en el 347» (IVASoportado/IVARepercutido.IncluidoModelo347 = False). Provincia: las dos primeras cifras del código postal de la ficha del tercero; sin él, no se genera.
- 2026-11-18 12:40 · Pendiente prudente: el programa no marca «arrendamiento de local de negocio» ni genera los registros de inmueble que pide el 347 a quien alquila locales (referencia catastral y situación): la sociedad alquila locales, así que el 347 real necesitará esos registros antes de presentarse en producción.
- 2026-11-18 12:40 · Copia: el 2025 da 8 declarados (6 de compras y 2 de ventas, 135.308,65 €) y 37 terceros fuera por no tener NIF español (inquilinos con pasaporte u otro documento): hay que revisar sus fichas. Cinco terceros no tenían código postal; en ProConta-modelos-01 se puso uno inventado para la prueba.
- 2026-11-18 12:40 · TGVI (entorno de pruebas): la AEAT cruza en pruebas NIF y nombre con el censo (error 20701) y no admite al declarante como declarado (20502), así que el 347 de prueba lleva como declarado a la propia Agencia Tributaria (Q2826000H, dato público) con los importes de una compra y una venta de la copia: 2 registros correctos y presentación con CSV L7QT7ZLTQBS5DHR8, expediente 2025347009840900000123, número 3472500900001 y PDF archivado.

## Requirements
- 2026-11-17 14:05 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo por TGVI Online con la pieza común.
- 2026-11-17 14:05 · draft: Generar el modelo 347 de cada ejercicio desde los libros de IVA: terceros que superan 3.005,06 € con su importe por trimestre, fichero con el diseño de registro del ejercicio y presentación por TGVI Online con la pieza común contabilidad/modelos-tributarios/presentacion, después de la Versión Mínima Viable.

## Hand-over
- 2026-11-18 12:40 · Pendiente: registros de inmuebles y marca de arrendamiento de locales; contraste con un 347 presentado (no hay de 2025 en la carpeta); validación del propietario.
