---
title: Mayor
status: draft
depends_on: [contabilidad/asientos]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Mayor «vitaminado» reutilizable desde contabilidad y desde la ficha de terceros: apertura, movimientos, saldo, filtros, navegación al asiento e impresión. Toma como base la pantalla Mayor de ProConta antiguo (revisión y cuadre de cuentas: mayor combinado de varias cuentas, vistas compactas por factura, punteo, asignación de número de factura a cobros y pagos, búsqueda del importe en Norma 43 y en asientos, asistente de asientos de corrección y marca de cuenta cuadrada), sin límite de filas ni cuadre contra el ERP.
**Aceptación:** el mayor embebido coincide con el completo bajo iguales filtros.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 10:20 · Referencia funcional: la pantalla Mayor de ProConta antiguo (`frmMayor`), descrita en `sources/proconta-antiguo/mayor/ANALISIS-FUNCIONAL.md`, incluida su lista de defectos, que no se reproducen.
- 2026-10-29 10:20 · Sin límite de filas: se muestran todos los movimientos y los totales siempre cuentan todos (el límite de 175 filas del antiguo venía de la rejilla de Access).
- 2026-10-29 10:20 · Sin cuadre contra DeporWin ni contra ningún ERP: ProContaVF sustituye a DeporWin, Proconta.accdb y ContaSol.
- 2026-10-29 10:20 · El asiento inverso solo se genera con el asistente de asientos de corrección, que invierte también el IVA; desaparece el «Asiento inverso» rápido.
- 2026-10-29 10:20 · Toda modificación de apuntes desde el Mayor (cuenta, contrapartida, concepto, número de factura, punteo) pasa por el motor de asientos, que guarda el registro de cambios (contabilidad/asientos).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-MAY-01: mayor reutilizable con servicios compartidos
- 2026-10-28 12:28 · From docs/README.md (descripción del propietario): mayor 'vitaminado'
- 2026-10-28 12:28 · draft: Implementar el mayor reutilizable.
- 2026-10-28 12:28 · [answered 2026-10-29 10:12] question: ¿qué hace «vitaminado» al mayor? (a) lo describes tú: punteo, notas, documentos vinculados, acciones desde la línea… (pendiente)
- 2026-10-29 10:12 · answer: ¿qué hace «vitaminado» al mayor? → lo que hace la pantalla Mayor de ProConta antiguo (sources/proconta-antiguo/mayor/ANALISIS-FUNCIONAL.md), con los cambios siguientes
- 2026-10-29 10:12 · El límite era por la capacidad de la grid de Access, aquí ya no tendría sentido aplicarlo
- 2026-10-29 10:12 · Ya no es necesario ningún cuadre con el ERP DeporWin, de hecho, ProContaVF sustituirá DeporWin, Proconta.accdb y ContaSol.
- 2026-10-29 10:12 · Ok al asiento inverso con asistente
- 2026-10-29 10:20 · Derived from plataforma/ui-base: crear y validar con el propietario el prototipo HTML del Mayor antes de construir su UI WPF.
