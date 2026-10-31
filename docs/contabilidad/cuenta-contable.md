---
title: Cuenta contable
status: draft
depends_on: [datos/esquema, datos/persistencia, organizacion/sociedades-ejercicios]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Ente CuentaContable: identidad permanente (IdentidadesContables) y cuenta por ejercicio (CuentasContables) con código, jerarquía, actividad, bloqueo e imputabilidad. Contrato `ICatalogoCuentas` (src/ProConta.Contabilidad/Cuentas): obtener, resolver por ejercicio y código exacto, listar, alta con reserva histórica de código y reconstrucción de la caché de saldos; `ReglasCuenta.AdmiteApuntes` dice si una cuenta puede recibir apuntes.
**Aceptación:** un código igual en ejercicios distintos no mezcla cuentas.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 19:05 · Se resuelve siempre por ejercicio y código exacto (índice único existente UX_Cuentas_EjercicioCodigo); una cuenta inexistente, inactiva, bloqueada o no imputable no recibe apuntes y el error dice por qué.
- 2026-10-31 19:05 · Alta: código solo con cifras (hasta 30) y nombre de hasta 150; crea en una transacción la identidad, la cuenta del ejercicio y la reserva del código en CodigosContablesReservados. Un código reservado por otra identidad de la sociedad no se reutiliza en otro ejercicio para una cuenta nueva: la misma cuenta se copiará entre ejercicios (pendiente). La cuenta padre debe ser del mismo ejercicio.
- 2026-10-31 19:05 · Caché de saldos: `ReconstruirSaldosAsync` recalcula SaldoActual (SaldoInicial + debe − haber) y NumeroApuntes de las cuentas del ejercicio a partir de sus apuntes, que guardan el código de la cuenta.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-CUE-01, 02: catálogo de cuentas e identidad
- 2026-10-28 12:28 · draft: Implementar el ente CuentaContable y publicar su contrato.
- 2026-10-31 17:53 · Derived from architecture: el contrato de cuentas por ejercicio que necesita el motor de asientos (resolver por código exacto, comprobar si admite apuntes), el alta con reserva de código y la reconstrucción de la caché de saldos.

## Hand-over
- 2026-10-31 19:05 · Hecho: contrato, alta y caché de saldos, con pruebas (mismo código en dos ejercicios, código reservado, padre de otro ejercicio, código no válido, saldo reconstruido igual a los apuntes). Pendiente: recodificación, sustitución y fusión con trazabilidad (faltan las tablas de fusiones, revisiones y auditoría de cuentas de los manifiestos antiguos), copia de cuentas entre ejercicios y aplicación de plan (la usará contabilidad/procesos al abrir ejercicio), saldos por periodo (0 a 14).
