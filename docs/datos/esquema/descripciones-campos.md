---
title: Descripciones de los campos de la BD
depends_on: []
status: draft
threads:
  - 2026-11-24 20:25 · Completar las descripciones de los campos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCbwpi32KB1HMnLzoL8VDBR
---
## Summary
Todas las tablas y campos de la BD tienen descripción (la propiedad Description de Access, que recoge el diccionario). Las que no traen de su migración, sobre todo las heredadas del ProConta antiguo y del ERP, están en el catálogo `src/ProConta.Datos.Esquema/Descripciones/<área>.txt` («Tabla: texto» y «Tabla.Campo: texto»; un «?» delante marca las pendientes de confirmar). El ActualizadorEsquema las escribe con DAO al terminar cada actualización y con el comando `descripciones <bd> <carpeta de copias>` (copia previa, BD en exclusiva, sin tocar estructura ni datos). La prueba `DescripcionesCamposAccessTests` falla si una tabla o un campo de la BD de pruebas no tiene descripción.
Es un borrador: el catálogo (1.228 descripciones, 161 por confirmar) espera la revisión del propietario por áreas ; ya está aplicado a producción y las correcciones se volverán a aplicar con el comando.
**Aceptación:** todos los campos de una BD actualizada tienen descripción, el diccionario la muestra y la prueba impide campos nuevos sin ella.

## Decisions
- 2026-11-24 19:15 · Creado como borrador en el hilo «Añadir las relaciones que faltan» (datos/esquema).
- 2026-11-24 20:25 · Catálogo de descripciones (plan acordado con el propietario al crear el nodo): un fichero por área del diccionario en `src/ProConta.Datos.Esquema/Descripciones/`, incrustado en el ensamblado (`CatalogoDescripciones`). Cada tabla va en el fichero de su área según `DuenosTablas` (lo comprueba una prueba). La maestra no se toca: las descripciones se escriben en cada BD al actualizarla. Las nuevas tablas y campos siguen llevando su descripción en las `Descripciones` de su migración; el catálogo es para lo heredado y para corregir.
- 2026-11-24 20:25 · Escritura: `EscritorDescripciones.Completar` escribe solo las que faltan o son distintas y se salta las tablas y campos que la BD no tiene. El ActualizadorEsquema la llama al final de cada actualización (si falla, solo avisa: la estructura queda bien) y el comando `descripciones` la aplica a una BD ya actualizada, con la copia `<bd>.pre-descripciones-v<versión>-<fecha>.accdb`; si la BD no está en la versión del programa, pide actualizarla primero.
- 2026-11-24 20:25 · Borrador del catálogo redactado desde el código (mantenimientos, migraciones, enumeraciones), los nodos, los valores que hay en una copia de producción y las descripciones de la maestra 0.24 del desarrollo anterior (151 campos). Llevan «?» las tablas heredadas que el programa actual no usa (Mail Dispatcher, CuentasActivo, LotesAmortizacionActivo, ConversacionesIA…) y los campos cuyo significado no se puede confirmar en el código.
- 2026-11-24 20:25 · 100 descripciones que ya había en producción (Configuracion, ConfiguracionCorreo, EnviosCorreo, VeriFactu*, Conceptos, LineasContrato…) tenían los acentos mal codificados («ConfiguraciÃ³n»); el catálogo las reescribe bien. Visto de paso: EstadosFactura.Descripcion tiene el mismo problema en los datos (no en la estructura); queda fuera de este nodo.
- 2026-11-24 20:53 · Aplicado a producción (C:\Docs\ProConta\datos\ProConta.accdb, esquema 83) con el comando `descripciones` y ProContaVF y Claude Desktop cerrados por el propietario: 1.228 descripciones escritas, 0 tablas o campos sin descripción; copia previa `copias-esquema\ProConta.pre-descripciones-v83-20261124-205307.accdb`. Las correcciones de la revisión se aplicarán igual.
- 2026-11-24 20:25 · Comprobado en una copia de producción (esquema 83, copia descripciones-01): el comando escribió 1.228 descripciones y quedaron 0 tablas o campos sin descripción y 0 con acentos rotos.

## Requirements
- 2026-11-24 18:55 · Antes de empezar, estoy viendo que muy pocos campos de la BD contienen descripción, cuando la idea era tener la BD bien documentada.
- 2026-11-24 18:56 · draft: Completar la descripción de todos los campos de la BD desde un fichero tabla.campo→descripción que aplica el ActualizadorEsquema, con una prueba que impida campos nuevos sin descripción.
