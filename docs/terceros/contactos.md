---
title: Contactos y destinatarios
status: draft
depends_on: [terceros/tercero]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 20:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Emails y teléfonos con flags de envío, resolución de destinatarios efectivos, contactos libres sin crear tercero y relaciones persona-organización con vigencia. Código (src/ProConta.Terceros/Contactos): `DatoContacto` y `ContactoLibre` forman parte de la ficha del tercero y se guardan con ella; `ResolutorDestinatarios` resuelve los destinatarios de una finalidad.
**Aceptación:** email único activo resuelve sin flags; dos activos ambiguos producen incidencia; contacto libre no crea tercero.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 20:05 · Datos de contacto (DatosContacto: EMAIL o TELEFONO, etiqueta, activo, EnvioFacturas, EnviosAdministrativos) y contactos libres (ContactosLibresTercero) son parte del agregado Tercero: se validan y guardan en la misma transacción que la ficha; un dato no puede apuntar a un contacto libre de otro titular.
- 2026-10-31 20:05 · Resolución de destinatarios (RF-TER-08): todos los emails activos marcados para la finalidad; si no hay marcados y hay una sola dirección activa distinta, esa sin cambiar marcas; si hay varias, incidencia explicada y ningún destinatario; sin emails activos, incidencia. La incidencia es un resultado, no una excepción, para no abortar el resto de envíos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7.1 RF-TER-07..11: datos de contacto, destinatarios, contactos libres, relaciones
- 2026-10-28 12:28 · draft: Implementar contactos, resolución de destinatarios, contactos libres y relaciones.
- 2026-10-31 17:53 · Derived from architecture: datos de contacto y contactos libres dentro de la ficha del tercero y la resolución de destinatarios.

## Hand-over
- 2026-10-31 20:05 · Hecho: datos de contacto, contactos libres y resolución de destinatarios, con pruebas. Pendiente: relaciones persona-organización con vigencia (RelacionesContactos, RF-TER-11), que llegarán con terceros/pantallas; la pastilla de destinatarios y el botón Enviar email son de pantalla.
