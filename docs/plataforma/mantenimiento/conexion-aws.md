---
title: Conexión de AWS en Integraciones
status: draft
depends_on: [compras/reconocimiento]
threads:
  - 2026-11-24 10:17 · Valorar la integración con WhatsApp | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GJXpa4WaUhKrEnujzqPQJ6G
---
## Summary
Pendiente: llevar la conexión de AWS de cada sociedad (región, clave de acceso y secreto cifrado), que hoy está en la configuración del reconocimiento de facturas de compra (compras/reconocimiento, decisión 2026-11-07 14:17), a Parámetros y tablas › Integraciones › «AWS · Conexión», según la regla «Integraciones». La usarían Textract y, si se hace, el intermediario de WhatsApp (creative-lab/integracion-con-whatsapp).
**Aceptación:** las claves de AWS se ven y se cambian solo en Integraciones › «AWS · Conexión»; las claves ya guardadas siguen funcionando sin volver a escribirlas; «Probar el OCR» y el reconocimiento con Textract funcionan igual que antes.

## Decisions
- 2026-11-24 10:17 · Creado como borrador desde el debate de creative-lab/integracion-con-whatsapp, a petición del propietario.

## Requirements
- 2026-11-24 10:16 · Quizá AWS debería estar en el menú de integraciones ?
- 2026-11-24 10:17 · draft: Mover la conexión de AWS de cada sociedad (región, clave de acceso y secreto cifrado), que hoy está en la configuración del reconocimiento de facturas de compra, a Parámetros y tablas › Integraciones › «AWS · Conexión», para que la compartan Textract y, si se hace, el intermediario de WhatsApp.
