---
title: Contactos y destinatarios
depends_on: [terceros/tercero]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-11-01 10:46 · Desarrollar la pantalla de Terceros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GVR2rh2hJF6h9BMYjKSu8jn
---
## Summary
Emails y teléfonos con flags de envío, resolución de destinatarios efectivos, contactos libres sin crear tercero y relaciones persona-organización con vigencia. Código (src/ProConta.Terceros/Contactos): `DatoContacto`, `ContactoLibre` y las relaciones forman parte de la ficha del tercero y se guardan con ella; `ResolutorDestinatarios` resuelve los destinatarios de una finalidad.
**Aceptación:** email único activo resuelve sin flags; dos activos ambiguos producen incidencia; contacto libre no crea tercero; la relación se elige con el buscador modal.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 19:05 · Datos de contacto (DatosContacto: EMAIL o TELEFONO, etiqueta, activo, EnvioFacturas, EnviosAdministrativos) y contactos libres (ContactosLibresTercero) son parte del agregado Tercero: se validan y guardan en la misma transacción que la ficha; un dato no puede apuntar a un contacto libre de otro titular.
- 2026-10-31 19:05 · Resolución de destinatarios (RF-TER-08): todos los emails activos marcados para la finalidad; si no hay marcados y hay una sola dirección activa distinta, esa sin cambiar marcas; si hay varias, incidencia explicada y ningún destinatario; sin emails activos, incidencia. La incidencia es un resultado, no una excepción, para no abortar el resto de envíos.
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).
- 2026-11-01 10:46 · Relaciones persona-organización (RelacionesContactos, RF-TER-11) dentro de la ficha: tipo, fechas desde y hasta, estado (Sin guardar, Vigente o Finalizada) y observaciones; la otra parte se elige con el buscador de terceros en una ventana modal («Vincular persona…» o «Vincular a organización…»). Si la persona no tiene el rol Contacto se le da, de forma idempotente, en la misma transacción. «Quitar» solo existe para una relación aún no guardada; una guardada se termina con «Cerrar vigencia», que le pone la fecha fin de hoy. El historial queda en los dos terceros.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7.1 RF-TER-07..11: datos de contacto, destinatarios, contactos libres, relaciones
- 2026-10-28 12:28 · draft: Implementar contactos, resolución de destinatarios, contactos libres y relaciones.
- 2026-10-31 17:53 · Derived from architecture: datos de contacto y contactos libres dentro de la ficha del tercero y la resolución de destinatarios.
- 2026-11-01 10:46 · Derived from terceros/pantallas: guardar las relaciones con vigencia dentro de la ficha y elegir la otra parte con el buscador modal.
- 2026-11-01 12:49 · De todas formas, la idea sería migrar los datos de forma que no haya que hacer revisión manual de cada ficha.

## Hand-over
- 2026-10-31 19:05 · Hecho: datos de contacto, contactos libres y resolución de destinatarios, con pruebas. Pendiente: relaciones persona-organización con vigencia (RelacionesContactos, RF-TER-11), que llegarán con terceros/pantallas; la pastilla de destinatarios y el botón Enviar email son de pantalla.
- 2026-11-01 10:46 · Hecho: relaciones persona-organización con vigencia, con pruebas. Pendiente: el botón Enviar email, que llega con el correo.
