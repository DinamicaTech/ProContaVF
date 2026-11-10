---
title: Motor de informes
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Catálogo de informes, proveedores de datos y plantillas versionadas con FastReport en su edición de código abierto; salidas HTML, PDF y XLSX sin Excel COM; modelo estable para el PDF de factura. Hecho hasta ahora solo el PDF de factura: `src/ProConta.Informes` (`GeneradorPdfFastReport`, `DatosFacturaPdf`) y las plantillas `Plantillas/Factura.frx` y `FacturaProforma.frx`; el catálogo, las versiones de plantilla y las salidas HTML y XLSX siguen pendientes.
**Aceptación:** el informe usa la plantilla publicada exacta; la exportación conserva decimales y fechas.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · [replaced by architecture 2026-11-01 16:20] Motor de plantillas: la edición gratuita de código abierto de FastReport (FastReport.OpenSource, licencia MIT), compatible con la licencia MIT de ProContaVF (architecture 2026-10-31 16:54); no se usa la edición comercial.
- 2026-11-01 16:20 · Motor de plantillas: la edición gratuita de código abierto de FastReport (FastReport.OpenSource, licencia MIT), compatible con la licencia AGPL-3.0 de ProContaVF (architecture 2026-11-01 16:20); no se usa la edición comercial.
- 2026-10-31 17:09 · Con la edición de código abierto, las plantillas se diseñan con FastReport Designer Community Edition (freeware, ejecutable aparte) y la vista previa se hace en el navegador o en el visor independiente de FastReport. Si en la práctica se queda corta, el propietario puede comprar una licencia FastReport Desktop Standard; esa decisión se tomará al construir este nodo.
- 2026-11-05 11:20 · PDF de factura con FastReport.OpenSource y FastReport.OpenSource.Export.PdfSimple 2026.2.3 (MIT). Las plantillas se copian junto al programa y se editan con FastReport Designer sin recompilar; reciben un modelo de textos ya formateados (cabecera, líneas y resumen) y no leen Access ni calculan importes. Limitación de PdfSimple, comprobada: cada página es una imagen (unos 370 KB), así que el texto del PDF no se puede seleccionar ni buscar; el exportador con texto real es comercial.
- 2026-11-05 11:25 · Validado por el propietario el PDF de factura (PR #18). Es probable que se compre la licencia comercial de FastReport para generar PDF nativos, con texto real. Antes de comprarla hay que comprobar que es compatible con la licencia AGPL-3.0 de ProContaVF y con la biblioteca VeriFactu AGPL (architecture 2026-11-01 16:20). El nodo sigue en borrador: faltan el catálogo, las versiones de plantilla y las salidas HTML y XLSX.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 24 RF-REP-01..03: catálogo, plantillas, salidas
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Reporting/docs/REPORTING.md: motor anterior (FastReport en el disco del propietario)
- 2026-10-28 12:28 · draft: Implementar el motor de informes con plantillas versionadas.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿motor de plantillas? (a) FastReport, como el proyecto anterior ✅ (b) otro
- 2026-10-31 16:54 · 22. a) Basta la edición gratuita, la idea es que ProContaVF sea licencia MIT
- 2026-10-31 16:54 · answer: motor de plantillas → (a) FastReport; basta la edición gratuita, porque ProContaVF será licencia MIT
- 2026-10-31 17:09 · 22. El diseñador, efectivamente, es un exe aparte (FastReport Designer Community Edition (freeware)). Revisando funcionalidades (https://opensource.fast-report.com/p/the-feature-comparison-table-for.html), la versión Open Source permite preview en web browser y en viewer aplication (supongo que otro exe independiente -> https://www.fast-report.com/downloads/viewer). Según  lo que veamos, puedo acabar comprando una licencia FastReport Desktop Standard si nos facilita la vida
- 2026-11-01 16:20 · Derived from architecture: la dependencia FastReport.OpenSource debe ser compatible con la licencia AGPL-3.0 de ProContaVF.
- 2026-11-05 11:20 · Derived from facturacion/factura-venta: PDF de factura y proforma desde un modelo estable, sin QR mientras VeriFactu esté desactivado.
- 2026-11-05 11:19 · Bueno, es probable que acabemos con la licencia comercial de FastReport para generar PDFs nativos, no hay problema.
- 2026-11-05 11:19 · Todo ok.
