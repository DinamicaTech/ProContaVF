---
title: Facturas desde orígenes externos
status: draft
depends_on: [facturacion/factura-venta]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Analizar formularios del ProConta antiguo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G5wuUSovHLCVH22T7EntXcw
---
## Summary
Generación genérica de facturas de venta a partir de orígenes externos importados; el primer caso son las reservas de alojamiento, hoy automatizadas en la capa de código del accdb.
**Aceptación:** mismo lote no duplica borradores; una factura emitida nunca cambia por reimportación.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 13 RF-RES-01..03: reservas de alojamiento
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/RESERVAS_ALOJAMIENTO_CONTRATO.md: contrato anterior
- 2026-10-28 13:14 · Q3) a) Es algo que ya está automatizado actualmente con la capa de código del accdb pero hay que re-escribirla utilizando las nuevas clases e intentando hacerlo algo más genérico (Facturacion de ventas?)
- 2026-10-28 12:28 · draft: Reescribir la facturación de reservas de alojamiento como generación genérica de facturas desde orígenes externos, con las nuevas clases.
- 2026-10-28 12:28 · question: P-07 destinatario (plataforma o huésped), atribución mensual, cancelaciones y divisas: (a) mantener lo que hace hoy la capa del accdb ✅ (b) redefinir
- 2026-10-28 12:28 · question: ¿el inmueble? Activos está en el laboratorio: (a) mapa explícito propio hasta rediseñar activos ✅
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 3.11, 5.8: «Facturar pisos turísticos» crea una factura de venta por plataforma, mes y piso, una línea por reserva, sin IVA y solo de meses cerrados.
