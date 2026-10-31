---
title: Núcleo común
status: draft
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:02 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Tipos compartidos por todas las áreas, en la biblioteca `src/ProConta.Nucleo`: NIF y su validación (`Identificacion/Nif`), importes Decimal con redondeo monetario (`Importes/Importe`), contexto sociedad/ejercicio (`Contexto/ContextoSociedadEjercicio`), resultado con errores recuperables (`Resultados/Resultado` y `Error`: registro afectado, causa y acción correctora) y auditoría (`Auditoria`: actor, instante, operación, contexto y correlación). No contiene reglas de negocio de ningún área.
**Aceptación:** la biblioteca compila, la usan al menos dos áreas y sus pruebas cubren validación de NIF y redondeo.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
2026-10-31 19:02 · NIF: un único tipo `Nif` para DNI, NIE, NIF especial (K, L, M) y NIF de entidad (antiguo CIF); se normaliza a mayúsculas sin espacios, guiones, puntos ni prefijo «ES» y se comprueba el carácter de control (letra, cifra o cualquiera de las dos según la letra inicial de la entidad). Un NIF inválido devuelve un `Error` que explica el carácter esperado, nunca una excepción.
2026-10-31 19:02 · Importe: struct sobre decimal siempre redondeado a dos decimales AwayFromZero; las operaciones redondean el resultado y la suma de líneas suma importes ya redondeados (redondeo por línea y después suma, RF-FAC-02).
2026-10-31 19:02 · ContextoSociedadEjercicio lleva el código permanente de la sociedad (texto), la clave de su instancia anual y el año; lo construye y valida organizacion/sociedades-ejercicios.
2026-10-31 19:02 · Los errores recuperables se devuelven como `Resultado`/`Resultado<T>` con uno o varios `Error` (código estable, mensaje en primera persona, registro afectado, causa, acción correctora y detalle técnico sin secretos); las excepciones quedan para fallos de programación o técnicos.
2026-10-31 19:02 · La auditoría se expresa con `MarcaAuditoria` (actor, instante local, operación, contexto, correlación) y un reloj inyectable (`IReloj`) para las pruebas.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-02: convenciones de código (adaptadas a C# en architecture)
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-02: Decimal y redondeo AwayFromZero a dos decimales, base del tipo Importe
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 6 RF-UI-06 y 27 RF-TEC-04: errores con registro afectado, causa y acción correctora; logs con correlación/actor/contexto sin secretos
- 2026-10-28 12:28 · draft: Crear en la solución nueva la biblioteca común con los tipos compartidos (NIF, Importe, ContextoSociedadEjercicio, Resultado/Error, Auditoría) y sus pruebas unitarias.
- 2026-10-31 17:53 · Derived from architecture: la biblioteca `ProConta.Nucleo` con NIF, Importe, ContextoSociedadEjercicio, Resultado/Error y auditoría, con sus pruebas unitarias (tests/ProConta.Nucleo.Tests).

## Hand-over
- 2026-10-31 19:02 · Hecho: biblioteca y 36 pruebas unitarias (NIF válidos e inválidos de cada tipo, normalización, redondeo, suma por líneas, porcentajes, resultados y auditoría). Pendiente: comprobar el criterio «la usan al menos dos áreas» cuando entren organización y terceros; validar NIF intracomunitarios no españoles queda fuera hasta que lo pida un consumidor.
