---
title: Descripciones de los campos de la BD
depends_on: []
status: draft
---
## Summary
Pendiente: completar la descripción de todos los campos de la BD. El 2026-11-24, en producción (esquema 82), tenían descripción 1.077 de los 2.103 campos; 78 tablas no tenían ningún campo descrito, sobre todo las heredadas del ProConta antiguo y del ERP. Propuesta: un fichero en el repositorio con «tabla.campo → descripción» que aplica el ActualizadorEsquema con DAO (como `Descripciones` de las migraciones), un borrador redactado a partir del código, los nodos y la maestra 0.24 del desarrollo anterior, revisado por el propietario por áreas, y una prueba que falle si un campo nuevo llega sin descripción.
**Aceptación:** todos los campos de una BD actualizada tienen descripción, el diccionario la muestra y la prueba impide campos nuevos sin ella.

## Decisions
- 2026-11-24 19:15 · Creado como borrador en el hilo «Añadir las relaciones que faltan» (datos/esquema).

## Requirements
- 2026-11-24 18:55 · Antes de empezar, estoy viendo que muy pocos campos de la BD contienen descripción, cuando la idea era tener la BD bien documentada.
- 2026-11-24 18:56 · draft: Completar la descripción de todos los campos de la BD desde un fichero tabla.campo→descripción que aplica el ActualizadorEsquema, con una prueba que impida campos nuevos sin descripción.
