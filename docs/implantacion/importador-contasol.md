---
title: Importador de ContaSol
status: draft
depends_on: [contabilidad/asientos, terceros/roles]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-10 18:30 · Construir el importador de ContaSol | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPr8kimJRCay1zwqBem5TDJ
---
## Summary
Importación reutilizable de ContaSol por ejercicio para nuevos usuarios: inspección y simulación previas, orden por fases, reanudación y conciliación.
**Aceptación:** la de spec § 26.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

- 2026-11-10 13:06 · Se construye antes que la migración del ERP, que usa sus datos (cuentas F_MAE, centros F_CDP/F_DEP, asientos F_APU, libro F_IVS) y los enlaza por MapasIdentidadContaSol; ContaSol no usa nada del ERP.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 26.2 RF-CS-01..05: origen intacto, orden, apuntes, reanudación, conciliación
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/ContaSol/docs/PROTOCOLO_IMPORTACION_CONTASOL.md: protocolo anterior
- 2026-10-28 12:28 · draft: Implementar el importador de ContaSol.
- 2026-11-10 13:06 · Derived from implantacion/migracion-erp: construir el importador de ContaSol antes de retomar la migración del ERP, y publicar los mapas de identidad de cuentas, centros de coste, asientos y registros del libro de IVA que la migración del ERP necesita para enlazar sus datos.
- 2026-11-10 13:06 · C.  Sí, vamos a por importador de ContaSol y luego reactivamos este
- 2026-11-10 17:21 · En C:\docs\codigo\ProContaVF\ProConta\ContaSol están las bases de datos de contasol desde el ejercicio 2018 hasta el 2026.
- 2026-11-10 17:21 · Solo migramos las tablas de ContaSol que aparecen en ProContaEstructura.accdb (las que hay en ProContaEstructura son del 2026)
- 2026-11-10 17:21 · El principal reto que nos encontramos es importar diversos ejercicios y bases de datos en una única BD de ProContaVF.
- 2026-11-10 17:44 · 1. De la BD de Contasol (0022026.accdb). Te la acabo de actualizar.
- 2026-11-10 17:44 · 2. Dame la lista de clientes/proveedores con el ejercicio en el que cambian de NIF y lo reviso primero.
- 2026-11-10 17:44 · 3. F_BAN no se importa. Ya tenemos la información en F_MAE
- 2026-11-10 17:44 · 4. A
- 2026-11-10 17:44 · 6. A
- 2026-11-10 17:53 · Realmente no me interesa trasladar a terceros F_CLI ni F_PRO sin actividad los últimos 5 años.
- 2026-11-10 18:06 · 1- Ok
- 2026-11-10 18:06 · 2- Ok
- 2026-11-10 18:06 · 3- Trátalos como titulares distintos
