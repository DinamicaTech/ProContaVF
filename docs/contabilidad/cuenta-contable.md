---
title: Cuenta contable
depends_on: [datos/esquema, datos/persistencia, organizacion/sociedades-ejercicios]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-11-01 10:46 · Desarrollar la pantalla de Terceros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GVR2rh2hJF6h9BMYjKSu8jn
  - 2026-11-06 14:34 · Construir facturas de compra | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEbtcX2Z7baM2yhwHY2wJXj
---
## Summary
Ente CuentaContable: identidad permanente (IdentidadesContables) y cuenta por ejercicio (CuentasContables) con código, jerarquía, actividad, bloqueo e imputabilidad. Contrato `ICatalogoCuentas` (src/ProConta.Contabilidad/Cuentas): obtener, resolver por ejercicio y código exacto, listar, alta con reserva histórica de código y reconstrucción de la caché de saldos; `ReglasCuenta.AdmiteApuntes` dice si una cuenta puede recibir apuntes. Buscador de cuentas común a todas las pantallas (`BuscadorCuentas` y, en `src/ProConta.Contabilidad.UI`, `BuscadorCuentasVM` y `DialogoCuentas`) con alta de subcuenta.
**Aceptación:** un código igual en ejercicios distintos no mezcla cuentas.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 19:05 · Se resuelve siempre por ejercicio y código exacto (índice único existente UX_Cuentas_EjercicioCodigo); una cuenta inexistente, inactiva, bloqueada o no imputable no recibe apuntes y el error dice por qué.
- 2026-10-31 19:05 · Alta: código solo con cifras (hasta 30) y nombre de hasta 150; crea en una transacción la identidad, la cuenta del ejercicio y la reserva del código en CodigosContablesReservados. Un código reservado por otra identidad de la sociedad no se reutiliza en otro ejercicio para una cuenta nueva: la misma cuenta se copiará entre ejercicios (pendiente). La cuenta padre debe ser del mismo ejercicio.
- 2026-10-31 19:05 · Caché de saldos: `ReconstruirSaldosAsync` recalcula SaldoActual (SaldoInicial + debe − haber) y NumeroApuntes de las cuentas del ejercicio a partir de sus apuntes, que guardan el código de la cuenta.
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).
- 2026-11-01 10:46 · `ICatalogoCuentas` añade `CuentaDeIdentidadAsync` (cuenta de una identidad en un ejercicio), `UltimaCuentaDeIdentidadAsync` y `AltaEnEjercicioAsync`, que da de alta la cuenta de una identidad existente en otro ejercicio respetando la reserva de códigos: admite el código reservado para esa identidad o uno libre, nunca el de otra identidad de la sociedad.
- 2026-11-01 15:48 · Validado por el propietario (pantalla de Terceros y revisión 1).
- 2026-11-06 14:34 · Buscador de cuentas común (reglas en `BuscadorCuentas`, ventana en el proyecto nuevo `ProConta.Contabilidad.UI`, que cualquier pantalla puede usar): busca en las cuentas del ejercicio de trabajo que admiten apuntes (activas, no bloqueadas e imputables) por el principio del código, por código con punto de relleno («628.11» = 6280011, con la longitud de cada cuenta) o por cualquier parte del nombre sin distinguir mayúsculas ni acentos. Cada pantalla lo limita a sus grupos (prefijos de código) y una casilla «Solo el grupo …» quita el filtro. «Crear subcuenta…» va en dos pasos: primero la cuenta de tres cifras (propuesta: la de la cuenta seleccionada o la del texto escrito; tiene que ser del grupo si el filtro está puesto) y después el código, que propone el siguiente libre (editable), y el nombre; no se pide descripción porque CuentasContables no la tiene. El alta usa `AltaAsync` (cuelga de la cuenta de tres cifras si existe en el ejercicio) y la subcuenta creada queda elegida.
- 2026-11-06 14:34 · Siguiente código libre (`SiguienteSubcuentaAsync`): uno más que la subcuenta más alta de la cuenta, con la longitud más frecuente entre sus subcuentas (7 si no tiene ninguna), sin usar los códigos del ejercicio ni los reservados de la sociedad en CodigosContablesReservados; al final de la numeración, el primer hueco libre.
- 2026-11-06 14:47 · Tras la comprobación en el equipo: la cuenta de tres cifras que se propone al crear una subcuenta es la de la cuenta seleccionada solo si es de los grupos de la pantalla (con el filtro quitado, una 400 o una 572 no se propone para una cuenta de gasto); si no, la del texto escrito o ninguna. Esc cierra el buscador y el asistente sin cambios.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-CUE-01, 02: catálogo de cuentas e identidad
- 2026-10-28 12:28 · draft: Implementar el ente CuentaContable y publicar su contrato.
- 2026-10-31 17:53 · Derived from architecture: el contrato de cuentas por ejercicio que necesita el motor de asientos (resolver por código exacto, comprobar si admite apuntes), el alta con reserva de código y la reconstrucción de la caché de saldos.
- 2026-11-01 10:46 · Derived from terceros/roles: obtener la cuenta de una identidad en un ejercicio y crearla en un ejercicio en el que aún no existe.
- 2026-11-06 14:34 · Derived from compras/pantallas: buscador pop-up genérico de cuentas contables con filtro por grupo desactivable y alta de subcuenta (cuenta de tres cifras y siguiente código libre).

## Hand-over
- 2026-10-31 19:05 · Hecho: contrato, alta y caché de saldos, con pruebas (mismo código en dos ejercicios, código reservado, padre de otro ejercicio, código no válido, saldo reconstruido igual a los apuntes). Pendiente: recodificación, sustitución y fusión con trazabilidad (faltan las tablas de fusiones, revisiones y auditoría de cuentas de los manifiestos antiguos), copia de cuentas entre ejercicios y aplicación de plan (la usará contabilidad/procesos al abrir ejercicio), saldos por periodo (0 a 14).
- 2026-11-06 14:34 · Hecho: buscador de cuentas con alta de subcuenta y pruebas (reglas en ProConta.Contabilidad.Tests; modelo de vista en ProConta.Compras.UI.Tests). Primer uso: la cuenta de gasto de Facturas recibidas. Pendiente: usarlo en las demás pantallas que piden una cuenta.
