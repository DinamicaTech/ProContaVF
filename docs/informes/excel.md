---
title: Informes en tablas dinámicas Excel
status: draft
depends_on: [informes/reporting, contabilidad/asientos, contabilidad/balances]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-11 15:00 · Generar balances en Excel | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRuPaDaEwyqkxbhZeBADiWX
---
## Summary
Informes que el usuario analiza en tablas dinámicas de Excel a partir de los datos contables y de facturación.
Hechos el balance de situación y la cuenta de pérdidas y ganancias (`src/ProConta.Informes/Excel/ExportadorBalanceExcel.cs`, con ClosedXML y sin automatizar Excel): hoja del informe con la tabla dinámica, hoja de datos con un apunte por fila y hoja SinClasificar. Pendientes: diario/mayor y ventas/compras por tercero y mes.
**Aceptación:** el xlsx abre en Excel con la tabla dinámica lista y cuadra con contabilidad.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Primeros informes Excel: diario/mayor y ventas/compras por tercero y mes.
- 2026-11-11 15:00 · Libro de los balances con el formato de BalancePyG2026_Nuevo.xlsx y BalanceSituacion2026_Nuevo.xlsx (contabilidad/balances 2026-10-31 16:54): hoja «PyG» o «Situacion» con la tabla dinámica (filas: Bloque, solo en situación, › Apartado › Titulo › Cuenta › SubCuenta con subtotales arriba; columnas: Mes; filtros: CentroDeCoste y NombreDepartamento; valor: Suma de Importe), hoja «DatosPyG» o «DatosSituacion» con la tabla TablaDatosBalance (las 19 columnas del libro anterior más Bloque) y hoja «SinClasificar» si hay cuentas sin regla. Se genera con ClosedXML 0.105 (MIT, ya en el proyecto por la exportación de tablas), sin Excel COM; la tabla dinámica se recalcula al abrir. Se guarda en Documentos\ProContaVF\Informes como Balance<Situacion|PyG>_<ejercicio>_<fecha-hora>.xlsx y se abre con Excel.

## Requirements
- 2026-10-28 12:28 · From docs/README.md (descripción del propietario): informes en tablas dinámicas Excel
- 2026-10-28 13:14 · Q6) a)
- 2026-10-28 12:28 · draft: Implementar los informes en tablas dinámicas Excel.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿qué informes primero? (a) diario/mayor y ventas/compras por tercero y mes ✅ (b) los describes tú
- 2026-10-31 16:54 · 20. a
- 2026-10-31 16:54 · answer: qué informes primero → (a) diario/mayor y ventas/compras por tercero y mes
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 4: listados de Mayor y Diario a Excel del menú antiguo.
- 2026-11-11 15:00 · Derived from contabilidad/balances: libros Excel con tabla dinámica del balance de situación y de la cuenta de pérdidas y ganancias, con el formato de los libros del ProConta anterior.
