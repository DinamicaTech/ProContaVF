---
title: Recepción de correo
status: draft
depends_on: [correo/envio, compras/reconocimiento, norma43, nominas]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Mail Dispatcher: recibe correo, autoriza remitentes, clasifica adjuntos (Norma 43, nóminas, facturas de compra) y los pasa a sus motores, interpreta comandos de informe y responde, con hitos persistidos para no duplicar.
**Aceptación:** la de spec § 23.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 23 RF-DIS-01..07: dispatcher, protocolo, flujo de factura recibida, hitos, comandos
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/MailDispatcher/: documentos y código anteriores
- 2026-10-28 12:28 · draft: Implementar la recepción y el procesamiento de correo.
- 2026-10-28 12:28 · question: RF-DIS-02 ¿protocolo? (a) POP3 con UIDL, como la implementación posterior ✅ (b) IMAP (c) ambos
