---
title: VeriFactu
status: draft
depends_on: [facturacion/factura-venta, plataforma/configuracion]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Intercambio fiscal con la AEAT encapsulado detrás de una interfaz propia (.NET nativo, sin COM): traducción de la factura, envío, estados de respuesta, recuperación de incertidumbres, PDF/QR tras el hito fiscal.
**Aceptación:** la de spec § 11; entorno de pruebas imposible de confundir con producción.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 11 RF-VF-01..05: integración, traducción, estados, recuperación, PDF/QR
- 2026-10-28 12:28 · From ProConta/VeriFactu-main (disco del propietario): biblioteca VeriFactu .NET de referencia
- 2026-10-28 12:28 · draft: Integrar VeriFactu .NET detrás de una interfaz propia, con pruebas que no emiten realmente.
