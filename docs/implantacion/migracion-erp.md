---
title: Migración del ERP
status: draft
depends_on: [terceros/tercero, facturacion/factura-venta, contabilidad/asientos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-05 11:24 · Construir facturas de compra | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEbtcX2Z7baM2yhwHY2wJXj
---
## Summary
Migración única desde ProcontaEstructura.accdb (antes deporwin.mdb) para sustituir la instalación actual, con inventario completo, mapas de identidades y conciliación; nada se reenvía a la AEAT ni se activa al cargar.
**Aceptación:** ensayo repetible en base nueva; original sin cambios; ninguna tabla omitida en silencio.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · La migración cubre todos los ejercicios con facturas emitidas. El ERP es un modelo continuo, sin ejercicios (a diferencia de ContaSol): el ejercicio de cada registro se deduce de su fecha (resuelve P-13).
- 2026-11-06 18:07 · Cuentas del ERP (F_MAE): CODMAE es el código, NOMMAE el nombre, NEXMAE pasa a CuentasContables.Descripcion y DEPMAE/SUBMAE (departamento y subdepartamento) a CuentasContables.IdCentroCostePredeterminado, con el centro de coste nuevo del subdepartamento (o del departamento si no hay subdepartamento).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 26.1 RF-ERP-01..05: migración única, coordinador, clasificación ContaSol, preservación
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Migracion/docs/: migrador anterior y tablas
- 2026-10-28 12:28 · draft: Implementar y ensayar la migración del ERP sobre copias, con conciliación.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-13 ¿cobertura temporal? (a) todos los ejercicios con facturas emitidas ✅ (b) solo el ejercicio en curso
- 2026-10-31 16:54 · 19. a. Aunque el ERP no tiene ejercicios (a diferencia de ContaSol), es un modelo continuo.
- 2026-10-31 16:54 · answer: P-13 cobertura temporal → (a) todos los ejercicios con facturas emitidas; el ERP no tiene ejercicios (a diferencia de ContaSol), es un modelo continuo
- 2026-11-06 13:36 · 2. Ok, el proveedor tendrá un TipoProducto por defecto. En la migración de datos, se podrá asignar este valor en función de la última factura de cada proveedor.
- 2026-11-06 13:42 · Derived from compras: rellenar CuentasProveedoresSociedad.TipoProductoDefecto con el TipoProducto de la última factura de compra de cada proveedor.
- 2026-11-06 18:07 · Derived from contabilidad/cuenta-contable: traer NEXMAE a CuentasContables.Descripcion y DEPMAE/SUBMAE a IdCentroCostePredeterminado.
- 2026-11-06 18:07 · Derived from compras/pantallas: traer PalabrasClaveTipoGasto y PalabrasClaveSubDepartamento a la tabla nueva de textos clave (bloque 2b).
- 2026-11-06 19:59 · Derived from compras/pantallas: PalabrasClaveTipoGasto y PalabrasClaveSubDepartamento van a TextosClaveCompra (versión 25), con la identidad contable de la cuenta y el centro de coste nuevo; un mismo texto en las dos tablas es una sola fila con los dos. Rellenar CuentasProveedoresSociedad.IdIdentidadGastoDefecto e IdCentroCosteDefecto con la cuenta de gasto y el centro de coste de la última factura de compra de cada proveedor.
