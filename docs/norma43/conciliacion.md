---
title: Conciliación bancaria asistida
status: draft
depends_on: [norma43, contabilidad/asientos, terceros/roles]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Genera de forma bastante automatizada un asiento por cada movimiento bancario N43 (habitualmente de 572 contra 6/43/41) mediante el motor de asientos, con propuesta revisable. No es tesorería.
**Aceptación:** cada línea propone una contrapartida explicada; confirmar genera asientos cuadrados; ausencia de cuenta no se convierte en cuenta puente.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 13:14 · Q2) a) La conciliación asistida sirve para generar asientos contables (habitualmente de 572XXXX a 6XXXX/43XXXX/41XXXX) de cada línea de movimiento bancario N43 de forma bastante automatizada, no tiene relación con tesorería
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 19 RF-N43-03: no convertir ejemplos 555/SEPA en reglas de imputación
- 2026-10-28 12:28 · draft: Generar de forma asistida un asiento por cada línea N43 (572 contra 6/43/41) mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · question: ¿cómo se propone la contrapartida? (a) reglas por concepto/importe/tercero aprendidas de las imputaciones anteriores, siempre revisables ✅ (b) solo reglas fijas configuradas
