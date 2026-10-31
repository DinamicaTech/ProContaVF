---
title: Nóminas
status: draft
depends_on: [contabilidad/asientos, datos/esquema, datos/persistencia, contabilidad/plantillas-asiento]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Importación de nóminas directamente desde el fichero Excel, también recibido por correo en ProContaVF (detección de cabecera, mapeo y alias por sociedad, resolución de empleados) y su contabilización con el motor común de plantillas (contabilidad/plantillas-asiento) y el motor de asientos; reversión y reimportación controladas.
**Aceptación:** la de spec § 18.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: `frmNominas` («Resumen Nóminas») y su contabilización con plantilla y cuenta 465 por trabajador, en `sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md`, incluida su lista de defectos, que no se reproducen (plantilla, contabilizar y deshacer sin filtrar por sociedad, importación sin control de mes repetido ni de cuadre bruto − retenciones = neto, búsqueda del trabajador por nombre exacto).
- 2026-10-29 17:45 · La nueva versión no importa desde el portapapeles: lee directamente el fichero Excel de nóminas, también cuando llega por correo al buzón de ProContaVF, que lo pasa a este nodo para parsearlo, importarlo y generar los asientos (correo/recepcion).
- 2026-10-29 17:50 · La contabilización de nóminas usa el motor común contabilidad/plantillas-asiento, con plantillas por sociedad y tipo de paga, en lugar de un motor de plantillas propio.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 18 RF-NOM-01..05: importación, idempotencia, plantillas, reversión
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Nominas/docs/: interfaz anterior
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Implementar la importación y contabilización de nóminas.
- 2026-10-28 12:28 · question: P-05/A08 ¿libros de ejemplo y asientos esperados? (a) los de ProConta/Nominas/2025 y 2026 de tu disco ✅
- 2026-10-29 16:55 · frmNominas: Importa y contabiliza nóminas a partir del portapapeles. El código actual es bastante operativo.
- 2026-10-29 17:14 · frmNominas actualmente importa desde el portapapeles, pero la idea es que la nueva versión ya sea capaz de trabajar directamente con el fichero Excel. De hecho, la idea es enviar el fichero de nóminas al email de ProContaVF para que lo parsee, importe y genere asientos.
- 2026-10-29 17:45 · question: Los valores reales de la plantilla de contabilización (PlantillasApuntes: cuentas, importes, condiciones y lados) son datos de la base y no se han exportado: (a) leerlos de ProcontaEstructura.accdb cuando se active este nodo, sin subirlos al repositorio (recomendado) (b) los facilita el propietario
- 2026-10-29 17:50 · Derived from contabilidad/plantillas-asiento: definir las plantillas de contabilización de nóminas (hoy PlantillasApuntes) en el motor común de plantillas.
