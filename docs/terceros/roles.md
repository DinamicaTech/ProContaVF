---
title: Roles del tercero
status: draft
depends_on: [terceros/tercero, contabilidad/cuenta-contable, organizacion/formas-pago]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Roles opcionales de una identidad (Cliente, Proveedor, Contacto…) con IDs funcionales propios y cuentas contables por rol y sociedad/ejercicio.
**Aceptación:** añadir un rol no duplica la identidad; las cuentas cambian con el contexto.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 18:14 · Al guardar o contabilizar una factura de compra, los valores por defecto del proveedor que se hayan cambiado en ella (tipo de operación, tipo de producto, retención, país, forma de pago y la cuenta de gasto si estaba vacía o era la genérica) se actualizan en su ficha sin preguntar, para no entorpecer las contabilizaciones masivas; cada cambio queda en el historial de la ficha.
- 2026-10-31 07:46 · La moneda de las facturas del proveedor es otro de sus valores por defecto: se propone en sus facturas y, si se cambia en una, se actualiza en su ficha como el resto (decisión de 2026-10-29 18:14).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-03, 04: roles idempotentes y cuentas por rol y sociedad/ejercicio
- 2026-10-28 12:28 · draft: Implementar los roles del tercero y sus cuentas por sociedad y ejercicio.
- 2026-10-28 12:28 · question: ¿Asociación y Particular son roles o tipos de persona? (a) tipos/clasificación: Particular es persona física y Asociación entidad sin personalidad, y los roles siguen siendo Cliente/Proveedor/Contacto ✅ (b) roles nuevos
- 2026-10-28 12:28 · question: P-04 ¿cómo se representa la pareja Proveedor/Acreedor? (a) dos cuentas opcionales del rol Proveedor ✅ (b) dos roles distintos
- 2026-10-29 18:14 · * Puede entorpecer exportaciones masivas de facturas. Si se ha modificado alguno de esos parámetros, asumimos que son los correctos y actualizamos, no tengo claro en qué circunstancia se pueden modificar sin quererlos actualizar en el proveedor.
- 2026-10-31 07:30 · Y en el caso de facturas de importación, se puede indicar la moneda y hago conversión a euros aplicando el tipo de cambio de la moneda en la fecha de la factura. En la contabilidad siempre se trabaja en euros.
