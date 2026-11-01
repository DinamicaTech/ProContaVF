---
title: Conceptos
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 16:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
---
## Summary
Catálogo de conceptos facturables por sociedad (tipos, precio, IVA, cuenta de venta como identidad contable, departamento y centro de coste) para preparar líneas; cambiar un concepto no reescribe líneas históricas. Código: `CatalogoConceptos` (src/ProConta.Facturacion/Conceptos) y su proveedor en Parámetros y tablas › Conceptos.
**Aceptación:** duplicar, consultar uso y eliminar según referencias.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · El catálogo de conceptos es por sociedad y continuo entre ejercicios (resuelve RF-CAT-02).
- 2026-11-01 17:15 · Migración `20261101_1630_CONCEPTOS_SOCIEDAD`: Conceptos.IdSociedad y Conceptos.IdIdentidadContable, que se rellenan desde el ejercicio y la cuenta de cada concepto; se conservan los IdConcepto. CuentaContable queda como dato heredado y se sigue rellenando cuando el código cabe en 7 caracteres.
- 2026-11-01 17:15 · Reglas: nombre único en la sociedad; la cuenta se muestra resuelta en el ejercicio de trabajo (o la última de su identidad, avisando de que no existe en este ejercicio) y, si no se cambia el código, se conserva la identidad; un concepto nuevo toma el siguiente IdConcepto libre. Duplicar crea «<nombre> (copia)» inactivo para revisarlo antes de usarlo. Ver uso cuenta las líneas de factura, de contrato y de facturas programadas y enseña las cinco últimas facturas; solo se elimina un concepto sin uso (si no, se desactiva).
- 2026-11-01 17:15 · Elegir un concepto en una línea copia su nombre, descripción, precio, IVA, cuenta, departamento y centro de coste; cambiar después el concepto no toca las líneas.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 9 RF-CAT-01, 02: catálogo de conceptos
- 2026-10-28 12:28 · draft: Implementar el catálogo de conceptos.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-CAT-02 ¿ámbito del catálogo? (a) por sociedad ✅ (b) global (c) por ejercicio
- 2026-10-31 16:54 · 12. a
- 2026-10-31 16:54 · answer: RF-CAT-02 ámbito del catálogo → (a) por sociedad
- 2026-11-01 16:18 · 3. Ok
- 2026-11-01 16:18 · answer: conceptos por sociedad → (a) migración: cada concepto pasa a su sociedad y la cuenta de venta se guarda como identidad contable; se conservan los IdConcepto y los repetidos en varios ejercicios se cuentan antes de migrar para decidir si se unen
- 2026-11-01 16:20 · Derived from plataforma/mantenimiento: proveedor de Conceptos y Tipos de concepto con Duplicar, Ver uso y baja según uso.

## Hand-over
- 2026-11-01 17:15 · Pendiente en el equipo del propietario: contar con `MigracionesFacturacion.ConceptosRepetidosAsync` los conceptos con el mismo nombre en una sociedad (venían de ejercicios distintos) para decidir si se unen.
