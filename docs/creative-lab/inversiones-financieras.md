---
title: Inversiones financieras
status: idea
depends_on: [activos]
threads:
  - 2026-11-12 10:30 · Rediseñar la gestión de activos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GBPSVp1txTh8sF2PtB3XvMR
---
## Summary
Segunda fase de activos: inversiones financieras (ETFs y fondos) como posiciones de la sociedad con sus compras, ventas, resultado y cotizaciones. Se separó de activos el 2026-11-12 para no retrasar los bienes amortizables.
Parte del diseño heredado: `sources/traspaso-2026-10-03/referencias/Activos/docs/MODELO_ACTIVOS.md` (instrumento, posición y operaciones, cotizaciones, asistente de compra y gráficos) y § 3.10 «Liquida ETFs» de `sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md`. Las tablas heredadas (ActivosInversiones, InstrumentosFinancieros, OperacionesInversion, CotizacionesInstrumentoFinanciero, ImportacionesActivos) son de este nodo. La migración del ERP no carga todavía los 18 activos de tipo 6.

## Decisions
- 2026-11-12 10:30 · Creado al sacar las inversiones financieras del alcance de activos (propietario 2026-11-11 18:20: «importamos todo excepto ETFs, que quedan en Creative Lab para una segunda fase»; ruta aceptada el 2026-11-12 10:19).

## Requirements
- 2026-11-11 18:20 · Q1. Ok, importamos todo excepto ETFs, que quedan en Creative Lab para una segunda fase
- 2026-11-12 10:30 · idea: Gestionar las inversiones financieras (ETFs y fondos) como posiciones con compras, ventas, resultado y cotizaciones, como segunda fase de activos.
