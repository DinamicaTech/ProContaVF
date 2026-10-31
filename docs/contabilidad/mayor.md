---
title: Mayor
status: draft
depends_on: [contabilidad/asientos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
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
- 2026-10-31 08:43 · Sin ERP desaparece la ayuda «Pagos − Conta» del antiguo. «Muy compacto» y «Facturas pendientes» se unen en una sola vista «Solo pendientes» (por cuenta y factura, oculta las que netean a cero; F2024001 y 2024001 son la misma factura). Si el asiento tiene varias contrapartidas, la columna de destino dice «Varias» y su ayuda las lista todas.
- 2026-10-31 09:58 · Cuatro fechas distintas en el Mayor: «Fecha» es la fecha contable del asiento (asientos.fecha; decide periodo y trimestre de IVA); «Fecha doc.» es la de la factura (facturas.fecha); «Grabado el» es el día del alta del asiento y no cambia nunca (un asiento de diciembre puede grabarse en enero); «Modificado el» es el día del último cambio, con el detalle en el historial. Si un asiento se reescribe en otra fecha cambian Fecha y Modificado el, no Grabado el. Las tres últimas son columnas ocultas de fábrica.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-MAY-01: mayor reutilizable con servicios compartidos
- 2026-10-28 12:28 · From docs/README.md (descripción del propietario): mayor 'vitaminado'
- 2026-10-28 12:28 · draft: Implementar el mayor reutilizable.
- 2026-10-28 12:28 · [answered 2026-10-29 10:12] question: ¿qué hace «vitaminado» al mayor? (a) lo describes tú: punteo, notas, documentos vinculados, acciones desde la línea… (pendiente)
- 2026-10-29 10:12 · answer: ¿qué hace «vitaminado» al mayor? → lo que hace la pantalla Mayor de ProConta antiguo (sources/proconta-antiguo/mayor/ANALISIS-FUNCIONAL.md), con los cambios siguientes
- 2026-10-29 10:12 · El límite era por la capacidad de la grid de Access, aquí ya no tendría sentido aplicarlo
- 2026-10-29 10:12 · Ya no es necesario ningún cuadre con el ERP DeporWin, de hecho, ProContaVF sustituirá DeporWin, Proconta.accdb y ContaSol.
- 2026-10-29 10:12 · Ok al asiento inverso con asistente
- 2026-10-29 10:20 · Derived from plataforma/ui-base: crear y validar con el propietario el prototipo HTML del Mayor antes de construir su UI WPF (lo hace el borrador contabilidad/mayor/prototipo).
- 2026-10-31 08:43 · 1. Desaparece 2. Ok 3. Ok
- 2026-10-31 09:48 · Ok, tiene sentido, yo puedo grabar en enero un asiento de diciembre del año anterior
- 2026-10-31 11:20 · Derived from contabilidad/plantillas-asiento: el asistente de asientos de corrección del Mayor es el motor común de plantillas (sources/proconta-antiguo/asientos-tipo/ANALISIS-FUNCIONAL.md).
- 2026-10-31 11:20 · Derived from (root): conservar «Reinicia cuadre del Mayor» del menú antiguo (marcar como cuadradas las cuentas con saldo de ±0,05 € o menos y, si se pide, desmarcar antes las demás; sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 3.13), con el umbral parametrizado.
