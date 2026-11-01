---
title: Factura de venta
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero, facturacion/conceptos, facturacion/verifactu]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 16:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
---
## Summary
Ente FacturaVenta: borrador sin número, cálculo de líneas y totales, validación, numeración transaccional, instantánea de emisor y destinatario, estados, inmutabilidad tras emitir, duplicado y rectificativa 1:1. Código: `src/ProConta.Facturacion/Facturas` (`FacturaVenta`, `CalculoFactura`, `ReglasFactura`, `ServicioFacturas`, `ServicioSeries`, `ConsultaFacturas`, `PasoFiscal`), con pruebas en `tests/ProConta.Facturacion.Tests`.
**Aceptación:** la de spec § 10.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Se ratifica la rectificativa completa 1:1 y el retorno a borrador tras consultar la AEAT, como dice RF-FAC-09 (resuelve P-05).
- 2026-11-01 17:15 · Estados (Facturas.EstadoFactura): 3 Borrador, 0 Pendiente de validar, 1 Validada, 4 Rectificación pendiente, 2 Rectificada. Un borrador se guarda sin número y puede estar incompleto; se identifica como «Borrador B-0007» con su IdFactura.
- 2026-11-01 17:15 · Validar: primero se comprueba todo y se devuelven todos los errores y avisos a la vez (cliente activo con NIF y nombre, descripción, líneas con nombre, porcentajes entre 0 y 100, cuenta de ingreso en las líneas no suplidas, periodo, serie del ejercicio y no agotada, fecha del ejercicio y no anterior a la última de la serie, y todas las cuentas del asiento). El total cero es un aviso que se confirma. Después, en una transacción: se reserva el número con concurrencia optimista (hasta 5 reintentos), se compone el número, se congelan la instantánea del cliente (domicilio fiscal si lo tiene, si no el principal) y la del emisor del ejercicio, pasa por el paso fiscal y, si es rectificativa, la original pasa a Rectificada. El asiento se genera después, fuera de esa transacción: si falla, la factura queda validada sin asiento y se puede contabilizar otra vez.
- 2026-11-01 17:15 · Número: prefijo de la serie y correlativo; las cifras continúan el formato del último número emitido de la serie (cuatro si no hay ninguno). Pendiente de comprobar con los datos reales en el equipo del propietario.
- 2026-11-01 17:15 · Paso fiscal intercambiable (`IPasoFiscal`): el provisional «sin VeriFactu» deja la factura Validada y la marca con Facturas.SinVeriFactu; solo vale para la BD de pruebas. VeriFactu sustituirá ese paso.
- 2026-11-01 17:15 · Concurrencia optimista con la columna Facturas.Version: guardar, eliminar, validar y guardar notas comprueban la versión leída; si otro operador cambió la factura, el error lo dice y pide volver a abrirla.
- 2026-11-01 17:15 · Rectificativa 1:1: al crearla, la original pasa a «Rectificación pendiente» en la misma transacción; el borrador va a la serie de rectificativas del ejercicio (una sola por ejercicio), tipo R5 si la original es F2 y R4 en el resto, TipoRectificativa «I», y lleva el cliente y las líneas de la original con cantidades e importes en negativo, que no se pueden cambiar (sí la descripción, las fechas y las notas). Eliminar el borrador devuelve la original a «Validada»; validarlo la pasa a «Rectificada».
- 2026-11-01 17:15 · Duplicar crea un borrador con el cliente, la descripción y las líneas, sin número ni fecha, en la serie normal predeterminada del ejercicio de trabajo.
- 2026-11-01 17:15 · Series (RF-MAN-03, servicio `ServicioSeries`): prefijo único en el ejercicio, una sola serie de rectificativas por ejercicio, que no puede ser la predeterminada; marcar una como predeterminada quita la marca a las demás y lo avisa; con facturas no cambia de prefijo ni de clase y su rango debe incluir el último número; solo se borra si no tiene ninguna factura ni borrador.
- 2026-11-01 17:15 · Permisos: FACTURAS.EDITAR (guardar, duplicar, eliminar borradores, notas y cuentas del cliente), FACTURAS.VALIDAR (validar y contabilizar) y FACTURAS.RECTIFICAR, comprobados en el servicio con la regla común de seguridad/permisos (una acción sin asignaciones la puede cualquier operador).
- 2026-11-01 17:15 · Migración `20261101_1631_FACTURAS_INSTANTANEA`: domicilio y email del cliente congelados en Facturas, SinVeriFactu, Version y LineasFactura.CuentaContable a 30 caracteres.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-01..09: borrador, cálculo, suplidos, validaciones, instantánea, estados, inmutabilidad, duplicar, rectificar
- 2026-10-28 12:28 · draft: Implementar el ente FacturaVenta con su cálculo, numeración, emisión y rectificación.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-05 ¿se ratifica la rectificativa completa 1:1 y el retorno a borrador tras consultar la AEAT? (a) sí, como dice la spec ✅ (b) revisar antes
- 2026-10-31 16:54 · 15. a
- 2026-10-31 16:54 · answer: P-05 rectificativa 1:1 y retorno a borrador → (a) sí, como dice la spec
- 2026-11-01 15:52 · 1. Conceptos, Factura Venta, etc.
- 2026-11-01 15:53 · Lanzamos 1 a 3 y así evitamos posibles colisiones.
- 2026-11-01 16:18 · 1. Ok
- 2026-11-01 16:18 · answer: validar sin VeriFactu → (a) validar numera, congela la instantánea, contabiliza y deja la factura Validada mediante un paso fiscal intercambiable, provisional «sin VeriFactu» para la BD de pruebas
- 2026-11-01 16:18 · 6. Una entrega final
- 2026-11-01 16:18 · answer: entregas → una sola entrega final con mantenimiento, conceptos, factura de venta, contabilización y pantalla de Facturas emitidas
- 2026-11-01 16:20 · Derived from facturacion/pantallas: estados, acciones y consultas ligeras del listado de Facturas emitidas.

## Hand-over
- 2026-11-01 17:15 · Hecho: el ente, su servicio, las series y la consulta del listado, con pruebas sin BD (cálculo, reglas y asiento) y contra Access (validar numera y contabiliza, total cero, retención sin cuenta, rectificativa, duplicar, numeración concurrente y conflicto de versión). Pendiente: ejecutar las pruebas Access y las migraciones en el equipo del propietario; comprobar el formato del número con las series reales; VeriFactu, PDF, correo y programadas quedan fuera de este desarrollo.
