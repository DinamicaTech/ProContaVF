---
title: Envío de correo
status: draft
depends_on: [datos/esquema, datos/persistencia, plataforma/configuracion, documentos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Cuentas, transporte SMTP con credenciales fuera de Access y coordinador saliente durable con intentos, lotes y estados que distinguen aceptación, fallo e incertidumbre.
**Aceptación:** la de spec § 22.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Reintentos automáticos solo ante fallo confirmado; ante incertidumbre nunca se reintenta ni se reenvía automáticamente (resuelve P-11).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 22 RF-COR-04..07: cuentas y secretos, coordinador saliente, reintento/reenvío, no duplicar transporte
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/GestorCorreo/docs/: contratos anteriores
- 2026-10-28 12:28 · draft: Implementar el envío de correo y los lotes con seguimiento.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-11 ¿política de reintentos automáticos? (a) solo ante fallo confirmado, nunca ante incertidumbre ✅ (b) ninguno automático
- 2026-10-31 16:54 · 6. a
- 2026-10-31 16:54 · answer: P-11 reintentos automáticos → (a) solo ante fallo confirmado, nunca ante incertidumbre
