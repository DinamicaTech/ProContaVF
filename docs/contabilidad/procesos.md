---
title: Procesos contables
status: draft
depends_on: [contabilidad/asientos, organizacion/sociedades-ejercicios]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Analizar formularios del ProConta antiguo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G5wuUSovHLCVH22T7EntXcw
---
## Summary
Liquidación de IVA, prorrata (legal propuesta por defecto, la del gestor si se fija, con desglose en Excel), Impuesto sobre Sociedades, cierre completo (regularización, cierre y apertura) y reapertura de ejercicio, y los demás procesos del menú antiguo (amortizaciones aparte), siempre como propuesta revisada y confirmada en lotes propios regenerables.
**Aceptación:** ejecutar dos veces no duplica lote; reabrir conserva asientos ajenos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: los procesos que el menú principal de ProConta antiguo ejecuta directamente, en `sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md` § 3.3-3.9 (saldar cuentas de IVA, liquidación de IVA con prorrata, mata centimillos, facturas pendientes de recibir, reparto de dividendos, asiento de apertura y de cierre) y § 5-6. Se conservan todos, como propuesta revisada y confirmada, con permisos, sin poder duplicarse y con cuentas, porcentajes y umbrales parametrizados por sociedad y ejercicio; no se reproducen sus defectos (§ 6).
- 2026-10-29 17:50 · Cierre de ejercicio completo estándar: regularización de los grupos 6 y 7 contra la 129 (asiento 10000), cierre de los grupos 1-5 (asiento 20000) y apertura (asiento 1), cuadrados entre sí; no se reproduce el cierre antiguo.
- 2026-10-29 17:50 · Prorrata: la marca el gestor. El sistema propone por defecto la prorrata legal (operaciones con derecho a deducción / total de operaciones, sin IVA), el usuario puede fijar la del gestor y se puede generar un Excel con el desglose del cálculo para contrastarlo. La clasificación del IVA soportado (100 % deducible, con prorrata o no deducible) sale de un atributo del centro de coste o de la cuenta, no de su nombre, y el IVA intracomunitario autorepercutido también se deduce.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 16 RF-GES-01..05: procesos, propuestas, IVA, Sociedades, cierre y reapertura
- 2026-10-28 12:28 · draft: Implementar los procesos contables con propuesta y confirmación.
- 2026-10-29 17:45 · Derived from (root): conservar las funcionalidades de los procesos del menú principal antiguo (sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 3.3-3.9), formalizando la forma de trabajar.
- 2026-10-29 17:45 · [answered 2026-10-29 17:39] question: El cierre antiguo está roto (graba el cierre como asiento 1 y no existe la regularización de los grupos 6 y 7 contra la 129, § 6.1-6.2): (a) cierre completo estándar: regularización (10000), cierre (20000) y apertura (1) cuadrados (recomendado) (b) solo apertura con los saldos 1-5, como hoy
- 2026-10-29 17:45 · [answered 2026-10-29 17:39] question: Prorrata y liquidación de IVA (§ 6.6-6.7: la del 4.º trimestre no es la legal, la liquidación depende de los nombres de centro de coste «T-INNOVA», «Alquiler Locales» y «General» y el IVA intracomunitario no se deduce): (a) prorrata legal sin IVA y clasificación del IVA soportado por un atributo del centro de coste o de la cuenta, no por su nombre (recomendado) (b) replicar el cálculo actual
- 2026-10-29 17:39 · answer: ¿Cómo será el cierre? → cierre completo estándar (opción a)
- 2026-10-29 17:39 · answer: ¿Prorrata legal o replicar el cálculo actual? → la prorrata nos la marca el gestor; el sistema propone la legal por defecto, con un Excel del desglose
- 2026-10-29 17:39 · Bueno, la prorrata nos la marca el gestor, pero veo bien que el sistema proponga el valor de la prorrata legal por defecto y sería bueno poder generar un Excel con el desglose del cálculo de prorrata para validarlo en caso de no cuadrar con el que nos facilita el gestor, que al final, no dejan de ser humanos con sus errores y tal.
