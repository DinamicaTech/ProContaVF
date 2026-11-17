---
title: Activos financieros
depends_on: []
status: draft
threads:
  - 2026-11-12 10:30 · Rediseñar la gestión de activos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GBPSVp1txTh8sF2PtB3XvMR
  - 2026-11-17 17:50 · Gestionar los activos financieros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G1fjUTF52Bn5taQ2WjqJKWu
---
## Summary
Segunda fase de activos: los activos financieros de la sociedad (ETFs, fondos y participaciones) como posiciones con sus compras y ventas, que se ven en la misma pantalla de Activos con su cotización (de un proveedor externo), su rentabilidad acumulada y una proyección de revalorización explicada para quien no es experto.
Al vender, el resultado va a 766/666 y la posición queda a cero; los rendimientos llegan al Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades): retenciones de Renta 4 (193) y plusvalías y minusvalías (760, 766, 666).
Parte del diseño heredado: `sources/traspaso-2026-10-03/referencias/Activos/docs/MODELO_ACTIVOS.md` (instrumento, posición y operaciones, cotizaciones, asistente de compra y gráficos) y § 3.10 «Liquida ETFs» de `sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md`. Las tablas heredadas (ActivosInversiones, InstrumentosFinancieros, OperacionesInversion, CotizacionesInstrumentoFinanciero, ImportacionesActivos) son de este nodo; la migración del ERP no carga todavía los 18 activos de tipo 6.
Borrador sin programar: quedan abiertas la fuente de cotizaciones y el método de la proyección (Requirements).

## Decisions
- 2026-11-12 10:30 · Creado como idea creative-lab/inversiones-financieras al sacar las inversiones financieras del alcance de activos (propietario 2026-11-11 18:20: «importamos todo excepto ETFs, que quedan en Creative Lab para una segunda fase»; ruta aceptada el 2026-11-12 10:19).
- 2026-11-15 21:10 · Recibe de contabilidad (revisión de las incidencias con la gestoría) un criterio para cuando se debata la idea.
- 2026-11-17 17:50 · Leaves the lab: draft. La idea creative-lab/inversiones-financieras pasa a ser el borrador activos/activos-financieros, hijo de activos (propietario 2026-11-17 17:32 y 17:41): los activos financieros amplían la pantalla de Activos. El código aún nombra la ruta antigua (DuenosTablas en src/ProConta.Datos.Esquema/Diccionario, el diccionario generado y los comentarios de la migración del ERP en EjecucionErp.Activos.cs): la primera fase que se programe la cambia a `activos/activos-financieros` y regenera el diccionario.

## Requirements
- 2026-11-11 18:20 · Q1. Ok, importamos todo excepto ETFs, que quedan en Creative Lab para una segunda fase
- 2026-11-12 10:30 · idea: Gestionar las inversiones financieras (ETFs y fondos) como posiciones con compras, ventas, resultado y cotizaciones, como segunda fase de activos.
- 2026-11-15 21:10 · Derived from contabilidad: vender una posición deja a cero su saldo en la cuenta de la inversión y lleva la diferencia a resultados; la cartera se concilia con el intermediario.
- 2026-11-17 17:32 · De momento como nodo Draft.
- 2026-11-17 17:32 · En este nodo ampliaremos la pantalla de Activos para incluir activos financieros, pudiendo consultar su cotización en tiempo real, rentabilidad acumulada, proyección de revalorización, etc.
- 2026-11-17 17:50 · draft: Ampliar la pantalla de Activos con los activos financieros (ETFs, fondos y participaciones): posiciones con sus compras y ventas, cotización en tiempo real de un proveedor externo, rentabilidad acumulada y proyección de revalorización explicada para neófitos; diseño por fases y con el visto bueno del propietario antes de programar cada una.
- 2026-11-17 17:41 · Sí, hará falta un sistema externo de cotizaciones (investing ? Yahoo finances ?). Tenemos cuenta en Alpaca que tiene un API bastante potente, aunque no sé si indexa todas las ETFs .... si tu conoces otro proveedor de cotizaciones, bienvenido sea
- 2026-11-17 17:41 · Para la proyección había pensado utilizar un script de análisis: https://www.agensi.io/skills/technical-scanner, lo único que habría que 'digerir' un poco la información que ofrece para hacerla más clara para neófitos.
- 2026-11-17 17:50 · question: Proveedor de cotizaciones. (a) comprobar primero con las posiciones reales qué cubre Alpaca (solo cotiza valores de bolsas de EE. UU., así que los ETF UCITS europeos y los fondos probablemente no) y completar con un proveedor que cubra bolsas europeas y fondos por ISIN (EOD Historical Data, Twelve Data o Financial Modeling Prep, de pago con plan básico), con la configuración en Integraciones ✅ recomendada (b) solo Alpaca (c) Yahoo Finance o Investing sin API oficial (gratis, pero pueden dejar de funcionar sin aviso). Los fondos solo publican un valor liquidativo diario: se muestra con su fecha, nunca como cotización en tiempo real.
- 2026-11-17 17:50 · question: Proyección de revalorización. (a) revisar el script technical-scanner y usar sus indicadores como señal orientativa (tendencia, soportes), traducida a frases sencillas, y para la proyección en euros un rango sencillo con la rentabilidad histórica del instrumento (escenario prudente, medio y optimista), siempre rotulado como estimación ✅ recomendada (b) solo los indicadores del script, explicados (c) solo el rango con la rentabilidad histórica. El script es una dependencia externa: se decide con el propietario antes de añadirla.

## Acceptance criteria
- La pantalla de Activos lista los activos financieros con su posición (participaciones, coste, valor actual con fecha y origen de la cotización), rentabilidad acumulada y proyección, y su saldo contable cuadra con la cartera del intermediario.
