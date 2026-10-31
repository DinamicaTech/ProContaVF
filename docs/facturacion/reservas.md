---
title: Facturas desde orígenes externos
status: draft
depends_on: [facturacion/factura-venta]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Modelo genérico de ventas registradas antes de facturar (nombre por decidir: VentasDiferidas, Albaranes, VentasExternas…), que se facturan y contabilizan después de forma masiva; el caso principal son las reservas de pisos turísticos, hoy automatizadas en la capa de código del accdb, que sirve de referencia.
**Aceptación:** mismo lote no duplica borradores; una factura emitida nunca cambia por reimportación.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Este nodo se redefine como un modelo genérico de ventas registradas antes de facturar (nombre por decidir: VentasDiferidas, Albaranes, VentasExternas…), que se facturan y contabilizan después de forma masiva; las reservas de pisos turísticos son su caso principal. Lo que hace hoy la capa del accdb (factura por plataforma, mes y piso, una línea por reserva, sin IVA, solo meses cerrados) es la referencia, pero no se copia tal cual (resuelve P-07).
- 2026-10-31 16:54 · El inmueble de cada venta se resuelve con un mapa explícito propio hasta que se formalicen los activos (creative-lab/activos).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 13 RF-RES-01..03: reservas de alojamiento
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/RESERVAS_ALOJAMIENTO_CONTRATO.md: contrato anterior
- 2026-10-28 13:14 · Q3) a) Es algo que ya está automatizado actualmente con la capa de código del accdb pero hay que re-escribirla utilizando las nuevas clases e intentando hacerlo algo más genérico (Facturacion de ventas?)
- 2026-10-28 12:28 · draft: Reescribir la facturación de reservas de alojamiento como generación genérica de facturas desde orígenes externos, con las nuevas clases.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-07 destinatario (plataforma o huésped), atribución mensual, cancelaciones y divisas: (a) mantener lo que hace hoy la capa del accdb ✅ (b) redefinir
- 2026-10-31 16:54 · 17. b. La definición de a) es bastante correcta, pero probablemente busquemos un modelo algo más genérico que no 'ReservasPisos'. Quizá VentasDiferidas, Albaranes, VentasExternas .....
- 2026-10-31 16:54 · 16. a, aunque aparecerá un submodelo de albaranes o algo similar para poder registrar ventas (en forma de albaranes o similar) que se facturarán (y contabilizarán) posteriormente de forma masiva. Aplicable, principalmente, a las reservas de los pisos turísticos
- 2026-10-31 16:54 · answer: P-07 destinatario, atribución, cancelaciones y divisas → (b) redefinir: lo que hace hoy la capa del accdb es bastante correcto, pero se busca un modelo más genérico que «ReservasPisos» (VentasDiferidas, Albaranes, VentasExternas…)
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿el inmueble? Activos está en el laboratorio: (a) mapa explícito propio hasta rediseñar activos ✅
- 2026-10-31 16:54 · 18. Correcto, hasta que formalicemos activos
- 2026-10-31 16:54 · answer: inmueble → (a) mapa explícito propio hasta que se formalicen los activos
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 3.11, 5.8: «Facturar pisos turísticos» crea una factura de venta por plataforma, mes y piso, una línea por reserva, sin IVA y solo de meses cerrados.
