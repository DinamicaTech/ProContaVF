---
title: Capa de persistencia
status: draft
depends_on: [datos/esquema, nucleo]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-10-31 18:40 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Capa común sobre ACE/OLE DB x64 en `src/ProConta.Datos.Persistencia`, la única que abre conexiones: `IBaseDatos` (conexión corta por trabajo, con o sin transacción), `ISesionDatos` (consultas con parámetros por posición, concurrencia optimista, numeración dentro de transacción, lectura de la estructura) e `IFilaDatos` (lectura de campos sin tipos de OLE DB). Traduce los fallos técnicos (tabla ausente, duplicado, integridad, bloqueo, BD no disponible, conflicto de versión) a excepciones con un `Error` explicado, nunca a «vacío» o «sin permiso». El dominio no conoce OLE DB.
**Aceptación:** pruebas sobre copia: transacción revertida no deja filas, conflicto de versión detectado, tabla ausente da error explícito.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Concurrencia optimista con versión de fila en las fichas editables y numeración dentro de transacción (confirma architecture 2026-10-28 12:45).
- 2026-10-31 18:40 · Contrato: `IBaseDatos.EjecutarAsync` (sin transacción) y `EnTransaccionAsync` (confirma si termina bien; revierte si lanza o si devuelve un `Resultado` fallido). Cada llamada abre y cierra su conexión; no se mantienen conexiones abiertas entre operaciones.
- 2026-10-31 18:40 · Concurrencia optimista: la orden de actualización filtra por la versión leída y `ActualizarConVersionAsync` lanza `ConflictoConcurrenciaException` si no afecta a ninguna fila, con un mensaje que pide reabrir el registro. En la maestra solo Asientos y Activos tienen columna Version; cada ente que necesite una la pide a datos/esquema.
- 2026-10-31 18:40 · Numeración: `SiguienteNumeroAsync` (MAX + 1) solo dentro de una transacción; la unicidad la garantiza un índice único y, si otra sesión gana la carrera, el llamador repite la transacción al recibir `UnicidadException`.
- 2026-10-31 18:40 · Parámetros por posición con tipos OLE DB explícitos: decimal e Importe como CURRENCY, fechas sin milisegundos como DATE, textos de más de 255 como LONGTEXT, null como DBNull.
- 2026-10-31 18:40 · Modos de apertura: compartido (uso normal), solo lectura (diagnóstico y diccionario) y exclusivo (ActualizadorEsquema). Sin grupo de conexiones (OLE DB Services=-4), para que cerrar libere el fichero de verdad.
- 2026-10-31 18:40 · Errores de ACE: se reconoce el número de error de Access que ACE devuelve en SQLState (3078 tabla ausente, 3022 duplicado, 3200/3201 integridad, 3704/3045/3008… bloqueo, 3024/3044 BD no disponible) y, si falta, el texto del mensaje.
- 2026-10-31 18:40 · Particularidades del ACE de Office instalado (Hacer clic y ejecutar) comprobadas en el equipo del propietario: falla con una violación de acceso si dos hilos del mismo proceso abren o cierran conexiones a la vez, así que la apertura y el cierre se serializan dentro del proceso; y falla al descargarse cuando termina un proceso .NET, aunque todo esté cerrado, así que los ejecutables terminan con `BaseDatosAccess.TerminarProceso(código)` como último paso.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-01: capas separadas, consultas parametrizadas, conexiones cortas fuera de HTTP/SMTP/OCR
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-03: concurrencia optimista en fichas y unicidad en BD
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 4 RF-CFG-04: tabla ausente o fallo de lectura no equivale a catálogo vacío
- 2026-10-28 12:28 · draft: Crear la capa de persistencia común sobre ACE/OLE DB con pruebas de integración sobre una copia de la maestra.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿política de concurrencia? (a) optimista con versión de fila en las fichas editables y transacción en numeración ✅ (b) bloqueo pesimista al abrir ficha
- 2026-10-31 16:54 · 10. a
- 2026-10-31 16:54 · answer: política de concurrencia → (a) optimista con versión de fila en las fichas editables y transacción en numeración
- 2026-10-31 17:53 · Derived from architecture: la capa OLE DB común (conexiones y transacciones cortas, parámetros por posición, concurrencia optimista, errores explícitos) con pruebas de integración sobre una copia de la maestra.

## Hand-over
- 2026-10-31 18:40 · Hecho: capa completa y 11 pruebas contra una copia sin datos de la maestra (transacción revertida por excepción y por resultado fallido, confirmación, conflicto de versión, tabla ausente, duplicado, tipos de parámetros, numeración, lectura de estructura, BD inexistente, la copia no tiene datos de negocio). Las pruebas contra Access se ejecutan sin paralelismo. Pendiente: comprobar en el Windows Server que el ACE instalado allí no tiene los mismos fallos (si es el redistribuible y no el de Office, quizá no los tenga).
