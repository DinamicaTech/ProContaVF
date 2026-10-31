---
title: Reconocimiento de facturas de compra
status: draft
depends_on: [compras/factura-compra, terceros/tercero]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Motor que reconoce los campos de un PDF de compra (texto nativo y OCR, proveedor por NIF, número, fecha, importes, IVA, retención, divisas) con confianza por campo, sin escribir; detecta por checksum los PDF nuevos de `<ejercicio>\Proveedores\<carpeta del proveedor>`; conserva el entrenamiento hecho y su corpus de regresión.
**Aceptación:** la de spec § 14: escaneado pide OCR, varias cuotas cuadran, discrepancia pide revisión, el log explica cada decisión; el corpus no empeora respecto al motor actual.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional de la extracción actual: la pantalla «Facturas Proveedores» de ProConta antiguo (`frmEntradaFacturas`), en `sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md` § 2.3-2.7 (proveedor por NIF, extracción automática del PDF y botón «Auto», VIES, conversión de moneda, captura del portapapeles) y § 6-7 (reglas y defectos, que no se reproducen).
- 2026-10-31 16:54 · Los PDF de compras viven en `<raíz>\<ejercicio>\Proveedores\<carpeta del proveedor>` (hoy D:\SynologyBox\SynologyDrive\2026\Proveedores), fuera de Git; son el corpus de regresión (A06) y se leen solo en local. Se mantiene el escaneo de los PDF del ejercicio, que detecta los nuevos por checksum y los incorpora a Facturas de Compra; las facturas que llegan al correo de ProContaVF se guardan en esa misma estructura, creando la carpeta del proveedor (nombre comercial) si no existe.
- 2026-10-31 16:54 · El entrenamiento se conserva portando a C# las reglas y umbrales del motor VB y validándolos con el corpus; no se reentrena desde cero.

## Requirements
- 2026-10-28 12:28 · Quizá la parte más importante es el módulo de FacturasCompra ya que se ha hecho un entrenamiento algo laborioso para afinar el reconocimiento automático de los campos de facturas de compra (PDF).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-01..04, 06: reconocer y procesar, extracción, proveedor, importes, confianza y regresión
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Facturacion/docs/ESPECIFICACION_REESCRITURA_RECONOCIMIENTO_FACTURAS_COMPRA.md: fuente de detalle obligatoria
- 2026-10-28 12:28 · draft: Reescribir en C# el motor de reconocimiento de facturas de compra conservando el entrenamiento y el corpus de regresión 2025/2026.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿dónde está el corpus de PDF con resultados esperados (anexo A06)? (a) en una carpeta de tu disco que indicarás, fuera de Git por datos reales ✅
- 2026-10-31 16:54 · 2. Actualmente, los PDFs de las facturas de compra se guardan en una carpeta '[EJERCICIO]\Proveedores\[PROVEEDOR]'. El ejercicio 2026, por ejemplo, está en D:\SynologyBox\SynologyDrive\2026\Proveedores. Tienes acceso libre (lectura) a esta ruta y ejercicios anteriores (D:\SynologyBox\SynologyDrive\2025\Proveedores....) ya que nos servirá para poder hacer pruebas de reconocimiento de facturas.
- 2026-10-31 16:54 · El sistema actual hace un scan de todos los PDF de su ejercicio y busca nuevos PDF (por checksum) para poderlos incorporar a la tabla de Facturas de Compra. Mantendremos este mecanismo y añadiremos la recepción de facturas por el email de ProContaVF, que guardará una copia en esta misma estructura, creando la carpeta del proveedor (nombre comercial) si no existiese.
- 2026-10-31 16:54 · La entidad Proveedor necesitará un nombre de carpeta donde estarán sus PDFs.
- 2026-10-31 16:54 · answer: corpus de PDF (A06) → los PDF reales de compras de cada ejercicio, en D:\SynologyBox\SynologyDrive\<ejercicio>\Proveedores\<proveedor> (2026, 2025 y anteriores), fuera de Git, con acceso de lectura para las pruebas de reconocimiento
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿cómo se conserva el entrenamiento hecho? (a) portar las reglas y umbrales del motor VB y validarlos con el corpus ✅ (b) reentrenar desde cero
- 2026-10-31 16:54 · 3. a
- 2026-10-31 16:54 · answer: conservar el entrenamiento → (a) portar las reglas y umbrales del motor VB y validarlos con el corpus
- 2026-10-29 17:45 · Derived from compras/pantallas: extraer de cada PDF de la lista de facturas de compra los campos principales (número de factura, fecha, CIF del proveedor, importes) como propuesta corregible, al menos como lo hace hoy frmEntradaFacturas (sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md § 2).
