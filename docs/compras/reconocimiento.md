---
title: Reconocimiento de facturas de compra
status: draft
depends_on: [compras/factura-compra, terceros/tercero]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Motor que reconoce los campos de un PDF de compra (texto nativo y OCR, proveedor por NIF, número, fecha, importes, IVA, retención, divisas) con confianza por campo, sin escribir; conserva el entrenamiento hecho y su corpus de regresión.
**Aceptación:** la de spec § 14: escaneado pide OCR, varias cuotas cuadran, discrepancia pide revisión, el log explica cada decisión; el corpus no empeora respecto al motor actual.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional de la extracción actual: la pantalla «Facturas Proveedores» de ProConta antiguo (`frmEntradaFacturas`), en `sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md` § 2.3-2.7 (proveedor por NIF, extracción automática del PDF y botón «Auto», VIES, conversión de moneda, captura del portapapeles) y § 6-7 (reglas y defectos, que no se reproducen).

## Requirements
- 2026-10-28 12:28 · Quizá la parte más importante es el módulo de FacturasCompra ya que se ha hecho un entrenamiento algo laborioso para afinar el reconocimiento automático de los campos de facturas de compra (PDF).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-01..04, 06: reconocer y procesar, extracción, proveedor, importes, confianza y regresión
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/ESPECIFICACION_REESCRITURA_RECONOCIMIENTO_FACTURAS_COMPRA.md: fuente de detalle obligatoria
- 2026-10-28 12:28 · draft: Reescribir en C# el motor de reconocimiento de facturas de compra conservando el entrenamiento y el corpus de regresión 2025/2026.
- 2026-10-28 12:28 · question: ¿dónde está el corpus de PDF con resultados esperados (anexo A06)? (a) en una carpeta de tu disco que indicarás, fuera de Git por datos reales ✅
- 2026-10-28 12:28 · question: ¿cómo se conserva el entrenamiento hecho? (a) portar las reglas y umbrales del motor VB y validarlos con el corpus ✅ (b) reentrenar desde cero
- 2026-10-29 17:45 · Derived from compras/pantallas: extraer de cada PDF de la lista de facturas de compra los campos principales (número de factura, fecha, CIF del proveedor, importes) como propuesta corregible, al menos como lo hace hoy frmEntradaFacturas (sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md § 2).
