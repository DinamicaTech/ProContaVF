---
title: Envío de correo
status: draft
depends_on: [datos/esquema, datos/persistencia, plataforma/configuracion, documentos]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Cuentas, transporte SMTP con credenciales fuera de Access y coordinador saliente durable con intentos, lotes y estados que distinguen aceptación, fallo e incertidumbre.
**Aceptación:** la de spec § 22.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 22 RF-COR-04..07: cuentas y secretos, coordinador saliente, reintento/reenvío, no duplicar transporte
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/GestorCorreo/docs/: contratos anteriores
- 2026-10-28 12:28 · draft: Implementar el envío de correo y los lotes con seguimiento.
- 2026-10-28 12:28 · question: P-11 ¿política de reintentos automáticos? (a) solo ante fallo confirmado, nunca ante incertidumbre ✅ (b) ninguno automático
