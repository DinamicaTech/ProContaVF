---
title: Tercero
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, organizacion/geografia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 20:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Ente Tercero: identidad estable (IdTercero) de persona física, jurídica o entidad sin personalidad; NIF revisable y no único; domicilio principal y adicionales. Nunca se fusionan terceros por NIF o nombre. Contrato `IRepositorioTerceros` (src/ProConta.Terceros/Tercero): obtener la ficha, buscar y guardar la ficha completa (con domicilios, datos de contacto y contactos libres) en una transacción con concurrencia optimista e historial.
**Aceptación:** dos terceros con igual NIF siguen siendo distintos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 10:20 · La consulta de datos de empresas por CIF en uproc.io que tenía ProConta antiguo (`CProveedor`) está obsoleta (el servicio dejó de funcionar) y no se migra.
- 2026-10-31 20:05 · Tipos de persona guardados en Terceros.TipoPersona como FISICA, JURIDICA o ENTIDAD; los registros antiguos sin tipo lo conservan nulo (no se inventa clasificación) y un tercero nuevo debe tenerlo. Física exige nombre (apellidos opcionales); jurídica y entidad, razón social (nombre comercial opcional). La columna Nombre se calcula: nombre y apellidos, o razón social.
- 2026-10-31 20:05 · El NIF español (identificación vacía, NIF, CIF, NIE o DNI) se valida con nucleo solo al dar de alta o al cambiarlo, para que un NIF antiguo mal escrito no impida guardar otros cambios; otras identificaciones (VAT, pasaporte…) no se validan.
- 2026-10-31 20:05 · Concurrencia optimista con la nueva columna Terceros.Version (migración 20261031_2005_TERCEROS_VERSION; las filas existentes empiezan en 0). Guardar sustituye los domicilios adicionales, los contactos libres y los datos de contacto por los de la ficha en la misma transacción.
- 2026-10-31 20:05 · Historial: cada guardado escribe en HistorialTerceros una fila por campo cambiado (operación ALTA o MODIFICACION, valor antiguo y nuevo, operador e identificador de operación común) y una fila resumen si cambian los datos de contacto o los domicilios.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-01, 02, 06: identidad, tipos de persona, domicilios
- 2026-10-28 12:28 · draft: Implementar el ente Tercero y publicar su contrato.
- 2026-10-31 17:53 · Derived from architecture: el ente Tercero con su contrato (ficha, búsqueda, guardado con concurrencia optimista e historial).

## Hand-over
- 2026-10-31 20:05 · Hecho: ente, repositorio y pruebas (reglas por tipo, nombre calculado, NIF solo al cambiar, NIF repetido sin fusión, ficha completa en una transacción, conflicto de versión, nada a medias ante un error, filas antiguas con versión 0 tras migrar). Pendiente: importación con mapas de origen (MapasOrigenTerceros, implantacion), instantánea fiscal en documentos emitidos (facturación) y baja de terceros.
