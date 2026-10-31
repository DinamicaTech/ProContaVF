---
title: Estructura de la BD (ActualizadorEsquema)
status: draft
depends_on: [architecture]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Único dueño del DDL: crea y versiona la maestra, actualiza instalaciones en producción con respaldo y diagnóstico, y publica el diccionario de datos (tablas, campos, tipos, longitudes, índices, relaciones y dueño funcional de cada tabla) que leen los demás nodos. Cada área pide aquí sus cambios de estructura con una línea "Derived from".
**Aceptación:** crear la maestra desde cero, segunda ejecución idempotente, migración parcial detectada, backup recuperable (spec § 25).

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Maestra de arranque: C:\docs\codigo\ProContaVF\ProConta\Proconta.accdb (fuera de Git), con su versión y SHA-256 registrados al activarla; antes se comprueba si tiene cambios pendientes de aplicar (manifiestos de migraciones antiguos). Terceros.Email tiene 254 caracteres (resuelve P-01).
- 2026-10-31 16:54 · El diccionario de datos se publica como un md por área, generado desde la maestra y nunca editado a mano.

## Requirements
- 2026-10-28 12:28 · es importante que haya un nodo especializado en mantener la estructura de la base de datos para poder actualizar el modelo cuando se actualice una instalación en producción, por lo tanto, cualquier nodo que quiera modificar la estructura de la Base de Datos ha de comunicarlo o pasar a través del nodo de mantenimiento de estructura de BD.
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 25 RF-ESQ-01..06: evolución reproducible con versiones, diagnóstico, respaldo, maestra limpia separada de actualización e importación
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 30 paso 1: matriz de propietario por tabla y API
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/ActualizadorEsquema/: manifiestos de migraciones 001..024 y documentos del actualizador antiguo (referencia, no ejecutable)
- 2026-10-28 12:28 · draft: Construir el ActualizadorEsquema en C# a partir de la maestra elegida y publicar el diccionario de datos con el dueño funcional de cada tabla.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-01 ¿qué base es la maestra de arranque? (a) ProConta.accdb del paquete de traspaso, el modelo más actualizado según 00_LEEME ✅ (b) la maestra 0.24 de ActualizadorEsquema (c) una maestra limpia nueva; y resolver Terceros.Email 150 frente a 254
- 2026-10-31 16:54 · 8. Creo que la BD Maestra más actualizada es C:\docs\codigo\ProContaVF\ProConta\Proconta.accdb aunque es posible que haya cambios pendientes de actualizar.
- 2026-10-31 16:54 · Un campo de 254 para un email es más que suficiente
- 2026-10-31 16:54 · answer: P-01 maestra de arranque → (a) la más actualizada es C:\docs\codigo\ProContaVF\ProConta\Proconta.accdb, aunque puede tener cambios pendientes de actualizar; Terceros.Email con 254
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿formato del diccionario de datos publicado? (a) un md por área generado desde la maestra ✅ (b) un único md con todas las tablas (c) solo el JSON de migraciones
- 2026-10-31 16:54 · 9. a
- 2026-10-31 16:54 · answer: formato del diccionario de datos → (a) un md por área generado desde la maestra
