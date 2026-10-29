---
title: Procesos contables
status: draft
depends_on: [contabilidad/asientos, organizacion/sociedades-ejercicios]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Analizar formularios del ProConta antiguo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G5wuUSovHLCVH22T7EntXcw
---
## Summary
Liquidación de IVA, prorrata, Impuesto sobre Sociedades, cierre y reapertura de ejercicio, siempre como propuesta revisada y confirmada en lotes propios regenerables.
**Aceptación:** ejecutar dos veces no duplica lote; reabrir conserva asientos ajenos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: los procesos que el menú principal de ProConta antiguo ejecuta directamente, en `sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md` § 3.3-3.9 (saldar cuentas de IVA, liquidación de IVA con prorrata, mata centimillos, facturas pendientes de recibir, reparto de dividendos, asiento de apertura y de cierre) y § 5-6. Se conservan todos, como propuesta revisada y confirmada, con permisos, sin poder duplicarse y con cuentas, porcentajes y umbrales parametrizados por sociedad y ejercicio; no se reproducen sus defectos (§ 6).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 16 RF-GES-01..05: procesos, propuestas, IVA, Sociedades, cierre y reapertura
- 2026-10-28 12:28 · draft: Implementar los procesos contables con propuesta y confirmación.
- 2026-10-29 17:45 · Derived from (root): conservar las funcionalidades de los procesos del menú principal antiguo (sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 3.3-3.9), formalizando la forma de trabajar.
- 2026-10-29 17:45 · question: El cierre antiguo está roto (graba el cierre como asiento 1 y no existe la regularización de los grupos 6 y 7 contra la 129, § 6.1-6.2): (a) cierre completo estándar: regularización (10000), cierre (20000) y apertura (1) cuadrados (recomendado) (b) solo apertura con los saldos 1-5, como hoy
- 2026-10-29 17:45 · question: Prorrata y liquidación de IVA (§ 6.6-6.7: la del 4.º trimestre no es la legal, la liquidación depende de los nombres de centro de coste «T-INNOVA», «Alquiler Locales» y «General» y el IVA intracomunitario no se deduce): (a) prorrata legal sin IVA y clasificación del IVA soportado por un atributo del centro de coste o de la cuenta, no por su nombre (recomendado) (b) replicar el cálculo actual
