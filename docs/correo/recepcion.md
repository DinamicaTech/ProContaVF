---
title: Recepción de correo
status: draft
depends_on: [correo/envio, compras/reconocimiento, norma43, nominas]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Mail Dispatcher: recibe correo, autoriza remitentes, clasifica adjuntos (Norma 43, nóminas, facturas de compra) y los pasa a sus motores, interpreta comandos de informe y responde, con hitos persistidos para no duplicar.
**Aceptación:** la de spec § 23.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Protocolo de recepción: POP3 con UIDL; la idempotencia se basa en cuenta + UIDL (resuelve RF-DIS-02).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 23 RF-DIS-01..07: dispatcher, protocolo, flujo de factura recibida, hitos, comandos
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/MailDispatcher/: documentos y código anteriores
- 2026-10-28 12:28 · draft: Implementar la recepción y el procesamiento de correo.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-DIS-02 ¿protocolo? (a) POP3 con UIDL, como la implementación posterior ✅ (b) IMAP (c) ambos
- 2026-10-31 16:54 · 7. a
- 2026-10-31 16:54 · answer: RF-DIS-02 protocolo → (a) POP3 con UIDL, como la implementación posterior
- 2026-10-29 17:45 · Derived from nominas: aceptar el fichero Excel de nóminas enviado al correo de ProContaVF y pasarlo a nominas para que lo parsee, importe y genere los asientos.
- 2026-10-31 16:54 · Derived from compras/reconocimiento: guardar cada factura de compra recibida por correo en `<raíz>\<ejercicio>\Proveedores\<carpeta del proveedor>`, creando la carpeta del proveedor (nombre comercial) si no existe, para que la incorpore el escaneo por checksum.
