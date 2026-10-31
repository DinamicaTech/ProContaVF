---
title: Geografía
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-10-31 18:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Países, provincias, poblaciones y códigos postales deducidos solo de los domicilios que ya hay en la BD (Terceros y DomiciliosTercero), lo más simple posible, con la resolución automática CP → provincia → población que usan los domicilios. No tiene tabla propia. Código: `CatalogoGeografico` y `FuenteGeografiaAccess` (src/ProConta.Organizacion/Geografia).
**Aceptación:** un CP con una sola provincia la selecciona; con ambigüedad se elige; sin correspondencia no se inventa.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · El catálogo geográfico se carga solo con los datos de la BD actual (sin importar el INE); la dirección física es cada vez más irrelevante, así que se mantiene lo más simple posible.
- 2026-10-31 18:55 · La maestra no tiene ninguna tabla geográfica (F_CDP, que lo parecía, es el catálogo de centros de coste del ERP). El catálogo se deduce, en solo lectura, de las combinaciones país/CP/provincia/población de Terceros y DomiciliosTercero; no se crea tabla ni se importa nada. Es la única lectura directa de tablas de terceros fuera de su dueño, y no escribe.
- 2026-10-31 18:55 · Se agrupan sin distinguir mayúsculas ni espacios repetidos y se muestra la forma escrita más frecuente (a igualdad, la que no está toda en mayúsculas). Un CP con una sola provincia la propone y, dentro de ella, la población si es única; con varias se deja elegir; sin correspondencia no se propone nada.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-05: orden y autocompletado del domicilio; catálogo geográfico pendiente
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 29 P-02: propietario de la geografía
- 2026-10-28 12:28 · draft: Definir e implementar el catálogo geográfico y su búsqueda por código postal.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿de dónde sale el catálogo? (a) INE (provincias y municipios) más el callejero de códigos postales disponible en la BD actual ✅ (b) solo los datos de la BD actual
- 2026-10-31 16:54 · 27. b. La dirección física es cada vez más irrelevante, por lo que cuanto más lo simplifiquemos, mejor
- 2026-10-31 16:54 · answer: origen del catálogo → (b) solo los datos de la BD actual: la dirección física es cada vez más irrelevante, cuanto más simple mejor
- 2026-10-31 17:53 · Derived from architecture: la búsqueda por código postal con los datos de la BD actual.

## Hand-over
- 2026-10-31 18:55 · Hecho: catálogo deducido y pruebas (CP con una provincia, ambiguo, sin correspondencia, listas, carga desde la BD). Pendiente: los países (código de 3 letras en Terceros.Pais sin catálogo de nombres) y refrescar el catálogo cuando se añaden domicilios; lo usará terceros/pantallas.
